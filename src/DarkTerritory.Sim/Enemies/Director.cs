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
/// <param name="Afoot">The crew-afoot term, per second (note 327).</param>
public readonly record struct PressureTerms(double Rate, double Escalation, double Quiet, double Loud, double Cargo, double Relief, double Conditions, double Afoot = 0);

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
        _budgetBeforeCrew = baseBudget * lengthMultiplier;
        Crew = crew;
        Active = crew;
        _startCrew = crew;
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

    readonly double _budgetBeforeCrew;
    readonly int _startCrew;

    /// <summary>
    /// App. B.1's run budget: base × length × crew multiplier. With <see cref="OrchestratorTuning.LiveCrew"/> (note 336;
    /// orchestrator.md §3.2 1) the crew is <see cref="Active"/>, the crew alive now, so a crew that's lost players spends like
    /// the smaller crew it is; off, the crew the night started with (App. B.1 as written).
    /// </summary>
    public double Budget => _budgetBeforeCrew * Math.Min(_t.CrewCap, _t.CrewBase + _t.CrewPerPlayer * (_t.Orchestrator.LiveCrew ? Active : _startCrew));

    /// <summary>
    /// The crew active now (note 336; orchestrator.md §3.1 1): alive in the night, as of the director's last second. Until
    /// anyone's been seen, the crew the night was planned for.
    /// </summary>
    public int Active { get; private set; }

    /// <summary>The orchestrator's census of posts and slack (note 345), as of the director's last second.</summary>
    public Census Posts { get; } = new();

    /// <summary>
    /// The live census, once a second (notes 336, 345): the crew alive this second (none seen leaves it as it was); and each
    /// one's post, the threats on them and their slack, which counts only on the run between stops: the train on the move,
    /// past the grace, out of the forts and the facilities, short of the final approach and the terminus' safe stretch.
    /// </summary>
    public void Count(World world, double elapsed, IReadOnlyList<Enemy> active, double noSpawnFinal)
    {
        if (world.CrewThisTick.Count > 0)
            Active = world.CrewThisTick.Count(c => c.State.Alive);
        // Counted whenever the orchestrator's on (the harness reads it); census steers only with its own flag (Steering).
        if (!_t.Orchestrator.On)
            return;
        double front = world.Train.Dynamics.Distance;
        bool home = _route is { } route && (front > route.RunLength - noSpawnFinal || route.Plan?.Director.TagsAt(front).Contains("terminus_safe") == true);
        // Not at a facility either: the stop's work is the crew's to answer there (App. F.1: "slowing opens the doors"; the stops
        // are the heightened part of the night), and the census is for the run between them (App. F.3).
        // And only with the train on the move: a generated stop keeps the run under way while the train stands to be worked.
        bool counting = elapsed >= Grace && !world.TrainInFort && !home && world.Run?.Phase is null or Run.RunPhase.Underway
            && Math.Abs(world.Train.Dynamics.Velocity) > _t.Orchestrator.DrivingAbove;
        Posts.Count(world, active, _t.Orchestrator, _t.Abandoned.BehindM, counting);
    }

    /// <summary>
    /// The census at work (note 345): it counts, and the crew it counted is big enough for it to steer (a crew of one is App.
    /// B.1's). The crew it saw, not the crew planned: a night with nobody aboard yet has no posts to steer by.
    /// </summary>
    bool Steering => _t.Orchestrator is { On: true, Census: true } o && Posts.Entries.Count >= o.MinCrew;

    /// <summary>
    /// The most threats engaged at once (App. B.1's hard caps; note 336, orchestrator.md §3.2 3, 5): the flat 4 (crew ≤ 4) or
    /// 6, and, with the orchestrator on, no more than ceil(active × engagedPerActive) nor perPlayer a player active, and at
    /// least one while anyone is: a crew of one meets one thing at a time, a crew of two two.
    /// </summary>
    public int EngagedCap
    {
        get
        {
            var o = _t.Orchestrator;
            if (!o.On)
                return MaxConcurrent;
            int byCrew = Math.Min((int)Math.Ceiling(Active * o.EngagedPerActive - 1e-9), (int)Math.Floor(Active * o.PerPlayer + 1e-9));
            return Math.Min(MaxConcurrent, Math.Max(1, byCrew));
        }
    }
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
        if (_route is null || distance < _route.RunLength * _t.SaveFrom || world.Enemies is not { } et)
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
        double first = facilities.Count > 0 ? facilities[0].Start : _route.RunLength * 0.25;
        double last = facilities.Count > 0 ? facilities[^1].End : _route.RunLength * 0.75;
        var s = _t.PhaseShares;
        double share = distance < first ? s[0] * distance / first
            : distance < last ? s[0] + s[1] * (distance - first) / Math.Max(1, last - first)
            : s[0] + s[1] + s[2] * (distance - last) / Math.Max(1, _route.RunLength - last);
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
        if (_route is not null && s > _route.RunLength - noSpawnFinal)
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
        // Slack presses (note 345; orchestrator.md §3.2 4): each crewmate who's had nothing to answer for slackPress seconds
        // adds slackPerSecond, on top of the pressure's own terms, so a crew with several people idle fills faster.
        if (Steering)
            _pressure = Math.Min(_t.Pressure.Max, _pressure + _t.Orchestrator.SlackPerSecond * Posts.Entries.Count(c => c.Slack >= _t.Orchestrator.SlackPress));
        // The night's first threat is drawn (note 287): it answers what the crew did. A draw past answerAt is answered out loud at
        // once (the World puts the answer where it's heard), and the threat follows leadSeconds on, pressed for; with no draw that
        // big the pressure waits (listening) up to holdUntil, and then the biggest draw standing takes it. A quiet crew buys
        // itself time (GDD §14: "sometimes the correct response to a monster is to go quiet").
        var draw = _t.Draw;
        bool answering = false;
        if (draw.Enabled && First is null)
        {
            if (_answer is null && Draws.Top is var top && top.Amount >= draw.AnswerAt)
            {
                _answer = (top.Cause, top.Actor, top.Amount, world.Run?.Seconds ?? elapsed);
                _answerIn = draw.LeadSeconds;
                _heard.Add((top.Cause, top.Actor));
                return Held("answering");
            }
            if (_answer is not null)
            {
                if ((_answerIn -= 1) > 0)
                    return Held("answering");
                _pressure = Math.Max(_pressure, _t.Pressure.Threshold);
                answering = true;
            }
            else if (draw.HoldFirst && _pressure >= _t.Pressure.Threshold && _pressure < draw.HoldUntil)
                return Held("listening");
        }
        // Past the threshold the director spends; well past it (pressed: a long quiet, or a night the budget's curve can't keep
        // up with) the cooldown gives way, and the curve may be overdrawn by up to pacedCost. The caps and each kind's gates
        // still hold. An answered draw is pressed for: the dark said it was coming.
        bool due = _pressure >= _t.Pressure.PressAt || answering;
        if (_pressure < _t.Pressure.Threshold)
            return Held("building");
        if (_cooldown > 0 && !due)
            return Held("cooldown");
        // The hound run's runners count: they're engaged with the crew like anything else (note 336).
        int total = active.Count(Engaged);
        if (total >= EngagedCap)
            return Held("at the cap");
        double available = Allowance(s) - _spent;
        var options = new List<(EnemyKind Kind, double Weight)>();
        if (world.Enemies is { } et)
        {
            var ctx = new SpawnContext(world, et, this);
            foreach (var rule in Spawns.Rules)
            {
                // Killed by the crew together tonight (note 288): it's done for the night.
                if (!Allows(rule.Kind) || world.Slain.Contains(rule.Kind) || !Room(rule.Kind, active) || rule.Weight(ctx) is not { } w || w <= 0)
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
        // The first threat answers its draw (note 287): what that draw calls comes on more (the whistle the Whistler, a lamp the
        // Fire Flies, the cargo the Car Hugger).
        if (draw.Enabled && First is null
            && draw.Answers.GetValueOrDefault(DrawLedger.Key(_answer?.Cause ?? Draws.Top.Cause)) is { } calls)
            for (int i = 0; i < options.Count; i++)
                options[i] = (options[i].Kind, options[i].Weight * calls.GetValueOrDefault(Key(options[i].Kind), 1));
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
        // Note 327: with crew afoot off the train, what comes for them out there (the outside creatures, and whatever lives at
        // the stops' sites) weighs more, by their share: more of the night is met off the train.
        if (_t.Afoot.On && AfootShare > 0 && _t.Afoot.OutsideWeight != 1)
            options = [.. options.Select(o => (o.Kind, Outdoors(o.Kind) ? o.Weight * (1 + (_t.Afoot.OutsideWeight - 1) * AfootShare) : o.Weight))];
        // Everything that could come is for someone who has one on them already (note 345): nothing, this second. Only when
        // the census is what zeroed them: a list weighing nothing for other reasons is App. B.1's as it was (its last pick).
        bool weighed = options.Any(op => op.Weight > 0);
        options = WhoseNext(options);
        if (weighed && options.All(op => op.Weight <= 0))
            return Held("one on each");
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

    /// <summary>The crewmate the census would send the next threat to (note 345): the free one with the most slack, past minSlack.</summary>
    public CensusEntry? Next => Steering
        ? Posts.Entries.Where(c => c.On == 0 && c.Post != Post.LeftBehind && c.Slack >= _t.Orchestrator.MinSlack).OrderByDescending(c => c.Slack).ThenBy(c => c.Player).FirstOrDefault()
        : null;

    /// <summary>
    /// Who's next (note 345; orchestrator.md §3.2 2, 3): a kind answered at the post of the free crewmate with the most slack
    /// weighs ×slackWeight; a kind answered only at posts nobody holds, ×emptyPostWeight (but for those whose rule is an empty
    /// post: the Track Doll's cab). And one threat on any one player (perTarget): a kind whose every answering crewmate already
    /// has one on them weighs nothing. (With the engaged cap at ceil(0.75 × active) the crew as a whole is never all taken
    /// before the cap holds, so the rule is per post, not a hold.) Kinds with no answer post in the tuning are left as they are.
    /// </summary>
    public List<(EnemyKind Kind, double Weight)> WhoseNext(List<(EnemyKind Kind, double Weight)> options)
    {
        if (!Steering)
            return options;
        var o = _t.Orchestrator;
        var entries = Posts.Entries;
        var held = entries.Select(c => c.Post).ToHashSet();
        var next = Next;
        return [.. options.Select(op =>
        {
            var posts = Census.AnswerPosts(o, op.Kind).ToList();
            if (posts.Count == 0)
                return op;
            if (o.PerTarget && entries.Where(c => posts.Contains(c.Post)).ToList() is { Count: > 0 } answering && answering.All(c => c.On > 0))
                return (op.Kind, 0.0);
            if (next is not null && posts.Contains(next.Post))
                return (op.Kind, op.Weight * o.SlackWeight);
            if (!posts.Any(held.Contains) && !o.EmptyPostExempt.Contains(Key(op.Kind)))
                return (op.Kind, op.Weight * o.EmptyPostWeight);
            return op;
        })];
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
            : Math.Max(s / Math.Max(1, _route.RunLength), _route.DawnSeconds > 0 ? elapsed / _route.DawnSeconds : 0);
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
        // Note 327 (App. F.3): the crew off the train, on foot, draw it on: the share of the living afoot (Watch, this second).
        double afoot = _t.Afoot.On ? _t.Afoot.PerSecond * AfootShare : 0;
        double rate = _tierRate * conditions * relief * busy * escalation * (p.BasePerSecond + quiet + loud + cargo + afoot);
        _pressure = Math.Min(p.Max, _pressure + rate);
        Terms = new PressureTerms(rate, escalation, _sinceThreat, loud, cargo, relief, conditions, afoot);
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

    /// <summary>The share of the living crew afoot off the train this second (note 327; <see cref="Watch"/>).</summary>
    public double AfootShare { get; private set; }

    /// <summary>Crew-seconds afoot off the train tonight (note 327), for the harness and the playthrough.</summary>
    public double AfootSeconds { get; private set; }

    /// <summary>Every sign shown tonight (note 327): when, to whom, what, and whether it came from a site at the stop.</summary>
    public List<SignShown> Signs { get; } = new();

    /// <summary>The director's spawns made while someone was afoot off the train (note 327).</summary>
    public int SpawnsAfoot { get; private set; }

    double _signIn = -1;
    int _signTurn;
    Pcg32 _signRng;
    bool _signSeeded;

    /// <summary>
    /// Note 327 (GDD App. F.3, the director, 7 Oct 2026: "when they leave, there is this presence of threat at all times").
    /// Once a second, out of the safe yard: who's afoot (alive on the ground, further than <see cref="AfootTuning.FromTrainM"/>
    /// from every car, outside the forts), their share of the living (the pressure model's afoot term), and while anyone is,
    /// now and then a sign shown one of them in turn: eyes at the lamp's edge toward a site of a creature that lives at this
    /// stop (note 309), and its sound. Honest: only what lives here and the night could send (a Ribbit's, from away from the
    /// train, where no site is in reach). Its own dice, so the night's draws are as they were. Null: no sign this second.
    /// </summary>
    public Watcher? Watch(World world)
    {
        var a = _t.Afoot;
        AfootShare = 0;
        if (!a.On)
            return null;
        if (!_signSeeded)
        {
            _signRng = new Pcg32(_seed, 0x5167);
            _signSeeded = true;
        }
        var train = world.Train;
        var afoot = new List<(int Id, Double3 At)>();
        int alive = 0;
        foreach (var (id, s) in world.CrewThisTick)
        {
            if (!s.Alive)
                continue;
            alive++;
            if (s.Parent != PlayerState.World)
                continue;
            var at = PlayerMotor.WorldPosition(s, train);
            if (FromTrain(train, at) > a.FromTrainM && !world.InFort(at))
                afoot.Add((id, at));
        }
        if (afoot.Count == 0)
        {
            _signIn = -1;
            return null;
        }
        AfootShare = (double)afoot.Count / Math.Max(1, alive);
        AfootSeconds += afoot.Count;
        // The first sign soon after someone steps off; then every so often while anyone's out.
        if (_signIn < 0)
            _signIn = a.FirstSign;
        if ((_signIn -= 1) > 0)
            return null;
        _signIn = _signRng.Range(a.SignEvery[0], a.SignEvery[1]);
        var (who, from) = afoot[_signTurn++ % afoot.Count];
        // The sites of what lives here, in reach of them, of what the night could send: the nearest.
        // Each kind's nearest site in reach; one kind drawn from them, so a village with a warren, a roost and a child's call
        // shows all three in time, not only the nearest over and over.
        var near = new List<(Double3 At, EnemyKind Kind, double D, double Radius)>();
        foreach (var lair in Enum.GetValues<Stops.LairKind>())
        {
            var kind = Lives(lair);
            if (!Allows(kind))
                continue;
            (Double3 At, double D, double Radius)? nearest = null;
            foreach (var (at, radius) in CreatureSites.Of(world, lair, world.Enemies?.Sites.Around ?? 300))
            {
                double d = ((at - from) with { Y = 0 }).Length;
                if (d < a.SignReach && (nearest is null || d < nearest.Value.D))
                    nearest = (at, d, radius);
            }
            if (nearest is { } n)
                near.Add((n.At, kind, n.D, n.Radius));
        }
        (Double3 At, EnemyKind Kind)? site = null;
        double best = a.SignReach, edge = 0;
        if (near.Count > 0)
        {
            var pick = near[(int)Math.Min(near.Count - 1, _signRng.NextDouble() * near.Count)];
            site = (pick.At, pick.Kind);
            best = pick.D;
            edge = pick.Radius;
        }
        Double3 toward;
        EnemyKind what;
        if (site is { } st)
        {
            toward = (st.At - from) with { Y = 0 };
            what = st.Kind;
        }
        else if (Allows(EnemyKind.Ribbit))
        {
            // Away from the train: out where nobody's looking.
            double hint = train.Dynamics.Distance;
            train.Line.Nearest(from, ref hint);
            toward = (from - train.Line.Sample(Rail.RailLine.MainPath, hint).Position) with { Y = 0 };
            what = EnemyKind.Ribbit;
        }
        else
            return null;
        if (toward.Length < 1e-3)
            toward = new Double3(1, 0, 0);
        double turn = _signRng.Range(-a.SignSpread, a.SignSpread);
        // At the lamp's edge, or at the site's edge if that's nearer: outside a roost or a warren, never in it (a Gaunt's roost is
        // a house the art draws whether or not the sim walls it).
        double out_ = Math.Min(_signRng.Range(a.SignOut[0], a.SignOut[1]), site is null ? double.MaxValue : best - edge - 1);
        // Seen, not through a wall: the bearing drawn, then others across the spread, the first with a clear line out that far
        // past the stop's walls; with none, the drawn one, as far as its first wall (something at the corner of a house).
        Double3 spot = default;
        double clearest = -1;
        foreach (double deg in new[] { turn, 0, -a.SignSpread, a.SignSpread, -a.SignSpread / 2, a.SignSpread / 2 })
        {
            var dir = Turned(toward.Normalized, deg * Math.PI / 180);
            double clear = Clear(world, from, dir, out_);
            if (clear > clearest)
                (spot, clearest) = (from + dir * clear, clear);
            if (clear >= out_)
                break;
        }
        out_ = clearest;
        // Walled in close all round: nothing's seen this time (eyes a step away would be a lie).
        if (out_ < a.SignOut[0] * 0.5)
            return null;
        double gh = train.Dynamics.Distance;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref gh) + a.SignHeight.GetValueOrDefault(Key(what), a.SignHeightDefault) };
        if (world.InFort(spot))
            return null;
        Signs.Add(new SignShown(world.Tick, who, what, site is not null, out_));
        return new Watcher(a.SignSeconds, what, spot, who);
    }

    static Double3 Turned(Double3 d, double rad) =>
        new(d.X * DMath.Cos(rad) - d.Z * DMath.Sin(rad), 0, d.X * DMath.Sin(rad) + d.Z * DMath.Cos(rad));

    /// <summary>
    /// How far out along <paramref name="dir"/> from <paramref name="from"/> is clear, to <paramref name="out_"/>: a metre short of
    /// the first of the stop's walls, or of a building's footprint (an open house's door gap isn't a way through it, and the art
    /// draws buildings the sim doesn't wall).
    /// </summary>
    static double Clear(World world, Double3 from, Double3 dir, double out_)
    {
        var walls = world.Train.Walls;
        for (double d = 1; d <= out_; d += 1)
        {
            var p = from + dir * d + Double3.Up * 1;
            if (walls is not null)
                foreach (var w in walls.Near(p))
                {
                    var l = w.ToLocal(p);
                    if (Math.Abs(l.X) <= w.HalfLength + 0.3 && Math.Abs(l.Z) <= w.HalfWidth + 0.3 && l.Y >= w.Bottom && l.Y <= w.Top)
                        return Math.Max(1, d - 1);
                }
            if (d >= 2 && InBuilding(world, p))
                return Math.Max(1, d - 1);
        }
        return out_;
    }

    /// <summary>Whether a world point is inside the footprint of a stop's building (in the stop's rail frame, S along and D out).</summary>
    static bool InBuilding(World world, Double3 p)
    {
        if (world.Route is not { } route)
            return false;
        var line = world.Train.Line;
        double h = world.Train.Dynamics.Distance;
        line.Nearest(p, ref h);
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || h < f.Start - 50 || h > f.End + 50)
                continue;
            var r = line.Sample(f.Start + (h - f.Start));
            var right = Double3.Cross(r.Tangent, Double3.Up).Normalized;
            double ps = h - f.Start, pd = Double3.Dot(p - r.Position, right);
            foreach (var b in stop.Buildings)
            {
                double c = DMath.Cos(b.Yaw), sn = DMath.Sin(b.Yaw), ds = ps - b.S, dd = pd - b.D;
                // Axis u = (cos, sin), across v = (−sin, cos), in (S, D), as StopWalls.Doorstep has it.
                double x = ds * c + dd * sn, y = -ds * sn + dd * c;
                if (Math.Abs(x) <= b.Length / 2 + 0.5 && Math.Abs(y) <= b.Width / 2 + 0.5)
                    return true;
            }
        }
        return false;
    }

    /// <summary>A creature met on foot off the train (note 327): the outside zone's, or one that lives at the stops' sites.</summary>
    public static bool Outdoors(EnemyKind kind) =>
        Profile(kind).Zone == PressureZone.Outside || Enum.GetValues<Stops.LairKind>().Any(l => Lives(l) == kind);

    /// <summary>What lives at a site of this kind (level-design H.2; note 309).</summary>
    public static EnemyKind Lives(Stops.LairKind lair) => lair switch
    {
        Stops.LairKind.Warren => EnemyKind.Ribbit,
        Stops.LairKind.GauntRoost => EnemyKind.Gaunt,
        Stops.LairKind.FollowerGround => EnemyKind.Follower,
        Stops.LairKind.SootCall => EnemyKind.SootChildren,
        Stops.LairKind.GrumblerPerch => EnemyKind.Grumbler,
        _ => EnemyKind.Whistler,
    };

    /// <summary>How far a world point is from the train, flat: to the nearest car's footprint.</summary>
    public static double FromTrain(TrainOnLine train, Double3 at)
    {
        double best = double.MaxValue;
        foreach (var f in train.Frames)
        {
            var l = f.ToLocal(at);
            var b = f.Shape.Bounds;
            double dx = Math.Max(0, Math.Max(b.Min.X - l.X, l.X - b.Max.X)), dz = Math.Max(0, Math.Max(b.Min.Z - l.Z, l.Z - b.Max.Z));
            best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }

    /// <summary>A hound run sent (note 328): when, how many in all, the crew alive it was sized to, and whether the boiler's heat drew it.</summary>
    public readonly record struct HoundRun(uint Tick, int Pack, int Size, int Active, bool Hot, double TrainSpeed, double Distance);

    /// <summary>How many of tonight's runs' pairs came from ahead, for the forward gun (note 405).</summary>
    public int AheadPairs => _aheadSent;
    int _aheadSent;
    readonly SortedDictionary<int, int> _aheadRunners = [];

    /// <summary>How many of a run's runners came from ahead (note 405).</summary>
    public int AheadRunners(int pack) => _aheadRunners.GetValueOrDefault(pack);

    /// <summary>How many of tonight's runs' pairs came in from the flanks (note 418), and of a run's runners.</summary>
    public int FlankPairs => _flankSent;
    int _flankSent;
    readonly SortedDictionary<int, int> _flankRunners = [];
    public int FlankRunners(int pack) => _flankRunners.GetValueOrDefault(pack);

    /// <summary>How many of tonight's flank pairs came in abeam the engine's gun, for the forward gun (note 443).</summary>
    public int FlankEnginePairs => _flankEngineSent;
    int _flankEngineSent;
    readonly SortedDictionary<int, int> _flankEngineRunners = [];
    public int FlankEngineRunners(int pack) => _flankEngineRunners.GetValueOrDefault(pack);

    /// <summary>
    /// Whether the country's open abeam the train at <paramref name="along"/> (note 418): the line's biome one of the flank
    /// lanes' (any, with no plan), and room that far out (no tunnel's bore or bridge's deck).
    /// </summary>
    bool Open(World w, HoundRunTuning rt, double along)
    {
        if (w.Train.Line.Conditions is { } land && land.LateralRoom(w.Train.Dynamics.Path, along) < rt.FlankOut)
            return false;
        if (_route?.Plan?.Biomes is not { Count: > 0 } biomes)
            return true;
        var here = biomes.FirstOrDefault(b => b.Edge == "main" && b.S0 <= along && along < b.S1) ?? biomes[^1];
        return rt.FlankBiomes.Contains(here.Biome);
    }

    /// <summary>Whether the engine's rake's last car carries a gun laid back (the guard van's): the flank lanes' (note 418).</summary>
    static bool RearGun(TrainOnLine train) =>
        train.Vehicles[train.Dynamics.Consist.Vehicles[^1].Id].Gun is { Mounted: true, Facing: > 0 };

    /// <summary>Whether the engine's rake carries a gun laid forward (the engine's own): the lane ahead's (note 405).</summary>
    static bool ForwardGun(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Any(v => train.Vehicles[v.Id].Gun is { Mounted: true, Facing: < 0 });

    /// <summary>
    /// Where along the line the engine's rake's gun laid forward stands (note 443): abeam it is the flank the forward gun has.
    /// Null without one.
    /// </summary>
    public static double? ForwardGunAlong(TrainOnLine train)
    {
        var consist = train.Dynamics.Consist;
        for (int i = 0; i < consist.Vehicles.Count; i++)
            if (train.Vehicles[consist.Vehicles[i].Id].Gun is { Mounted: true, Facing: < 0 } g)
                // The car's centre, then its gun's place on it (local −Z is forward, up the line).
                return train.Dynamics.Distance - consist.OffsetOf(i) - train.Frames[consist.Vehicles[i].Id].Shape.HalfLength - g.Z;
        return null;
    }

    /// <summary>Every hound run sent tonight (note 328).</summary>
    public List<HoundRun> HoundRuns { get; } = new();

    readonly SortedDictionary<int, (int Scattered, int Killed, int Boarded)> _runOutcomes = [];

    /// <summary>How a run's runners have ended so far (note 328): scattered by a ball landing near, killed by one, or aboard.</summary>
    public (int Scattered, int Killed, int Boarded) RunOutcome(int pack) => _runOutcomes.GetValueOrDefault(pack);

    /// <summary>A runner of <paramref name="pack"/> ended: <paramref name="how"/> 0 scattered, 1 killed, 2 aboard.</summary>
    public void RunnerEnded(int pack, int how)
    {
        var o = _runOutcomes.GetValueOrDefault(pack);
        _runOutcomes[pack] = how switch { 0 => o with { Scattered = o.Scattered + 1 }, 1 => o with { Killed = o.Killed + 1 }, _ => o with { Boarded = o.Boarded + 1 } };
    }

    /// <summary>The line run fast since the train last slowed, the last run or the grace (note 328).</summary>
    public double RunMetres => _runMetres;

    double _runMetres, _nextPair;
    int _runPack = -1, _pairsLeft, _runnersLeft, _pairsSent;
    Pcg32 _runRng;
    bool _runSeeded;

    /// <summary>
    /// The hound run (ARCHITECTURE §8 note 328; docs/design/orchestrator.md §5.3, §6.1; GDD App. F.3, the director, 7 Oct
    /// 2026: "more threats that can board the train at speed ... tower defense style that gives our gunners things to do").
    /// Once a second. A train that's run <see cref="HoundRunTuning.AfterMetres"/> fast since it last slowed (sooner with the
    /// boiler hot) draws a stream of Cinder Hounds faster than it, sized to the crew alive, in pairs a few seconds apart on
    /// alternating flanks; the guns answer them one at a time (<see cref="CinderHound.Runner"/>). Not from the budget, not on
    /// the caps: the fast train's own, as the left-behind's hunts are theirs. Never in the grace, a fort, the final approach
    /// or at a stop, one run at a time; its own dice, so the night's other draws are as they were.
    /// </summary>
    public void Runs(World world, double elapsed, IReadOnlyList<Enemy> active, double noSpawnFinal)
    {
        var r = _t.Run;
        if (!r.On || world.Enemies is not { } et || !Allows(EnemyKind.CinderHound))
            return;
        if (!_runSeeded)
        {
            _runRng = new Pcg32(_seed, 0x4A11);
            _runSeeded = true;
        }
        var train = world.Train;
        double speed = train.Dynamics.Speed, front = train.Dynamics.Distance;
        bool home = _route is { } route && (front > route.RunLength - noSpawnFinal || route.Plan?.Director.TagsAt(front).Contains("terminus_safe") == true);
        bool open = elapsed >= Grace && !world.TrainInFort && !home && train.Dynamics.Consist.CarCount >= 1
            && world.Run?.Phase is null or Run.RunPhase.Underway;
        if (!open || speed < r.StopSpeed)
        {
            // Slowed, stopped or somewhere nothing comes: the count starts again, and no more pairs of this run are sent
            // (what's already running keeps coming).
            _runMetres = 0;
            _pairsLeft = 0;
            return;
        }
        if (_pairsLeft > 0)
        {
            if (elapsed >= _nextPair)
                SendPair(world, et, r);
            return;
        }
        if (_runPack >= 0 && active.Any(e => e is CinderHound { Runner: true, Gone: false } h && h.Pack == _runPack))
            return; // one run at a time: the count starts once the last runner's dealt with
        // The run is one threat to the crew's caps (note 336): with the crew already at its cap, it waits (the count banked),
        // so a crew of one never meets a run and the director's threat at once.
        bool atCap = _t.Orchestrator.On && active.Count(Engaged) >= EngagedCap;
        if (speed >= r.FromSpeed)
            _runMetres += speed;
        bool hot = train.BoilerTuning is { } b && train.Boiler.Pressure > b.WorkingBandMax;
        if (_runMetres < r.AfterMetres * (hot ? r.HotShorter : 1) || atCap)
            return;
        int alive = Math.Max(1, world.CrewThisTick.Count(c => c.State.Alive));
        int size = Math.Clamp((int)Math.Round(r.Base + r.PerActive * alive, MidpointRounding.AwayFromZero), r.Size[0], r.Size[1]);
        _runMetres = 0;
        _runPack = world.NextEnemyId;
        _runnersLeft = size;
        _pairsLeft = (size + 1) / 2;
        _pairsSent = 0;
        HoundRuns.Add(new HoundRun(world.Tick, _runPack, size, alive, hot, speed, front));
        SendPair(world, et, r);

        void SendPair(World w, EnemyTuning t, HoundRunTuning rt)
        {
            var h = t.CinderHounds;
            double side = _pairsSent % 2 == 0 ? 1 : -1;
            int n = Math.Min(2, _runnersLeft);
            // The lane ahead (note 405): every aheadEvery-th pair from the second on (1: every pair), for a forward gun, from in front.
            bool ahead = rt.AheadEvery > 0 && _pairsSent % rt.AheadEvery == Math.Min(1, rt.AheadEvery - 1) && ForwardGun(w.Train);
            // The flank lanes (note 418): every flankEvery-th pair (the last of each), from the open country abeam the guard van's
            // gun: a gun traverses only so far round from its facing (traverseDegrees), so abeam its own car is the flank it has.
            // Every flankEngineEvery-th flank pair of the night (the second of each two) comes abeam the engine's gun instead
            // (note 443): the forward gun traverses as far round from ahead as the guard van's does from astern, so abeam the
            // engine is its flank, as abeam the guard van is the guard gun's. A train with no gun laid back has only that one.
            double? forward = rt.FlankEngineEvery > 0 ? ForwardGunAlong(w.Train) : null;
            bool rearGun = RearGun(w.Train);
            bool toEngine = forward is not null && (!rearGun || _flankSent % rt.FlankEngineEvery == rt.FlankEngineEvery - 1);
            double abeam = toEngine ? forward!.Value : w.Train.Dynamics.RearDistance + rt.FlankAbeam;
            bool flank = !ahead && rt.FlankEvery > 0 && _pairsSent % rt.FlankEvery == rt.FlankEvery - 1 && (toEngine || rearGun) && Open(w, rt, abeam);
            for (int i = 0; i < n; i++)
            {
                int k = i;
                double lateral = ahead
                    ? side * (k == 0 ? rt.AheadLateral[0] : rt.AheadLateral[1]) + _runRng.Range(-0.5, 0.5)
                    : flank ? side * (rt.FlankOut + k * 3) + _runRng.Range(-0.5, 0.5)
                    : side * (k == 0 ? rt.Lateral[0] : rt.Lateral[1]) + _runRng.Range(-0.5, 0.5);
                w.AddEnemy(id => new CinderHound(id, _runPack)
                {
                    Runner = true,
                    Ahead = ahead,
                    Flank = flank,
                    LineDistance = ahead ? w.Train.Dynamics.Distance + rt.AheadMetres + k * 3
                        : flank ? abeam - k * 3
                        : w.Train.Dynamics.RearDistance - rt.SpawnBehind - k * 3,
                    Lateral = lateral,
                    Height = 0.6,
                    Health = h.Health,
                });
            }
            if (ahead)
            {
                _aheadSent++;
                _aheadRunners[_runPack] = AheadRunners(_runPack) + n;
            }
            if (flank)
            {
                _flankSent++;
                _flankRunners[_runPack] = FlankRunners(_runPack) + n;
                if (toEngine)
                {
                    _flankEngineSent++;
                    _flankEngineRunners[_runPack] = FlankEngineRunners(_runPack) + n;
                }
            }
            _runnersLeft -= n;
            _pairsSent++;
            _pairsLeft--;
            _nextPair = elapsed + rt.Spacing;
        }
    }

    readonly HashSet<string> _trussesSeen = [];
    Pcg32 _dropRng;
    bool _dropSeeded;

    /// <summary>How many Draggers have been put on a truss tonight (note 435).</summary>
    public int TrussDraggers { get; private set; }

    /// <summary>
    /// The Gannet's passes tonight (note 454, for the bots' heed): how often it hung over a walker, folded on one, and stabbed
    /// one (a fold that found nobody under the beak is a miss). Host only, as its spine is.
    /// </summary>
    public (int Hangs, int Folds, int Stabs) GannetPasses { get; private set; }

    /// <summary>A Gannet hung over a walker (0), folded (1) or stabbed one (2).</summary>
    public void GannetPass(int what) => GannetPasses = what switch
    {
        0 => GannetPasses with { Hangs = GannetPasses.Hangs + 1 },
        1 => GannetPasses with { Folds = GannetPasses.Folds + 1 },
        _ => GannetPasses with { Stabs = GannetPasses.Stabs + 1 },
    };

    /// <summary>
    /// Draggers off a truss (note 435, orchestrator.md §5.2 S4): once a second, each through-truss on the main line coming up
    /// within <see cref="DraggerDropTuning.Ahead"/> m of a train at <see cref="DraggerDropTuning.FromSpeed"/> or more is
    /// rolled for once (<see cref="DraggerDropTuning.Chance"/>), and a Dragger perched on its top chord, mid-span, on a side.
    /// Not from the budget or on the caps (the line's, as the run is the fast train's); never in the grace, a fort or the
    /// final approach. Its own dice.
    /// </summary>
    public void Drops(World world, double elapsed, double noSpawnFinal)
    {
        var t = world.Enemies?.Draggers.Drop;
        if (t is not { On: true } || !Allows(EnemyKind.Dragger) || _route?.Plan is not { } plan)
            return;
        if (!_dropSeeded)
        {
            // The seed scrambled first: a PCG stream's first draw barely moves between neighbouring seeds (frontier:7's seeds
            // 1-3 all rolled 0.73 for its truss), and this one makes a single draw a crossing.
            _dropRng = new Pcg32(unchecked(_seed * 0x9E3779B97F4A7C15UL + 0xD209), 0xD209);
            _dropSeeded = true;
        }
        var train = world.Train;
        double front = train.Dynamics.Distance;
        if (elapsed < Grace || world.TrainInFort || train.Dynamics.Speed < t.FromSpeed || front > _route.RunLength - noSpawnFinal
            || train.Dynamics.Consist.CarCount < world.Enemies!.Draggers.MinCars)
            return;
        foreach (var st in plan.Structures)
        {
            if (st.Type != LineGen.StructureType.Truss || st.Edge != "main" || st.S0 - front > t.Ahead || st.S0 < front || !_trussesSeen.Add(st.Id))
                continue;
            if (_dropRng.NextDouble() >= t.Chance)
                continue;
            int side = _dropRng.NextDouble() < 0.5 ? -1 : 1;
            world.AddEnemy(id => Dragger.OnTruss(id, (st.S0 + st.S1) / 2, side, t));
            TrussDraggers++;
        }
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
        // A Gannet that's peeled off (note 340) isn't there to answer.
        && e is not Gannet { Mode: GannetMode.Away }
        // The Mourners (note 362) hunt nobody: they come for the dead, and cost the caps nothing.
        && e.Kind != EnemyKind.Mourners
        // Nor a Brakeman out of sight under the train (note 364), waiting to come up again.
        && e is not Brakeman { Mode: BrakemanMode.Hidden }
        && (e.Phase is SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish
            // Dormant but on the move is pressure too (a Climber pacing the train); only what lies in wait isn't.
            || e.Phase == SpinePhase.Dormant && e.Kind is not (EnemyKind.Dragger or EnemyKind.Whistler or EnemyKind.CarHugger or EnemyKind.Gaunt or EnemyKind.TippyToesie
                // A grazing Moose (note 339) only waits to be bothered.
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
                EnemyKind.Gannet => new Gannet(0),
                EnemyKind.Mourners => new Mourner(0),
                EnemyKind.TowerJaw => new TowerJaw(0),
                EnemyKind.FreightBeetle => new FreightBeetle(0),
                EnemyKind.Brakeman => new Brakeman(0),
                EnemyKind.Hotbox => new Hotbox(0),
                EnemyKind.Knotter => new Knotter(0),
                EnemyKind.Dave => new Dave(0),
                EnemyKind.Jacob => new Jacob(0),
                _ => new ChoirGhost(0),
            };
            d[kind] = (e.Zone, e.Sense, e.Want);
        }
        return d;
    }

    readonly Dictionary<string, double> _spentByWant = new();

    /// <summary>The night's draws (note 287), host-side: the World credits each act as it's made, and <see cref="Listen"/> the rest.</summary>
    public DrawLedger Draws { get; } = new();
    /// <summary>The night's first threat and what drew it (note 287), once it's come.</summary>
    public FirstThreat? First { get; private set; }
    (DrawCause Cause, int Actor, double Amount, double At)? _answer;
    double _answerIn;
    readonly List<(DrawCause Cause, int Actor)> _heard = [];
    readonly SortedDictionary<int, double> _loadsWere = [];
    bool _listened;

    /// <summary>The answers given since last asked (note 287): the World puts each where it's heard, for every machine.</summary>
    public List<(DrawCause Cause, int Actor)> TakeAnswers()
    {
        var heard = _heard.ToList();
        _heard.Clear();
        return heard;
    }

    /// <summary>
    /// Once a second, out of the safe yard (note 287): the draws that are a state rather than an act (the firebox held hot, by
    /// whoever tended it; the engine at speed, by whoever drove; cargo come aboard, by the crewmate nearest the car it came
    /// into), and the ledger's fade. The acts (the whistle, a round, voices, a toy, a lamp) the World credits as they happen.
    /// </summary>
    public void Listen(World world)
    {
        var draw = _t.Draw;
        if (!draw.Enabled)
            return;
        Draws.Fade(1, draw.HalfLifeSeconds);
        var train = world.Train;
        var a = world.Attribution;
        if (train.BoilerTuning is { FireboxCapacity: > 0 } bt && !train.Boiler.Ruptured && train.Boiler.Firebox > draw.FireboxFrom)
            Draws.Add(DrawCause.Firebox, a.Tender, draw.Weight(DrawCause.Firebox)
                * Math.Clamp((train.Boiler.Firebox - draw.FireboxFrom) / Math.Max(0.1, bt.FireboxCapacity - draw.FireboxFrom), 0, 1));
        if (draw.EngineFullSpeed > 0)
            Draws.Add(DrawCause.Engine, a.Driver, draw.Weight(DrawCause.Engine) * Math.Clamp(Math.Abs(train.Dynamics.Speed) / draw.EngineFullSpeed, 0, 1));
        // Cargo come aboard since last second: loaded at a stop, or a loaded car coupled on. What the train left the fortress
        // with isn't a draw (the first look only notes it).
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.Kind != VehicleKind.Cargo)
                continue;
            double loads = v.Cargo == CargoKind.None ? 0
                : v.Load * _t.Pressure.CargoValue.GetValueOrDefault(char.ToLowerInvariant(v.Cargo.ToString()[0]) + v.Cargo.ToString()[1..], 1);
            double were = _loadsWere.GetValueOrDefault(v.Id, _listened ? 0 : loads);
            _loadsWere[v.Id] = loads;
            if (loads > were + 1e-6 && v.Id < train.Frames.Count)
                Draws.Add(DrawCause.Cargo, Run.IncidentLog.Nearest(world, train.Frames[v.Id].Origin, world.CrewThisTick).Actor,
                    draw.Weight(DrawCause.Cargo) * (loads - were));
        }
        _listened = true;
    }

    /// <summary>
    /// Records the night's first threat (note 287), once: what came, what drew it and who, and the incident report's line (C.9:
    /// "Cinder Hounds came first at km 2. Drawn by the whistle: Dave."). The director writes the line; it never reads the log.
    /// </summary>
    public void Came(World world, EnemyKind kind, DrawCause cause, int actor, double amount, double answeredAt = -1)
    {
        if (First is not null)
            return;
        double seconds = world.Run?.Seconds ?? world.ElapsedSeconds;
        // Switched off, it's still noted when the first came (the harness's before and after), but nothing drew it.
        if (!_t.Draw.Enabled)
            (cause, actor, amount, answeredAt) = (DrawCause.None, -1, 0, -1);
        First = new FirstThreat(world.Tick, seconds, world.Train.Dynamics.Distance, kind, cause, actor, amount, answeredAt >= 0,
            answeredAt >= 0 ? answeredAt : seconds);
        if (!_t.Draw.Enabled)
            return;
        string by = cause == DrawCause.None ? "Drawn by nothing anyone did."
            : actor >= 0 ? $"Drawn by {DrawLedger.Said(cause)}: {{actor}}." : $"Drawn by {DrawLedger.Said(cause)}.";
        world.Attribution.Add(Run.IncidentLog.Event(world, Run.IncidentKind.Drawn, $"{Named(kind)} came first", actor, by));
    }

    /// <summary>A kind as the clerk names it: "The Track Doll", "Cinder Hounds", "The Fire Flies".</summary>
    static string Named(EnemyKind kind) => kind switch
    {
        EnemyKind.CinderHound => "Cinder Hounds",
        EnemyKind.Dragger => "Draggers",
        EnemyKind.Climber => "Climbers",
        EnemyKind.Follower => "Followers",
        EnemyKind.Ribbit => "The Ribbits",
        _ => $"The {Run.IncidentLog.Spoken(kind.ToString())}",
    };

    /// <summary>Condition-triggered enemies (the Stoker) cost budget only when they actually fire (App. B.5).</summary>
    public void Charge(World world, EnemyKind kind, IReadOnlyList<Enemy> active, bool paced = false)
    {
        // The night's first (note 287): the Stoker is the firebox's own answer (drawn by heat, note 263), whoever tended it last;
        // anything else, the draw that was answered, or the biggest standing.
        if (kind == EnemyKind.Stoker)
            Came(world, kind, DrawCause.Firebox, world.Attribution.Tender, Draws.Of(DrawCause.Firebox, world.Attribution.Tender));
        else if (_answer is { } answer)
            Came(world, kind, answer.Cause, answer.Actor, answer.Amount, answer.At);
        else
            Came(world, kind, Draws.Top.Cause, Draws.Top.Actor, Draws.Top.Amount);
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
        if (AfootShare > 0)
            SpawnsAfoot++;
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
