using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class PhillyTrain : BGSprite
{
    public Sound sound;

    public bool moving = false;
    public bool finishing = false;
    public bool startedMoving = false;
    public float frameTiming = 0;

    public int cars = 8;
    public int cooldown = 0;

    public PhillyTrain(float x = 0, float y = 0, string image = "philly/train", string soundName = "train_passes") 
        : base(image, x, y)
    {
        Active = true;
        Antialiasing = ClientPrefs.Data.Antialiasing;

        sound = Audio.PlaySound(soundName, 1f, false, true); // Play paused/loaded
    }

    public override void Update(float elapsed)
    {
        

        if (moving)
        {
            frameTiming += elapsed;
            if (frameTiming >= 1f / 24f)
            {
                if (sound != null && sound.Position >= 4700)
                {
                    startedMoving = true;
                    if (PlayScene.Current.Gf != null)
                    {
                        PlayScene.Current.Gf.PlayAnim("hairBlow");
                        PlayScene.Current.Gf.SpecialAnim = true;
                    }
                }
        
                if (startedMoving)
                {
                    X -= 400;
                    if (X < -2000 && !finishing)
                    {
                        X = -1150;
                        cars -= 1;

                        if (cars <= 0)
                            finishing = true;
                    }

                    if (X < -4000 && finishing)
                        Restart();
                }
                frameTiming = 0;
            }
        }
        base.Update(elapsed);
    }

    public void BeatHit(int curBeat)
    {
        if (!moving)
            cooldown += 1;

        if (curBeat % 8 == 4 && PsychGlobals.Random.Next(0, 100) < 30 && !moving && cooldown > 8)
        {
            cooldown = PsychGlobals.Random.Next(-4, 0);
            Start();
        }
    }
    
    public void Start()
    {
        moving = true;
        if (sound != null)
        {
            sound.Stop();
            sound.Play();
        }
    }

    public void Restart()
    {
        if (PlayScene.Current.Gf != null)
        {
            PlayScene.Current.Gf.Danced = false;
            PlayScene.Current.Gf.PlayAnim("hairFall");
            PlayScene.Current.Gf.SpecialAnim = true;
        }
        X = FNFGame.SCREEN_WIDTH + 200;
        moving = false;
        cars = 8;
        finishing = false;
        startedMoving = false;
    }
}
