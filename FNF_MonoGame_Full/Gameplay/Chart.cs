using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Represents a song chart with all note data
/// Supports both legacy FNF format and new Funkin' format
/// </summary>
public class Chart
{
    public string SongName { get; set; }
    /// <summary>Psych chart file (Song.chartPath) and SONG.needsVoices for Psych-mod charts.</summary>
    public string PsychChartPath { get; set; }
    public bool NeedsVoices { get; set; } = true;
    /// <summary>Display name from metadata ("songName"), e.g. "Bopeebo". Psych's SONG.song.</summary>
    public string DisplayName { get; set; }
    public float BPM { get; set; } = 120;
    public float Speed { get; set; } = 1.0f;
    public double SongLength { get; set; } = 180; // seconds
    public string PlayerCharacter { get; set; } = "bf";
    public string OpponentCharacter { get; set; } = "dad";
    public string GirlfriendCharacter { get; set; } = "gf";
    public string Stage { get; set; } = "stage";
    public string Difficulty { get; set; } = "normal";
    public string Artist { get; set; } = "Unknown";
    public string Charter { get; set; } = "Unknown";
    public string NoteStyle { get; set; } = "funkin";
    public string Album { get; set; } = "volume1";
    /// <summary>meta playData.difficulties of the loaded variation (Song.listDifficulties), null if unknown.</summary>
    public List<string> Difficulties { get; set; }
    
    public List<Note> Notes { get; set; } = new();
    public List<Section> Sections { get; set; } = new();
    public List<GameEvent> Events { get; set; } = new();
    
    /// <summary>
    /// BPM changes parsed from timeChanges. Each entry is (timeSeconds, newBPM).
    /// Sorted by time. The first entry is (0, initialBPM).
    /// </summary>
    public List<(double Time, float Bpm)> BPMChanges { get; set; } = new();
    
    public static Chart Load(string songName, AssetManager assets, string difficulty = "normal", string modPath = null)
    {
        // If this is a mod song, try Psych Engine format first
        if (!string.IsNullOrEmpty(modPath))
        {
            var modChart = TryLoadPsychEngineChart(songName, difficulty, modPath);
            if (modChart != null)
            {
                Console.WriteLine($"[CHART] Loaded Psych Engine chart: {songName} ({difficulty}) from mod");
                return modChart;
            }
        }

        // Try multiple name variants (dad-battle, dad_battle, dadbattle)
        var candidates = new List<string> { songName };
        if (songName.Contains('-'))
        {
            candidates.Add(songName.Replace('-', '_'));
            candidates.Add(songName.Replace("-", ""));
        }
        if (songName.Contains('_'))
        {
            candidates.Add(songName.Replace('_', '-'));
            candidates.Add(songName.Replace("_", ""));
        }
        
        foreach (var name in candidates.Distinct())
        {
            // Try new format first (charts/chart.json)
            string newChartPath = $"songs/{name}/charts/chart.json";
            string metaPath = $"songs/{name}/charts/meta.json";
            
            var newChart = assets.LoadJson<NewChartFile>(newChartPath);
            if (newChart?.Notes != null)
            {
                var meta = assets.LoadJson<ChartMeta>(metaPath);
                return ParseNewFNFChart(songName, newChart, meta, difficulty);
            }
            
            // Try legacy format
            string legacyPath = $"songs/{name}/chart";
            var legacyChart = assets.LoadJson<ChartFile>(legacyPath);
            if (legacyChart?.Song != null)
            {
                return ParseLegacyFNFChart(legacyChart, difficulty);
            }
        }
        
        // Return demo chart if all fails
        return CreateDemoChart(songName);
    }

