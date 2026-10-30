extern alias IRIS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FNF_MonoGame.Engine.PureHScript;

namespace FNF_MonoGame.Engine;

/// <summary>Dynamic object seen by V-Slice scripts (fields/methods resolved by name, like Haxe Dynamic).</summary>
public class VObj
{
    public readonly Dictionary<string, object> Extra = new(StringComparer.Ordinal);
    public virtual bool TryGet(string f, out object v) { v = null; return false; }
    public virtual bool TrySet(string f, object v) => false;
    public object Get(string f) { if (TryGet(f, out var v)) return v; return Extra.TryGetValue(f, out v) ? v : null; }
    public void Set(string f, object v) { if (!TrySet(f, v)) Extra[f] = v; }
    public bool Has(string f) => TryGet(f, out _) || Extra.ContainsKey(f);
    protected static HxFn Fn(Func<object[], object> f) => new HxFn(f);
    protected static object A(object[] a, int i) => HxFn.Arg(a, i);
    public static double D(object o) => o switch { null => 0, double d => d, float f => f, int i => i, long l => l, bool b => b ? 1 : 0, string s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0, _ => Convert.ToDouble(o) };
    public static bool B(object o) => o switch { null => false, bool b => b, double d => d != 0, int i => i != 0, _ => true };
    public static string S(object o) => o?.ToString();
    public override string ToString() => GetType().Name;
}

/// <summary>Stand-in for engine classes the port does not emulate: every field is null and every call is a no-op,
/// so a script keeps running past unsupported features (the original would run them).</summary>
public sealed class VStub : VObj
{
    public readonly string Name;
    public VStub(string name) { Name = name; }
    static readonly HashSet<string> _logged = new();
    public override bool TryGet(string f, out object v)
    {
        if (Extra.TryGetValue(f, out v)) return true;
        string key = Name + "." + f;
        lock (_logged) if (_logged.Add(key)) Console.WriteLine($"[VSLICE-SCRIPT] unsupported: {key}");
        v = new VStub(key); return true;
    }
    public override string ToString() => Name;
}

/// <summary>Simple event object (ScriptEvent): fields + cancel()/stopPropagation().</summary>
public sealed class VEvent : VObj
{
    public bool Cancelled, Stopped;
    public VEvent(string type, params (string, object)[] fields) { Extra["type"] = type; foreach (var (k, v) in fields) Extra[k] = v; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "cancel": v = Fn(a => { Cancelled = true; return null; }); return true;
            case "stopPropagation": v = Fn(a => { Stopped = true; return null; }); return true;
            case "eventCanceled": v = Cancelled; return true;
        }
        v = null; return false;
    }
}

/// <summary>hscript-iris interpreter with V-Slice scripted-class semantics: identifiers fall back to the class instance
/// ("this", the C# facade of the superclass), assignments to superclass fields go to the facade, VObj field access,
/// unknown classes/identifiers become <see cref="VStub"/>s instead of aborting the script.</summary>
public sealed class VInterp : IRIS::psychlua.CustomInterp
{
    public VObj Self;
    public Func<string, object[], object> Constructor;
    public string ScriptName;

    public VInterp() : base() { set_parentInstance(null); }

    bool InScope(string id)
    {
        var loc = (IRIS::haxe.ds.StringMap<object>)IRIS::haxe.ds.StringMap<object>.__hx_cast<object>((IRIS::haxe.ds.StringMap)(object)locals);
        if (loc.exists(id)) return true;
        return ((IRIS::haxe.ds.StringMap)(object)variables).exists(id) || ((IRIS::haxe.ds.StringMap)(object)imports).exists(id);
    }

    public override object resolve(string id)
    {
        if (InScope(id)) return base.resolve(id);
        if (Self != null && Self.TryGet(id, out var v)) return v;
        if (Self != null && Self.Extra.TryGetValue(id, out v)) return v;
        var g = VSliceScripts.Global(id);
        if (g != null) return g;
        return base.resolve(id);   // throws EUnknownVariable like hscript
    }

    public override object assign(IRIS::crowplexus.hscript.Expr e1, IRIS::crowplexus.hscript.Expr e2)
    {
        if (e1.e is IRIS::crowplexus.hscript.ExprDef_EIdent ei && Self != null && !InScope(ei.v) && Self.Has(ei.v))
        {
            object v = expr(e2);
            Self.Set(ei.v, v);
            return v;
        }
        return base.assign(e1, e2);
    }

