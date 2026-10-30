using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using PNoteV = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options > Note Skin = Controller Buttons for Psych-mod songs (port option; default Arrows = native Psych look).
/// Same rules as the V-Slice strumline (PlayScene.VSliceStrumline.cs VsDrawStrum): only the PLAYER strums/notes become
/// Xbox button icons, centred on the image Psych would draw (StrumNote / Note at 0.7 scale, Lua x/y/alpha moves and
/// note scale/angle included) and sized to the static arrow; the opponent side, custom-texture notes and notes with
/// non-default colours (Hurt Note, script-recoloured notes) keep their Psych art so their meaning stays visible.
/// </summary>
public partial class PlayScene
{
    private int _psychButtonSize;
    /// <summary>Scene-RT pixels per HUD unit (HUD zoom): the controller skin picks its 2x art when drawn above ~135 px.</summary>
    private float HudArtScale { get { float m = Math.Abs(_hudBatchMatrix.M11); return m > 0.01f ? m : 1f; } }

    /// <summary>Button icon size = drawn size (min of w/h) of Psych's static 'arrowLEFT' at setGraphicSize(width * 0.7).</summary>
    private int PsychButtonSize()
    {
        if (_psychButtonSize > 0) return _psychButtonSize;
        var stat = PsychAnim(PsychDefaultSkin, "arrowLEFT");
        if (stat == null || stat.Count == 0) return (int)(STRUMLINE_SIZE * 0.75f);
        var f = stat[0];
        float s = (float)Math.Floor(FW(f) * 0.7f) / FW(f);
        return _psychButtonSize = Math.Max(8, (int)MathF.Round(Math.Min(f.SourceRect.Width, f.SourceRect.Height) * s));
    }

    /// <summary>True when this Psych note is drawn as a controller button (player note with the default look).</summary>
    private bool PsychButtonNote(PNoteV note)
    {
        if (!PsychNotes || note == null || !ButtonsFor(note.IsPlayerNote)) return false;   // local player side(s) only
        int lane = note.Lane;
        if (lane < 0 || lane > 3) return false;
        if (!string.IsNullOrEmpty(note.Texture) || note.Data.RgbDisabled) return false;
        var rgb = note.Data.Rgb;
        if (rgb != null)
        {
            var d = PsychNoteSkin.DefaultRgb(lane);
            for (int i = 0; i < 3 && i < rgb.Length; i++) if (rgb[i] != d[i]) return false;
        }
        return true;
    }

    /// <summary>Player strum as a button: centred on the static arrow's drawn image (x,y = Psych strum.x/y).</summary>
    private bool PsychDrawButtonStrum(SpriteBatch sb, int x, int y, int lane, bool pressed, bool confirm)
    {
        float cx, cy;
        var stat = PsychAnim(PsychDefaultSkin, "arrow" + PsychDirs[lane]);
        if (stat != null && stat.Count > 0)
        {
            var f = stat[0];
            float w = (float)Math.Floor(FW(f) * 0.7f), s = w / FW(f), h = FH(f) * s;
            // trimmed image box = strum hitbox centre - frame/2*s + frameOffset*s, size SourceRect*s
            cx = x + w / 2f - FW(f) / 2f * s + f.Offset.X * s + f.SourceRect.Width * s / 2f;
            cy = y + h / 2f - FH(f) / 2f * s + f.Offset.Y * s + f.SourceRect.Height * s / 2f;
        }
        else { cx = x + STRUMLINE_SIZE / 2f; cy = y + STRUMLINE_SIZE / 2f; }
        int size = PsychButtonSize();
        float a = Math.Clamp(StrumMods[lane + 4].Alpha, 0f, 1f);
        if (ControllerNoteSkin.DrawReceptor(sb, Assets, lane, Input.NoteFaceButtons[lane], new Vector2(cx, cy), size, pressed && !confirm, confirm, a, -1f, HudArtScale)) return true;
        Assets.DrawButtonReceptor(sb, Input.NoteFaceButtons[lane], (int)MathF.Round(cx - size / 2f), (int)MathF.Round(cy - size / 2f), size,
            pressed: pressed && !confirm, confirm: confirm, alpha: a);
        return true;
    }

    /// <summary>Player note head as a button, centred where PsychDrawNoteHead draws the default note image.</summary>
    private bool PsychDrawButtonNote(SpriteBatch sb, int x, int y, int lane, PNoteV note, float alpha, float angle, float scale)
    {
        var tex = Assets.GetButtonSprite(Input.NoteFaceButtons[lane]);
        if (tex == null || tex == Assets.Pixel) return false;
        float cx = x + STRUMLINE_SIZE / 2f, cy = y + STRUMLINE_SIZE / 2f;
        var sheet = PsychNoteSheet(PsychDefaultSkin);
        var fr = PsychPrefixFrame(PsychDefaultSkin, sheet, PsychNoteSkin.ColArray[lane] + "0");
        if (fr != null && !fr.Rotated)
        {
            float s = 0.7f * scale;
            int fw = fr.FrameWidth > 0 ? fr.FrameWidth : fr.SourceRect.Width, fh = fr.FrameHeight > 0 ? fr.FrameHeight : fr.SourceRect.Height;
            // PsychDrawNoteHead: pos (x + fw*0.7/2, y + fh*0.7/2), origin = frame centre - frameOffset -> image centre
            float px = x + fw * 0.7f / 2f, py = y + fh * 0.7f / 2f;
            float ix = fr.Offset.X + fr.SourceRect.Width / 2f - fw / 2f, iy = fr.Offset.Y + fr.SourceRect.Height / 2f - fh / 2f;
            float c = MathF.Cos(angle), sn = MathF.Sin(angle);
            cx = px + (ix * c - iy * sn) * s; cy = py + (ix * sn + iy * c) * s;
        }
        if (ControllerNoteSkin.DrawNote(sb, Assets, lane, Input.NoteFaceButtons[lane], new Vector2(cx, cy), PsychButtonSize() * scale, alpha, angle, HudArtScale)) return true;
        float bs = PsychButtonSize() * scale / tex.Width;
        sb.Draw(tex, new Vector2(cx, cy), null, Color.White * Math.Clamp(alpha, 0f, 1f), angle, new Vector2(tex.Width / 2f, tex.Height / 2f), bs, SpriteEffects.None, 0f);
        return true;
    }
}
