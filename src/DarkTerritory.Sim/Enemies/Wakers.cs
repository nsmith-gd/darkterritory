using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// THE WAKERS · what gets up at dawn (GDD §8; docs/design/creatures/wakers.md; ARCHITECTURE §8 note 588; the director, 9 Oct
/// 2026: "The gargantuan creatures, the picking up of the train, especially with you in it, you should experience that if
/// you're still playing"). The night is the crew's because the Wakers sleep through it; at dawn they rise behind the train and
/// come down the line after it, slow, then faster than any engine. One that reaches the last car takes it: the train's dragged
/// to a stop, lifted from the rear a car at a time with the crew in it, and eaten from the back to the engine. A train inside
/// the terminus's gate is safe: they stop short of the guns.
/// </summary>
/// <remarks>
/// Free on the engine's path (<see cref="Enemy.LineDistance"/>, <see cref="Enemy.Lateral"/> for the ones that rise to the
/// side), sent to every client (<see cref="Far"/>). <see cref="Enemy.Extra"/> is the seconds since it took the train (0 until
/// then): the lift and the eating are read from it, on clients too (<see cref="Lifted"/>, <see cref="EatenCars"/>).
/// <see cref="Enemy.Extra2"/> is which it is (0 the one on the line, which decides the catch; ±1 the ones off to the sides).
/// Its place is a function of the seconds since dawn and where the train's rear was then: no randomness, no pathing.
/// <para>
/// The ones that take whoever ran (note 710; the director, 14 Oct 2026: "It should capture players who escape the train and eat
/// them too"): while one holds the train, another rises out of the land beside each crewmate off it (<see cref="Beside"/>,
/// <see cref="Enemy.Extra2"/> <see cref="PickerMark"/>), loose (<see cref="Enemy.Local"/> its feet, <see cref="Enemy.Lateral"/>
/// its heading), its quarry's id its <see cref="Enemy.Extra"/> (−1 with none). Up (the telegraph), it strides to them, takes
/// them in its hand (the spine's grab), lifts them to its mouth and eats them; then the next, if anyone else ran.
/// </para>
/// </remarks>
public sealed class Waker(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Waker;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Movement;
    /// <summary>Not the director's to spend or count (the night's over); it ends the night on its own terms.</summary>
    public override bool Hazard => true;
    public override bool Far => true;
    public override string Called => "a Waker";

    /// <summary>The one on the line: its catch is the train's.</summary>
    public bool Lead => Extra2 == 0;
    /// <summary>It has the train (the lift and the eating, <see cref="Enemy.Extra"/> seconds in).</summary>
    public bool Holds => Phase == SpinePhase.Punish && !Picker;

    /// <summary>Its <see cref="Enemy.Extra2"/> as one risen beside a crewmate off the train (note 710).</summary>
    public const double PickerMark = 2;
    /// <summary>One risen beside a crewmate who ran (note 710): it takes them, not the train.</summary>
    public bool Picker => Extra2 == PickerMark;
    /// <summary>The crewmate a <see cref="Picker"/> is after (its <see cref="Enemy.Extra"/>), or −1.</summary>
    public int Quarry => Picker ? (int)Math.Round(Extra) : -1;

    /// <summary>
    /// The night's creatures are going to ground (<see cref="WakersTuning.NightFleesSeconds"/> or less to dawn, or past it),
    /// or the night's over stranded (note 661): the crew alive out on the line, waiting on first light's recovery, and the
    /// night done with them. The director's crew, stranded at Voss Green on 13 Oct, were "picked up by something, even though
    /// I'm stranded, but I'm not dead".
    /// </summary>
    public static bool NightFleeing(World world, WakersTuning t) => t.NightFleesSeconds >= 0 && world.Run is { } run
        && (run.DawnIn <= t.NightFleesSeconds || run.End == Sim.Run.RunEnd.Stranded);

    /// <summary>
    /// One of the night's creatures, which go to ground at dawn: not a hazard (a car fire, the Drift, the Sleepers, the Wakers
    /// themselves), nor the trouble in the cars, nor a person (Dave, Jacob), nor the Hollow House (note 585: it's the house
    /// itself, and can't run; one gone at dawn would stand up again out of its own wreck).
    /// </summary>
    public static bool NightThing(Enemy e) => !e.Hazard && e is not Incident && e.Kind is not (EnemyKind.Dave or EnemyKind.Jacob
        or EnemyKind.Waker or EnemyKind.CarFire or EnemyKind.Drift or EnemyKind.Sleepers or EnemyKind.HollowHouse);

    double _start, _since;

    /// <summary>
    /// One risen at dawn, <paramref name="behind"/> m behind the train's rear on its path, <paramref name="side"/> (−1, 0, +1)
    /// of the line.
    /// </summary>
    public static Waker Rise(int id, double rearAlong, int side, WakersTuning t) => new(id)
    {
        LineDistance = rearAlong - t.RiseBehind - Math.Abs(side) * t.AsideBack,
        Lateral = side * t.RiseAside,
        Extra2 = side,
        _start = rearAlong - t.RiseBehind - Math.Abs(side) * t.AsideBack,
    };

    /// <summary>
    /// One rising out of the land <c>pickAside</c> m beyond <paramref name="quarry"/> (out from the line from where they stand at
    /// <paramref name="at"/>), facing them (note 710).
    /// </summary>
    public static Waker Beside(int id, int quarry, Double3 at, Train.TrainOnLine train, WakersTuning t)
    {
        double hint = train.Dynamics.Distance;
        var (path, d) = train.Line.Nearest(at, ref hint);
        var near = train.Line.Sample(path, d);
        var out_ = (at - near.Position) with { Y = 0 };
        if (out_.Length < 1e-3)
            out_ = Double3.Cross(near.Tangent, Double3.Up) with { Y = 0 };
        var feet = at + out_.Normalized * t.PickAside;
        feet = feet with { Y = PlayerMotor.GroundAt(feet, train.Line, ref hint) };
        return new Waker(id)
        {
            Attached = Loose,
            Local = feet,
            LineDistance = hint,
            Lateral = Heading(at - feet),
            Extra = quarry,
            Extra2 = PickerMark,
        };
    }

    /// <summary>A heading toward <paramref name="way"/>, as a player's yaw (−Z forward).</summary>
    static double Heading(Double3 way) => DMath.Atan2(-way.X, -way.Z);

    /// <summary>
    /// Where a <see cref="Picker"/> holds what it took, up at its mouth (note 710): <c>pickMouthOut</c> m in front of its feet
    /// and <c>pickMouthHeight</c> m up. The art bows his mouth onto it (CreatureArt.WakerReach), as it does onto a car.
    /// </summary>
    public static Double3 Mouth(Double3 feet, double heading, WakersTuning t) =>
        feet + new Double3(-DMath.Sin(heading), 0, -DMath.Cos(heading)) * t.PickMouthOut + Double3.Up * t.PickMouthHeight;

    /// <summary>How far it's come in <paramref name="seconds"/> since it rose: <c>startSpeed</c> up to <c>topSpeed</c> over <c>rampSeconds</c>.</summary>
    public static double Covered(double seconds, WakersTuning t)
    {
        double ramp = Math.Max(1e-6, t.RampSeconds), up = Math.Min(seconds, ramp);
        double gain = (t.TopSpeed - t.StartSpeed) / ramp;
        return t.StartSpeed * up + 0.5 * gain * up * up + t.TopSpeed * Math.Max(0, seconds - ramp);
    }

    /// <summary>The cars lifted off the rails so far, from the rear (the presentation tilts and raises them).</summary>
    public int Lifted(WakersTuning t, int cars) => !Holds ? 0 : Math.Min(cars, 1 + (int)Math.Floor(Extra / Math.Max(1e-6, t.LiftSeconds)));
    /// <summary>The cars eaten so far, from the rear.</summary>
    public int EatenCars(WakersTuning t, int cars) =>
        !Holds ? 0 : Math.Min(cars, (int)Math.Floor(Math.Max(0, Extra - t.EatAfter) / Math.Max(1e-6, t.EatSeconds)));

    /// <summary>
    /// Note 621: seconds after dawn the lead Waker reaches the rear of a train going on at <paramref name="speed"/> (m/s) all the
    /// while: it rises <c>riseSeconds</c> after dawn <c>riseBehind</c> m behind the rear and runs it down. A stood train's is the
    /// soonest (about 72 s on the tuning of 9 Oct); +∞ if the train runs at their top speed or over.
    /// </summary>
    public static double CatchAfterDawn(WakersTuning t, double speed)
    {
        double v = Math.Max(0, speed);
        if (v >= t.TopSpeed)
            return double.PositiveInfinity;
        double lo = 0, hi = 60;
        while (Covered(hi, t) - t.RiseBehind - v * (t.RiseSeconds + hi) < 0 && hi < 1e6)
            hi *= 2;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Covered(mid, t) - t.RiseBehind - v * (t.RiseSeconds + mid) < 0)
                lo = mid;
            else
                hi = mid;
        }
        return t.RiseSeconds + hi;
    }

    /// <summary>
    /// Note 607 (shared with D1.5's #337, the driver at dawn): seconds before the lead Waker reaches the train's rear, the train
    /// going on at its speed now; +∞ with no Wakers tonight or before the dawn line's reckoning can start, 0 once one holds it.
    /// From what every machine has (the run's dawn clock, the lead's place on the line, the tuning), so a client's bot reckons it
    /// as the host does. Whether they stop short of it first is <see cref="SecondsToShelter"/>. With <paramref name="speed"/>,
    /// reckoned for the train going on at that instead (0: had it stood here, note 631's closest call).
    /// </summary>
    public static double SecondsInHand(World world, double? speed = null)
    {
        if (world.Enemies is not { Wakers: { Enabled: true } t } || world.Run is not { } run)
            return double.PositiveInfinity;
        Waker? lead = null;
        foreach (var e in world.ActiveEnemies)
            if (e is Waker { Lead: true, Gone: false } w)
                lead = w;
        if (lead is { Holds: true })
            return 0;
        var train = world.Train;
        // Seconds it's been coming (negative: still to rise, or still to dawn) and how far behind the rear it is.
        double since = -run.DawnIn - t.RiseSeconds;
        double gap = lead is not null ? train.RearDistance - lead.LineDistance : t.RiseBehind;
        if (gap <= 0)
            return 0;
        double wait = Math.Max(0, -since), s0 = Math.Max(0, since), v = Math.Max(0, speed ?? train.Dynamics.Speed);
        if (v >= t.TopSpeed)
            return double.PositiveInfinity;
        // Covered(s0 + τ) − Covered(s0) = gap + v (wait + τ): the closing speed only rises, so it's met once.
        double Short(double tau) => Covered(s0 + tau, t) - Covered(s0, t) - gap - v * (wait + tau);
        double lo = 0, hi = 60;
        while (Short(hi) < 0 && hi < 1e6)
            hi *= 2;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Short(mid) < 0)
                lo = mid;
            else
                hi = mid;
        }
        return wait + hi;
    }

    /// <summary>
    /// Note 607: seconds before the train's rear is past where the Wakers stop short of the terminus's gate (in under the guns),
    /// at its speed now: 0 once it is, +∞ stood short of it. Under <see cref="SecondsInHand"/>, it's home before they catch it.
    /// </summary>
    public static double SecondsToShelter(World world)
    {
        if (world.Enemies is not { Wakers: { Enabled: true } t } || world.Run is null)
            return double.PositiveInfinity;
        double past = Wall(world, t) - world.Train.RearDistance;
        if (past <= 0)
            return 0;
        double v = world.Train.Dynamics.Speed;
        return v > 0.05 ? past / v : double.PositiveInfinity;
    }

    /// <summary>How far short of the terminus's gate they stop: in under the guns, the crew's home.</summary>
    internal static double Wall(World world, WakersTuning t) => world.Run is { } run
        ? (run.Route.Plan?.Terminus.GateM ?? run.Route.Length - run.Tuning.TerminusZone) - t.StopShort
        : double.MaxValue;

    /// <summary>The cause card's line: the train caught, and by what (note 190's record of a punish that holds nobody).</summary>
    protected override Run.Incident? Punished(EnemyContext ctx)
    {
        var at = WorldPosition(ctx.Train);
        var (actor, action) = Run.IncidentLog.Nearest(ctx.World, at, ctx.Crew.Select(c => ((int)c.Player.Id, c.Player.State)));
        return Run.IncidentLog.Event(ctx.World, Run.IncidentKind.Punished, "Taken at dawn by a Waker", actor, action, at);
    }

    public override int Drags => Holds && Lead ? _dragged : -1;
    public override double DragFactor => _dragFactor;
    int _dragged = -1;
    double _dragFactor;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Wakers;
        var train = ctx.Train;
        if (Picker)
        {
            Pick(ctx, t);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // up out of the land
                return;
            case SpinePhase.Telegraph:
                if (PhaseSeconds >= Math.Max(ctx.Tuning.MinReactionSeconds, t.RiseSeconds))
                    Enter(ctx, SpinePhase.Commit);
                return;
            case SpinePhase.Commit:
                {
                    _since += SimConstants.TickSeconds;
                    double wall = Wall(ctx.World, t);
                    LineDistance = Math.Min(_start + Covered(_since, t), wall);
                    // The ones off to the sides close on the line as they come.
                    if (!Lead)
                        Lateral = Extra2 * t.RiseAside * Math.Max(0, 1 - _since / Math.Max(1e-6, t.JoinSeconds));
                    if (!Lead || LineDistance >= wall)
                        return;
                    if (LineDistance >= train.RearDistance)
                    {
                        _dragged = train.Dynamics.Consist.Vehicles[^1].Id;
                        _dragFactor = t.DragFactor;
                        Enter(ctx, SpinePhase.Punish);
                    }
                    return;
                }
            case SpinePhase.Punish:
                {
                    Extra += SimConstants.TickSeconds;
                    var cars = train.Dynamics.Consist.Vehicles;
                    LineDistance = train.RearDistance;
                    int eaten = EatenCars(t, cars.Count);
                    // Whoever's in an eaten car goes with it; whoever's off the train is taken by the rest of them, one risen
                    // beside each (note 710).
                    var gone = new HashSet<int>();
                    for (int i = 0; i < eaten; i++)
                        gone.Add(cars[cars.Count - 1 - i].Id);
                    foreach (var (p, _) in ctx.Crew)
                    {
                        var s = p.State;
                        if (s.Alive && gone.Contains(s.Parent))
                            ctx.Harm(p.Id, int.MaxValue / 2, DeathCause.Woken);
                    }
                    RaisePickers(ctx, t);
                    if (eaten >= cars.Count)
                        ctx.World.TakenAtDawn = true;
                    return;
                }
        }
    }

    /// <summary>A crewmate a picker may take: alive, off the train, free of any other's grip, and not in under a fort's guns.</summary>
    static bool Takeable(EnemyContext ctx, in PlayerState s) =>
        s.Alive && s.Parent < 0 && !ctx.World.InFort(PlayerMotor.WorldPosition(s, ctx.Train));

    /// <summary>
    /// Note 710: a Waker beside each crewmate off the held train who hasn't one after them (up to <c>pickers</c> at once; the
    /// rest wait for one to finish), in the crew's order.
    /// </summary>
    void RaisePickers(EnemyContext ctx, WakersTuning t)
    {
        var pickers = ctx.World.ActiveEnemies.OfType<Waker>().Where(w => w.Picker && !w.Gone).ToList();
        foreach (var (p, _) in ctx.Crew)
        {
            if (pickers.Count >= t.Pickers)
                return;
            if (!Takeable(ctx, p.State) || pickers.Any(w => w.Quarry == p.Id))
                continue;
            var at = PlayerMotor.WorldPosition(p.State, ctx.Train);
            pickers.Add(ctx.World.AddEnemy(id => Beside(id, p.Id, at, ctx.Train, t)));
        }
    }

    /// <summary>
    /// A picker's step (note 710): up out of the land (the telegraph), across to its quarry, the grab, the lift to its mouth
    /// (<see cref="Mouth"/>), and the eating; then the next who ran and has no one after them, or it stands.
    /// </summary>
    void Pick(EnemyContext ctx, WakersTuning t)
    {
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph);
                return;
            case SpinePhase.Telegraph:
                if (PhaseSeconds >= Math.Max(ctx.Tuning.MinReactionSeconds, t.PickRiseSeconds))
                    Enter(ctx, SpinePhase.Commit);
                return;
            case SpinePhase.Commit:
                {
                    int found = Quarry < 0 ? -1 : ctx.Crew.FindIndex(c => c.Player.Id == Quarry);
                    if (found < 0 || !Takeable(ctx, ctx.Crew[found].Player.State))
                    {
                        Extra = Next(ctx);
                        return;
                    }
                    var quarry = ctx.Crew[found].Player;
                    var at = PlayerMotor.WorldPosition(quarry.State, ctx.Train);
                    var way = (at - Local) with { Y = 0 };
                    if (way.Length > 1e-6)
                        Lateral = Heading(way);
                    if (way.Length <= t.PickReach + 0.05) // (it strides up to just its reach)
                    {
                        _from = at;
                        Grab(ctx, quarry.Id, t.PickLiftSeconds);
                        return;
                    }
                    double hint = LineDistance;
                    var next = Local + way.Normalized * Math.Min(way.Length - t.PickReach, t.PickSpeed * SimConstants.TickSeconds);
                    Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
                    LineDistance = hint;
                    return;
                }
            case SpinePhase.Grab:
                {
                    // In its hand, up off the ground to its mouth.
                    double k = Math.Clamp(PhaseSeconds / Math.Max(1e-6, GrabWindow), 0, 1);
                    k = k * k * (3 - 2 * k);
                    ctx.Lift(Holding, _from + (Mouth(Local, Lateral, t) - _from) * k);
                    return;
                }
            case SpinePhase.Punish:
                // Eaten; once it's swallowed, after the next who ran.
                if (PhaseSeconds >= t.PickEatSeconds && Next(ctx) is var next2 && next2 >= 0)
                {
                    Extra = next2;
                    Enter(ctx, SpinePhase.Commit);
                }
                return;
        }
    }

    Double3 _from;

    /// <summary>The nearest crewmate a picker could take who has no other picker after them, or −1 (by id on a tie).</summary>
    int Next(EnemyContext ctx)
    {
        var taken = ctx.World.ActiveEnemies.OfType<Waker>().Where(w => w != this && w.Picker && !w.Gone).Select(w => w.Quarry).ToHashSet();
        int best = -1;
        double bestD = double.MaxValue;
        foreach (var (p, _) in ctx.Crew)
        {
            if (!Takeable(ctx, p.State) || taken.Contains(p.Id))
                continue;
            double d = ctx.Weigh(p.Id, ((PlayerMotor.WorldPosition(p.State, ctx.Train) - Local) with { Y = 0 }).Length);
            if (d < bestD || d == bestD && p.Id < best)
                (best, bestD) = (p.Id, d);
        }
        return best;
    }

    /// <summary>A picker eats who it took (note 710): the spine's punish, after the lift to its mouth.</summary>
    protected override void Punish(EnemyContext ctx, int victim) => Kill(ctx, victim, DeathCause.Woken);
}

