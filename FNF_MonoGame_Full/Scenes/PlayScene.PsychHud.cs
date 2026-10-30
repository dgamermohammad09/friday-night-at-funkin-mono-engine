using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 gameplay HUD for Psych-mod songs (states/PlayState.hx create 481-564, update 1695-1756,
/// updateIconsScale/Position 1873-1890, updateScoreText 1146, RecalculateRating; objects/Bar.hx, objects/HealthIcon.hx):
///  healthBar = Bar(0, 720*0.89, 'healthBar') screenCenter(X), leftToRight=false, leftBar = dad colour, rightBar = bf colour
///  (char JSON healthbar_colors), barOffset 3,3; barCenter = bg.x + leftSize + 3.
///  iconP1.x = barCenter + (150*s-150)/2 - 26, iconP2.x = barCenter - 150*s/2 - 52, y = bar.y - 75; scale 1.2 on beat,
///  lerp back exp(-elapsed*9). Frames: P1 losing when percent < 20, P2 when percent > 80.
///  scoreTxt FlxText(0, bar.y+40, 1280, 20) centred, OUTLINE 1.25; timeTxt FlxText(434, 19, 400, 32) border 2 = remaining m:ss;
///  timeBar = Bar(0, timeTxt.y + timeTxt.height/4, 'timeBar') white/black, both fade in (alpha 0->1, 0.5 s circOut) at song start;
///  botplayTxt FlxText(400, bar.y-90, 480, "BOTPLAY", 32) border 1.25, alpha = 1 - sin(pi*sine/180), sine += 180*elapsed.
/// </summary>
public partial class PlayScene
{
    private Texture2D _psychHealthBarTex, _psychTimeBarTex;
    private float _psychBotSine, _psychTimeAlphaT = -1f;
    // iconP1/iconP2.scale (scale.set(mult, mult) in updateIconsScale; scripts may set x/y separately)
    private float _psychIconScale1 { get { EnsurePsychHud(); return _pIconP1.scaleX; } set { EnsurePsychHud(); _pIconP1.scaleX = _pIconP1.scaleY = value; } }
    private float _psychIconScale2 { get { EnsurePsychHud(); return _pIconP2.scaleX; } set { EnsurePsychHud(); _pIconP2.scaleX = _pIconP2.scaleY = value; } }
    private string _psychLastScore;
    private PsychHudObject _pBotplayTxt;
    private static readonly bool s_devHideBotTxt = System.Environment.GetEnvironmentVariable("FNF_DEV_HIDEBOTTXT") == "1";   // dev: clean screenshots
    public PsychHudObject botplayTxt { get { EnsurePsychHud(); return _pBotplayTxt ??= new PsychHudObject("botplayTxt", 400f, (_downscroll ? 720f * 0.11f : 720f * 0.89f) - 90f, 480f, 32f) { visible = _botplay, text = "BOTPLAY" }; } }

    private bool UsePsychHud => PsychNotes && !_mpActive;
    /// <summary>Strum x: Psych StrumNote x = STRUM_X (42) + 50 + swagWidth*i (+640 for the player); V-Slice 0.5*swagWidth.</summary>
    private int StrumXOffset => PsychNotes ? 42 + 50 : STRUMLINE_X_OFFSET;

    private Texture2D LoadPsychUiTexture(string name)
    {
        // Paths.image: the mod's images/<name>.png first, then Psych's shared image (Content/images/psych)
        try
        {
            foreach (var root in new[] { _modPath, PsychModsRootFolder() })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string p = Path.Combine(root, "images", name + ".png");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(p); return Texture2D.FromStream(Game.GraphicsDevice, fs); }
            }
        }
        catch (Exception e) { Console.WriteLine($"[PSYCH-HUD] {name}: {e.Message}"); }
        return Assets.LoadTexture("images/psych/" + name);
    }

    private static string PsychModsRootFolder()
    {
        string p = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.GameRoot, "mods");
        return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(p) ? p : null;
    }

    /// <summary>beatHit: iconP1/P2.scale.set(1.2, 1.2).</summary>
    internal void PsychHudBeat() { _psychIconScale1 = _psychIconScale2 = 1.2f; }

    /// <summary>Per frame, before onUpdatePost (scripts may move icons afterwards, as in Psych).</summary>
    private void PsychHudUpdate(float dt)
    {
        if (!PsychNotes) return;
        UpdateHxFlixel(dt);
        UpdatePsychCameras(dt);
        if (!UsePsychHud) return;
        EnsurePsychHud();
        float k = MathF.Exp(-dt * 9f);
        _psychIconScale1 = MathHelper.Lerp(1f, _psychIconScale1, k);
        _psychIconScale2 = MathHelper.Lerp(1f, _psychIconScale2, k);
        float pct = Math.Clamp(_health, 0f, 2f) / 2f * 100f;
        float barW = (_psychHealthBarTex?.Width ?? 601) - 6;
        float leftSize = MathHelper.Lerp(0f, barW, 1f - pct / 100f);
        float barCenter = _pHealthBar.x + leftSize + 3f;
        _pIconP1.x = barCenter + (150f * _psychIconScale1 - 150f) / 2f - 26f;
        _pIconP2.x = barCenter - (150f * _psychIconScale2) / 2f - 26f * 2f;
        if (_pBotplayTxt != null || _botplay)
        {
            var b = botplayTxt; // visible = cpuControlled only at create (scripts may hide it)
            if (b.visible) { _psychBotSine += 180f * dt; b.alpha = 1f - MathF.Sin(MathF.PI * _psychBotSine / 180f); }
        }
        if (_psychTimeAlphaT < 0f) { _pTimeBar.alpha = _pTimeTxt.alpha = 0f; if (_conductor.SongPosition > 0) _psychTimeAlphaT = 0f; }
        if (_psychTimeAlphaT >= 0f && _psychTimeAlphaT < 0.5f)
        {
            _psychTimeAlphaT += dt;
            float t = Math.Min(1f, _psychTimeAlphaT / 0.5f);
            float a = MathF.Sqrt(1f - (t - 1f) * (t - 1f)); // circOut
            _pTimeBar.alpha = _pTimeTxt.alpha = a;
        }
    }

    private static Color PsychCharColor(Gameplay.Character c)
    {
        string h = c?.JsonData?.HealthBarColor;
        if (!string.IsNullOrEmpty(h) && h.StartsWith("#") && h.Length >= 7
            && int.TryParse(h.Substring(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return new Color((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        return new Color(161, 161, 161);
    }

    private string PsychScoreText()
    {
        int played = _tallySick + _tallyGood + _tallyBad + _tallyShit + _misses;
        string str;
        if (_botplay || played == 0) str = "?";
        else
        {
            double pctD = Math.Clamp((_tallySick + _tallyGood * 0.67 + _tallyBad * 0.34) / played, 0, 1);
            string name = "Perfect!!";
            if (pctD < 1)
            {
                (string n, double v)[] rs = { ("You Suck!", 0.2), ("Shit", 0.4), ("Bad", 0.5), ("Bruh", 0.6), ("Meh", 0.69), ("Nice", 0.7), ("Good", 0.8), ("Great", 0.9), ("Sick!", 1) };
                foreach (var r in rs) if (pctD < r.v) { name = r.n; break; }
            }
            string fc = _misses == 0 ? (_tallyBad > 0 || _tallyShit > 0 ? "FC" : _tallyGood > 0 ? "GFC" : _tallySick > 0 ? "SFC" : "")
                                     : (_misses < 10 ? "SDCB" : "Clear");
            double floor2 = Math.Floor(pctD * 100 * 100) / 100;
            str = $"{name} ({floor2.ToString(CultureInfo.InvariantCulture)}%) - {fc}";
        }
        return $"Score: {_score} | Misses: {_misses} | Rating: {str}";
    }

    /// <summary>FlxText: box (x, y, width), alignment centre, OUTLINE border (flixel: iterations = int(borderSize), delta = size/iter).</summary>
    private void DrawPsychText(SpriteBatch sb, int size, string text, float x, float y, float boxW, Color color, float borderSize, float alpha, float scale = 1f)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0f) return;
        var font = Assets.GetFont(size);
        if (font == null) return;
        float adv = MathF.Round(size * 1200f / 2048f);
        float textW = text.Length * adv;
        float startX = boxW > 0 ? x + 2f + MathF.Floor((boxW - 4f - textW) / 2f) : x + 2f;
        float py = y + 2f + FlxTextYOffset;
        int iters = Math.Max(1, (int)borderSize);
        float delta = borderSize / iters;
        var border = new Color(Color.Black, alpha);
        var fill = new Color(color, alpha);
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ') continue;
                string ch = text[i].ToString();
                var p = new Vector2(MathF.Round(startX + i * adv), MathF.Round(py)); if (scale != 1f) { var c = new Vector2(x + boxW / 2f, y + (size + 4f) / 2f); p = c + (p - c) * scale; }
                if (pass == 0)
                {
                    for (int it = 1; it <= iters; it++)
                    {
                        float d = delta * it;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (dx != 0 || dy != 0) font.DrawText(sb, ch, p + new Vector2(dx * d, dy * d) * scale, border, scale: new Vector2(scale));
                    }
                }
                else font.DrawText(sb, ch, p, fill, scale: new Vector2(scale));
            }
    }

    private static readonly BlendState PsychAddStraight = new()
    {
        ColorSourceBlend = Blend.SourceAlpha, AlphaSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.One, AlphaDestinationBlend = Blend.One,
    };

    /// <summary>objects/Bar.hx draw: children leftBar, rightBar (makeGraphic white, tinted, clipRect in frame pixels:
    /// left [3, 3+leftSize], right [3+leftSize, 3+barW], y [3, 3+barH]) and bg, all at the group's x/y. Each child is a
    /// flixel FlxSprite: screen = x - offset + origin + (framePos - origin) * scale; flipX mirrors inside the frame
    /// (clipped frames keep their full size). barW/barH stay bg size - 6 (set at creation, not after scaleObject).</summary>
    private void DrawPsychBar(SpriteBatch sb, Texture2D bg, PsychHudObject o, float percent, bool leftToRight, Color left, Color right, float alpha)
    {
        if (alpha <= 0f) return;
        int w = bg?.Width ?? 601, h = bg?.Height ?? 19;
        int barW = w - 6, barH = h - 6;
        float leftSize = leftToRight ? MathHelper.Lerp(0, barW, percent / 100f) : MathHelper.Lerp(0, barW, 1f - percent / 100f);
        float sx = o.scaleX, sy = o.scaleY;
        float ox = o.originX ?? w / 2f, oy = o.originY ?? h / 2f;
        float bx = o.x - o.offsetX + ox, by = o.y - o.offsetY + oy; // screen point of the frame origin
        void Rect(float a, float b, Color c)
        {
            if (b - a <= 0f) return;
            if (o.flipX) { float t = w - b; b = w - a; a = t; }
            sb.Draw(Assets.Pixel, new Vector2(bx + (a - ox) * sx, by + (3f - oy) * sy), null, c, 0f, Vector2.Zero,
                new Vector2((b - a) * sx, barH * sy), SpriteEffects.None, 0f);
        }
        Rect(3f, 3f + leftSize, new Color(left, alpha));
        Rect(3f + leftSize, 3f + barW, new Color(right, alpha));
        if (bg != null && bg != Assets.Pixel)
            sb.Draw(bg, new Vector2(bx - ox * sx, by - oy * sy), null, new Color(Color.White, alpha), 0f, Vector2.Zero,
                new Vector2(sx, sy), o.flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    private static bool PsychIsAdd(PsychHudObject o) => o.blend != null && string.Equals(o.blend.ToString().Trim(), "add", StringComparison.OrdinalIgnoreCase);
    private static bool PsychOnOtherCam(PsychHudObject o) =>
        o.camera == FNF_MonoGame.Engine.PsychEngine.PsychCamera.OtherCam
        || (o.camera is string cs && (string.Equals(cs, "other", StringComparison.OrdinalIgnoreCase) || string.Equals(cs, "camOther", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Health/time bar in the current pass (camHUD batch, or the camOther pass); ADD blend needs its own batch.</summary>
    private void DrawPsychBarPass(SpriteBatch sb, bool otherPass, Matrix batchMatrix)
    {
        foreach (var isTime in new[] { true, false })
        {
            var o = isTime ? _pTimeBar : _pHealthBar;
            if (o == null || !o.visible || o.alpha <= 0f || PsychOnOtherCam(o) != otherPass) continue;
            bool add = PsychIsAdd(o);
            if (add) { sb.End(); sb.Begin(samplerState: SamplerState.LinearClamp, blendState: PsychAddStraight, transformMatrix: batchMatrix); }
            if (isTime)
            {
                double len = Audio.MusicLength;
                float songPercent = len > 0 ? (float)Math.Clamp(_conductor.SongPosition / len, 0, 1) : 0f;
                DrawPsychBar(sb, _psychTimeBarTex, o, songPercent * 100f, true, _timeBarColorOverride ? _timeBarLeftColor : Color.White, _timeBarColorOverride ? _timeBarRightColor : Color.Black, o.alpha);
            }
            else
            {
                float pct = Math.Clamp(_health, 0f, 2f) / 2f * 100f;
                DrawPsychBar(sb, _psychHealthBarTex, o, pct, false, _healthBarColorOverride ? _healthBarOpponentColor : PsychCharColor(_opponent), _healthBarColorOverride ? _healthBarPlayerColor : PsychCharColor(_boyfriend), o.alpha);
            }
            if (add) { sb.End(); sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: batchMatrix); }
        }
    }

    /// <summary>camOther pass (flixel draws camOther after camHUD, at zoom 1): HUD bars a script moved to camOther.</summary>
    private bool _barDbg;
    internal void DrawPsychOtherCamBars(SpriteBatch sb)
    {
        if (!_barDbg && Environment.GetEnvironmentVariable("FNF_BARLOG") == "1" && _pHealthBar != null) { _barDbg = true; Console.WriteLine($"[BARLOG] hb cam={_pHealthBar.camera?.GetType().Name}:{_pHealthBar.camera} other={PsychOnOtherCam(_pHealthBar)} vis={_pHealthBar.visible} a={_pHealthBar.alpha} pos={_pHealthBar.x},{_pHealthBar.y} sc={_pHealthBar.scaleX},{_pHealthBar.scaleY} off={_pHealthBar.offsetX},{_pHealthBar.offsetY} flip={_pHealthBar.flipX} blend={_pHealthBar.blend} | tb cam={_pTimeBar.camera} vis={_pTimeBar.visible} a={_pTimeBar.alpha} pos={_pTimeBar.x},{_pTimeBar.y} sc={_pTimeBar.scaleX} | other vis={FNF_MonoGame.Engine.PsychEngine.PsychCamera.OtherCam.Visible} ui={UsePsychHud}"); }
        if (!UsePsychHud || _pHealthBar == null) return;
        _psychHealthBarTex ??= LoadPsychUiTexture("healthBar");
        _psychTimeBarTex ??= LoadPsychUiTexture("timeBar");
        if (!PsychOnOtherCam(_pHealthBar) && !PsychOnOtherCam(_pTimeBar)) return;
        var oc = FNF_MonoGame.Engine.PsychEngine.PsychCamera.OtherCam;
        if (!oc.Visible || oc.Alpha <= 0f) return;
        var m = PsychCamTransform(oc, Vector2.Zero);
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m);
        DrawPsychBarPass(sb, true, m);
        sb.End();
    }

    private void DrawPsychIcon(SpriteBatch sb, Texture2D icon, PsychHudObject o, bool losing, bool flip)
    {
        if (icon == null || icon == Assets.Pixel || o.Opacity <= 0f) return;
        int iSize = Math.Max(1, (int)Math.Round(icon.Width / (double)icon.Height));
        int fw = icon.Width / iSize, fh = icon.Height;
        int frame = losing && iSize > 1 ? 1 : 0;
        float offX = (fw - 150f) / iSize, offY = (fh - 150f) / iSize; // HealthIcon.updateHitbox: offset = iconOffsets
        // flixel draw: x - offset + origin*(1 - scale), origin = frame centre
        float sx = o.scaleX, sy = o.scaleY;
        float dx = o.x - offX + fw / 2f * (1f - sx), dy = o.y - offY + fh / 2f * (1f - sy);
        sb.Draw(icon, new Vector2(dx, dy), new Rectangle(frame * fw, 0, fw, fh), new Color(Color.White, o.Opacity), 0f, Vector2.Zero,
            new Vector2(sx, sy), flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    private void DrawHudPsych(SpriteBatch sb)
    {
        EnsurePsychHud();
        _psychHealthBarTex ??= LoadPsychUiTexture("healthBar");
        _psychTimeBarTex ??= LoadPsychUiTexture("timeBar");
        // time bar + text (only shown once the song started); bars on camOther are drawn in DrawPsychOtherCamBars
        DrawPsychBarPass(sb, false, _hudBatchMatrix);
        if (_pTimeTxt.visible && _pTimeTxt.alpha > 0f)
        {
            double len = Audio.MusicLength;
            int secs = Math.Max(0, (int)Math.Floor(len - Math.Max(0, _conductor.SongPosition)));
            { string tt = PsychTimeText(out int ts); DrawPsychText(sb, ts, tt, _pTimeTxt.x, _pTimeTxt.y, 400f, Color.White, 2f, _pTimeTxt.alpha); }
        }
        float pct = Math.Clamp(_health, 0f, 2f) / 2f * 100f;
        DrawPsychIcon(sb, _playerIcon, _pIconP1, pct < 20f, true);
        DrawPsychIcon(sb, _opponentIcon, _pIconP2, pct > 80f, false);
        // updateScoreText runs only when score/misses/rating change (RecalculateRating), so a script's setTextString stays
        if (!PsychNotes) { string sc = PsychScoreText(); if (sc != _psychLastScore) { _psychLastScore = sc; _pScoreTxt.text = sc; } }
        else if (_psScoreTextDirty) { _psScoreTextDirty = false; _pScoreTxt.text = PsychScoreTextPsych(); } // updateScoreText only from updateScore
        if (_pScoreTxt.visible) DrawPsychText(sb, 20, _pScoreTxt.text, _pScoreTxt.x, _pScoreTxt.y, 1280f, Color.White, 1.25f, _pScoreTxt.alpha, PsychScoreScale);
        if (_pBotplayTxt != null && _pBotplayTxt.visible && !s_devHideBotTxt) DrawPsychText(sb, 32, _pBotplayTxt.text, _pBotplayTxt.x, _pBotplayTxt.y, 480f, Color.White, 1.25f, Math.Clamp(_pBotplayTxt.alpha, 0f, 1f));
    }
}
