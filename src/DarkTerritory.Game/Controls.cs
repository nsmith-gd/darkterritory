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
    // T91: cutting a coupling is its own key, held while looking down at the coupler.
    Uncouple,
    // T94: onto the nearest ladder.
    Ladder,
    // GDD v1.4 App. D.10, D.12: dead, a bookmark of the view you're following.
    Bookmark,
    // Note 267 (the director's notes on build 1121): the blow-off, held, from anywhere in the cab.
    Vent,
    // The director's decision of 2026-10-06: the supplies aboard, one panel toggled on.
    Supplies,
    // Note 298 (GDD §9's yard: "dance"): held, the emote wheel; let go, the one the mouse is on.
    Emote,
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
        [Control.RegulatorClose] = "Y",
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
        [Control.Uncouple] = "Z",
        [Control.Ladder] = "F",
        [Control.Bookmark] = "P",
        [Control.Vent] = "LeftCtrl",
        [Control.Supplies] = "I",
        [Control.Emote] = "J",
    };

    /// <summary>Keys nothing can be bound to: the menus' own.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "Escape", "Enter", "Up", "Down", "Left", "Right", "F1", "F2", "F3", "F4" };

    public static string Label(Control c) => c switch
    {
        // T97: steam drives the train; the regulator keys are the brake's release (and a throttle only a boiler-less test train has).
        Control.RegulatorOpen => "RELEASE BRAKE (GO)",
        Control.RegulatorClose => "REGULATOR CLOSE (NO BOILER)",
        Control.Roster => "CREW ROSTER",
        Control.RouteCard => "ROUTE CARD",
        Control.Chase => "CHASE VIEW",
        Control.Talk => "TALK (PUSH TO TALK)",
        Control.Swing => "SWING TOOL",
        Control.Whistle => "WHISTLE CORD",
        Control.CarLamp => "CAR LAMP",
        Control.Uncouple => "UNCOUPLE (HOLD, LOOKING DOWN)",
        Control.Ladder => "GRAB LADDER",
        Control.Bookmark => "BOOKMARK (DEAD)",
        Control.Vent => "VENT STEAM (HOLD, IN THE CAB)",
        Control.Supplies => "SUPPLIES ABOARD (TOGGLE)",
        Control.Emote => "EMOTE (HOLD, THE MOUSE PICKS)",
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
