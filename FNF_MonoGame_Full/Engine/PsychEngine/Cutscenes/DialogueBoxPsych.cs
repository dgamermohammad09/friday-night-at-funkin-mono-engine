using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine.PsychEngine.Cutscenes;

// 1:1 port of Psych Engine 1.0.4 cutscenes/DialogueBoxPsych.hx, cutscenes/DialogueCharacter.hx,
// objects/TypedAlphabet.hx and objects/Alphabet.hx (non-bold path) on top of FlxLiteSprite.

public class PsychDialogueLine
{
    public string portrait, expression, text, boxState, sound;
    public float? speed;
}

public class PsychDialogueFile
{
    public List<PsychDialogueLine> dialogue = new();

    /// <summary>DialogueBoxPsych.parseDialogue: Json.parse of the file, dummy() when missing.</summary>
    public static PsychDialogueFile Parse(string path)
    {
        if (path == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return Dummy();
        var f = new PsychDialogueFile();
        var root = JObject.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
        if (root["dialogue"] is JArray arr)
            foreach (var t in arr)
            {
                if (t is not JObject o) { f.dialogue.Add(null); continue; }
                f.dialogue.Add(new PsychDialogueLine
                {
                    portrait = (string)o["portrait"], expression = (string)o["expression"], text = (string)o["text"],
                    boxState = (string)o["boxState"], sound = (string)o["sound"],
                    speed = o["speed"] != null && o["speed"].Type != JTokenType.Null ? (float?)(float)o["speed"] : null
                });
            }
        return f;
    }

    public static PsychDialogueFile Dummy() => new()
    {
        dialogue = { new PsychDialogueLine { expression = "talk", text = "DIALOGUE NOT FOUND", boxState = "normal", speed = 0.05f, portrait = "bf" } }
    };
}

internal static class PsychCutsceneAssets
{
    /// <summary>Paths.getSparrowAtlas(key): mod folders first, then Psych's shared images (Content/images/psych).</summary>
    public static SpriteSheet Sparrow(string key)
    {
        var game = FNFGame.Instance;
        string mod = PsychMods.ModFolders("images/" + key + ".png");
        if (mod != null)
        {
            var s = SpriteSheet.Load(game, mod.Substring(0, mod.Length - 4), sparrowOnly: true);
            if (s != null) return s;
        }
        return SpriteSheet.Load(game, "images/psych/" + key, sparrowOnly: true);
    }

    public static string Text(string rel)
    {
        string mod = PsychMods.ModFolders(rel);
        if (mod != null) return mod;
        string c = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, rel.StartsWith("images/") ? "images/psych/" + rel.Substring(7) : rel);
        return global::FNF_MonoGame.Engine.FsCI.FileExists(c) ? c : null;
    }

    /// <summary>FlxG.sound.play(Paths.sound(key), volume)</summary>
    public static void PlaySound(string key, float volume = 1f)
    {
        if (string.IsNullOrEmpty(key)) return;
        try
        {
            string p = PsychMods.ModFolders("sounds/" + key + ".ogg");
            if (p == null)
            {
                string c = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "sounds", "psych", key + ".ogg");
                p = global::FNF_MonoGame.Engine.FsCI.FileExists(c) ? c : "sounds/" + key + ".ogg";
            }
            FNFGame.Instance.Audio?.PlaySound(p, volume);
        }
        catch (Exception e) { Console.WriteLine($"[PSYCH-DIALOGUE] sound {key}: {e.Message}"); }
    }
}

/// <summary>objects/Alphabet.hx AlphaCharacter.</summary>
public class AlphaCharacter : FlxLiteSprite
{
    public sealed class Letter { public string anim; public float[] offsets, offsetsBold; }
    public static Dictionary<string, Letter> AllLetters;
    private static SpriteSheet _sheet;

