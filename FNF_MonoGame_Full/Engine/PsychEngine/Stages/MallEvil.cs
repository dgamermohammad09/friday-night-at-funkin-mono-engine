using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

public class MallEvil : BaseStage
{
    private Sprite blackScreen;
    
    // Tween variables
    private bool doingBlackScreenTween = false;
    private float blackScreenAlphaTime = 0;
    
    private bool doingZoomTween = false;
    private float zoomTweenTime = 0;
    private float startZoom = 1.5f;

    private float timerDelay = -1;

    public MallEvil(PlayScene game) : base(game) { }

    public override void Create()
    {
        var bg = new BGSprite("christmas/evilBG", -400, -500, 0.2f, 0.2f);
        bg.SetGraphicSize((int)(bg.Width * 0.8f));
        bg.UpdateHitbox();
        Add(bg);

        var evilTree = new BGSprite("christmas/evilTree", 300, -300, 0.2f, 0.2f);
        Add(evilTree);

        var evilSnow = new BGSprite("christmas/evilSnow", -200, 700);
        Add(evilSnow);
        
        SetDefaultGF("gf-christmas");
        
        // Winter Horrorland cutscene
        if (IsStoryMode && !SeenCutscene)
        {
            if (SongName == "winter-horrorland")
            {
                SetStartCallback(WinterHorrorlandCutscene);
            }
        }
    }

    private void WinterHorrorlandCutscene()
    {
        Game.CamHUD.Visible = false;
        InCutscene = true;

        Audio.PlaySound("Lights_Turn_On");
        Game.CamGame.Zoom = 1.5f;
        // Game.CamGame.FocusOn(new Vector2(400, -2050)); // Needs implementation in CamGame

        // blackout at the start
        blackScreen = new Sprite();
        blackScreen.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.Black);
        blackScreen.ScrollFactor = Vector2.Zero;
        Add(blackScreen);

        // FlxTween.tween(blackScreen, {alpha: 0}, 0.7, ...)
        doingBlackScreenTween = true;
        blackScreenAlphaTime = 0.7f; // 0.7 seconds

        // new FlxTimer().start(0.8, ...)
        timerDelay = 0.8f;
    }

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (doingBlackScreenTween)
        {
            blackScreenAlphaTime -= elapsed;
            if (blackScreenAlphaTime > 0)
            {
                blackScreen.Alpha = blackScreenAlphaTime / 0.7f;
            }
            else
            {
                blackScreen.Alpha = 0;
                doingBlackScreenTween = false;
                Remove(blackScreen);
            }
        }

        if (timerDelay > 0)
        {
            timerDelay -= elapsed;
            if (timerDelay <= 0)
            {
                Game.CamHUD.Visible = true;
                timerDelay = -1;
                
                // FlxTween.tween(FlxG.camera, {zoom: defaultCamZoom}, 2.5, ...)
                doingZoomTween = true;
                zoomTweenTime = 2.5f; // 2.5 seconds
                startZoom = Game.CamGame.Zoom;
            }
        }

        if (doingZoomTween)
        {
            zoomTweenTime -= elapsed;
            if (zoomTweenTime > 0)
            {
                float t = 1.0f - (zoomTweenTime / 2.5f);
                // quadInOut approx
                float easedT = t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t; 
                Game.CamGame.Zoom = PsychCoolUtil.Lerp(startZoom, Game.DefaultCamZoom, easedT);
            }
            else
            {
                Game.CamGame.Zoom = Game.DefaultCamZoom;
                doingZoomTween = false;
                StartCountdown();
            }
        }
    }
}
