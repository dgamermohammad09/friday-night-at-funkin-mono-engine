using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class BackgroundDancer : Sprite
{
    private bool danceDir = false;

    public BackgroundDancer(float x, float y) : base(x, y)
    {
        Frames = PsychPaths.GetSparrowAtlas("limo/limoDancer");
        Animation.AddByIndices("danceLeft", "bg dancer sketch PINK", new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, "", 24, false);
        Animation.AddByIndices("danceRight", "bg dancer sketch PINK", new int[] { 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29 }, "", 24, false);
        Animation.Play("danceLeft");
        Antialiasing = ClientPrefs.Data.Antialiasing;
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
