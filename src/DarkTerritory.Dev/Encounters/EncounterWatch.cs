using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Dev.Encounters;

/// <summary>An enemy as the watch sees it on one tick: what it is and what it's doing, nothing more.</summary>
/// <param name="Hazard">Trouble that isn't a creature: a hazard (the Drift, Sleepers on the rail) or an incident on the train (a car
/// fire). It's danger while it lasts (§6), never an encounter of its own.</param>
/// <param name="Capped">What the director's engaged cap counts it as today (<see cref="Sim.Enemies.Director.Engaged"/>).</param>
public readonly record struct Seen(int Id, EnemyKind Kind, SpinePhase Phase, bool Hazard, bool Capped);

/// <summary>One encounter: the first of its members to come on, its kind, how many came on with it, and how long it was on.</summary>
public sealed record WatchedEncounter(double Start, EnemyKind Kind, int Members, double Seconds);

/// <summary>
/// `dt director sweep`'s eye on a night (the director's brief §7–§8, note 888), from world state only: it reads the enemies each
/// tick and never touches them, so a watched night plays as an unwatched one. Measured, not inferred (§8): an enemy "comes on"
/// when its spine reaches Telegraph or further (it is telegraphing at, committing to, grabbing or punishing someone), which
/// is the sim's own fact; nothing here says a player saw it.
/// <list type="bullet">
/// <item><b>An encounter</b> is the first enemy of a kind to come on, with every other of that kind coming on within
/// <see cref="GroupSeconds"/> of it (a pack, a swarm, a Householder's house): distinct encounters, not bodies (§7).</item>
/// <item><b>A drought</b> is a stretch after the director's grace with no encounter on; <b>danger</b> also counts a hazard
/// burning (a car fire), the sustained trouble §6 says is not a drought.</item>
/// <item><b>Overlap</b> is how many encounters are on at once, sampled each second.</item>
/// </list>
/// Until #600's encounter telemetry lands this is the sweep's only count; once it does, the sweep reports both side by side.
/// </summary>
public sealed class EncounterWatch(double groupSeconds = EncounterWatch.DefaultGroupSeconds)
{
    /// <summary>Two of a kind coming on within this long are one encounter (a Cinder Hound pack boards over some seconds).</summary>
    public const double DefaultGroupSeconds = 20;

    /// <summary>The director's grace (s), from its report once the night's done; the droughts are counted from it.</summary>
    public double Grace { get; set; }
    public double GroupSeconds { get; } = groupSeconds;

    readonly Dictionary<int, int> _encounterOf = [];
    readonly List<(double Start, EnemyKind Kind, int Members, double On, double LastOn)> _encounters = [];
    readonly Dictionary<EnemyKind, int> _lastOfKind = [];
    double _lastSecond = -1;
    double _seconds;
    readonly List<(double Second, bool Threat, bool Danger)> _samples = [];
    readonly int[] _overlapSeconds = new int[8];

    public int MaxOverlap { get; private set; }
    public int CappedMax { get; private set; }
    /// <summary>Seconds the cap counted something while no encounter was on: bodies holding the cap that threaten nobody (§2 issue 4).</summary>
    public int CappedIdleSeconds { get; private set; }
    public int Seconds => (int)_seconds;

    /// <summary>A world's tick, read: every enemy, as <see cref="Seen"/>; and whether the director is held for a quiet the brief
    /// keeps (<see cref="Protected"/>).</summary>
    public void Observe(World world)
    {
        double seconds = world.Tick * SimConstants.TickSeconds;
        if (world.Director?.HeldBecause is { } why && Protects(why))
            _protected.Add((int)Math.Floor(seconds));
        Tick(seconds, world.ActiveEnemies.Select(e => new Seen(e.Id, e.Kind, e.Phase, e.Hazard || e is Incident, Sim.Enemies.Director.Engaged(e))));
    }

