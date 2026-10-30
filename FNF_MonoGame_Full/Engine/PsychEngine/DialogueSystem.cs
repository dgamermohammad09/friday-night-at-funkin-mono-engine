using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Dialogue system — cutscene dialogues before/after songs.
/// Direct port of cutscenes/DialogueBoxPsych.hx and DialogueCharacter.hx.
/// Xbox compatible: no platform-specific code.
/// </summary>

// ═══════════════════════════════════════════
// Data Types
// ═══════════════════════════════════════════

public class DialogueFile
{
    [JsonPropertyName("dialogue")] public List<DialogueLine> Dialogue { get; set; } = new();
}

public class DialogueLine
{
    [JsonPropertyName("portrait")] public string Portrait { get; set; }
    [JsonPropertyName("expression")] public string Expression { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; }
    [JsonPropertyName("boxState")] public string BoxState { get; set; }
    [JsonPropertyName("speed")] public float? Speed { get; set; }
    [JsonPropertyName("sound")] public string Sound { get; set; }
}

public class DialogueCharacterFile
{
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("dialogue_pos")] public string DialoguePos { get; set; } = "left";
    [JsonPropertyName("no_antialiasing")] public bool NoAntialiasing { get; set; } = false;
    [JsonPropertyName("animations")] public List<DialogueAnimData> Animations { get; set; } = new();
    [JsonPropertyName("position")] public float[] Position { get; set; } = { 0, 0 };
    [JsonPropertyName("scale")] public float Scale { get; set; } = 0.7f;
}

public class DialogueAnimData
{
    [JsonPropertyName("anim")] public string Anim { get; set; } = "";
    [JsonPropertyName("loop_name")] public string LoopName { get; set; } = "";
    [JsonPropertyName("loop_offsets")] public int[] LoopOffsets { get; set; } = { 0, 0 };
    [JsonPropertyName("idle_name")] public string IdleName { get; set; } = "";
    [JsonPropertyName("idle_offsets")] public int[] IdleOffsets { get; set; } = { 0, 0 };
}

// ═══════════════════════════════════════════
// Dialogue Manager
// ═══════════════════════════════════════════

/// <summary>
/// Manages a dialogue cutscene sequence.
/// </summary>
public class DialogueManager
{
    public const float DEFAULT_TEXT_X = 175;
    public const float DEFAULT_TEXT_Y = 460;
    public const float LONG_TEXT_ADD = 24;

    public DialogueFile Data { get; private set; }
    public int CurrentLine { get; private set; } = 0;
    public bool IsFinished { get; private set; }
    public bool IsActive { get; private set; }

    // Current dialogue state
    public string CurrentText { get; private set; } = "";
    public string CurrentPortrait { get; private set; } = "";
    public string CurrentExpression { get; private set; } = "";
    public string CurrentBoxState { get; private set; } = "normal";
    public float CurrentSpeed { get; private set; } = 0.05f;

    // Typing effect
    private float _typeTimer;
    private int _typeIndex;
    private string _fullText = "";

    // Callbacks
    public Action OnFinish { get; set; }
    public Action OnNextLine { get; set; }
    public Action OnSkip { get; set; }

    // Loaded dialogue characters
    private readonly Dictionary<string, DialogueCharacterFile> _characters = new(StringComparer.OrdinalIgnoreCase);

    public DialogueManager() { }

    /// <summary>
    /// Load a dialogue JSON file.
    /// </summary>
    public bool LoadDialogue(string path)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            Console.WriteLine($"[DIALOGUE] File not found: {path}");
            return false;
        }

        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
            Data = JsonSerializer.Deserialize<DialogueFile>(json);
            if (Data?.Dialogue == null || Data.Dialogue.Count == 0)
            {
                Console.WriteLine("[DIALOGUE] No dialogue lines found.");
                return false;
            }
            Console.WriteLine($"[DIALOGUE] Loaded {Data.Dialogue.Count} lines from {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIALOGUE] Parse error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load a dialogue character JSON.
    /// </summary>
    public DialogueCharacterFile LoadCharacter(string name, string basePath)
    {
        if (_characters.TryGetValue(name, out var cached))
            return cached;

        string path = Path.Combine(basePath, "images", "dialogue", name + ".json");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            Console.WriteLine($"[DIALOGUE] Character not found: {name}");
            return null;
        }

        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
            var charData = JsonSerializer.Deserialize<DialogueCharacterFile>(json);
            _characters[name] = charData;
            return charData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIALOGUE] Character parse error ({name}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Start the dialogue sequence.
    /// </summary>
    public void Start()
    {
        if (Data == null || Data.Dialogue.Count == 0)
        {
            IsFinished = true;
            OnFinish?.Invoke();
            return;
        }

        CurrentLine = 0;
        IsActive = true;
        IsFinished = false;
        ShowLine(0);
    }

    /// <summary>
    /// Advance to the next dialogue line, or finish if at the end.
    /// </summary>
    public void Next()
    {
        if (!IsActive) return;

        // If still typing, complete the line instantly
        if (_typeIndex < _fullText.Length)
        {
            _typeIndex = _fullText.Length;
            CurrentText = _fullText;
            return;
        }

        CurrentLine++;
        OnNextLine?.Invoke();

        if (CurrentLine >= Data.Dialogue.Count)
        {
            Finish();
            return;
        }

        ShowLine(CurrentLine);
    }

    /// <summary>
    /// Skip the entire dialogue.
    /// </summary>
    public void Skip()
    {
        OnSkip?.Invoke();
        Finish();
    }

    /// <summary>
    /// Update typing effect.
    /// </summary>
    public void Update(float elapsed)
    {
        if (!IsActive || IsFinished) return;

        if (_typeIndex < _fullText.Length)
        {
            _typeTimer += elapsed;
            while (_typeTimer >= CurrentSpeed && _typeIndex < _fullText.Length)
            {
                _typeTimer -= CurrentSpeed;
                _typeIndex++;
                CurrentText = _fullText[.._typeIndex];
            }
        }
    }

    private void ShowLine(int index)
    {
        var line = Data.Dialogue[index];
        CurrentPortrait = line.Portrait ?? "";
        CurrentExpression = line.Expression ?? "";
        CurrentBoxState = line.BoxState ?? "normal";
        CurrentSpeed = line.Speed ?? 0.05f;
        _fullText = line.Text ?? "";
        _typeIndex = 0;
        _typeTimer = 0;
        CurrentText = "";
    }

    private void Finish()
    {
        IsActive = false;
        IsFinished = true;
        OnFinish?.Invoke();
    }
}