    public override object @get(object o, string f)
    {
        if (o is VObj vo) return vo.Get(f);
        if (o is System.Type t && t == typeof(VStub)) return null;
        try { return base.@get(o, f); }
        catch (Exception) { return null; }
    }

    public override object @set(object o, string f, object v)
    {
        if (o is VObj vo) { vo.Set(f, v); return v; }
        try { return base.@set(o, f, v); } catch (Exception) { return v; }
    }

    public override object fcall(object o, string f, IRIS::Array args)
    {
        if (o is VObj vo)
        {
            var fn = vo.Get(f);
            if (fn == null) { Console.WriteLine($"[VSLICE-SCRIPT] {ScriptName}: call of null function {f} on {vo}"); return null; }
            return call(o, fn, args);
        }
        if (o is string s) return VSliceScripts.StringMethod(s, f, ToArr(args));
        return base.fcall(o, f, args);
    }

    public override object call(object o, object f, IRIS::Array args)
    {
        if (f is VStub || f == null) return null;   // unsupported engine API: no-op
        return base.call(o, f, args);
    }

    public override object cnew(string cl, IRIS::Array args)
    {
        var r = Constructor?.Invoke(cl, ToArr(args)) ?? VSliceScripts.New(cl, ToArr(args));
        if (r != null) return r;
        try { return base.cnew(cl, args); } catch { return new VStub(cl); }
    }

    public static object[] ToArr(IRIS::Array a)
    {
        if (a == null) return System.Array.Empty<object>();
        if (a is IRIS::Array<object> ao) { var ro = new object[ao.length]; for (int i = 0; i < ro.Length; i++) ro[i] = ao.__get(i); return ro; }
        int n = (int)IRIS::haxe.lang.Runtime.toInt(IRIS::Reflect.field(a, "length"));
        var r = new object[n];
        for (int i = 0; i < n; i++) r[i] = IRIS::Reflect.callMethod(a, IRIS::Reflect.field(a, "__get"), new IRIS::Array<object>(new object[] { i }));
        return r;
    }
}

/// <summary>One parsed .hxc class (polymod scripted class).</summary>
public sealed class VScriptClass
{
    public string Name, Extends, File, ModId;
    public List<(string kind, string name, string code)> Members = new();
    public string[] Imports;
    public override string ToString() => $"{Name} extends {Extends}";
}

/// <summary>An instance of a scripted class: its own interpreter; fields/methods are interp variables, the superclass
/// behaviour is the facade <see cref="Self"/>.</summary>
public sealed class VScriptInstance : VObj
{
    public readonly VScriptClass Class;
    public readonly VInterp Interp;
    public VObj Self => Interp.Self;
    public object[] SuperArgs = System.Array.Empty<object>();
    public string Id => SuperArgs.Length > 0 ? SuperArgs[0]?.ToString() : null;
    public bool Failed;

    public VScriptInstance(VScriptClass cls, VObj self)
    {
        Class = cls;
        Interp = new VInterp { Self = self, ScriptName = cls.Name };
        Interp.showPosOnLog = false;
    }

    static IRIS::haxe.ds.StringMap Vars(VInterp i) => (IRIS::haxe.ds.StringMap)(object)i.variables;
    object VGet(string n) => IRIS::haxe.lang.Runtime.callField(Vars(Interp), "get", 5144726, new object[] { n });
    public bool HasFunction(string n) => Vars(Interp).exists(n) && VGet(n) is IRIS::haxe.lang.Function;

    public void SetVar(string n, object v) => IRIS::haxe.lang.Runtime.callField(Vars(Interp), "set", 5741474, new object[] { n, v });
    public object GetVar(string n) => Vars(Interp).exists(n) ? VGet(n) : null;

    public override bool TryGet(string f, out object v)
    {
        if (Vars(Interp).exists(f)) { v = VGet(f); return true; }
        if (Self != null && Self.TryGet(f, out v)) return true;
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        if (Vars(Interp).exists(f)) { SetVar(f, v); return true; }
        if (Self != null && Self.Has(f)) { Self.Set(f, v); return true; }
        SetVar(f, v); return true;
    }

