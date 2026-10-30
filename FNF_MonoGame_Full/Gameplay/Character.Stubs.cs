using Microsoft.Xna.Framework;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Gameplay {
    public partial class Character : PsychCharacter {
        public Color Color { get => Tint; set => Tint = value; }
        public bool Danced { get; set; }
        public bool SpecialAnim { get; set; }
        public float X { get => Position.X; set => Position = new Vector2(value, Position.Y); }
        public float Y { get => Position.Y; set => Position = new Vector2(Position.X, value); }
        public float Height => _sprite?.GetFrameHeight() ?? 0f;
        public float Width => _sprite?.GetFrameWidth() ?? 0f;
        public string CurCharacter { get => OriginalName; set => OriginalName = value; }
        public bool HasAnimation(string name) => _sprite?.Sheet?.Animations?.ContainsKey(name) ?? false;
        public Vector2 GetGraphicMidpoint() => new Vector2(X + Width / 2f, Y + Height / 2f);
        public string GetAnimationName() => CurrentAnimation;
        // Psych Character fields read by Lua (getProperty('dad.cameraPosition[0]'), 'boyfriend.positionArray[1]')
        public float[] CameraPosition { get { if (JsonData == null) return new float[2]; return JsonData.CameraOffsets ??= new float[2]; } set { if (JsonData != null) JsonData.CameraOffsets = value; } }
        public float[] PositionArray { get => PsychCharacterOffset ??= new float[2]; set => PsychCharacterOffset = value; }
        public float SingDuration { get => JsonData?.SingTime ?? 4f; set { if (JsonData != null) JsonData.SingTime = value; } }
        // Psych/flixel "animation" controller view for scripts: dad.animation.curAnim.name / curFrame / finished
        private FNF_MonoGame.Scenes.PsychAnimFacade _psychAnim;
        public FNF_MonoGame.Scenes.PsychAnimFacade animation => _psychAnim ??= new FNF_MonoGame.Scenes.PsychAnimFacade(
            () => CurrentAnimation, () => _sprite?.FrameIndex ?? 0, () => _sprite?.Finished ?? true, (n, f) => PlayAnimation(n, f));
        private static int s_psychIdSeq;
        public int ID { get; set; } = s_psychIdSeq++; // FlxBasic.ID
        public void PlayAnim(string anim, bool force = false, bool reversed = false, int frame = 0) => PlayAnimation(anim, force);
    }
}
