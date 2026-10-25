using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Close is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum CloseMode : byte { Bow, Pour, Pin, Burst }

/// <summary>
/// THE CLOSE · vibration · interior (GDD §21; the director's playtest of 23 Oct 2026, line 36: "if everybody's crowded into one
/// car ... something that does a little bit of crowd control ... It should always be a creature with a clear tell and a clear
/// counter"; Sloe's docs/design/creatures/the-close.md, note 1004; built by Rook, queue #716, note 1013). A great soft grey
/// sack, all skin and no bones, that lies along a moving train's roofs. With <c>crowd</c> of the crew (fewer on a small crew)
/// shut in one car for <c>settle</c> seconds (<see cref="Crowding"/>), it settles on that roof and bows its boards in over
/// their heads (the telegraph, <c>bowSeconds</c>); then it pours in through the hatch (<c>pourSeconds</c>) and pins everyone
/// in the car to the walls (held: a look, a voice and Use). <c>crushAfter</c> seconds pinned they're crushed,
/// <c>crushPerSecond</c> each (never the last point); <c>killSeconds</c> pinned the weakest dies, the spine's grab and punish,
/// and it slides off sated. The car ever fewer than the crowd and it slides off. A door of the car open: it lets the pinned
/// crewmate nearest one go every <c>getOutSeconds</c>. A blow from outside the car (on its roof, at its door, from the next
/// roof) and <c>health</c> of them burst it; a gun round bursts it at once; a blow from inside goes into its thickness.
/// Rule: when the roof bows, someone get out.
/// </summary>
/// <remarks>
/// On its car's roof over the hatch (<see cref="Enemy.Attached"/>, <see cref="Enemy.Local"/> in the car's frame) the whole
/// while: what's outside is what can be hit. <see cref="Enemy.Extra"/> is its car's half length (it lies the car's length, so a
/// swing anywhere along the roof reaches it), <see cref="Enemy.Extra2"/> the seconds in its mode, <see cref="Enemy.Height"/> its
/// <see cref="CloseMode"/>. Who it's let out, and its crush's count, are the host's.
/// </remarks>
public sealed class Close(int id) : Enemy(id)
{
    double _crush, _out;
    readonly SortedSet<int> _letOut = [];

    public override EnemyKind Kind => EnemyKind.Close;
    public override PressureZone Zone => PressureZone.Interior;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Kill;
    /// <summary>It lies the car's length: a swing from anywhere along its roof (or across the gap from the next) lands.</summary>
    public override double MeleeRadius => Gone ? 0 : Extra + 0.5;
    /// <summary>It's there for the crowd in its car: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public CloseMode Mode => (CloseMode)(int)Height;
    /// <summary>Seconds in its mode (replicated: the bow's breathing and the swell are drawn by it).</summary>
    public double ModeSeconds => Extra2;

    /// <summary>One settled on <paramref name="car"/>'s roof, over its hatch (the middle of a car without one), bowing it in.</summary>
    public static Close Over(int id, int car, TrainOnLine train, CloseTuning t)
    {
        var shape = train.Frames[car].Shape;
        return new(id)
        {
            Attached = car,
            Local = new Double3(0, shape.RoofHeight, shape.Hatch?.Centre.Z ?? 0),
            Extra = shape.HalfLength,
            Height = (double)CloseMode.Bow,
            Health = t.Health,
        };
    }

    /// <summary>A living crewmate on <paramref name="car"/>'s floor, inside its walls (not its roof, steps or plates).</summary>
    public static bool InCar(in PlayerState s, TrainOnLine train, int car) =>
        s.Alive && car >= 1 && s.Parent == car && car < train.Frames.Count && train.Frames[car].Shape.Interior is { } room
        && room.Contains(s.Position + new Double3(0, 0.2, 0));

