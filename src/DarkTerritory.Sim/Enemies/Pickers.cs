using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What a Picker is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum PickerMode : byte { Emerge, Scuttle, Wait, Carry, Heave, Bite, Scatter, Down }

/// <summary>
/// THE PICKERS · sight · outside (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 574; the director's pick to prototype, 9 Oct
/// 2026: "other stuff that is more frequently active in the yard"; docs/design/creatures/pickers.md). Knee-high scavengers
/// that live in a yard's drains. A while after a train comes to a stand at the yard they come up out of the drains at the
/// foot of the sheds and race the crew for the loose crates on the ground: each takes the nearest one nobody's standing by
/// and carries it over its head back down its drain, gone (a heavy crate takes two). They keep off people: come right up to
/// one carrying a crate and it bites (a hurt, never a kill) and drops it. A blow kills one, and the rest near it scatter to
/// their drains a while. When the train starts away, they go down for good. Rule: get there first.
/// </summary>
/// <remarks>
/// One of a group: each is its own enemy, loose in the world (<see cref="Enemy.Loose"/>, <see cref="Enemy.Local"/> its world
/// position). <see cref="Enemy.Extra"/> is the crate it's after or has (−1 none), <see cref="Enemy.Extra2"/> its drain (an
/// index into the group's drains), <see cref="Enemy.Lateral"/> its heading, <see cref="Enemy.Height"/> its
/// <see cref="PickerMode"/>. Each decides from the same state, in id order, so the host and every client agree.
/// </remarks>
public sealed class Picker(int id) : Enemy(id)
{
    double _modeSeconds, _scatter, _biteAgain;
    bool _leaving;
    Double3 _drain;

    public override EnemyKind Kind => EnemyKind.Pickers;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    /// <summary>What they take is the stop's loot.</summary>
    public override Want Want => Want.Cargo;
    /// <summary>Frail: any blow that reaches one lands, but not one gone down its drain.</summary>
    public override double MeleeRadius => Mode == PickerMode.Down ? 0 : 0.8;
    /// <summary>They belong to the stop: the director never dismisses them for want of company.</summary>
    public override bool StaysAboard => true;

    public PickerMode Mode => (PickerMode)(int)Height;
    public int? Crate => Extra >= 0 ? (int)Extra : null;
    /// <summary>Its drain, in the world (where it comes up and where it takes what it carries).</summary>
    public Double3 Drain => _drain;

    /// <summary>One come up out of <paramref name="drain"/> (the <paramref name="index"/>-th of the group's), facing the yard.</summary>
    public static Picker Up(int id, Double3 drain, int index, double lineHint, double yaw, PickersTuning t) =>
        new(id) { Attached = Loose, Local = drain, _drain = drain, LineDistance = lineHint, Lateral = yaw, Extra = -1, Extra2 = index, Health = t.Health };

    void SetMode(PickerMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    /// <summary>The train's going: every one goes down its drain, and it's gone once it has.</summary>
    public void Leave() => _leaving = true;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Pickers;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        _scatter = Math.Max(0, _scatter - dt);
        _biteAgain = Math.Max(0, _biteAgain - dt);
        if (Attached != Loose)
        {
            Local = WorldPosition(train);
            Attached = Loose;
        }
        // Their coming up is the telegraph (the grating's scrape, the chitter); a crate taken, the commit.
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);

        var held = Held(ctx);
        if (_leaving)
        {
            if (held is { } h)
                ctx.World.Bodies.LetGo(h);
            Extra = -1;
            if (GoDown(ctx, t, t.Run))
                Enter(ctx, SpinePhase.Gone);
            return;
        }
        // Down a drain (with a crate, or scattered): up again a little after.
        if (Mode == PickerMode.Down)
        {
            if (_modeSeconds >= t.DownSeconds && _scatter <= 0)
                SetMode(PickerMode.Emerge);
            return;
        }
        if (Mode == PickerMode.Emerge && _modeSeconds < t.EmergeSeconds)
            return;

        var me = Local;
        var living = ctx.LivingCrew().ToList();

        // Scattered: back to its drain and down it, whatever it had.
        if (_scatter > 0)
        {
            if (held is { } h)
                ctx.World.Bodies.LetGo(h);
            Extra = -1;
            SetMode(PickerMode.Scatter);
            if (GoDown(ctx, t, t.Run))
                SetMode(PickerMode.Down);
            return;
        }