    /// <summary>AlphaCharacter.loadAlphabetData('alphabet')</summary>
    public static void LoadAlphabetData()
    {
        AllLetters = new Dictionary<string, Letter>(StringComparer.Ordinal);
        try
        {
            string path = PsychCutsceneAssets.Text("images/alphabet.json");
            var data = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
            string allowed = (string)data["allowed"] ?? "";
            foreach (char ch in allowed) { if (ch == ' ') continue; AllLetters[char.ToLowerInvariant(ch).ToString()] = null; }
            if (data["characters"] is JObject chars)
                foreach (var kv in chars)
                {
                    string c = kv.Key.ToLowerInvariant().Substring(0, 1);
                    var ld = kv.Value as JObject; if (ld == null) continue;
                    if ((ld["animation"] != null || ld["normal"] != null || ld["bold"] != null) && AllLetters.ContainsKey(c))
                        AllLetters[c] = new Letter
                        {
                            anim = (string)ld["animation"],
                            offsets = ld["normal"]?.Select(x => (float)x).ToArray(),
                            offsetsBold = ld["bold"]?.Select(x => (float)x).ToArray()
                        };
                }
        }
        catch (Exception e) { Console.WriteLine($"[PSYCH-DIALOGUE] alphabet data: {e.Message}"); }
        if (!AllLetters.ContainsKey("?")) AllLetters["?"] = new Letter { anim = "question" };
    }

    public float[] LetterOffset = { 0, 0 };
    public int Row; public float RowWidth; public string Character = "?";
    public Letter CurLetter;

    public AlphaCharacter()
    {
        _sheet ??= PsychCutsceneAssets.Sparrow("alphabet");
        SetFrames(_sheet);
    }

    public static bool IsTypeAlphabet(string c)
    {
        int a = c[0];
        return (a >= 65 && a <= 90) || (a >= 97 && a <= 122) || (a >= 192 && a <= 214) || (a >= 216 && a <= 246) || (a >= 248 && a <= 255);
    }

    public void SetupAlphaCharacter(float x, float y, string character, bool bold, float scaleX, float scaleY)
    {
        X = x; Y = y; Scale = new Vector2(scaleX, scaleY);
        Character = character; CurLetter = null;
        string lower = character.ToLowerInvariant();
        if (AllLetters.TryGetValue(lower, out var l)) CurLetter = l; else AllLetters.TryGetValue("?", out CurLetter);
        string postfix = !bold ? (IsTypeAlphabet(lower) ? (lower != character ? " uppercase" : " lowercase") : " normal") : " bold";
        string alphaAnim = CurLetter?.anim ?? lower;
        string anim = alphaAnim + postfix;
        if (!Anims.ContainsKey(anim)) AddByPrefix(anim, anim, 24);
        Play(anim, true);
        if (CurAnim == null || CurAnim.Name != anim)
        {
            if (postfix != " bold") postfix = " normal";
            anim = "question" + postfix;
            if (!Anims.ContainsKey(anim)) AddByPrefix(anim, anim, 24);
            Play(anim, true);
        }
        UpdateHitbox();
    }

    public override void UpdateHitbox()
    {
        base.UpdateHitbox();
        if (CurAnim == null) return;
        float add = 110;
        if (CurAnim.Name.EndsWith("bold"))
        {
            if (CurLetter?.offsetsBold != null) { LetterOffset[0] = CurLetter.offsetsBold[0]; LetterOffset[1] = CurLetter.offsetsBold[1]; }
            add = 70;
        }
        else if (CurLetter?.offsets != null) { LetterOffset[0] = CurLetter.offsets[0]; LetterOffset[1] = CurLetter.offsets[1]; }
        add *= Scale.Y;
        Offset = new Vector2(Offset.X + LetterOffset[0] * Scale.X, Offset.Y + LetterOffset[1] * Scale.Y - (add - Height));
    }
}

