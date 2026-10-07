using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A decision the director made, for the harness's pacing and cap audit (App. B.9).</summary>
/// <param name="Paced">
/// Pressed for: the pressure was at <see cref="PressureTuning.PressAt"/>, and it came through the cooldown or over the budget's
/// curve (by at most <see cref="DirectorTuning.PacedCost"/>).
/// </param>
public readonly record struct DirectorSpawn(uint Tick, EnemyKind Kind, double Cost, double TrainDistance, int ActiveInZone, int ActiveTotal, bool Paced = false);

/// <summary>What built the director's pressure in its last second (note 266), for the harness's trace.</summary>
/// <param name="Rate">Pressure a second, all told.</param>
/// <param name="Escalation">The night's escalation multiplier (1 at the gate, rising toward dawn or the terminus).</param>
/// <param name="Quiet">Seconds since a threat was last engaged.</param>
/// <param name="Loud">The loudness term (the Choir meter), per second.</param>
/// <param name="Cargo">The cargo-aboard term, per second.</param>
/// <param name="Relief">The crew's relief valve (1 all well; less with crew down or hurt).</param>
/// <param name="Conditions">The conditions multiplier (dark, cold, wet, wind).</param>
public readonly record struct PressureTerms(double Rate, double Escalation, double Quiet, double Loud, double Cargo, double Relief, double Conditions);

/// <summary>
/// The pressure director (GDD App. B.1). Enemies aren't rolled independently: a budget is spent across the
/// run against a rising curve (15% before the first facility, 45% across the middle, 40% in the final
/// approach), with hard caps per zone and overall, a grace period at the gate, a trough after every spawn,
/// and silence in the final 500 m. Sleepers are level content and aren't spent from the budget.
/// When it spends is algorithmic (design decision 2026-10, note 266): a pressure builds from the night's escalation, quiet,
/// the crew's loudness and the cargo aboard, eased when the crew is down, and past a threshold the director sends what its
/// weights pick; the spawn relieves it.
/// </summary>
public sealed class Director
{
    readonly DirectorTuning _t;
    readonly Route.Route? _route;
    Pcg32 _rng;
    readonly ulong _seed;
    double _cooldown;

    double _spent;

