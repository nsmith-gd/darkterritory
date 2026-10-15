using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

public static partial class Hud
{
    // Note 740's roll: the horn last heard (the tick its blast began) and who's called back since it, by the tick they did.
    [ThreadStatic] static uint _hornAt, _lastWhistle;
    [ThreadStatic] static bool _horned;
    [ThreadStatic] static Dictionary<int, uint>? _calledBack;
    [ThreadStatic] static World? _rollWorld; // a new night (or a rejoin) begins with no roll

    /// <summary>
    /// Note 740 (the director, 14 Oct: "We need some way to signal to the driver that everybody is on the train ... with
    /// bots, we need some sort of way of telling people"; GDD §17): "Aboard!" is called, not shown. Each crewmate's call,
    /// with their name, for as long as their hand's up; and in the cab, after a horn at a stand, the roll of who's called
    /// back, and ALL ABOARD once every living crewmate has. Nobody's counted who hasn't called. Good ink for the last.
    /// </summary>
    public static List<(string Text, bool Good)> AboardLines(World world, int me, bool inCab, IReadOnlyList<RosterLine> roster)
    {
        _calledBack ??= [];
        if (!ReferenceEquals(_rollWorld, world))
        {
            _rollWorld = world;
            _horned = false;
            _calledBack.Clear();
        }
        var lines = new List<(string, bool)>();
        bool standing = world.Train.Dynamics.Speed < CabControls.StandingBelow;
        // A horn at a stand begins a roll: a fresh blast (not the cord held on), by a hand on the cord.
        if (world.WhistleSeconds > 0 && world.WhistleBy >= 0)
        {
            if (standing && (!_horned || world.Tick - _lastWhistle > SimConstants.TickRate * 2))
            {
                _horned = true;
                _hornAt = world.Tick;
                _calledBack.Clear();
            }
            _lastWhistle = world.Tick;
        }
        if (!standing)
            _horned = false;
        foreach (var e in world.Emotes)
        {
            if (e.Kind != Emote.Aboard)
                continue;
            if (_horned && e.Tick >= _hornAt)
                _calledBack[e.By] = e.Tick;
            if (e.By != me)
                lines.Add(($"{IncidentLog.NameOf(world, e.By).ToUpperInvariant()}: ABOARD!", false));
        }
        double since = (world.Tick - _hornAt) * SimConstants.TickSeconds;
        if (inCab && _horned && since <= Tuning.AboardRollSeconds)
        {
            var others = roster.Where(l => l.Alive && !l.You && l.Id != me).ToList();
            var called = others.Where(l => _calledBack.ContainsKey(l.Id)).ToList();
            if (others.Count > 0 && called.Count == others.Count)
                lines.Add(("ALL ABOARD", true));
            else if (others.Count > 0)
                lines.Add(($"ABOARD: {(called.Count == 0 ? "NOBODY YET" : string.Join(", ", called.Select(l => l.Name.ToUpperInvariant())))} ({called.Count}/{others.Count})", false));
        }
        return lines;
    }
}
