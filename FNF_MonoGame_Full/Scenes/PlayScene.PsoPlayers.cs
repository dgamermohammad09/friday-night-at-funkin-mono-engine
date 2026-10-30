using System;
using System.Collections.Generic;
using System.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;
using FNF_MonoGame.Gameplay;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PNoteS = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

// Psych Online per-player visuals that were missing (ported from psych_online source/):
//  - note colours / note skins of each side (ClientPrefs.getRGBColor / getRGBPixelColor / getNoteSkin(player),
//    Note.hx 188 + getNoteSkinPostfix, StrumNote.hx 43/65): the strumline of our side uses OUR arrowColors / noteSkin
//    (state player.arrowColors["4k"] echoed by the server), the other side uses PlayState.opponentPlayer's - only in a
//    duel (players.length <= 2), else the default colours. Drawn with the RGBPalette shader on Psych's NOTE_assets(-skin)
//    at the V-Slice strumline positions when that side is not the default look;
//  - every player gets a character (PlayState.initPlayCharacter / startCharacterPos 1966: x += ox * (right ? 250 : -250),
//    groups sorted by ox so higher ox is drawn behind), nameplates (1065: vcr 30 white, 1.5 outline, above the character,
//    faded after nameplateFadeTime: 0 = hidden, -1 = never hidden), per-sid sing/miss/charPlay;
//  - vocals: getVocalsFromSIDVolume(sid, 1 on hit / 0 on miss) (opponentNoteHit 5611, onMessage noteMiss 6771),
//    nothing in One Lane Mode (getVocalsFromSID: royalMode -> null).
public partial class PlayScene
{
    // ------------------------------------------------------------------ note colours / skins
    private uint[][][] _psoLineRgb = new uint[2][][];     // [0] = dad line, [1] = bf line; null = default look
    private string[] _psoLineSkin = new string[2];        // Psych sheet key (noteSkins/NOTE_assets[-skin]) or null

    private static uint[][] PsoColorsOf(SchemaObject p, bool pixel)
    {
        var m = p?.Map(pixel ? "arrowColorsPixel" : "arrowColors");
        if (m == null || !m.Items.TryGetValue("4k", out var o) || o is not SchemaObject ca) return null;
        var arr = ca.Array("value");
        if (arr == null || arr.Items.Count < 12) return null;
        var r = new uint[4][];
        for (int l = 0; l < 4; l++)
        {
            r[l] = new uint[3];
            for (int k = 0; k < 3; k++) r[l][k] = unchecked((uint)(long)MsgPack.Num(arr.Items[l * 3 + k]));
        }
        return r;
    }

    private static bool PsoSameColors(uint[][] a, uint[][] b)
    {
        if (a == null || b == null) return a == b;
        for (int l = 0; l < 4; l++) for (int k = 0; k < 3; k++) if ((a[l][k] & 0xFFFFFF) != (b[l][k] & 0xFFFFFF)) return false;
        return true;
    }

    /// <summary>PsoOnLoaded: resolve both lines (ClientPrefs.getRGBColor(0|1) + getNoteSkin(0|1)).</summary>
    private void PsoResolveNoteLooks()
    {
        _psoLineRgb = new uint[2][][]; _psoLineSkin = new string[2];
        if (!_mpPso || PsychOnline.State == null) return;
        bool pixel = _vs?.Pixel ?? false;
        bool duel = (PsychOnline.Players?.Items.Count ?? 0) <= 2;      // PlayState.isDuel
        SchemaObject opp = null;
        if (duel) foreach (var (sid, p) in PsychOnline.AllPlayers()) if (sid != PsychOnline.SessionId) opp = p;
        var defaults = Enumerable.Range(0, 4).Select(PsychEngine_DefaultRgb).ToArray();
        for (int line = 0; line < 2; line++)
        {
            bool bfLine = line == 1;
            bool mine = bfLine == PsychOnline.PlaysAsBF;                // mustPress == playsAsBF -> player 0 (self)
            var p = mine ? PsychOnline.Self : opp;
            uint[][] rgb = p != null ? PsoColorsOf(p, pixel) : null;
            if (mine && rgb == null) rgb = Engine.PsychEngine.ClientPrefs.Data.ArrowRGB;
            string skin = p?.Str("noteSkin") ?? (mine ? Engine.PsychEngine.ClientPrefs.Data.NoteSkin : null);
            string key = null;
            if (!string.IsNullOrEmpty(skin) && !skin.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                key = "noteSkins/NOTE_assets-" + skin.Trim().ToLowerInvariant().Replace(' ', '_');
                if (PsychNoteSheet(key) == null) { Console.WriteLine($"[PSO] note skin '{skin}' not installed - default"); key = null; }
            }
            bool custom = !pixel && (key != null || (rgb != null && !PsoSameColors(rgb, defaults)));
            if (custom) { _psoLineRgb[line] = rgb ?? defaults; _psoLineSkin[line] = key ?? PsychDefaultSkin; }
            Console.WriteLine($"[PSO] note look {(bfLine ? "bf" : "dad")} line ({(mine ? "self" : opp != null ? "opponent " + opp.Str("name") : "default")}): skin={skin ?? "-"} rgb={(rgb == null ? "default" : string.Join(" ", rgb.Select(c => c[0].ToString("X6").Substring(2))))} custom={custom}");
        }
    }

