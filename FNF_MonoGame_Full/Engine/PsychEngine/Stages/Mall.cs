using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class Mall : BaseStage
{
    private BGSprite upperBoppers;
    private MallCrowd bottomBoppers;
    private BGSprite santa;

    public Mall(PlayScene game) : base(game) { }

    public override void Create()
    {
        var bg = new BGSprite("christmas/bgWalls", -1000, -500, 0.2f, 0.2f);
        bg.SetGraphicSize((int)(bg.Width * 0.8f));
        bg.UpdateHitbox();
        Add(bg);

        if (!ClientPrefs.Data.LowQuality)
        {
            upperBoppers = new BGSprite("christmas/upperBop", -240, -90, 0.33f, 0.33f, new string[] { "Upper Crowd Bob" });
            upperBoppers.SetGraphicSize((int)(upperBoppers.Width * 0.85f));
            upperBoppers.UpdateHitbox();
            Add(upperBoppers);

            var bgEscalator = new BGSprite("christmas/bgEscalator", -1100, -600, 0.3f, 0.3f);
            bgEscalator.SetGraphicSize((int)(bgEscalator.Width * 0.9f));
            bgEscalator.UpdateHitbox();
            Add(bgEscalator);
        }

        var tree = new BGSprite("christmas/christmasTree", 370, -250, 0.40f, 0.40f);
        Add(tree);

        bottomBoppers = new MallCrowd(-300, 140);
        Add(bottomBoppers);

        var fgSnow = new BGSprite("christmas/fgSnow", -600, 700);
        Add(fgSnow);

        santa = new BGSprite("christmas/santa", -840, 150, 1, 1, new string[] { "santa idle in fear" });
        Add(santa);
        
        // Precache audio
        // Audio.LoadSound("Lights_Shut_off");
        
        SetDefaultGF("gf-christmas");

        if (IsStoryMode && !SeenCutscene)
            SetEndCallback(EggnogEndCutscene);
    }

    public override void CountdownTick(FNF_MonoGame.Gameplay.Countdown count, int num) => EveryoneDance();
    public override void BeatHit() => EveryoneDance();

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName == "Hey!")
        {
            string val = value1.ToLower().Trim();
            if (val == "bf" || val == "boyfriend" || val == "0") return;

            bottomBoppers.Animation.Play("hey", true);
            bottomBoppers.heyTimer = flValue2.HasValue ? flValue2.Value : 0;
        }
    }

    private void EveryoneDance()
    {
        if (!ClientPrefs.Data.LowQuality)
            upperBoppers.Dance(true);

        bottomBoppers.Dance(true);
        santa.Dance(true);
    }

    private float timerDelay = -1;

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        if (timerDelay > 0)
        {
            timerDelay -= elapsed;
            if (timerDelay <= 0)
            {
                EndSong();
                timerDelay = -1;
            }
        }
    }

    private void EggnogEndCutscene()
    {
        if (PlayScene.storyPlaylist.Count <= 1)
        {
            EndSong();
            return;
        }

        string nextSong = PsychPaths.FormatToSongPath(PlayScene.storyPlaylist[1]);
        if (nextSong == "winter-horrorland")
        {
            Audio.PlaySound("Lights_Shut_off");

            var blackShit = new Sprite(-FNFGame.SCREEN_WIDTH * Game.CamGame.Zoom, -FNFGame.SCREEN_HEIGHT * Game.CamGame.Zoom);
            blackShit.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 3), (int)(FNFGame.SCREEN_HEIGHT * 3), Color.Black);
            blackShit.ScrollFactor = Vector2.Zero;
            Add(blackShit);
            
            Game.CamHUD.Visible = false;
            
            InCutscene = true;
            Game.CanPause = false;

            timerDelay = 1.5f; // End song after 1.5 seconds
        }
        else
        {
            EndSong();
        }
    }
}
