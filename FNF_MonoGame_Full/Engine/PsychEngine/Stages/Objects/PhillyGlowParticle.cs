using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class PhillyGlowParticle : Sprite
{
    private float lifeTime = 0;
    private float decay = 0;
    private float originalScale = 1;

    public PhillyGlowParticle(float x = 0, float y = 0, Color? color = null) : base(x, y)
    {
        if (color.HasValue)
            Color = color.Value;
        else
            Color = Color.White;

        LoadGraphic(PsychPaths.Image("philly/particle"));
        Antialiasing = ClientPrefs.Data.Antialiasing;
        Start();
    }

    public void Start()
    {
        lifeTime = PsychGlobals.Random.Next(60, 90) / 100f;
        decay = PsychGlobals.Random.Next(80, 100) / 100f;
        
        if (!ClientPrefs.Data.Flashing)
        {
            decay *= 0.5f;
            Alpha = 0.5f;
        }
        else
        {
            Alpha = 1;
        }

        originalScale = PsychGlobals.Random.Next(75, 100) / 100f;
        Scale = new Vector2(originalScale, originalScale);

        ScrollFactor = new Vector2(PsychGlobals.Random.Next(30, 75) / 100f, PsychGlobals.Random.Next(65, 75) / 100f);
        Velocity = new Vector2(PsychGlobals.Random.Next(-40, 40), PsychGlobals.Random.Next(-250, -175));
        Acceleration = new Vector2(PsychGlobals.Random.Next(-10, 10), 25);
    }

    public override void Update(float elapsed)
    {
        
        lifeTime -= elapsed;
        
        if (lifeTime < 0)
        {
            lifeTime = 0;
            Alpha -= decay * elapsed;
            if (Alpha > 0)
            {
                Scale = new Vector2(originalScale * Alpha, originalScale * Alpha);
            }
        }
        base.Update(elapsed);
    }
}