/// <summary>objects/TypedAlphabet.hx (+ Alphabet.createLetters, left alignment, bold = false).</summary>
public class DialogueTypedAlphabet
{
    public float X, Y; public float ScaleX = 1, ScaleY = 1; public bool Bold;
    public readonly List<AlphaCharacter> Letters = new();
    private readonly List<AlphaCharacter> _pool = new(); // FlxGroup.recycle: killed letters are reused (they keep letterOffset)
    public int Rows;
    public bool FinishedText; public float Delay = 0.05f; public string Sound = "dialogue"; public float Volume = 1f;
    private int _curLetter = -1; private float _timeToUpdate;
    private string _text = "";
    private const float Y_PER_ROW = 85;

    public DialogueTypedAlphabet(float x, float y, string text = "", float delay = 0.05f, bool bold = false)
    { X = x; Y = y; Bold = bold; Delay = delay; if (AlphaCharacter.AllLetters == null) AlphaCharacter.LoadAlphabetData(); Text = text; }

    public void SetScale(float s) { ScaleX = ScaleY = s; foreach (var l in Letters) l.SetupAlphaCharacter(l.X * 1f, l.Y, l.Character, Bold, s, s); }

    public string Text
    {
        get => _text;
        set
        {
            string t = (value ?? "").Replace("\\n", "\n");
            foreach (var l in Letters) { l.Alive = false; _pool.Add(l); }
            Letters.Clear(); Rows = 0;
            CreateLetters(t);
            _text = t;
            ResetDialogue();
        }
    }

    private void CreateLetters(string text)
    {
        int consecutiveSpaces = 0; float xPos = 0; var rowData = new List<float>(); Rows = 0;
        void SetRow(int r, float v) { while (rowData.Count <= r) rowData.Add(0); rowData[r] = v; }
        foreach (char ch in text)
        {
            string character = ch.ToString();
            if (ch == '\n') { xPos = 0; Rows++; continue; }
            bool spaceChar = ch == ' ' || (Bold && ch == '_');
            if (spaceChar) consecutiveSpaces++;
            if (AlphaCharacter.AllLetters.ContainsKey(character.ToLowerInvariant()) && (!Bold || !spaceChar))
            {
                if (consecutiveSpaces > 0)
                {
                    xPos += 28 * consecutiveSpaces * ScaleX;
                    SetRow(Rows, xPos);
                    if (!Bold && xPos >= 1280 * 0.65f) { xPos = 0; Rows++; }
                }
                consecutiveSpaces = 0;
                AlphaCharacter letter;
                if (_pool.Count > 0) { letter = _pool[0]; _pool.RemoveAt(0); letter.Alive = true; } else letter = new AlphaCharacter();
                letter.RowWidth = 0;
                letter.SetupAlphaCharacter(xPos, Rows * Y_PER_ROW * ScaleY, character, Bold, ScaleX, ScaleY);
                letter.Row = Rows;
                float off = Bold ? 0 : 2;
                xPos += letter.Width + (letter.LetterOffset[0] + off) * ScaleX;
                SetRow(Rows, xPos);
                Letters.Add(letter);
            }
        }
        foreach (var l in Letters) l.RowWidth = (l.Row < rowData.Count ? rowData[l.Row] : 0) / ScaleX;
        if (Letters.Count > 0) Rows++;
    }

    public void ResetDialogue() { _curLetter = -1; FinishedText = false; _timeToUpdate = 0; foreach (var l in Letters) l.Visible = false; }

    private void ShowCharacterUpTo(int upTo)
    {
        int start = Math.Max(0, _curLetter);
        for (int i = start; i <= upTo; i++) if (i < Letters.Count) Letters[i].Visible = true;
    }

    public void FinishText()
    {
        if (FinishedText) return;
        ShowCharacterUpTo(Letters.Count - 1);
        if (Sound != "") PsychCutsceneAssets.PlaySound(Sound, Volume);
        FinishedText = true; _timeToUpdate = 0;
    }

