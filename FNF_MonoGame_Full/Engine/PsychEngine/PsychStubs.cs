using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Sprites
{
    public partial class Sprite : FNF_MonoGame.Scenes.PlayScene.StaticStageProp, FNF_MonoGame.Engine.PsychEngine.Objects.PsychCharacter
    {
        public virtual void Update(float elapsed) { }
        public virtual void Draw() { }

        public AnimationHelper Animation { get; private set; }
        public AnimationHelper Anim { get; private set; }
        public SpriteSheet Frames { get => Sheet; set => Sheet = value; }
        public Vector2 Offset { get; set; }
        public virtual void PlayAnim(string name, bool force = false, bool reversed = false, int frame = 0) { }
        public bool HasAnimation(string name) => false;
        public string GetAnimationName() => CurrentAnimation;
        public float Width { get => base.width; set { } }
        public float Height { get => base.height; set { } }
        public Color Color { get; set; }
        public Vector2 ScrollFactor { get; set; }
        public new bool Antialiasing { get; set; }
        public Vector2 Scale { get; set; } = Vector2.One;
        public Vector2 Velocity { get; set; }
        public Vector2 Acceleration { get; set; }
        public new bool Active { get; set; } = true;
        public bool Danced { get; set; }
        public bool SpecialAnim { get; set; }
        public string CurCharacter { get; set; }

        public Sprite() : this(0, 0) { }

        public Sprite(float x, float y)
        {
            X = x;
            Y = y;
            Animation = new AnimationHelper(this);
            Anim = new AnimationHelper(this);
            OnAnimationFinished += name =>
            {
                Animation?.InvokeComplete(name);
                Anim?.InvokeComplete(name);
            };
        }

        public void LoadGraphic(string path)
        {
            string basePath = Path.ChangeExtension(PsychPaths.Image(path), null);
            if (!string.IsNullOrEmpty(basePath))
                Sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
        }

        public void MakeGraphic(int width, int height, Color color)
        {
            var tex = new Texture2D(FNFGame.Instance.GraphicsDevice, width, height);
            var data = new Color[width * height];
            for (int i = 0; i < data.Length; i++) data[i] = color;
            tex.SetData(data);
            Sheet = null;
            Texture = tex;
            SourceRect = new Rectangle(0, 0, width, height);
        }

        public void UpdateHitbox() { }
        public void SetGraphicSize(int width = 0, int height = 0) { }
        public void CenterOffsets() { }

        public virtual void Kill() { Active = false; Visible = false; }

        public void ScreenCenter(Axes axes = Axes.XY)
        {
            if (axes == Axes.X || axes == Axes.XY)
                X = (FNFGame.SCREEN_WIDTH - Width) / 2f;
            if (axes == Axes.Y || axes == Axes.XY)
                Y = (FNFGame.SCREEN_HEIGHT - Height) / 2f;
        }

        public bool IsMouseOver() { return false; }
        public bool MouseLeftPressed() { return false; }
        public bool MouseRightPressed() { return false; }
        public bool MouseMiddlePressed() { return false; }
    }

    public enum Axes { X, Y, XY }

    public class SpriteGroup : Sprite
    {
        public SpriteGroup() : base() { }
        public SpriteGroup(float x, float y) : base(x, y) { }
        public void Add(FNF_MonoGame.Scenes.PlayScene.StaticStageProp sprite) { Members.Add(sprite); }
        public void Remove(FNF_MonoGame.Scenes.PlayScene.StaticStageProp sprite) { Members.Remove(sprite); }
        public void RemoveAt(int index) { Members.RemoveAt(index); }
        public System.Collections.Generic.List<FNF_MonoGame.Scenes.PlayScene.StaticStageProp> Members { get; set; } = new();
    }

    public class FlxAnimateSprite : Sprite
    {
        public FlxAnimateSprite() : base() { }
        public FlxAnimateSprite(float x, float y) : base(x, y) { }
    }
}

namespace FNF_MonoGame.Engine.PsychEngine.Objects
{
    public interface PsychCharacter
    {
        Color Color { get; set; }
        bool Danced { get; set; }
        bool SpecialAnim { get; set; }
        string CurCharacter { get; set; }
        string GetAnimationName();
        bool HasAnimation(string name);
        void PlayAnim(string name, bool force = false, bool reversed = false, int frame = 0);
    }

    public class TextObject : FNF_MonoGame.Sprites.Sprite
    {
        public string Text { get; set; }
        public string Format { get; set; }
        public TextObject(float x, float y, string text)
        {
            X = x;
            Y = y;
            Text = text;
        }
        public TextObject(float x, float y) : this(x, y, "") { }
    }
}

namespace FNF_MonoGame.Gameplay
{
    public class EventNote
    {
        public float strumTime;
        public string eventName;
        public string value1;
        public string value2;
    }
}

namespace FNF_MonoGame.Engine
{
    public class Sound {
        public System.Action OnComplete;
        public double Position { get; set; }
        public float Volume { get; set; } = 1f;
        public virtual Sound Play() { OnComplete?.Invoke(); return this; }
        public virtual void Stop() { }
    }
    public class Audio { }

    public class Basic
    {
        public virtual void Update(float elapsed) { }
    }
    
    public class HaxeInterpreter {
        /// <summary>
        /// Mirrors BaseCharacter.hx setScale: position the character's top-left corner
        /// given the stage feet position and the idle frame size after scaling.
        /// </summary>
        public static (float, float) EvalSetScale(float stageX, float stageY, float widthAfterScale, float heightAfterScale, float globalOffsetX, float globalOffsetY)
        {
            float x = stageX - widthAfterScale / 2f + globalOffsetX;
            float y = stageY - heightAfterScale + globalOffsetY;
            return (x, y);
        }

        /// <summary>
        /// Mirrors Bopper.hx getScreenPosition: offset the base position by
        /// (animOffset - globalOffset) * scale so each animation frame lines up.
        /// </summary>
        public static (float, float) EvalGetScreenPosition(float charX, float charY, float scrollX, float scrollY, float animOffsetX, float animOffsetY, float globalOffsetX, float globalOffsetY, float scaleX, float scaleY)
        {
            float x = charX - (animOffsetX - globalOffsetX) * scaleX;
            float y = charY - (animOffsetY - globalOffsetY) * scaleY;
            return (x, y);
        }
    } 
    public class HaxeFlixelModBridge {
        public void LoadCharacterScript(object obj, string str) {}
    } 
    public class HaxeScriptEngine {
        public void SetVariable(string name, object value) {}
        public float EvaluateFloat(string code) { return 0f; }
    } 
    public class PsychEvents {
        public static void Trigger(params object[] args) { }
    }
    public class PsychGlobals {
        public static Random Random { get; set; } = new Random();
    }
}

// PsychHScriptEngine: real implementation in Engine/PureHScript/PsychHScriptEngine.cs (hscript-iris)