    private bool _soloLooksResolved;

    /// <summary>Solo / local play: Options > Note Skin = a Psych note skin (Chip, Future, mod skins) draws both V-Slice
    /// strumlines with that sheet + the player's Note Colors, like Psych applies its note skin to every strum
    /// (pixel songs keep their pixel art).</summary>
    private void SoloResolveNoteLooks()
    {
        if (_soloLooksResolved) return;
        _soloLooksResolved = true;
        if (_vs == null || _vs.Pixel) return;
        string key = UserNoteSkinKey;
        if (key == PsychDefaultSkin) return;
        var rgb = Engine.PsychEngine.ClientPrefs.Data.ArrowRGB ?? Enumerable.Range(0, 4).Select(PsychEngine_DefaultRgb).ToArray();
        for (int line = 0; line < 2; line++) { _psoLineRgb[line] = rgb; _psoLineSkin[line] = key; }
        Console.WriteLine($"[NOTESKIN] solo note skin {key} on both strumlines");
    }

    private static uint[] PsychEngine_DefaultRgb(int lane) => Engine.PsychEngine.PsychNoteSkin.DefaultRgb(lane);

    /// <summary>Colours of a lane on the bf (true) / dad line in a Psych Online match (Psych-mod note path); null = default.</summary>
    private uint[] PsoRgbFor(int lane, bool bfLine)
    {
        if (!_mpPso) return null;
        var c = _psoLineRgb[bfLine ? 1 : 0];
        if (c != null) return c[lane & 3];
        if (bfLine == PsychOnline.PlaysAsBF) return Engine.PsychEngine.ClientPrefs.Data.ArrowRGB?[lane & 3];
        return null;
    }
    private string PsoSkinFor(bool bfLine) => _mpPso ? _psoLineSkin[bfLine ? 1 : 0] : null;

