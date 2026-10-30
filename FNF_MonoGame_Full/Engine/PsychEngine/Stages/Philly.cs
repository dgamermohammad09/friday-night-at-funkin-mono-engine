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

public class Philly : BaseStage
{
    private List<Color> phillyLightsColors;
    private BGSprite phillyWindow;
    private BGSprite phillyStreet;
    private PhillyTrain phillyTrain;
    private int curLight = -1;

    // For Philly Glow events
    private Sprite blammedLightsBlack;
    private PhillyGlowGradient phillyGlowGradient;
    private SpriteGroup phillyGlowParticles;
    private BGSprite phillyWindowEvent;
    private int curLightEvent = -1;

    public Philly(PlayScene game) : base(game) { }

    public override void Create()
    {
        if (!ClientPrefs.Data.LowQuality)
        {
            var bg = new BGSprite("philly/sky", -100, 0, 0.1f, 0.1f);
            Add(bg);
        }

        var city = new BGSprite("philly/city", -10, 0, 0.3f, 0.3f);
        city.SetGraphicSize((int)(city.Width * 0.85f));
        city.UpdateHitbox();
        Add(city);

        phillyLightsColors = new List<Color>()
        {
            new Color(0x31, 0xA2, 0xFD),
            new Color(0x31, 0xFD, 0x8C),
            new Color(0xFB, 0x33, 0xF5),
            new Color(0xFD, 0x45, 0x31),
            new Color(0xFB, 0xA6, 0x33)
        };

        phillyWindow = new BGSprite("philly/window", city.X, city.Y, 0.3f, 0.3f);
        phillyWindow.SetGraphicSize((int)(phillyWindow.Width * 0.85f));
        phillyWindow.UpdateHitbox();
        Add(phillyWindow);
        phillyWindow.Alpha = 0;

        if (!ClientPrefs.Data.LowQuality)
        {
            var streetBehind = new BGSprite("philly/behindTrain", -40, 50);
            Add(streetBehind);
        }

        phillyTrain = new PhillyTrain(2000, 360);
        Add(phillyTrain);

        phillyStreet = new BGSprite("philly/street", -40, 50);
        Add(phillyStreet);
    }

