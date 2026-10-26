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
    // Note 664 (the director's playtest, 13 Oct): crouch, held; and pushing the gun along, its own key (Use is sitting in it).
    Crouch, Push,
    // Note 717 (horseplay; the director, 14 Oct 2026: "For shoving, we can try adding it in"): shove the crewmate you look at.
    Shove,
    // Note 720 (GDD §12): with a hand lamp in the fist, held, the lamp signals' wheel; let go, the one the mouse is on.
    Signal,
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
        // Note 749 (the director, 14 Oct 2026: "Hold down right click to use the radio since communication is so important";
        // "swap input conflict there so right click is radio call. Throw is so rarely used"): Throw took the radio's old T.
        [Control.Throw] = "T",
        [Control.RegulatorOpen] = "R",
        [Control.RegulatorClose] = "Y",
        [Control.Brake] = "B",
        [Control.Reverser] = "X",
        [Control.Lamp] = "L",
        [Control.Talk] = "V",
        [Control.Radio] = "MouseRight",
        [Control.Roster] = "Q",
        [Control.RouteCard] = "C",
        [Control.Chase] = "Tab",
        [Control.Swing] = "G",
        [Control.Whistle] = "H",
        [Control.CarLamp] = "K",
        [Control.Uncouple] = "Z",
        [Control.Ladder] = "F",
        [Control.Bookmark] = "P",
        // Note 664: the genre's crouch key is LeftCtrl, and the vent (cab only, rare) went to its own key: one key doing two
        // things in two places is what the director flagged for E. Note 703 (the director, 13 Oct evening: "I don't like N
        // being the button ... it should be a different button that is more accessible than the WASD"): Caps Lock, by A
        // under the little finger. Note 844 (the director's playtest of 16 Oct, line 23: "remap venting to the left alt next to
        // the spacebar"): Left Alt, under the left thumb; the shove took Caps Lock.
        [Control.Vent] = "LeftAlt",
        [Control.Supplies] = "I",
        [Control.Emote] = "J",
        [Control.Crouch] = "LeftCtrl",
        // Note 703 (the director: "Push O to walk doesn't make any sense. It's not something that is near the mouse, and
        // it's not something that's near the left hand"): the middle mouse button, held while walking.
        [Control.Push] = "MouseMiddle",
        // Note 717: the shove, under the left thumb beside the jump (every key near the left hand being taken, note 703, and
        // the director wanting keys by the hands, not across the keyboard). Note 844: the vent took Left Alt, and the shove
        // its old Caps Lock. Note 898 (the director, 20 Oct: "We shouldn't default any input to caps lock, find a different key
        // easily accessible to left hand that isn't taken up already"): 5, the one key under the left hand nothing has, a
        // finger's reach past the hotbar's 1-4. Settings.Load moves a saved shove off Caps Lock.
        [Control.Shove] = "D5",
        // Note 720: beside the emote wheel's J, by the lamp-carrying hand's other keys (the car lamp's K).
        [Control.Signal] = "U",
    };

    /// <summary>
    /// What each control does, a line each for the controls map (README.md, the players' PLAYING.txt; note 898's
    /// <see cref="ControlsMap"/>), where <see cref="Label"/> is only the menu's name for it.
    /// </summary>
    public static string Help(Control c) => c switch
    {
        Control.Forward => "Walk forward",
        Control.Back => "Walk back",
        Control.Left => "Step left",
        Control.Right => "Step right",
        Control.Run => "Run (held)",
        Control.Jump => "Jump",
        Control.Use => "Use: press to pick up or put down; hold to work things (the shovel, a valve, a door, a coupler, a switch stand, a brake wheel), to carry a crewmate, to struggle free",
        Control.Fire => "Fire the gun you're at",
        Control.Throw => "Throw what you're holding (the coal off the shovel too)",
        Control.RegulatorOpen => "In the cab: release the brake and go",
        Control.RegulatorClose => "In the cab: close the regulator (a train with no boiler only)",
        Control.Brake => "In the cab: brake (held)",
        Control.Reverser => "In the cab: the reverser (stopped only)",
        Control.Lamp => "The engine's headlamp, on or off",
        Control.Talk => "Push to talk (with PUSH TO TALK on in Settings; otherwise the mic is open)",
        Control.Radio => "The radio, held, with one on your belt: the whole train hears",
        Control.Roster => "The crew roster (held); with a number, mute that crewmate (Shift and a number: put them off, or vote to)",
        Control.RouteCard => "The route card, a page a press, then away",
        Control.Chase => "The chase view; dead, the next of the dead's views",
        Control.Swing => "Swing the tool you carry",
        Control.Whistle => "The whistle cord in the cab; the communication cord in a car",
        Control.CarLamp => "The lamp in the car you're in",
        Control.Uncouple => "Cut a coupling (held, looking down at the coupler)",
        Control.Ladder => "Grab the nearest ladder",
        Control.Bookmark => "Dead: bookmark the view you're following",
        Control.Vent => "Vent the boiler's steam (held, in the cab)",
        Control.Supplies => "What's aboard (toggle)",
        Control.Emote => "The emote wheel (held; the mouse picks)",
        Control.Crouch => "Crouch (held, or a toggle with HOLD KEYS on TOGGLE)",
        Control.Push => "Push the gun along its rail (held, walking)",
        Control.Shove => "Shove the crewmate in front of you",
        Control.Signal => "With a hand lamp: the lamp signals' wheel (held; the mouse picks)",
        _ => Label(c),
    };

    /// <summary>
    /// The keys that aren't controls and can't be rebound (the app's own, <c>Program.cs</c>), for the controls map: the key as
    /// the map says it, and what it does. A developer build's own keys (F3, F6-F10) aren't a player's and aren't here.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Does)> Fixed =
    [
        ("1-4 · Mouse wheel", "The hotbar's slots: pick one · step through them"),
        ("1-9 · Enter", "Dead, with a ballot: pick a creature · cast it (the same number again casts it too)"),
        ("Space", "Dead: your live mic to the living, on or off; at the run's end, give the commendation the arrows picked"),
        ("F1", "The HUD, off or on; dead and watching, every word and the UI off for the picture alone"),
        ("F2", "Invite Steam friends"),
        ("F4", "Hide the first nights' card"),
        ("F5", "Reconnect, when the link to the host is gone"),
        ("Esc", "The night's menu (settings, leave the night); in a menu, back"),
        ("Alt+Enter", "Windowed or full screen"),
    ];

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
        Control.Crouch => "CROUCH (HOLD)",
        Control.Push => "PUSH THE GUN ALONG (HOLD, WALKING)",
        Control.Shove => "SHOVE A CREWMATE",
        Control.Signal => "LAMP SIGNAL (HOLD, THE MOUSE PICKS)",
        _ => c.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// A key's name where there's only a slot's corner for it (the hotbar's radio, note 749): the mouse buttons short, as the
    /// HUD's prompts write them; anything else as the menu says it.
    /// </summary>
    public static string ShortKeyLabel(string key) => key switch
    {
        "MouseLeft" => "LMB",
        "MouseRight" => "RMB",
        "MouseMiddle" => "MMB",
        "Mouse4" => "M4",
        "Mouse5" => "M5",
        _ => KeyLabel(key),
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