    /// <summary>
    /// Pinned in a car a Close holds, with a door of that car in reach: the pinned can still Use a door they're against (Sloe's
    /// design §5). From replicated state (its mode, its car, the door), so a predicting client works the door as the host does.
    /// </summary>
    public static bool DoorInReach(in PlayerState s, World world)
    {
        int car = s.Parent;
        return car >= 1 && world.ActiveEnemies.Any(e => e is Close { Gone: false, Mode: CloseMode.Pin } c && c.Attached == car)
            && CrewActions.NearestInteractable(s, world.Train, world.Hand) is ({ Kind: InteractableKind.Door }, var v) && v == car;
    }

    /// <summary><paramref name="car"/>'s doors all shut (the four; the hatch is its way in).</summary>
    public static bool Shut(TrainOnLine train, int car) => (train.Vehicles[car].DoorsOpen & ((1 << CarShape.HatchBit) - 1)) == 0;

    /// <summary>The car its rule offers now: the one crowded and shut longest, at least the tier's <c>settle</c>; null for none.</summary>
    public static int? For(SpawnContext c)
    {
        if (c.Director.Crowding.Longest(MooseTuning.ByTier(c.Tuning.Close.Settle, c.Tier)) is not { } car
            || car >= c.Train.Frames.Count || c.Train.Dynamics.Consist.IndexOf(car) < 0)
            return null;
        return car;
    }

    void SetMode(CloseMode mode)
    {
        if (Mode != mode)
            Extra2 = 0;
        Height = (double)mode;
    }

    /// <summary>It slides off into the next gap and is gone (the car too light for it, or sated).</summary>
    void Leave(EnemyContext ctx)
    {
        if (Phase == SpinePhase.Grab)
            Release(ctx);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Burst from outside: it lets go of everyone and sags off the car. It can come again to another crowd tonight.</summary>
    void Burst(EnemyContext ctx)
    {
        // (Staged without a car, its Height is where it stands: it's left there.)
        if (Attached >= 0)
            SetMode(CloseMode.Burst);
        Slay(ctx, forTheNight: false);
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Close;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        Extra2 += dt;
        // Settled, it's bowing the roof at once: the bow is its telegraph.
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);
        // Staged without a car (a look, a test): it lies where it's put.
        if (Attached < 0)
            return;
        if (Attached < 1 || Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Leave(ctx);
            return;
        }
        int car = Attached;
        var inside = ctx.Crew.Where(c => InCar(c.Player.State, train, car) && !_letOut.Contains(c.Player.Id))
            .Select(c => c.Player).OrderBy(p => p.Id).ToList();
        // The car down to fewer than a crowd: it can't hold it, and slides off. Not in the creature gym (GDD §34.1; note 676),
        // where it's called onto a car to be looked at: there it lies and bows until it's cleared, and pins whoever goes in.
        int crowd = ctx.World.CallOnly ? 0 : t.CrowdFor(ctx.World.Director?.Crew ?? ctx.Crew.Count);
        if (inside.Count < crowd)
        {
            Leave(ctx);
            return;
        }
        switch (Mode)
        {
            case CloseMode.Bow:
                // The roof bowing in (the telegraph); bowSeconds on (never inside the reaction window), it pours.
                if (Phase == SpinePhase.Telegraph && Extra2 >= Math.Max(ctx.Tuning.MinReactionSeconds, t.BowSeconds) && Enter(ctx, SpinePhase.Commit))
                    SetMode(CloseMode.Pour);
                return;
            case CloseMode.Pour:
                // In through the hatch, swelling; then everyone's pinned, and the weakest is the one the crush takes first (the
                // spine's grab: its window is killSeconds).
                if (Extra2 >= t.PourSeconds && inside.Count > 0)
                {
                    var weakest = inside.OrderBy(p => p.State.Health).ThenBy(p => p.Id).First();
                    if (Grab(ctx, weakest.Id, t.KillSeconds))
                    {
                        SetMode(CloseMode.Pin);
                        (_crush, _out) = (0, 0);
                    }
                }
                return;
            case CloseMode.Pin:
                foreach (var p in inside)
                    ctx.Hold(p.Id);
                var tier = ctx.World.Route?.Tier ?? Route.RouteTier.Frontier;
                if (Extra2 >= MooseTuning.ByTier(t.CrushAfter, tier))
                {
                    _crush += t.CrushPerSecond * dt;
                    if ((int)_crush is var bite and > 0)
                    {
                        _crush -= bite;
                        foreach (var p in inside)
                            ctx.Bite(p.Id, bite, DeathCause.Crushed);
                    }
                }
                // A door open (opened by the one against it, or by a friend outside): one gets out a getOutSeconds, the nearest
                // it. The one the crush is taking first goes last.
                int open = train.Vehicles[car].DoorsOpen & ((1 << CarShape.HatchBit) - 1);
                if (open == 0)
                {
                    _out = 0;
                    return;
                }
                _out += dt;
                if (_out < t.GetOutSeconds)
                    return;
                _out -= t.GetOutSeconds;
                var shape = train.Frames[car].Shape;
                var doors = shape.DoorList.Where(d => (open & (1 << d.Index)) != 0).Select(d => d.Box.Centre).ToList();
                var freed = inside.Where(p => p.Id != Holding)
                    .OrderBy(p => doors.Min(d => (d - p.State.Position).Length)).ThenBy(p => p.Id).FirstOrDefault();
                if (freed.State.Alive)
                    _letOut.Add(freed.Id);
                return;
        }
    }

