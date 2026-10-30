using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>Where the editors save: one Psych-style mod folder (mods/&lt;name&gt;) that also receives the V-Slice files.</summary>
public static class EdProject
{
    public static string ModName = Environment.GetEnvironmentVariable("FNF_EDITOR_MOD") ?? "";   // dev hook: preselected target mod

    public static string ModsBase
    {
        get
        {
            string b = FNF_MonoGame.Engine.PsychEngine.PsychMods.ModsBasePath;
            if (string.IsNullOrEmpty(b)) b = Path.Combine(AssetManager.GameRoot ?? AppContext.BaseDirectory, "mods");
            return b;
        }
    }

    public static List<string> Mods()
    {
        try { return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ModsBase) ? global::FNF_MonoGame.Engine.FsCI.GetDirectories(ModsBase).Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
        catch { return new List<string>(); }
    }

    public static string Root => string.IsNullOrEmpty(ModName) ? null : Path.Combine(ModsBase, ModName);

    /// <summary>Creates the Psych mod skeleton (pack.json + folders) and enables it in modsList.txt.</summary>
    public static string Ensure(string name)
    {
        name = string.Join("_", (name ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0) name = "MyEditorMod";
        ModName = name;
        string r = Root;
        foreach (var d in new[] { "", "data", "songs", "characters", "stages", "weeks", "images", "images/characters", "images/icons", "images/menucharacters", "images/storymenu", "data/stages", "data/characters", "data/levels" })
            Directory.CreateDirectory(Path.Combine(r, d));
        string pack = Path.Combine(r, "pack.json");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(pack))
            File.WriteAllText(pack, new JObject { ["name"] = name, ["description"] = "Made with the in-engine editors (Psych + V-Slice formats).", ["restart"] = false, ["runsGlobally"] = false, ["color"] = new JArray(170, 0, 255) }.ToString());
        try
        {
            string list = Path.Combine(ModsBase, "modsList.txt");
            var lines = global::FNF_MonoGame.Engine.FsCI.FileExists(list) ? global::FNF_MonoGame.Engine.FsCI.ReadAllLines(list).ToList() : new List<string>();
            if (!lines.Any(l => l.Split('|')[0].Equals(name, StringComparison.OrdinalIgnoreCase))) { lines.Add(name + "|1"); File.WriteAllLines(list, lines); }
            FNF_MonoGame.Engine.PsychEngine.PsychMods.ParseList();
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] modsList: " + ex.Message); }
        return r;
    }

