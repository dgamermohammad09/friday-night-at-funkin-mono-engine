using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class School : BaseStage
{
    private BackgroundGirls bgGirls;
    
    // For schoolIntro cutscene timer
    private float blackAlphaTimer = -1;
    private Sprite blackScreen;

    public School(PlayScene game) : base(game) { }

    public override void Create()
    {
        // var _song = PlayState.SONG;
        // Game.gameOverSound = "fnf_loss_sfx-pixel";
        // Game.gameOverLoop = "gameOver-pixel";
        // Game.gameOverEnd = "gameOverEnd-pixel";
        // Game.gameOverChar = "bf-pixel-dead";

        var bgSky = new BGSprite("weeb/weebSky", 0, 0, 0.1f, 0.1f);
        Add(bgSky);
        bgSky.Antialiasing = false;

        float repositionShit = -200;

        var bgSchool = new BGSprite("weeb/weebSchool", repositionShit, 0, 0.6f, 0.90f);
        Add(bgSchool);
        bgSchool.Antialiasing = false;

        var bgStreet = new BGSprite("weeb/weebStreet", repositionShit, 0, 0.95f, 0.95f);
        Add(bgStreet);
        bgStreet.Antialiasing = false;

        int widShit = (int)(bgSky.Width * PlayScene.daPixelZoom);
        
        if (!ClientPrefs.Data.LowQuality)
        {
            var fgTrees = new BGSprite("weeb/weebTreesBack", repositionShit + 170, 130, 0.9f, 0.9f);
            fgTrees.SetGraphicSize((int)(widShit * 0.8f));
            fgTrees.UpdateHitbox();
            Add(fgTrees);
            fgTrees.Antialiasing = false;
        }

        var bgTrees = new Sprite(repositionShit - 380, -800);
        bgTrees.Frames = PsychPaths.GetPackerAtlas("weeb/weebTrees");
        bgTrees.Animation.Add("treeLoop", new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, 12);
        bgTrees.Animation.Play("treeLoop");
        bgTrees.ScrollFactor = new Vector2(0.85f, 0.85f);
        Add(bgTrees);
        bgTrees.Antialiasing = false;

        if (!ClientPrefs.Data.LowQuality)
        {
            var treeLeaves = new BGSprite("weeb/petals", repositionShit, -40, 0.85f, 0.85f, new string[] { "PETALS ALL" }, true);
            treeLeaves.SetGraphicSize(widShit);
            treeLeaves.UpdateHitbox();
            Add(treeLeaves);
            treeLeaves.Antialiasing = false;
        }

        bgSky.SetGraphicSize(widShit);
        bgSchool.SetGraphicSize(widShit);
        bgStreet.SetGraphicSize(widShit);
        bgTrees.SetGraphicSize((int)(widShit * 1.4f));

        bgSky.UpdateHitbox();
        bgSchool.UpdateHitbox();
        bgStreet.UpdateHitbox();
        bgTrees.UpdateHitbox();

        if (!ClientPrefs.Data.LowQuality)
        {
            bgGirls = new BackgroundGirls(-100, 190);
            bgGirls.ScrollFactor = new Vector2(0.9f, 0.9f);
            Add(bgGirls);
        }
        
        SetDefaultGF("gf-pixel");

        switch (SongName)
        {
            case "senpai":
                Audio.PlayMusic("Lunchbox", 0);
                Audio.Music.FadeIn(1, 0, 0.8f);
                break;
            case "roses":
                Audio.PlaySound("ANGRY_TEXT_BOX");
                break;
        }
        
        if (IsStoryMode && !SeenCutscene)
        {
            if (SongName == "roses") Audio.PlaySound("ANGRY");
            InitDoof();
            SetStartCallback(SchoolIntro);
        }
    }

    public override void BeatHit()
    {
        if (bgGirls != null) bgGirls.Dance();
    }

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName == "BG Freaks Expression")
        {
            if (bgGirls != null) bgGirls.SwapDanceType();
        }
    }

    // var doof:DialogueBox = null;
    private void InitDoof()
    {
        // Dialogue box will be implemented in Phase 3
        /*
        string file = PsychPaths.Txt(SongName + "/" + SongName + "Dialogue_" + ClientPrefs.Data.Language);
        if (!FileSystem.Exists(file))
        {
            file = PsychPaths.Txt(SongName + "/" + SongName + "Dialogue");
        }
        if (!FileSystem.Exists(file))
        {
            StartCountdown();
            return;
        }
        // doof = new DialogueBox(false, file contents...)
        */
        
        // For now, bypass dialogue since we don't have DialogueBox yet
        // StartCountdown();
    }

    private void SchoolIntro()
    {
        InCutscene = true;
        blackScreen = new Sprite(-100, -100);
        blackScreen.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.Black);
        blackScreen.ScrollFactor = Vector2.Zero;
        
        if (SongName == "senpai") Add(blackScreen);

        // Start timer
        blackAlphaTimer = 0.3f;
    }
    
    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (blackAlphaTimer > 0)
        {
            blackAlphaTimer -= elapsed;
            if (blackAlphaTimer <= 0)
            {
                if (blackScreen != null)
                {
                    blackScreen.Alpha -= 0.15f;
                    if (blackScreen.Alpha <= 0)
                    {
                        // if (doof != null) Add(doof);
                        // else
                        StartCountdown();
                        
                        Remove(blackScreen);
                        blackScreen = null;
                        blackAlphaTimer = -1;
                    }
                    else
                    {
                        blackAlphaTimer = 0.3f; // reset timer for next alpha decrement
                    }
                }
            }
        }
    }
}
