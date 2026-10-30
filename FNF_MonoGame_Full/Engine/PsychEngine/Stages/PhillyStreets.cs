using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public enum NeneState
{
    STATE_DEFAULT,
    STATE_PRE_RAISE,
    STATE_RAISE,
    STATE_READY,
    STATE_LOWER
}

public class PhillyStreets : BaseStage
{
    private const int MIN_BLINK_DELAY = 3;
    private const int MAX_BLINK_DELAY = 7;
    private const float VULTURE_THRESHOLD = 0.5f;
    private int blinkCountdown = 3;

    // private RainShader rainShader;
    private float rainShaderStartIntensity = 0;
    private float rainShaderEndIntensity = 0;

    private BGSprite scrollingSky;
    private BGSprite phillyTraffic;
    private BGSprite phillyCars;
    private BGSprite phillyCars2;

    private Sprite picoFade;
    private SpraycanAtlasSprite spraycan;
    private BGSprite spraycanPile;

    private List<Sprite> darkenable = new List<Sprite>();
    private ABotSpeaker abot;

    private List<string> noteTypes = new List<string>();

    public PhillyStreets(PlayScene game) : base(game) { }

    public override void Create()
    {
        if (!ClientPrefs.Data.LowQuality)
        {
            scrollingSky = new BGSprite("phillyStreets/phillySkybox", -650, -375);
            scrollingSky.Antialiasing = ClientPrefs.Data.Antialiasing;
            scrollingSky.ScrollFactor = new Vector2(0.1f, 0.1f);
            scrollingSky.Scale = new Vector2(0.65f, 0.65f);
            scrollingSky.UpdateHitbox();
            Add(scrollingSky);
            darkenable.Add(scrollingSky);

            var phillySkyline = new BGSprite("phillyStreets/phillySkyline", -545, -273, 0.2f, 0.2f);
            Add(phillySkyline);
            darkenable.Add(phillySkyline);

            var phillyForegroundCity = new BGSprite("phillyStreets/phillyForegroundCity", 625, 94, 0.3f, 0.3f);
            Add(phillyForegroundCity);
            darkenable.Add(phillyForegroundCity);
        }

        var phillyConstruction = new BGSprite("phillyStreets/phillyConstruction", 1800, 364, 0.7f, 1f);
        Add(phillyConstruction);
        darkenable.Add(phillyConstruction);

        var phillyHighwayLights = new BGSprite("phillyStreets/phillyHighwayLights", 284, 305, 1f, 1f);
        Add(phillyHighwayLights);
        darkenable.Add(phillyHighwayLights);

        if (!ClientPrefs.Data.LowQuality)
        {
            var phillyHighwayLightsLightmap = new BGSprite("phillyStreets/phillyHighwayLights_lightmap", 284, 305, 1f, 1f);
            phillyHighwayLightsLightmap.Alpha = 0.6f;
            Add(phillyHighwayLightsLightmap);
            darkenable.Add(phillyHighwayLightsLightmap);
        }

        var phillyHighway = new BGSprite("phillyStreets/phillyHighway", 139, 209, 1f, 1f);
        Add(phillyHighway);
        darkenable.Add(phillyHighway);

        if (!ClientPrefs.Data.LowQuality)
        {
            var phillySmog = new BGSprite("phillyStreets/phillySmog", -6, 245, 0.8f, 1f);
            Add(phillySmog);
            darkenable.Add(phillySmog);

            for (int i = 0; i < 2; i++)
            {
                var car = new BGSprite("phillyStreets/phillyCars", 1200, 818, 0.9f, 1f, new string[] { "car1", "car2", "car3", "car4" }, false);
                Add(car);
                if (i == 0) phillyCars = car;
                else phillyCars2 = car;
                darkenable.Add(car);
            }
            phillyCars2.FlipX = true;

            phillyTraffic = new BGSprite("phillyStreets/phillyTraffic", 1840, 608, 0.9f, 1f, new string[] { "redtogreen", "greentored" }, false);
            Add(phillyTraffic);
            darkenable.Add(phillyTraffic);

            var phillyTrafficLightmap = new BGSprite("phillyStreets/phillyTraffic_lightmap", 1840, 608, 0.9f, 1f);
            phillyTrafficLightmap.Alpha = 0.6f;
            Add(phillyTrafficLightmap);
            darkenable.Add(phillyTrafficLightmap);
        }

        var phillyForeground = new BGSprite("phillyStreets/phillyForeground", 88, 317, 1f, 1f);
        Add(phillyForeground);
        darkenable.Add(phillyForeground);

        if (!ClientPrefs.Data.LowQuality)
        {
            picoFade = new Sprite();
            picoFade.Antialiasing = ClientPrefs.Data.Antialiasing;
            picoFade.Alpha = 0;
            Add(picoFade);
            darkenable.Add(picoFade);
        }

        abot = new ABotSpeaker(Game.GfGroup.X, Game.GfGroup.Y + 550);
        UpdateABotEye(true);
        Add(abot);

        SetDefaultGF("nene");
    }

    public override void CreatePost()
    {
        foreach (var note in Game.UnspawnNotes)
        {
            if (note == null) continue;
            if (note.NoteType == "weekend-1-firegun")
                note.BlockHit = true;
                
            if (!noteTypes.Contains(note.NoteType))
                noteTypes.Add(note.NoteType);
        }

        spraycanPile = new BGSprite("SpraycanPile", 920, 1045, 1f, 1f);
        Precache();
        Add(spraycanPile);
        darkenable.Add(spraycanPile);

        if (Game.Gf != null)
        {
            // Game.Gf.Animation.Callback = ... // Port when FlxAnimate/Sprite callback is fully supported
        }
    }

    private void UpdateABotEye(bool finishInstantly = false)
    {
        int sec = (int)MathHelper.Clamp(Game.CurSection, 0, Game.Notes.Length - 1);
        if (Game.Notes.Length > sec && Game.Notes[sec].MustHitSection)
            abot.LookRight();
        else
            abot.LookLeft();
            
        if (finishInstantly)
        {
            if (abot.eyes.Anim.Length > 0)
                abot.eyes.Anim.CurFrame = abot.eyes.Anim.Length - 1;
        }
    }

    public override void StartSong()
    {
        abot.BeatHit();
    }

    private void Precache()
    {
        bool didCreateCan = false;
        Action createCan = () =>
        {
            if (didCreateCan) return;
            spraycan = new SpraycanAtlasSprite(spraycanPile.X + 530, spraycanPile.Y - 240);
            Add(spraycan);
            didCreateCan = true;
        };

        bool didCreateCasing = false;
        Action precacheCasing = () =>
        {
            if (didCreateCasing) return;
            if (!ClientPrefs.Data.LowQuality)
            {
                // casingFrames = Paths.getSparrowAtlas('PicoBullet');
                // casingGroup = new FlxSpriteGroup();
            }
            didCreateCasing = true;
        };

        foreach (var noteType in noteTypes)
        {
            switch (noteType)
            {
                case "weekend-1-kickcan": createCan(); break;
                case "weekend-1-cockgun": precacheCasing(); break;
                case "weekend-1-firegun": break; // precache bonk sound
            }
        }
    }
}