    /// <summary>Picks mods/&lt;name&gt; as the target when a file inside it is opened (dev preload hooks).</summary>
    public static void GuessModFrom(string file)
    {
        try
        {
            string b = Path.GetFullPath(ModsBase).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(file);
            if (!f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return;
            string rest = f.Substring(b.Length);
            int i = rest.IndexOfAny(new[] { '\\', '/' });
            if (i > 0) ModName = rest.Substring(0, i);
        }
        catch { }
    }

    public static string Rel(string full)
    {
        if (string.IsNullOrEmpty(full)) return "";
        try
        {
            string b = Path.GetFullPath(ModsBase);
            string f = Path.GetFullPath(full);
            if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return "mods" + f.Substring(b.Length).Replace('\\', '/');
            string c = Path.GetFullPath(AssetManager.ContentRoot);
            if (f.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return "Content" + f.Substring(c.Length).Replace('\\', '/');
        }
        catch { }
        return full;
    }
}

/// <summary>Shared frame for every merged editor: input latch, paper top bar with the target mod, Esc = back to the hub.</summary>
public abstract class EditorBase : Scene
{
    protected abstract string EdTitle { get; }
    protected virtual Color EdColor => Ed.Yellow;
    protected float Dt;
    private static readonly Color Bg = new(44, 28, 66);
    protected bool ModPicker;
    private float _modScroll; private int _modSel = -1; private string _newMod = "";

    public override void Update(GameTime gameTime)
    {
        Dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Ed.Frame(Input, MouseVirtual, Dt);
        if (!Ed.Typing && (Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B) || Input.IsGamePadPressed(Buttons.Back)))
        {
            if (ModPicker) ModPicker = false;
            else if (Ed.PadWorld && Input.IsGamePadPressed(Buttons.B)) { Ed.PadWorld = false; Ed.Toast("PAD: PANEL (D-pad walks the widgets)", Ed.Cyan); }
            else { OnExit(); Ed.PadWorld = false; Game.Scenes.ChangeScene(new EditorsHubScene()); return; }
        }
        if (!Ed.Typing && !ModPicker)
        {
            // Y: switch the pad between the side panel (widgets) and the editor's own tools; Start: save
            if (Input.IsGamePadPressed(Buttons.Y) && HasWorldTools) { Ed.PadWorld = !Ed.PadWorld; Ed.Toast(Ed.PadWorld ? "PAD: TOOLS (" + EdTitle.ToLowerInvariant() + " view)" : "PAD: PANEL (D-pad walks the widgets)", Ed.PadWorld ? Ed.Pink : Ed.Cyan); }
            if (Input.IsGamePadPressed(Buttons.Start)) PadSave();
        }
        if (!ModPicker) EdUpdate(Dt);
    }

    protected virtual void OnExit() { }
    /// <summary>Start on the pad (and F1 in most editors): save the Psych file(s).</summary>
    protected virtual void PadSave() { }
    /// <summary>Editor-specific pad bindings shown while <see cref="Ed.PadWorld"/> (Y toggles).</summary>
    protected virtual List<(Buttons, string)> PadWorldHints() => null;
    protected bool HasWorldTools => PadWorldHints() != null;

    private static readonly List<(Buttons, string)> PanelHints = new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadUpDown, "widget"), (Buttons.A, "pick / edit"), (FNF_MonoGame.Engine.ButtonPrompts.DPadLeftRight, "change"),
        (Buttons.Y, "tools"), (Buttons.Start, "save"), (Buttons.B, "back"),
    };
    private static readonly List<(Buttons, string)> OskHints = new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadAll, "key"), (Buttons.A, "type"), (Buttons.X, "delete"), (Buttons.Y, "caps"), (Buttons.LeftShoulder, "space"), (Buttons.Start, "done"),
    };
    protected abstract void EdUpdate(float dt);
    protected abstract void DrawWorld(SpriteBatch sb);
    protected abstract void DrawUI();

    public override void Draw(SpriteBatch sb)
    {
        sb.GraphicsDevice.Clear(Bg);
        DrawWorld(sb);
        FunkUI.Begin(sb);
        Ed.BeginWidgets();
        Ed.TipText = null;
        Ed.SetAssets(Assets);
        Ed.ShowPadHints = FNF_MonoGame.Engine.ButtonPrompts.UseController(Input) || Ed.PadMode;
        if (Ed.Osk) Ed.PadHints = OskHints;
        else if (Ed.PadWorld) { var w = new List<(Buttons, string)>(PadWorldHints() ?? new()); w.Add((Buttons.Y, "panel")); w.Add((Buttons.Start, "save")); Ed.PadHints = w; }
        else Ed.PadHints = HasWorldTools ? PanelHints : PanelHints.FindAll(h => h.Item1 != Buttons.Y);
        DrawUI();
        TopBar();
        if (ModPicker) DrawModPicker();
        Ed.EndWidgets();
        sb.End();
    }

    /// <summary>Checkered "graph paper" backdrop for world views.</summary>
    protected static void Backdrop(float camX, float camY, float zoom, Color a, Color b)
    {
        FunkUI.Fill(0, 0, 1280, 720, a);
        float step = 64f * zoom;
        if (step < 8f) return;
        float ox = ((-camX * zoom) % step + step) % step, oy = ((-camY * zoom) % step + step) % step;
        for (float x = ox; x < 1280; x += step) FunkUI.Fill(x, 0, 2f, 720, b);
        for (float y = oy; y < 720; y += step) FunkUI.Fill(0, y, 1280, 2f, b);
    }

    private void TopBar()
    {
        Ed.Title(EdTitle, EdColor);
        if (Ed.PadWorld)
        {   // pad is driving the view tools: pink tag next to the title
            float tx = 40f + FunkUI.BoldWidth(EdTitle, 0.5f);
            FunkUI.Push(tx + 70f, 30f, FunkUI.Rad(5f));
            FunkUI.ShadowBlock(tx, 16f, 140f, 28f, Ed.Pink, 3f, 4f, 5f);
            FunkUI.Text("PAD: TOOLS", tx + 70f, 22f, 16f, Ed.Ink, 0.5f);
            FunkUI.Pop();
        }
        string mod = string.IsNullOrEmpty(EdProject.ModName) ? "(pick a mod)" : EdProject.ModName;
        if (Ed.Button("MOD: " + mod, 930f, 12f, 250f, 30f, Ed.Cyan, false, "Folder where Save writes (mods/<name>)")) { ModPicker = !ModPicker; _newMod = EdProject.ModName; }
        if (Ed.Button("BACK", 1192f, 12f, 72f, 30f, Ed.Red)) { OnExit(); Game.Scenes.ChangeScene(new EditorsHubScene()); }
        if (!string.IsNullOrEmpty(Ed.TipText)) { float w = FunkUI.MeasureVcr(Ed.TipText, 14f) + 16f; FunkUI.Fill(Math.Min(1270f - w, Ed.M.X + 14f), Ed.M.Y + 18f, w, 22f, Ed.Ink); FunkUI.Text(Ed.TipText, Math.Min(1270f - w, Ed.M.X + 14f) + 8f, Ed.M.Y + 22f, 14f, Color.White); }
    }

    private void DrawModPicker()
    {
        FunkUI.Fill(0, 0, 1280, 720, new Color(0, 0, 0, 150));
        Ed.Panel(390f, 120f, 500f, 470f, Ed.Paper, "TARGET MOD", Ed.Cyan, 3);
        var mods = EdProject.Mods();
        _modSel = mods.FindIndex(m => m.Equals(EdProject.ModName, StringComparison.OrdinalIgnoreCase));
        int s = Ed.List("modlist", mods, _modSel, 410f, 140f, 460f, 300f, ref _modScroll, Ed.Pink);
        if (s != _modSel && s >= 0) { EdProject.ModName = mods[s]; OnModChanged(); }
        Ed.Field("newmod", "New mod", ref _newMod, 410f, 460f, 330f, 90f);
        if (Ed.Button("CREATE", 750f, 459f, 120f, 28f, Ed.Green)) { EdProject.Ensure(_newMod); OnModChanged(); Ed.Toast("Mod folder ready: " + EdProject.Rel(EdProject.Root)); }
        Ed.Label("Saves go to mods/<mod>/ in BOTH formats:", 410f, 505f, 14f);
        Ed.Label("Psych (data/, characters/, stages/, weeks/)", 410f, 525f, 14f);
        Ed.Label("V-Slice (data/songs, data/characters, data/stages, data/levels)", 410f, 545f, 14f);
        if (Ed.Button("DONE", 760f, 565f, 110f, 30f, Ed.Yellow)) ModPicker = false;
    }

    protected virtual void OnModChanged() { }

    /// <summary>Mod root, creating the default test mod when none is selected yet.</summary>
    protected string NeedRoot()
    {
        if (EdProject.Root == null || !global::FNF_MonoGame.Engine.FsCI.DirectoryExists(EdProject.Root)) EdProject.Ensure(string.IsNullOrEmpty(EdProject.ModName) ? "MyEditorMod" : EdProject.ModName);
        return EdProject.Root;
    }
}