    public void Update(float elapsed)
    {
        if (!FinishedText)
        {
            bool playedSound = false;
            _timeToUpdate += elapsed;
            while (_timeToUpdate >= Delay)
            {
                ShowCharacterUpTo(_curLetter + 1);
                if (!playedSound && Sound != "" && (Delay > 0.025f || _curLetter % 2 == 0)) PsychCutsceneAssets.PlaySound(Sound, Volume);
                playedSound = true;
                _curLetter++;
                if (_curLetter >= Letters.Count - 1) { FinishedText = true; _timeToUpdate = 0; break; }
                _timeToUpdate = 0;
            }
        }
        foreach (var l in Letters) l.Update(elapsed);
    }

    public void Draw(SpriteBatch sb, float alpha = 1f) { foreach (var l in Letters) l.Draw(sb, alpha, X, Y); }
}

/// <summary>cutscenes/DialogueCharacter.hx</summary>
public class DialogueCharacter : FlxLiteSprite
{
    private const string IDLE_POSTFIX = "-IDLE";
    public const string DEFAULT_CHARACTER = "bf";
    public const float DEFAULT_SCALE = 0.7f;
    public sealed class AnimArray { public string anim, loop_name, idle_name; public int[] loop_offsets = { 0, 0 }, idle_offsets = { 0, 0 }; }
    public string Image = "", DialoguePos = "left"; public bool NoAntialiasing; public float[] Position = { 0, 0 }; public float JsonScale = 1;
    public readonly Dictionary<string, AnimArray> DialogueAnimations = new(StringComparer.Ordinal);
    private readonly List<string> _animOrder = new();
    public float StartingPos; public string CurCharacter;
    private static readonly Random _rng = new();

    public DialogueCharacter(float x, float y, string character) : base(x, y)
    {
        character ??= DEFAULT_CHARACTER; CurCharacter = character;
        ReloadCharacterJson(character);
        SetFrames(PsychCutsceneAssets.Sparrow("dialogue/" + Image));
        ReloadAnimations();
    }

    private void ReloadCharacterJson(string character)
    {
        string path = PsychCutsceneAssets.Text("images/dialogue/" + character + ".json") ?? PsychCutsceneAssets.Text("images/dialogue/" + DEFAULT_CHARACTER + ".json");
        var o = JObject.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
        Image = (string)o["image"] ?? ""; DialoguePos = (string)o["dialogue_pos"] ?? "left"; NoAntialiasing = (bool?)o["no_antialiasing"] ?? false;
        if (o["position"] is JArray p && p.Count >= 2) Position = new[] { (float)p[0], (float)p[1] };
        JsonScale = (float?)o["scale"] ?? 1f;
        if (o["animations"] is JArray anims)
            foreach (var a in anims.OfType<JObject>())
            {
                var aa = new AnimArray { anim = (string)a["anim"], loop_name = (string)a["loop_name"], idle_name = (string)a["idle_name"] };
                if (a["loop_offsets"] is JArray lo && lo.Count >= 2) aa.loop_offsets = new[] { (int)lo[0], (int)lo[1] };
                if (a["idle_offsets"] is JArray io && io.Count >= 2) aa.idle_offsets = new[] { (int)io[0], (int)io[1] };
                if (aa.anim == null) continue;
                if (!DialogueAnimations.ContainsKey(aa.anim)) _animOrder.Add(aa.anim);
                DialogueAnimations[aa.anim] = aa;
            }
    }

    private void ReloadAnimations()
    {
        foreach (var name in _animOrder)
        {
            var a = DialogueAnimations[name];
            AddByPrefix(a.anim, a.loop_name, 24, false);
            AddByPrefix(a.anim + IDLE_POSTFIX, a.idle_name, 24, true);
        }
    }

