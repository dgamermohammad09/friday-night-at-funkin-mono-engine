using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine runtime shaders (FlxRuntimeShader / initLuaShader: shaders/&lt;name&gt;.frag + optional .vert) on MonoGame.
/// MonoGame cannot compile GLSL, so the fragment source is translated to HLSL (common flixel/Psych patterns: '#pragma header',
/// openfl_TextureCoordv / openfl_TextureSize / bitmap / flixel_texture2D, vecN/matN types, mix/fract/mod/atan(y,x)/texture2D,
/// scalar vector constructors, globals -> static, uniforms -> float4 _U[64]) and compiled at runtime with D3DCompile
/// (d3dcompiler_47, desktop DX + UWP). The bytecode replaces the pixel shader of a template effect
/// (Shaders/PsychRuntime.fx: same t0-t3 / s0-s3 / $Globals layout) loaded under a unique effect key.
/// Anything that fails (vertex shaders, matrix*vector products, uniform arrays, DesktopGL/Android) is logged and the sprite is
/// drawn without the shader (honest fallback, like Psych with Shaders off).
/// </summary>
public sealed class RuntimeShader
{
    public string Name { get; }
    public Effect Effect { get; private set; }
    public string Error { get; private set; }
    /// <summary>uniform name -> (slot in _U, component count, type)</summary>
    private readonly Dictionary<string, (int Slot, int Size, string Type)> _uniforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _samplerSlots = new(StringComparer.Ordinal);
    private readonly Vector4[] _u = new Vector4[64];
    private bool _dirty = true;
    public string Hlsl { get; private set; }

    private static byte[] _template;
    private static int _keySeed = 0x5A170000;

    private RuntimeShader(string name) { Name = name; }
    private byte[] _bc;

    /// <summary>Psych setSpriteShader: every object gets its own FlxRuntimeShader (own uniform values).</summary>
    public RuntimeShader Instantiate(GraphicsDevice gd)
    {
        var r = new RuntimeShader(Name) { Hlsl = Hlsl, Error = Error, _bc = _bc };
        foreach (var kv in _uniforms) r._uniforms[kv.Key] = kv.Value;
        foreach (var kv in _samplerSlots) r._samplerSlots[kv.Key] = kv.Value;
        if (_bc != null) { try { r.Effect = MakeEffect(gd, _bc); } catch (Exception ex) { r.Error = ex.Message; } }
        return r;
    }

    public static RuntimeShader Create(GraphicsDevice gd, string name, string fragSource)
    {
        var rs = new RuntimeShader(name);
        try
        {
            rs.Hlsl = rs.Translate(fragSource);
            byte[] bc = D3D.Compile(rs.Hlsl, name, out string err);
            if (bc == null) { rs.Error = "compile: " + err; return rs; }
            rs._bc = bc;
            rs.Effect = MakeEffect(gd, bc);
            if (rs.Effect == null) rs.Error = "template effect swap failed";
        }
        catch (Exception ex) { rs.Error = ex.GetType().Name + ": " + ex.Message; rs.Effect = null; }
        return rs;
    }

