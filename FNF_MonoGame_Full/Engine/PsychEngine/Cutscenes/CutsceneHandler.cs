using FontStashSharp;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Cutscenes;

public class CutsceneEvent
{
    public float Time;
    public Action Func;
}

public class CutsceneHandler : Basic
{
    public List<CutsceneEvent> timedEvents = new List<CutsceneEvent>();
    public Action skipCallback = null;
    public Action onStart = null;
    public float endTime = 0;
    public List<Sprite> objects = new List<Sprite>();
    public string music = null;

    private readonly float _timeToSkip = 1;
    private bool _canSkip = false;
    public float holdingTime = 0;
    
    // public FlxPieDial skipSprite; // Need a substitute for FlxPieDial if we want a visual indicator
    public Action finishCallback = null;

    public CutsceneHandler(bool canSkip = true)
    {
        Timer(0, () =>
        {
            if (music != null)
            {
                Audio.PlayMusic(music, 0, false);
                Audio.Music.FadeIn();
            }
            if (onStart != null) onStart();
        });
        
        // Psych: FlxG.state.add(this) - the owner scene updates/draws the handler
        
        this._canSkip = canSkip;
        if (canSkip)
        {
            // skipSprite initialization...
            // Omitted for now, could use a standard Sprite or custom drawing
        }
    }

    private float cutsceneTime = 0;
    private bool firstFrame = false;

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        if (!firstFrame)
        {
            firstFrame = true;
            return;
        }

        cutsceneTime += elapsed;
        
        while (timedEvents.Count > 0 && timedEvents[0].Time <= cutsceneTime)
        {
            timedEvents[0].Func();
            timedEvents.RemoveAt(0);
        }

        if (_canSkip && cutsceneTime > 0.1f)
        {
            if (Controls.Instance.Pressed("accept"))
                holdingTime = Math.Max(0, Math.Min(_timeToSkip, holdingTime + elapsed));
            else if (holdingTime > 0)
                holdingTime = Math.Max(0, PsychCoolUtil.Lerp(holdingTime, -0.1f, MathHelper.Clamp(elapsed * 3, 0, 1)));

            UpdateSkipAlpha();
        }

        if (endTime <= cutsceneTime || holdingTime >= _timeToSkip)
        {
            if (holdingTime >= _timeToSkip)
            {
                Console.WriteLine("skipped cutscene");
                if (skipCallback != null)
                    skipCallback();
            }
            else
            {
                if (finishCallback != null)
                    finishCallback();
            }

            foreach (var spr in objects)
            {
                spr.Kill();
                PlayScene.Current?.Remove(spr); // Or PlayScene.Instance.Remove(spr)
                // spr.Destroy();
            }

            // skipSprite destroy
            PlayScene.Current?.Remove(this);
        }
    }

    private void UpdateSkipAlpha()
    {
        // Update visual skip indicator here
    }

    public void Push(Sprite spr)
    {
        objects.Add(spr);
    }

    public void Timer(float time, Action func)
    {
        timedEvents.Add(new CutsceneEvent { Time = time, Func = func });
        timedEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
}
