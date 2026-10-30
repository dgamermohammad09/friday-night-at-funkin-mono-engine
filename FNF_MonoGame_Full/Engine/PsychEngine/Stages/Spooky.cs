using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class Spooky : BaseStage
{
    private BGSprite halloweenBG;
    private Sprite halloweenWhite;
    private int lightningStrikeBeat = 0;
    private int lightningOffset = 8;
    
    // Timer state for cutscene
    private float whiteScreenAlphaTime = -1;
    private Sprite whiteScreen;

    // Timer states for lightning flash
    private float flashAlphaTimer = -1;
    private int flashState = 0;

    public Spooky(PlayScene game) : base(game) { }

    public override void Create()
    {
        if (!ClientPrefs.Data.LowQuality)
        {
            halloweenBG = new BGSprite("halloween_bg", -200, -100, 1f, 1f, new string[] { "halloweem bg0", "halloweem bg lightning strike" });
        }
        else
        {
            halloweenBG = new BGSprite("halloween_bg_low", -200, -100);
        }
        Add(halloweenBG);

        // Precache audio
        // Audio.LoadSound("thunder_1");
        // Audio.LoadSound("thunder_2");

        if (IsStoryMode && !SeenCutscene)
        {
            if (SongName == "monster")
            {
                SetStartCallback(MonsterCutscene);
            }
        }
    }

    public override void CreatePost()
    {
        halloweenWhite = new Sprite(-800, -400);
        halloweenWhite.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.White);
        halloweenWhite.Alpha = 0;
        halloweenWhite.ScrollFactor = Vector2.Zero;
        // halloweenWhite.blend = ADD;
        Add(halloweenWhite);
    }

    public override void BeatHit()
    {
        if (PsychGlobals.Random.Next(0, 100) < 10 && CurBeat > lightningStrikeBeat + lightningOffset)
        {
            LightningStrikeShit();
        }
    }

    private void LightningStrikeShit()
    {
        Audio.PlaySound("thunder_" + PsychGlobals.Random.Next(1, 3));
        
        if (!ClientPrefs.Data.LowQuality)
            halloweenBG.Animation.Play("halloweem bg lightning strike");

        lightningStrikeBeat = CurBeat;
        lightningOffset = PsychGlobals.Random.Next(8, 25);

        if (Game.Boyfriend.HasAnimation("scared"))
            Game.Boyfriend.PlayAnim("scared", true);

        if (Game.Dad.HasAnimation("scared"))
            Game.Dad.PlayAnim("scared", true);

        if (Game.Gf != null && Game.Gf.HasAnimation("scared"))
            Game.Gf.PlayAnim("scared", true);

        if (ClientPrefs.Data.CamZooms)
        {
            Game.CamGame.Zoom += 0.015f;
            Game.CamHUD.Zoom += 0.03f;

            if (!Game.CamZooming)
            {
                // FlxTween.tween(FlxG.camera, {zoom: defaultCamZoom}, 0.5);
                // For simplicity, just reset it directly or let it decay naturally
                // The port engine generally handles camera zoom decay automatically if `camZooming` is false in PsychEngine.
            }
        }

        if (ClientPrefs.Data.Flashing)
        {
            halloweenWhite.Alpha = 0.4f;
            
            // FlxTween.tween(halloweenWhite, {alpha: 0.5}, 0.075);
            // FlxTween.tween(halloweenWhite, {alpha: 0}, 0.25, {startDelay: 0.15});
            flashState = 1;
            flashAlphaTimer = 0.075f;
        }
    }

    private void MonsterCutscene()
    {
        InCutscene = true;
        Game.CamHUD.Visible = false;

        // FlxG.camera.focusOn(new FlxPoint(dad.getMidpoint().x + 150, dad.getMidpoint().y - 100));

        Audio.PlaySound("thunder_" + PsychGlobals.Random.Next(1, 3));
        
        if (Game.Gf != null) Game.Gf.PlayAnim("scared", true);
        Game.Boyfriend.PlayAnim("scared", true);

        whiteScreen = new Sprite();
        whiteScreen.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.White);
        whiteScreen.ScrollFactor = Vector2.Zero;
        // whiteScreen.blend = ADD;
        Add(whiteScreen);

        // FlxTween.tween(whiteScreen, {alpha: 0}, 1, { startDelay: 0.1 ...
        whiteScreenAlphaTime = 1.1f; // 0.1 delay + 1.0 duration
    }

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (whiteScreenAlphaTime > 0)
        {
            whiteScreenAlphaTime -= elapsed;
            
            if (whiteScreenAlphaTime <= 1.0f)
            {
                if (whiteScreenAlphaTime <= 0)
                {
                    Remove(whiteScreen);
                    whiteScreen = null;
                    Game.CamHUD.Visible = true;
                    StartCountdown();
                }
                else
                {
                    whiteScreen.Alpha = whiteScreenAlphaTime;
                }
            }
        }

        if (flashState > 0)
        {
            flashAlphaTimer -= elapsed;
            if (flashAlphaTimer <= 0)
            {
                if (flashState == 1) // Finished 0.4 -> 0.5
                {
                    halloweenWhite.Alpha = 0.5f;
                    flashState = 2;
                    flashAlphaTimer = 0.15f; // Wait delay
                }
                else if (flashState == 2) // Finished wait delay
                {
                    flashState = 3;
                    flashAlphaTimer = 0.25f; // Fade out duration
                }
                else if (flashState == 3)
                {
                    halloweenWhite.Alpha = 0;
                    flashState = 0;
                }
            }
            else
            {
                if (flashState == 1)
                {
                    float t = 1.0f - (flashAlphaTimer / 0.075f);
                    halloweenWhite.Alpha = PsychCoolUtil.Lerp(0.4f, 0.5f, t);
                }
                else if (flashState == 3)
                {
                    float t = 1.0f - (flashAlphaTimer / 0.25f);
                    halloweenWhite.Alpha = PsychCoolUtil.Lerp(0.5f, 0f, t);
                }
            }
        }
    }
}
