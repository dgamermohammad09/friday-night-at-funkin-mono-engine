using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Sprites;

/// <summary>
/// Minimal FlxSprite animation helper used by Psych stage classes.
/// Wraps a Sprite/StaticStageProp and the underlying SpriteSheet so callers can
/// add/play animations by prefix/symbol/indices like the original Haxe source.
/// </summary>
public class AnimationHelper
{
    private readonly Sprite _sprite;

    public AnimationState CurAnim { get; private set; }

    public int Length => CurAnim?.Length ?? 0;

    public int CurFrame
    {
        get => _sprite.CurrentFrame;
        set => _sprite.CurrentFrame = value;
    }

    public event Action<string> OnComplete;
    public Action<string> FinishCallback;

    public AnimationHelper(Sprite sprite)
    {
        _sprite = sprite ?? throw new ArgumentNullException(nameof(sprite));
    }

    public void Add(string name, int[] indices, int framerate, bool looped = false)
    {
        AddByIndices(name, "", indices, "", framerate, looped);
    }

    public void AddByPrefix(string name, string prefix, int framerate, bool looped = false)
    {
        AddByIndices(name, prefix, null, "", framerate, looped);
    }

    public void AddByIndices(string name, string prefix, int[] indices, string postfix, int framerate, bool looped)
    {
        if (_sprite.Sheet == null) return;
        var matched = new List<SpriteFrame>();

        foreach (var kvp in _sprite.Sheet.Frames)
        {
            if (string.IsNullOrEmpty(prefix) || kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(postfix) || kvp.Key.EndsWith(postfix, StringComparison.OrdinalIgnoreCase))
                    matched.Add(kvp.Value);
            }
        }

        if (matched.Count == 0) return;

        matched = matched
            .OrderBy(f => f.Name, NaturalStringComparer.Instance)
            .ToList();

        if (indices != null && indices.Length > 0)
        {
            var picked = new List<SpriteFrame>();
            foreach (int idx in indices)
                if (idx >= 0 && idx < matched.Count) picked.Add(matched[idx]);
            matched = picked;
        }

        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = prefix,
            Indices = indices,
            Framerate = framerate,
            Loop = looped
        };
        foreach (var frame in matched)
        {
            anim.FrameRects.Add(frame.SourceRect);
            anim.FrameOffsets.Add(frame.Offset);
        }

        _sprite.Animations[name] = anim;
    }

    public void AddBySymbolIndices(string name, string symbol, int[] indices, int framerate, bool looped)
    {
        AddBySymbol(name, symbol, framerate, looped, indices);
    }

    public void AddBySymbol(string name, string symbol, int framerate, bool looped, int[] indices = null)
    {
        if (_sprite.Sheet == null) return;

        List<SpriteFrame> frames = null;
        if (indices == null || indices.Length == 0)
        {
            if (_sprite.Sheet.Animations.TryGetValue(symbol, out var list))
                frames = list;
        }
        else
        {
            if (_sprite.Sheet.Animations.TryGetValue(symbol, out var list))
            {
                frames = new List<SpriteFrame>();
                foreach (int idx in indices)
                    if (idx >= 0 && idx < list.Count) frames.Add(list[idx]);
            }
        }

        if (frames == null || frames.Count == 0) return;

        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = symbol,
            Indices = indices,
            Framerate = framerate,
            Loop = looped
        };
        foreach (var frame in frames)
        {
            anim.FrameRects.Add(frame.SourceRect);
            anim.FrameOffsets.Add(frame.Offset);
        }

        _sprite.Animations[name] = anim;
    }

    public void Play(string name, bool restart = false, bool reversed = false, int frame = 0)
    {
        if (_sprite.Animations == null || !_sprite.Animations.TryGetValue(name, out var anim)) return;

        _sprite.CurrentAnimation = name;
        _sprite.CurrentFrame = Math.Min(frame, Math.Max(0, anim.FrameRects.Count - 1));
        _sprite.AnimTimer = 0f;
        _sprite.AnimPlaying = true;
        _sprite.AnimFramerate = anim.Framerate;
        _sprite.AnimLoop = anim.Loop;

        CurAnim = new AnimationState(_sprite, anim);
    }

    public void Finish()
    {
        if (CurAnim == null) return;
        _sprite.CurrentFrame = Math.Max(0, CurAnim.Length - 1);
        _sprite.AnimPlaying = false;
    }

    internal void InvokeComplete(string name)
    {
        OnComplete?.Invoke(name);
        FinishCallback?.Invoke(name);
    }

    private class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new();
        public int Compare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    long na = 0, nb = 0;
                    while (i < a.Length && char.IsDigit(a[i])) { na = na * 10 + (a[i] - '0'); i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { nb = nb * 10 + (b[j] - '0'); j++; }
                    int c = na.CompareTo(nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}

/// <summary>
/// Read-only snapshot of the currently playing animation for stage scripts.
/// </summary>
public class AnimationState
{
    private readonly Sprite _sprite;
    private readonly ModchartAnimation _anim;

    public string Name => _anim?.Name;
    public bool Finished => !_sprite.AnimPlaying;
    public int Length => _anim?.FrameRects?.Count ?? 0;

    public int CurFrame
    {
        get => _sprite.CurrentFrame;
        set => _sprite.CurrentFrame = value;
    }

    public void Finish()
    {
        if (Length <= 0) return;
        CurFrame = Length - 1;
        _sprite.AnimPlaying = false;
    }

    public Rectangle[] Frames => _anim?.FrameRects?.ToArray();

    public AnimationState(Sprite sprite, ModchartAnimation anim)
    {
        _sprite = sprite;
        _anim = anim;
    }
}
