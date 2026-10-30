using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Stages.Objects;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages;

/// <summary>
/// Week 1 stage (Bopeebo / Fresh / Dad Battle).
/// Original: states/stages/StageWeek1.hx
/// </summary>
public class StageWeek1 : BaseStage
{
    private BGSprite _bg;
    private BGSprite _stageFront;
    private BGSprite _stageLightLeft;
    private BGSprite _stageLightRight;
    private BGSprite _stageCurtains;

    private BGSprite _dadbattleBlack;
    private BGSprite _dadbattleLight;
    private DadBattleFog _dadbattleFog;

    // Tweening states for Dadbattle Spotlight
    private float lightAlphaTimer = -1;
    private float fogAlphaTimer = -1;
    private int fogState = 0; // 0 = idle, 1 = fade in, 2 = fade out

    public StageWeek1(PlayScene game) : base(game) { }

    public override void Create()
    {
        _bg = new BGSprite("stageback", -600, -200, 0.9f, 0.9f);
        Add(_bg);

        _stageFront = new BGSprite("stagefront", -650, 600, 0.9f, 0.9f);
        _stageFront.SetGraphicSize((int)((_stageFront.Texture?.Width ?? 1) * 1.1f));
        _stageFront.UpdateHitbox();
        Add(_stageFront);

        if (!ClientPrefs.Data.LowQuality)
        {
            _stageLightLeft = new BGSprite("stage_light", -125, -100, 0.9f, 0.9f);
            _stageLightLeft.SetGraphicSize((int)((_stageLightLeft.Texture?.Width ?? 1) * 1.1f));
            _stageLightLeft.UpdateHitbox();
            Add(_stageLightLeft);

            _stageLightRight = new BGSprite("stage_light", 1225, -100, 0.9f, 0.9f);
            _stageLightRight.SetGraphicSize((int)((_stageLightRight.Texture?.Width ?? 1) * 1.1f));
            _stageLightRight.UpdateHitbox();
            _stageLightRight.FlipX = true;
            Add(_stageLightRight);

            _stageCurtains = new BGSprite("stagecurtains", -500, -300, 1.3f, 1.3f);
            _stageCurtains.SetGraphicSize((int)((_stageCurtains.Texture?.Width ?? 1) * 0.9f));
            _stageCurtains.UpdateHitbox();
            Add(_stageCurtains);
        }
    }

    public override void EventPushed(GameEvent eventNote)
    {
        if (eventNote.Event == "Dadbattle Spotlight")
        {
            _dadbattleBlack = new BGSprite(null, -800, -400, 0, 0);
            _dadbattleBlack.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 2), (int)(FNFGame.SCREEN_HEIGHT * 2), Color.Black);
            _dadbattleBlack.Alpha = 0.25f;
            _dadbattleBlack.Visible = false;
            Add(_dadbattleBlack);

            _dadbattleLight = new BGSprite("spotlight", 400, -400);
            _dadbattleLight.Alpha = 0.375f;
            // _dadbattleLight.blend = ADD;
            _dadbattleLight.Visible = false;
            Add(_dadbattleLight);

            _dadbattleFog = new DadBattleFog { Visible = false };
            Add(_dadbattleFog);
        }
    }

    public override void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime)
    {
        if (eventName != "Dadbattle Spotlight" || _dadbattleBlack == null) return;

        float flVal = flValue1.HasValue ? flValue1.Value : 0;
        int val = (int)Math.Round(flVal);
        var dad = Game.Dad;
        var boyfriend = Game.Boyfriend;

        switch (val)
        {
            case 1:
            case 2:
            case 3: // enable and target dad
                if (val == 1) // enable
                {
                    _dadbattleBlack.Visible = true;
                    _dadbattleLight.Visible = true;
                    _dadbattleFog.Visible = true;
                    Game.DefaultCamZoom += 0.12f;
                }

                var who = dad;
                if (val > 2) who = boyfriend; // 2 only targets dad

                _dadbattleLight.Alpha = 0;
                lightAlphaTimer = 0.12f; // Wait 0.12s before setting alpha to 0.375
                
                if (who != null)
                {
                    var mid = who.GetGraphicMidpoint();
                    _dadbattleLight.Position = new Vector2(
                        mid.X - (_dadbattleLight.Width / 2f),
                        who.Y + who.Height - _dadbattleLight.Height + 50
                    );
                }
                
                fogState = 1;
                fogAlphaTimer = 1.5f;
                break;

            default:
                _dadbattleBlack.Visible = false;
                _dadbattleLight.Visible = false;
                Game.DefaultCamZoom -= 0.12f;
                
                fogState = 2;
                fogAlphaTimer = 0.7f;
                break;
        }
    }

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        

        if (lightAlphaTimer > 0)
        {
            lightAlphaTimer -= elapsed;
            if (lightAlphaTimer <= 0)
            {
                if (_dadbattleLight != null)
                    _dadbattleLight.Alpha = 0.375f;
            }
        }

        if (fogState > 0 && _dadbattleFog != null)
        {
            fogAlphaTimer -= elapsed;

            if (fogAlphaTimer <= 0)
            {
                if (fogState == 1)
                {
                    _dadbattleFog.Alpha = 0.7f;
                }
                else if (fogState == 2)
                {
                    _dadbattleFog.Alpha = 0;
                    _dadbattleFog.Visible = false;
                }
                fogState = 0;
            }
            else
            {
                if (fogState == 1) // Fade in to 0.7 over 1.5s (quadInOut approx)
                {
                    float t = 1.0f - (fogAlphaTimer / 1.5f);
                    float easedT = t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
                    _dadbattleFog.Alpha = easedT * 0.7f;
                }
                else if (fogState == 2) // Fade out to 0 over 0.7s (linear)
                {
                    float t = fogAlphaTimer / 0.7f;
                    _dadbattleFog.Alpha = t * 0.7f;
                }
            }
        }
    }
}