    public void PlayAnim(string animName = null, bool playIdle = false)
    {
        string leAnim = animName;
        if (animName == null || !DialogueAnimations.ContainsKey(animName))
            if (_animOrder.Count > 0) leAnim = _animOrder[_rng.Next(_animOrder.Count)];
        if (leAnim != null && DialogueAnimations.TryGetValue(leAnim, out var d)
            && (string.IsNullOrEmpty(d.loop_name) || d.loop_name == d.idle_name)) playIdle = true;
        Play(playIdle ? leAnim + IDLE_POSTFIX : leAnim, false);
        if (leAnim != null && DialogueAnimations.TryGetValue(leAnim, out var an))
        {
            var o = playIdle ? an.idle_offsets : an.loop_offsets;
            Offset = new Vector2(o[0], o[1]);
        }
        else Offset = Vector2.Zero;
    }

    public bool AnimationIsLoop() => CurAnim != null && !CurAnim.Name.EndsWith(IDLE_POSTFIX);
}

/// <summary>cutscenes/DialogueBoxPsych.hx (FlxSpriteGroup on camHUD, scrollFactor 0).</summary>
public class DialogueBoxPsych
{
    public const int DEFAULT_TEXT_X = 175, DEFAULT_TEXT_Y = 460, LONG_TEXT_ADD = 24;
    private const float scrollSpeed = 4000;
    public const float LEFT_CHAR_X = -60, RIGHT_CHAR_X = -100, DEFAULT_CHAR_Y = 60;

    private readonly PsychDialogueFile dialogueList;
    public Action finishThing, nextDialogueThing, skipDialogueThing;
    private FlxLiteSprite bgFade, box;
    private readonly List<DialogueCharacter> arrayCharacters = new();
    private int currentText;
    private const float offsetPos = -600;
    private bool skipTextVisible = true;
    private readonly string[] textBoxTypes = { "normal", "angry" };
    private DialogueTypedAlphabet daText;
    private bool ignoreThisFrame = true, dialogueEnded;
    public string closeSound = "dialogueClose"; public float closeVolume = 1;
    private int lastCharacter = -1; private string lastBoxType = "";
    public bool Alive { get; private set; } = true;
    private readonly bool _startedMusic;

    public DialogueBoxPsych(PsychDialogueFile dialogueList, string song = null)
    {
        if (!string.IsNullOrEmpty(song))
        {
            try
            {
                var audio = FNFGame.Instance.Audio;
                string p = PsychMods.ModFolders("music/" + song + ".ogg") ?? "music/" + song;
                audio.PlayMusic(p, true);
                audio.FadeIn(2f, 0f, 1f);
                _startedMusic = true;
            }
            catch (Exception e) { Console.WriteLine($"[PSYCH-DIALOGUE] music {song}: {e.Message}"); }
        }
        bgFade = new FlxLiteSprite(-500, -500); bgFade.MakeGraphic(1280 * 2, 720 * 2, Color.White); bgFade.Alpha = 0;
        this.dialogueList = dialogueList;
        SpawnCharacters();

        box = new FlxLiteSprite(70, 370);
        box.SetFrames(PsychCutsceneAssets.Sparrow("speech_bubble"));
        box.AddByPrefix("normal", "speech bubble normal", 24);
        box.AddByPrefix("normalOpen", "Speech Bubble Normal Open", 24, false);
        box.AddByPrefix("angry", "AHH speech bubble", 24);
        box.AddByPrefix("angryOpen", "speech bubble loud open", 24, false);
        box.AddByPrefix("center-normal", "speech bubble middle", 24);
        box.AddByPrefix("center-normalOpen", "Speech Bubble Middle Open", 24, false);
        box.AddByPrefix("center-angry", "AHH Speech Bubble middle", 24);
        box.AddByPrefix("center-angryOpen", "speech bubble Middle loud open", 24, false);
        box.Play("normal", true);
        box.Visible = false;
        box.SetGraphicSize((int)(box.Width * 0.9f));
        box.UpdateHitbox();

        daText = new DialogueTypedAlphabet(DEFAULT_TEXT_X, DEFAULT_TEXT_Y, "");
        daText.SetScale(0.7f);
        StartNextDialog();
    }