    /// <summary>Call a script method (override) if defined. Returns (found, result).</summary>
    public (bool found, object result) Call(string fn, params object[] args)
    {
        if (Failed || !HasFunction(fn)) return (false, null);
        try
        {
            var f = (IRIS::haxe.lang.Function)VGet(fn);
            var norm = args.Select(a => a is float fl ? (double)fl : a).ToArray();
            return (true, IRIS::Reflect.callMethod(null, f, new IRIS::Array<object>(norm)));
        }
        catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] {Class.Name}.{fn}: {VSliceScripts.Describe(ex)}"); return (true, null); }
    }
    public override string ToString() => Class.Name;
}

/// <summary>Loader + registry of V-Slice scripted classes (polymod .hxc) from enabled V-Slice mods.</summary>
public static class VSliceScripts
{
    public static readonly List<VScriptClass> Classes = new();
    static bool _loaded;
    /// <summary>Globals (class names) resolved by scripts; filled by the game layer (PlayScene facade registers PlayState etc.).</summary>
    public static readonly Dictionary<string, object> Globals = new(StringComparer.Ordinal);
    public static Func<string, object[], object> Factory;   // game-layer constructor hook for "new X(...)"

    public static object Global(string id)
    {
        if (Globals.TryGetValue(id, out var v)) return v;
        if (id.Length > 0 && char.IsUpper(id[0])) return new VStub(id);   // unknown class name -> stub
        return null;
    }

    public static object New(string cl, object[] args)
    {
        var f = Factory?.Invoke(cl, args);
        if (f != null) return f;
        string shortName = cl.Contains('.') ? cl.Substring(cl.LastIndexOf('.') + 1) : cl;
        if (shortName == "Array") return new IRIS::Array<object>();
        if (Classes.Any(c => c.Name == shortName)) return Instantiate(Classes.First(c => c.Name == shortName), new VObj(), args);
        return new VStub(shortName);
    }

    public static object StringMethod(string s, string f, object[] a)
    {
        switch (f)
        {
            case "toLowerCase": return s.ToLowerInvariant();
            case "toUpperCase": return s.ToUpperInvariant();
            case "startsWith": return s.StartsWith(VObj.S(a.ElementAtOrDefault(0)) ?? "", StringComparison.Ordinal);
            case "endsWith": return s.EndsWith(VObj.S(a.ElementAtOrDefault(0)) ?? "", StringComparison.Ordinal);
            case "indexOf": return s.IndexOf(VObj.S(a.ElementAtOrDefault(0)) ?? "", StringComparison.Ordinal);
            case "contains": return s.Contains(VObj.S(a.ElementAtOrDefault(0)) ?? "");
            case "split": return new IRIS::Array<object>(s.Split(new[] { VObj.S(a.ElementAtOrDefault(0)) ?? "" }, StringSplitOptions.None).Cast<object>().ToArray());
            case "substr": { int p = (int)VObj.D(a.ElementAtOrDefault(0)); if (p < 0) p = Math.Max(0, s.Length + p); int l = a.Length > 1 ? (int)VObj.D(a[1]) : s.Length - p; return s.Substring(Math.Min(p, s.Length), Math.Max(0, Math.Min(l, s.Length - Math.Min(p, s.Length)))); }
            case "substring": { int p = (int)VObj.D(a.ElementAtOrDefault(0)); int e = a.Length > 1 ? (int)VObj.D(a[1]) : s.Length; p = Math.Clamp(p, 0, s.Length); e = Math.Clamp(e, 0, s.Length); if (e < p) (p, e) = (e, p); return s.Substring(p, e - p); }
            case "charAt": { int p = (int)VObj.D(a.ElementAtOrDefault(0)); return p >= 0 && p < s.Length ? s[p].ToString() : ""; }
            case "replace": return s.Replace(VObj.S(a.ElementAtOrDefault(0)) ?? "", VObj.S(a.ElementAtOrDefault(1)) ?? "");
            case "trim": return s.Trim();
            case "toString": return s;
        }
        return null;
    }

