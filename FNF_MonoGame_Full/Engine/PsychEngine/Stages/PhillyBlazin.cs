using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class PhillyBlazin : BaseStage
{
    // var rainShader:RainShader; // Shaders not yet ported
    private float rainTimeScale = 1;

    // private FlxTiledSprite scrollingSky; // FlxTiledSprite might not exist, fall back to BGSprite or stub
    private BGSprite scrollingSky; 
    private BGSprite skyAdditive;
    private BGSprite lightning;
    private BGSprite foregroundMultiply;
    private Sprite additionalLighten;
    
    private float lightningTimer = 3.0f;

    private ABotSpeaker abot;

    private PicoBlazinHandler picoFight = new PicoBlazinHandler();
    private DarnellBlazinHandler darnellFight = new DarnellBlazinHandler();

    // Tweens for lightning
    private bool doingLightningTween = false;
    private float lightningAlphaTimer = 0;
    private const float LIGHTNING_FULL_DURATION = 1.5f;
    private const float LIGHTNING_FADE_DURATION = 0.3f;
    private Color bfOriginalColor;
    private Color dadOriginalColor;
    private Color gfOriginalColor;
    private Color abotOriginalColor;

    public PhillyBlazin(PlayScene game) : base(game) { }

    public override void Create()
    {
        // FlxTransitionableState.skipNextTransOut = true;

        Action<BGSprite> setupScale = (spr) =>
        {
            spr.Scale = new Vector2(1.75f, 1.75f);
            spr.UpdateHitbox();
        };

        if (!ClientPrefs.Data.LowQuality)
        {
            // Fallback for FlxTiledSprite
            scrollingSky = new BGSprite("phillyBlazin/skyBlur", -500, -120);
            scrollingSky.Antialiasing = ClientPrefs.Data.Antialiasing;
            scrollingSky.ScrollFactor = Vector2.Zero;
            Add(scrollingSky);

            skyAdditive = new BGSprite("phillyBlazin/skyBlur", -600, -175, 0.0f, 0.0f);
            setupScale(skyAdditive);
            skyAdditive.Visible = false;
            Add(skyAdditive);
            
            lightning = new BGSprite("phillyBlazin/lightning", -50, -300, 0.0f, 0.0f, new string[] { "lightning0" }, false);
            setupScale(lightning);
            lightning.Visible = false;
            Add(lightning);
        }
        
        var phillyForegroundCity = new BGSprite("phillyBlazin/streetBlur", -600, -175, 0.0f, 0.0f);
        setupScale(phillyForegroundCity);
        Add(phillyForegroundCity);
        
        if (!ClientPrefs.Data.LowQuality)
        {
            foregroundMultiply = new BGSprite("phillyBlazin/streetBlur", -600, -175, 0.0f, 0.0f);
            setupScale(foregroundMultiply);
            // foregroundMultiply.blend = MULTIPLY; // Blend states are set on draw in MG, omitting for now
            foregroundMultiply.Visible = false;
            Add(foregroundMultiply);
            
            additionalLighten = new Sprite(-600, -175);
            additionalLighten.MakeGraphic(1, 1, Color.White);
            additionalLighten.ScrollFactor = Vector2.Zero;
            additionalLighten.Scale = new Vector2(2500, 2500);
            additionalLighten.UpdateHitbox();
            // additionalLighten.blend = ADD;
            additionalLighten.Visible = false;
            Add(additionalLighten);
        }

        abot = new ABotSpeaker(Game.GfGroup.X, Game.GfGroup.Y + 550);
        Add(abot);
        
        /*if(ClientPrefs.data.shaders)
            setupRainShader();*/

        // var _song = PlayState.SONG;
        // Game.gameOverSound = "fnf_loss_sfx-pico-gutpunch";
        // Game.gameOverLoop = "gameOver-pico";
        // Game.gameOverEnd = "gameOverEnd-pico";
        // Game.gameOverChar = "pico-blazin";
        // Game.deathDelay = 0.15f;

        SetDefaultGF("nene");
        Precache();
        
        if (IsStoryMode)
        {
            if (SongName == "blazin")
            {
                SetEndCallback(() =>
                {
                    // game.endingSong = true;
                    InCutscene = true;
                    Game.CanPause = false;
                    // FlxTransitionableState.skipNextTransIn = true;
                    Game.CamGame.Visible = false;
                    Game.CamHUD.Visible = false;
                    // game.startVideo('blazinCutscene');
                });
            }
        }
    }
    
    public override void CreatePost()
    {
        // Game.CamGame.FocusOn(Game.CamFollow.Position);
        // Game.CamGame.Fade(Color.Black, 1.5f, true, null, true);

        bfOriginalColor = new Color(0xDE, 0xDE, 0xDE);
        dadOriginalColor = new Color(0xDE, 0xDE, 0xDE);
        gfOriginalColor = new Color(0x88, 0x88, 0x88);
        abotOriginalColor = new Color(0x88, 0x88, 0x88);

        foreach (var character in Game.BoyfriendGroup.Members)
        {
            if (character is PsychCharacter c) c.Color = bfOriginalColor;
        }
        foreach (var character in Game.DadGroup.Members)
        {
            if (character is PsychCharacter c) c.Color = dadOriginalColor;
        }
        foreach (var character in Game.GfGroup.Members)
        {
            if (character is PsychCharacter c) c.Color = gfOriginalColor;
        }
        abot.Color = abotOriginalColor;

        foreach (var note in Game.UnspawnNotes)
        {
            if (note == null) continue;
            note.NoAnimation = true;
            note.NoMissAnimation = true;
        }
        
        Remove(Game.DadGroup, true);
        AddBehindBF(Game.DadGroup);
    }

    public override void StartSong()
    {
        abot.BeatHit(); // start music or sync
    }

    private void Precache()
    {
        for (int i = 1; i < 4; i++)
        {
            // Audio.LoadSound("lightning/Lightning" + i);
        }
    }

    public override void Update(float elapsed)
    {
        

        if (scrollingSky != null)
        {
            // Basic scrolling implementation for BGSprite since we lack FlxTiledSprite
            // scrollingSky.scrollX -= elapsed * 35;
        }
        
        lightningTimer -= elapsed;
        if (lightningTimer <= 0)
        {
            ApplyLightning();
            lightningTimer = PsychGlobals.Random.Next(700, 1500) / 100f; // 7 to 15
        }
    }
    
    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (doingLightningTween)
        {
            lightningAlphaTimer -= elapsed;

            if (lightningAlphaTimer <= 0)
            {
                doingLightningTween = false;
                
                skyAdditive.Visible = false;
                lightning.Visible = false;
                foregroundMultiply.Visible = false;
                additionalLighten.Visible = false;

                Game.Boyfriend.Color = bfOriginalColor;
                Game.Dad.Color = dadOriginalColor;
                Game.Gf.Color = gfOriginalColor;
                abot.Color = abotOriginalColor;
            }
            else
            {
                // Lerp for colors and alphas
                float fullT = Math.Max(0, lightningAlphaTimer / LIGHTNING_FULL_DURATION);
                float fadeT = Math.Max(0, (lightningAlphaTimer - (LIGHTNING_FULL_DURATION - LIGHTNING_FADE_DURATION)) / LIGHTNING_FADE_DURATION);
                fadeT = Math.Min(1, fadeT); // clamp to 0-1
                
                skyAdditive.Alpha = 0.7f * fullT;
                foregroundMultiply.Alpha = 0.64f * fullT;
                additionalLighten.Alpha = 0.3f * fadeT;

                Color darkenColor = new Color(0x60, 0x60, 0x60);
                Game.Boyfriend.Color = Color.Lerp(bfOriginalColor, darkenColor, fadeT);
                Game.Dad.Color = Color.Lerp(dadOriginalColor, darkenColor, fadeT);
                Game.Gf.Color = Color.Lerp(gfOriginalColor, darkenColor, fadeT);
                abot.Color = Color.Lerp(abotOriginalColor, darkenColor, fadeT);
            }
        }
    }

    private void ApplyLightning()
    {
        if (ClientPrefs.Data.LowQuality) return;

        skyAdditive.Visible = true;
        skyAdditive.Alpha = 0.7f;
        foregroundMultiply.Visible = true;
        foregroundMultiply.Alpha = 0.64f;
        additionalLighten.Visible = true;
        additionalLighten.Alpha = 0.3f;

        lightning.Visible = true;
        lightning.Animation.Play("lightning0", true);

        if (PsychGlobals.Random.Next(0, 100) < 65)
            lightning.X = PsychGlobals.Random.Next(-250, 280);
        else
            lightning.X = PsychGlobals.Random.Next(780, 900);

        Audio.PlaySound("lightning/Lightning" + PsychGlobals.Random.Next(1, 4));
        
        doingLightningTween = true;
        lightningAlphaTimer = LIGHTNING_FULL_DURATION;
    }

    // Note functions
    public override void GoodNoteHit(Note note)
    {
        rainTimeScale += 0.7f;
        picoFight.NoteHit(note);
        darnellFight.NoteHit(note);
    }
    
    public override void NoteMiss(Note note)
    {
        picoFight.NoteMiss(note);
        darnellFight.NoteMiss(note);
    }

    public override void NoteMissPress(int direction)
    {
        picoFight.NoteMissPress(direction);
        darnellFight.NoteMissPress(direction);
    }

    public override void OpponentNoteHit(Note note)
    {
        picoFight.NoteMiss(note); // Original says "picoFight.noteMiss(note);"
        darnellFight.NoteMiss(note);
    }
}