    private void SpawnCharacters()
    {
        var order = new List<string>();
        foreach (var l in dialogueList.dialogue) if (l != null && !order.Contains(l.portrait)) order.Add(l.portrait);
        foreach (var individualChar in order)
        {
            float x = LEFT_CHAR_X, y = DEFAULT_CHAR_Y;
            var ch = new DialogueCharacter(x + offsetPos, y, individualChar);
            ch.SetGraphicSize((int)(ch.Width * DialogueCharacter.DEFAULT_SCALE * ch.JsonScale));
            ch.UpdateHitbox();
            ch.Alpha = 0.00001f;
            bool saveY = false;
            switch (ch.DialoguePos)
            {
                case "center":
                    ch.X = 1280 / 2f; ch.X -= ch.Width / 2f;
                    y = ch.Y; ch.Y = 720 + 50; saveY = true; break;
                case "right":
                    x = 1280 - ch.Width + RIGHT_CHAR_X; ch.X = x - offsetPos; break;
            }
            x += ch.Position[0]; y += ch.Position[1];
            ch.X += ch.Position[0]; ch.Y += ch.Position[1];
            ch.StartingPos = saveY ? y : x;
            arrayCharacters.Add(ch);
        }
    }

    public void Update(float elapsed, bool accept, bool back)
    {
        if (!Alive) return;
        if (ignoreThisFrame) { ignoreThisFrame = false; UpdateMembers(elapsed); return; }

        if (!dialogueEnded)
        {
            bgFade.Alpha += 0.5f * elapsed; if (bgFade.Alpha > 0.5f) bgFade.Alpha = 0.5f;
            if (accept || back)
            {
                if (!daText.FinishedText && !back)
                {
                    daText.FinishText();
                    skipDialogueThing?.Invoke();
                }
                else if (back || currentText >= dialogueList.dialogue.Count)
                {
                    dialogueEnded = true;
                    Console.WriteLine($"[DLG] ending back={back} box={box.CurAnim?.Name} bg={bgFade?.Alpha}");
                    foreach (var tb in textBoxTypes)
                        foreach (var pre in new[] { "", "center-" })
                        {
                            string an = box.CurAnim?.Name;
                            if (an == pre + tb || an == pre + tb + "Open") box.Play(pre + tb + "Open", true);
                        }
                    if (box.CurAnim != null) { box.CurAnim.CurFrame = box.CurAnim.Frames.Count - 1; box.CurAnim.Reverse(); }
                    daText = null;
                    skipTextVisible = false;
                    UpdateBoxOffsets(box);
                    try { var au = FNFGame.Instance.Audio; if (_startedMusic) au.FadeOutMusic(1f, () => au.StopMusic()); } catch { }
                }
                else StartNextDialog();
                PsychCutsceneAssets.PlaySound(closeSound, closeVolume);
            }
            else if (daText.FinishedText)
            {
                var ch = lastCharacter >= 0 && lastCharacter < arrayCharacters.Count ? arrayCharacters[lastCharacter] : null;
                if (ch != null && ch.CurAnim != null && ch.AnimationIsLoop() && ch.CurAnim.Finished) ch.PlayAnim(ch.CurAnim.Name, true);
            }
            else
            {
                var ch = lastCharacter >= 0 && lastCharacter < arrayCharacters.Count ? arrayCharacters[lastCharacter] : null;
                if (ch != null && ch.CurAnim != null && ch.CurAnim.Finished) ch.CurAnim.Restart();
            }

            if (box.CurAnim != null && box.CurAnim.Finished)
            {
                foreach (var tb in textBoxTypes)
                    foreach (var pre in new[] { "", "center-" })
                    {
                        string an = box.CurAnim.Name;
                        if (an == pre + tb || an == pre + tb + "Open") box.Play(pre + tb, true);
                    }
                UpdateBoxOffsets(box);
            }

            if (lastCharacter != -1 && arrayCharacters.Count > 0)
                for (int i = 0; i < arrayCharacters.Count; i++)
                {
                    var c = arrayCharacters[i];
                    if (i != lastCharacter)
                    {
                        switch (c.DialoguePos)
                        {
                            case "left": c.X -= scrollSpeed * elapsed; if (c.X < c.StartingPos + offsetPos) c.X = c.StartingPos + offsetPos; break;
                            case "center": c.Y += scrollSpeed * elapsed; if (c.Y > c.StartingPos + 720) c.Y = c.StartingPos + 720; break;
                            case "right": c.X += scrollSpeed * elapsed; if (c.X > c.StartingPos - offsetPos) c.X = c.StartingPos - offsetPos; break;
                        }
                        c.Alpha -= 3 * elapsed; if (c.Alpha < 0.00001f) c.Alpha = 0.00001f;
                    }
                    else
                    {
                        switch (c.DialoguePos)
                        {
                            case "left": c.X += scrollSpeed * elapsed; if (c.X > c.StartingPos) c.X = c.StartingPos; break;
                            case "center": c.Y -= scrollSpeed * elapsed; if (c.Y < c.StartingPos) c.Y = c.StartingPos; break;
                            case "right": c.X -= scrollSpeed * elapsed; if (c.X < c.StartingPos) c.X = c.StartingPos; break;
                        }
                        c.Alpha += 3 * elapsed; if (c.Alpha > 1) c.Alpha = 1;
                    }
                }
        }
        else
        {
            if (box != null && box.CurAnim != null && box.CurAnim.CurFrame <= 0) box = null;
            if (bgFade != null) { bgFade.Alpha -= 0.5f * elapsed; if (bgFade.Alpha <= 0) bgFade = null; }
            foreach (var c in arrayCharacters)
            {
                switch (c.DialoguePos)
                {
                    case "left": c.X -= scrollSpeed * elapsed; break;
                    case "center": c.Y += scrollSpeed * elapsed; break;
                    case "right": c.X += scrollSpeed * elapsed; break;
                }
                c.Alpha -= elapsed * 10;
            }
            if (box == null && bgFade == null)
            {
                arrayCharacters.Clear();
                Alive = false;
                finishThing?.Invoke();
                return;
            }
        }
        UpdateMembers(elapsed);
    }

