namespace FNF_MonoGame.Scenes;

// Story-mode Results data (PlayState.moveToResultsScreen): in story mode the Results screen shows
// Highscore.talliesLevel (tallies of every song of the level, combined at each song end by
// Highscore.combineTallies), PlayStatePlaylist.campaignScore and the level title (campaignTitle),
// not only the last song. StoryMenuState resets talliesLevel when a level starts.
public partial class PlayScene
{
    /// <summary>PlayStatePlaylist.campaignTitle (level name, set by the story menu).</summary>
    public static string StoryCampaignTitle;
    // Highscore.talliesLevel: sick, good, bad, shit, missed, totalNotesHit, totalNotes, maxCombo
    private static readonly int[] _talliesLevel = new int[8];

    /// <summary>StoryMenuState: Highscore.talliesLevel = new Tallies(); campaignTitle = level.getTitle().</summary>
    public static void ResetStoryCampaign(string title)
    {
        System.Array.Clear(_talliesLevel, 0, _talliesLevel.Length);
        StoryCampaignTitle = title;
    }

    /// <summary>PlayState.endSong (story): talliesLevel = combineTallies(tallies, talliesLevel).</summary>
    private void CombineStoryTallies()
    {
        _talliesLevel[0] += _tallySick; _talliesLevel[1] += _tallyGood; _talliesLevel[2] += _tallyBad;
        _talliesLevel[3] += _tallyShit; _talliesLevel[4] += _tallyMissed; _talliesLevel[5] += _totalNotesHit;
        _talliesLevel[6] += _totalNotes; _talliesLevel[7] = System.Math.Max(_talliesLevel[7], _maxCombo);
    }

    private ResultsScene CreateResultsScene(bool isNewHighscore)
    {
        if (_weekId == null)
            return new ResultsScene(_score, _maxCombo, _misses, _songName, _currentDifficulty, isNewHighscore, _tallySick, _tallyGood, _tallyBad, _tallyShit, _tallyMissed, _totalNotesHit, _totalNotes, false)
            { Title = $"{_chart?.DisplayName ?? _songName} by {_chart?.Artist ?? "Unknown"}", ReplayPath = ReplaySaveOnEnd() };
        var t = _talliesLevel;
        return new ResultsScene(_weekAccumulatedScore + _score, t[7], t[4], _songName, _currentDifficulty, isNewHighscore, t[0], t[1], t[2], t[3], t[4], t[5], t[6], true)
        { Title = StoryCampaignTitle ?? _weekId.ToUpperInvariant() };
    }
}
