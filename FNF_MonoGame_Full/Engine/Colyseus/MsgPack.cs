using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FNF_MonoGame.Engine.Colyseus;

// Minimal MessagePack encoder/decoder (spec: https://github.com/msgpack/msgpack/blob/master/spec.md), enough for
// Colyseus room messages: nil, bool, ints, floats, str, bin, array, map (+ fixext skipped). Values decode to
// null / bool / long / double / string / byte[] / List<object> / Dictionary<string, object>.
// No reflection, so it is .NET Native (UWP) safe.
public static class MsgPack
{
    // ------------------------------------------------------------------ encode
    public static void Write(Stream s, object v)
    {
        switch (v)
        {
            case null: s.WriteByte(0xc0); return;
            case bool b: s.WriteByte(b ? (byte)0xc3 : (byte)0xc2); return;
            case string str: WriteString(s, str); return;
            case byte[] bin: WriteHeader(s, bin.Length, 0xc4, 0xc5, 0xc6, -1, -1); s.Write(bin, 0, bin.Length); return;
            case int i: WriteInt(s, i); return;
            case long l: WriteInt(s, l); return;
            case short sh: WriteInt(s, sh); return;
            case byte by: WriteInt(s, by); return;
            case uint ui: WriteInt(s, ui); return;
            case float f: WriteDouble(s, f); return;
            case double d: WriteDouble(s, d); return;
            case System.Collections.IDictionary dict:
                WriteHeader(s, dict.Count, 0xde, 0xde, 0xdf, 0x80, 15);
                foreach (System.Collections.DictionaryEntry e in dict) { Write(s, e.Key?.ToString()); Write(s, e.Value); }
                return;
            case System.Collections.IList list:
                WriteHeader(s, list.Count, 0xdc, 0xdc, 0xdd, 0x90, 15);
                foreach (var x in list) Write(s, x);
                return;
            default: WriteString(s, v.ToString()); return;
        }
    }

    public static byte[] Encode(object v) { var ms = new MemoryStream(); Write(ms, v); return ms.ToArray(); }

