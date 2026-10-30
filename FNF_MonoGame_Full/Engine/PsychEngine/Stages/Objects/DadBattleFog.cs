using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class DadBattleFog : SpriteGroup
{
    public DadBattleFog() : base()
    {
        Alpha = 0;
        // Blend mode ADD is handled at rendering, wait, does SpriteGroup have BlendMode?
        // Let's assume we can just set it on the children or the group itself.

        float offsetX = 200;
        
        BGSprite smoke1 = new BGSprite("smoke", -1550 + offsetX, 660 + PsychGlobals.Random.Next(-200, 200) / 10f, 1.2f, 1.05f);
        smoke1.SetGraphicSize((int)(smoke1.Width * PsychGlobals.Random.Next(110, 122) / 100f));
        smoke1.UpdateHitbox();
        smoke1.Velocity.X = PsychGlobals.Random.Next(150, 220) / 10f;
        smoke1.Active = true;
        smoke1.Antialiasing = ClientPrefs.Data.Antialiasing;
        Add(smoke1);

        BGSprite smoke2 = new BGSprite("smoke", 1550 + offsetX, 660 + PsychGlobals.Random.Next(-200, 200) / 10f, 1.2f, 1.05f);
        smoke2.SetGraphicSize((int)(smoke2.Width * PsychGlobals.Random.Next(110, 122) / 100f));
        smoke2.UpdateHitbox();
        smoke2.Velocity.X = PsychGlobals.Random.Next(-220, -150) / 10f;
        smoke2.Active = true;
        smoke2.FlipX = true;
        smoke2.Antialiasing = ClientPrefs.Data.Antialiasing;
        Add(smoke2);
    }
}
