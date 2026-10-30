using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class SchoolEvil : BaseStage
{
    private BGSprite bgGhouls;

    // Cutscene variables
    private float timerDelay = -1;
    private int cutsceneState = 0;
    private Sprite red;
    private FlxAnimateSprite senpaiEvil;

    public SchoolEvil(PlayScene game) : base(game) { }

    public override void Create()
    {
        // var _song = PlayState.SONG;
        // Game.gameOverSound = "fnf_loss_sfx-pixel";
        // Game.gameOverLoop = "gameOver-pixel";
        // Game.gameOverEnd = "gameOverEnd-pixel";
        // Game.gameOverChar = "bf-pixel-dead";

        int posX = 400;
        int posY = 200;

        BGSprite bg;
        if (!ClientPrefs.Data.LowQuality)
            bg = new BGSprite("weeb/animatedEvilSchool", posX, posY, 0.8f, 0.9f, new string[] { "background 2" }, true);
        else
            bg = new BGSprite("weeb/animatedEvilSchool_low", posX, posY, 0.8f, 0.9f);

        bg.Scale = new Vector2(PlayScene.daPixelZoom, PlayScene.daPixelZoom);
        bg.Antialiasing = false;
        Add(bg);
        
        SetDefaultGF("gf-pixel");

        Audio.PlayMusic("LunchboxScary", 0);
        Audio.Music.FadeIn(1, 0, 0.8f);

        if (IsStoryMode && !SeenCutscene)
        {
            InitDoof();
            SetStartCallback(SchoolIntro);
        }
    }

    public override void CreatePost()
    {
        // var trail = new FlxTrail(Game.Dad, null, 4, 24, 0.3f, 0.069f);
        // AddBehindDad(trail);
    }

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName == "Trigger BG Ghouls")
        {
            if (!ClientPrefs.Data.LowQuality && bgGhouls != null)
            {
                bgGhouls.Dance(true);
                bgGhouls.Visible = true;
            }
        }
    }

    public override void EventPushed(GameEvent eventNote)
    {
        if (eventNote.Event == "Trigger BG Ghouls")
        {
            if (!ClientPrefs.Data.LowQuality)
            {
                bgGhouls = new BGSprite("weeb/bgGhouls", -100, 190, 0.9f, 0.9f, new string[] { "BG freaks glitch instance" }, false);
                bgGhouls.SetGraphicSize((int)(bgGhouls.Width * PlayScene.daPixelZoom));
                bgGhouls.UpdateHitbox();
                bgGhouls.Visible = false;
                bgGhouls.Antialiasing = false;
                
                // Needs callback:
                // bgGhouls.Animation.FinishCallback = delegate(string name) { if (name == "BG freaks glitch instance") bgGhouls.Visible = false; };
                
                AddBehindGF(bgGhouls);
            }
        }
    }

    private void InitDoof()
    {
        // Stub for DialogueBox initialization
    }

    private void SchoolIntro()
    {
        InCutscene = true;
        
        red = new Sprite(-100, -100);
        red.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), new Color(0xFF, 0x1b, 0x31));
        red.ScrollFactor = Vector2.Zero;
        Add(red);

        senpaiEvil = new FlxAnimateSprite();
        senpaiEvil.Frames = PsychPaths.GetSparrowAtlas("weeb/senpaiCrazy");
        senpaiEvil.Anim.AddByPrefix("idle", "Senpai Pre Explosion", 24, false);
        senpaiEvil.SetGraphicSize((int)(senpaiEvil.Width * 6));
        senpaiEvil.ScrollFactor = Vector2.Zero;
        senpaiEvil.UpdateHitbox();
        senpaiEvil.ScreenCenter();
        senpaiEvil.X += 300;
        
        Game.CamHUD.Visible = false;

        cutsceneState = 1;
        timerDelay = 2.1f;
    }

    private float swagTimer = -1;
    
    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (timerDelay > 0)
        {
            timerDelay -= elapsed;
            if (timerDelay <= 0)
            {
                timerDelay = -1;

                if (cutsceneState == 1) // First timer finished
                {
                    // if (doof != null)
                    {
                        Add(senpaiEvil);
                        senpaiEvil.Alpha = 0;
                        swagTimer = 0.3f;
                        cutsceneState = 2;
                    }
                    // else StartCountdown();
                }
                else if (cutsceneState == 3) // deadTime timer finished
                {
                    Game.CamGame.Fade(Color.White, 1.6f, false);
                    cutsceneState = 0;
                }
            }
        }

        if (swagTimer > 0)
        {
            swagTimer -= elapsed;
            if (swagTimer <= 0)
            {
                senpaiEvil.Alpha += 0.15f;
                if (senpaiEvil.Alpha < 1)
                {
                    swagTimer = 0.3f; // reset
                }
                else
                {
                    swagTimer = -1;
                    senpaiEvil.Anim.Play("idle");
                    
                    Sound snd = Audio.PlaySound("Senpai_Dies");
                    snd.OnComplete += () => 
                    {
                        Remove(senpaiEvil);
                        senpaiEvil = null;
                        Remove(red);
                        red = null;
                        
                        Game.CamGame.Fade(Color.White, 0.01f, true, () => 
                        {
                            // Add(doof);
                            StartCountdown();
                            Game.CamHUD.Visible = true;
                        }, true);
                    };

                    timerDelay = 3.2f;
                    cutsceneState = 3;
                }
            }
        }
    }
}
