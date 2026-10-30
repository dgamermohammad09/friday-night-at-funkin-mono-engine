using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class TankmenBG : Sprite
{
    public static List<dynamic> animationNotes = new List<dynamic>();
    private float tankSpeed;
    private float endingOffset;
    private bool goingRight;
    public float strumTime;

    public TankmenBG() : this(0, 0, true) { }

    public TankmenBG(float x, float y, bool facingRight) : base(x, y)
    {
        tankSpeed = 0.7f;
        goingRight = facingRight;
        strumTime = 0;

        Frames = PsychPaths.GetSparrowAtlas("tankmanKilled1");
        Animation.AddByPrefix("run", "tankman running", 24, true);
        Animation.AddByPrefix("shot", "John Shot " + PsychGlobals.Random.Next(1, 3), 24, false); // 1 or 2
        Animation.Play("run");
        if (Animation.CurAnim.Frames.Length > 0)
            Animation.CurAnim.CurFrame = PsychGlobals.Random.Next(0, Animation.CurAnim.Frames.Length);
        
        Antialiasing = ClientPrefs.Data.Antialiasing;

        Scale = new Vector2(0.8f, 0.8f);
        UpdateHitbox();
    }

    public void ResetShit(float x, float y, bool goingRight)
    {
        X = x;
        Y = y;
        this.goingRight = goingRight;
        endingOffset = PsychGlobals.Random.Next(50, 200);
        tankSpeed = PsychGlobals.Random.Next(60, 100) / 100f; // 0.6 to 1
        FlipX = goingRight;
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        Visible = (X > -0.5f * FNFGame.SCREEN_WIDTH && X < 1.2f * FNFGame.SCREEN_WIDTH);

        if (Animation.CurAnim.Name == "run")
        {
            float speed = (float)(PsychConductor.songPosition - strumTime) * tankSpeed;
            if (goingRight)
                X = (0.02f * FNFGame.SCREEN_WIDTH - endingOffset) + speed;
            else
                X = (0.74f * FNFGame.SCREEN_WIDTH + endingOffset) - speed;
        }
        else if (Animation.CurAnim.Finished)
        {
            Kill();
        }

        if (PsychConductor.songPosition > strumTime)
        {
            Animation.Play("shot");
            if (goingRight)
            {
                Offset = new Vector2(300, 200);
            }
        }
    }
}