    private void UpdateMembers(float elapsed)
    {
        foreach (var c in arrayCharacters) c.Update(elapsed);
        box?.Update(elapsed);
        daText?.Update(elapsed);
    }

    private void StartNextDialog()
    {
        PsychDialogueLine cur = null;
        while (cur == null && currentText < dialogueList.dialogue.Count) { cur = dialogueList.dialogue[currentText]; if (cur == null) currentText++; }
        if (cur == null) return;
        if (string.IsNullOrEmpty(cur.text)) cur.text = " ";
        cur.boxState ??= "normal";
        if (cur.speed == null || float.IsNaN(cur.speed.Value)) cur.speed = 0.05f;

        string boxType = textBoxTypes[0];
        foreach (var tb in textBoxTypes) if (tb == cur.boxState) boxType = cur.boxState;
        int character = 0;
        box.Visible = true;
        for (int i = 0; i < arrayCharacters.Count; i++) if (arrayCharacters[i].CurCharacter == cur.portrait) { character = i; break; }
        string lePosition = arrayCharacters.Count > 0 ? arrayCharacters[character].DialoguePos : "left";
        string centerPrefix = lePosition == "center" ? "center-" : "";
        if (character != lastCharacter)
        {
            box.Play(centerPrefix + boxType + "Open", true);
            UpdateBoxOffsets(box);
            box.FlipX = lePosition == "left";
        }
        else if (boxType != lastBoxType)
        {
            box.Play(centerPrefix + boxType, true);
            UpdateBoxOffsets(box);
        }
        lastCharacter = character; lastBoxType = boxType;

        daText.Text = cur.text;
        daText.Delay = cur.speed.Value;
        daText.Sound = string.IsNullOrWhiteSpace(cur.sound) ? "dialogue" : cur.sound;
        daText.Y = DEFAULT_TEXT_Y;
        if (daText.Rows > 2) daText.Y -= LONG_TEXT_ADD;

        var ch = arrayCharacters.Count > 0 ? arrayCharacters[character] : null;
        if (ch != null)
        {
            ch.PlayAnim(cur.expression, daText.FinishedText);
            if (ch.CurAnim != null)
            {
                float rate = 24 - (((cur.speed.Value - 0.05f) / 5) * 480);
                ch.CurAnim.FrameRate = Math.Clamp(rate, 12, 48);
            }
        }
        currentText++;
        nextDialogueThing?.Invoke();
    }