    private static void WriteDouble(Stream s, double d)
    {
        // whole numbers are sent as ints (like msgpackr does for JS numbers)
        if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && Math.Abs(d) < 4e15) { WriteInt(s, (long)d); return; }
        s.WriteByte(0xcb);
        long bits = BitConverter.DoubleToInt64Bits(d);
        for (int i = 7; i >= 0; i--) s.WriteByte((byte)(bits >> (i * 8)));
    }

    private static void WriteInt(Stream s, long v)
    {
        if (v >= 0)
        {
            if (v < 128) s.WriteByte((byte)v);
            else if (v <= 0xff) { s.WriteByte(0xcc); s.WriteByte((byte)v); }
            else if (v <= 0xffff) { s.WriteByte(0xcd); WriteBE(s, v, 2); }
            else if (v <= 0xffffffffL) { s.WriteByte(0xce); WriteBE(s, v, 4); }
            else { s.WriteByte(0xcf); WriteBE(s, v, 8); }
        }
        else
        {
            if (v >= -32) s.WriteByte((byte)(sbyte)v);
            else if (v >= sbyte.MinValue) { s.WriteByte(0xd0); s.WriteByte((byte)(sbyte)v); }
            else if (v >= short.MinValue) { s.WriteByte(0xd1); WriteBE(s, v, 2); }
            else if (v >= int.MinValue) { s.WriteByte(0xd2); WriteBE(s, v, 4); }
            else { s.WriteByte(0xd3); WriteBE(s, v, 8); }
        }
    }

    private static void WriteBE(Stream s, long v, int n) { for (int i = n - 1; i >= 0; i--) s.WriteByte((byte)(v >> (i * 8))); }

    public static void WriteString(Stream s, string str)
    {
        var b = Encoding.UTF8.GetBytes(str);
        if (b.Length < 32) s.WriteByte((byte)(0xa0 | b.Length));
        else WriteHeader(s, b.Length, 0xd9, 0xda, 0xdb, -1, -1);
        s.Write(b, 0, b.Length);
    }

    private static void WriteHeader(Stream s, int n, int c8, int c16, int c32, int fix, int fixMax)
    {
        if (fix >= 0 && n <= fixMax) { s.WriteByte((byte)(fix | n)); return; }
        if (n <= 0xff && c8 != c16) { s.WriteByte((byte)c8); s.WriteByte((byte)n); }
        else if (n <= 0xffff) { s.WriteByte((byte)c16); WriteBE(s, n, 2); }
        else { s.WriteByte((byte)c32); WriteBE(s, n, 4); }
    }

    // ------------------------------------------------------------------ decode
    public static object Decode(byte[] b, ref int o)
    {
        byte p = b[o++];
        if (p <= 0x7f) return (long)p;
        if (p >= 0xe0) return (long)(sbyte)p;
        if ((p & 0xf0) == 0x80) return ReadMap(b, ref o, p & 0x0f);
        if ((p & 0xf0) == 0x90) return ReadArray(b, ref o, p & 0x0f);
        if ((p & 0xe0) == 0xa0) return ReadStr(b, ref o, p & 0x1f);
        switch (p)
        {
            case 0xc0: return null;
            case 0xc2: return false;
            case 0xc3: return true;
            case 0xc4: return ReadBin(b, ref o, (int)BE(b, ref o, 1));
            case 0xc5: return ReadBin(b, ref o, (int)BE(b, ref o, 2));
            case 0xc6: return ReadBin(b, ref o, (int)BE(b, ref o, 4));
            case 0xc7: { int n = (int)BE(b, ref o, 1); o += 1 + n; return null; }  // ext8
            case 0xc8: { int n = (int)BE(b, ref o, 2); o += 1 + n; return null; }
            case 0xc9: { int n = (int)BE(b, ref o, 4); o += 1 + n; return null; }
            case 0xca: { int bits = (int)BE(b, ref o, 4); return (double)BitConverter.ToSingle(BitConverter.GetBytes(bits), 0); }
            case 0xcb: return BitConverter.Int64BitsToDouble(BE(b, ref o, 8));
            case 0xcc: return BE(b, ref o, 1);
            case 0xcd: return BE(b, ref o, 2);
            case 0xce: return BE(b, ref o, 4);
            case 0xcf: return BE(b, ref o, 8);
            case 0xd0: return (long)(sbyte)BE(b, ref o, 1);
            case 0xd1: return (long)(short)BE(b, ref o, 2);
            case 0xd2: return (long)(int)BE(b, ref o, 4);
            case 0xd3: return BE(b, ref o, 8);
            case 0xd4: o += 2; return null;   // fixext 1..16 (unused by Colyseus with useRecords:false)
            case 0xd5: o += 3; return null;
            case 0xd6: o += 5; return null;
            case 0xd7: o += 9; return null;
            case 0xd8: o += 17; return null;
            case 0xd9: return ReadStr(b, ref o, (int)BE(b, ref o, 1));
            case 0xda: return ReadStr(b, ref o, (int)BE(b, ref o, 2));
            case 0xdb: return ReadStr(b, ref o, (int)BE(b, ref o, 4));
            case 0xdc: return ReadArray(b, ref o, (int)BE(b, ref o, 2));
            case 0xdd: return ReadArray(b, ref o, (int)BE(b, ref o, 4));
            case 0xde: return ReadMap(b, ref o, (int)BE(b, ref o, 2));
            case 0xdf: return ReadMap(b, ref o, (int)BE(b, ref o, 4));
        }
        throw new FormatException("msgpack: bad prefix 0x" + p.ToString("x2"));
    }

    private static long BE(byte[] b, ref int o, int n) { long v = 0; for (int i = 0; i < n; i++) v = (v << 8) | b[o++]; return v; }
    private static string ReadStr(byte[] b, ref int o, int n) { var s = Encoding.UTF8.GetString(b, o, n); o += n; return s; }
    private static byte[] ReadBin(byte[] b, ref int o, int n) { var r = new byte[n]; System.Array.Copy(b, o, r, 0, n); o += n; return r; }
    private static List<object> ReadArray(byte[] b, ref int o, int n) { var l = new List<object>(n); for (int i = 0; i < n; i++) l.Add(Decode(b, ref o)); return l; }
    private static Dictionary<string, object> ReadMap(byte[] b, ref int o, int n)
    {
        var d = new Dictionary<string, object>(n);
        for (int i = 0; i < n; i++) { var k = Decode(b, ref o); d[k?.ToString() ?? ""] = Decode(b, ref o); }
        return d;
    }

    // ------------------------------------------------------------------ helpers for message payloads
    public static string Str(object v) => v as string ?? (v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
    public static double Num(object v, double def = 0) => v switch { long l => l, double d => d, int i => i, float f => f, bool b => b ? 1 : 0, string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) => r, _ => def };
    public static bool Bool(object v) => v switch { bool b => b, long l => l != 0, double d => d != 0, _ => false };
    public static List<object> Arr(object v) => v as List<object>;
}