    /// <summary>
    /// Try to load a Psych Engine format chart from a mod folder.
    /// Psych Engine charts: data/<songname>/<songname>-<difficulty>.json or data/<songname>/<songname>.json
    /// </summary>
    private static Chart TryLoadPsychEngineChart(string songName, string difficulty, string modPath)
    {
        string dataDir = System.IO.Path.Combine(modPath, "data", songName);
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dataDir))
        {
            // Try with spaces replaced
            string altName = songName.Replace(" ", "-").ToLowerInvariant();
            dataDir = System.IO.Path.Combine(modPath, "data", altName);
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dataDir))
                return null;
            songName = altName;
        }

        // Try difficulty-specific chart: <songname>-<difficulty>.json
        string chartPath = null;
        string diffLower = difficulty.ToLowerInvariant();

        if (diffLower != "normal")
        {
            chartPath = System.IO.Path.Combine(dataDir, $"{songName}-{diffLower}.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(chartPath))
                chartPath = null;
        }

        // Fall back to base chart: <songname>.json (normal difficulty)
        if (chartPath == null)
        {
            chartPath = System.IO.Path.Combine(dataDir, $"{songName}.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(chartPath))
            {
                // Try any chart file in the folder
                var jsonFiles = global::FNF_MonoGame.Engine.FsCI.GetFiles(dataDir, "*.json")
                    .Where(f => !System.IO.Path.GetFileName(f).Equals("events.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (jsonFiles.Length > 0)
                    chartPath = jsonFiles[0];
                else
                    return null;
            }
        }

        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(chartPath);
            // Psych Song.parseJSON: the 'song' wrapper is optional (1.0 "psych_v1" charts are unwrapped, 'song' = name string)
            var root = JObject.Parse(json);
            var chartFile = root["song"] is JObject ? root.ToObject<ChartFile>() : new ChartFile { Song = root.ToObject<ChartSong>() };
            if (chartFile?.Song != null)
            {
                var chart = ParseLegacyFNFChart(chartFile, difficulty);
                chart.SongName = songName;
                chart.DisplayName = chartFile.Song.Song; chart.PsychChartPath = chartPath; chart.NeedsVoices = chartFile.Song.NeedsVoices; // SONG.song / Song.chartPath / needsVoices
                MergePsychEventsFile(chart, dataDir);
                return chart;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHART] Error parsing Psych Engine chart {chartPath}: {ex.Message}");
        }

        return null;
    }
    
    /// <summary>Psych Online: parse a Psych/legacy chart file (data/&lt;folder&gt;/&lt;song&gt;.json) as is.</summary>
    public static Chart LoadPsychFile(string chartPath, string difficulty)
    {
        try
        {
            var root = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(chartPath));
            if (!(root["song"] is JObject) && root["notes"] != null) root = new JObject { ["song"] = root };   // Psych 1.0 "psych_v1": no wrapper
            var chartFile = root.ToObject<ChartFile>();
            var pc = chartFile?.Song != null ? ParseLegacyFNFChart(chartFile, difficulty) : null;
            if (pc != null) { pc.PsychChartPath = chartPath; pc.NeedsVoices = chartFile.Song.NeedsVoices; MergePsychEventsFile(pc, System.IO.Path.GetDirectoryName(chartPath)); }
            return pc;
        }
        catch (Exception ex) { Console.WriteLine($"[CHART] Error parsing {chartPath}: {ex.Message}"); return null; }
    }

    /// <summary>PlayState.generateSong 1327-1333: Song.getChart('events', songName) -> data/&lt;song&gt;/events.json events are
    /// added (makeEvent) on top of the chart's own events, duplicates included; old-format event notes in sectionNotes
    /// (noteData &lt; 0, Song.onLoadJson) count too. Stable time sort afterwards (events.json events first on ties).</summary>
    private static void MergePsychEventsFile(Chart chart, string dataDir)
    {
        try
        {
            if (chart == null || string.IsNullOrEmpty(dataDir)) return;
            string path = System.IO.Path.Combine(dataDir, "events.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return;
            var root = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
            var song = root["song"] as JObject ?? root;
            var extra = new List<GameEvent>();
            void Add(double ms, JToken name, JToken v1, JToken v2) =>
                extra.Add(new GameEvent { Time = ms / 1000.0, Name = name?.ToString() ?? "", Value = new string[] { v1?.ToString() ?? "", v2?.ToString() ?? "" } });
            if (song["notes"] is JArray secs)
                foreach (var sec in secs)
                    if (sec?["sectionNotes"] is JArray sn)
                        foreach (var n in sn)
                            if (n is JArray a && a.Count >= 5 && a[1].Type != JTokenType.String && a[1].ToObject<double>() < 0)
                                Add(a[0].ToObject<double>(), a[2], a[3], a[4]);
            if (song["events"] is JArray evs)
                foreach (var e in evs)
                    if (e is JArray g && g.Count >= 2 && g[1] is JArray subs)
                        foreach (var sub in subs)
                            if (sub is JArray d && d.Count >= 1)
                                Add(g[0].ToObject<double>(), d[0], d.Count > 1 ? d[1] : null, d.Count > 2 ? d[2] : null);
            if (extra.Count == 0) return;
            int added = extra.Count;
            extra.AddRange(chart.Events);
            chart.Events = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(extra, ev => ev.Time));
            Console.WriteLine($"[CHART] events.json: +{added} -> {chart.Events.Count} events");
        }
        catch (Exception ex) { Console.WriteLine($"[CHART] events.json error: {ex.Message}"); }
    }

    private static Chart ParseNewFNFChart(string songName, NewChartFile chartData, ChartMeta meta, string difficulty = "normal")
    {
        // Pick scroll speed for the chosen difficulty
        float scrollSpeed = chartData.ScrollSpeed?.GetSpeed(difficulty) ?? 1.5f;
        
        var chart = new Chart
        {
            SongName = songName,
            DisplayName = meta?.SongName,
            BPM = meta?.TimeChanges?.FirstOrDefault()?.Bpm ?? 100,
            Speed = scrollSpeed,
            PlayerCharacter = meta?.PlayData?.Characters?.Player ?? "bf",
            OpponentCharacter = meta?.PlayData?.Characters?.Opponent ?? "dad",
            GirlfriendCharacter = meta?.PlayData?.Characters?.Girlfriend ?? "", // V-Slice SongCharacterData.girlfriend @:default('') = no GF
            Stage = meta?.PlayData?.Stage ?? "stage",
            Difficulty = difficulty,
            Artist = meta?.Artist ?? "Unknown",
            Charter = meta?.Charter ?? "Unknown",
            NoteStyle = meta?.PlayData?.NoteStyle ?? "funkin",
            Album = meta?.Album ?? "volume1",
            Difficulties = meta?.PlayData?.Difficulties
        };
        
        // Get notes from the specified difficulty, fall back to others
        List<NewNote> noteList = chartData.Notes.GetByName(difficulty);
        
        // Difficulty-aware fallback chain
        if (noteList == null || noteList.Count == 0)
        {
            noteList = difficulty.ToLower() switch
            {
                "easy" => chartData.Notes.Normal ?? chartData.Notes.Hard,
                "hard" => chartData.Notes.Normal ?? chartData.Notes.Easy,
                "erect" => chartData.Notes.Hard ?? chartData.Notes.Normal,
                "nightmare" => chartData.Notes.Erect ?? chartData.Notes.Hard,
                _ => chartData.Notes.Normal ?? chartData.Notes.Easy ?? chartData.Notes.Hard
            };
        }
        
        // Final fallback: try any non-null list with notes
        if (noteList == null || noteList.Count == 0)
        {
            noteList = chartData.Notes.GetAny();
        }
        
        if (noteList != null)
        {
            foreach (var note in noteList)
            {
                // Original FNF: getStrumlineIndex = floor(data / 4)
                // Strumline 0 = PLAYER (BF), Strumline 1 = OPPONENT (Dad)
                // d=0-3: player notes (strumline 0), d=4-7: opponent notes (strumline 1)
                bool isPlayerNote = note.D < 4;
                int lane = note.D % 4;
                
                chart.Notes.Add(new Note
                {
                    Time = note.T / 1000.0, // Convert ms to seconds
                    Lane = lane,
                    SustainLength = (note.L ?? 0) / 1000.0,
                    IsPlayerNote = isPlayerNote,
                    Kind = note.K
                });
            }
        }
        
        // Sort notes by time
        chart.Notes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(chart.Notes, n => n.Time)); // stable like Strumline.applyNoteData insertionSort: chord notes keep chart order (last one sets the sing pose)
        
        // Parse events
        if (chartData.Events != null)
        {
            foreach (var ev in chartData.Events)
            {
                chart.Events.Add(new GameEvent
                {
                    Time = ev.T / 1000.0,
                    Name = ev.E ?? "",
                    Value = ev.V
                });
            }
            chart.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        
        // Parse BPM changes from timeChanges
        chart.BPMChanges.Add((0.0, chart.BPM));
        if (meta?.TimeChanges != null && meta.TimeChanges.Count > 1)
        {
            for (int i = 1; i < meta.TimeChanges.Count; i++)
            {
                var tc = meta.TimeChanges[i];
                chart.BPMChanges.Add((tc.T / 1000.0, tc.Bpm));
            }
            chart.BPMChanges.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        
        // Calculate song length from last note + buffer
        if (chart.Notes.Count > 0)
        {
            chart.SongLength = chart.Notes.Last().Time + 5;
        }
        
        return chart;
    }
    
    /// <summary>
    /// Psych Engine default note types for backwards compatibility with 0.1-0.3 charts.
    /// Index 0 is always empty. Matches Note.hx defaultNoteTypes.
    /// </summary>
    private static readonly string[] PsychDefaultNoteTypes = {
        "", "Alt Animation", "Hey!", "Hurt Note", "GF Sing", "No Animation"
    };

    private static Chart ParseLegacyFNFChart(ChartFile chartFile, string difficulty = "normal")
    {
        var song = chartFile.Song;

        // Psych Engine: gfVersion fallback to player3
        string gfVersion = song.GfVersion;
        if (string.IsNullOrEmpty(gfVersion))
            gfVersion = song.Player3;
        if (string.IsNullOrEmpty(gfVersion))
            gfVersion = "gf";

        var chart = new Chart
        {
            SongName = song.Song,
            BPM = song.Bpm,
            Speed = song.Speed,
            PlayerCharacter = song.Player1 ?? "bf",
            OpponentCharacter = song.Player2 ?? "dad",
            GirlfriendCharacter = gfVersion,
            Stage = song.Stage ?? "stage",
            Difficulty = difficulty
        };

        // Detect Psych Engine psych_v1 format
        bool isPsychV1 = song.Format != null && song.Format.StartsWith("psych_v1");

        // If not psych_v1, convert notes to psych_v1 format (matching Song.hx convert())
        // This normalizes note[1] to absolute: 0-3=player, 4-7=opponent
        if (!isPsychV1 && song.Notes != null)
        {
            ConvertToPsychV1(song);
        }

        // Parse sections and notes using Psych Engine timing (sectionBeats)
        double currentTime = 0;
        float currentBpm = chart.BPM;
        bool? lastMustHit = null;

        // Initialize BPM changes with starting BPM
        chart.BPMChanges.Add((0.0, chart.BPM));

        if (song.Notes != null)
        {
            foreach (var section in song.Notes)
            {
                float sectionBeats = section.GetSectionBeats();
                int stepsInSection = (int)Math.Round(sectionBeats * 4);
                bool mustHitSection = section.MustHitSection ?? true;
                bool isGfSection = section.GfSection ?? false;
                bool isAltAnim = section.AltAnim ?? false;
                // Psych MusicBeatState.updateSection: section lengths = round(sectionBeats*4) steps (curSection / onSectionHit / mustHitSection)
                chart.Sections.Add(new Section { MustHitSection = mustHitSection, GfSection = isGfSection, AltAnim = isAltAnim, LengthInSteps = stepsInSection });

                // Handle BPM changes (Psych Engine: Conductor.hx mapBPMChanges)
                if (section.ChangeBPM == true && section.Bpm.HasValue && section.Bpm.Value > 0
                    && Math.Abs(section.Bpm.Value - currentBpm) > 0.001f)
                {
                    currentBpm = section.Bpm.Value;
                    chart.BPMChanges.Add((currentTime, currentBpm));
                }

                // Generate FocusCamera events from mustHitSection changes
                if (lastMustHit != mustHitSection)
                {
                    lastMustHit = mustHitSection;
                    chart.Events.Add(new GameEvent
                    {
                        Time = currentTime,
                        Name = "FocusCamera",
                        Value = mustHitSection ? 0 : 1, PortSynthetic = true
                    });
                }

                if (section.SectionNotes != null)
                {
                    foreach (var noteArr in section.SectionNotes)
                    {
                        if (noteArr == null || noteArr.Count < 2) continue;

                        double noteTime = noteArr[0].ToObject<double>() / 1000.0; // ms to seconds
                        int noteDirection = noteArr[1].ToObject<int>();
                        double sustainLength = noteArr.Count > 2 ? noteArr[2].ToObject<double>() / 1000.0 : 0;

                        // In psych_v1 (or after conversion): 0-3=player, 4-7=opponent
                        bool isPlayerNote = noteDirection < 4;
                        int lane = noteDirection % 4;

                        // Parse note type (4th element) - Psych Engine
                        string noteType = null;
                        if (noteArr.Count > 3)
                        {
                            var typeToken = noteArr[3];
                            if (typeToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            {
                                noteType = typeToken.ToObject<string>();
                            }
                            else if (typeToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                            {
                                // Old format: numeric index into defaultNoteTypes
                                int typeIdx = typeToken.ToObject<int>();
                                if (typeIdx >= 0 && typeIdx < PsychDefaultNoteTypes.Length)
                                    noteType = PsychDefaultNoteTypes[typeIdx];
                            }
                        }

                        // Determine note flags from noteType
                        bool gfNote = isGfSection;
                        bool altAnim = isAltAnim;
                        bool noAnimation = false;

                        if (!string.IsNullOrEmpty(noteType))
                        {
                            switch (noteType)
                            {
                                case "Alt Animation":
                                    altAnim = true;
                                    break;
                                case "GF Sing":
                                    gfNote = true;
                                    break;
                                case "No Animation":
                                    noAnimation = true;
                                    break;
                            }
                        }

                        // Skip event notes (negative noteData in old format)
                        if (noteDirection < 0) continue;

                        chart.Notes.Add(new Note
                        {
                            Time = noteTime,
                            Lane = lane,
                            SustainLength = sustainLength,
                            IsPlayerNote = isPlayerNote,
                            NoteType = noteType,
                            GfNote = gfNote,
                            AltAnim = altAnim,
                            NoAnimation = noAnimation
                        });
                    }
                }

                // Advance time: Psych Engine uses (60/bpm * 1000 / 4) * deltaSteps for ms
                double stepCrochetMs = (60.0 / currentBpm) * 1000.0 / 4.0;
                currentTime += stepsInSection * stepCrochetMs / 1000.0; // convert ms to seconds
            }
        }

        // Parse song-level events array (Psych Engine format)
        // Format: [[strumTime, [[eventName, value1, value2]], ...], ...]
        if (song.Events != null)
        {
            try
            {
                foreach (var eventGroup in song.Events)
                {
                    if (eventGroup == null || eventGroup.Count < 2) continue;
                    double eventTime = Convert.ToDouble(eventGroup[0]) / 1000.0;
                    
                    // eventGroup[1] is array of [eventName, value1, value2]
                    if (eventGroup[1] is Newtonsoft.Json.Linq.JArray subEvents)
                    {
                        foreach (var subEvent in subEvents)
                        {
                            if (subEvent is Newtonsoft.Json.Linq.JArray eventData && eventData.Count >= 1)
                            {
                                string eventName = eventData[0]?.ToString() ?? "";
                                string val1 = eventData.Count > 1 ? eventData[1]?.ToString() ?? "" : "";
                                string val2 = eventData.Count > 2 ? eventData[2]?.ToString() ?? "" : "";

                                chart.Events.Add(new GameEvent
                                {
                                    Time = eventTime,
                                    Name = eventName,
                                    Value = new string[] { val1, val2 }
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CHART] Error parsing Psych events: {ex.Message}");
            }
        }

        chart.SongLength = currentTime;

        // Sort notes by time
        chart.Notes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(chart.Notes, n => n.Time)); // stable like Strumline.applyNoteData insertionSort: chord notes keep chart order (last one sets the sing pose)

        // Sort events by time
        chart.Events.Sort((a, b) => a.Time.CompareTo(b.Time));

        return chart;
    }

    /// <summary>
    /// Convert old format chart notes to psych_v1 absolute format.
    /// Matches Song.hx convert() exactly.
    /// After conversion: note[1] 0-3 = player must hit, 4-7 = opponent must hit.
    /// </summary>
    private static void ConvertToPsychV1(ChartSong song)
    {
        if (song.Notes == null) return;

        // Extract events from notes with negative noteData
        if (song.Events == null)
            song.Events = new List<List<object>>();

        foreach (var section in song.Notes)
        {
            if (section.SectionNotes == null) continue;
            bool mustHitSection = section.MustHitSection ?? true;

            // Fix sectionBeats
            float beats = section.GetSectionBeats();
            section.SectionBeats = beats;

            var toRemove = new List<Newtonsoft.Json.Linq.JArray>();
            foreach (var note in section.SectionNotes)
            {
                if (note == null || note.Count < 2) continue;

                int noteData = note[1].ToObject<int>();

                // Negative noteData = event note in old format
                if (noteData < 0)
                {
                    // Extract as event: [strumTime, [[eventName, val1, val2]]]
                    double strumTime = note[0].ToObject<double>();
                    string evName = note.Count > 2 ? note[2]?.ToString() ?? "" : "";
                    string evVal1 = note.Count > 3 ? note[3]?.ToString() ?? "" : "";
                    string evVal2 = note.Count > 4 ? note[4]?.ToString() ?? "" : "";
                    song.Events.Add(new List<object> { strumTime, new List<object> { new List<object> { evName, evVal1, evVal2 } } });
                    toRemove.Add(note);
                    continue;
                }

                // Convert to absolute: gottaHitNote determines if 0-3 or 4-7
                bool gottaHitNote = (noteData < 4) ? mustHitSection : !mustHitSection;
                int newNoteData = (noteData % 4) + (gottaHitNote ? 0 : 4);
                note[1] = newNoteData;

                // Convert numeric note type to string
                if (note.Count > 3)
                {
                    var typeToken = note[3];
                    if (typeToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    {
                        int typeIdx = typeToken.ToObject<int>();
                        if (typeIdx >= 0 && typeIdx < PsychDefaultNoteTypes.Length)
                            note[3] = PsychDefaultNoteTypes[typeIdx];
                    }
                }
            }

            foreach (var rem in toRemove)
                section.SectionNotes.Remove(rem);
        }

        song.Format = "psych_v1_convert";
    }
    
    private static Chart CreateDemoChart(string songName)
    {
        var chart = new Chart
        {
            SongName = songName,
            BPM = 150,
            Speed = 1.5f,
            SongLength = 60
        };
        
        // Create a simple demo pattern
        var random = new Random(songName.GetHashCode());
        double time = 2.0; // Start 2 seconds in
        
        while (time < chart.SongLength - 5)
        {
            chart.Notes.Add(new Note
            {
                Time = time,
                Lane = random.Next(4),
                IsPlayerNote = true
            });
            
            time += 60.0 / chart.BPM * (random.Next(2) + 1) * 0.5; // Random spacing
        }
        
        return chart;
    }
}

/// <summary>
/// Represents a single note in the chart
/// </summary>
public class Note
{
    public double Time { get; set; }      // Time in seconds
    public int Lane { get; set; }          // 0=Left, 1=Down, 2=Up, 3=Right
    public double SustainLength { get; set; } // Hold note duration
    public bool IsPlayerNote { get; set; } = true;
    public bool IsHit { get; set; } = false;
    public bool HoldComplete { get; set; } = false; // True when hold note fully consumed
    public string Kind { get; set; }      // Note kind (e.g., "mom" for alt animations)
    public string NoteType { get; set; }  // Psych Engine note type (e.g., "Alt Animation", "Hurt Note", "GF Sing")
    public bool GfNote { get; set; }      // Whether this note should be sung by GF
    public bool AltAnim { get; set; }     // Whether to use alt animation suffix
    public bool NoAnimation { get; set; } // Whether to skip sing animation
    public bool NoMissAnimation { get; set; } // Whether to skip miss animation
    // Psych Engine per-note settings (objects/Note.hx defaults); set by notetype scripts via unspawnNotes
    public float HitHealth { get; set; } = 0.02f;
    public float MissHealth { get; set; } = 0.1f;
    public bool IgnoreNote { get; set; }
    public bool HitCausesMiss { get; set; }
    public bool LowPriority { get; set; }
    public string Texture { get; set; }
    public float MultAlpha { get; set; } = 1f;
    public bool NoteSplashDisabled { get; set; }
    /// <summary>Psych rgbShader colours (ARGB r,g,b) for this note; null = Psych default arrowRGB.</summary>
    public uint[] Rgb { get; set; }
    public bool RgbDisabled { get; set; }
    /// <summary>Psych noteSplashData.texture / .a</summary>
    public string SplashTexture { get; set; }
    public float SplashAlpha { get; set; } = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.SplashAlpha;   // Psych noteSplashData.a = ClientPrefs.splashAlpha (0.6)
    public bool BlockHit { get; set; }
    /// <summary>Psych runtime per-note alpha/visibility written by scripts on the spawned note (notes group).</summary>
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;

    public void CopyPsychFields(Note o)
    {
        HitHealth = o.HitHealth; MissHealth = o.MissHealth; IgnoreNote = o.IgnoreNote; HitCausesMiss = o.HitCausesMiss;
        LowPriority = o.LowPriority; Texture = o.Texture; MultAlpha = o.MultAlpha; NoteSplashDisabled = o.NoteSplashDisabled;
        Rgb = o.Rgb; RgbDisabled = o.RgbDisabled; SplashTexture = o.SplashTexture; SplashAlpha = o.SplashAlpha;
        BlockHit = o.BlockHit; NoMissAnimation = o.NoMissAnimation; Alpha = o.Alpha; Visible = o.Visible;
    }

    public void ResetPsychFields()
    {
        HitHealth = 0.02f; MissHealth = 0.1f; IgnoreNote = false; HitCausesMiss = false; LowPriority = false;
        Texture = null; MultAlpha = 1f; NoteSplashDisabled = false; Rgb = null; RgbDisabled = false; SplashTexture = null; SplashAlpha = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.SplashAlpha; BlockHit = false; NoMissAnimation = false; Alpha = 1f; Visible = true;
    }
}

public class Section
{
    public bool MustHitSection { get; set; }
    public bool GfSection { get; set; }
    public bool AltAnim { get; set; }
    public int LengthInSteps { get; set; } = 16;
    public List<Note> Notes { get; set; } = new();
}

// JSON deserialization classes for FNF chart format
public class ChartFile
{
    [JsonProperty("song")]
    public ChartSong Song { get; set; }
}

public class ChartSong
{
    [JsonProperty("song")]
    public string Song { get; set; }
    
    [JsonProperty("bpm")]
    public float Bpm { get; set; }
    
    [JsonProperty("speed")]
    public float Speed { get; set; }
    
    [JsonProperty("player1")]
    public string Player1 { get; set; }
    
    [JsonProperty("player2")]
    public string Player2 { get; set; }
    
    [JsonProperty("gfVersion")]
    public string GfVersion { get; set; }
    
    // Psych Engine also checks "player3" as fallback for gfVersion
    [JsonProperty("player3")]
    public string Player3 { get; set; }
    
    [JsonProperty("stage")]
    public string Stage { get; set; }
    
    [JsonProperty("notes")]
    public List<ChartSection> Notes { get; set; }
    
    // Psych Engine format fields
    [JsonProperty("format")]
    public string Format { get; set; }
    
    [JsonProperty("offset")]
    public float Offset { get; set; }
    
    [JsonProperty("needsVoices")]
    public bool NeedsVoices { get; set; } = true;
    
    [JsonProperty("events")]
    public List<List<object>> Events { get; set; }
    
    [JsonProperty("gameOverChar")]
    public string GameOverChar { get; set; }
    
    [JsonProperty("gameOverSound")]
    public string GameOverSound { get; set; }
    
    [JsonProperty("gameOverLoop")]
    public string GameOverLoop { get; set; }
    
    [JsonProperty("gameOverEnd")]
    public string GameOverEnd { get; set; }
    
    [JsonProperty("disableNoteRGB")]
    public bool DisableNoteRGB { get; set; }
    
    [JsonProperty("arrowSkin")]
    public string ArrowSkin { get; set; }
    
    [JsonProperty("splashSkin")]
    public string SplashSkin { get; set; }
}

public class ChartSection
{
    [JsonProperty("mustHitSection")]
    public bool? MustHitSection { get; set; }
    
    [JsonProperty("lengthInSteps")]
    public int? LengthInSteps { get; set; }
    
    // Psych Engine psych_v1 format uses sectionBeats instead of lengthInSteps
    [JsonProperty("sectionBeats")]
    public float? SectionBeats { get; set; }
    
    // Use JArray to handle mixed types in sectionNotes (strumTime:float, noteData:int, sustainLength:float, noteType:string)
    [JsonProperty("sectionNotes")]
    public List<Newtonsoft.Json.Linq.JArray> SectionNotes { get; set; }
    
    [JsonProperty("bpm")]
    public float? Bpm { get; set; }
    
    [JsonProperty("changeBPM")]
    public bool? ChangeBPM { get; set; }
    
    // Psych Engine section flags
    [JsonProperty("altAnim")]
    public bool? AltAnim { get; set; }
    
    [JsonProperty("gfSection")]
    public bool? GfSection { get; set; }
    
    /// <summary>
    /// Get the number of beats in this section. Psych v1 uses sectionBeats, legacy uses lengthInSteps/4.
    /// </summary>
    public float GetSectionBeats()
    {
        if (SectionBeats.HasValue && !float.IsNaN(SectionBeats.Value))
            return SectionBeats.Value;
        if (LengthInSteps.HasValue)
            return LengthInSteps.Value / 4f;
        return 4f; // Default: 4 beats per section
    }
}

// ============================================
// NEW FNF FORMAT (Funkin' / Psych Engine style)
// ============================================

public class NewChartFile
{
    [JsonProperty("scrollSpeed")]
    public ScrollSpeed ScrollSpeed { get; set; }
    
    [JsonProperty("notes")]
    public NotesByDifficulty Notes { get; set; }
    
    [JsonProperty("events")]
    public List<ChartEvent> Events { get; set; }
}

public class ScrollSpeed
{
    [JsonProperty("easy")]
    public float? Easy { get; set; }
    
    [JsonProperty("normal")]
    public float? Normal { get; set; }
    
    [JsonProperty("hard")]
    public float? Hard { get; set; }
    
    [JsonProperty("erect")]
    public float? Erect { get; set; }
    
    [JsonProperty("nightmare")]
    public float? Nightmare { get; set; }
    
    [JsonProperty("default")]
    public float? Default { get; set; }
    
    [JsonExtensionData]
    public Dictionary<string, JToken> ExtraSpeeds { get; set; }
    
    public float GetSpeed(string difficulty)
    {
        float? result = difficulty?.ToLower() switch
        {
            "easy" => Easy,
            "normal" => Normal,
            "hard" => Hard,
            "erect" => Erect,
            "nightmare" => Nightmare,
            _ => null
        };
        if (result.HasValue) return result.Value;
        
        if (ExtraSpeeds != null && difficulty != null 
            && ExtraSpeeds.TryGetValue(difficulty, out var token))
        {
            try { return token.ToObject<float>(); }
            catch { }
        }
        return Default ?? 1.5f;
    }
}

public class NotesByDifficulty
{
    [JsonProperty("easy")]
    public List<NewNote> Easy { get; set; }
    
    [JsonProperty("normal")]
    public List<NewNote> Normal { get; set; }
    
    [JsonProperty("hard")]
    public List<NewNote> Hard { get; set; }
    
    [JsonProperty("erect")]
    public List<NewNote> Erect { get; set; }
    
    [JsonProperty("nightmare")]
    public List<NewNote> Nightmare { get; set; }
    
    /// <summary>
    /// Captures any difficulty names not covered by the fixed properties above
    /// (e.g. custom mod difficulties like "funkin", "pico", etc.)
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JToken> ExtraDifficulties { get; set; }
    
    /// <summary>
    /// Get notes for a difficulty by name, checking both fixed properties and extras.
    /// </summary>
    public List<NewNote> GetByName(string difficulty)
    {
        var result = difficulty?.ToLower() switch
        {
            "easy" => Easy,
            "normal" => Normal,
            "hard" => Hard,
            "erect" => Erect,
            "nightmare" => Nightmare,
            _ => null
        };
        if (result != null) return result;
        
        // Check extension data for custom difficulties
        if (ExtraDifficulties != null && difficulty != null 
            && ExtraDifficulties.TryGetValue(difficulty, out var token))
        {
            try { return token.ToObject<List<NewNote>>(); }
            catch { }
        }
        return null;
    }
    
    /// <summary>
    /// Get the first non-null, non-empty note list from any difficulty.
    /// </summary>
    public List<NewNote> GetAny()
    {
        return Erect ?? Nightmare ?? Normal ?? Easy ?? Hard ?? GetFirstExtra();
    }
    
    private List<NewNote> GetFirstExtra()
    {
        if (ExtraDifficulties == null) return null;
        foreach (var kvp in ExtraDifficulties)
        {
            try
            {
                var list = kvp.Value.ToObject<List<NewNote>>();
                if (list != null && list.Count > 0) return list;
            }
            catch { }
        }
        return null;
    }
}

public class NewNote
{
    [JsonProperty("d")]
    public int D { get; set; } // Direction: 0-3 = opponent (strumline 0), 4-7 = player (strumline 1)
    
    [JsonProperty("t")]
    public double T { get; set; } // Time in milliseconds
    
    [JsonProperty("l")]
    public double? L { get; set; } // Length (for sustain notes)
    
    [JsonProperty("k")]
    public string K { get; set; } // Note kind (e.g., "mom" for alt animations)
}

public class ChartEvent
{
    [JsonProperty("e")]
    public string E { get; set; } // Event name
    
    [JsonProperty("t")]
    public double T { get; set; } // Time
    
    [JsonProperty("v")]
    public object V { get; set; } // Value
}

public class ChartMeta
{
    [JsonProperty("songName")]
    public string SongName { get; set; }
    
    [JsonProperty("artist")]
    public string Artist { get; set; }
    
    [JsonProperty("charter")]
    public string Charter { get; set; }
    
    [JsonProperty("playData")]
    public PlayData PlayData { get; set; }
    
    [JsonProperty("timeChanges")]
    public List<TimeChange> TimeChanges { get; set; }
    
    [JsonProperty("album")]
    public string Album { get; set; }
}

public class PlayData
{
    [JsonProperty("stage")]
    public string Stage { get; set; }
    
    [JsonProperty("noteStyle")]
    public string NoteStyle { get; set; }
    
    [JsonProperty("difficulties")]
    public List<string> Difficulties { get; set; }
    
    [JsonProperty("characters")]
    public Characters Characters { get; set; }
}

public class Characters
{
    [JsonProperty("player")]
    public string Player { get; set; }
    
    [JsonProperty("girlfriend")]
    public string Girlfriend { get; set; }
    
    [JsonProperty("opponent")]
    public string Opponent { get; set; }
}

public class TimeChange
{
    [JsonProperty("t")]
    public double T { get; set; }
    
    [JsonProperty("bpm")]
    public float Bpm { get; set; }
}

/// <summary>
/// Parsed game event (from chart events array).
/// Common events: "FocusCamera", "ZoomCamera", "SetCameraBop", "PlayAnimation"
/// </summary>
public class GameEvent
{
    public double Time { get; set; }
    public string Name { get; set; }
    public string Event => Name;
    public object Value { get; set; }
    public bool Fired { get; set; }
    /// <summary>Generated by the port (FocusCamera from mustHitSection), not a Psych chart event: hidden from Psych scripts.</summary>
    public bool PortSynthetic { get; set; }
}