    /// <summary>A blow (App. C.2): from outside the car, a third of what bursts it; from inside, nothing (it's thick there).</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        var hitter = ctx.Crew.FirstOrDefault(c => c.Player.Id == by).Player;
        if (Attached >= 0 && InCar(hitter.State, ctx.Train, Attached))
            return;
        Health -= 1;
        if (Health <= 0)
            Burst(ctx);
    }

    /// <summary>A gun's round (or a crewmate fired from the cannon) into it: it bursts.</summary>
    public override bool Hit(EnemyContext ctx, int by, double damage)
    {
        if (!Exposed)
            return false;
        Marked(ctx, by);
        Burst(ctx);
        return true;
    }

    /// <summary>Pinned the whole while: the weakest crushed to death, and it slides off sated.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Crushed);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}

/// <summary>
/// The Close's census (note 1013): once a director second, host only, each car with a crowd of the living crew on its floor
/// (<see cref="CloseTuning.CrowdFor"/>) and its doors shut, and how many seconds running it has been. Cars by id; no draws.
/// </summary>
public sealed class Crowding
{
    readonly SortedDictionary<int, double> _seconds = new();

    /// <summary>One second's count over <paramref name="world"/>'s crew (of <paramref name="crew"/>, the night's crew size).</summary>
    public void Second(World world, int crew, CloseTuning t)
    {
        var train = world.Train;
        var counts = new SortedDictionary<int, int>();
        foreach (var (_, s) in world.CrewThisTick)
            if (Close.InCar(s, train, s.Parent))
                counts[s.Parent] = counts.GetValueOrDefault(s.Parent) + 1;
        int crowd = t.CrowdFor(crew);
        foreach (int car in _seconds.Keys.ToList())
            if (counts.GetValueOrDefault(car) < crowd || !Close.Shut(train, car))
                _seconds.Remove(car);
        foreach (var (car, n) in counts)
            if (n >= crowd && Close.Shut(train, car))
                _seconds[car] = _seconds.GetValueOrDefault(car) + 1;
    }

    /// <summary>Seconds <paramref name="car"/> has been crowded and shut (0 for none).</summary>
    public double Of(int car) => _seconds.GetValueOrDefault(car);

    /// <summary>The car crowded and shut longest, at least <paramref name="atLeast"/> seconds (the lowest id on a tie); null for none.</summary>
    public int? Longest(double atLeast)
    {
        int? best = null;
        double longest = -1;
        foreach (var (car, s) in _seconds)
            if (s >= atLeast && s > longest)
                (best, longest) = (car, s);
        return best;
    }
}