    public override void EventPushed(GameEvent eventNote)
    {
        if (eventNote.Event == "Philly Glow")
        {
            blammedLightsBlack = new Sprite(FNFGame.SCREEN_WIDTH * -0.5f, FNFGame.SCREEN_HEIGHT * -0.5f);
            blammedLightsBlack.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.Black);
            blammedLightsBlack.Visible = false;
            Insert(Members.IndexOf(phillyStreet), blammedLightsBlack);

            phillyWindowEvent = new BGSprite("philly/window", phillyWindow.X, phillyWindow.Y, 0.3f, 0.3f);
            phillyWindowEvent.SetGraphicSize((int)(phillyWindowEvent.Width * 0.85f));
            phillyWindowEvent.UpdateHitbox();
            phillyWindowEvent.Visible = false;
            Insert(Members.IndexOf(blammedLightsBlack) + 1, phillyWindowEvent);

            phillyGlowGradient = new PhillyGlowGradient(-400, 225);
            phillyGlowGradient.Visible = false;
            Insert(Members.IndexOf(blammedLightsBlack) + 1, phillyGlowGradient);
            
            if (!ClientPrefs.Data.Flashing) 
                phillyGlowGradient.intendedAlpha = 0.7f;

            // Precache philly glow particle image
            PsychPaths.Image("philly/particle"); 
            
            phillyGlowParticles = new SpriteGroup();
            phillyGlowParticles.Visible = false;
            Insert(Members.IndexOf(phillyGlowGradient) + 1, phillyGlowParticles);
        }
    }

    public override void Update(float elapsed)
    {
        

        phillyWindow.Alpha -= (float)(PsychConductor.crochet / 1000f) * elapsed * 1.5f;

        if (phillyGlowParticles != null)
        {
            for (int i = phillyGlowParticles.Members.Count - 1; i >= 0; i--)
            {
                var particle = (PhillyGlowParticle)phillyGlowParticles.Members[i];
                if (particle.Alpha <= 0)
                {
                    particle.Kill();
                    phillyGlowParticles.Remove(particle);
                }
            }
        }
    }

    public override void BeatHit()
    {
        phillyTrain.BeatHit(CurBeat);
        if (CurBeat % 4 == 0)
        {
            int oldLight = curLight;
            curLight = PsychGlobals.Random.Next(0, phillyLightsColors.Count);
            if (curLight == oldLight) curLight = (curLight + 1) % phillyLightsColors.Count; // Avoid repeating

            phillyWindow.Color = phillyLightsColors[curLight];
            phillyWindow.Alpha = 1;
        }
    }

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName == "Philly Glow")
        {
            float val = flValue1.HasValue ? flValue1.Value : 0;
            if (val <= 0) val = 0;
            int lightId = (int)Math.Round(val);

            var chars = new List<PsychCharacter>() { Game.Boyfriend, Game.Gf, Game.Dad };
            switch (lightId)
            {
                case 0:
                    if (phillyGlowGradient.Visible)
                    {
                        DoFlash();
                        if (ClientPrefs.Data.CamZooms)
                        {
                            Game.CamGame.Zoom += 0.5f;
                            Game.CamHUD.Zoom += 0.1f;
                        }

                        blammedLightsBlack.Visible = false;
                        phillyWindowEvent.Visible = false;
                        phillyGlowGradient.Visible = false;
                        phillyGlowParticles.Visible = false;
                        curLightEvent = -1;

                        foreach (var who in chars)
                        {
                            if (who != null) who.Color = Color.White;
                        }
                        phillyStreet.Color = Color.White;
                    }
                    break;

                case 1: // turn on
                    int oldLightEvent = curLightEvent;
                    curLightEvent = PsychGlobals.Random.Next(0, phillyLightsColors.Count);
                    if (curLightEvent == oldLightEvent) curLightEvent = (curLightEvent + 1) % phillyLightsColors.Count;
                    
                    Color color = phillyLightsColors[curLightEvent];

                    if (!phillyGlowGradient.Visible)
                    {
                        DoFlash();
                        if (ClientPrefs.Data.CamZooms)
                        {
                            Game.CamGame.Zoom += 0.5f;
                            Game.CamHUD.Zoom += 0.1f;
                        }

                        blammedLightsBlack.Visible = true;
                        blammedLightsBlack.Alpha = 1;
                        phillyWindowEvent.Visible = true;
                        phillyGlowGradient.Visible = true;
                        phillyGlowParticles.Visible = true;
                    }
                    else if (ClientPrefs.Data.Flashing)
                    {
                        Color colorButLower = color * 0.25f;
                        Game.CamGame.Flash(colorButLower, 0.5f);
                    }

                    Color charColor = color;
                    // Haxe saturation approximation: just blend towards gray or lower intensity
                    if (!ClientPrefs.Data.Flashing) 
                        charColor = Color.Lerp(charColor, Color.Gray, 0.5f);
                    else 
                        charColor = Color.Lerp(charColor, Color.Gray, 0.25f);

                    foreach (var who in chars)
                    {
                        if (who != null) who.Color = charColor;
                    }

                    foreach (var item in phillyGlowParticles.Members)
                    {
                        if (item is PhillyGlowParticle particle)
                            particle.Color = color;
                    }
                    
                    phillyGlowGradient.Color = color;
                    phillyWindowEvent.Color = color;

                    Color streetColor = color * 0.5f;
                    streetColor.A = 255;
                    phillyStreet.Color = streetColor;
                    break;

                case 2: // spawn particles
                    if (!ClientPrefs.Data.LowQuality)
                    {
                        int particlesNum = PsychGlobals.Random.Next(8, 13);
                        float width = (2000f / particlesNum);
                        Color c = phillyLightsColors[curLightEvent];
                        
                        for (int j = 0; j < 3; j++)
                        {
                            for (int i = 0; i < particlesNum; i++)
                            {
                                var particle = new PhillyGlowParticle();
                                particle.X = -400 + width * i + PsychGlobals.Random.Next((int)(-width / 5), (int)(width / 5));
                                particle.Y = phillyGlowGradient.originalY + 200 + (PsychGlobals.Random.Next(0, 126) + j * 40);
                                particle.Color = c;
                                particle.Start();
                                phillyGlowParticles.Add(particle);
                            }
                        }
                    }
                    phillyGlowGradient.Bop();
                    break;
            }
        }
    }

    private void DoFlash()
    {
        Color color = Color.White;
        if (!ClientPrefs.Data.Flashing) color *= 0.5f;
        Game.CamGame.Flash(color, 0.15f);
    }
}