/// <summary>enemies.json <c>wakers</c> (note 588): what gets up at dawn. Field docs live there.</summary>
public sealed record WakersTuning
{
    /// <summary>Off, dawn is v1's: the line goes live and the run fails <c>run.json dawnGraceSeconds</c> later.</summary>
    public bool Enabled { get; init; }
    public double StirSeconds { get; init; } = 180;
    public int[] Count { get; init; } = [1, 1, 2, 3];
    public double RiseBehind { get; init; } = 1200;
    public double RiseAside { get; init; } = 600;
    public double AsideBack { get; init; } = 300;
    public double JoinSeconds { get; init; } = 90;
    public double RiseSeconds { get; init; } = 8;
    public double StartSpeed { get; init; } = 6;
    public double TopSpeed { get; init; } = 26;
    public double RampSeconds { get; init; } = 45;
    public double StopShort { get; init; } = 300;
    public double DragFactor { get; init; } = 3;
    public double LiftSeconds { get; init; } = 2.5;
    public double EatAfter { get; init; } = 6;
    public double EatSeconds { get; init; } = 4;
    // Those who ran (note 710): see enemies.json.
    public int Pickers { get; init; } = 3;
    public double PickAside { get; init; } = 40;
    public double PickRiseSeconds { get; init; } = 4;
    public double PickSpeed { get; init; } = 12;
    public double PickReach { get; init; } = 18;
    public double PickLiftSeconds { get; init; } = 4;
    public double PickMouthOut { get; init; } = 10;
    public double PickMouthHeight { get; init; } = 22;
    public double PickEatSeconds { get; init; } = 4;
    /// <summary>
    /// wakers.md §2, the night things run (note 635; the director, the inbox, 12 Oct 2026): this many seconds before dawn the
    /// night's creatures go to ground, and the director sends none of them after. Negative: they stay (v1).
    /// </summary>
    public double NightFleesSeconds { get; init; } = -1;
    /// <summary>The kinds that don't (their names in the director's costs): the parasites with a train function, the Knotter and Hotbox.</summary>
    public string[] StaysAtDawn { get; init; } = [];

    /// <summary>A creature of <paramref name="kind"/> goes to ground at dawn (<see cref="StaysAtDawn"/> doesn't name it).</summary>
    public bool Flees(EnemyKind kind) => !StaysAtDawn.Contains(Director.Key(kind));

    /// <summary>How many rise for a night of <paramref name="tier"/>.</summary>
    public int For(RouteTier tier) => Count.Length == 0 ? 0 : Count[Math.Clamp((int)tier, 0, Count.Length - 1)];
}
