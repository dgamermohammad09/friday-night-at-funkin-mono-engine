using FontStashSharp;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Cutscenes;

public class DialogueBox : SpriteGroup
{
    private Sprite box;
    private string curCharacter = "";
    private List<string> dialogueList = new List<string>();

    private TextObject swagDialogue; // TypeText not yet implemented in mono, use normal text

    public Action finishThing;
    public Action nextDialogueThing = null;
    public Action skipDialogueThing = null;

    private Sprite portraitLeft;
    private Sprite portraitRight;
    private Sprite handSelect;
    private Sprite bgFade;
    private TextObject skipText;

    public static string CurrentSong = ""; // set by PlayScene before creating the box (Psych: PlayState.SONG.song)
    private string songName = PsychPaths.FormatToSongPath(CurrentSong ?? "");
    private float bgFadeTimer = -1;

    public DialogueBox(bool talkingRight = true, List<string> dialogueList = null)
    {
        bgFade = new Sprite(-200, -200);
        bgFade.MakeGraphic((int)(FNFGame.SCREEN_WIDTH * 1.3f), (int)(FNFGame.SCREEN_HEIGHT * 1.3f), new Color(0xB3, 0xDF, 0xd8));
        bgFade.ScrollFactor = Vector2.Zero;
        bgFade.Alpha = 0;
        Add(bgFade);

        bgFadeTimer = 5 * 0.83f; // 5 ticks of 0.83s

        // box = new FlxAnimateSprite(-20, 45); // Using standard Sprite since FlxAnimate isn't fully ready
        box = new Sprite(-20, 45);
        bool hasDialog = true;
        
        switch (songName)
        {
            case "senpai":
                box.Frames = PsychPaths.GetSparrowAtlas("weeb/pixelUI/dialogueBox-pixel");
                box.Animation.AddByPrefix("normalOpen", "Text Box Appear", 24, false);
                box.Animation.AddByIndices("normal", "Text Box Appear instance 1", new int[] { 4 }, "", 24, false);
                break;
            case "roses":
                box.Frames = PsychPaths.GetSparrowAtlas("weeb/pixelUI/dialogueBox-senpaiMad");
                box.Animation.AddByPrefix("normalOpen", "SENPAI ANGRY IMPACT SPEECH", 24, false);
                box.Animation.AddByIndices("normal", "SENPAI ANGRY IMPACT SPEECH instance 1", new int[] { 4 }, "", 24, false);
                break;
            case "thorns":
                box.Frames = PsychPaths.GetSparrowAtlas("weeb/pixelUI/dialogueBox-evil");
                box.Animation.AddByPrefix("normalOpen", "Spirit Textbox spawn", 24, false);
                box.Animation.AddByIndices("normal", "Spirit Textbox spawn instance 1", new int[] { 11 }, "", 24, false);

                var face = new Sprite(320, 170);
                face.LoadGraphic(PsychPaths.Image("weeb/spiritFaceForward"));
                face.SetGraphicSize((int)(face.Width * 6));
                Add(face);
                break;
            default:
                hasDialog = false;
                break;
        }

        this.dialogueList = dialogueList;
        
        if (!hasDialog) return;

        portraitLeft = new Sprite(-20, 40);
        portraitLeft.Frames = PsychPaths.GetSparrowAtlas("weeb/senpaiPortrait");
        portraitLeft.Animation.AddByPrefix("enter", "Senpai Portrait Enter", 24, false);
        portraitLeft.SetGraphicSize((int)(portraitLeft.Width * PlayScene.daPixelZoom * 0.9f));
        portraitLeft.UpdateHitbox();
        portraitLeft.ScrollFactor = Vector2.Zero;
        Add(portraitLeft);
        portraitLeft.Visible = false;

        portraitRight = new Sprite(0, 40);
        portraitRight.Frames = PsychPaths.GetSparrowAtlas("weeb/bfPortrait");
        portraitRight.Animation.AddByPrefix("enter", "Boyfriend portrait enter", 24, false);
        portraitRight.SetGraphicSize((int)(portraitRight.Width * PlayScene.daPixelZoom * 0.9f));
        portraitRight.UpdateHitbox();
        portraitRight.ScrollFactor = Vector2.Zero;
        Add(portraitRight);
        portraitRight.Visible = false;
        
        box.Animation.Play("normalOpen");
        box.SetGraphicSize((int)(box.Width * PlayScene.daPixelZoom * 0.9f));
        box.UpdateHitbox();
        Add(box);

        box.ScreenCenter(Axes.X);
        portraitLeft.ScreenCenter(Axes.X);

        handSelect = new Sprite(1042, 590);
        handSelect.LoadGraphic(PsychPaths.Image("weeb/pixelUI/hand_textbox"));
        handSelect.SetGraphicSize((int)(handSelect.Width * PlayScene.daPixelZoom * 0.9f));
        handSelect.UpdateHitbox();
        handSelect.Visible = false;
        Add(handSelect);

        swagDialogue = new TextObject(240, 500, "");
        swagDialogue.Format = PsychPaths.Font("pixel-latin.ttf"); // Not exactly true to Flixel API but a close analogue
        swagDialogue.Color = new Color(0x3F, 0x20, 0x21);
        Add(swagDialogue);

        skipText = new TextObject(FNFGame.SCREEN_WIDTH - 320, FNFGame.SCREEN_HEIGHT - 30, "Press BACK to Skip");
        skipText.Color = Color.White;
        Add(skipText);
    }

