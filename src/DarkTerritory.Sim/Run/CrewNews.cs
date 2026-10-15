using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// Note 750 (GDD open question 2, App. D.6; the social collision audit's R1): a crewmate held or killed out of voice reach is a
/// line for the living. Mirror of player.json <c>crewNews</c>; field docs live there. Off by default: nothing shown.
/// </summary>
public sealed record CrewNewsTuning
{
    public bool On { get; init; }
    public double BeyondMetres { get; init; } = Net.VoiceRouting.ProximityCutoff;
    public double DeathSeconds { get; init; } = 4;
}

/// <summary>One crewmate's emergency as a living crewmate's HUD says it: who and where, never what.</summary>
/// <param name="Victim">Whom it's about.</param>
/// <param name="Text">"PRIYA - CAR 4 ROOF", "PRIYA IS GONE - CAR 4" (a hyphen: the HUD font has no longer dash, and its middle dot reads as a full stop).</param>
/// <param name="Dead">A death's line (a few seconds), not a hold's (as long as it lasts).</param>
/// <param name="Since">The tick it began: the hold's first, or the death's.</param>
public readonly record struct CrewNewsLine(int Victim, string Text, bool Dead, long Since);

/// <summary>
/// Note 750 (the director, 14 Oct 2026, on the audit's R1: "looks good"): roll call stays verbal, but an emergency travels
/// the train. Worked out by each machine for its own player from what the snapshots already carry (where everyone is, who's
/// held, who's alive), so nothing new goes on the wire. Within voice reach nothing's shown: you hear it. The dead are shown
/// nothing (the director cut haunting). One line at a time, the newest.
/// </summary>
public sealed class CrewNews
{
    readonly Dictionary<int, bool> _alive = [];
    readonly Dictionary<int, long> _heldSince = [];
    readonly Dictionary<int, CrewNewsLine?> _gone = [];

    /// <summary>
    /// Where a crewmate is, as the line says it: "CAR 4 ROOF", "INSIDE CAR 4", "CAR 4 GAP" (the gap behind car 4), "THE CAB",
    /// "THE ENGINE", "CAR 4", and off the train "AT THE STOP" (<paramref name="stop"/>) or "OFF THE TRAIN, 40 M BACK".
    /// Cars by their number on the train, as the news names them (<see cref="News.Cars"/>).
    /// </summary>
    public static string Place(TrainOnLine train, in PlayerState s, bool stop = false)
    {
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (int gap in CrewSense.Gaps(train))
            if (CrewSense.InGap(at, train, gap))
                return $"CAR {gap} GAP";
        int car = s.Parent;
        if (car == PlayerState.World || car >= train.Frames.Count)
        {
            if (stop)
                return "AT THE STOP";
            double hint = s.LineHint;
            double along = train.Line.Nearest(at, ref hint).Distance;
            double back = train.RearDistance - along, ahead = along - train.Dynamics.Distance;
            return back > 0 ? $"OFF THE TRAIN, {Metres(back)} M BACK"
                : ahead > 0 ? $"OFF THE TRAIN, {Metres(ahead)} M AHEAD"
                : "BESIDE THE TRAIN";
        }
        if (car == 0)
            return PlayerMotor.InCab(s, train) ? "THE CAB" : "THE ENGINE";
        var shape = train.Frames[car].Shape;
        if (shape.Interior is { } room && room.Contains(s.Position))
            return $"INSIDE CAR {car}";
        return s.Position.Y >= shape.RoofHeight - 0.2 ? $"CAR {car} ROOF" : $"CAR {car}";
    }

    /// <summary>Rounded as a crewmate would call it: to 10 m.</summary>
    static int Metres(double m) => Math.Max(10, (int)Math.Round(m / 10) * 10);

    /// <summary>
    /// Brought up to <paramref name="tick"/>: the line <paramref name="me"/> (<paramref name="mine"/>) is shown, or null. Called
    /// as often as the HUD is drawn; a death is caught the first time a crewmate is seen dead after being seen alive.
    /// </summary>
    /// <param name="crew">Everyone's state as this machine has it, <paramref name="me"/> among them or not.</param>
    /// <param name="name">A crewmate's name.</param>
    /// <param name="stop">Off the train is "at the stop" (stood at a facility).</param>
    public CrewNewsLine? Update(CrewNewsTuning t, TrainOnLine train, int me, in PlayerState mine, long tick,
        IReadOnlyList<(int Id, PlayerState State)> crew, Func<int, string> name, bool stop = false)
    {
        var here = PlayerMotor.WorldPosition(mine, train);
        bool Far(in PlayerState s) => (PlayerMotor.WorldPosition(s, train) - here).Length > t.BeyondMetres;
        foreach (var (id, s) in crew)
        {
            if (id == me)
                continue;
            bool was = _alive.TryGetValue(id, out bool alive) && alive;
            _alive[id] = s.Alive;
            if (was && !s.Alive)
                // Out of reach as they went: the line, for its few seconds. Within it, heard (and the cut heard, App. D.2).
                _gone[id] = Far(s) ? new CrewNewsLine(id, $"{name(id).ToUpperInvariant()} IS GONE - {Place(train, s, stop)}", true, tick) : null;
            if (s.Alive && _gone.ContainsKey(id))
                _gone.Remove(id);
            if (s.Alive && s.Has(PlayerFlags.Held))
                _heldSince.TryAdd(id, tick);
            else
                _heldSince.Remove(id);
        }
        // Never to the dead (the director cut haunting), and nothing with it off.
        if (!t.On || !mine.Alive)
            return null;
        CrewNewsLine? newest = null;
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
        {
            if (id == me)
                continue;
            if (_heldSince.TryGetValue(id, out long since) && Far(s) && (newest is null || since > newest.Value.Since))
                newest = new CrewNewsLine(id, $"{name(id).ToUpperInvariant()} - {Place(train, s, stop)}", false, since);
            if (_gone.TryGetValue(id, out var gone) && gone is { } line && (tick - line.Since) * SimConstants.TickSeconds <= t.DeathSeconds
                && (newest is null || line.Since >= newest.Value.Since))
                newest = line;
        }
        return newest;
    }
}
