namespace FNF_MonoGame.Scenes;

// Dev log for automated bug-hunt runs (FNF_SYNCLOG=1): one [BH2] line when the song ends with the loaded
// characters/stage/variation, tallies and audio stream underruns (audio falling behind the song clock).
public partial class PlayScene
{
    private bool _bh2EndLogged;

    private void DevBh2EndLog()
    {
#if WINDOWS_UAP
        if (_bh2EndLogged || System.Environment.GetEnvironmentVariable("FNF_SYNCLOG") != "1") return;
        var u = new int[3];
#else
        if (_bh2EndLogged || !FNF_MonoGame.Engine.AudioManager.DevSyncLog) return;
        var u = FNF_MonoGame.Engine.AudioManager.DevUnderruns;
#endif
        _bh2EndLogged = true;
        System.Console.WriteLine($"[BH2] END song={_songName} diff={_currentDifficulty} var={_selectedCharacterVariation} week={_weekId ?? "-"} "
            + $"bf={_boyfriend?.OriginalName} dad={_opponent?.OriginalName} gf={_girlfriend?.OriginalName} stage={System.IO.Path.GetFileName(_stageJsonFilePath ?? "")} "
            + $"pos={_conductor.SongPosition:F2} instLen={base.Audio.MusicLength:F2} playing={base.Audio.MusicPlaying} forced={_forceEndSong} "
            + $"score={_score} hit={_totalNotesHit}/{_totalNotes} sick={_tallySick} miss={_tallyMissed} combo={_maxCombo} health={_health:F2} "
            + $"underruns={u[0]}/{u[1]}/{u[2]}");
    }
}
