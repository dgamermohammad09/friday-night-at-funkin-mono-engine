using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay;
using System;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine.Stages.Objects;

public class PicoBlazinHandler
{
    private bool cantUppercut = false;
    private bool alternate = false;

    public void NoteHit(Note note)
    {
        if (WasNoteHitPoorly(note.Rating) && IsPlayerLowHealth() && IsDarnellPreppingUppercut())
        {
            PlayPunchHighAnim();
            return;
        }

        if (cantUppercut)
        {
            PlayBlockAnim();
            cantUppercut = false;
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

            case "weekend-1-blockhigh": PlayBlockAnim(); break;
            case "weekend-1-blocklow": PlayBlockAnim(); break;
            case "weekend-1-blockspin": PlayBlockAnim(); break;

            case "weekend-1-dodgehigh": PlayDodgeAnim(); break;
            case "weekend-1-dodgelow": PlayDodgeAnim(); break;
            case "weekend-1-dodgespin": PlayDodgeAnim(); break;

            case "weekend-1-hithigh": PlayHitHighAnim(); break;
            case "weekend-1-hitlow": PlayHitLowAnim(); break;
            case "weekend-1-hitspin": PlayHitSpinAnim(); break;

            case "weekend-1-picouppercutprep": PlayUppercutPrepAnim(); break;
            case "weekend-1-picouppercut": PlayUppercutAnim(true); break;

            case "weekend-1-darnelluppercutprep": PlayIdleAnim(); break;
            case "weekend-1-darnelluppercut": PlayUppercutHitAnim(); break;

            case "weekend-1-idle": PlayIdleAnim(); break;
            case "weekend-1-fakeout": PlayFakeoutAnim(); break;
            case "weekend-1-taunt": PlayTauntConditionalAnim(); break;
            case "weekend-1-tauntforce": PlayTauntAnim(); break;
            case "weekend-1-reversefakeout": PlayIdleAnim(); break;
        }
    }

    public void NoteMiss(Note note)
    {
        if (IsDarnellInUppercut())
        {
            PlayUppercutHitAnim();
            return;
        }

        if (WillMissBeLethal())
        {
            PlayHitLowAnim();
            return;
        }

        if (cantUppercut)
        {
            PlayHitHighAnim();
            return;
        }

        switch (note.NoteType)
        {
            case "weekend-1-punchlow": PlayHitLowAnim(); break;
            case "weekend-1-punchlowblocked": PlayHitLowAnim(); break;
            case "weekend-1-punchlowdodged": PlayHitLowAnim(); break;
            case "weekend-1-punchlowspin": PlayHitSpinAnim(); break;

            case "weekend-1-punchhigh": PlayHitHighAnim(); break;
            case "weekend-1-punchhighblocked": PlayHitHighAnim(); break;
            case "weekend-1-punchhighdodged": PlayHitHighAnim(); break;
            case "weekend-1-punchhighspin": PlayHitSpinAnim(); break;

            case "weekend-1-blockhigh": PlayHitHighAnim(); break;
            case "weekend-1-blocklow": PlayHitLowAnim(); break;
            case "weekend-1-blockspin": PlayHitSpinAnim(); break;

            case "weekend-1-dodgehigh": PlayHitHighAnim(); break;
            case "weekend-1-dodgelow": PlayHitLowAnim(); break;
            case "weekend-1-dodgespin": PlayHitSpinAnim(); break;

            case "weekend-1-hithigh": PlayHitHighAnim(); break;
            case "weekend-1-hitlow": PlayHitLowAnim(); break;
            case "weekend-1-hitspin": PlayHitSpinAnim(); break;

            case "weekend-1-picouppercutprep": PlayPunchHighAnim(); cantUppercut = true; break;
            case "weekend-1-picouppercut": PlayUppercutAnim(false); break;

            case "weekend-1-darnelluppercutprep": PlayIdleAnim(); break;
            case "weekend-1-darnelluppercut": PlayUppercutHitAnim(); break;

            case "weekend-1-idle": PlayIdleAnim(); break;
            case "weekend-1-fakeout": PlayHitHighAnim(); break;
            case "weekend-1-taunt": PlayTauntConditionalAnim(); break;
            case "weekend-1-tauntforce": PlayTauntAnim(); break;
            case "weekend-1-reversefakeout": PlayIdleAnim(); break;
        }
    }

