using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class Tank : BaseStage
{
    private BGSprite tankWatchtower;
    private BackgroundTank tankGround;
    private SpriteGroup tankmanRun;
    private SpriteGroup foregroundSprites;

    public Tank(PlayScene game) : base(game) { }

    public override void Create()
    {
        var sky = new BGSprite("tankSky", -400, -400, 0, 0);
        Add(sky);

        if (!ClientPrefs.Data.LowQuality)
        {
            var clouds = new BGSprite("tankClouds", PsychGlobals.Random.Next(-700, -100), PsychGlobals.Random.Next(-20, 21), 0.1f, 0.1f);
            clouds.Active = true;
            clouds.Velocity = new Vector2(PsychGlobals.Random.Next(50, 150) / 10f, 0);
            Add(clouds);

            var mountains = new BGSprite("tankMountains", -300, -20, 0.2f, 0.2f);
            mountains.SetGraphicSize((int)(1.2f * mountains.Width));
            mountains.UpdateHitbox();
            Add(mountains);

            var buildings = new BGSprite("tankBuildings", -200, 0, 0.3f, 0.3f);
            buildings.SetGraphicSize((int)(1.1f * buildings.Width));
            buildings.UpdateHitbox();
            Add(buildings);
        }

        var ruins = new BGSprite("tankRuins", -200, 0, 0.35f, 0.35f);
        ruins.SetGraphicSize((int)(1.1f * ruins.Width));
        ruins.UpdateHitbox();
        Add(ruins);

        if (!ClientPrefs.Data.LowQuality)
        {
            var smokeLeft = new BGSprite("smokeLeft", -200, -100, 0.4f, 0.4f, new string[] { "SmokeBlurLeft" }, true);
            Add(smokeLeft);
            var smokeRight = new BGSprite("smokeRight", 1100, -100, 0.4f, 0.4f, new string[] { "SmokeRight" }, true);
            Add(smokeRight);

            tankWatchtower = new BGSprite("tankWatchtower", 100, 50, 0.5f, 0.5f, new string[] { "watchtower gradient color" });
            Add(tankWatchtower);
        }

        tankGround = new BackgroundTank();
        Add(tankGround);

        tankmanRun = new SpriteGroup();
        Add(tankmanRun);

        var ground = new BGSprite("tankGround", -420, -150);
        ground.SetGraphicSize((int)(1.15f * ground.Width));
        ground.UpdateHitbox();
        Add(ground);

        foregroundSprites = new SpriteGroup();
        foregroundSprites.Add(new BGSprite("tank0", -500, 650, 1.7f, 1.5f, new string[] { "fg" }));
        if (!ClientPrefs.Data.LowQuality) foregroundSprites.Add(new BGSprite("tank1", -300, 750, 2f, 0.2f, new string[] { "fg" }));
        foregroundSprites.Add(new BGSprite("tank2", 450, 940, 1.5f, 1.5f, new string[] { "foreground" }));
        if (!ClientPrefs.Data.LowQuality) foregroundSprites.Add(new BGSprite("tank4", 1300, 900, 1.5f, 1.5f, new string[] { "fg" }));
        foregroundSprites.Add(new BGSprite("tank5", 1620, 700, 1.5f, 1.5f, new string[] { "fg" }));
        if (!ClientPrefs.Data.LowQuality) foregroundSprites.Add(new BGSprite("tank3", 1300, 1200, 3.5f, 2.5f, new string[] { "fg" }));

        // Default GFs
        if (SongName == "stress") SetDefaultGF("pico-speaker");
        else SetDefaultGF("gf-tankmen");
        
        if (IsStoryMode && !SeenCutscene)
        {
            switch (SongName)
            {
                case "ugh":
                    SetStartCallback(UghIntro);
                    break;
                case "guns":
                    SetStartCallback(GunsIntro);
                    break;
                case "stress":
                    SetStartCallback(StressIntro);
                    break;
            }
        }
    }

    public override void CreatePost()
    {
        Add(foregroundSprites);

        if (!ClientPrefs.Data.LowQuality)
        {
            foreach (var daGf in Game.GfGroup.Members)
            {
                if (daGf is PsychCharacter gf && gf.CurCharacter == "pico-speaker")
                {
                    var firstTank = new TankmenBG(20, 500, true);
                    firstTank.ResetShit(20, 1500, true);
                    firstTank.strumTime = 10;
                    firstTank.Visible = false;
                    tankmanRun.Add(firstTank);

                    for (int i = 0; i < TankmenBG.animationNotes.Count; i++)
                    {
                        if (PsychGlobals.Random.Next(0, 100) < 16)
                        {
                            var tankBih = new TankmenBG();
                            tankBih.strumTime = TankmenBG.animationNotes[i].Item1;
                            tankBih.ResetShit(500, 200 + PsychGlobals.Random.Next(50, 101), TankmenBG.animationNotes[i].Item2 < 2);
                            tankmanRun.Add(tankBih);
                        }
                    }
                    break;
                }
            }
        }
    }

    public override void CountdownTick(FNF_MonoGame.Gameplay.Countdown count, int num)
    {
        if (num % 2 == 0) EveryoneDance();
    }

    public override void BeatHit()
    {
        EveryoneDance();
    }

    private void EveryoneDance()
    {
        if (!ClientPrefs.Data.LowQuality && tankWatchtower != null) tankWatchtower.Dance();
        foreach (var spr in foregroundSprites.Members)
        {
            if (spr is BGSprite b) b.Dance();
        }
    }

    // Cutscenes omitted for Phase 2. They require FlxAnimate, CutsceneHandler, etc.
    // They will just start the countdown for now.
    
    private void UghIntro()
    {
        StartCountdown();
    }

    private void GunsIntro()
    {
        StartCountdown();
    }

    private void StressIntro()
    {
        StartCountdown();
    }
}
