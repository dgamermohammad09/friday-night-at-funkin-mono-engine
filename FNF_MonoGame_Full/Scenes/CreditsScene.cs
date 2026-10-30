using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Credits scene � displays the Funkin' Crew and contributors.
/// Matches original CreditsState.hx layout.
/// </summary>
public class CreditsScene : Scene
{
    private const int ScreenPad = 24;
    private const float ScrollBaseSpeed = 100f;
    private const float ScrollFastSpeed = ScrollBaseSpeed * 4f;

    private readonly List<CreditEntry> _entries = new();
    private readonly List<CreditLine> _lines = new();
    private int _nextEntryIndex;
    private int _nextLineIndex;
    private bool _buildingHeader;
    private float _creditsGroupY;
    private float _creditsLineY;
    private float _bgScrollX;
    private Texture2D _bgTex;

    private struct CreditEntry
    {
        public string Header;
        public List<string> Lines;
    }

    private struct CreditLine
    {
        public string Text;
        public bool IsHeader;
        public float Y;
    }

    public override void Load()
    {
        LoadCreditsData();
        _creditsGroupY = FNFGame.SCREEN_HEIGHT;
        _creditsLineY = 0f;
        _nextEntryIndex = 0;
        _nextLineIndex = 0;
        _buildingHeader = true;
        _lines.Clear();

        _bgTex = Assets.LoadTexture("menus/main_menu/background.png");

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freeplayRandom", true);
    }

    public override void Unload() { }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _bgScrollX -= dt * 30f;

        BuildNextLine();
        KillOffScreenLines();

        float scrollSpeed = ScrollBaseSpeed;
        if (Input.ConfirmHeld)
            scrollSpeed = ScrollFastSpeed;
        if (Input.PauseHeld)
            scrollSpeed = 0f;

        _creditsGroupY -= scrollSpeed * dt;