        // Carrying: to its drain, biting whoever comes right up to it (and dropping the crate).
        if (held is { } crate)
        {
            if (living.Where(c => Flat(c.World - me) <= t.BiteWithin).OrderBy(c => Flat(c.World - me)).ThenBy(c => c.Player.Id)
                .Select(c => (int?)c.Player.Id).FirstOrDefault() is { } bitten)
            {
                if (_biteAgain <= 0)
                {
                    ctx.Bite(bitten, t.Bite, DeathCause.Gnawed);
                    _biteAgain = t.BiteEvery;
                }
                ctx.World.Bodies.LetGo(crate);
                Extra = -1;
                SetMode(PickerMode.Bite);
                _scatter = t.ScatterSeconds;
                return;
            }
            bool heavy = crate.Kind == BodyKind.Heavy;
            if (heavy && Helper(ctx, crate) is not { } helper)
            {
                // A heavy crate waits for its second.
                SetMode(PickerMode.Carry);
                ctx.World.Bodies.TakeAlong(crate, train, Id, PlayerState.World, me + Double3.Up * t.HoldHeight, Lateral);
                return;
            }
            SetMode(PickerMode.Carry);
            if (Flat(_drain - me) <= t.AtDrain)
            {
                // Down the drain with it: the crate's gone, the stop's loot the less.
                ctx.World.Bodies.Remove(crate);
                ctx.World.PickersTook++;
                Extra = -1;
                Enter(ctx, SpinePhase.Commit);
                SetMode(PickerMode.Down);
                return;
            }
            StepTo(ctx, _drain, heavy ? t.CarryHeavy : t.Carry);
            ctx.World.Bodies.TakeAlong(crate, train, Id, PlayerState.World, Local + Double3.Up * t.HoldHeight, Lateral);
            return;
        }

        // Unladen: keeps off anyone close.
        if (living.Where(c => Flat(c.World - me) <= t.ShyWithin).OrderBy(c => Flat(c.World - me)).ThenBy(c => c.Player.Id)
            .Select(c => (Double3?)c.World).FirstOrDefault() is { } near)
        {
            Extra = -1;
            SetMode(PickerMode.Scuttle);
            Walk(ctx, (me - near) with { Y = 0 }, t.Run);
            return;
        }

        // A heavy crate's second: beside its lead, under it.
        if (Crate is { } target && ctx.World.ActiveEnemies.OfType<Picker>()
            .FirstOrDefault(p => !p.Gone && p != this && p.Extra == target && p.Held(ctx) is not null) is { } lead)
        {
            SetMode(PickerMode.Heave);
            var right = Double3.Cross(Way(lead.Lateral), Double3.Up).Normalized;
            StepTo(ctx, lead.Local + right * t.HeaveBeside, t.Run);
            Lateral = lead.Lateral;
            return;
        }