    public Director(DirectorTuning tuning, Route.Route? route, ulong seed, int cars, int crew)
    {
        _t = tuning;
        _route = route;
        _rng = new Pcg32(seed, 0xD1EC7);
        _seed = seed;
        var tier = route?.Tier ?? RouteTier.Frontier;
        string key = char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];
        double baseBudget = tuning.BaseBudget.GetValueOrDefault(key, 70);
        double lengthMultiplier = 1 + tuning.LengthPerCarBeyondThird * Math.Max(0, cars - 3);
        double crewMultiplier = Math.Min(tuning.CrewCap, tuning.CrewBase + tuning.CrewPerPlayer * crew);
        Budget = baseBudget * lengthMultiplier * crewMultiplier;
        Crew = crew;
        // The night's quiet spell (design decision 2026-10): somewhere in the range, from the seed alone (its own stream, so
        // the director's draws are untouched), and shorter on the harder tiers.
        double u = new Pcg32(seed, 0x6EACE).NextDouble() * tuning.Pressure.GraceTierScale.GetValueOrDefault(key, 1);
        Grace = tuning.GraceMinSeconds + (tuning.GraceMaxSeconds - tuning.GraceMinSeconds) * Math.Clamp(u, 0, 1);
        _tierRate = tuning.Pressure.Tier.GetValueOrDefault(key, 1);
    }

    readonly double _tierRate;
    double _pressure;
    bool _building;
    double _sinceThreat;
    /// <summary>The line run since a threat was last engaged or sent (note 270: quiet counted in kilometres).</summary>
    double _quietMetres;

    /// <summary>This night's grace: no spawns of the director's own before it (seconds into the night).</summary>
    public double Grace { get; }
    /// <summary>The pressure now (note 266): 0 until the grace is over, then <see cref="PressureTuning.Start"/> and building.</summary>
    public double Pressure => _pressure;
    /// <summary>What built it in the last second the director thought.</summary>
    public PressureTerms Terms { get; private set; }

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
        if (elapsed < Grace)
            return Held("grace");
        if (_route is not null && s > _route.Length - noSpawnFinal)
            return Held("final stretch");
        // GDD §9, T128 (note 273): nothing comes for a train that's in one of the forts.
        if (world.TrainInFort)
            return Held("in a fort");
        // A generated line's director context (linegen plan §15): no spawns under a ban (the grace stretch, the terminus), and
        // none of its own while the terrain is already at its hardest there (§15.4). The pressure waits with it: the terrain's
        // the problem there, not the quiet.
        var context = _route?.Plan?.Director;
        var tags = context?.TagsAt(s).ToList() ?? [];
        if (context is not null)
        {
            // The line's grace stretch is sized for the GDD's 90 s grace at speed (plan §4); a crew that takes its time over
            // the yard and the first two kilometres would sit through minutes of nothing. It gives way to the night's own
            // grace (T74, ARCHITECTURE §8 notes 85 and 195): the stretch keeps its easy geometry and no Sleepers or Grease on it.
            bool graceOver = _t.LineGraceSeconds >= 0 && world.Run is { } graceRun && graceRun.Seconds >= Math.Max(_t.LineGraceSeconds, Grace);
            if (tags.FirstOrDefault(t => context.SpawnBans.Contains(t) && !(t == "grace" && graceOver)) is { } ban)
                return Held($"banned ({ban})");
            if (context.PressureAt(s) >= context.PressureCeiling)
                return Held("terrain at its ceiling");
        }
        Build(world, active, elapsed, s);
        // Past the threshold the director spends; well past it (pressed: a long quiet, or a night the budget's curve can't keep
        // up with) the cooldown gives way, and the curve may be overdrawn by up to pacedCost. The caps and each kind's gates
        // still hold.
        bool due = _pressure >= _t.Pressure.PressAt;
        if (_pressure < _t.Pressure.Threshold)
            return Held("building");
        if (_cooldown > 0 && !due)
            return Held("cooldown");
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
        // The budget saved up for what's still to come (the Passenger) holds, except that a pressed spawn may always spend what
        // a pressed spawn may: the pressure beats saving up.
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
        // GDD §18: what already lives at the facility the train's stopped at comes on more there (note 185).
        if (world.Run?.FacilityFeature?.Facility is { } here
            && _t.Residents.GetValueOrDefault(char.ToLowerInvariant(here.ToString()[0]) + here.ToString()[1..]) is { } residents)
            for (int i = 0; i < options.Count; i++)
                options[i] = (options[i].Kind, options[i].Weight * residents.GetValueOrDefault(Key(options[i].Kind), 1));
        options.RemoveAll(o => !Allows(o.Kind));
        options.RemoveAll(o => Cost(o.Kind) > (due ? Math.Max(available - Reserve(world, s, o.Kind), _t.PacedCost) : available - Reserve(world, s, o.Kind)));
        // Pressed for: something that shows itself at once. A Dragger under a car's edge, or a Whistler in its
        // gap, lies silent until someone comes near: that's no answer to a pressing night, if there's anything else to send.
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
        // Under a generated line's terrain tags the enemies that belong there come more often (linegen plan §15.1).
        if (context is not null)
        {
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
        options = WeighVotes(options);

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
        Charge(world, kind, active, paced: due && (_cooldown > 0 || Cost(kind) > available - Reserve(world, s, kind)));
        _cooldown = _rng.Range(_t.CooldownSeconds[0], _t.CooldownSeconds[1]);
        return kind;
    }

    /// <summary>
    /// One second of pressure (design decision 2026-10, note 266): the night's escalation toward dawn or the terminus, the quiet
    /// since a threat was last engaged, the crew's loudness, the cargo aboard; scaled by the tier and the conditions; eased
    /// when the crew is down or hurt (a relief valve, so a night that's going badly doesn't snowball) and while threats are
    /// already engaged. The dead's votes aren't in it: D.11 has them move weight between creatures, never the pacing.
    /// </summary>
    void Build(World world, IReadOnlyList<Enemy> active, double elapsed, double s)
    {
        var p = _t.Pressure;
        if (!_building)
        {
            _building = true;
            _pressure = p.Start;
        }
        // How far into the night: along the line, or toward dawn if the clock's ahead of the train.
        double progress = _route is null ? s / 20_000
            : Math.Max(s / Math.Max(1, _route.Length), _route.DawnSeconds > 0 ? elapsed / _route.DawnSeconds : 0);
        double escalation = 1 + p.Escalation * DMath.Pow(Math.Clamp(progress, 0, 1), p.EscalationPower);
        // Engaged with the crew: showing itself, coming on, holding someone, punishing. What only paces the train or lingers
        // (on the caps, <see cref="Engaged"/>) neither stops the quiet nor keeps the crew busy (harness: a Climber pacing a car
        // nobody walked into held the night's pressure flat).
        int engaged = active.Count(Confronting);
        _sinceThreat = engaged > 0 ? 0 : _sinceThreat + 1;
        _quietMetres = engaged > 0 ? 0 : _quietMetres + world.Train.Dynamics.Speed;
        // GDD App. F.1 (note 270): a stretch of line holds the same danger whatever the train's speed; time is the backstop.
        double ramp = p.QuietRampMetres > 0
            ? Math.Max(_quietMetres / p.QuietRampMetres, _sinceThreat / Math.Max(1, p.QuietBackstopSeconds))
            : _sinceThreat / Math.Max(1, p.QuietRampSeconds);
        double quiet = p.QuietPerSecond * Math.Min(1, ramp);
        // The meter's loudness against its threshold (App. C.7): a crew loud enough to draw the Choir draws everything else too.
        double loud = world.Combat is { } combat && combat.Choir.Threshold > 0
            ? p.LoudPerSecond * Math.Min(p.LoudCap, world.Choir.Loudness / combat.Choir.Threshold) : 0;
        double loads = 0;
        foreach (var v in world.Train.Dynamics.Consist.Vehicles)
            if (v.Kind == VehicleKind.Cargo && v.Load > 0.01 && v.Cargo != CargoKind.None)
                loads += v.Load * v.CargoIntegrity * p.CargoValue.GetValueOrDefault(char.ToLowerInvariant(v.Cargo.ToString()[0]) + v.Cargo.ToString()[1..], 1);
        double cargo = p.CargoPerLoad * loads;
        var train = world.Train;
        int coldStep = train.Line.Conditions?.ColdStep(train.Dynamics.Path, s) ?? 0;
        var weather = _route?.Weather;
        double conditions = 1 + (world.LampLit ? 0 : p.Dark) + p.ColdPerStep * Math.Max(0, coldStep)
            + (weather is null ? 0 : p.Cold * weather.Cold + (weather.Wet ? p.Wet : 0) + p.Wind * weather.Wind);
        // The relief valve: the share of the crew still alive, to a power, and less for each of them badly hurt.
        double relief = 1;
        var crew = world.CrewThisTick;
        if (crew.Count > 0)
        {
            int alive = 0, hurt = 0;
            foreach (var (_, state) in crew)
                if (state.Alive)
                {
                    alive++;
                    hurt += state.Health < p.HurtBelow ? 1 : 0;
                }
            relief = alive == 0 ? 0 : DMath.Pow((double)alive / crew.Count, p.DownPower) * (1 - p.HurtRelief * hurt / alive);
        }
        // Busy fades as the night goes on: late, the director no longer waits for the crew to finish what's on them.
        double busy = 1 / (1 + p.Busy * engaged * Math.Max(0, 1 - p.BusyFade * Math.Clamp(progress, 0, 1)));
        double rate = _tierRate * conditions * relief * busy * escalation * (p.BasePerSecond + quiet + loud + cargo);
        _pressure = Math.Min(p.Max, _pressure + rate);
        Terms = new PressureTerms(rate, escalation, _sinceThreat, loud, cargo, relief, conditions);
    }

    /// <summary>A crewmate the train's left behind (T128, note 273): how long, their pressure, the hunts sent, the last hunt's pack.</summary>
    sealed class LeftBehind
    {
        public double Seconds, Pressure;
        public int Hunts, Pack = -1;
    }

    readonly SortedDictionary<int, LeftBehind> _left = [];
    Pcg32 _huntRng;
    bool _huntSeeded;

    /// <summary>A hunt sent at a crewmate left behind (T128): when, at whom, its pack and size, how long and how far behind they'd been.</summary>
    public readonly record struct Hunt(uint Tick, int Player, int Pack, int Size, double Seconds, double Behind, int Gaunt = -1);

    /// <summary>Every hunt sent at a crewmate left behind tonight (T128, note 273).</summary>
    public List<Hunt> Hunts { get; } = new();

    /// <summary>A left-behind crewmate's own pressure now (0 for anyone the train hasn't left).</summary>
    public double AbandonedPressure(int player) => _left.TryGetValue(player, out var l) ? l.Pressure : 0;

    /// <summary>
    /// T128 (build 1121: "a player left behind by the train should feel the world close in: tension, monsters coming, the
    /// difficulty spiking for that player. They needn't die at once"; GDD §7, §23; note 273). Once a second, with the night's
    /// own pressure (whatever holds it: the grace, a ban, the caps). Each crewmate alive on the ground further than
    /// <see cref="AbandonedTuning.BehindM"/> along the line from the train, and outside the forts, builds a pressure of their own,
    /// on the night's tier and conditions, rising the longer and the further they're left; past its threshold a Ribbit pack
    /// comes for them alone (App. A.6: it hops in on whoever it outnumbers, and can be outrun), each bigger and put down closer
    /// than the last, never two at once. Not from the budget, not on the caps: it's theirs, not the train's. Its own dice, so
    /// the night's draws are as they were.
    /// </summary>
    public void Abandoned(World world, IReadOnlyList<Enemy> active)
    {
        var a = _t.Abandoned;
        if (!a.On || world.Enemies is not { } et || !Allows(EnemyKind.Ribbit))
        {
            _left.Clear();
            return;
        }
        if (!_huntSeeded)
        {
            _huntRng = new Pcg32(_seed, 0xAB4D0);
            _huntSeeded = true;
        }
        var train = world.Train;
        double front = train.Dynamics.Distance, rear = train.Dynamics.RearDistance;
        var crew = world.CrewThisTick;
        var seen = new HashSet<int>();
        foreach (var (id, s) in crew)
        {
            if (!s.Alive || s.Parent != PlayerState.World)
                continue;
            var at = PlayerMotor.WorldPosition(s, train);
            if (world.InFort(at))
                continue;
            double hint = s.LineHint;
            train.Line.Nearest(at, ref hint);
            double behind = Math.Max(rear - hint, hint - front);
            if (behind <= a.BehindM)
                continue;
            seen.Add(id);
            if (!_left.TryGetValue(id, out var l))
                _left[id] = l = new LeftBehind();
            l.Seconds += 1;
            double conditions = Terms.Conditions > 0 ? Terms.Conditions : 1;
            l.Pressure += _tierRate * conditions
                * (a.PerSecond + a.RampPerSecond * Math.Min(1, l.Seconds / Math.Max(1, a.RampSeconds)) + a.PerKm * behind / 1000);
            bool hunted = l.Pack >= 0 && active.Any(e => e is Ribbit r && !r.Gone && r.Pack == l.Pack);
            if (hunted || l.Pressure < a.Threshold)
                continue;
            // More than the group they're in (Ribbits only take the outnumbered), one more each time, to the most.
            var r = et.Ribbits;
            int group = crew.Count(c => c.State.Alive && c.State.Parent == PlayerState.World
                && ((PlayerMotor.WorldPosition(c.State, train) - at) with { Y = 0 }).Length <= r.GroupRadius);
            int size = Math.Max(group + 1, Math.Min(a.Pack[1], a.Pack[0] + l.Hunts));
            double k = a.Pack[1] > a.Pack[0] ? Math.Min(1, (double)l.Hunts / (a.Pack[1] - a.Pack[0])) : 1;
            double out_ = a.SpawnOut[0] + (a.SpawnOut[1] - a.SpawnOut[0]) * k;
            if (HuntSpot(world, at, hint, out_) is not { } spot)
                continue;
            int pack = world.NextEnemyId;
            for (int i = 0; i < size; i++)
            {
                var p = spot + new Double3(i * 1.2, 0, (i % 2) * 1.2);
                world.AddEnemy(e => Ribbit.At(e, pack, p, r));
            }
            // From the hunt numbered gauntFrom on, a Gaunt woken on them comes with it (note 296): one at a time each, never
            // where the crew's too small for one or the night bans it.
            int gaunt = -1;
            var g = et.Gaunt;
            if (a.GauntFrom >= 0 && l.Hunts >= a.GauntFrom && Allows(EnemyKind.Gaunt) && crew.Count(c => c.State.Alive) >= g.MinCrew
                && !active.Any(e => e is Gaunt { Gone: false } w && w.Waker == id)
                && HuntSpot(world, at, hint, g.SpawnOut) is { } far)
            {
                gaunt = world.NextEnemyId;
                world.AddEnemy(e => Gaunt.WokenBy(e, far, id, g));
            }
            l.Pack = pack;
            l.Hunts++;
            l.Pressure = Math.Max(0, l.Pressure - a.Relief);
            Hunts.Add(new Hunt(world.Tick, id, pack, size, l.Seconds, behind, gaunt));
        }
        foreach (int id in _left.Keys.Where(k => !seen.Contains(k)).ToList())
            _left.Remove(id);
    }

    /// <summary>Where a hunt is put down: <paramref name="distance"/> from them, off the line's side they're on, on the ground, out of the forts.</summary>
    Double3? HuntSpot(World world, Double3 at, double along, double distance)
    {
        var line = world.Train.Line;
        var rail = line.Sample(Rail.RailLine.MainPath, along);
        var right = Double3.Cross(rail.Tangent, Double3.Up).Normalized;
        double side = Double3.Dot(at - rail.Position, right) >= 0 ? 1 : -1;
        double jitter = _huntRng.Range(-0.8, 0.8);
        foreach (double s in new[] { side, -side })
        {
            var dir = (right * s + rail.Tangent * jitter).Normalized;
            var spot = at + dir * distance;
            double hint = along;
            spot = spot with { Y = PlayerMotor.GroundAt(spot, line, ref hint) };
            if (!world.InFort(spot))
                return spot;
        }
        return null;
    }

    /// <summary>A threat engaged with the crew now: telegraphing, committing, grabbing or punishing (note 266).</summary>
    public static bool Confronting(Enemy e) => !e.Gone && !e.Hazard
        && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;

    /// <summary>
    /// App. B.1's caps are on what's active: a Dragger lying dormant under a car's edge all night, a Rattle waiting in its
    /// gap, or a Lamplighter that's lost the light and only lingers, isn't pressure (the playtest found them holding the
    /// caps full, and the night went quiet).
    /// </summary>
    public static bool Engaged(Enemy e) => !e.Gone && !e.Hazard
        && (e.Phase is SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish
            // Dormant but on the move is pressure too (a Climber pacing the train); only what lies in wait isn't.
            || e.Phase == SpinePhase.Dormant && e.Kind is not (EnemyKind.Dragger or EnemyKind.Whistler or EnemyKind.CarHugger or EnemyKind.Gaunt or EnemyKind.TippyToesie
                // A grazing Moose (note 323) only waits to be bothered.
                or EnemyKind.Moose));

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

    public static Want WantOf(EnemyKind kind) => Profiles.TryGetValue(kind, out var p) ? p.Want : Want.Kill;

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
                EnemyKind.Moose => new Moose(0),
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
        // D.11's payoff for the dead: a creature they voted for is coming; the host tells the dead (and only them) who called it.
        if (VotersFor(kind) is { Count: > 0 } voters)
            _cues.Add((kind, voters));
        _spent += Cost(kind);
        // Spending relieves the pressure (note 266): the trough after a spawn is the pressure building again, and the quiet
        // counts again from the threat's coming.
        _pressure = Math.Max(0, _pressure - _t.Pressure.ReliefPerCost * Cost(kind));
        _sinceThreat = 0;
        _quietMetres = 0;
        string want = WantOf(kind).ToString().ToLowerInvariant();
        _spentByWant[want] = _spentByWant.GetValueOrDefault(want) + Cost(kind);
        if (Completes(kind, world, active) is { } pair)
            Pairs.Add(pair);
        var zone = Profile(kind).Zone;
        Log.Add(new DirectorSpawn(world.Tick, kind, Cost(kind), world.Train.Dynamics.Distance,
            active.Count(e => Engaged(e) && e.Zone == zone) + 1, active.Count(Engaged) + 1, paced));
    }

    // GDD v1.4 App. D.11, the creature vote (note 180): host-side, by player id in order, so every choice is deterministic.
    readonly SortedDictionary<int, (EnemyKind[] Options, EnemyKind? Cast)> _ballots = [];
    readonly List<(EnemyKind Kind, IReadOnlyList<int> Voters)> _cues = [];

    /// <summary>
    /// A dead player's ballot (D.11): drawn once, the first time they're offered it, by weighted roll from the creatures the
    /// director could send now (allowed, with room, its spawn rule wanting it; the Stoker and the Choir aren't in the table
    /// at all). Kept for the run: an unused vote carries over to a later death.
    /// </summary>
    public IReadOnlyList<EnemyKind> Ballot(World world, int player)
    {
        if (_ballots.TryGetValue(player, out var ballot))
            return ballot.Options;
        var eligible = new List<(EnemyKind Kind, double Weight)>();
        if (world.Enemies is { } et)
        {
            var ctx = new SpawnContext(world, et, this);
            foreach (var rule in Spawns.Rules)
                if (Allows(rule.Kind) && rule.Weight(ctx) is > 0 and var w)
                    eligible.Add((rule.Kind, w));
        }
        var rng = new Pcg32(_seed ^ (ulong)(player + 1) * 0x9E3779B97F4A7C15UL, 0xB0A7);
        var options = new List<EnemyKind>();
        while (options.Count < _t.Vote.Options && eligible.Count > 0)
        {
            double pick = rng.NextDouble() * eligible.Sum(e => e.Weight);
            int i = 0;
            for (; i < eligible.Count - 1 && pick >= eligible[i].Weight; i++)
                pick -= eligible[i].Weight;
            options.Add(eligible[i].Kind);
            eligible.RemoveAt(i);
        }
        _ballots[player] = ([.. options], null);
        return options;
    }

    /// <summary>Whether <paramref name="player"/> still has their vote (offered or not yet: once per run, locked on submit).</summary>
    public bool CanVote(int player) => !_ballots.TryGetValue(player, out var b) || b.Cast is null;

    /// <summary>What <paramref name="player"/> voted for, if they have.</summary>
    public EnemyKind? VoteOf(int player) => _ballots.TryGetValue(player, out var b) ? b.Cast : null;

    /// <summary>Casts <paramref name="player"/>'s vote for one of their ballot's creatures; false if it isn't on it or they've voted.</summary>
    public bool Vote(int player, EnemyKind kind)
    {
        if (!_ballots.TryGetValue(player, out var b) || b.Cast is not null || !b.Options.Contains(kind))
            return false;
        _ballots[player] = (b.Options, kind);
        return true;
    }

    /// <summary>Who voted for <paramref name="kind"/>, in id order.</summary>
    public IReadOnlyList<int> VotersFor(EnemyKind kind) => [.. _ballots.Where(b => b.Value.Cast == kind).Select(b => b.Key)];

    /// <summary>Every vote cast, by voter.</summary>
    public IEnumerable<(int Voter, EnemyKind Kind)> Votes => _ballots.Where(b => b.Value.Cast is not null).Select(b => (b.Key, b.Value.Cast!.Value));

    /// <summary>The votes' weight on a creature (D.11): ×perVote each, to at most ×cap.</summary>
    public double VoteWeight(EnemyKind kind) => Math.Min(_t.Vote.Cap, DMath.Pow(_t.Vote.PerVote, VotersFor(kind).Count));

    /// <summary>The voted creatures that have spawned since last asked (for the dead's cue), oldest first.</summary>
    public List<(EnemyKind Kind, IReadOnlyList<int> Voters)> TakeVoteCues()
    {
        var cues = _cues.ToList();
        _cues.Clear();
        return cues;
    }

    /// <summary>
    /// D.11 "the multiplier applies within the creature's want tag, so the target shares still hold": each want's options are
    /// weighted by their votes, then scaled back to the want's own total, so the vote moves weight between creatures of a want
    /// and never between wants.
    /// </summary>
    public List<(EnemyKind Kind, double Weight)> WeighVotes(List<(EnemyKind Kind, double Weight)> options)
    {
        if (_ballots.Count == 0)
            return options;
        var result = options.ToList();
        foreach (var want in options.Select(o => WantOf(o.Kind)).Distinct().ToList())
        {
            var idx = Enumerable.Range(0, result.Count).Where(i => WantOf(result[i].Kind) == want).ToList();
            double before = idx.Sum(i => result[i].Weight);
            foreach (int i in idx)
                result[i] = (result[i].Kind, result[i].Weight * VoteWeight(result[i].Kind));
            double after = idx.Sum(i => result[i].Weight);
            if (after > 0)
                foreach (int i in idx)
                    result[i] = (result[i].Kind, result[i].Weight * before / after);
        }
        return result;
    }

    /// <summary>This edition has the kind (<see cref="DirectorTuning.Roster"/>, empty for every kind).</summary>
    public bool Allows(EnemyKind kind) => _t.Roster.Length == 0 || _t.Roster.Contains(Key(kind));

    /// <summary>What's been spent on each want (App. B.1), for the harness's want-balance check.</summary>
    public IReadOnlyDictionary<string, double> SpentByWant => _spentByWant;

    public Pcg32 Rng => _rng;
    public double NextRange(double min, double max) => _rng.Range(min, max);
}
