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
/// other: both ends of a heavy crate or a jackpot, a gunner and their loader, two at a mend, the winch's two handles; and
/// <c>pairThreatened</c> such a pair with a creature come on at them, within voice's reach. <paramref name="StopsWithShared"/>:
/// the stops with a problem for two (<see cref="SharedWatch.Problem"/>: grabNear, rescue, pairThreatened), of
/// <paramref name="Stops"/>'s count; <paramref name="Jackpots"/>: the night's two-person jackpots, every stop's.
/// </summary>
public sealed record SharedReport(IReadOnlyDictionary<string, int> Moments, IReadOnlyDictionary<string, int> OnTheRun, IReadOnlyList<SharedStop> Stops,
    int StopsWithShared, int Jackpots)
{
    /// <summary>The stops where two worked one job (going to plan or not).</summary>
    public int StopsWithPairJob { get; init; }
    /// <summary>Note 867: the night's jackpots lying stowed in a car (paid at the gate). Those carried by two are the moments' <c>jackpotCarried</c>.</summary>
    public int JackpotsBanked { get; init; }
}

/// <summary>
/// Note 861: watches a night a tick at a time for problems two crewmates are in together, and where they are (at a stop, or on
/// the run), so a sweep can say how many stops gave a crew something to share. A watch only: it changes nothing.
/// </summary>
public sealed class SharedWatch(SharedTuning t)
{
    /// <summary>Who's held now, and whether a friend has come within reach of them this hold.</summary>
    readonly SortedDictionary<int, bool> _held = [];
    readonly HashSet<(int, int)> _pairs = [], _threatened = [];
    readonly HashSet<int> _carried = [];
    readonly SortedDictionary<string, int> _all = new(StringComparer.Ordinal), _run = new(StringComparer.Ordinal);
    readonly List<SharedStop> _stops = [];
    int _incidents, _facility = -1;
    double _arrived, _left = double.NegativeInfinity, _afoot;
    double _still;
    SortedDictionary<string, int>? _stop;
    /// <summary>Stood this long by a village out on the line (s) is a stop there: not just a slow-down beside it.</summary>
    const double StandSeconds = 5;
    string _kind = "";