    /// <summary>
    /// The quiets the brief keeps (§1 A, §6; #613, note 930): the night's grace, the final stretch and the terminus's safe stretch,
    /// the line's grace ban, and a facility's lull. Seconds under them can't hold an encounter of the director's, so the halves'
    /// counts are also read per minute open (n1-C2's finding: the terminus approach takes ~225 s of every late half).
    /// </summary>
    public static bool Protects(string heldBecause) => heldBecause is "grace" or "final stretch" or "facility lull"
        || heldBecause.StartsWith("banned (terminus", StringComparison.Ordinal) || heldBecause.StartsWith("banned (grace", StringComparison.Ordinal);

    readonly HashSet<int> _protected = [];

    /// <summary>Seconds before <paramref name="half"/> (early) and from it to <paramref name="end"/> (late) not under a protected quiet.</summary>
    public (double Early, double Late) OpenSeconds(double half, double end) =>
        (Math.Max(0, half - _protected.Count(s => s < half)), Math.Max(0, end - half - _protected.Count(s => s >= half && s < end)));

    public void Tick(double seconds, IEnumerable<Seen> enemies)
    {
        var all = enemies.Where(e => e.Kind != EnemyKind.Sleepers).ToList();
        foreach (var e in all.Where(e => !e.Hazard && On(e.Phase)))
        {
            if (!_encounterOf.TryGetValue(e.Id, out int at))
            {
                if (_lastOfKind.TryGetValue(e.Kind, out int last) && seconds - _encounters[last].Start <= GroupSeconds)
                {
                    at = last;
                    _encounters[at] = _encounters[at] with { Members = _encounters[at].Members + 1 };
                }
                else
                {
                    at = _encounters.Count;
                    _encounters.Add((seconds, e.Kind, 1, 0, seconds));
                    _lastOfKind[e.Kind] = at;
                }
                _encounterOf[e.Id] = at;
            }
            _encounters[at] = _encounters[at] with { LastOn = seconds };
        }
        _seconds = Math.Max(_seconds, seconds);
        // The rest once a second: what was on, what the cap held, and the droughts.
        if (Math.Floor(seconds) <= _lastSecond)
            return;
        _lastSecond = Math.Floor(seconds);
        var on = all.Where(e => !e.Hazard && On(e.Phase) && _encounterOf.ContainsKey(e.Id)).Select(e => _encounterOf[e.Id]).Distinct().ToList();
        foreach (int i in on)
            _encounters[i] = _encounters[i] with { On = _encounters[i].On + 1 };
        MaxOverlap = Math.Max(MaxOverlap, on.Count);
        _overlapSeconds[Math.Min(on.Count, _overlapSeconds.Length - 1)]++;
        int capped = all.Count(e => e.Capped);
        CappedMax = Math.Max(CappedMax, capped);
        if (capped > 0 && on.Count == 0)
            CappedIdleSeconds++;
        _samples.Add((_lastSecond, on.Count > 0, on.Count > 0 || all.Any(e => e.Hazard && !Gone(e.Phase))));
    }

    /// <summary>The stretches from <see cref="Grace"/> on with nothing <paramref name="on"/>, the one running at the end included.</summary>
    IReadOnlyList<double> Gaps(Func<(double Second, bool Threat, bool Danger), bool> on)
    {
        var gaps = new List<double>();
        double since = Grace;
        foreach (var s in _samples.Where(s => s.Second >= Grace && on(s)))
        {
            if (s.Second - since > 1)
                gaps.Add(s.Second - since);
            since = s.Second;
        }
        if (_seconds - since > 1)
            gaps.Add(_seconds - since);
        return gaps;
    }

    static bool On(SpinePhase p) => p is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
    static bool Gone(SpinePhase p) => p == SpinePhase.Gone;

    public IReadOnlyList<WatchedEncounter> Encounters =>
        [.. _encounters.Select(e => new WatchedEncounter(Math.Round(e.Start, 1), e.Kind, e.Members, e.On))];

    /// <summary>Every drought of the night, the one still running at its end included.</summary>
    public IReadOnlyList<double> Droughts => Gaps(s => s.Threat);
    public IReadOnlyList<double> DangerDroughts => Gaps(s => s.Danger);

    /// <summary>Seconds with this many encounters on at once (the last bucket, that many or more).</summary>
    public IReadOnlyList<int> OverlapSeconds => _overlapSeconds;
}
