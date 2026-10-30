using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class DarnellBlazinHandler
{
    private bool cantUppercut = false;
    private bool alternate = false;

    public void NoteHit(Note note)
    {
        if (WasNoteHitPoorly(note.Rating) && IsPlayerLowHealth() && PsychGlobals.Random.Next(0, 100) < 30)
        {
            PlayUppercutPrepAnim();
            return;
        }

        if (cantUppercut)
        {
            PlayPunchHighAnim();
            return;
        }

        switch (note.NoteType)
        {
            case "weekend-1-punchlow": PlayHitLowAnim(); break;
            case "weekend-1-punchlowblocked": PlayBlockAnim(); break;
            case "weekend-1-punchlowdodged": PlayDodgeAnim(); break;
            case "weekend-1-punchlowspin": PlaySpinAnim(); break;

            case "weekend-1-punchhigh": PlayHitHighAnim(); break;
            case "weekend-1-punchhighblocked": PlayBlockAnim(); break;
            case "weekend-1-punchhighdodged": PlayDodgeAnim(); break;
            case "weekend-1-punchhighspin": PlaySpinAnim(); break;

            case "weekend-1-blockhigh": PlayPunchHighAnim(); break;
            case "weekend-1-blocklow": PlayPunchLowAnim(); break;
            case "weekend-1-blockspin": PlayPunchHighAnim(); break;

            case "weekend-1-dodgehigh": PlayPunchHighAnim(); break;
            case "weekend-1-dodgelow": PlayPunchLowAnim(); break;
            case "weekend-1-dodgespin": PlayPunchHighAnim(); break;

            case "weekend-1-hithigh": PlayPunchHighAnim(); break;
            case "weekend-1-hitlow": PlayPunchLowAnim(); break;
            case "weekend-1-hitspin": PlayPunchHighAnim(); break;

            case "weekend-1-picouppercutprep": break;
            case "weekend-1-picouppercut": PlayUppercutHitAnim(); break;

            case "weekend-1-darnelluppercutprep": PlayUppercutPrepAnim(); break;
            case "weekend-1-darnelluppercut": PlayUppercutAnim(); break;

            case "weekend-1-idle": PlayIdleAnim(); break;
            case "weekend-1-fakeout": PlayCringeAnim(); break;
            case "weekend-1-taunt": PlayPissedConditionalAnim(); break;
            case "weekend-1-tauntforce": PlayPissedAnim(); break;
            case "weekend-1-reversefakeout": PlayFakeoutAnim(); break;
        }

        cantUppercut = false;
    }

    public void NoteMiss(Note note)
    {
        if (Dad.GetAnimationName() == "uppercutPrep")
        {
            PlayUppercutAnim();
            return;
        }

        if (WillMissBeLethal())
        {
            PlayPunchLowAnim();
            return;
        }

        if (cantUppercut)
        {
            PlayPunchHighAnim();
            return;
        }

        switch (note.NoteType)
        {
            case "weekend-1-punchlow": PlayPunchLowAnim(); break;
            case "weekend-1-punchlowblocked": PlayPunchLowAnim(); break;
            case "weekend-1-punchlowdodged": PlayPunchLowAnim(); break;
            case "weekend-1-punchlowspin": PlayPunchLowAnim(); break;

            case "weekend-1-punchhigh": PlayPunchHighAnim(); break;
            case "weekend-1-punchhighblocked": PlayPunchHighAnim(); break;
            case "weekend-1-punchhighdodged": PlayPunchHighAnim(); break;
            case "weekend-1-punchhighspin": PlayPunchHighAnim(); break;

            case "weekend-1-blockhigh": PlayPunchHighAnim(); break;
            case "weekend-1-blocklow": PlayPunchLowAnim(); break;
            case "weekend-1-blockspin": PlayPunchHighAnim(); break;

            case "weekend-1-dodgehigh": PlayPunchHighAnim(); break;
            case "weekend-1-dodgelow": PlayPunchLowAnim(); break;
            case "weekend-1-dodgespin": PlayPunchHighAnim(); break;

            case "weekend-1-hithigh": PlayPunchHighAnim(); break;
            case "weekend-1-hitlow": PlayPunchLowAnim(); break;
            case "weekend-1-hitspin": PlayPunchHighAnim(); break;

            case "weekend-1-picouppercutprep": PlayHitHighAnim(); cantUppercut = true; break;
            case "weekend-1-picouppercut": PlayDodgeAnim(); break;

            case "weekend-1-darnelluppercutprep": PlayUppercutPrepAnim(); break;
            case "weekend-1-darnelluppercut": PlayUppercutAnim(); break;

            case "weekend-1-idle": PlayIdleAnim(); break;
            case "weekend-1-fakeout": PlayCringeAnim(); break;
            case "weekend-1-taunt": PlayPissedConditionalAnim(); break;
            case "weekend-1-tauntforce": PlayPissedAnim(); break;
            case "weekend-1-reversefakeout": PlayFakeoutAnim(); break;
        }
        cantUppercut = false;
    }

    public void NoteMissPress(int direction)
    {
        if (WillMissBeLethal())
        {
            PlayPunchLowAnim();
        }
        else
        {
            bool shouldDodge = PsychGlobals.Random.Next(0, 100) < 50;
            if (shouldDodge) PlayDodgeAnim();
            else PlayBlockAnim();
        }
    }

    private string DoAlternate()
    {
        alternate = !alternate;
        return alternate ? "1" : "2";
    }

    private void PlayBlockAnim()
    {
        Dad.PlayAnim("block", true);
        PlayScene.Current.CamGame.Shake(0.002f, 0.1f);
        MoveToBack();
    }

    private void PlayCringeAnim()
    {
        Dad.PlayAnim("cringe", true);
        MoveToBack();
    }

    private void PlayDodgeAnim()
    {
        Dad.PlayAnim("dodge", true, false);
        MoveToBack();
    }

    private void PlayIdleAnim()
    {
        Dad.PlayAnim("idle", false);
        MoveToBack();
    }

    private void PlayFakeoutAnim()
    {
        Dad.PlayAnim("fakeout", true);
        MoveToBack();
    }

    private void PlayPissedConditionalAnim()
    {
        if (Dad.GetAnimationName() == "cringe") PlayPissedAnim();
        else PlayIdleAnim();
    }

    private void PlayPissedAnim()
    {
        Dad.PlayAnim("pissed", true);
        MoveToBack();
    }

    private void PlayUppercutPrepAnim()
    {
        Dad.PlayAnim("uppercutPrep", true);
        MoveToFront();
    }

    private void PlayUppercutAnim()
    {
        Dad.PlayAnim("uppercut", true);
        MoveToFront();
    }

    private void PlayUppercutHitAnim()
    {
        Dad.PlayAnim("uppercutHit", true);
        MoveToBack();
    }

    private void PlayHitHighAnim()
    {
        Dad.PlayAnim("hitHigh", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private void PlayHitLowAnim()
    {
        Dad.PlayAnim("hitLow", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private void PlayPunchHighAnim()
    {
        Dad.PlayAnim("punchHigh" + DoAlternate(), true);
        MoveToFront();
    }

    private void PlayPunchLowAnim()
    {
        Dad.PlayAnim("punchLow" + DoAlternate(), true);
        MoveToFront();
    }

    private void PlaySpinAnim()
    {
        Dad.PlayAnim("hitSpin", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private bool WillMissBeLethal()
    {
        return PlayScene.Current.Health <= 0.0f && !PlayScene.Current.PracticeMode;
    }

    private bool WasNoteHitPoorly(string rating)
    {
        return rating == "bad" || rating == "shit";
    }

    private bool IsPlayerLowHealth()
    {
        return PlayScene.Current.Health <= 0.3f * 2f;
    }

    private void MoveToBack()
    {
        var members = PlayScene.Current.Stage.Members;
        int dadPos = members.IndexOf(DadGroup);
        int bfPos = members.IndexOf(BoyfriendGroup);
        if (dadPos < bfPos) return;

        members[bfPos] = DadGroup;
        members[dadPos] = BoyfriendGroup;
    }

    private void MoveToFront()
    {
        var members = PlayScene.Current.Stage.Members;
        int dadPos = members.IndexOf(DadGroup);
        int bfPos = members.IndexOf(BoyfriendGroup);
        if (dadPos > bfPos) return;

        members[bfPos] = DadGroup;
        members[dadPos] = BoyfriendGroup;
    }

    private Character Boyfriend => PlayScene.Current.Boyfriend;
    private Character Dad => PlayScene.Current.Dad;
    private SpriteGroup BoyfriendGroup => PlayScene.Current.BoyfriendGroup;
    private SpriteGroup DadGroup => PlayScene.Current.DadGroup;
}