    // ------------------------------------------------------------------------------------------------ uniforms
    public bool HasUniform(string n) => _uniforms.ContainsKey(n) || _samplerSlots.ContainsKey(n);
    public void SetFloat(string n, double v) { if (_uniforms.TryGetValue(n, out var u)) { Set(u.Slot, 0, (float)v); } }
    public void SetFloatArray(string n, IList<double> v)
    {
        if (!_uniforms.TryGetValue(n, out var u) || v == null) return;
        for (int i = 0; i < Math.Min(4, v.Count); i++) Set(u.Slot, i, (float)v[i]);
    }
    public double GetFloat(string n) => _uniforms.TryGetValue(n, out var u) ? Comp(_u[u.Slot], 0) : 0.0;
    public double[] GetFloatArray(string n)
    {
        if (!_uniforms.TryGetValue(n, out var u)) return null;
        var r = new double[u.Size]; for (int i = 0; i < u.Size; i++) r[i] = Comp(_u[u.Slot], i); return r;
    }
    public void SetSampler(string n, Texture2D tex)
    {
        if (Effect == null || !_samplerSlots.TryGetValue(n, out int slot) || slot < 1 || slot > 3) return;
        try { Effect.Parameters["T" + slot]?.SetValue(tex); } catch { }
    }
    private void Set(int slot, int comp, float v)
    {
        var x = _u[slot];
        switch (comp) { case 0: x.X = v; break; case 1: x.Y = v; break; case 2: x.Z = v; break; default: x.W = v; break; }
        _u[slot] = x; _dirty = true;
    }
    private static double Comp(Vector4 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : i == 2 ? v.Z : v.W;

    /// <summary>Before drawing a sprite: openfl_TextureSize = texture size, upload uniforms.</summary>
    public void Prepare(Texture2D tex)
    {
        if (Effect == null) return;
        if (tex != null && _uniforms.TryGetValue("openfl_TextureSize", out var ts))
        {
            if (_u[ts.Slot].X != tex.Width || _u[ts.Slot].Y != tex.Height) { _u[ts.Slot].X = tex.Width; _u[ts.Slot].Y = tex.Height; _dirty = true; }
        }
        if (_dirty) { try { Effect.Parameters["_U"]?.SetValue(_u); } catch { } _dirty = false; }
    }

    // ------------------------------------------------------------------------------------------------ translation
    private static readonly string Header = @"
// ---- flixel '#pragma header' (FlxRuntimeShader / FlxGraphicsShader) ----
static float2 openfl_TextureCoordv;
static float openfl_Alphav;
static float4 openfl_ColorMultiplierv;
static float4 openfl_ColorOffsetv;
static float4 _fragColor;
static float4 _fragCoord;
static const bool hasTransform = true;
static const bool hasColorTransform = true;
float4 _psy_tex(Texture2D _t_, SamplerState _s_, float2 _uv_) { float4 _c_ = _t_.Sample(_s_, _uv_); return float4(_c_.rgb * _c_.a, _c_.a); }
float _glsl_mod(float _x_, float _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float2 _glsl_mod(float2 _x_, float2 _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float3 _glsl_mod(float3 _x_, float3 _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float4 _glsl_mod(float4 _x_, float4 _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float2 _glsl_mod(float2 _x_, float _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float3 _glsl_mod(float3 _x_, float _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float4 _glsl_mod(float4 _x_, float _y_) { return _x_ - _y_ * floor(_x_ / _y_); }
float4 flixel_texture2D_(Texture2D _t_, SamplerState _s_, float2 _coord_)
{
    float4 _c_ = _psy_tex(_t_, _s_, _coord_);
    if (!hasTransform) return _c_;
    if (_c_.a == 0.0) return float4(0.0, 0.0, 0.0, 0.0);
    if (!hasColorTransform) return _c_ * openfl_Alphav;
    _c_ = float4(_c_.rgb / _c_.a, _c_.a);
    _c_ = clamp(openfl_ColorOffsetv + (_c_ * openfl_ColorMultiplierv), 0.0, 1.0);
    if (_c_.a > 0.0) return float4(_c_.rgb * _c_.a * openfl_Alphav, _c_.a * openfl_Alphav);
    return float4(0.0, 0.0, 0.0, 0.0);
}
#define texture2D(S, UV) _psy_tex(S##Tex, S##Samp, UV)
#define texture(S, UV) _psy_tex(S##Tex, S##Samp, UV)
#define flixel_texture2D(S, UV) flixel_texture2D_(S##Tex, S##Samp, UV)
#define gl_FragColor _fragColor
#define gl_FragCoord _fragCoord
";

    private static readonly (string, string)[] Words =
    {
        ("vec2", "float2"), ("vec3", "float3"), ("vec4", "float4"), ("ivec2", "int2"), ("ivec3", "int3"), ("ivec4", "int4"),
        ("bvec2", "bool2"), ("bvec3", "bool3"), ("bvec4", "bool4"), ("mat2", "float2x2"), ("mat3", "float3x3"), ("mat4", "float4x4"),
        ("mix", "lerp"), ("fract", "frac"), ("inversesqrt", "rsqrt"), ("dFdx", "ddx"), ("dFdy", "ddy"), ("mod", "_glsl_mod"),
        ("lowp", ""), ("mediump", ""), ("highp", ""),
    };

    internal string Translate(string src)
    {
        src = src.Replace("\r\n", "\n");
        src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        src = Regex.Replace(src, @"//[^\n]*", "");
        var sb = new StringBuilder();
        var globals = new StringBuilder();
        int slot = 4; // _U[0..3] reserved
        int sampler = 1;
        _uniforms["openfl_TextureSize"] = (slot++, 2, "vec2");
        globals.Append("#define openfl_TextureSize (_U[4].xy)").Append('\n'); // flixel header uniform, set from the sprite texture
        foreach (var rawLine in src.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("#pragma header")) continue;      // header emitted once
            if (line.StartsWith("#version") || line.StartsWith("#extension") || line.StartsWith("precision ")) continue;
            if (line.StartsWith("#pragma")) continue;
            sb.Append(rawLine).Append('\n');
        }
        string body = sb.ToString();
        // uniforms: 'uniform <type> a, b;' -> #define name (_U[slot].comps)
        body = Regex.Replace(body, @"\buniform\s+(?:lowp\s+|mediump\s+|highp\s+)?(\w+)\s+([^;]+);", m =>
        {
            string type = m.Groups[1].Value;
            foreach (var part in m.Groups[2].Value.Split(','))
            {
                string n = part.Trim();
                if (n.Length == 0) continue;
                if (n.Contains("[")) throw new NotSupportedException("uniform arrays (" + n + ")");
                if (type == "sampler2D")
                {
                    if (n == "bitmap") continue;
                    if (sampler > 3) throw new NotSupportedException("more than 3 extra sampler2D uniforms");
                    _samplerSlots[n] = sampler;
                    globals.Append($"Texture2D {n}Tex : register(t{sampler}); SamplerState {n}Samp : register(s{sampler});\n");
                    sampler++;
                    continue;
                }
                if (n == "openfl_TextureSize") continue;
                if (slot >= 63) throw new NotSupportedException("too many uniforms");
                int size = type switch { "vec2" or "ivec2" or "bvec2" => 2, "vec3" or "ivec3" or "bvec3" => 3, "vec4" or "ivec4" or "bvec4" => 4, _ => 1 };
                string sw = size switch { 1 => ".x", 2 => ".xy", 3 => ".xyz", _ => "" };
                string expr = type switch
                {
                    "int" => $"((int)_U[{slot}].x)", "bool" => $"(_U[{slot}].x != 0.0)",
                    "ivec2" or "ivec3" or "ivec4" => $"((int{size})_U[{slot}]{sw})",
                    "mat2" or "mat3" or "mat4" => throw new NotSupportedException("matrix uniforms"),
                    _ => $"(_U[{slot}]{sw})"
                };
                _uniforms[n] = (slot, size, type);
                globals.Append($"#define {n} {expr}\n");
                slot++;
            }
            return "";
        });
        // varyings / attributes from flixel's header are declared by us
        body = Regex.Replace(body, @"\b(varying|attribute|in)\s+\w+\s+openfl_\w+\s*;", "");
        body = Regex.Replace(body, @"\bvarying\s+[^;]+;", "");
        foreach (var (a, b) in Words) body = Regex.Replace(body, @"\b" + a + @"\b", b);
        // GLSL texture-coordinate swizzles (.st / .stp / .stpq) -> .xy / .xyz / .xyzw
        body = Regex.Replace(body, @"(?<=[\w\)\]])\.([stpq]{1,4})\b(?!\s*\()", m => "." + new string(m.Groups[1].Value.Select(ch => ch == 's' ? 'x' : ch == 't' ? 'y' : ch == 'p' ? 'z' : 'w').ToArray()));
        body = RewriteCalls(body);
        body = RewriteMatMul(body);
        body = StaticGlobals(body);
        body = Regex.Replace(body, @"\bvoid\s+main\s*\(\s*(void)?\s*\)", "void _glsl_main()");
        if (Regex.IsMatch(body, @"\w\s*\*\s*float[234]x[234]\b|float[234]x[234]\s+\w+\s*=") && Regex.IsMatch(body, @"float[234]x[234]"))
            Console.WriteLine($"[LUA-SHADER] {Name}: matrix maths: only matrix-variable products are reordered (mul), other matrix expressions are kept as written");
        var o = new StringBuilder();
        o.Append("Texture2D bitmapTex : register(t0); SamplerState bitmapSamp : register(s0);\n");
        o.Append("float4 _U[64];\n");
        o.Append(globals);
        o.Append(Header);
        o.Append(body);
        o.Append(@"
float4 PSMain(float4 _pos_ : SV_Position, float4 _col_ : COLOR0, float2 _uv0_ : TEXCOORD0) : SV_Target
{
    openfl_TextureCoordv = _uv0_; openfl_Alphav = _col_.a;
    openfl_ColorMultiplierv = float4(_col_.rgb, 1.0); openfl_ColorOffsetv = float4(0.0, 0.0, 0.0, 0.0);
    _fragCoord = float4(_pos_.xy, 0.0, 1.0);
    _fragColor = float4(0.0, 0.0, 0.0, 0.0);
    _glsl_main();
    float4 _o_ = _fragColor;
    // flixel output is premultiplied; the port's sprite batches blend straight alpha
    if (_o_.a > 0.0) _o_.rgb /= _o_.a;
    return saturate(_o_);
}
");
        return o.ToString();
    }

    /// <summary>atan(y, x) -> atan2; floatN(scalar) -> ((floatN)(scalar)).</summary>
    private static string RewriteCalls(string s)
    {
        var rx = new Regex(@"\b(atan|float[234]|int[234])\s*\(");
        int pos = 0;
        var o = new StringBuilder();
        while (true)
        {
            var m = rx.Match(s, pos);
            if (!m.Success) { o.Append(s, pos, s.Length - pos); break; }
            int open = m.Index + m.Length - 1;
            int depth = 0, commas = 0, close = -1;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) { close = i; break; } }
                else if (c == ',' && depth == 1) commas++;
            }
            if (close < 0) { o.Append(s, pos, s.Length - pos); break; }
            string fn = m.Groups[1].Value;
            string inner = RewriteCalls(s.Substring(open + 1, close - open - 1));
            o.Append(s, pos, m.Index - pos);
            if (fn == "atan") o.Append(commas == 1 ? "atan2(" : "atan(").Append(inner).Append(')');
            else if (commas == 0 && inner.Trim().Length > 0) o.Append("((").Append(fn).Append(")(").Append(inner).Append("))");
            else o.Append(fn).Append('(').Append(inner).Append(')');
            pos = close + 1;
        }
        return o.ToString();
    }

    /// <summary>GLSL matN constructors fill columns, HLSL floatNxN rows (M_hlsl = transpose(M_glsl)), so for matrix variables:
    /// m * v -> mul(v, m), v * m -> mul(m, v), v *= m -> v = mul(m, v). Expressions other than plain identifiers stay as-is.</summary>
    private static string RewriteMatMul(string s)
    {
        var mats = new HashSet<string>();
        foreach (Match m in Regex.Matches(s, @"\bfloat([234])x\1\s+(\w+)")) mats.Add(m.Groups[2].Value);
        foreach (Match m in Regex.Matches(s, @"\buniform\s+mat[234]\s+(\w+)")) mats.Add(m.Groups[1].Value);
        if (mats.Count == 0) return s;
        s = Regex.Replace(s, @"\b(\w+(?:\.\w+)?)\s*\*=\s*(\w+)\b", m => mats.Contains(m.Groups[2].Value) && !mats.Contains(m.Groups[1].Value)
            ? m.Groups[1].Value + " = mul(" + m.Groups[2].Value + ", " + m.Groups[1].Value + ")" : m.Value);
        s = Regex.Replace(s, @"\b(\w+(?:\.\w+)?)\s*\*\s*(\w+(?:\.\w+)?)\b(?!\s*\()", m =>
        {
            string a = m.Groups[1].Value, b = m.Groups[2].Value;
            if (mats.Contains(a) && !mats.Contains(b)) return "mul(" + b + ", " + a + ")";
            if (mats.Contains(b) && !mats.Contains(a) && !Regex.IsMatch(a, @"^\d")) return "mul(" + b + ", " + a + ")";
            return m.Value;
        });
        return s;
    }

    /// <summary>HLSL globals without 'static' are uniforms: prefix file-scope variable declarations.</summary>
    private static string StaticGlobals(string s)
    {
        var o = new StringBuilder();
        int depth = 0; int stmtStart = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) stmtStart = i + 1; }
            else if (c == ';' && depth == 0)
            {
                string stmt = s.Substring(stmtStart, i + 1 - stmtStart);
                string t = stmt.TrimStart();
                int lead = stmt.Length - t.Length;
                if (!t.StartsWith("#") && Regex.IsMatch(t, @"^(const\s+)?(float|int|bool|uint|float[234]|int[234]|bool[234]|float[234]x[234])\b") && !Regex.IsMatch(t, @"^[^=]*\)\s*;$"))
                {
                    // whole statement up to here belongs to o already except this declaration
                    o.Length -= (i - stmtStart);
                    o.Append(stmt.Substring(0, lead)).Append("static ").Append(t.Substring(0, t.Length - 1));
                }
                stmtStart = i + 1;
            }
            else if (c == '\n' && depth == 0 && s.Substring(stmtStart, i - stmtStart).TrimStart().StartsWith("#")) stmtStart = i + 1;
            o.Append(c);
        }
        return o.ToString();
    }

    // ------------------------------------------------------------------------------------------------ effect
    private static Effect MakeEffect(GraphicsDevice gd, byte[] psBytecode)
    {
        if (_template == null)
        {
            string p = Path.Combine(AssetManager.ContentRoot, "shaders", "PsychRuntime.mgfx");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) throw new FileNotFoundException("shaders/PsychRuntime.mgfx");
            _template = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(p);
        }
        var bytes = (byte[])_template.Clone();
        // MGFX header: 'MGFX', version, profile, int32 effectKey -> unique key = own cache entry (own Shader objects)
        int key = System.Threading.Interlocked.Increment(ref _keySeed);
        BitConverter.GetBytes(key).CopyTo(bytes, 6);
        var fx = new Effect(gd, bytes);
        var pass = fx.Techniques[0].Passes[0];
        var shField = typeof(EffectPass).GetField("_pixelShader", BindingFlags.NonPublic | BindingFlags.Instance);
        object shader = shField?.GetValue(pass);
        if (shader == null) return null;
        var st = shader.GetType();
        var bcField = st.GetField("_shaderBytecode", BindingFlags.NonPublic | BindingFlags.Instance);
        var psField = st.GetField("_pixelShader", BindingFlags.NonPublic | BindingFlags.Instance);
        if (bcField == null || psField == null) return null;
        (psField.GetValue(shader) as IDisposable)?.Dispose();
        bcField.SetValue(shader, psBytecode);
        psField.SetValue(shader, null); // recreated from the new bytecode on first use (Shader.PixelShader getter)
        return fx;
    }

