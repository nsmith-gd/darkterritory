namespace DarkTerritory.Game;

/// <summary>
/// What a keyboard-and-mouse player can bind (T80, roadmap M6 "settings"). The menus keep the arrows, Enter and Escape
/// whatever's bound, and the designer's keys (F1-F3, the prototype's respawns) aren't controls.
/// </summary>
public enum Control
{
    Forward, Back, Left, Right, Run, Jump, Use, Fire, Throw,
    RegulatorOpen, RegulatorClose, Brake, Reverser, Lamp,
    Talk, Radio, Roster, RouteCard, Chase,
    // GDD v1.1: swing the tool you carry (App. C.2), the whistle cord (§12), the lamp in the car you're in (App. A.5).
    Swing, Whistle, CarLamp,
    // GDD App. D, while dead or lobbied: let the next in the queue go first (D.6), bookmark the moment (D.12). The rest of
    // the dead phase is on the living's keys: Fire and Throw cycle whom you watch, Use calls out, Radio is the Live Mic.
    LetNextGo, Bookmark,
}

/// <summary>The default key for each control, by the key's name (Ballast.Platform's <c>Key</c>), and how the menu says it.</summary>
public static class Controls
{
    public static readonly IReadOnlyDictionary<Control, string> Defaults = new Dictionary<Control, string>
    {
        [Control.Forward] = "W",
        [Control.Back] = "S",
        [Control.Left] = "A",
        [Control.Right] = "D",
        [Control.Run] = "LeftShift",
        [Control.Jump] = "Space",
        [Control.Use] = "E",
        [Control.Fire] = "MouseLeft",
        [Control.Throw] = "MouseRight",
        [Control.RegulatorOpen] = "R",
        [Control.RegulatorClose] = "F",
        [Control.Brake] = "B",
        [Control.Reverser] = "X",
        [Control.Lamp] = "L",
        [Control.Talk] = "V",
        [Control.Radio] = "T",
        [Control.Roster] = "Q",
        [Control.RouteCard] = "C",
        [Control.Chase] = "Tab",
        [Control.Swing] = "G",
        [Control.Whistle] = "H",
        [Control.CarLamp] = "K",
        [Control.LetNextGo] = "N",
        [Control.Bookmark] = "P",
    };

    /// <summary>Keys nothing can be bound to: the menus' own.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "Escape", "Enter", "Up", "Down", "Left", "Right", "F1", "F2", "F3" };

    public static string Label(Control c) => c switch
    {
        Control.RegulatorOpen => "REGULATOR OPEN",
        Control.RegulatorClose => "REGULATOR CLOSE",
        Control.Roster => "CREW ROSTER",
        Control.RouteCard => "ROUTE CARD",
        Control.Chase => "CHASE VIEW",
        Control.Talk => "TALK (PUSH TO TALK)",
        Control.Swing => "SWING TOOL",
        Control.Whistle => "WHISTLE CORD",
        Control.CarLamp => "CAR LAMP",
        Control.LetNextGo => "DEAD: LET THE NEXT GO FIRST",
        Control.Bookmark => "DEAD: BOOKMARK",
        _ => c.ToString().ToUpperInvariant(),
    };

    /// <summary>A key's name as the menu shows it.</summary>
    public static string KeyLabel(string key) => key switch
    {
        "MouseLeft" => "LEFT MOUSE",
        "MouseRight" => "RIGHT MOUSE",
        "MouseMiddle" => "MIDDLE MOUSE",
        "Mouse4" => "MOUSE 4",
        "Mouse5" => "MOUSE 5",
        "LeftShift" => "LEFT SHIFT",
        "RightShift" => "RIGHT SHIFT",
        "LeftCtrl" => "LEFT CTRL",
        "RightCtrl" => "RIGHT CTRL",
        "LeftAlt" => "LEFT ALT",
        "RightAlt" => "RIGHT ALT",
        "CapsLock" => "CAPS LOCK",
        ['D', >= '0' and <= '9'] => key[1..],
        _ => key.ToUpperInvariant(),
    };
}
