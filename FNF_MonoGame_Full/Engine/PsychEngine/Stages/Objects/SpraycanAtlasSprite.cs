using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public enum SpraycanState
{
    WAITING,
    ARCING,     // In the air.
    SHOT,       // Hit by the player.
    IMPACTED    // Impacted the player.
}

public class SpraycanAtlasSprite : SpriteGroup
{
    public SpraycanState currentState = SpraycanState.WAITING;

    public FlxAnimateSprite canAtlas;
    public Sprite explosion;
    public bool cutscene = false;
    private string playingAnim;

    public SpraycanAtlasSprite(float x = 0, float y = 0) : base()
    {
        canAtlas = new FlxAnimateSprite(x, y);
        PsychPaths.LoadAnimateAtlas(canAtlas, "spraycanAtlas");
        canAtlas.Anim.AddBySymbolIndices("Can Start", "Can with Labels", new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 }, 24, false);
        canAtlas.Anim.AddBySymbolIndices("Hit Pico", "Can with Labels", new int[] { 19, 20, 21, 22, 23, 24, 25 }, 24, false);
        canAtlas.Anim.AddBySymbolIndices("Can Shot", "Can with Labels", new int[] { 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42 }, 24, false);
        canAtlas.Anim.OnComplete += FinishCanAnimation;
        canAtlas.Visible = canAtlas.Active = false;
        canAtlas.Antialiasing = ClientPrefs.Data.Antialiasing;
        Add(canAtlas);

        explosion = new Sprite(x - 25, y - 450);
        explosion.Frames = PsychPaths.GetSparrowAtlas("spraypaintExplosionEZ");
        explosion.Animation.AddByPrefix("idle", "explosion round 1 short0", 24, false);
        explosion.Animation.FinishCallback = (name) => { explosion.Visible = explosion.Active = false; };
        explosion.Visible = explosion.Active = false;
        explosion.Antialiasing = ClientPrefs.Data.Antialiasing;
        Add(explosion);
    }

    public void FinishCanAnimation(string name)
    {
        switch (playingAnim)
        {
            case "Can Start":
                PlayHitPico();
                break;
            case "Can Shot":
                canAtlas.Visible = canAtlas.Active = false;
                currentState = SpraycanState.WAITING;
                break;
            case "Hit Pico":
                if (!cutscene) PlayHitExplosion();
                canAtlas.Visible = canAtlas.Active = false;
                currentState = SpraycanState.WAITING;
                break;
        }
    }

    public void PlayHitExplosion()
    {
        explosion.Visible = explosion.Active = true;
        explosion.Animation.Play("idle", true);
    }

    public void PlayCanStart()
    {
        PlayAnimation("Can Start");
        canAtlas.Visible = canAtlas.Active = true;
        currentState = SpraycanState.ARCING;
    }

    public void PlayCanShot()
    {
        PlayAnimation("Can Shot");
        currentState = SpraycanState.SHOT;
    }

    public void PlayHitPico()
    {
        PlayAnimation("Hit Pico");
        currentState = SpraycanState.IMPACTED;
    }

    public void PlayAnimation(string name)
    {
        canAtlas.Anim.Play(name, true);
        playingAnim = name;
    }
}