    private bool dialogueOpened = false;
    private bool dialogueStarted = false;
    private bool dialogueEnded = false;
    private bool isEnding = false;
    private float endingTimer = -1;
    private float finishTimer = -1;

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        if (bgFadeTimer > 0)
        {
            bgFadeTimer -= elapsed;
            if (bgFade.Alpha < 0.7f) bgFade.Alpha += (elapsed / 0.83f) * (1f / 5f) * 0.7f;
            if (bgFade.Alpha > 0.7f) bgFade.Alpha = 0.7f;
        }

        if (endingTimer > 0)
        {
            endingTimer -= elapsed;
            box.Alpha -= elapsed / 0.83f * (1f / 5f);
            bgFade.Alpha -= elapsed / 0.83f * (1f / 5f) * 0.7f;
            portraitLeft.Visible = false;
            portraitRight.Visible = false;
            swagDialogue.Alpha -= elapsed / 0.83f * (1f / 5f);
            handSelect.Alpha -= elapsed / 0.83f * (1f / 5f);
        }

        if (finishTimer > 0)
        {
            finishTimer -= elapsed;
            if (finishTimer <= 0)
            {
                if (finishThing != null) finishThing();
                Kill();
                return;
            }
        }

        switch(songName)
        {
            case "roses":
                portraitLeft.Visible = false;
                break;
            case "thorns":
                portraitLeft.Visible = false;
                swagDialogue.Color = Color.White;
                break;
        }

        if (box.Animation.CurAnim != null && box.Animation.CurAnim.Name == "normalOpen" && box.Animation.CurAnim.Finished)
        {
            box.Animation.Play("normal");
            dialogueOpened = true;
        }

        if (dialogueOpened && !dialogueStarted)
        {
            StartDialogue();
            dialogueStarted = true;
        }

        if (Controls.Instance.Pressed("back"))
        {
            if (dialogueStarted && !isEnding)
            {
                DialogueCompleted();
                Audio.PlaySound("clickText", 0.8f);
            }
        }
        else if (Controls.Instance.Pressed("accept"))
        {
            if (dialogueEnded)
            {
                if (dialogueList.Count <= 1)
                {
                    if (!isEnding) DialogueCompleted();
                }
                else
                {
                    dialogueList.RemoveAt(0);
                    StartDialogue();
                    Audio.PlaySound("clickText", 0.8f);
                }
            }
            else if (dialogueStarted)
            {
                Audio.PlaySound("clickText", 0.8f);
                // swagDialogue.skip();
                if (skipDialogueThing != null) skipDialogueThing();
            }
        }
    }

    private void DialogueCompleted()
    {
        isEnding = true;
        Audio.PlaySound("clickText", 0.8f);

        if (songName == "senpai" || songName == "thorns")
            Audio.Music.FadeOutMusic(1.5f, () => Audio.Music.StopMusic());

        endingTimer = 5 * 0.2f;
        // swagDialogue.skip();
        skipText.Visible = false;
        finishTimer = 1.5f;
    }

    private void StartDialogue()
    {
        CleanDialog();
        
        swagDialogue.Text = dialogueList[0];
        // In original this used FlxTypeText, we just show it instantly for now
        handSelect.Visible = true;
        dialogueEnded = true;

        switch (curCharacter)
        {
            case "dad":
                portraitRight.Visible = false;
                if (!portraitLeft.Visible)
                {
                    if (songName == "senpai") portraitLeft.Visible = true;
                    portraitLeft.Animation.Play("enter");
                }
                break;
            case "bf":
                portraitLeft.Visible = false;
                if (!portraitRight.Visible)
                {
                    portraitRight.Visible = true;
                    portraitRight.Animation.Play("enter");
                }
                break;
        }
        
        if (nextDialogueThing != null) nextDialogueThing();
    }

    private void CleanDialog()
    {
        string[] splitName = dialogueList[0].Split(':');
        curCharacter = splitName[1];
        dialogueList[0] = dialogueList[0].Substring(splitName[1].Length + 2).Trim();
    }
}
