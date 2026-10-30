#if ANDROID || TOUCH_TEST
namespace FNF_MonoGame.Scenes;

// Android-only accessor for the touch UI layer (Platforms/Android/TouchControls.cs): game over uses the menu pad
// (accept = retry, back = exit) instead of the note hitbox.
public partial class PlayScene
{
    public bool TouchGameOver => _gameOver;
}
#endif
