using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class ABotSpeaker : SpriteGroup
{
    private readonly int VIZ_MAX = 7;
    private readonly float[] VIZ_POS_X = { 0, 59, 56, 66, 54, 52, 51 };
    private readonly float[] VIZ_POS_Y = { 0, -8, -3.5f, -0.4f, 0.5f, 4.7f, 7 };

    public Sprite bg;
    public List<Sprite> vizSprites = new List<Sprite>();
    public Sprite eyeBg;
    public FlxAnimateSprite eyes;
    public FlxAnimateSprite speaker;

    // Analyzer is not fully implemented in C# yet, so volume levels are stubbed
    private List<float> volumes = new List<float>();

    public ABotSpeaker(float x = 0, float y = 0) : base(x, y)
    {
        bool antialias = ClientPrefs.Data.Antialiasing;

        bg = new Sprite(90, 20);
        bg.LoadGraphic(PsychPaths.Image("abot/stereoBG"));
        bg.Antialiasing = antialias;
        Add(bg);

        float vizX = 0;
        float vizY = 0;
        var vizFrames = PsychPaths.GetSparrowAtlas("abot/aBotViz");
        for (int i = 1; i <= VIZ_MAX; i++)
        {
            volumes.Add(0.0f);
            vizX += VIZ_POS_X[i - 1];
            vizY += VIZ_POS_Y[i - 1];
            
            Sprite viz = new Sprite(vizX + 140, vizY + 74);
            viz.Frames = vizFrames;
            viz.Animation.AddByPrefix("VIZ", "viz" + i, 0);
            viz.Animation.Play("VIZ", true);
            viz.Animation.CurAnim?.Finish(); // make it go to the lowest point
            viz.Antialiasing = antialias;
            vizSprites.Add(viz);
            viz.UpdateHitbox();
            viz.CenterOffsets();
            Add(viz);
        }

        eyeBg = new Sprite(-30, 215);
        eyeBg.MakeGraphic(1, 1, Color.White);
        eyeBg.Scale = new Vector2(160, 60);
        eyeBg.UpdateHitbox();
        Add(eyeBg);

        eyes = new FlxAnimateSprite(-10, 230);
        PsychPaths.LoadAnimateAtlas(eyes, "abot/systemEyes");
        eyes.Anim.AddBySymbolIndices("lookleft", "a bot eyes lookin", new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 }, 24, false);
        eyes.Anim.AddBySymbolIndices("lookright", "a bot eyes lookin", new int[] { 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35 }, 24, false);
        eyes.Anim.Play("lookright", true);
        if (eyes.Anim.Length > 0) eyes.Anim.CurFrame = eyes.Anim.Length - 1;
        Add(eyes);

        speaker = new FlxAnimateSprite(-65, -10);
        PsychPaths.LoadAnimateAtlas(speaker, "abot/abotSystem");
        speaker.Anim.AddBySymbol("anim", "Abot System", 24, false);
        speaker.Anim.Play("anim", true);
        if (speaker.Anim.Length > 0) speaker.Anim.CurFrame = speaker.Anim.Length - 1;
        speaker.Antialiasing = antialias;
        Add(speaker);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        
        // Stub: In original, spectrum analyzer logic goes here to update vizSprites frames
    }

    public void BeatHit()
    {
        speaker.Anim.Play("anim", true);
    }

    private bool lookingAtRight = true;

    public void LookLeft()
    {
        if (lookingAtRight) eyes.Anim.Play("lookleft", true);
        lookingAtRight = false;
    }

    public void LookRight()
    {
        if (!lookingAtRight) eyes.Anim.Play("lookright", true);
        lookingAtRight = true;
    }
}