    public void NoteMissPress(int direction)
    {
        if (WillMissBeLethal())
            PlayHitLowAnim();
        else
            PlayPunchHighAnim();
    }

    private string DoAlternate()
    {
        alternate = !alternate;
        return alternate ? "1" : "2";
    }

    private void PlayBlockAnim()
    {
        Boyfriend.PlayAnim("block", true);
        PlayScene.Current.CamGame.Shake(0.002f, 0.1f);
        MoveToBack();
    }

    private void PlayCringeAnim()
    {
        Boyfriend.PlayAnim("cringe", true);
        MoveToBack();
    }

    private void PlayDodgeAnim()
    {
        Boyfriend.PlayAnim("dodge", true, false);
        MoveToBack();
    }

    private void PlayIdleAnim()
    {
        Boyfriend.PlayAnim("idle", false);
        MoveToBack();
    }

    private void PlayFakeoutAnim()
    {
        Boyfriend.PlayAnim("fakeout", true);
        MoveToBack();
    }

    private void PlayUppercutPrepAnim()
    {
        Boyfriend.PlayAnim("uppercutPrep", true);
        MoveToFront();
    }

    private void PlayUppercutAnim(bool hit)
    {
        Boyfriend.PlayAnim("uppercut", true);
        if (hit) PlayScene.Current.CamGame.Shake(0.005f, 0.25f);
        MoveToFront();
    }

    private void PlayUppercutHitAnim()
    {
        Boyfriend.PlayAnim("uppercutHit", true);
        PlayScene.Current.CamGame.Shake(0.005f, 0.25f);
        MoveToBack();
    }

    private void PlayHitHighAnim()
    {
        Boyfriend.PlayAnim("hitHigh", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private void PlayHitLowAnim()
    {
        Boyfriend.PlayAnim("hitLow", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private void PlayHitSpinAnim()
    {
        Boyfriend.PlayAnim("hitSpin", true);
        PlayScene.Current.CamGame.Shake(0.0025f, 0.15f);
        MoveToBack();
    }

    private void PlayPunchHighAnim()
    {
        Boyfriend.PlayAnim("punchHigh" + DoAlternate(), true);
        MoveToFront();
    }

    private void PlayPunchLowAnim()
    {
        Boyfriend.PlayAnim("punchLow" + DoAlternate(), true);
        MoveToFront();
    }

    private void PlayTauntConditionalAnim()
    {
        if (Boyfriend.GetAnimationName() == "fakeout")
            PlayTauntAnim();
        else
            PlayIdleAnim();
    }

    private void PlayTauntAnim()
    {
        Boyfriend.PlayAnim("taunt", true);
        MoveToBack();
    }

    private bool WillMissBeLethal()
    {
        return PlayScene.Current.Health <= 0.0f && !PlayScene.Current.PracticeMode;
    }

    private bool IsDarnellPreppingUppercut()
    {
        return Dad.GetAnimationName() == "uppercutPrep";
    }

    private bool IsDarnellInUppercut()
    {
        return Dad.GetAnimationName() == "uppercut" || Dad.GetAnimationName() == "uppercut-hold";
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
        int bfPos = members.IndexOf(BoyfriendGroup);
        int dadPos = members.IndexOf(DadGroup);
        if (bfPos < dadPos) return;

        members[dadPos] = BoyfriendGroup;
        members[bfPos] = DadGroup;
    }

    private void MoveToFront()
    {
        var members = PlayScene.Current.Stage.Members;
        int bfPos = members.IndexOf(BoyfriendGroup);
        int dadPos = members.IndexOf(DadGroup);
        if (bfPos > dadPos) return;

        members[dadPos] = BoyfriendGroup;
        members[bfPos] = DadGroup;
    }

    private Character Boyfriend => PlayScene.Current.Boyfriend;
    private Character Dad => PlayScene.Current.Dad;
    private SpriteGroup BoyfriendGroup => PlayScene.Current.BoyfriendGroup;
    private SpriteGroup DadGroup => PlayScene.Current.DadGroup;
}
