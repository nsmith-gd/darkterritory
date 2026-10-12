using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// Note 619: a bot's hand on a car's lamp key, to put the lamp out (the Fire Flies', App. A.5; a burning car's, note 188).
/// The key toggles the lamp, and what a client sees of it is a snapshot behind. Pressed every other tick by the two walkers
/// in car 1 at frontier:12's first stop (seed 1), the lamp went out and the next press in flight lit it again, and the Fire
/// Flies came back to it every 30 to 40 s. So a hand presses the moment it's there (the flies may be seconds from setting the
/// car alight), then leaves the key alone long enough to see its press come back. And a crewmate in the car before it with a
/// lower id is let press first, for a while (they may be a person, and not pressing).
/// </summary>
public sealed class LampKey
{
    uint? _pressed;
    uint _wanted, _asked;
    bool _wanting;

    /// <summary>Ticks after a press before another: the press to the host and its snapshot back, with room.</summary>
    public const uint Settle = SimConstants.TickRate / 2;
    /// <summary>Ticks a hand leaves the key to a crewmate in the car with a lower id before pressing it anyway.</summary>
    public const uint Defer = SimConstants.TickRate;

    /// <summary>
    /// Whether to press the key this tick, wanting the lamp of the car <paramref name="self"/> is in out, it lit as this client
    /// sees it; <paramref name="crew"/> the rest of the crew as it sees them. Called every tick it wants it out.
    /// </summary>
    public bool Press(in PlayerState self, World world, int me, IReadOnlyList<(int Id, PlayerState State)> crew, uint tick)
    {
        // Asked every tick while it's wanted: a gap is a new want.
        if (!_wanting || tick < _asked || tick - _asked > 2)
            _wanted = tick;
        (_wanting, _asked) = (true, tick);
        uint since = _wanted;
        if (_pressed is { } at && tick >= at && tick - at < Settle)
            return false;
        int car = self.Parent;
        var train = world.Train;
        if (tick - since < Defer)
            foreach (var (id, c) in crew)
                if (id < me && c.Alive && c.Parent == car && PlayerMotor.Indoors(c, train))
                    return false;
        _pressed = tick;
        return true;
    }
}