    public static void UpdateBoxOffsets(FlxLiteSprite box)
    {
        box.CenterOffsets();
        box.UpdateHitbox();
        string n = box.CurAnim?.Name ?? "";
        if (n.StartsWith("angry")) box.Offset = new Vector2(50, 65);
        else if (n.StartsWith("center-angry")) box.Offset = new Vector2(50, 30);
        else box.Offset = new Vector2(10, 0);
        if (!box.FlipX) box.Offset = new Vector2(box.Offset.X, box.Offset.Y + 10);
    }

    /// <summary>Dev: member state like the native autotest dump (x, y, alpha, anim/frame, offset, scale, width).</summary>
    public string DevInfo()
    {
        var sb = new System.Text.StringBuilder();
        void S(string n, FlxLiteSprite m)
        {
            if (m == null) return;
            sb.Append($"  {n} x={m.X:0.###} y={m.Y:0.###} a={m.Alpha:0.###} vis={m.Visible}");
            if (m.CurAnim != null) sb.Append($" anim={m.CurAnim.Name} f={m.CurAnim.CurFrame}");
            sb.Append($" off={m.Offset.X:0.###},{m.Offset.Y:0.###} scale={m.Scale.X:0.####} w={m.Width:0.###} flip={m.FlipX}\n");
        }
        S("bgFade", bgFade);
        foreach (var c in arrayCharacters) S("char:" + c.CurCharacter, c);
        S("box", box);
        if (daText != null) sb.Append($"  text x={daText.X} y={daText.Y} w={(daText.Letters.Count == 0 ? 0 : daText.Letters.Max(l => l.X + l.Width) - daText.Letters.Min(l => l.X)):0.###} rows={daText.Rows} letters={daText.Letters.Count} visible={daText.Letters.Count(l => l.Visible)} finished={daText.FinishedText}\n");
        if (daText != null && daText.Letters.Count > 0)
        {
            var l = daText.Letters[0];
            sb.Append($"  letter0 '{l.Character}' x={l.X} y={l.Y} vis={l.Visible} alive={l.Alive} anim={l.CurAnim?.Name}({l.CurAnim?.Frames.Count}) w={l.Width} h={l.Height} off={l.Offset} sheet={(l.Sheet == null ? "null" : l.Sheet.Frames.Count.ToString())}\n");
        }
        return sb.ToString();
    }

    /// <summary>Draw in FlxSpriteGroup add order: bgFade, characters, box, text, skipText.</summary>
    public void Draw(SpriteBatch sb, Action<SpriteBatch, string, float, float, float> drawSkipText)
    {
        if (!Alive) return;
        bgFade?.Draw(sb);
        foreach (var c in arrayCharacters) c.Draw(sb);
        box?.Draw(sb);
        daText?.Draw(sb);
        // skipText = FlxText(FlxG.width - 320, FlxG.height - 30, 300, 'Press BACK to Skip', 16) RIGHT, OUTLINE_FAST black, border 2
        if (skipTextVisible) drawSkipText?.Invoke(sb, "Press BACK to Skip", 1280 - 320, 720 - 30, 300);
    }
}
