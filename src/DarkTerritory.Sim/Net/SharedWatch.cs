using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Mirror of balance.json <c>shared</c> (note 861): what the harness counts as two crewmates in one problem. Field docs live in
/// that file.
/// </summary>
public sealed record SharedTuning
{
    public double NearMetres { get; init; } = 26;
    public double PairMetres { get; init; } = 6;
    public double AfterStopSeconds { get; init; } = 30;
    public double StopsWithShared { get; init; } = 0.5;
}

/// <summary>
/// A stop on a harness night (note 861): which facility, its kind, how long the train stood, and the shared moments in it (from
/// arriving till <see cref="SharedTuning.AfterStopSeconds"/> after leaving), by kind. <paramref name="Afoot"/>: crew-seconds
/// off the train there.
/// </summary>
public sealed record SharedStop(int Facility, string Kind, double Arrived, double Seconds, double Afoot, IReadOnlyDictionary<string, int> Moments);

/// <summary>
/// The night's shared problems (note 861; the director's playtest of 16 Oct evening, line 8): moments that took two. By kind:
/// <c>grabNear</c> a crewmate held with a friend within <see cref="SharedTuning.NearMetres"/> (voice reach: they can hear it and
/// come), <c>grabAlone</c> held with nobody near (the moment missed), <c>rescue</c> a hold broken by someone else, and
/// <c>pairJob</c> two crewmates busy (<see cref="Census.IsCommitted"/>) within <see cref="SharedTuning.PairMetres"/> of each
/// other: both ends of a heavy crate or a jackpot, a gunner and their loader, two at a mend, the winch's two handles.
/// <paramref name="StopsWithShared"/>: the stops with one or more, of <paramref name="Stops"/>'s count.
/// </summary>
public sealed record SharedReport(IReadOnlyDictionary<string, int> Moments, IReadOnlyDictionary<string, int> OnTheRun, IReadOnlyList<SharedStop> Stops,
    int StopsWithShared, int Jackpots);

/// <summary>
/// Note 861: watches a night a tick at a time for problems two crewmates are in together, and where they are (at a stop, or on
/// the run), so a sweep can say how many stops gave a crew something to share. A watch only: it changes nothing.
/// </summary>
public sealed class SharedWatch(SharedTuning t)
{
    readonly HashSet<(int Enemy, int Victim)> _grabs = [], _near = [];
    readonly HashSet<(int, int)> _pairs = [];
    readonly SortedDictionary<string, int> _all = new(StringComparer.Ordinal), _run = new(StringComparer.Ordinal);
    readonly List<SharedStop> _stops = [];
    int _incidents, _facility = -1;
    double _arrived, _left = double.NegativeInfinity, _afoot;
    double _still;
    SortedDictionary<string, int>? _stop;
    /// <summary>Stood this long out on the line (s) is a stop: a village's, a halt's, a stand for a crewmate left behind.</summary>
    const double StandSeconds = 5;
    string _kind = "";

