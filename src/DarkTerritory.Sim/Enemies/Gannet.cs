using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Gannet is doing within its spine phase (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum GannetMode : byte { Away, Soar, Hang, Fold, Stuck, Climb, Bank, Pin }

/// <summary>
/// THE GANNET · sight · flank (GDD §21, App. A.4, B.4; ARCHITECTURE §8 note 340; the director's decisions of 7 Oct 2026,
/// docs/design/creatures/gannet.md; the orchestrator's S3, docs/design/orchestrator.md §5.2). One enormous corrupted
/// seabird that rides a fast train: over 18 m/s for a while in open country it comes, holding station in the smoke and the
/// wake over the cars. It hunts movement on the roofs: it hangs over a walker (the calls stop), folds and drops beak-first
/// at where they'll be (its line locked at the fold). Break stride after the fold and it misses, its beak buried in the
/// roof a while; hold the line and it stabs, a heavy hit, and climbs for another pass. Whoever hits it (a blow while it's
/// stuck, a ball in the air) is its mark: it banks round, screaming (the telegraph: get inside), and comes down on them,
/// pins them under a foot and pecks at their head, four pecks, the last their death; three blows from friends drive it off
/// them (and the last of them is its mark then). It can be killed by a team, and its head is a trophy worth a good deal;
/// hurt below a third, it leaves for the run. A slow train, a tunnel or a still roof and it peels off, back a few minutes
/// later if the train runs fast again. It never goes inside: any car or the cab is shelter. Rule: when it folds, break
/// your stride.
/// </summary>
/// <remarks>
/// It rides in a car's frame (<see cref="Enemy.Attached"/>, <see cref="Enemy.Local"/>): flying over the train is flying
/// in the train's moving air, so the frame keeps it with the train with no frame change before a tick (the moving-frames
/// rule). Pinning someone left on the ground, it's loose in the world. <see cref="Enemy.Extra"/> is the walker it's after on
/// a dive (−1 none), <see cref="Enemy.Extra2"/> its mark (−1 none), <see cref="Enemy.Lateral"/> its heading (a yaw in the
/// frame it's in, as a player's), <see cref="Enemy.Height"/> its <see cref="GannetMode"/>.
/// </remarks>
public sealed class Gannet(int id) : Enemy(id)
{
    double _modeSeconds, _theta, _nextDive, _fastFor, _slowFor, _quietFor, _height = 25, _stillFor;
    Double3 _from, _aim;
    int _blowsInPin;
    // Each crewmate's pace on their car this tick (the roof's walk moves them without a velocity): where they were last
    // tick, in which frame, and how fast they're going in it. By id, in order: never a dictionary's.
    readonly SortedDictionary<int, (int Parent, Double3 At, Double3 Pace)> _pace = [];

    public override EnemyKind Kind => EnemyKind.Gannet;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Kill;
    /// <summary>A blow lands only while it's down on a roof or a crewmate (stuck, or pinning).</summary>
    public override double MeleeRadius => Mode is GannetMode.Stuck or GannetMode.Pin ? 1.8 : 0;
    /// <summary>In the air or down, a ball can find it; away, it isn't there.</summary>
    public override bool Exposed => !Gone && Mode != GannetMode.Away;
    /// <summary>The guns are an answer (orchestrator §5.3: "gives our gunners things to do"), at the cost of the gunner's being its mark.</summary>
    public override bool GunAnswers => Mode != GannetMode.Away;
    /// <summary>It stays with the run until it's killed or gives up; the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public GannetMode Mode => (GannetMode)(int)Height;
    public int? Prey => Extra >= 0 ? (int)Extra : null;
    public int? Mark => Extra2 >= 0 ? (int)Extra2 : null;
    /// <summary>Pecks landed on whoever it pins (four, the last their death), from the spine's own clock: the same on every client.</summary>
    public int PecksLanded(GannetTuning t) => Phase == SpinePhase.Grab ? Math.Min(t.Pecks, (int)(PhaseSeconds / t.PeckEvery)) : 0;

    /// <summary>Arriving over the train: over its middle car, far up and coming down to soar.</summary>
    public static Gannet Arriving(int id, TrainOnLine train, GannetTuning t, double height)
    {
        var rake = train.Dynamics.Consist.Vehicles;
        int car = rake[rake.Count / 2].Id;
        var g = new Gannet(id) { Attached = car, Local = new Double3(t.SoarRadius, height + 20, 0), Extra = -1, Extra2 = -1, Health = t.Health };
        g._height = height;
        g.SetMode(GannetMode.Soar);
        return g;
    }

    /// <summary>Already marking <paramref name="mark"/> (a test's, an audit's: as if they'd hit it): it comes for them first.</summary>
    public static Gannet Marking(int id, TrainOnLine train, GannetTuning t, double height, int mark)
    {
        var g = Arriving(id, train, t, height);
        g.Extra2 = mark;
        return g;
    }

    void SetMode(GannetMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Gannet;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        Pace(ctx);
        double speed = train.Dynamics.Speed;
        bool tunnel = InTunnel(ctx.World);
        _fastFor = speed >= t.ArriveAbove && !tunnel ? _fastFor + dt : 0;
        if (Attached >= train.Frames.Count)
            Attached = MiddleCar(train);

        if (Mode == GannetMode.Away)
        {
            // Back once the train's run fast again a while, and it's been gone long enough.
            if (_modeSeconds >= t.ReturnSeconds && _fastFor >= t.ArriveSeconds)
            {
                Attached = MiddleCar(train);
                Local = new Double3(t.SoarRadius, _height + 20, 0);
                SetMode(GannetMode.Soar);
            }
            return;
        }

        // It can't stay up: the train slowed (no lift to ride), a tunnel, or a still roof with nobody for it.
        _slowFor = speed < t.StallBelow ? _slowFor + dt : 0;
        bool anyone = Walkers(ctx, t).Any() || Mark is { } m && MarkOut(ctx, m, t);
        _quietFor = anyone ? 0 : _quietFor + dt;
        if (Phase != SpinePhase.Grab && (tunnel || _slowFor >= t.StallSeconds || _quietFor >= t.QuietSeconds))
        {
            GoAway(ctx);
            return;
        }

        switch (Mode)
        {
            case GannetMode.Soar or GannetMode.Climb:
                Soar(ctx, t);
                return;
            case GannetMode.Hang:
                Hang(ctx, t);
                return;
            case GannetMode.Fold:
                Fold(ctx, t);
                return;
            case GannetMode.Stuck:
                if (_modeSeconds >= t.StuckSeconds)
                    Climb(ctx, t);
                return;
            case GannetMode.Bank:
                Bank(ctx, t);
                return;
            case GannetMode.Pin:
                // Stood on them: the spine counts the pecks down (its grab window); a ground pin keeps to the ground.
                if (Phase != SpinePhase.Grab)
                    Climb(ctx, t);
                return;
        }
    }

    /// <summary>SOAR: circling over the train in its smoke; when its time comes, after its mark, or over a walker.</summary>
    void Soar(EnemyContext ctx, GannetTuning t)
    {
        double dt = SimConstants.TickSeconds;
        if (Attached < 0)
        {
            // Back up to the train from the ground (a mark pinned there): over the nearest car again.
            var at = WorldPosition(ctx.Train);
            int car = Nearest(ctx.Train, at);
            Attached = car;
            Local = ctx.Train.Frames[car].ToLocal(at);
        }
        _theta += 0.35 * dt;
        var orbit = new Double3(t.SoarRadius * DMath.Cos(_theta), _height, t.SoarRadius * DMath.Sin(_theta));
        Fly(orbit, t.FlySpeed);
        if (Mode == GannetMode.Climb && (Local - orbit).Length < 2)
            SetMode(GannetMode.Soar);
        _nextDive -= dt;
        if (_nextDive > 0)
            return;
        if (Mark is { } mark && MarkOut(ctx, mark, t))
        {
            // BANK: it's coming for whoever hit it (the scream, the wingbeats closing).
            Extra = mark;
            _from = WorldPosition(ctx.Train);
            SetMode(GannetMode.Bank);
            Enter(ctx, SpinePhase.Alert);
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        var me = WorldPosition(ctx.Train);
        var walkers = Walkers(ctx, t).OrderBy(w => (w.World - me).Length).ThenBy(w => w.Id).ToList();
        if (walkers.Count > 0)
        {
            Extra = walkers[0].Id;
            _stillFor = 0;
            SetMode(GannetMode.Hang);
            ctx.World.Director?.GannetPass(0);
            Enter(ctx, SpinePhase.Alert);
        }
    }

    /// <summary>SPOT: it hangs still over a walker, head down, its calls stopped. If they stop walking, it lets them be.</summary>
    void Hang(EnemyContext ctx, GannetTuning t)
    {
        double dt = SimConstants.TickSeconds;
        bool walking = Prey is { } p && Walkers(ctx, t).Any(w => w.Id == p);
        var held = Prey is { } q ? ctx.Crew.FirstOrDefault(c => c.Player.Id == q).Player.State : default;
        if (Prey is not { } prey || ctx.Crew.All(c => c.Player.Id != prey) || !held.Alive || held.Parent < 0)
        {
            Climb(ctx, t);
            return;
        }
        _stillFor = walking ? 0 : _stillFor + dt;
        if (_stillFor >= 1.0)
        {
            Climb(ctx, t);
            return;
        }
        Rehome(ctx.Train, held.Parent);
        Fly(held.Position with { Y = t.SoarHeight[0] }, t.FlySpeed);
        if (_modeSeconds < t.HangSeconds || !walking)
            return;
        // FOLD: its line locks on where they'll be (their pace on the roof, the fold's length on).
        _aim = held.Position + PaceOf(prey) * t.FoldSeconds;
        _from = Local;
        Turn(_aim - _from);
        SetMode(GannetMode.Fold);
        ctx.World.Director?.GannetPass(1);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>FOLD → STRIKE: down its line; whoever's under the beak at the end takes it, and nobody there is a miss.</summary>
    void Fold(EnemyContext ctx, GannetTuning t)
    {
        double f = Math.Min(1, PhaseSeconds / t.FoldSeconds);
        Local = Double3.Lerp(_from, _aim, f * f);
        if (PhaseSeconds < t.FoldSeconds || !Enter(ctx, SpinePhase.Commit))
            return;
        var shape = ctx.Train.Frames[Attached].Shape;
        bool hit = false;
        foreach (var (p, _) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent != Attached || s.Has(PlayerFlags.Held) || Sheltered(ctx.Train, s))
                continue;
            var off = (s.Position - _aim) with { Y = 0 };
            if (off.Length > t.StrikeRadius || Math.Abs(s.Position.Y - _aim.Y) > 2.5)
                continue;
            ctx.Bite(p.Id, t.StabDamage, DeathCause.Pecked);
            if (!hit)
                ctx.World.Director?.GannetPass(2);
            hit = true;
        }
        _nextDive = Next(ctx, t);
        bool onRoof = Math.Abs(_aim.X) <= shape.HalfWidth && Math.Abs(_aim.Z) <= shape.HalfLength;
        if (!hit && onRoof)
        {
            // MISS: its beak buried in the planks, thrashing: the moment to hit it, and be its mark for it.
            Local = _aim with { Y = shape.RoofHeight };
            SetMode(GannetMode.Stuck);
            return;
        }
        Climb(ctx, t);
    }

    /// <summary>BANK → SWOOP: round and in low on its mark; if they're still out (not inside a car or the cab), down on them.</summary>
    void Bank(EnemyContext ctx, GannetTuning t)
    {
        if (Mark is not { } mark || !MarkOut(ctx, mark, t))
        {
            // Got inside in time: a pass over, and up again.
            _nextDive = t.DiveEvery[0];
            Climb(ctx, t);
            return;
        }
        var s = ctx.Crew.First(c => c.Player.Id == mark).Player.State;
        var target = PlayerMotor.WorldPosition(s, ctx.Train);
        double f = Math.Min(1, PhaseSeconds / t.BankSeconds);
        var at = Double3.Lerp(_from, target + Double3.Up * 1.2, f);
        Put(ctx.Train, at);
        if (PhaseSeconds < t.BankSeconds || !Enter(ctx, SpinePhase.Commit))
            return;
        // Down on them: in their car's frame, or on the ground beside the line.
        if (s.Parent >= 0)
        {
            Attached = s.Parent;
            Local = s.Position + new Double3(0, 0.3, 0);
        }
        else
        {
            Attached = Loose;
            Local = target;
            LineDistance = s.LineHint;
        }
        _blowsInPin = 0;
        if (Grab(ctx, mark, t.Pecks * t.PeckEvery))
            SetMode(GannetMode.Pin);
        else
            Climb(ctx, t);
    }

    void Climb(EnemyContext ctx, GannetTuning t)
    {
        Extra = -1;
        if (Phase is not (SpinePhase.Dormant or SpinePhase.Gone))
            Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
        SetMode(GannetMode.Climb);
        if (_nextDive <= 0)
            _nextDive = t.DiveEvery[0];
    }

    /// <summary>Peels off (a slow train, a tunnel, a still roof): away, and back later if the train runs fast again.</summary>
    void GoAway(EnemyContext ctx)
    {
        Extra = -1;
        if (Phase is not (SpinePhase.Dormant or SpinePhase.Gone))
            Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Dormant);
        Attached = MiddleCar(ctx.Train);
        Local = new Double3(0, 300, 0);
        _slowFor = _quietFor = 0;
        SetMode(GannetMode.Away);
    }

    /// <summary>PUNISH: the fourth peck. Then up again; its mark is done with.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Pecked);
        if (Mark == victim)
            Extra2 = -1;
        Climb(ctx, ctx.Tuning.Gannet);
    }

    /// <summary>Driven off its pin (or a lone victim struggled free): up, screaming, and the one who did it is its mark.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        if (by >= 0 && by != Holding)
            Extra2 = by;
        _nextDive = ctx.Tuning.Gannet.DiveEvery[0];
        Climb(ctx, ctx.Tuning.Gannet);
    }

    /// <summary>
    /// A blow (only while it's down) or a ball (anywhere): it's hurt, and whoever did it is its mark. Three blows from friends
    /// on its pin drive it off; under a third of its health it leaves for the run; killed, it drops its head, a trophy.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        if (Gone || Mode == GannetMode.Away)
            return;
        var t = ctx.Tuning.Gannet;
        Health -= damage;
        if (Health <= 0)
        {
            Trophy(ctx);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Health < t.GiveUpBelow)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (by < 0)
            return;
        if (Phase == SpinePhase.Grab)
        {
            if (by == Holding)
                return;
            if (++_blowsInPin >= t.DriveOffBlows)
                Rescued(ctx, by);
            return;
        }
        Extra2 = by;
    }

    /// <summary>
    /// Killed (the director, 7 Oct 2026: "a dead Gannet can be worth a good deal"): its head, the plated skull and the spear,
    /// lies where it fell, a find (<see cref="Run.Run.TrophyOwner"/>) that pays when it's stowed aboard.
    /// </summary>
    void Trophy(EnemyContext ctx)
    {
        var train = ctx.Train;
        Body? body;
        if (Attached >= 0 && Attached < train.Frames.Count)
        {
            var shape = train.Frames[Attached].Shape;
            var on = new Double3(Math.Clamp(Local.X, -shape.HalfWidth + 0.3, shape.HalfWidth - 0.3), shape.RoofHeight,
                Math.Clamp(Local.Z, -shape.HalfLength + 0.5, shape.HalfLength - 0.5));
            body = ctx.World.Bodies.SpawnCrate(train, Attached, on, Physics.BodyKind.Loot);
        }
        else
        {
            double hint = LineDistance;
            var at = Local with { Y = PlayerMotor.GroundAt(Local, train.Line, ref hint) };
            body = ctx.World.Bodies.SpawnLoot(at, hint, 0, 0.15);
        }
        body.Owner = Run.Run.TrophyOwner(EnemyKind.Gannet);
        body.Claimed = false;
    }

    /// <summary>Who's walking the roofs (moving on a car's roof over <see cref="GannetTuning.PreyAbove"/>, out in the open): its prey.</summary>
    IEnumerable<(int Id, Double3 World)> Walkers(EnemyContext ctx, GannetTuning t)
    {
        foreach (var (p, _) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent < 0 || s.Surface != Surface.Roof || s.Has(PlayerFlags.Held) || s.Has(PlayerFlags.Seated))
                continue;
            if (PaceOf(p.Id).Length <= t.PreyAbove)
                continue;
            yield return (p.Id, PlayerMotor.WorldPosition(s, ctx.Train));
        }
    }

    /// <summary>Everyone's pace in their frame since last tick (level; nothing when they changed frame).</summary>
    void Pace(EnemyContext ctx)
    {
        foreach (var (p, _) in ctx.Crew)
        {
            var s = p.State;
            var pace = _pace.TryGetValue(p.Id, out var was) && was.Parent == s.Parent
                ? (s.Position - was.At) with { Y = 0 } * (1 / SimConstants.TickSeconds)
                : Double3.Zero;
            _pace[p.Id] = (s.Parent, s.Position, pace);
        }
    }

    Double3 PaceOf(int id) => _pace.TryGetValue(id, out var p) ? p.Pace : Double3.Zero;

    /// <summary>Whether its mark is out where it can come down on them: alive, not inside a car or the cab, and (on the ground) within reach.</summary>
    bool MarkOut(EnemyContext ctx, int mark, GannetTuning t)
    {
        var c = ctx.Crew.FirstOrDefault(x => x.Player.Id == mark);
        if (ctx.Crew.All(x => x.Player.Id != mark) || c.Player.State is not { Alive: true } s || s.Has(PlayerFlags.Held))
            return false;
        if (s.Parent >= 0)
            return !Sheltered(ctx.Train, s);
        return (PlayerMotor.WorldPosition(s, ctx.Train) - WorldPosition(ctx.Train)).Length <= t.MarkReach;
    }

    /// <summary>Inside a car's room or the cab: somewhere it can't come.</summary>
    static bool Sheltered(TrainOnLine train, in PlayerState s)
    {
        if (s.Parent < 0 || s.Parent >= train.Frames.Count)
            return false;
        var shape = train.Frames[s.Parent].Shape;
        return shape.Interior is { } room && room.Contains(s.Position) || shape.Cab is { } cab && cab.Contains(s.Position);
    }

    static bool InTunnel(World world) =>
        world.Route?.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(world.Train.Dynamics.Distance)) == true;

    static int MiddleCar(TrainOnLine train)
    {
        var rake = train.Dynamics.Consist.Vehicles;
        return rake[rake.Count / 2].Id;
    }

    static int Nearest(TrainOnLine train, Double3 at) =>
        train.Dynamics.Consist.Vehicles.Select(v => v.Id).OrderBy(id => (train.Frames[id].Origin - at).Length).First();

    /// <summary>Into another car's frame, where it is in the world (a frame change between ticks, never inside one).</summary>
    void Rehome(TrainOnLine train, int car)
    {
        if (car == Attached || car < 0 || car >= train.Frames.Count)
            return;
        var at = WorldPosition(train);
        Attached = car;
        Local = train.Frames[car].ToLocal(at);
    }

    /// <summary>Put at a world point, in the frame it's in.</summary>
    void Put(TrainOnLine train, Double3 world)
    {
        if (Attached >= 0 && Attached < train.Frames.Count)
            Local = train.Frames[Attached].ToLocal(world);
        else
            Local = world;
    }

    void Fly(Double3 to, double speed)
    {
        var d = to - Local;
        double step = speed * SimConstants.TickSeconds;
        if (d.Length <= step)
            Local = to;
        else
        {
            Local += d.Normalized * step;
            Turn(d);
        }
    }

    void Turn(Double3 d)
    {
        var flat = d with { Y = 0 };
        if (flat.Length > 1e-6)
            Lateral = DMath.Atan2(-flat.X, -flat.Z);
    }

    /// <summary>Its next pass's wait, from its id and the tick (its own, so it never moves the director's dice).</summary>
    double Next(EnemyContext ctx, GannetTuning t)
    {
        uint h = (ctx.Tick * 2654435761u) ^ (uint)(Id * 40503);
        return t.DiveEvery[0] + (t.DiveEvery[1] - t.DiveEvery[0]) * (h % 1000) / 1000.0;
    }
}
