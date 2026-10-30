using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class MallCrowd : BGSprite
{
    public float heyTimer = 0;

    public MallCrowd(float x = 0, float y = 0, string sprite = "christmas/bottomBop", string idle = "Bottom Level Boppers Idle", string hey = "Bottom Level Boppers HEY") 
        : base(sprite, x, y, 0.9f, 0.9f, new string[] { idle })
    {
        Animation.AddByPrefix("hey", hey, 24, false);
        Antialiasing = ClientPrefs.Data.Antialiasing;
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        

        if (heyTimer > 0)
        {
            heyTimer -= elapsed;
            if (heyTimer <= 0)
            {
                Dance(true);
                heyTimer = 0;
            }
        }
    }

    public override void Dance(bool forcePlay = false)
    {
        if (heyTimer > 0) return;
        base.Dance(forcePlay);
    }
}
