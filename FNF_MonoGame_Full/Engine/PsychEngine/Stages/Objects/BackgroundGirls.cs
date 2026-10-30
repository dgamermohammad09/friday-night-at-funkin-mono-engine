using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class BackgroundGirls : Sprite
{
    private bool isPissed = true;
    private bool danceDir = false;

    public BackgroundGirls(float x, float y) : base(x, y)
    {
        // BG fangirls dissuaded
        Frames = PsychPaths.GetSparrowAtlas("weeb/bgFreaks");
        Antialiasing = false;
        SwapDanceType();

        SetGraphicSize((int)(Width * PlayScene.daPixelZoom));
        UpdateHitbox();
        Animation.Play("danceLeft");
    }

    public void SwapDanceType()
    {
        isPissed = !isPissed;
        if (!isPissed)
        {
            // Gets unpissed
            Animation.AddByIndices("danceLeft", "BG girls group", PsychCoolUtil.NumberArray(14), "", 24, false);
            Animation.AddByIndices("danceRight", "BG girls group", PsychCoolUtil.NumberArray(30, 15), "", 24, false);
        }
        else
        {
            // Pisses
            Animation.AddByIndices("danceLeft", "BG fangirls dissuaded", PsychCoolUtil.NumberArray(14), "", 24, false);
            Animation.AddByIndices("danceRight", "BG fangirls dissuaded", PsychCoolUtil.NumberArray(30, 15), "", 24, false);
        }
        Dance();
    }

    public void Dance()
    {
        danceDir = !danceDir;

        if (danceDir)
            Animation.Play("danceRight", true);
        else
            Animation.Play("danceLeft", true);
    }
}