    public void Watch(uint tick, World world, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        double now = tick * SimConstants.TickSeconds;
        var run = world.Run;
        // The stop's window: from the train standing (at a facility, or still a while out on the line: a village, a halt) till a
        // while after it moves off (what it started carries on past the train moving).
        var train = world.Train;
        bool still = Math.Abs(train.Dynamics.Velocity) < 0.05;
        _still = still ? _still + SimConstants.TickSeconds : 0;
        bool atFacility = run is { Phase: Run.RunPhase.AtFacility };
        bool standing = atFacility || run is { Phase: Run.RunPhase.Underway } && _still >= StandSeconds;
        int facility = atFacility ? run!.Facility : -1;
        // Stood again soon after moving off at the same place (a spur's set-back, a creep along a platform): the same stop.
        if (standing && _stop is not null && !double.IsPositiveInfinity(_left) && facility == _facility)
            _left = double.PositiveInfinity;
        if (standing && (_stop is null || facility != _facility))
        {
            Close();
            (_facility, _arrived, _left, _afoot) = (facility, now, double.PositiveInfinity, 0);
            _kind = atFacility && run!.FacilityFeature is { } f ? (f.Facility?.ToString() ?? f.Kind.ToString()) : Where(run, train.Dynamics.Distance);
            _stop = new(StringComparer.Ordinal);
        }
        else if (_stop is not null && !standing && double.IsPositiveInfinity(_left))
            _left = now;
        if (_stop is not null && now - _left > t.AfterStopSeconds)
            Close();
        if (_stop is not null && double.IsPositiveInfinity(_left))
            _afoot += crew.Count(c => c.State.Alive && c.State.Parent == PlayerState.World) * SimConstants.TickSeconds;

        var living = crew.Where(c => c.State.Alive).Select(c => (c.Id, c.State, At: PlayerMotor.WorldPosition(c.State, train))).ToList();
        foreach (var e in world.ActiveEnemies)
        {
            int h = e.Holding < 0 || e.Gone ? -1 : living.FindIndex(c => c.Id == e.Holding);
            if (h < 0)
                continue;
            var held = living[h];
            if (_grabs.Add((e.Id, e.Holding)))
                Count("grab");
            if (!_near.Contains((e.Id, e.Holding)) && living.Any(c => c.Id != held.Id && (c.At - held.At).Length <= t.NearMetres))
            {
                _near.Add((e.Id, e.Holding));
                Count("grabNear");
            }
        }
        // A hold broken by a friend (the log's Rescue, its actor someone else).
        var log = world.Attribution.Log;
        for (; _incidents < log.Count; _incidents++)
            if (log[_incidents] is { Kind: Run.IncidentKind.Rescue } r && r.Actor >= 0 && r.Actor != r.Victim)
                Count("rescue");

        // Busy as the census read it (its second's count comes inside the host's tick, while a winch's hands are on its handles;
        // they're let go again before the tick ends), or read now with no director.
        var busy = world.Director is { } d
            ? d.Posts.Entries.Where(c => c.Committed && living.Any(l => l.Id == c.Player)).Select(c => (Id: c.Player, c.At)).ToList()
            : living.Where(c => Census.IsCommitted(world, c.Id, c.State)).Select(c => (c.Id, c.At)).ToList();
        var together = new HashSet<(int, int)>();
        for (int i = 0; i < busy.Count; i++)
            for (int j = i + 1; j < busy.Count; j++)
                if ((busy[i].At - busy[j].At).Length <= t.PairMetres && !Apart(world, busy[i].Id, busy[j].Id))
                {
                    var pair = (Math.Min(busy[i].Id, busy[j].Id), Math.Max(busy[i].Id, busy[j].Id));
                    together.Add(pair);
                    // Once a spell of it: the same two at it again after letting go is another.
                    if (!_pairs.Contains(pair))
                        Count("pairJob");
                }
        _pairs.Clear();
        _pairs.UnionWith(together);
    }

    /// <summary>A stand out on the line by what it's beside: a village (a halt, a dead town), or the line.</summary>
    static string Where(Run.Run? run, double along) =>
        run?.Route.Features.FirstOrDefault(f => f.Kind == Route.FeatureKind.Village && along >= f.Start - 150 && along <= f.End + 150) is not null ? "Village" : "Line";

    /// <summary>Each with something of their own in their hands, not the same thing: two carries side by side, not one job.</summary>
    static bool Apart(World world, int a, int b)
    {
        bool aHolds = false, bHolds = false;
        foreach (var body in world.Bodies.All)
        {
            bool byA = body.HeldBy(a), byB = body.HeldBy(b);
            if (byA && byB)
                return false;
            (aHolds, bHolds) = (aHolds || byA, bHolds || byB);
        }
        return aHolds && bHolds;
    }

    void Count(string kind)
    {
        _all[kind] = _all.GetValueOrDefault(kind) + 1;
        var into = _stop ?? _run;
        into[kind] = into.GetValueOrDefault(kind) + 1;
    }

    void Close()
    {
        if (_stop is null)
            return;
        double until = double.IsPositiveInfinity(_left) ? _arrived : _left;
        _stops.Add(new SharedStop(_facility, _kind, Math.Round(_arrived, 1), Math.Round(Math.Max(0, until - _arrived), 1), Math.Round(_afoot), _stop));
        _stop = null;
    }

    /// <summary>The night as it stands (a stop still open is counted as it is). A grab is "grab"; one the crew could share, "grabNear".</summary>
    public SharedReport Report(World world)
    {
        var stops = _stop is null ? _stops : [.. _stops, new SharedStop(_facility, _kind, Math.Round(_arrived, 1), 0, Math.Round(_afoot), _stop)];
        var all = new SortedDictionary<string, int>(_all, StringComparer.Ordinal);
        if (all.GetValueOrDefault("grab") - all.GetValueOrDefault("grabNear") is > 0 and var alone)
            all["grabAlone"] = alone;
        return new SharedReport(all, _run, stops, stops.Count(s => s.Moments.Keys.Any(k => k != "grab")), world.Run?.Jackpots ?? 0);
    }
}
