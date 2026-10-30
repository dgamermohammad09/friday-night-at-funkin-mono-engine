using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public enum HenchmenKillState
{
    WAIT,
    KILLING,
    SPEEDING_OFFSCREEN,
    SPEEDING,
    STOPPING
}

public class Limo : BaseStage
{
    private SpriteGroup grpLimoDancers;
    private BGSprite fastCar;
    private bool fastCarCanDrive = true;

    // event
    private HenchmenKillState limoKillingState = HenchmenKillState.WAIT;
    private BGSprite limoMetalPole;
    private BGSprite limoLight;
    private BGSprite limoCorpse;
    private BGSprite limoCorpseTwo;
    private BGSprite bgLimo;
    private SpriteGroup grpLimoParticles;
    private float dancersDiff = 320;
    private float limoSpeed = 0;

    public Limo(PlayScene game) : base(game) { }

    public override void Create()
    {
        var skyBG = new BGSprite("limo/limoSunset", -120, -50, 0.1f, 0.1f);
        Add(skyBG);

        if (!ClientPrefs.Data.LowQuality)
        {
            limoMetalPole = new BGSprite("gore/metalPole", -500, 220, 0.4f, 0.4f);
            Add(limoMetalPole);

            bgLimo = new BGSprite("limo/bgLimo", -150, 480, 0.4f, 0.4f, new string[] { "background limo pink" }, true);
            Add(bgLimo);

            limoCorpse = new BGSprite("gore/noooooo", -500, limoMetalPole.Y - 130, 0.4f, 0.4f, new string[] { "Henchmen on rail" }, true);
            Add(limoCorpse);

            limoCorpseTwo = new BGSprite("gore/noooooo", -500, limoMetalPole.Y, 0.4f, 0.4f, new string[] { "henchmen death" }, true);
            Add(limoCorpseTwo);

            grpLimoDancers = new SpriteGroup();
            Add(grpLimoDancers);

            for (int i = 0; i < 5; i++)
            {
                var dancer = new BackgroundDancer((370 * i) + dancersDiff + bgLimo.X, bgLimo.Y - 400);
                dancer.ScrollFactor = new Vector2(0.4f, 0.4f);
                grpLimoDancers.Add(dancer);
            }

            limoLight = new BGSprite("gore/coldHeartKiller", limoMetalPole.X - 180, limoMetalPole.Y - 80, 0.4f, 0.4f);
            Add(limoLight);

            grpLimoParticles = new SpriteGroup();
            Add(grpLimoParticles);

            // PRECACHE BLOOD
            var particle = new BGSprite("gore/stupidBlood", -400, -400, 0.4f, 0.4f, new string[] { "blood" }, false);
            particle.Alpha = 0.01f;
            grpLimoParticles.Add(particle);
            ResetLimoKill();

            // PRECACHE SOUND
            // Audio.LoadSound("dancerdeath");
            SetDefaultGF("gf-car");
        }

        fastCar = new BGSprite("limo/fastCarLol", -300, 160);
        fastCar.Active = true;
    }

    public override void CreatePost()
    {
        ResetFastCar();
        AddBehindGF(fastCar);
        
        var limo = new BGSprite("limo/limoDrive", -120, 550, 1, 1, new string[] { "Limo stage" }, true);
        AddBehindGF(limo);
    }