        // The nearest crate it can have: loose on the ground, nobody standing by it, nobody else's.
        var want = Pick(ctx, t, living.Select(c => c.World).ToList());
        if (want is null)
        {
            Extra = -1;
            SetMode(PickerMode.Wait);
            StepTo(ctx, _drain, t.Run * 0.5);
            return;
        }
        Extra = want.Id;
        SetMode(PickerMode.Scuttle);
        var at = want.Centre;
        // A heavy crate another has up already: it's the second, and comes under it (next tick, beside its lead).
        if (want.TakenBy >= 0)
        {
            StepTo(ctx, at, t.Run);
            return;
        }
        if (Flat(at - me) > t.TakeReach)
        {
            StepTo(ctx, at, t.Run);
            return;
        }
        // Got it. (A heavy crate's taken up by the first there; the second comes under it.)
        Lateral = Yaw(_drain - me);
        ctx.World.Bodies.TakeAlong(want, train, Id, PlayerState.World, me + Double3.Up * t.HoldHeight, Lateral);
        SetMode(PickerMode.Carry);
    }

    /// <summary>The crate it has hold of, if it's still its own (not taken up by a crewmate meanwhile).</summary>
    Body? Held(EnemyContext ctx) =>
        Crate is { } id && ctx.World.Bodies.All.FirstOrDefault(b => b.Id == id && b.TakenBy == Id) is { } b ? b : null;

    /// <summary>The second under a heavy crate, beside it: another of them after the same crate, close.</summary>
    Picker? Helper(EnemyContext ctx, Body crate) =>
        ctx.World.ActiveEnemies.OfType<Picker>()
            .Where(p => !p.Gone && p != this && p.Extra == crate.Id && p.Mode == PickerMode.Heave
                && Flat(p.Local - Local) <= ctx.Tuning.Pickers.HeaveBeside + 0.6)
            .OrderBy(p => p.Id).FirstOrDefault();

    static bool OnTheGround(Body b) => b.Kind is BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy && b.Parent == PlayerState.World
        && b.Carrier < 0 && b.Second < 0 && b.TakenBy < 0 && !b.Stowed;

    /// <summary>
    /// The nearest crate it can go for (by distance, then id): loose on the ground within <c>facilityReach</c> of the train,
    /// nobody within <c>guardWithin</c> of it, and not another's (a heavy crate takes two: the second may join the first).
    /// </summary>
    Body? Pick(EnemyContext ctx, PickersTuning t, List<Double3> living)
    {
        var me = Local;
        var front = ctx.Train.Frames[0].Origin;
        double reach = t.FacilityReach + ctx.Train.Dynamics.Consist.LengthMetres;
        var others = ctx.World.ActiveEnemies.OfType<Picker>().Where(p => !p.Gone && p != this && p.Extra >= 0).ToList();
        return ctx.World.Bodies.All
            .Where(b => OnTheGround(b) || b.Kind == BodyKind.Heavy && others.Any(p => p.Extra == b.Id && b.TakenBy == p.Id))
            .Where(b => Flat(b.Centre - front) <= reach)
            .Where(b => !living.Any(p => Flat(p - b.Centre) <= t.GuardWithin))
            .Where(b => others.Count(p => p.Extra == b.Id) < (b.Kind == BodyKind.Heavy ? 2 : 1))
            .OrderBy(b => Flat(b.Centre - me)).ThenBy(b => b.Id).FirstOrDefault();
    }

    /// <summary>To its drain and down it: true once it's there.</summary>
    bool GoDown(EnemyContext ctx, PickersTuning t, double speed)
    {
        if (Flat(_drain - Local) <= t.AtDrain)
            return true;
        StepTo(ctx, _drain, speed);
        return false;
    }

    /// <summary>
    /// Struck: a blow kills one (they're frail); it drops what it had, and every one of them within <c>scatterWithin</c>
    /// drops theirs and runs for its drain for <c>scatterSeconds</c>.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.Pickers;
        if (Held(ctx) is { } h)
            ctx.World.Bodies.LetGo(h);
        base.Struck(ctx, by, damage);
        foreach (var p in ctx.World.ActiveEnemies.OfType<Picker>().Where(p => !p.Gone && p != this).OrderBy(p => p.Id))
            if (Flat(p.Local - Local) <= t.ScatterWithin)
                p._scatter = t.ScatterSeconds;
    }

    void Walk(EnemyContext ctx, Double3 way, double speed)
    {
        way = way with { Y = 0 };
        if (way.Length < 1e-6)
            return;
        var next = Local + way.Normalized * speed * SimConstants.TickSeconds;
        double hint = LineDistance;
        Local = next with { Y = PlayerMotor.GroundAt(next, ctx.Train.Line, ref hint) };
        LineDistance = hint;
        Lateral = Yaw(way);
    }

    void StepTo(EnemyContext ctx, Double3 to, double speed)
    {
        var way = (to - Local) with { Y = 0 };
        double step = speed * SimConstants.TickSeconds;
        if (way.Length <= step)
        {
            double hint = LineDistance;
            Local = to with { Y = PlayerMotor.GroundAt(to, ctx.Train.Line, ref hint) };
            LineDistance = hint;
            return;
        }
        Walk(ctx, way, speed);
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>A heading for a way along the ground, as a player's yaw (−Z forward).</summary>
    static double Yaw(Double3 way) => DMath.Atan2(-way.X, -way.Z);

    static Double3 Way(double yaw) => new(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
}

/// <summary>
/// Where the Pickers come from (note 574): a train standing at a facility with loose crates brings a group up out of the
/// yard's drains <c>after</c> seconds, one group to a stop; the train started away sends them down again. On the host, once
/// a second. Not the director's: they cost it nothing.
/// </summary>
public sealed class Picking
{
    // The facility the group came up at (−1 none yet), and how long the train's stood at the one it's at.
    int _cameAt = -1, _standingAt = -1;
    double _standing;

    public void Step(World world, PickersTuning t, Route.RouteTier tier, int crew, ref int nextId, List<Enemy> into, double seconds)
    {
        var train = world.Train;
        int facility = world.Run is { Phase: Run.RunPhase.AtFacility } run ? run.Facility : -1;
        bool standing = facility >= 0 && train.Dynamics.Speed < 0.3;
        // Gone from the stop (or moving off it): every one of them goes down.
        if (facility < 0 || train.Dynamics.Speed > 1)
            foreach (var p in into.OfType<Picker>().Where(p => !p.Gone))
                p.Leave();
        if (!standing)
        {
            _standing = 0;
            return;
        }
        if (_standingAt != facility)
        {
            _standingAt = facility;
            _standing = 0;
        }
        _standing += seconds;
        if (_standing < t.After || _cameAt == facility)
            return;
        var front = train.Frames[0].Origin;
        double reach = t.FacilityReach + train.Dynamics.Consist.LengthMetres;
        if (!world.Bodies.All.Any(b => b.Kind is BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy && b.Parent == PlayerState.World
                && b.Carrier < 0 && b.TakenBy < 0 && !b.Stowed && Flat(b.Centre - front) <= reach))
            return;
        _cameAt = facility;
        var drains = Drains(world, t);
        int count = t.CountFor(tier, crew);
        for (int i = 0; i < count; i++)
        {
            int d = i % drains.Count;
            var (at, hint, yaw) = drains[d];
            into.Add(Picker.Up(nextId++, at, d, hint, yaw, t));
        }
    }

    /// <summary>
    /// The group's drains (up to <c>drains</c>): at the foot of the yard's sheds nearest the train, <c>drainOff</c> out from
    /// the wall on the track's side, facing the track; with no shed in reach, out on the ground beyond the loose crates.
    /// Read from the stop's own sheds, never placed by its generator, so no stop's layout changes.
    /// </summary>
    public static List<(Double3 At, double Hint, double Yaw)> Drains(World world, PickersTuning t)
    {
        var train = world.Train;
        var front = train.Frames[0].Origin;
        double reach = t.FacilityReach + train.Dynamics.Consist.LengthMetres;
        var found = new List<(Double3, double, double)>();
        if (world.Run?.FacilityFeature is { Stop: { } stop } f)
        {
            var sheds = stop.Buildings
                .Where(b => b.Kind is Stops.BuildingKind.Shed or Stops.BuildingKind.GoodsShed && b.Zone == Stops.StopZone.Yard)
                .Select(b => (B: b, At: Run.Run.StopWorld(train.Line, f, new Stops.Pt(b.S, b.D))))
                .Where(x => Flat(x.At - front) <= reach)
                .OrderBy(x => Flat(x.At - front)).ThenBy(x => x.B.S).ThenBy(x => x.B.D)
                .Take(t.Drains);
            foreach (var (b, at) in sheds)
            {
                var toward = Toward(train, at);
                var spot = at + toward * (b.Width / 2 + t.DrainOff);
                double hint = train.Dynamics.Distance;
                spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
                found.Add((spot, hint, DMath.Atan2(-toward.X, -toward.Z)));
            }
        }
        if (found.Count == 0)
        {
            // No shed: out beyond the crates, on the far side of them from the line.
            var crates = world.Bodies.All.Where(b => b.Kind is BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy
                && b.Parent == PlayerState.World && Flat(b.Centre - front) <= reach).OrderBy(b => b.Id).ToList();
            var mid = crates.Count > 0 ? crates.Aggregate(Double3.Zero, (s, b) => s + b.Centre) * (1.0 / crates.Count) : front;
            var away = Toward(train, mid) * -1;
            var spot = mid + away * 15;
            double hint = train.Dynamics.Distance;
            spot = spot with { Y = PlayerMotor.GroundAt(spot, train.Line, ref hint) };
            found.Add((spot, hint, DMath.Atan2(away.X, away.Z)));
        }
        return found;
    }

    /// <summary>Level, toward the nearest track from <paramref name="at"/>.</summary>
    static Double3 Toward(TrainOnLine train, Double3 at)
    {
        double hint = train.Dynamics.Distance;
        var (path, d) = train.Line.Nearest(at, ref hint);
        var on = train.Line.Sample(path, d);
        var to = (on.Position - at) with { Y = 0 };
        return to.Length > 1e-3 ? to.Normalized : Double3.Cross(on.Tangent, Double3.Up).Normalized;
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;
}