    public void Watch(uint tick, World world, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        double now = tick * SimConstants.TickSeconds;
        var run = world.Run;
        // The stop's window: from the train standing (at a facility, or still a while by a village out on the line: a halt, a
        // dead town) till a while after it moves off (what it started carries on past the train moving). A stand anywhere else
        // (a switch set, a crewmate waited for) is the run's.
        var train = world.Train;
        bool still = Math.Abs(train.Dynamics.Velocity) < 0.05;
        _still = still ? _still + SimConstants.TickSeconds : 0;
        bool atFacility = run is { Phase: Run.RunPhase.AtFacility };
        bool standing = atFacility || run is { Phase: Run.RunPhase.Underway } && _still >= StandSeconds && ByAVillage(run, train.Dynamics.Distance);
        int facility = atFacility ? run!.Facility : -1;
        // Stood again soon after moving off at the same place (a spur's set-back, a creep along a platform): the same stop.
        if (standing && _stop is not null && !double.IsPositiveInfinity(_left) && facility == _facility)
            _left = double.PositiveInfinity;
        if (standing && (_stop is null || facility != _facility))
        {
            Close();
            (_facility, _arrived, _left, _afoot) = (facility, now, double.PositiveInfinity, 0);
            _kind = atFacility && run!.FacilityFeature is { } f ? (f.Facility?.ToString() ?? f.Kind.ToString()) : "Village";
            _stop = new(StringComparer.Ordinal);
        }
        else if (_stop is not null && !standing && double.IsPositiveInfinity(_left))
            _left = now;
        if (_stop is not null && now - _left > t.AfterStopSeconds)
            Close();
        if (_stop is not null && double.IsPositiveInfinity(_left))
            _afoot += crew.Count(c => c.State.Alive && c.State.Parent == PlayerState.World) * SimConstants.TickSeconds;

        var living = crew.Where(c => c.State.Alive).Select(c => (c.Id, c.State, At: PlayerMotor.WorldPosition(c.State, train))).ToList();
        // Each hold (the Held flag's spell, whatever has them), and whether a friend was ever within reach of it.
        foreach (var held in living)
        {
            if (!held.State.Has(PlayerFlags.Held))
            {
                _held.Remove(held.Id);
                continue;
            }
            if (_held.TryAdd(held.Id, false))
                Count("grab");
            if (!_held[held.Id] && living.Any(c => c.Id != held.Id && (c.At - held.At).Length <= t.NearMetres))
            {
                _held[held.Id] = true;
                Count("grabNear");
            }
        }
        foreach (int gone in _held.Keys.Where(k => !living.Any(c => c.Id == k)).ToList())
            _held.Remove(gone);
        // A two-person jackpot up between two (note 867: bots carry them too): once a jackpot.
        foreach (var body in world.Bodies.All)
            if (body is { Lifted: true, Second: >= 0 } && Run.Run.IsJackpot(body) && _carried.Add(body.Id))
                Count("jackpotCarried");
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
        // The crane's two (spec D.2: the operator up in its cab, the rigger at the hook below, out of each other's sight): one job
        // however far apart.
        if (run is { Phase: Run.RunPhase.AtFacility, CurrentSite: { } site })
            foreach (var crane in site.Cranes)
                if (crane.Rigger >= 0 && busy.Any(c => c.Id == crane.Rigger)
                    && living.FirstOrDefault(c => c.Id != crane.Rigger && c.State.Has(PlayerFlags.Operating)) is { State.Alive: true } op)
                {
                    var pair = (Math.Min(op.Id, crane.Rigger), Math.Max(op.Id, crane.Rigger));
                    if (together.Add(pair) && !_pairs.Contains(pair))
                        Count("pairJob");
                }
        // A pair job with something come on at them (engaged, within voice's reach of either): both hands full, and a problem.
        var threats = world.ActiveEnemies.Where(Director.Engaged).Select(e => e.WorldPosition(train)).ToList();
        foreach (var (a, b) in together)
        {
            if (_threatened.Contains((a, b)))
                continue;
            var at = busy.Where(c => c.Id == a || c.Id == b).Select(c => c.At).ToList();
            if (threats.Any(e => at.Any(p => (p - e).Length <= t.NearMetres)))
            {
                _threatened.Add((a, b));
                Count("pairThreatened");
            }
        }
        _threatened.IntersectWith(together);
        _pairs.Clear();
        _pairs.UnionWith(together);
    }

    /// <summary>Beside a village out on the line (a halt, a dead town): stood there, the crew can get down to it.</summary>
    static bool ByAVillage(Run.Run run, double along) =>
        run.Route.Features.Any(f => f.Kind == Route.FeatureKind.Village && along >= f.Start - 150 && along <= f.End + 150);

    /// <summary>Each with something of their own in their arms, not the same thing: two carries side by side, not one job.</summary>
    static bool Apart(World world, int a, int b)
    {
        bool aHolds = false, bHolds = false;
        // What's in the arms, as World.CrewAct's Heavy reads it (not the radio on the uniform, nor a find in a slot).
        foreach (var body in world.Bodies.All.Where(b => !b.Stashed && b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy or Physics.BodyKind.Toy
            or Physics.BodyKind.Loot or Physics.BodyKind.Child or Physics.BodyKind.Ragdoll))
        {
            bool byA = body.HeldBy(a), byB = body.HeldBy(b);
            if (byA && byB)
                return false;
            (aHolds, bHolds) = (aHolds || byA, bHolds || byB);
        }
        return aHolds && bHolds;
    }

    /// <summary>The moments that are a problem two are in together (not a pair job going to plan, not a hold nobody was near).</summary>
    public static readonly IReadOnlySet<string> Problem = new HashSet<string>(StringComparer.Ordinal) { "grabNear", "rescue", "pairThreatened" };

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
        return new SharedReport(all, _run, stops, stops.Count(s => s.Moments.Keys.Any(Problem.Contains)), world.Run?.Jackpots ?? 0)
        {
            StopsWithPairJob = stops.Count(s => s.Moments.ContainsKey("pairJob")),
            JackpotsBanked = world.Bodies.All.Count(b => b.Banked && Run.Run.IsJackpot(b)),
        };
    }
}