    public override void Update(float elapsed)
    {
        

        if (!ClientPrefs.Data.LowQuality)
        {
            for (int j = grpLimoParticles.Members.Count - 1; j >= 0; j--)
            {
                var spr = (BGSprite)grpLimoParticles.Members[j];
                if (spr.Animation.CurAnim != null && spr.Animation.CurAnim.Finished)
                {
                    spr.Kill();
                    grpLimoParticles.Remove(spr);
                }
            }

            switch (limoKillingState)
            {
                case HenchmenKillState.KILLING:
                    limoMetalPole.X += 5000 * elapsed;
                    limoLight.X = limoMetalPole.X - 180;
                    limoCorpse.X = limoLight.X - 50;
                    limoCorpseTwo.X = limoLight.X + 35;

                    for (int i = 0; i < grpLimoDancers.Members.Count; i++)
                    {
                        var dancer = (BackgroundDancer)grpLimoDancers.Members[i];
                        if (dancer.X < FNFGame.SCREEN_WIDTH * 1.5f && limoLight.X > (370 * i) + 170)
                        {
                            switch (i)
                            {
                                case 0:
                                case 3:
                                    if (i == 0) Audio.PlaySound("dancerdeath", 0.5f);

                                    string diffStr = i == 3 ? " 2 " : " ";
                                    var p1 = new BGSprite("gore/noooooo", dancer.X + 200, dancer.Y, 0.4f, 0.4f, new string[] { "hench leg spin" + diffStr + "PINK" }, false);
                                    grpLimoParticles.Add(p1);
                                    var p2 = new BGSprite("gore/noooooo", dancer.X + 160, dancer.Y + 200, 0.4f, 0.4f, new string[] { "hench arm spin" + diffStr + "PINK" }, false);
                                    grpLimoParticles.Add(p2);
                                    var p3 = new BGSprite("gore/noooooo", dancer.X, dancer.Y + 50, 0.4f, 0.4f, new string[] { "hench head spin" + diffStr + "PINK" }, false);
                                    grpLimoParticles.Add(p3);

                                    var blood = new BGSprite("gore/stupidBlood", dancer.X - 110, dancer.Y + 20, 0.4f, 0.4f, new string[] { "blood" }, false);
                                    blood.FlipX = true;
                                    blood.Angle = -57.5f;
                                    grpLimoParticles.Add(blood);
                                    break;
                                case 1:
                                    limoCorpse.Visible = true;
                                    break;
                                case 2:
                                    limoCorpseTwo.Visible = true;
                                    break;
                            }
                            dancer.X += FNFGame.SCREEN_WIDTH * 2;
                        }
                    }

                    if (limoMetalPole.X > FNFGame.SCREEN_WIDTH * 2)
                    {
                        ResetLimoKill();
                        limoSpeed = 800;
                        limoKillingState = HenchmenKillState.SPEEDING_OFFSCREEN;
                    }
                    break;

                case HenchmenKillState.SPEEDING_OFFSCREEN:
                    limoSpeed -= 4000 * elapsed;
                    bgLimo.X -= limoSpeed * elapsed;
                    if (bgLimo.X > FNFGame.SCREEN_WIDTH * 1.5f)
                    {
                        limoSpeed = 3000;
                        limoKillingState = HenchmenKillState.SPEEDING;
                    }
                    break;

                case HenchmenKillState.SPEEDING:
                    limoSpeed -= 2000 * elapsed;
                    if (limoSpeed < 1000) limoSpeed = 1000;

                    bgLimo.X -= limoSpeed * elapsed;
                    if (bgLimo.X < -275)
                    {
                        limoKillingState = HenchmenKillState.STOPPING;
                        limoSpeed = 800;
                    }
                    DancersParenting();
                    break;

                case HenchmenKillState.STOPPING:
                    bgLimo.X = PsychCoolUtil.Lerp(-150, bgLimo.X, (float)Math.Exp(-elapsed * 9));
                    if (Math.Round(bgLimo.X) == -150)
                    {
                        bgLimo.X = -150;
                        limoKillingState = HenchmenKillState.WAIT;
                    }
                    DancersParenting();
                    break;
            }
        }
    }

    public override void BeatHit()
    {
        if (!ClientPrefs.Data.LowQuality && grpLimoDancers != null)
        {
            foreach (var dancer in grpLimoDancers.Members)
            {
                ((BackgroundDancer)dancer).Dance();
            }
        }

        if (PsychGlobals.Random.Next(0, 10) == 0 && fastCarCanDrive)
            FastCarDrive();
    }
    
    // Timer substitute since FlxTimer is not directly available 
    private float carTimerDelay = -1;

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        if (carTimerDelay > 0)
        {
            carTimerDelay -= elapsed;
            if (carTimerDelay <= 0)
            {
                ResetFastCar();
                carTimerDelay = -1;
            }
        }
    }

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName == "Kill Henchmen")
        {
            KillHenchmen();
        }
    }

    private void DancersParenting()
    {
        for (int i = 0; i < grpLimoDancers.Members.Count; i++)
        {
            grpLimoDancers.Members[i].X = (370 * i) + dancersDiff + bgLimo.X;
        }
    }
    
    private void ResetLimoKill()
    {
        limoMetalPole.X = -500;
        limoMetalPole.Visible = false;
        limoLight.X = -500;
        limoLight.Visible = false;
        limoCorpse.X = -500;
        limoCorpse.Visible = false;
        limoCorpseTwo.X = -500;
        limoCorpseTwo.Visible = false;
    }

    private void ResetFastCar()
    {
        fastCar.X = -12600;
        fastCar.Y = PsychGlobals.Random.Next(140, 250);
        fastCar.Velocity.X = 0;
        fastCarCanDrive = true;
    }

    private void FastCarDrive()
    {
        Audio.PlaySound("game/stages/limo/carPass" + PsychGlobals.Random.Next(0, 2), 0.7f);

        fastCar.Velocity.X = PsychGlobals.Random.Next(30600, 39600);
        fastCarCanDrive = false;
        carTimerDelay = 2.0f; // 2 seconds
    }

    private void KillHenchmen()
    {
        if (!ClientPrefs.Data.LowQuality)
        {
            if (limoKillingState == HenchmenKillState.WAIT)
            {
                limoMetalPole.X = -400;
                limoMetalPole.Visible = true;
                limoLight.Visible = true;
                limoCorpse.Visible = false;
                limoCorpseTwo.Visible = false;
                limoKillingState = HenchmenKillState.KILLING;

                Achievements.AddScore("roadkill_enthusiast");
                Console.WriteLine("Henchmen kills achieved");
            }
        }
    }
}
