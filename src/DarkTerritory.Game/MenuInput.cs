using System.Numerics;

namespace DarkTerritory.Game;

/// <summary>The front end's keys this frame, each on its press (the app reads them off the keyboard; tests set them).</summary>
[Flags]
public enum MenuKey : byte { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Select = 16, Back = 32, Erase = 64 }

/// <summary>
/// The pointer over the front end this frame (note 264): where it is in the overlay's pixels (480x270), whether it moved,
/// the buttons pressed (on the press), and the wheel's notches (positive: up, away from the player).
/// </summary>
public readonly record struct MenuMouse(Vector2 At, bool Moved = false, bool Left = false, bool Right = false, float Wheel = 0);

/// <summary>
/// The front end's input from a keyboard and mouse (note 264, the director's notes on build 1121: "we really should be able
/// to click with a mouse"): the keys move and choose as they always did, and the mouse hovers, clicks and scrolls, against
/// what the last <see cref="FrontEnd.Draw"/> put where. The app feeds it from the window; tests feed it clicks, headless.
/// </summary>
public static class MenuInput
{
    /// <summary>Applies one frame's keys, typing and pointer; returns what was chosen, if anything.</summary>
    public static Launch? Apply(FrontEnd menu, MenuKey keys, string text = "", MenuMouse? mouse = null)
    {
        Launch? chosen = null;
        if (keys.HasFlag(MenuKey.Up)) menu.Up();
        if (keys.HasFlag(MenuKey.Down)) menu.Down();
        if (keys.HasFlag(MenuKey.Left)) menu.Left();
        if (keys.HasFlag(MenuKey.Right)) menu.Right();
        if (keys.HasFlag(MenuKey.Select)) chosen = menu.Select();
        if (keys.HasFlag(MenuKey.Back)) menu.Back();
        if (keys.HasFlag(MenuKey.Erase)) menu.Erase();
        if (text.Length > 0) menu.Type(text);
        if (mouse is { } m && menu.Capturing is null)
        {
            if (m.Wheel != 0)
                menu.Scroll((int)MathF.Round(m.Wheel));
            var hit = menu.HitTest(m.At.X, m.At.Y);
            if (m.Moved && hit is { } over)
                menu.Hover(over.Item);
            if (m.Left || m.Right)
                chosen ??= menu.Click(hit, right: m.Right && !m.Left);
        }
        return chosen;
    }

    /// <summary>
    /// The menus' keys from the keyboard (<paramref name="pressed"/> by the key's name): the arrows, Enter, Escape and
    /// Backspace always; WASD and Space too, but not while a field's being typed in, where they're letters.
    /// </summary>
    public static MenuKey Keys(Func<string, bool> pressed, bool typing)
    {
        var k = MenuKey.None;
        if (pressed("Up") || !typing && pressed("W")) k |= MenuKey.Up;
        if (pressed("Down") || !typing && pressed("S")) k |= MenuKey.Down;
        if (pressed("Left") || !typing && pressed("A")) k |= MenuKey.Left;
        if (pressed("Right") || !typing && pressed("D")) k |= MenuKey.Right;
        if (pressed("Enter") || !typing && pressed("Space")) k |= MenuKey.Select;
        if (pressed("Escape")) k |= MenuKey.Back;
        if (pressed("Backspace")) k |= MenuKey.Erase;
        return k;
    }
}
