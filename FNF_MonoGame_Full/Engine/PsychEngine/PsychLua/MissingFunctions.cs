using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;
using FNF_MonoGame.Gameplay;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.PsychLua;

/// <summary>
/// Psych 1.0.4 Lua callbacks that were not registered by the port (FunkinLua.hx, psychlua/*.hx).
/// Calling an unregistered callback raised "attempt to call a nil value" and aborted the whole Lua
/// callback (e.g. cgstage.lua onCreate stopped at loadFrames, so the stage was never built).
/// Only names that are still undefined are registered, so existing implementations win.
/// Functions without an engine equivalent (sound tags, shader arrays) are safe no-ops returning
/// Psych's defaults so the rest of the script keeps running.
/// </summary>
public static class MissingFunctions
{
    public static void Implement(FunkinLua funk)
    {
        var lua = funk?.LuaScript; var game = funk?.GameScene;
        if (lua == null || game == null) return;
        void Reg(string name, Delegate d) { if (lua.Globals.Get(name).IsNil()) lua.Globals[name] = d; }
        void Stub(string name, DynValue ret = null) { if (lua.Globals.Get(name).IsNil()) lua.Globals[name] = DynValue.NewCallback((c, a) => ret ?? DynValue.Nil); }
        // LuaUtils.addAnimByIndices: indices = a "0,1,2" string, a single number or a Lua array
        static int[] LuaFrameList(DynValue v)
        {
            if (v == null || v.IsNil()) return System.Array.Empty<int>();
            if (v.Type == DataType.String) return LuaUtils.StringToIntArray(v.String);
            if (v.Type == DataType.Number) return new[] { (int)v.Number };
            if (v.Type == DataType.Table)
            {
                var l = new System.Collections.Generic.List<int>();
                foreach (var x in v.Table.Values) if (x.Type == DataType.Number) l.Add((int)x.Number); else if (x.Type == DataType.String && int.TryParse(x.String.Trim(), out int n)) l.Add(n);
                return l.ToArray();
            }
            return System.Array.Empty<int>();
        }

        // ── FunkinLua.hx: midpoints / positions ──
        Reg("getMidpointX", (Func<string, double>)(o => game.PsychMidpoint(o)?.X ?? 0));
        Reg("getMidpointY", (Func<string, double>)(o => game.PsychMidpoint(o)?.Y ?? 0));
        Reg("getGraphicMidpointX", (Func<string, double>)(o => game.PsychMidpoint(o, true)?.X ?? 0));
        Reg("getGraphicMidpointY", (Func<string, double>)(o => game.PsychMidpoint(o, true)?.Y ?? 0));
        Reg("getScreenPositionX", (Func<string, string, double>)((o, cam) => ScreenPos(game, o, true)));
        Reg("getScreenPositionY", (Func<string, string, double>)((o, cam) => ScreenPos(game, o, false)));
        Reg("objectsOverlap", (Func<string, string, bool>)((a, b) =>
        {
            var ra = Bounds(game, a); var rb = Bounds(game, b);
            return ra.HasValue && rb.HasValue && ra.Value.Intersects(rb.Value);
        }));
        // getPixelColor(obj, x, y): spr.pixels.getPixel32(x, y) (whole graphic), FlxColor.BLACK when missing
        Reg("getPixelColor", (Func<string, int, int, int>)((o, x, y) => game.PsychGetPixel32(o, x, y)));
        // TextFunctions getTextString: any FlxText-like object's text
        Reg("getTextString", (Func<string, string>)(t =>
        {
            var lt = game.GetLuaTextObject(t);
            if (lt != null) return lt.Text;
            var obj = game.ResolvePsychObject(t);
            object txt = obj == null ? null : LuaUtils.GetVarInArray(obj, "text", false);
            if (txt != null) return txt.ToString();
            FunkinLua.LuaTrace("getTextString: Object " + t + " doesn't exist!", false, false);
            return null;
        }));
        // backend/Language.hx + Discord.hx callbacks
        if (lua.Globals.Get("getTranslationPhrase").IsNil()) Language.AddLuaCallbacks(lua);
        Stub("changeDiscordPresence");
        Stub("changeDiscordClientID");

        // ── health / rating ──
        Reg("addHealth", (Action<double>)(v => game.Health = game.Health + (float)v));
        Reg("setRatingPercent", (Action<double>)(v => game.SetPsychRating((float)v * 100f, null, null)));
        Reg("setRatingName", (Action<string>)(v => game.SetPsychRating(null, v, null)));
        Reg("setRatingFC", (Action<string>)(v => game.SetPsychRating(null, null, v)));
        Reg("updateScoreText", (Action)(() => game.PsychUpdateScoreTextNow())); // PlayState.updateScoreText
        // FunkinLua addHScript(file, ignoreAlreadyRunning) / removeHScript(file) / setOnHScript(var, value)
        Reg("addHScript", (Func<string, DynValue, bool>)((f, ign) => game.PsychAddHScript(f)));
        Reg("removeHScript", (Func<string, bool>)(f => game.PsychRemoveHScript(f)));
        Reg("setOnHScript", (Action<string, DynValue>)((n, v) => game.PsychSetOnHScript(n, v?.ToObject())));
        Reg("setHealthBarColors", (Action<DynValue, DynValue>)((l, r) => game.SetHealthBarColors(l.IsNil() ? null : l.CastToString(), r.IsNil() ? null : r.CastToString())));
        Reg("setTimeBarColors", (Action<DynValue, DynValue>)((l, r) => game.SetTimeBarColors(l.IsNil() ? null : l.CastToString(), r.IsNil() ? null : r.CastToString())));

        // ── camera follow / scroll (FlxCamera of camGame) ──
        Reg("getCameraFollowX", (Func<double>)(() => game.camFollow.x));
        Reg("getCameraFollowY", (Func<double>)(() => game.camFollow.y));
        Reg("setCameraFollowPoint", (Action<double, double>)((x, y) => game.camFollow.set((float)x, (float)y)));
        Reg("addCameraFollowPoint", (Action<double, double>)((x, y) => game.camFollow.set(game.camFollow.x + (float)x, game.camFollow.y + (float)y)));
        // FunkinLua: getCameraScrollX = scroll.x + FlxG.width/2 (camera centre); setCameraScroll(x, y) = scroll.set(x - w/2, y - h/2)
        Reg("getCameraScrollX", (Func<double>)(() => game.PsychCamScrollX + 640.0));
        Reg("getCameraScrollY", (Func<double>)(() => game.PsychCamScrollY + 360.0));
        Reg("setCameraScroll", (Action<double, double>)((x, y) => { game.PsychCamScrollX = (float)x - 640f; game.PsychCamScrollY = (float)y - 360f; }));
        Reg("addCameraScroll", (Action<double, double>)((x, y) => { game.PsychCamScrollX += (float)x; game.PsychCamScrollY += (float)y; }));

        // ── existence checks ──
        Reg("luaSpriteExists", (Func<string, bool>)(t => t != null && game.ModchartSprites.ContainsKey(t)));
        Reg("luaTextExists", (Func<string, bool>)(t => game.GetLuaTextObject(t) != null));
        Stub("luaSoundExists", DynValue.False);
        Reg("achievementExists", (Func<string, bool>)(n => n != null && Achievements.AllAchievements.ContainsKey(n)));
        Reg("getModSetting", (Func<string, string, DynValue>)((s, mod) =>
        {
            var v = LuaUtils.GetModSetting(string.IsNullOrEmpty(mod) ? PsychMods.CurrentModDirectory : mod, s);
            return v == null ? DynValue.Nil : DynValue.FromObject(lua, v);
        }));

        // ── sprites ──
        Reg("loadGraphic", (Action<string, string, int, int>)((tag, image, gw, gh) =>
        {
            if (game.ResolvePsychObject(tag) is not PlayScene.StaticStageProp p || string.IsNullOrEmpty(image)) return;
            string path = game.AssetManager?.ResolvePath($"images/{image}.png");
            if (path == null) { Console.WriteLine($"[FUNKIN-LUA] loadGraphic: image not found: {image}"); return; }
            try { p.Texture = Texture2D.FromFile(FNFGame.Instance.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.Fix(path)); p.Sheet = null; p.SourceRect = null; p.Animations = null; p.CurrentAnimation = null; p.PsychHitW = p.PsychHitH = null; }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] loadGraphic failed: {ex.Message}"); }
        }));
        Action<string, string> loadFrames = (tag, image) =>
        {
            if (game.ResolvePsychObject(tag) is not PlayScene.StaticStageProp p || string.IsNullOrEmpty(image)) return;
            SpriteSheet sheet = null;
            try { sheet = SpriteSheet.Load(FNFGame.Instance, $"images/{image}"); } catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] loadFrames failed: {ex.Message}"); }
            if (sheet == null) { Console.WriteLine($"[FUNKIN-LUA] loadFrames: not found: {image}"); return; }
            p.Sheet = sheet; p.Texture = sheet.Texture; p.SourceRect = null; p.PsychHitW = p.PsychHitH = null; // frames setter: width = frameWidth
            if (p is ModchartSprite ms) { ms.SpriteSheet = sheet; }
        };
        Reg("loadFrames", (Action<string, string, string>)((tag, image, type) => loadFrames(tag, image)));
        Reg("loadMultipleFrames", (Action<string, DynValue>)((tag, images) =>
        {
            if (images.Type == DataType.Table && images.Table.Length > 0) loadFrames(tag, images.Table.Get(1).CastToString());
            else if (images.Type == DataType.String) loadFrames(tag, images.String);
        }));
        Reg("setPropertyLuaSprite", (Action<string, string, DynValue>)((tag, prop, v) => game.SetObjectProperty(tag, prop, v?.ToObject())));
        // FunkinLua addOffset(obj, anim, x, y): obj.addOffset (Character / ModchartSprite animOffsets)
        Reg("addOffset", (Func<string, string, double, double, bool>)((obj, anim, x, y) => game.PsychAddOffset(obj, anim, (float)x, (float)y)));
        // FunkinLua addAnimation(obj, name, frames, framerate=24, loop=true) = LuaUtils.addAnimByIndices(obj, name, null, frames, ..):
        // animation.add(name, frames) - indices into the whole frame list
        Reg("addAnimation", (Func<string, string, DynValue, DynValue, DynValue, bool>)((obj, name, frames, fps, loop) =>
            game.PsychAddAnimationFrames(obj, name, LuaFrameList(frames), fps.IsNil() ? 24 : (float)fps.CastToNumber().GetValueOrDefault(24), loop.IsNil() || loop.CastToBool())));
        // ReflectionFunctions addToGroup(group, tag, index=-1) / removeFromGroup(group, index=-1, tag=null, destroy=true)
        Reg("addToGroup", (Action<string, string, DynValue>)((g, tag, idx) =>
        { var err = game.PsychAddToGroup(g, tag, idx.IsNil() ? -1 : (int)idx.CastToNumber().GetValueOrDefault(-1)); if (err != null) FunkinLua.LuaTrace(err, false, false); }));
        Reg("removeFromGroup", (Action<string, DynValue, DynValue, DynValue>)((g, idx, tag, destroy) =>
        { var err = game.PsychRemoveFromGroup(g, idx.IsNil() ? -1 : (int)idx.CastToNumber().GetValueOrDefault(-1), tag.IsNil() ? null : tag.CastToString()); if (err != null) FunkinLua.LuaTrace(err, false, false); }));
        // DeprecatedFunctions updateHitboxFromGroup(group, index)
        Reg("updateHitboxFromGroup", (Action<string, int>)((g, i) => { game.PsychUpdateHitboxFromGroup(g, i); FunkinLua.LuaTrace("updateHitboxFromGroup is deprecated! Use updateHitbox instead.", false, true); }));

        // ── tweens: startTween(tag, obj, {x=..}, duration, {ease=..}) ──
        // FunkinLua startTween(tag, vars, values, duration, options): one FlxTween over all fields; options = {type, ease,
        // startDelay, loopDelay, onStart, onUpdate, onComplete} (callbacks get [tag key, vars]); returns the tag.
        Reg("startTween", (Func<string, string, DynValue, double, DynValue, string>)((tag, obj, values, duration, options) =>
        {
            object target = LuaTweenManager.ResolveTweenTarget(obj);
            if (target == null) { FunkinLua.LuaTrace("startTween: Couldnt find object: " + obj, false, false); return null; }
            if (values == null || values.Type != DataType.Table) { FunkinLua.LuaTrace("startTween: No values on 2nd argument!", false, false); return null; }
            var vals = new Dictionary<string, double>();
            foreach (var pair in values.Table.Pairs)
                if (pair.Value.Type == DataType.Number) vals[pair.Key.CastToString()] = pair.Value.Number;
            string ease = "", type = null, onS = null, onU = null, onC = null; double sd = 0, ld = 0;
            bool hasOpt = options != null && options.Type == DataType.Table;
            if (hasOpt)
            {
                var t = options.Table;
                if (t.Get("ease").Type == DataType.String) ease = t.Get("ease").String;
                if (t.Get("type").Type == DataType.String) type = t.Get("type").String;
                if (t.Get("startDelay").Type == DataType.Number) sd = t.Get("startDelay").Number;
                if (t.Get("loopDelay").Type == DataType.Number) ld = t.Get("loopDelay").Number;
                if (t.Get("onStart").Type == DataType.String) onS = t.Get("onStart").String;
                if (t.Get("onUpdate").Type == DataType.String) onU = t.Get("onUpdate").String;
                if (t.Get("onComplete").Type == DataType.String) onC = t.Get("onComplete").String;
            }
            LuaTweenManager.StartTweenOn(tag, target, vals, duration, hasOpt ? LuaTweenManager.EaseByString(ease) : null,
                LuaTweenManager.TypeByString(type), sd, ld, onS, onU, onC, obj, true, startTweenStyle: true);
            return tag;
        }));

        // ── text ──
        // FlxText.font after setFormat(Paths.font('vcr.ttf')) is the font family name
        Reg("getTextFont", (Func<string, string>)(t => { var f = game.GetLuaTextObject(t)?.FontName; return f == null ? null : (f.StartsWith("vcr", StringComparison.OrdinalIgnoreCase) ? "VCR OSD Mono" : f); }));
        Reg("getTextSize", (Func<string, int>)(t => game.GetLuaTextObject(t)?.Size ?? -1));
        Reg("setTextAutoSize", (Action<string, bool>)((t, v) => { var o = game.GetLuaTextObject(t); if (o != null) o.AutoSize = v; }));
        Reg("setTextHeight", (Action<string, double>)((t, v) => { var o = game.GetLuaTextObject(t); if (o != null) o.FieldHeight = (float)v; }));
        // TextFunctions setTextItalic(tag, italic): FlxText.italic (the bundled fonts have no italic face, so nothing redraws)
        Reg("setTextItalic", (Func<string, bool, bool>)((tag, it) =>
        {
            if (game.LuaTextItalic(tag, it)) return true;
            FunkinLua.LuaTrace("setTextItalic: Object " + tag + " doesn't exist!", false, false);
            return false;
        }));

        // ── sounds by tag (port Audio has no tagged sound objects yet) ──
        // ── tagged sounds (PlayScene.PsychSounds.cs); playSound is replaced because the base one ignored tag/loop ──
        static string S(DynValue v) => v == null || v.IsNil() ? null : v.CastToString();
        static double N(DynValue v, double d) => v == null || v.IsNil() ? d : (v.CastToNumber() ?? d);
        lua.Globals["playSound"] = (Func<string, DynValue, DynValue, DynValue, string>)((snd, vol, tag, loop) =>
            game.PsychPlaySound(snd, (float)N(vol, 1), S(tag), loop != null && !loop.IsNil() && loop.CastToBool()));
        lua.Globals["stopSound"] = (Action<DynValue>)(t => game.PsychStopSound(S(t)));
        lua.Globals["pauseSound"] = (Action<DynValue>)(t => game.PsychPauseSound(S(t)));
        lua.Globals["resumeSound"] = (Action<DynValue>)(t => game.PsychResumeSound(S(t)));
        lua.Globals["luaSoundExists"] = (Func<DynValue, bool>)(t => game.PsychSoundExists(S(t)));
        lua.Globals["getSoundVolume"] = (Func<DynValue, double>)(t => game.PsychGetSoundVolume(S(t)));
        lua.Globals["setSoundVolume"] = (Action<DynValue, double>)((t, v) => game.PsychSetSoundVolume(S(t), (float)v));
        lua.Globals["getSoundTime"] = (Func<DynValue, double>)(t => game.PsychGetSoundTime(S(t)));
        lua.Globals["setSoundTime"] = (Action<DynValue, double>)((t, v) => { }); // SoundEffectInstance cannot seek
        lua.Globals["getSoundPitch"] = (Func<DynValue, double>)(t => game.PsychGetSoundPitch(S(t)));
        lua.Globals["setSoundPitch"] = (Action<DynValue, double>)((t, v) => game.PsychSetSoundPitch(S(t), (float)v));
        lua.Globals["soundFadeIn"] = (Action<DynValue, double, DynValue, DynValue>)((t, d, from, to) => game.PsychSoundFade(S(t), (float)d, (float)N(from, 0), (float)N(to, 1)));
        lua.Globals["soundFadeOut"] = (Action<DynValue, double, DynValue>)((t, d, to) => game.PsychSoundFade(S(t), (float)d, game.PsychGetSoundVolume(S(t)), (float)N(to, 0)));
        lua.Globals["soundFadeCancel"] = (Action<DynValue>)(t => game.PsychSoundFadeCancel(S(t)));
        foreach (var n in new[] { "musicFadeIn", "musicFadeOut" }) Stub(n);

        // ── flow: dialogue / video / exit. Psych calls startAndEnd() when the file is missing or
        //    unsupported: endSong() if ending, otherwise the countdown continues (already running here).
        Reg("startDialogue", (Func<string, string, bool>)((file, music) => game.PsychLuaStartDialogue(file, music)));
        // startVideo(videoFile, canSkip = true, forMidSong = false): mods/<mod>/videos/<file>.mp4 through VideoCutscenePlayer
        // (H.264/AAC mp4 only; Media Foundation). Missing/unplayable -> Psych fallback (song continues).
        Reg("startVideo", (Func<string, DynValue, DynValue, bool>)((file, canSkip, forMidSong) =>
            game.PsychStartVideo(file, canSkip.IsNil() || canSkip.CastToBool(), !forMidSong.IsNil() && forMidSong.CastToBool())));
        Reg("exitSong", (Action<bool>)(skip => game.EndSong()));

        // ── HScript management / shader arrays ──
        Stub("addHScript"); Stub("removeHScript", DynValue.False); Stub("setOnHScript");
        foreach (var n in new[] { "setShaderBoolArray", "setShaderIntArray", "setShaderFloatArray" }) Stub(n);
        foreach (var n in new[] { "getShaderBoolArray", "getShaderIntArray", "getShaderFloatArray" }) Stub(n);
    }

    private static Rectangle? Bounds(PlayScene game, string tag)
    {
        switch (game.ResolvePsychObject(tag))
        {
            case Character c: return new Rectangle((int)c.X, (int)c.Y, (int)(c.Width * c.Scale), (int)(c.Height * c.Scale));
            case PlayScene.StaticStageProp p: var (w, h) = PlayScene.PsychPropSize(p); return new Rectangle((int)p.X, (int)p.Y, (int)w, (int)h);
            case PsychHudObject o: return new Rectangle((int)o.x, (int)o.y, (int)o.width, (int)o.height);
        }
        return null;
    }

    private static double ScreenPos(PlayScene game, string tag, bool xAxis)
    {
        switch (game.ResolvePsychObject(tag))
        {
            case Character c: { var sf = game.PsychCharScrollFactor(c); return xAxis ? c.X - game.PsychCamScrollX * sf.X : c.Y - game.PsychCamScrollY * sf.Y; } // getScreenPosition: x - scroll * scrollFactor
            case PlayScene.StaticStageProp p: return xAxis ? p.X - game.PsychCamScrollX * p.ScrollX : p.Y - game.PsychCamScrollY * p.ScrollY;
            case PsychHudObject o: return xAxis ? o.x : o.y;
        }
        return 0;
    }
}