    private static class D3D
    {
        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)] string src, UIntPtr srcSize,
            [MarshalAs(UnmanagedType.LPStr)] string sourceName, IntPtr defines, IntPtr include,
            [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
            uint flags1, uint flags2, out IntPtr code, out IntPtr errors);

        // ID3DBlob vtable: 0 QueryInterface, 1 AddRef, 2 Release, 3 GetBufferPointer, 4 GetBufferSize
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr GetPtr(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr GetSize(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Release(IntPtr self);

        private static byte[] Blob(IntPtr blob)
        {
            if (blob == IntPtr.Zero) return null;
            IntPtr vt = Marshal.ReadIntPtr(blob);
            var gp = Marshal.GetDelegateForFunctionPointer<GetPtr>(Marshal.ReadIntPtr(vt, 3 * IntPtr.Size));
            var gs = Marshal.GetDelegateForFunctionPointer<GetSize>(Marshal.ReadIntPtr(vt, 4 * IntPtr.Size));
            var rl = Marshal.GetDelegateForFunctionPointer<Release>(Marshal.ReadIntPtr(vt, 2 * IntPtr.Size));
            int n = (int)gs(blob).ToUInt64();
            var r = new byte[n];
            Marshal.Copy(gp(blob), r, 0, n);
            rl(blob);
            return r;
        }

        public static byte[] Compile(string hlsl, string name, out string error)
        {
            error = null;
            const uint OPTIMIZATION_LEVEL3 = 1u << 15;
            var bytes = Encoding.ASCII.GetBytes(hlsl);
            int hr = D3DCompile(hlsl, (UIntPtr)bytes.Length, name, IntPtr.Zero, IntPtr.Zero, "PSMain", "ps_4_0", OPTIMIZATION_LEVEL3, 0, out IntPtr code, out IntPtr errs);
            var eb = Blob(errs);
            if (eb != null) error = Encoding.ASCII.GetString(eb).TrimEnd('\0');
            if (hr < 0) { Blob(code); return null; }
            return Blob(code);
        }
    }
}
