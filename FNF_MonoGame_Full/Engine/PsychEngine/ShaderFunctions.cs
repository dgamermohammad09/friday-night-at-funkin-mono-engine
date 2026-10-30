using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// psychlua/ShaderFunctions.hx: initLuaShader(name) reads shaders/&lt;name&gt;.frag from the mod folders, setSpriteShader(obj, name)
/// gives the object its OWN FlxRuntimeShader, and get/setShader*(obj, prop, ..) address that object's shader. On MonoGame the
/// .frag is translated + compiled by <see cref="RuntimeShader"/>; failures are logged and the object draws unshaded.
/// </summary>
public static class ShaderFunctions
{
    private static readonly Dictionary<string, RuntimeShader> _protos = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Object tag -> its shader instance (drawn by PlayScene.PsychShaders.cs).</summary>
    public static Dictionary<string, RuntimeShader> SpriteShaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static RuntimeShader GetForSprite(string tag)
        => tag != null && SpriteShaders.TryGetValue(tag, out var fx) ? fx : null;

    public static void ClearAll()
    {
        SpriteShaders.Clear();
        _protos.Clear();
    }

    /// <summary>FunkinLua.initLuaShader: true when the .frag/.vert files exist (compiled once per name).</summary>
    public static bool InitShader(string name, bool trace = true)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!ClientPrefs.Data.Shaders) return false;
        if (_protos.ContainsKey(name)) return true;
        string frag = PsychMods.ModFolders("shaders/" + name + ".frag");
        string vert = PsychMods.ModFolders("shaders/" + name + ".vert");
        if (frag == null && vert == null)
        {
            if (trace) FunkinLua.LuaTrace("Missing shader " + name + " .frag AND .vert files!", false, false);
            return false;
        }
        string src = frag != null ? global::FNF_MonoGame.Engine.FsCI.ReadAllText(frag) : "#pragma header\nvoid main() { gl_FragColor = flixel_texture2D(bitmap, openfl_TextureCoordv); }";
        var rs = RuntimeShader.Create(FNFGame.Instance.GraphicsDevice, name, src);
        if (vert != null) Console.WriteLine($"[LUA-SHADER] {name}: custom .vert ignored (MonoGame sprite batches use their own vertex shader)");
        if (rs.Error != null)
        {
            Console.WriteLine($"[LUA-SHADER] {name}: not supported on this build, drawn without it: {rs.Error}");
            if (Environment.GetEnvironmentVariable("FNF_SHADERLOG") == "1") Console.WriteLine("[LUA-SHADER] HLSL:\n" + rs.Hlsl);
        }
        else Console.WriteLine($"[LUA-SHADER] {name}: translated + compiled");
        _protos[name] = rs;
        return true;
    }

    private static RuntimeShader Obj(string obj) => GetForSprite(obj?.Split('.')[0]);

    public static void Implement(FunkinLua funk)
    {
        var lua = funk.LuaScript;

        lua.Globals["initLuaShader"] = (Func<string, DynValue, bool>)((name, glslVersion) => InitShader(name));

        lua.Globals["setSpriteShader"] = (Func<string, string, bool>)((obj, shader) =>
        {
            if (!ClientPrefs.Data.Shaders) return false;
            if (!_protos.ContainsKey(shader ?? "") && !InitShader(shader))
            {
                FunkinLua.LuaTrace("setSpriteShader: Shader " + shader + " is missing!", false, false);
                return false;
            }
            string tag = (obj ?? "").Split('.')[0];
            SpriteShaders[tag] = _protos[shader].Instantiate(FNFGame.Instance.GraphicsDevice);
            return true;
        });

        lua.Globals["removeSpriteShader"] = (Func<string, bool>)((obj) => SpriteShaders.Remove((obj ?? "").Split('.')[0]));

        lua.Globals["getShaderBool"] = (Func<string, string, DynValue>)((obj, prop) =>
        { var s = Obj(obj); return s == null || !s.HasUniform(prop) ? DynValue.Nil : DynValue.NewBoolean(s.GetFloat(prop) != 0); });
        lua.Globals["getShaderBoolArray"] = (Func<string, string, DynValue>)((obj, prop) => Arr(lua, Obj(obj), prop, true));
        lua.Globals["getShaderInt"] = (Func<string, string, DynValue>)((obj, prop) =>
        { var s = Obj(obj); return s == null || !s.HasUniform(prop) ? DynValue.Nil : DynValue.NewNumber((int)s.GetFloat(prop)); });
        lua.Globals["getShaderIntArray"] = (Func<string, string, DynValue>)((obj, prop) => Arr(lua, Obj(obj), prop, false));
        lua.Globals["getShaderFloat"] = (Func<string, string, DynValue>)((obj, prop) =>
        { var s = Obj(obj); return s == null || !s.HasUniform(prop) ? DynValue.Nil : DynValue.NewNumber(s.GetFloat(prop)); });
        lua.Globals["getShaderFloatArray"] = (Func<string, string, DynValue>)((obj, prop) => Arr(lua, Obj(obj), prop, false));

        lua.Globals["setShaderBool"] = (Func<string, string, bool, bool>)((obj, prop, v) => { var s = Obj(obj); s?.SetFloat(prop, v ? 1 : 0); return s != null; });
        lua.Globals["setShaderInt"] = (Func<string, string, double, bool>)((obj, prop, v) => { var s = Obj(obj); s?.SetFloat(prop, Math.Floor(v)); return s != null; });
        lua.Globals["setShaderFloat"] = (Func<string, string, double, bool>)((obj, prop, v) => { var s = Obj(obj); s?.SetFloat(prop, v); return s != null; });
        lua.Globals["setShaderBoolArray"] = (Func<string, string, Table, bool>)((obj, prop, t) => SetArr(Obj(obj), prop, t));
        lua.Globals["setShaderIntArray"] = (Func<string, string, Table, bool>)((obj, prop, t) => SetArr(Obj(obj), prop, t));
        lua.Globals["setShaderFloatArray"] = (Func<string, string, Table, bool>)((obj, prop, t) => SetArr(Obj(obj), prop, t));
        lua.Globals["setShaderSampler2D"] = (Func<string, string, string, bool>)((obj, prop, bitmapDataPath) =>
        {
            var s = Obj(obj);
            if (s == null) return false;
            try
            {
                string resolved = PsychMods.ModFolders("images/" + bitmapDataPath + ".png")
                                  ?? funk.GameScene?.AssetManager?.ResolvePath($"images/{bitmapDataPath}.png");
                if (resolved == null) return false;
                using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(resolved);
                s.SetSampler(prop, Texture2D.FromStream(FNFGame.Instance.GraphicsDevice, fs));
                return true;
            }
            catch (Exception ex) { Console.WriteLine($"[LUA-SHADER] setShaderSampler2D error: {ex.Message}"); return false; }
        });
    }

    private static DynValue Arr(Script lua, RuntimeShader s, string prop, bool asBool)
    {
        var a = s?.GetFloatArray(prop);
        if (a == null) return DynValue.Nil;
        var t = new Table(lua);
        for (int i = 0; i < a.Length; i++) t[i + 1] = asBool ? DynValue.NewBoolean(a[i] != 0) : DynValue.NewNumber(a[i]);
        return DynValue.NewTable(t);
    }

    private static bool SetArr(RuntimeShader s, string prop, Table t)
    {
        if (s == null || t == null) return false;
        var l = new List<double>();
        foreach (var v in t.Values) l.Add(v.Type == DataType.Boolean ? (v.Boolean ? 1 : 0) : v.CastToNumber() ?? 0);
        s.SetFloatArray(prop, l);
        return true;
    }
}
