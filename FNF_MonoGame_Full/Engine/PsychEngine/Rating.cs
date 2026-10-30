using System.Collections.Generic;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Rating system — Sick, Good, Bad, Shit.
/// Direct port of backend/Rating.hx.
/// </summary>
public class Rating
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public float HitWindow { get; set; } = 0f; // ms
    /// <summary>Lua/HScript 'hitWindow': Rating.new does Reflect.field(ClientPrefs.data, name + 'Window'); there is no
    /// 'shitWindow' -> null for 'shit' (the judge uses safeZoneOffset for it).</summary>
    public object hitWindow
    {
        get => _hitWindowNull ? null : (object)(double)HitWindow;
        set { _hitWindowNull = value == null; if (value != null) HitWindow = System.Convert.ToSingle(value); }
    }
    private bool _hitWindowNull;
    public float RatingMod { get; set; } = 1f;
    public int Score { get; set; } = 350;
    public bool NoteSplash { get; set; } = true;
    public int Hits { get; set; } = 0;

    public Rating(string name)
    {
        Name = name;
        Image = name;

        // Load hit window from ClientPrefs
        HitWindow = name.ToLower() switch
        {
            "sick" => ClientPrefs.Data.SickWindow,
            "good" => ClientPrefs.Data.GoodWindow,
            "bad" => ClientPrefs.Data.BadWindow,
            _ => 166f // safe frames fallback
        };
        _hitWindowNull = !(name.ToLower() is "sick" or "good" or "bad");
    }

    /// <summary>
    /// Load the default rating list (highest priority first).
    /// Matches Psych Engine Rating.loadDefault() exactly.
    /// </summary>
    public static List<Rating> LoadDefault()
    {
        var ratings = new List<Rating>();

        // Sick — highest rating, full score
        var sick = new Rating("sick");
        ratings.Add(sick);

        // Good
        var good = new Rating("good");
        good.RatingMod = 0.67f;
        good.Score = 200;
        good.NoteSplash = false;
        ratings.Add(good);

        // Bad
        var bad = new Rating("bad");
        bad.RatingMod = 0.34f;
        bad.Score = 100;
        bad.NoteSplash = false;
        ratings.Add(bad);

        // Shit
        var shit = new Rating("shit");
        shit.RatingMod = 0f;
        shit.Score = 50;
        shit.NoteSplash = false;
        ratings.Add(shit);

        return ratings;
    }

    /// <summary>
    /// Determine which rating a note hit gets based on timing difference.
    /// </summary>
    public static Rating Judge(float diffMs, List<Rating> ratings)
    {
        float absDiff = System.Math.Abs(diffMs);
        int offset = ClientPrefs.Data.RatingOffset;

        for (int i = 0; i < ratings.Count; i++)
        {
            if (absDiff <= ratings[i].HitWindow + offset)
                return ratings[i];
        }

        // If nothing matched, return the worst rating
        return ratings.Count > 0 ? ratings[^1] : null;
    }
}
