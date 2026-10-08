namespace DarkTerritory.Game;

/// <summary>
/// HOLD KEYS (note 383; GDD §32 "Accessibility"): with TOGGLE, a press of run, the brake, talk, the radio or the crew roster
/// latches it on and another lets it go, for a player who can't keep a key down. Client-side only: what the host is sent is
/// the same held button either way, so prediction and the sim don't know the difference.
/// </summary>
public sealed class HoldLatch
{
    readonly HashSet<Control> _on = [];

    /// <summary>HOLD KEYS is TOGGLE (<see cref="Settings.ToggleHolds"/>). Turned off, everything latched lets go.</summary>
    public bool Toggles
    {
        get;
        set
        {
            if (!value)
                _on.Clear();
            field = value;
        }
    }

    /// <summary>Whether a control is one HOLD KEYS changes.</summary>
    public static bool Latches(Control c) => Array.IndexOf(Settings.Toggleable, c) >= 0;

    /// <summary>A control's key pressed this frame (once a frame, before <see cref="Held"/>): on if it was off, off if on.</summary>
    public void Press(Control c)
    {
        if (Toggles && Latches(c) && !_on.Remove(c))
            _on.Add(c);
    }

    /// <summary>Whether a control is held: latched with TOGGLE, its key's down without.</summary>
    public bool Held(Control c, bool down) => Toggles && Latches(c) ? _on.Contains(c) : down;

    /// <summary>Everything lets go (a new night).</summary>
    public void Clear() => _on.Clear();
}
