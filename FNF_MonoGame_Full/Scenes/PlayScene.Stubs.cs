using Microsoft.Xna.Framework;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes
{
    public partial class PlayScene
    {
        public PsychCamera CamGame { get; set; } = PsychCamera.GameCam;
        public PsychCamera CamHUD { get; set; } = PsychCamera.HudCam;
        public PsychCamera CamOther { get; set; } = PsychCamera.OtherCam;
        public Character Dad => Opponent;
        public Character Gf => Girlfriend;
        public SpriteGroup BoyfriendGroup { get; set; }
        public SpriteGroup DadGroup { get; set; }
        public SpriteGroup GfGroup { get; set; }
        public bool PracticeMode { get; set; }
        public float Health { get => _health; set => _health = value; }
        public bool CamZooming { get => _psychCamZooming; set => _psychCamZooming = value; }
        public int CurSection { get; set; }
        public System.Collections.Generic.List<FNF_MonoGame.Engine.PsychEngine.Note> UnspawnNotes { get; set; } = new();
        public SpriteGroup Stage { get; set; } = new SpriteGroup();
        public static float daPixelZoom { get; set; } = 6f;
        public float PlaybackRate { get; set; }
    }
}
