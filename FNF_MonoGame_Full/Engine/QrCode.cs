using System;
using System.Collections.Generic;
using System.Text;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Minimal QR Code encoder (ISO 18004; byte mode, error correction M, versions 1-20, mask chosen by the standard penalty
/// score). Used to show external download links on Xbox / Android where no system browser can be opened for the user.
/// Algorithm follows Project Nayuki's reference implementation (MIT). Modules[y, x] = true for dark.
/// </summary>
public sealed class QrCode
{
    public readonly int Size;
    public readonly bool[,] Modules;
    private readonly bool[,] _func;

    // ECC level M, index = version (0 unused)
    private static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26 };
    private static readonly int[] NumBlocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16 };
    public const int MaxVersion = 20;

    private QrCode(int ver)
    {
        Size = ver * 4 + 17;
        Modules = new bool[Size, Size];
        _func = new bool[Size, Size];
    }

    /// <summary>Encodes UTF-8 text; null when it does not fit in version 20-M (~ 666 bytes).</summary>
    public static QrCode Encode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text ?? "");
        for (int ver = 1; ver <= MaxVersion; ver++)
        {
            int cap = DataCodewords(ver) * 8;
            int ccBits = ver <= 9 ? 8 : 16;
            int need = 4 + ccBits + data.Length * 8;
            if (need > cap) continue;
            var bits = new List<bool>();
            Append(bits, 4, 4); Append(bits, data.Length, ccBits);
            foreach (byte b in data) Append(bits, b, 8);
            Append(bits, 0, Math.Min(4, cap - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var cw = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            var q = new QrCode(ver);
            q.DrawFunctionPatterns(ver);
            q.DrawCodewords(AddEcc(cw, ver));
            // pick the mask with the lowest penalty
            int best = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                q.ApplyMask(m); q.DrawFormat(m);
                int s = q.Penalty();
                if (s < bestScore) { bestScore = s; best = m; }
                q.ApplyMask(m);   // xor undo
            }
            q.ApplyMask(best); q.DrawFormat(best);
            return q;
        }
        return null;
    }

    private static void Append(List<bool> bits, int val, int len) { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }

    private static int RawDataModules(int ver)
    {
        int r = (16 * ver + 128) * ver + 64;
        if (ver >= 2) { int na = ver / 7 + 2; r -= (25 * na - 10) * na - 55; if (ver >= 7) r -= 36; }
        return r;
    }
    private static int DataCodewords(int ver) => RawDataModules(ver) / 8 - EccPerBlock[ver] * NumBlocks[ver];

    private static byte[] AddEcc(byte[] data, int ver)
    {
        int nb = NumBlocks[ver], ecc = EccPerBlock[ver], raw = RawDataModules(ver) / 8;
        int numShort = nb - raw % nb, shortLen = raw / nb;
        var div = RsDivisor(ecc);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < nb; i++)
        {
            int dl = shortLen - ecc + (i < numShort ? 0 : 1);
            var dat = new byte[dl]; System.Array.Copy(data, k, dat, 0, dl); k += dl;
            var rem = RsRemainder(dat, div);
            var blk = new byte[shortLen + 1];
            System.Array.Copy(dat, 0, blk, 0, dl);
            // short blocks get a dummy byte at position shortLen-ecc (skipped when interleaving)
            System.Array.Copy(rem, 0, blk, i < numShort ? dl + 1 : dl, ecc);
            blocks.Add(blk);
        }
        var res = new List<byte>(raw);
        for (int i = 0; i < shortLen + 1; i++)
            for (int j = 0; j < nb; j++)
                if (i != shortLen - ecc || j >= numShort) res.Add(blocks[j][i]);
        return res.ToArray();
    }

    private static int GfMul(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
        return z & 0xFF;
    }
    private static byte[] RsDivisor(int degree)
    {
        var r = new byte[degree]; r[degree - 1] = 1; int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++) { r[j] = (byte)GfMul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
            root = GfMul(root, 0x02);
        }
        return r;
    }
    private static byte[] RsRemainder(byte[] data, byte[] div)
    {
        var r = new byte[div.Length];
        foreach (byte b in data)
        {
            int f = b ^ r[0];
            System.Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= (byte)GfMul(div[i], f);
        }
        return r;
    }

    private void Set(int x, int y, bool dark) { Modules[y, x] = dark; _func[y, x] = true; }

    private void DrawFunctionPatterns(int ver)
    {
        for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
        Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
        var pos = AlignPos(ver); int n = pos.Length;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)) continue;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) Set(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormat(0);
        if (ver >= 7)
        {
            int rem = ver;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = ver << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0; int a = Size - 11 + i % 3, b = i / 3;
                Set(a, b, bit); Set(b, a, bit);
            }
        }
    }

    private void Finder(int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (x >= 0 && x < Size && y >= 0 && y < Size) Set(x, y, d != 2 && d != 4);
            }
    }

    private int[] AlignPos(int ver)
    {
        if (ver == 1) return new int[0];
        int na = ver / 7 + 2;
        int step = (ver * 4 + na * 2 + 1) / (na * 2 - 2) * 2;
        var r = new int[na]; r[0] = 6;
        for (int i = na - 1, p = Size - 7; i >= 1; i--, p -= step) r[i] = p;
        return r;
    }

    private void DrawFormat(int mask)
    {
        int data = 0 << 3 | mask;   // ECC M = 0b00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = (data << 10 | rem) ^ 0x5412;
        bool B(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(8, i, B(i));
        Set(8, 7, B(6)); Set(8, 8, B(7)); Set(7, 8, B(8));
        for (int i = 9; i < 15; i++) Set(14 - i, 8, B(i));
        for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, B(i));
        for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, B(i));
        Set(8, Size - 8, true);
    }

    private void DrawCodewords(byte[] data)
    {
        int i = 0, total = data.Length * 8;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool up = ((right + 1) & 2) == 0;
                    int y = up ? Size - 1 - vert : vert;
                    if (!_func[y, x] && i < total) { Modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0; i++; }
                }
        }
    }

    private void ApplyMask(int m)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (_func[y, x]) continue;
                bool inv = m switch
                {
                    0 => (x + y) % 2 == 0, 1 => y % 2 == 0, 2 => x % 3 == 0, 3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0, 5 => x * y % 2 + x * y % 3 == 0, 6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (inv) Modules[y, x] = !Modules[y, x];
            }
    }

    private int Penalty()
    {
        int p = 0, dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < Size; a++)
            {
                int run = 0; bool prev = false;
                for (int b = 0; b < Size; b++)
                {
                    bool v = pass == 0 ? Modules[a, b] : Modules[b, a];
                    if (b > 0 && v == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                    else run = 1;
                    prev = v;
                    // finder-like 1:1:3:1:1 with 4 light on a side (simplified check)
                    if (b >= 10)
                    {
                        bool F(int k) => pass == 0 ? Modules[a, b - k] : Modules[b - k, a];
                        bool core = F(4) && !F(5) && F(6) && F(7) && F(8) && !F(9) && F(10);
                        bool core2 = F(0) && !F(1) && F(2) && F(3) && F(4) && !F(5) && F(6);
                        if (core && !F(0) && !F(1) && !F(2) && !F(3)) p += 40;
                        if (core2 && !F(7) && !F(8) && !F(9) && !F(10)) p += 40;
                    }
                }
            }
        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
            {
                bool c = Modules[y, x];
                if (c == Modules[y, x + 1] && c == Modules[y + 1, x] && c == Modules[y + 1, x + 1]) p += 3;
            }
        foreach (bool v in Modules) if (v) dark++;
        int total = Size * Size;
        int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        p += Math.Max(0, k) * 10;
        return p;
    }
}