    /// <summary>VsDrawLine replacement for a line with a custom PSO look: Psych StrumNote / Note / sustain art with the
    /// RGBPalette shader, centred where the V-Slice art would be. Returns false to keep the V-Slice art.</summary>
    private bool PsoDrawPsychLine(SpriteBatch sb, bool player, List<PNoteS> notes)
    {
        if (!_mpPso) SoloResolveNoteLooks();
        if (_vs == null || _psoLineRgb[player ? 1 : 0] == null || (player && VsButtonSkin) || (!_mpPso && VsButtonSkin)) return false;
        var rgb = _psoLineRgb[player ? 1 : 0]; string key = _psoLineSkin[player ? 1 : 0];
        var sh = PsychNoteSheet(key);
        if (sh == null) return false;
        const float s = 0.7f;
        // strums (StrumNote: static without RGB, press/confirm with it)
        for (int lane = 0; lane < 4; lane++)
        {
            var mod = StrumMods[lane + (player ? 4 : 0)];
            if (!mod.Visible || mod.Alpha <= 0.01f) continue;
            var st = _vsStrum[lane + (player ? 4 : 0)];
            float ft = Math.Clamp((_vsIntroT - (0.5f + 0.2f * lane)) / 1f, 0f, 1f);
            float fe = MathF.Sqrt(1f - (ft - 1f) * (ft - 1f));
            float cx = VsStrumX(lane, player) + mod.OffsetX + _vs.StrumW / 2f, cy = VsStrumY() + mod.OffsetY - 10f * (1f - fe) + _vs.StrumH / 2f;
            var stat = PsychAnim(key, "arrow" + PsychDirs[lane]);
            if (stat == null) continue;
            List<SpriteFrame> frames = stat; bool lit = false;
            if (st.Anim == VS_CONFIRM || st.Anim == VS_CONFIRM_HOLD) { var c = PsychAnim(key, PsychDirs[lane].ToLowerInvariant() + " confirm"); if (c != null) { frames = c; lit = true; } }
            else if (st.Anim == VS_PRESS) { var p = PsychAnim(key, PsychDirs[lane].ToLowerInvariant() + " press"); if (p != null) { frames = p; lit = true; } }
            var fr = frames[lit ? Math.Clamp((int)(st.T * 24f), 0, frames.Count - 1) : 0];
            bool fx = lit && PsychBeginRgb(sb, rgb[lane]);
            PsoDrawCentred(sb, sh, fr, cx, cy, s, mod.Alpha * fe);
            PsychEndRgb(sb, fx);
        }
        if (_countdownActive) { VsDrawCoversAndSplashes(sb, player); return true; }
        double posMs = _conductor.SongPosition * 1000.0;
        float speed = _chart.Speed, dirSign = _downscroll ? 1f : -1f;
        float lineX = VsLineX(player), lineY = VsLineY();
        // sustains: piece stretched from the note centre, end cap at the tail (Note.hx sustain pieces, alpha = multAlpha)
        foreach (var note in notes)
        {
            if (note.IsPlayerNote != player || note.SustainLength <= 0) continue;
            int lane = note.Lane % 4;
            var mod = StrumMods[lane + (player ? 4 : 0)];
            float alpha = (note.Alpha * note.MultAlpha) * PsychStrumAlphaFor(note) * HoldTrailMul;
            if (alpha <= 0) continue;
            double tMs = note.Time * 1000.0, endMs = tMs + note.SustainLength * 1000.0;
            double startMs = note.IsHit && posMs > tMs ? posMs : tMs;
            if (endMs - startMs <= 10) continue;
            float cx = lineX + lane * VS_NOTE_SPACING + VS_STRUMLINE_SIZE / 2f + mod.OffsetX + note.OffsetX;
            float yHead = (float)(lineY - VS_INITIAL_OFFSET + VS_PIXELS_PER_MS * (posMs - startMs) * speed * dirSign) + mod.OffsetY + note.OffsetY + _vs.NoteH / 2f;
            float yTail = (float)(lineY - VS_INITIAL_OFFSET + VS_PIXELS_PER_MS * (posMs - endMs) * speed * dirSign) + mod.OffsetY + note.OffsetY + _vs.NoteH / 2f;
            var piece = PsychAnim(key, Engine.PsychEngine.PsychNoteSkin.ColArray[lane] + " hold piece");
            var endA = PsychAnim(key, Engine.PsychEngine.PsychNoteSkin.ColArray[lane] + " hold end");
            if (piece == null || endA == null) continue;
            float pw = FW(piece[0]) * s, eh = FH(endA[0]) * s, ew = FW(endA[0]) * s;
            float top = Math.Min(yHead, yTail), bot = Math.Max(yHead, yTail);
            bool fx = PsychBeginRgb(sb, rgb[lane]);
            var col = new Color(Color.White, Math.Clamp(alpha * 0.6f / 0.6f, 0f, 1f));
            if (!_downscroll)
            {
                float bodyBot = Math.Max(top, bot - eh);
                if (bodyBot > top) sb.Draw(piece[0].Texture ?? sh.Texture, new Rectangle((int)MathF.Round(cx - pw / 2f), (int)MathF.Round(top), (int)MathF.Round(pw), (int)MathF.Round(bodyBot - top)), piece[0].SourceRect, col);
                sb.Draw(endA[0].Texture ?? sh.Texture, new Rectangle((int)MathF.Round(cx - ew / 2f), (int)MathF.Round(bodyBot), (int)MathF.Round(ew), (int)MathF.Round(Math.Min(eh, bot - top))), endA[0].SourceRect, col);
            }
            else
            {
                float bodyTop = Math.Min(bot, top + eh);
                if (bot > bodyTop) sb.Draw(piece[0].Texture ?? sh.Texture, new Rectangle((int)MathF.Round(cx - pw / 2f), (int)MathF.Round(bodyTop), (int)MathF.Round(pw), (int)MathF.Round(bot - bodyTop)), piece[0].SourceRect, col);
                sb.Draw(endA[0].Texture ?? sh.Texture, new Rectangle((int)MathF.Round(cx - ew / 2f), (int)MathF.Round(top), (int)MathF.Round(ew), (int)MathF.Round(Math.Min(eh, bot - top))), endA[0].SourceRect, col, 0f, Vector2.Zero, SpriteEffects.FlipVertically, 0f);
            }
            PsychEndRgb(sb, fx);
        }
        // note heads
        foreach (var note in notes)
        {
            if (note.IsPlayerNote != player || note.IsHit) continue;
            int lane = note.Lane % 4;
            var mod = StrumMods[lane + (player ? 4 : 0)];
            float alpha = (note.Alpha * note.MultAlpha) * PsychStrumAlphaFor(note);
            if (alpha <= 0) continue;
            float nx = lineX + lane * VS_NOTE_SPACING - (_vs.NoteW - VS_STRUMLINE_SIZE) / 2f - VS_NUDGE + mod.OffsetX + note.OffsetX;
            float ny = (float)(lineY - VS_INITIAL_OFFSET + VS_PIXELS_PER_MS * (posMs - note.Time * 1000.0) * speed * dirSign) + mod.OffsetY + note.OffsetY;
            if (ny > FNFGame.SCREEN_HEIGHT + 50 || ny < -_vs.NoteH - 50) continue;
            note.Position = new Vector2(nx, ny);
            var head = PsychAnim(key, Engine.PsychEngine.PsychNoteSkin.ColArray[lane]);
            if (head == null) continue;
            bool fx = PsychBeginRgb(sb, rgb[lane]);
            PsoDrawCentred(sb, sh, head[0], nx + _vs.NoteW / 2f, ny + _vs.NoteH / 2f, s, alpha);
            PsychEndRgb(sb, fx);
        }
        VsDrawCoversAndSplashes(sb, player);
        return true;
    }

