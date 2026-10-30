extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;

namespace FNF_MonoGame.Scenes;

/// <summary>flixel.text.FlxText created by V-Slice mod scripts (QT Rewired blissful 'Skip [ENTER]'): drawn with the
/// VCR FlxText metrics (MenuUI.FlxText) on the camera given by .cameras (camCutscene above camHUD, camHUD otherwise).</summary>
public partial class PlayScene
{
    private readonly List<VsText> _vsTexts = new();
    internal void VsAddText(VsText t) { if (!_vsTexts.Contains(t)) _vsTexts.Add(t); }
    internal void VsRemoveText(VsText t) { _vsTexts.Remove(t); }

    /// <summary>Texts on camCutscene (drawn under the camCutscene fade) or camHUD.</summary>
    internal void VsDrawTexts(SpriteBatch sb, bool cutsceneCam)
    {
        foreach (var t in _vsTexts) if (t.OnCutsceneCam == cutsceneCam) t.Draw(sb);
    }
}

public sealed class VsText : VObj
{
    private readonly PlayScene _ps;
    public float X, Y, Alpha = 1f, FieldWidth; public bool Visible = true, Alive = true; public int Size = 8; public string Text = "";
    public Color TextColor = Color.White, BorderColor = Color.Black; public string Align = "left", BorderStyle = "none"; public float BorderSize = 1f;
    public bool OnCutsceneCam;
    public VsText(PlayScene ps, object[] a)
    {
        _ps = ps;
        X = (float)D(A(a, 0)); Y = (float)D(A(a, 1)); FieldWidth = (float)D(A(a, 2));
        if (A(a, 3) is string s) Text = s;
        if (a != null && a.Length > 4 && a[4] != null) Size = (int)D(a[4]);
    }
    public float Width => FieldWidth > 0 ? FieldWidth : MenuUI.FlxTextWidth(Size, Text);
    public float Height => MenuUI.FlxTextLineHeight(Size) + 4f;
    static Color Col(object o) { long c = (long)D(o); return new Color((int)((c >> 16) & 255), (int)((c >> 8) & 255), (int)(c & 255)); }

    public void Draw(SpriteBatch sb)
    {
        if (!Visible || !Alive || Alpha <= 0.001f || string.IsNullOrEmpty(Text)) return;
        float x = X;
        if (FieldWidth > 0)
        {
            float tw = MenuUI.FlxTextWidth(Size, Text);
            if (Align == "right") x += FieldWidth - tw; else if (Align == "center") x += (float)Math.Floor((FieldWidth - tw) / 2f);
        }
        float a = Math.Clamp(Alpha, 0f, 1f);
        if (BorderStyle == "outline" && BorderSize > 0)
        {
            // FlxText.applyBorderStyle OUTLINE: iterations = borderSize * borderQuality(1), 8 directions per step
            int it = Math.Max(1, (int)BorderSize); float delta = BorderSize / it;
            var bc = new Color(BorderColor, a);
            for (int i = 1; i <= it; i++)
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (ox != 0 || oy != 0) MenuUI.FlxText(sb, Size, Text, x + ox * delta * i, Y + oy * delta * i, bc);
        }
        MenuUI.FlxText(sb, Size, Text, x, Y, new Color(TextColor, a));
    }

    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)X; return true;
            case "y": v = (double)Y; return true;
            case "alpha": v = (double)Alpha; return true;
            case "visible": v = Visible; return true;
            case "alive": case "exists": v = Alive; return true;
            case "text": v = Text; return true;
            case "size": v = Size; return true;
            case "width": case "fieldWidth": v = (double)Width; return true;
            case "height": v = (double)Height; return true;
            case "borderSize": v = (double)BorderSize; return true;
            case "zIndex": v = Extra.TryGetValue("zIndex", out var z) ? z : 0; return true;
            case "scrollFactor": v = new VsPoint(() => 0f, _ => { }, () => 0f, _ => { }); return true;
            case "setFormat":
                v = Fn(a =>
                {
                    if (a.Length > 1 && a[1] != null) Size = (int)D(a[1]);
                    if (a.Length > 2 && a[2] != null) TextColor = Col(a[2]);
                    if (a.Length > 3 && a[3] is string al) Align = al.ToLowerInvariant();
                    if (a.Length > 4 && a[4] != null) BorderStyle = (a[4].ToString() ?? "").ToLowerInvariant().Contains("outline") ? "outline" : (a[4].ToString() ?? "").ToLowerInvariant();
                    if (a.Length > 5 && a[5] != null) BorderColor = Col(a[5]);
                    return this;
                }); return true;
            case "setBorderStyle": v = Fn(a => { BorderStyle = (A(a, 0)?.ToString() ?? "").ToLowerInvariant().Contains("outline") ? "outline" : "none"; if (a.Length > 1) BorderColor = Col(a[1]); if (a.Length > 2) BorderSize = (float)D(a[2]); return this; }); return true;
            case "screenCenter": v = Fn(a => { string ax = A(a, 0)?.ToString() ?? "XY"; bool bx = ax.Contains("X") || ax == "1" || ax == "17", by = ax.Contains("Y") || ax == "16" || ax == "17"; if (ax == "XY") bx = by = true; if (bx) X = (1280f - Width) / 2f; if (by) Y = (720f - Height) / 2f; return this; }); return true;
            case "kill": v = Fn(a => { Alive = false; return null; }); return true;
            case "revive": v = Fn(a => { Alive = true; return null; }); return true;
            case "destroy": v = Fn(a => { Alive = false; _ps?.VsRemoveText(this); return null; }); return true;
            case "setPosition": v = Fn(a => { X = (float)D(A(a, 0)); Y = (float)D(A(a, 1)); return this; }); return true;
            case "updateHitbox": v = Fn(a => null); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "x": X = (float)D(v); return true;
            case "y": Y = (float)D(v); return true;
            case "alpha": Alpha = (float)D(v); return true;
            case "visible": Visible = B(v); return true;
            case "text": Text = v?.ToString() ?? ""; return true;
            case "size": Size = (int)D(v); return true;
            case "borderSize": BorderSize = (float)D(v); return true;
            case "color": TextColor = Col(v); return true;
            case "fieldWidth": FieldWidth = (float)D(v); return true;
            case "zIndex": Extra["zIndex"] = v; return true;
            case "camera": case "cameras":
                {
                    object cam = v is IRIS::Array<object> arr && arr.length > 0 ? arr.__get(0) : v;
                    OnCutsceneCam = cam is VsCamera vc && vc.IsCutscene;
                    return true;
                }
        }
        return false;
    }
}