        if (Input.BackPressed || HasEnded())
        {
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new MainMenuScene());
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Background (original: menuBG same as main menu)
        if (_bgTex != null && _bgTex != Assets.Pixel)
        {
            spriteBatch.Draw(_bgTex,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.White);
            spriteBatch.Draw(Assets.Pixel,
                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),
                Color.Black * 0.4f);
        }
        else
        {
            DrawCheckerboard(spriteBatch);
        }

        DrawCreditsLines(spriteBatch);

        spriteBatch.End();
    }

    private void LoadCreditsData()
    {
        _entries.Clear();
        var data = Assets.LoadJson<CreditsData>("data/credits.json");
        if (data?.Entries == null || data.Entries.Count == 0)
        {
            data = CreditsData.CreateFallback();
        }

        foreach (var entry in data.Entries)
        {
            _entries.Add(new CreditEntry
            {
                Header = entry.Header,
                Lines = entry.Body?.Select(item => item?.Line ?? string.Empty).ToList() ?? new List<string>()
            });
        }

        AddPsychModCredits();

        _entries.Add(new CreditEntry
        {
            Header = "MONOGAME PORT",
            Lines = new List<string> { "mohammad aljafari" }
        });
    }

    /// <summary>Psych CreditsState.pushModCreditsToList: every enabled mod's data/credits.txt (lines 'name::icon::desc::link::color',
    /// a line without '::' separators is a section title). Shown in the original game's scrolling credits style:
    /// sections become headers, people become "name - description" lines.</summary>
    private void AddPsychModCredits()
    {
        try
        {
            foreach (var mod in FNF_MonoGame.Engine.PsychEngine.PsychMods.GetAllMods())
            {
                if (!mod.Enabled) continue;
                string dir = FNF_MonoGame.Engine.PsychEngine.PsychMods.GetModDirectory(mod.Name);
                string f = dir == null ? null : System.IO.Path.Combine(dir, "data", "credits.txt");
                if (f == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(f)) continue;
                List<string> cur = null;
                foreach (var raw in global::FNF_MonoGame.Engine.FsCI.ReadAllLines(f))
                {
                    var parts = raw.Split(new[] { "::" }, StringSplitOptions.None);
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (parts.Length < 2)
                    {
                        cur = new List<string>();
                        _entries.Add(new CreditEntry { Header = parts[0].Trim().ToUpperInvariant(), Lines = cur });
                        continue;
                    }
                    if (cur == null) { cur = new List<string>(); _entries.Add(new CreditEntry { Header = mod.Name.ToUpperInvariant(), Lines = cur }); }
                    string name = parts[0].Trim(), desc = parts.Length > 2 ? parts[2].Trim() : "";
                    cur.Add(desc.Length > 0 ? name + " - " + desc : name);
                }
                Console.WriteLine("[CREDITS] mod credits from " + mod.Name + ": " + _entries.Count + " entries total");
            }
        }
        catch (Exception ex) { Console.WriteLine("[CREDITS] mod credits: " + ex.Message); }
    }

    private void BuildNextLine()
    {
        if (_nextEntryIndex >= _entries.Count)
        {
            return;
        }

        if (_creditsGroupY + _creditsLineY >= FNFGame.SCREEN_HEIGHT)
        {
            return;
        }

        var entry = _entries[_nextEntryIndex];
        if (_buildingHeader && !string.IsNullOrWhiteSpace(entry.Header))
        {
            _lines.Add(new CreditLine { Text = entry.Header, IsHeader = true, Y = _creditsLineY });
            _creditsLineY += 32f + 32f;
            _buildingHeader = false;
            return;
        }

        if (entry.Lines != null && _nextLineIndex < entry.Lines.Count)
        {
            var line = entry.Lines[_nextLineIndex];
            _lines.Add(new CreditLine { Text = line, IsHeader = false, Y = _creditsLineY });
            _creditsLineY += 24f;
            _nextLineIndex++;
            return;
        }

        _nextEntryIndex++;
        _nextLineIndex = 0;
        _buildingHeader = true;
        _creditsLineY += 24f * 2.5f;
    }

    private void KillOffScreenLines()
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Y + _creditsGroupY + 40f <= 0f)
            {
                _lines.RemoveAt(i);
            }
        }
    }

    private bool HasEnded()
    {
        return _nextEntryIndex >= _entries.Count && _lines.Count == 0;
    }

    private void DrawCreditsLines(SpriteBatch spriteBatch)
    {
        float baseX = Math.Max(ScreenPad, 0f);
        float width = FNFGame.SCREEN_WIDTH - ScreenPad * 2;
        int textX = (int)baseX;
        var font = Assets.GetFont(24);
        var headerFont = Assets.GetFont(32);

        foreach (var line in _lines)
        {
            float y = _creditsGroupY + line.Y;
            if (y < -60 || y > FNFGame.SCREEN_HEIGHT + 60)
            {
                continue;
            }

            var activeFont = line.IsHeader ? headerFont : font;
            if (activeFont == null)
            {
                continue;
            }

            var text = line.Text ?? string.Empty;
            activeFont.DrawText(spriteBatch, text, new Vector2(textX, y), Color.White);
        }
    }

    private void DrawCheckerboard(SpriteBatch sb)
    {
        int tileSize = 80;
        int off = ((int)_bgScrollX % (tileSize * 2) + tileSize * 2) % (tileSize * 2);
        for (int y = -tileSize; y < FNFGame.SCREEN_HEIGHT + tileSize; y += tileSize)
        {
            for (int x = -tileSize * 2 + off; x < FNFGame.SCREEN_WIDTH + tileSize; x += tileSize)
            {
                int row = ((y + tileSize) / tileSize);
                int col = ((x + tileSize * 2 - off) / tileSize);
                Color c = ((row + col) % 2 == 0) ? new Color(30, 20, 50) : new Color(45, 30, 70);
                sb.Draw(Assets.Pixel, new Rectangle(x, y, tileSize, tileSize), c);
            }
        }
    }
}