    private static void PsoDrawCentred(SpriteBatch sb, SpriteSheet sh, SpriteFrame fr, float cx, float cy, float s, float alpha)
    {
        var pos = new Vector2(cx - FW(fr) / 2f * s + fr.Offset.X * s, cy - FH(fr) / 2f * s + fr.Offset.Y * s);
        if (!fr.Rotated) sb.Draw(fr.Texture ?? sh.Texture, pos, fr.SourceRect, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        else sb.Draw(fr.Texture ?? sh.Texture, new Vector2(pos.X, pos.Y + fr.SourceRect.Width * s), fr.SourceRect, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)), -MathF.PI / 2f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    // ------------------------------------------------------------------ extra players' characters + nameplates
    private sealed class PsoPlayerChar { public string Sid, Name; public bool Bf; public int Ox; public Character Char; public bool Main; }
    private readonly List<PsoPlayerChar> _psoChars = new();
    private float _psoNameplateT;
    private bool _psoBeatHooked;

    /// <summary>PsoOnLoaded: a character for every player (ox &gt; 0 = extra, built like initPlayCharacter), nameplates.</summary>
    private void PsoInitPlayers()
    {
        _psoChars.Clear(); _psoNameplateT = 0f;
        if (!_mpPso || PsychOnline.State == null) return;
        foreach (var (sid, p) in PsychOnline.AllPlayers())
        {
            bool bf = p.Bool("bfSide");                 // initPlayCharacter(player.bfSide, ...)
            int ox = (int)p.Num("ox");
            var main = bf ? _boyfriend : _opponent;
            var e = new PsoPlayerChar { Sid = sid, Name = p.Str("name") ?? "?", Bf = bf, Ox = ox, Main = ox == 0 };
            if (ox == 0 || main == null) { e.Char = main; _psoChars.Add(e); continue; }
            try
            {
                string name = main.CurCharacter;
                var skin = PsychOnline.SkinOf(p);
                string prevMod = Engine.PsychEngine.PsychMods.CurrentModDirectory; bool prevActive = Character.PsychStageActive;
                if (skin != null && skin.Count >= 4)
                {
                    string sk = skin[0] + skin[bf ? 2 : 1];
                    if (!name.StartsWith(skin[0], StringComparison.OrdinalIgnoreCase) && PsychOnline.CharacterExists(sk, skin[3]))
                    { name = sk; Engine.PsychEngine.PsychMods.CurrentModDirectory = skin[3]; Character.PsychStageActive = true; }
                }
                var c = new Character(ResolveCharacterSprite(name), 0, 0);
                c.LoadJsonData(Assets, name);
                c.SetFlipForRole(bf);
                c.StepCrochet = (float)_conductor.StepCrochet;
                c.LoadSprites(Game);
                Engine.PsychEngine.PsychMods.CurrentModDirectory = prevMod; Character.PsychStageActive = prevActive;
                c.StageUsesPsychPositioning = main.StageUsesPsychPositioning;
                c.Scale *= bf ? _pendingBfScale : _pendingDadScale;
                var slot = bf ? _pendingBfPos : _pendingDadPos;
                if (name == main.CurCharacter || slot == null) c.Position = main.Position;
                else c.ApplyStagePosition(slot);
                // startCharacterPos: x += ox * (char.isPlayer == playsAsBF() ? 250 : -250) = away from the centre
                c.X += ox * (bf ? 250f : -250f);
                c.PlayAnimation("idle", force: true);
                e.Char = c;
                Console.WriteLine($"[PSO] player {e.Name} ox {ox} {(bf ? "bf" : "dad")} side -> extra character {name} at ({c.X:0},{c.Y:0})");
            }
            catch (Exception ex) { Console.WriteLine($"[PSO] extra character for {e.Name} failed: {ex.Message}"); }
            _psoChars.Add(e);
        }
        if (!_psoBeatHooked && _psoChars.Any(x => !x.Main))
        {
            _psoBeatHooked = true;
            _conductor.OnBeat += b => { foreach (var x in _psoChars) if (!x.Main) x.Char?.Dance(b); };
        }
    }

    private Character PsoCharOf(string sid)
    {
        foreach (var x in _psoChars) if (x.Sid == sid && !x.Main) return x.Char;
        return null;
    }

    private void PsoPlayersUpdate(float dt)
    {
        _psoNameplateT += dt;
        foreach (var x in _psoChars) if (!x.Main) x.Char?.Update(dt);
    }

    /// <summary>Extra characters of a side, drawn just before that side's main character (higher ox further behind).</summary>
    private void PsoDrawExtraChars(SpriteBatch sb, bool bf)
    {
        if (_psoChars.Count == 0) return;
        var sc = bf ? _bfCharScroll : _dadCharScroll;
        foreach (var x in _psoChars.Where(x => !x.Main && x.Bf == bf && x.Char != null).OrderByDescending(x => x.Ox))
            DrawStageCharacter(sb, bf ? 0 : 1, x.Char, WorldToScreen(x.Char.Position.X, x.Char.Position.Y, sc.X, sc.Y));
    }

    /// <summary>Nameplates (PlayState 1065): vcr 30, white, 1.5 px black outline, centred 20 px above each character.</summary>
    private void PsoDrawNameplates(SpriteBatch sb)
    {
        float fade = Engine.PsychEngine.ClientPrefs.Data.NameplateFadeTime;
        if (fade == 0f) return;
        float a = fade < 0 ? 1f : Math.Clamp(1f - (_psoNameplateT - fade), 0f, 1f);
        if (a <= 0f) return;
        foreach (var x in _psoChars)
        {
            var c = x.Char; if (c == null || !c.Visible) continue;
            var sc = x.Bf ? _bfCharScroll : _dadCharScroll;
            float w = MenuUI.FlxTextWidth(30, x.Name);
            var p = WorldToScreen(c.Position.X + c.Width * c.Scale / 2f - w / 2f, c.Position.Y - 34f - 20f, sc.X, sc.Y);
            DrawFlxOutlinedText(sb, 30, x.Name, p, new Color(Color.White, a), new Color(Color.Black, a));
        }
    }

    // ------------------------------------------------------------------ vocals
    /// <summary>getVocalsFromSIDVolume: the vocal track of the sender's side (bf = player track, dad = opponent track or the
    /// single track); nothing in One Lane Mode.</summary>
    private void PsoVocals(string sid, float v)
    {
        if (!_mpPso || PsychOnline.State == null || PsychOnline.State.Bool("royalMode")) return;
        bool bf = sid == null ? PsychOnline.PlaysAsBF : PsoSideOf(sid);
        if (!bf && Audio.HasOpponentVoice) Audio.SetOpponentVoiceVolume(v);
        else Audio.SetPlayerVoiceVolume(v);
    }
}
