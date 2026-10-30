namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine AttachedText — text that follows a sprite.
/// Original: objects/AttachedText.hx
/// </summary>
public class AttachedText : AttachedSprite
{
    public string Text { get; set; }
    public int ID { get; set; }

    public AttachedText(string text = null, float xAdd = 0, float yAdd = 0, float size = 0, bool bold = false)
        : base(null, null, null, false)
    {
        Text = text;
        XAdd = xAdd;
        YAdd = yAdd;
    }
}