    public static string Describe(Exception ex)
    {
        if (ex is IRIS::haxe.Exception hx) { var v = hx.unwrap(); return v is IRIS::crowplexus.hscript.Error he ? IRIS::crowplexus.hscript.Printer.errorToString(he, new IRIS::haxe.lang.Null<bool>(true, true)) : (hx.get_message() ?? v?.ToString()); }
        if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null) return Describe(tie.InnerException);
        return ex.GetType().Name + ": " + ex.Message;
    }

    // ------------------------------------------------------------------ loading
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Classes.Clear();
        foreach (var (mod, path) in VSliceMods.ScriptFiles())
        {
            try
            {
                foreach (var c in ParseFile(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path), path)) { c.ModId = mod.Id; Classes.Add(c); }
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] parse {path}: {ex.Message}"); }
        }
        Console.WriteLine($"[VSLICE-SCRIPT] {Classes.Count} scripted classes: {string.Join(", ", Classes.Select(c => c.ToString()))}");
    }

    public static void Reset() { _loaded = false; Classes.Clear(); }

    /// <summary>Split a .hxc file into classes and class members (vars / functions), stripping Haxe-only syntax that
    /// hscript-iris does not parse inside a class body (access modifiers, override, inline).</summary>
    public static List<VScriptClass> ParseFile(string src, string path)
    {
        var list = new List<VScriptClass>();
        src = StripPreprocessor(src);
        var imports = Regex.Matches(src, @"^\s*import\s+([\w.]+)(?:\s+as\s+(\w+))?\s*;", RegexOptions.Multiline)
            .Cast<Match>().Select(m => m.Groups[2].Success ? m.Groups[1].Value + " as " + m.Groups[2].Value : m.Groups[1].Value).ToArray();
        int pos = 0;
        var clsRe = new Regex(@"\bclass\s+(\w+)(?:\s+extends\s+([\w.]+))?[^{]*\{");
        while (true)
        {
            var m = clsRe.Match(src, pos);
            if (!m.Success) break;
            int bodyStart = m.Index + m.Length;
            int bodyEnd = MatchBrace(src, bodyStart - 1);
            var cls = new VScriptClass { Name = m.Groups[1].Value, Extends = m.Groups[2].Success ? m.Groups[2].Value : null, File = path, Imports = imports };
            SplitMembers(src.Substring(bodyStart, bodyEnd - bodyStart), cls);
            list.Add(cls);
            pos = bodyEnd + 1;
        }
        return list;
    }

    static string StripPreprocessor(string s)
    {
        // #if mobile ... #else ... #end : keep the desktop branch (mobile/html5/web false, everything else true-ish handled by iris)
        var lines = s.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var stack = new Stack<(bool active, bool taken)>();
        bool Active() => stack.All(x => x.active);
        foreach (var raw in lines)
        {
            string line = raw;
            // inline "#if mobile || x #end" fragments inside expressions
            line = Regex.Replace(line, @"#if\s+(mobile|html5|web|android|ios)\b[^#]*#end", "");
            line = Regex.Replace(line, @"#if\s+!(mobile|html5|web|android|ios)\b\s*([^#]*)#end", "$2");
            var t = line.Trim();
            if (t.StartsWith("#if"))
            {
                string cond = t.Substring(3).Trim();
                bool v = EvalCond(cond);
                stack.Push((v, v)); continue;
            }
            if (t.StartsWith("#elseif")) { var top = stack.Pop(); bool v = !top.taken && EvalCond(t.Substring(7).Trim()); stack.Push((v, top.taken || v)); continue; }
            if (t.StartsWith("#else")) { var top = stack.Pop(); stack.Push((!top.taken, true)); continue; }
            if (t.StartsWith("#end")) { if (stack.Count > 0) stack.Pop(); continue; }
            if (Active()) sb.Append(line).Append('\n'); else sb.Append('\n');
        }
        return sb.ToString();
    }

    static bool EvalCond(string c)
    {
        c = c.Trim().Trim('(', ')').Trim();
        if (c.Contains("||")) return c.Split(new[] { "||" }, StringSplitOptions.None).Any(EvalCond);
        if (c.Contains("&&")) return c.Split(new[] { "&&" }, StringSplitOptions.None).All(EvalCond);
        if (c.StartsWith("!")) return !EvalCond(c.Substring(1));
        return c switch { "mobile" or "html5" or "web" or "android" or "ios" or "FEATURE_MOBILE_ADVERTISEMENTS" or "NO_FEATURE_TOUCH_CONTROLS" => false, "desktop" or "windows" or "sys" or "cpp" or "FEATURE_NEWGROUNDS" => c != "FEATURE_NEWGROUNDS", _ => false };
    }

    /// <summary>Index of the brace matching the '{' at <paramref name="open"/> (skips strings and comments).</summary>
    public static int MatchBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { i = s.IndexOf('\n', i); if (i < 0) return s.Length; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { i = s.IndexOf("*/", i + 2, StringComparison.Ordinal); if (i < 0) return s.Length; i++; continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i); continue; }
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return s.Length;
    }

    static int SkipString(string s, int i)
    {
        char q = s[i];
        for (int j = i + 1; j < s.Length; j++)
        {
            if (s[j] == '\\') { j++; continue; }
            if (s[j] == q) return j;
        }
        return s.Length;
    }

    static readonly Regex Modifiers = new(@"^(\s*(?:@:?[\w.]+(?:\([^)]*\))?\s*)*)((?:(?:override|public|private|static|inline|dynamic|macro|extern)\s+)*)", RegexOptions.Compiled);

    static void SplitMembers(string body, VScriptClass cls)
    {
        int i = 0;
        while (i < body.Length)
        {
            // skip whitespace / comments
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length) break;
            if (body[i] == '/' && i + 1 < body.Length && body[i + 1] == '/') { int n = body.IndexOf('\n', i); i = n < 0 ? body.Length : n + 1; continue; }
            if (body[i] == '/' && i + 1 < body.Length && body[i + 1] == '*') { int n = body.IndexOf("*/", i + 2, StringComparison.Ordinal); i = n < 0 ? body.Length : n + 2; continue; }
            // find member end: ';' at depth 0, or the end of the first {...} block for functions
            int start = i, depth = 0; bool isFunc = false; int end = -1;
            for (int j = i; j < body.Length; j++)
            {
                char c = body[j];
                if (c == '/' && j + 1 < body.Length && body[j + 1] == '/') { j = body.IndexOf('\n', j); if (j < 0) j = body.Length; continue; }
                if (c == '/' && j + 1 < body.Length && body[j + 1] == '*') { j = body.IndexOf("*/", j + 2, StringComparison.Ordinal); if (j < 0) j = body.Length; j++; continue; }
                if (c == '"' || c == '\'') { j = SkipString(body, j); continue; }
                if (depth == 0 && !isFunc && Regex.IsMatch(body.Substring(start, j - start + 1), @"\bfunction\s*$")) isFunc = true;
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == '{')
                {
                    if (depth == 0 && isFunc) { end = MatchBrace(body, j); break; }
                    depth++;
                }
                else if (c == '}') depth--;
                else if (c == ';' && depth == 0) { end = j; break; }
            }
            if (end < 0) end = body.Length - 1;
            string member = body.Substring(start, end - start + 1);
            i = end + 1;
            var mm = Modifiers.Match(member);
            string rest = member.Substring(mm.Length).Trim();
            bool isStatic = mm.Groups[2].Value.Contains("static");
            var fm = Regex.Match(rest, @"^function\s+(\w+)");
            var vm = Regex.Match(rest, @"^(?:var|final)\s+(\w+)");
            if (fm.Success)
            {
                string name = fm.Groups[1].Value == "new" ? "__new" : fm.Groups[1].Value;
                string code = "function " + name + rest.Substring(fm.Length);
                code = RewriteBody(code);
                cls.Members.Add((isStatic ? "sfunction" : "function", name, code));
            }
            else if (vm.Success)
            {
                string code = Regex.Replace(rest, @"^final\b", "var");
                code = RewriteBody(code);
                cls.Members.Add((isStatic ? "svar" : "var", vm.Groups[1].Value, code));
            }
        }
    }

    /// <summary>super(...) -> __superNew(...), super.x -> __super.x, this.x -> x (fields live in the interpreter or the facade).</summary>
    static string RewriteBody(string code)
    {
        code = Regex.Replace(code, @"\bsuper\s*\(", "__superNew(");
        code = Regex.Replace(code, @"\bsuper\s*\.", "__super.");
        code = Regex.Replace(code, @"\bthis\s*\.\s*(?=\w)", "");
        code = Regex.Replace(code, @"([\w.\]\)]+)\s+is\s+([A-Z][\w.]*)", "Std.isOfType($1, $2)");
        code = Regex.Replace(code, @"\bcast\s*\(\s*([^,()]+?)\s*,\s*[\w.<>]+\s*\)", "($1)");   // cast(x, T) -> (x)
        code = Regex.Replace(code, @"(?<=[\w\)\]])\?\.", ".");   // optional chaining a?.b -> a.b (null access throws -> caught per call)
        code = InterpolateSingleQuoted(code);
        code = DefaultParams(code);
        return code;
    }

    /// <summary>Haxe string interpolation: 'a $b ${c + 1}' -> ("a " + b + " " + (c + 1)) (hscript-iris does not interpolate).</summary>
    static string InterpolateSingleQuoted(string code)
    {
        if (code.IndexOf('$') < 0) return code;
        var sb = new StringBuilder(code.Length + 16);
        for (int i = 0; i < code.Length; i++)
        {
            char ch = code[i];
            if (ch == '/' && i + 1 < code.Length && code[i + 1] == '/') { int e = code.IndexOf('\n', i); if (e < 0) e = code.Length; sb.Append(code, i, e - i); i = e - 1; continue; }
            if (ch == '"') { int j = i + 1; while (j < code.Length && code[j] != '"') { if (code[j] == '\\') j++; j++; } j = Math.Min(j, code.Length - 1); sb.Append(code, i, j - i + 1); i = j; continue; }
            if (ch != '\'') { sb.Append(ch); continue; }
            int k = i + 1; var lit = new StringBuilder();
            while (k < code.Length && code[k] != '\'') { if (code[k] == '\\' && k + 1 < code.Length) { lit.Append(code[k]).Append(code[k + 1]); k += 2; continue; } lit.Append(code[k]); k++; }
            string body = lit.ToString();
            if (body.IndexOf('$') < 0 || k >= code.Length) { sb.Append('\'').Append(body).Append(k < code.Length ? "'" : ""); i = k; continue; }
            var parts = new List<string>(); var cur = new StringBuilder();
            for (int p = 0; p < body.Length; p++)
            {
                if (body[p] == '$' && p + 1 < body.Length && body[p + 1] == '$') { cur.Append('$'); p++; continue; }
                if (body[p] == '$' && p + 1 < body.Length && body[p + 1] == '{')
                {
                    int depth = 0, q = p + 1;
                    for (; q < body.Length; q++) { if (body[q] == '{') depth++; else if (body[q] == '}') { depth--; if (depth == 0) break; } }
                    parts.Add("\"" + cur.ToString().Replace("\"", "\\\"") + "\""); cur.Clear();
                    parts.Add("(" + body.Substring(p + 2, Math.Max(0, q - p - 2)) + ")"); p = q; continue;
                }
                if (body[p] == '$' && p + 1 < body.Length && (char.IsLetter(body[p + 1]) || body[p + 1] == '_'))
                {
                    int q = p + 1; while (q < body.Length && (char.IsLetterOrDigit(body[q]) || body[q] == '_')) q++;
                    parts.Add("\"" + cur.ToString().Replace("\"", "\\\"") + "\""); cur.Clear();
                    parts.Add("Std.string(" + body.Substring(p + 1, q - p - 1) + ")"); p = q - 1; continue;
                }
                cur.Append(body[p]);
            }
            parts.Add("\"" + cur.ToString().Replace("\"", "\\\"") + "\"");
            sb.Append('(').Append(string.Join(" + ", parts)).Append(')');
            i = k;
        }
        return sb.ToString();
    }

    /// <summary>function f(a:T = 1, b = 'x') { ... } -> function f(?a:T, ?b) { if (a == null) a = 1; ... } (iris requires all args otherwise).</summary>
    static string DefaultParams(string code)
    {
        var m = Regex.Match(code, @"^(\s*function\s+\w+\s*\()([^)]*)(\)[^{]*\{)");
        if (!m.Success || m.Groups[2].Value.IndexOf('=') < 0) return code;
        var ps = new List<string>(); int depth = 0; var cur = new StringBuilder();
        foreach (char c in m.Groups[2].Value) { if (c == '(' || c == '[' || c == '{') depth++; if (c == ')' || c == ']' || c == '}') depth--; if (c == ',' && depth == 0) { ps.Add(cur.ToString()); cur.Clear(); } else cur.Append(c); }
        ps.Add(cur.ToString());
        var outPs = new List<string>(); var pre = new StringBuilder();
        foreach (var p0 in ps)
        {
            string p = p0.Trim(); int eq = p.IndexOf('=');
            if (eq < 0) { outPs.Add(p); continue; }
            string decl = p.Substring(0, eq).Trim(), def = p.Substring(eq + 1).Trim();
            string name = Regex.Match(decl.TrimStart('?'), @"^\w+").Value;
            outPs.Add(decl.StartsWith("?") ? decl : "?" + decl);
            pre.Append($" if ({name} == null) {name} = {def};");
        }
        return m.Groups[1].Value + string.Join(", ", outPs) + m.Groups[3].Value + pre + code.Substring(m.Length);
    }

    /// <summary>Create an instance of a scripted class with the given superclass facade; runs field initialisers and new().</summary>
    public static VScriptInstance Instantiate(VScriptClass cls, VObj self, object[] ctorArgs = null)
    {
        var inst = new VScriptInstance(cls, self);
        var parser = new IRIS::crowplexus.hscript.Parser();
        parser.allowTypes = true; parser.allowJSON = true; parser.allowMetadata = true;
        inst.SetVar("this", (object)inst); inst.SetVar(cls.Name, (object)inst);   // ClassName.staticField from inside the class (static vars live on the instance)
        inst.SetVar("__super", (object)(self ?? new VObj()));
        inst.SetVar("__superNew", new HxFn(a => { inst.SuperArgs = a; (self as IVSuperInit)?.SuperInit(a); return null; }));
        string lastTrace = null; int traceRepeats = 0;
        inst.SetVar("trace", new HxFn(a => { string msg = string.Join(", ", a.Select(x => x?.ToString())); if (msg == lastTrace) { if (++traceRepeats % 600 == 0) Console.WriteLine($"[VSLICE-TRACE] {cls.Name}: {msg} (x{traceRepeats})"); return null; } lastTrace = msg; traceRepeats = 0; Console.WriteLine($"[VSLICE-TRACE] {cls.Name}: {msg}"); return null; }));   // per-frame traces (blissful.hxc onUpdate) are collapsed
        // imports with alias ("import a.B as C"): register the alias name
        foreach (var imp in cls.Imports ?? System.Array.Empty<string>())
        {
            var parts = imp.Split(new[] { " as " }, StringSplitOptions.None);
            string full = parts[0], alias = parts.Length > 1 ? parts[1] : full.Substring(full.LastIndexOf('.') + 1);
            string shortName = full.Substring(full.LastIndexOf('.') + 1);
            var g = Global(shortName);
            if (g != null && alias != shortName) inst.SetVar(alias, g);
        }
        foreach (var (kind, name, code) in cls.Members)
        {
            try
            {
                if (kind.EndsWith("function"))
                    inst.Interp.execute(parser.parseString(code, cls.File));
                else
                {
                    // var x:T = init;  -> evaluate init in the interpreter and store as a variable
                    var vm = Regex.Match(code, @"^var\s+(\w+)\s*(?::\s*[^=;]+?)?\s*(?:=\s*([\s\S]*?))?;?\s*$");
                    object val = null;
                    if (vm.Success && vm.Groups[2].Success && vm.Groups[2].Value.Trim().Length > 0)
                        val = inst.Interp.execute(parser.parseString(vm.Groups[2].Value.Trim(), cls.File));
                    inst.SetVar(name, val);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] {cls.Name}.{name}: {Describe(ex)}"); }
        }
        var args = ctorArgs ?? System.Array.Empty<object>();
        var ctor = cls.Members.FirstOrDefault(m => m.name == "__new");
        if (ctor.code != null)
        {
            var pm = Regex.Match(ctor.code, @"^function\s+__new\s*\(([^)]*)\)");
            int n = pm.Success && pm.Groups[1].Value.Trim().Length > 0 ? pm.Groups[1].Value.Split(',').Length : 0;
            if (args.Length < n) { var pad = new object[n]; System.Array.Copy(args, pad, args.Length); args = pad; }
        }
        else if (self is IVSuperInit si) si.SuperInit(System.Array.Empty<object>());
        inst.Call("__new", args);
        return inst;
    }

    /// <summary>Scripted classes whose superclass short name is one of <paramref name="bases"/>.</summary>
    public static IEnumerable<VScriptClass> ClassesExtending(params string[] bases)
    {
        EnsureLoaded();
        foreach (var c in Classes)
        {
            string b = c.Extends == null ? null : c.Extends.Substring(c.Extends.LastIndexOf('.') + 1);
            if (b != null && b.StartsWith("Scripted")) b = b.Substring(8);   // polymod ScriptedX == X
            if (b != null && bases.Contains(b)) yield return c;
        }
    }
}

/// <summary>Facades that care about the super(...) constructor arguments (e.g. the stage/character id).</summary>
public interface IVSuperInit { void SuperInit(object[] args); }
