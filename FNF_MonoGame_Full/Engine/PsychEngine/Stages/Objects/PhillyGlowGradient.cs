using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class PhillyGlowGradient : Sprite
{
    public float originalY;
    public int originalHeight = 400;
    public float intendedAlpha = 1;

    public PhillyGlowGradient(float x, float y) : base(x, y)
    {
        originalY = y;

        LoadGraphic(PsychPaths.Image("philly/gradient"));
        ScrollFactor = new Vector2(0, 0.75f);
        SetGraphicSize(2000, originalHeight);
        UpdateHitbox();
        Antialiasing = ClientPrefs.Data.Antialiasing;
    }

    public override void Update(float elapsed)
    {
        

        int newHeight = (int)Math.Round(Height - 1000 * elapsed);
        if (newHeight > 0)
        {
            Alpha = intendedAlpha;
            SetGraphicSize(2000, newHeight);
            UpdateHitbox();
            Y = originalY + (originalHeight - Height);
        }
        else
        {
            Alpha = 0;
            Y = -5000;
        }

        base.Update(elapsed);
    }

    public void Bop()
    {
        SetGraphicSize(2000, originalHeight);
        UpdateHitbox();
        Y = originalY;
        Alpha = intendedAlpha;
    }
}
