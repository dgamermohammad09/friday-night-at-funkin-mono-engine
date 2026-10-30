using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// FreeplayState is a FlxSubState opened over MainMenuState (MainMenuState freeplay item / FreeplayState.build()).
/// The parent menu stays drawn behind Freeplay (persistentDraw = true) until onDJIntroDone sets persistentDraw = false,
/// and goBack() sets persistentDraw = true again so the menu shows through while the exit movers slide everything
/// away; when the longest mover ends, close() returns to the very same menu instantly (no transition, no loading).
/// </summary>
public partial class FreeplayScene
{
    /// <summary>The MainMenuScene this Freeplay was opened from (kept loaded); created on demand otherwise.</summary>
    public MainMenuScene ParentMenu { get; set; }
    private bool _parentMenuLoaded;

    private void EnsureParentMenu()
    {
        if (ParentMenu == null)
        {
            // FreeplayState.build(): a fresh MainMenuState is the parent (results / character select paths)
            ParentMenu = new MainMenuScene();
            ParentMenu.Load();
            ParentMenu.SelectItemSilently(1);   // freeplay entry
        }
        _parentMenuLoaded = true;
    }

    /// <summary>persistentDraw of the parent: true until the DJ intro is done, and again while exiting.</summary>
    private bool ParentPersistentDraw => ParentMenu != null && (_exiting || !_introDone);

    /// <summary>Draws the parent menu (no active batch). Returns true when drawn.</summary>
    private bool DrawParentMenu(SpriteBatch sb)
    {
        if (!ParentPersistentDraw) return false;
        ParentMenu.DrawnAsParent = true;
        ParentMenu.Draw(sb);
        ParentMenu.DrawnAsParent = false;
        return true;
    }

    private void ReturnToParentMenu()
    {
        EnsureParentMenu();
        var menu = ParentMenu;
        menu.OnSubStateClosed();
        Game.Scenes.SwitchSceneInstant(menu, load: false, unloadOld: true);   // parent is already loaded
    }
}
