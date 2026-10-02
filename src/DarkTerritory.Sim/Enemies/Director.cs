using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A decision the director made, for the harness's pacing and cap audit (App. B.9).</summary>
/// <param name="Paced">Sent because it had been quiet too long (<see cref="DirectorTuning.PaceSeconds"/>), not on the budget's curve.</param>
public readonly record struct DirectorSpawn(uint Tick, EnemyKind Kind, double Cost, double TrainDistance, int ActiveInZone, int ActiveTotal, bool Paced = false);

/// <summary>
/// The pressure director (GDD App. B.1). Enemies aren't rolled independently: a budget is spent across the
/// run against a rising curve (15% before the first facility, 45% across the middle, 40% in the final
/// approach), with hard caps per zone and overall, a grace period at the gate, a trough after every spawn,
/// and silence in the final 500 m. Sleepers are level content and aren't spent from the budget.
/// </summary>
public sealed class Director
{
    readonly DirectorTuning _t;
    readonly Route.Route? _route;
    Pcg32 _rng;
    double _cooldown;

    double _spent;

    public Director(DirectorTuning tuning, Route.Route? route, ulong seed, int cars, int crew)
    {
        _t = tuning;
        _route = route;
        _rng = new Pcg32(seed, 0xD1EC7);
        var tier = route?.Tier ?? RouteTier.Frontier;
        string key = char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];
        double baseBudget = tuning.BaseBudget.GetValueOrDefault(key, 70);
        double lengthMultiplier = 1 + tuning.LengthPerCarBeyondThird * Math.Max(0, cars - 3);
        double crewMultiplier = Math.Min(tuning.CrewCap, tuning.CrewBase + tuning.CrewPerPlayer * crew);
        Budget = baseBudget * lengthMultiplier * crewMultiplier;
        Crew = crew;
    }

    public double Budget { get; }
    public double Spent => _spent;
    /// <summary>
    /// The crew its gates go by (the crew-size threats' <c>minCrew</c>): the expected crew at the start, then whoever's
    /// actually in the night (T115 playtest: a solo host was planned for four, and Tippy Toesie, which needs a friend to
    /// pull it off, came for them alone in the cab).
    /// </summary>
    public int Crew { get; private set; }

    /// <summary>The players in the night this tick (host); none seen leaves it as it was.</summary>
    public void Present(int crew)
    {
        if (crew > 0)
            Crew = crew;
    }
    public List<DirectorSpawn> Log { get; } = new();
    /// <summary>The conflict-table pairs this run has put together (App. B.1 "contradiction seeding"), as "a+b".</summary>
    public List<string> Pairs { get; } = new();

    public double Cost(EnemyKind kind) => _t.Costs.GetValueOrDefault(Key(kind), 2);
    public DirectorTuning Tuning => _t;

    /// <summary>The kind's name in the tuning (costs, the conflict table, the roster).</summary>
    public static string Key(EnemyKind kind) => kind switch
    {
        EnemyKind.CinderHound => "cinderHounds",
        EnemyKind.Dragger => "draggers",
        EnemyKind.Climber => "climbers",
        EnemyKind.Follower => "followers",
        EnemyKind.Ribbit => "ribbits",
        EnemyKind.Sleepers => "sleepers",
        _ => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..],
    };

    /// <summary>
    /// Whether one side of a conflict-table pair is there now (App. B.1): an enemy of that kind about, or a condition:
    /// "grade" (a climb or fall just ahead), "facilityLoading" (at a facility), "facilityStop" (stopped at one), "choir"
    /// (gathering or here).
    /// </summary>
    bool Present(string side, World world, IReadOnlyList<Enemy> active)
    {
        var train = world.Train;
        double s = train.Dynamics.Distance;
        switch (side)
        {
            case "choir":
                return world.Choir.Build > 0 || world.Choir.Present;
            case "facilityLoading" or "facilityStop":
                return world.Run is { Phase: Run.RunPhase.AtFacility };
            case "grade":
                for (double at = s; at <= s + _t.GradeAhead; at += 50)
                    if (Math.Abs(train.Line.Sample(train.Dynamics.Path, at).GradePercent) >= _t.GradePercent)
                        return true;
                return false;
            default:
                return active.Any(e => !e.Gone && Key(e.Kind) == side);
        }
    }

    /// <summary>The conflict-table pair spawning this would make, with what's there now, if any.</summary>
    string? Completes(EnemyKind kind, World world, IReadOnlyList<Enemy> active)
    {
        string key = Key(kind);
        foreach (var pair in _t.Conflicts)
        {
            if (pair.Length != 2)
                continue;
            string? other = pair[0] == key ? pair[1] : pair[1] == key ? pair[0] : null;
            if (other is not null && Present(other, world, active))
                return $"{pair[0]}+{pair[1]}";
        }
        return null;
    }

    /// <summary>App. B.1: "at least one pair per run on Frontier and above. Two on Deep Territory."</summary>
    int PairsWanted => _route is null ? 0 : _t.PairsPerRun.GetValueOrDefault(char.ToLowerInvariant(_route.Tier.ToString()[0]) + _route.Tier.ToString()[1..], 0);

    /// <summary>
    /// Budget held back for the rare, expensive threats this run can still have (the Passenger): the director spends as soon
    /// as it can afford something, so without saving, a cost-5 threat would only ever come when nothing cheaper could.
    /// </summary>
    double Reserve(World world, double distance, EnemyKind forKind)
    {
        if (_route is null || distance < _route.Length * _t.SaveFrom || world.Enemies is not { } et)
            return 0;
        double reserve = 0;
        foreach (var name in _t.SaveFor)
        {
            // Nothing kept back for what this edition hasn't got (the demo has no Gaunt).
            if (Key(forKind) == name || Log.Any(l => Key(l.Kind) == name) || _t.Roster.Length > 0 && !_t.Roster.Contains(name))
                continue;
            bool possible = name switch
            {
                "gaunt" => _route.Tier >= Gate(world, RouteTier.Frontier) && Crew >= et.Gaunt.MinCrew
                    && _route.Of(FeatureKind.Facility).Any(f => f.End >= distance),
                // Only at a stop: while there's a facility still to come.
                "passenger" => _route.Tier >= Gate(world, RouteTier.DeadLines) && Crew >= et.Passenger.MinCrew
                    && _route.Of(FeatureKind.Facility).Any(f => f.End >= distance),
                // Anything else: while its own spawn rule would have it now.
                _ => Enum.GetValues<EnemyKind>().FirstOrDefault(k => Key(k) == name) is var kind && Key(kind) == name
                    && Spawns.For(kind)?.Weight(new SpawnContext(world, et, this)) is > 0,
            };
            if (possible)
                reserve = Math.Max(reserve, _t.Costs.GetValueOrDefault(name, 0));
        }
        return reserve;
    }

    /// <summary>The cargo aboard: what's in the loaded cargo cars of the engine's rake (a cut car on a spur isn't aboard).</summary>
    public static HashSet<CargoKind> Aboard(World world) =>
        world.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load > 0.01 && v.Cargo != CargoKind.None)
            .Select(v => v.Cargo).ToHashSet();

    /// <summary>A tier gate, relaxed by one with comet-derived material aboard (App. B.8).</summary>
    public RouteTier Gate(World world, RouteTier tier) =>
        _t.CometRelaxesGates && tier > RouteTier.Local && Aboard(world).Contains(CargoKind.Comet) ? tier - 1 : tier;

    /// <summary>An enemy's name as a generated line's affinity table has it (linegen/tiers.json): its kind, camel-cased.</summary>
    static string Name(EnemyKind kind) => kind.ToString() is var n ? char.ToLowerInvariant(n[0]) + n[1..] : "";

    /// <summary>How much of the budget may have been spent by this point along the line.</summary>
    public double Allowance(double distance)
    {
        if (_route is null)
            return Budget * Math.Clamp(distance / 20_000, 0, 1);
        var facilities = _route.Of(FeatureKind.Facility).ToList();
        double first = facilities.Count > 0 ? facilities[0].Start : _route.Length * 0.25;
        double last = facilities.Count > 0 ? facilities[^1].End : _route.Length * 0.75;
        var s = _t.PhaseShares;
        double share = distance < first ? s[0] * distance / first
            : distance < last ? s[0] + s[1] * (distance - first) / Math.Max(1, last - first)
            : s[0] + s[1] + s[2] * (distance - last) / Math.Max(1, _route.Length - last);
        return Budget * Math.Clamp(share, 0, 1);
    }

    int MaxConcurrent => Crew >= 6 ? _t.MaxConcurrentLargeCrew : _t.MaxConcurrentSmallCrew;

    /// <summary>At or near one of the route's facilities: within <paramref name="margin"/> of its span.</summary>
    bool NearFacility(double distance, double margin) =>
        _route is not null && _route.Of(FeatureKind.Facility).Any(f => distance >= f.Start - margin && distance <= f.End + margin);

    /// <summary>Why the last decision sent nothing (for the harness's pacing report); null after one that sent something.</summary>
    public string? HeldBecause { get; private set; }

    EnemyKind? Held(string why)
    {
        HeldBecause = why;
        return null;
    }

    /// <summary>Called once a second by the world. Returns what to spawn, if anything.</summary>
    public EnemyKind? Decide(World world, double elapsed, IReadOnlyList<Enemy> active, double noSpawnFinal)
    {
        _cooldown -= 1;
        var train = world.Train;
        double s = train.Dynamics.Distance;
        // Quiet too long (after the playtest: a reward or a problem every 30 s at most, 20 s ideally): something now, the
        // cooldown and the budget's curve notwithstanding. The caps and each kind's gates still hold.
        bool due = world.QuietSeconds >= _t.PaceSeconds;
        if (elapsed < _t.GraceSeconds || _cooldown > 0 && !due)
            return Held(elapsed < _t.GraceSeconds ? "grace" : "cooldown");
        if (_route is not null && s > _route.Length - noSpawnFinal)
            return Held("final stretch");
        int total = active.Count(Engaged);
        if (total >= MaxConcurrent)
            return Held("at the cap");
        double available = Allowance(s) - _spent;
        var options = new List<(EnemyKind Kind, double Weight)>();
        if (world.Enemies is { } et)
        {
            var ctx = new SpawnContext(world, et, this);
            foreach (var rule in Spawns.Rules)
            {
                if (!Allows(rule.Kind) || !Room(rule.Kind, active) || rule.Weight(ctx) is not { } w || w <= 0)
                    continue;
                options.Add((rule.Kind, w));
            }
        }
        // The budget saved up for what's still to come (the Gaunt) holds, except that a paced spawn (it's been quiet too
        // long) may always spend what a paced spawn may: the pace rule beats saving up.
        // App. B.8: "cargo changes the run rather than just scoring it". What's aboard weighs its threats up.
        var aboard = Aboard(world);
        if (aboard.Count > 0)
            for (int i = 0; i < options.Count; i++)
            {
                double w = options[i].Weight;
                if (aboard.Contains(CargoKind.Livestock) && options[i].Kind == EnemyKind.CinderHound)
                    w *= _t.HoundsLivestockWeight;
                foreach (var cargo in aboard)
                    if (_t.CargoWeights.GetValueOrDefault(char.ToLowerInvariant(cargo.ToString()[0]) + cargo.ToString()[1..]) is { } table)
                        w *= table.GetValueOrDefault(Key(options[i].Kind), 1) * table.GetValueOrDefault("*", 1);
                options[i] = (options[i].Kind, w);
            }
        options.RemoveAll(o => !Allows(o.Kind));
        options.RemoveAll(o => Cost(o.Kind) > (due ? Math.Max(available - Reserve(world, s, o.Kind), _t.PacedCost) : available - Reserve(world, s, o.Kind)));
        // Sent because it's been quiet: something that shows itself at once. A Dragger under a car's edge, or a Whistler in its
        // gap, lies silent until someone comes near: that's no answer to a quiet night, if there's anything else to send.
        if (due && options.Any(o => o.Kind is not (EnemyKind.Dragger or EnemyKind.Whistler)))
            options.RemoveAll(o => o.Kind is EnemyKind.Dragger or EnemyKind.Whistler);
        // App. B.1 want balance: each want (kill, split, trust, cargo) aims at its share of what's been spent. A want under
        // its share weighs up, one over it down, once there's been enough spent to have shares.
        if (_spent > 0 && _t.WantShares.Count > 0)
            for (int i = 0; i < options.Count; i++)
            {
                string want = WantOf(options[i].Kind).ToString().ToLowerInvariant();
                double target = _t.WantShares.GetValueOrDefault(want, 0.25);
                double share = _spentByWant.GetValueOrDefault(want) / _spent;
                options[i] = (options[i].Kind, options[i].Weight * Math.Clamp(Math.Sqrt(target / Math.Max(0.05, share)), 0.5, 2));
            }
        // A generated line's director context (linegen plan §15): no spawns under a ban (the grace stretch, the
        // terminus), none of its own while the terrain is already at its hardest there (§15.4), and under the terrain's
        // tags the enemies that belong there come more often (§15.1).
        if (_route?.Plan?.Director is { } context)
        {
            var tags = context.TagsAt(s).ToList();
            // The line's grace stretch is sized for the GDD's 90 s grace at speed (plan §4); a crew that takes its time over
            // the yard and the first two kilometres would sit through minutes of nothing. It gives way to the director's own
            // grace (T74, ARCHITECTURE §8 note 85): the stretch keeps its easy geometry and no Sleepers or Grease on it.
            bool graceOver = _t.LineGraceSeconds >= 0 && world.Run is { } graceRun && graceRun.Seconds >= _t.LineGraceSeconds;
            if (tags.FirstOrDefault(t => context.SpawnBans.Contains(t) && !(t == "grace" && graceOver)) is { } ban)
                return Held($"banned ({ban})");
            if (context.PressureAt(s) >= context.PressureCeiling)
                return Held("terrain at its ceiling");
            for (int i = 0; i < options.Count; i++)
                foreach (var tag in tags)
                    if (context.Affinity.GetValueOrDefault(tag)?.GetValueOrDefault(Name(options[i].Kind)) is { } w)
                        options[i] = (options[i].Kind, options[i].Weight * w);
        }
        if (options.Count == 0)
            return Held("nothing fits");
        // App. B.1 contradiction seeding: "the director draws pairs from a conflict table rather than spawning
        // independently". Whatever would make a pair with what's about now is weighted up, and more so past halfway on a
        // run that hasn't had its pairs yet.
        // Seeded, not flooded: only while the run is short of its pairs.
        if (_route is { } pr && Pairs.Count < PairsWanted)
        {
            bool behind = s > pr.Length * 0.5;
            for (int i = 0; i < options.Count; i++)
                if (Completes(options[i].Kind, world, active) is not null)
                    options[i] = (options[i].Kind, options[i].Weight * _t.PairWeight * (behind ? _t.BehindPairWeight : 1));
        }
        // Variety: a kind sent lately comes on less (the Lamplighters were half of everything in the playtest).
        var recent = Log.TakeLast(_t.VarietyWindow).Select(l => l.Kind).ToList();
        options = [.. options.Select(o => (o.Kind, o.Weight / DMath.Pow(2, recent.Count(k => k == o.Kind))))];

        double pick = _rng.NextDouble() * options.Sum(o => o.Weight);
        var kind = options[^1].Kind;
        foreach (var o in options)
        {
            if (pick < o.Weight)
            {
                kind = o.Kind;
                break;
            }
            pick -= o.Weight;
        }
        HeldBecause = null;
        Charge(world, kind, active, paced: due && _cooldown > 0);
        _cooldown = _rng.Range(_t.CooldownSeconds[0], _t.CooldownSeconds[1]);
        return kind;
    }

    /// <summary>
    /// App. B.1's caps are on what's active: a Dragger lying dormant under a car's edge all night, a Rattle waiting in its
    /// gap, or a Lamplighter that's lost the light and only lingers, isn't pressure (the playtest found them holding the
    /// caps full, and the night went quiet).
    /// </summary>
    public static bool Engaged(Enemy e) => !e.Gone && !e.Hazard
        && (e.Phase is SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish
            // Dormant but on the move is pressure too (a Climber pacing the train); only what lies in wait isn't.
            || e.Phase == SpinePhase.Dormant && e.Kind is not (EnemyKind.Dragger or EnemyKind.Whistler or EnemyKind.CarHugger or EnemyKind.Gaunt or EnemyKind.TippyToesie));

    /// <summary>
    /// App. B.1's hard caps, on what's engaged: two at a time in the flank, the interior and outside (the middle is
    /// uncoverable; three is unfair), one corrupted human, and one of a tell type at once (tells must stay distinguishable).
    /// The total cap is checked before this.
    /// </summary>
    bool Room(EnemyKind kind, IReadOnlyList<Enemy> active)
    {
        var (zone, sense) = Profile(kind);
        var engaged = active.Where(Engaged).ToList();
        int cap = zone == PressureZone.Corrupted ? _t.MaxCorrupted : _t.MaxConcurrentZone;
        if (engaged.Count(e => e.Zone == zone) >= cap)
            return false;
        return !engaged.Any(e => e.Sense == sense && e.Zone == zone);
    }

    /// <summary>A kind's zone and sense (App. B.1's caps are by them), and want: from a stand-in (they're fixed per kind).</summary>
    static (PressureZone Zone, Sense Sense) Profile(EnemyKind kind) => Profiles.TryGetValue(kind, out var p) ? (p.Zone, p.Sense) : (PressureZone.Interior, Sense.Heat);

    static Want WantOf(EnemyKind kind) => Profiles.TryGetValue(kind, out var p) ? p.Want : Want.Kill;

    static readonly Dictionary<EnemyKind, (PressureZone Zone, Sense Sense, Want Want)> Profiles = Build();

    static Dictionary<EnemyKind, (PressureZone, Sense, Want)> Build()
    {
        var d = new Dictionary<EnemyKind, (PressureZone, Sense, Want)>();
        foreach (var kind in Enum.GetValues<EnemyKind>())
        {
            Enemy e = kind switch
            {
                EnemyKind.Sleepers => new Sleepers(0),
                EnemyKind.CinderHound => new CinderHound(0, 0),
                EnemyKind.Switchman => new Switchman(0),
                EnemyKind.SootChildren => new SootChildren(0),
                EnemyKind.Dragger => new Dragger(0),
                EnemyKind.Stoker => new Stoker(0),
                EnemyKind.Climber => new Climber(0),
                EnemyKind.Gaunt => new Gaunt(0),
                EnemyKind.CarFire => new CarFire(0),
                EnemyKind.Passenger => new Passenger(0),
                EnemyKind.Follower => new Follower(0),
                EnemyKind.Drift => new Drift(0),
                EnemyKind.TrackDoll => new TrackDoll(0),
                EnemyKind.CarHugger => new CarHugger(0),
                EnemyKind.Whistler => new Whistler(0),
                EnemyKind.TippyToesie => new TippyToesie(0),
                EnemyKind.FireFlies => new FireFlies(0),
                EnemyKind.Ribbit => new Ribbit(0, 0),
                EnemyKind.Grumbler => new Grumbler(0),
                _ => new ChoirGhost(0),
            };
            d[kind] = (e.Zone, e.Sense, e.Want);
        }
        return d;
    }

    readonly Dictionary<string, double> _spentByWant = new();

    /// <summary>Condition-triggered enemies (the Stoker) cost budget only when they actually fire (App. B.5).</summary>
    public void Charge(World world, EnemyKind kind, IReadOnlyList<Enemy> active, bool paced = false)
    {
        _spent += Cost(kind);
        string want = WantOf(kind).ToString().ToLowerInvariant();
        _spentByWant[want] = _spentByWant.GetValueOrDefault(want) + Cost(kind);
        if (Completes(kind, world, active) is { } pair)
            Pairs.Add(pair);
        var zone = Profile(kind).Zone;
        Log.Add(new DirectorSpawn(world.Tick, kind, Cost(kind), world.Train.Dynamics.Distance,
            active.Count(e => Engaged(e) && e.Zone == zone) + 1, active.Count(Engaged) + 1, paced));
    }

    /// <summary>This edition has the kind (<see cref="DirectorTuning.Roster"/>, empty for every kind).</summary>
    public bool Allows(EnemyKind kind) => _t.Roster.Length == 0 || _t.Roster.Contains(Key(kind));

    /// <summary>What's been spent on each want (App. B.1), for the harness's want-balance check.</summary>
    public IReadOnlyDictionary<string, double> SpentByWant => _spentByWant;

    public Pcg32 Rng => _rng;
    public double NextRange(double min, double max) => _rng.Range(min, max);
}
