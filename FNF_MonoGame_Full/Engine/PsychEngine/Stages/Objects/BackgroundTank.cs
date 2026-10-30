using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class BackgroundTank : BGSprite
{
    public float offsetX = 400;
    public float offsetY = 1300;
    public float tankSpeed = 0;
    public float tankAngle = 0;

    public BackgroundTank() 
        : base("tankRolling", 0, 0, 0.5f, 0.5f, new string[] { "BG tank w lighting" }, true)
    {
        tankSpeed = PsychGlobals.Random.Next(500, 700) / 100f; // Random between 5 and 7
        tankAngle = PsychGlobals.Random.Next(-90, 45);
        Antialiasing = ClientPrefs.Data.Antialiasing;
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        

        tankAngle += elapsed * tankSpeed;
        Angle = tankAngle - 90 + 15;
        X = offsetX + 1500 * (float)Math.Cos(Math.PI / 180 * (tankAngle + 180));
        Y = offsetY + 1100 * (float)Math.Sin(Math.PI / 180 * (tankAngle + 180));
    }
}
