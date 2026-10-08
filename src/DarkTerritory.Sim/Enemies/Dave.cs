using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Dave, the wandering painter (the director, 8 Oct 2026; GDD §3.2; ARCHITECTURE §8 note 483). Just Dave. Some nights he's
/// at his easel out past a stop, painting the world as it was. Nothing out there hunts him (no creature's target is ever
/// anything but the crew), he's kind to whoever comes by, and he's patient. Not endlessly: a crewmate who strikes him
/// <see cref="DaveTuning.Blows"/> times is taken by the neck. His blows one to four are his telegraph (he says so, and at
/// the last of them he sets his brush down and turns), so the grab and the punish go through the spine as every kill does.
/// <para>
/// He's built on <see cref="Enemy"/> for its blows, replication and its one way to kill, and is a <see cref="Hazard"/> so the
/// director never counts, spends on or dismisses him. Replicated: <see cref="Enemy.Extra"/> is the blows of
/// <see cref="Enemy.Extra2"/>, the last crewmate to strike him (a client says his answer to that blow).
/// </para>
/// </summary>
public sealed class Dave(int id) : Enemy(id)
{
    // Blows by crewmate (player id): host-only, as the spine is. Sorted, so who he turns to is the same on every run.
    readonly SortedDictionary<int, int> _blows = [];
    int _due = -1;
    DaveTuning _t = new();

    public override EnemyKind Kind => EnemyKind.Dave;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override bool Hazard => true;
    public override bool Far => true;
    public override double MeleeRadius => _t.MeleeRadius;
    /// <summary>Only a hand's blow counts as striking him: no gun is laid on him, and a ball doesn't find him.</summary>
    public override bool Exposed => false;
    public override string Called => "Dave";

    /// <summary>The world yaw he faces, at his easel (−Z at 0, as a player's).</summary>
    public double Yaw => Lateral;
    /// <summary>How many times the last crewmate to strike him has (<see cref="Striker"/>); replicated.</summary>
    public int StrikerBlows => (int)Math.Round(Extra);
    public int Striker => (int)Math.Round(Extra2);

    /// <summary>Host: how many times <paramref name="player"/> has struck him tonight.</summary>
    public int BlowsBy(int player) => _blows.GetValueOrDefault(player);

    /// <summary>Dave at his easel at <paramref name="world"/>, facing <paramref name="yaw"/>.</summary>
    public static Dave At(int id, Double3 world, double lineHint, double yaw, DaveTuning t) =>
        new(id) { Attached = Loose, Local = world, Lateral = yaw, LineDistance = lineHint, Extra = 0, Extra2 = -1, _t = t };

    protected override void Tick(EnemyContext ctx)
    {
        _t = ctx.Tuning.Dave;
        // The one who's had their warning and struck him again: once he's stood turned long enough (the reaction window),
        // he takes them by the neck, close.
        if (_due >= 0 && Phase == SpinePhase.Telegraph)
        {
            if (ctx.Crew.All(c => c.Player.Id != _due || !c.Player.State.Alive))
                _due = -1;
            else if (Enter(ctx, SpinePhase.Commit) && Grab(ctx, _due, _t.GrabSeconds))
                _due = -1;
        }
        if (Phase == SpinePhase.Grab && Holding >= 0)
        {
            var facing = new Double3(-DMath.Sin(Yaw), 0, -DMath.Cos(Yaw));
            ctx.Carry(Holding, Local + facing * 0.55);
        }
        // Back to his painting once nobody living is on their last warning.
        if (Phase is SpinePhase.Punish or SpinePhase.BreakOff)
            Enter(ctx, SpinePhase.Dormant);
        else if (Phase == SpinePhase.Telegraph && _due < 0 && !Warned(ctx))
            Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>Whether anyone living has struck him all but the last time.</summary>
    bool Warned(EnemyContext ctx) =>
        ctx.Crew.Any(c => c.Player.State.Alive && _blows.GetValueOrDefault(c.Player.Id) >= _t.Blows - 1);

    /// <summary>
    /// Struck by a crewmate: counted against them. Their last warning turns him (the telegraph); the blow after it, he
    /// takes them. Nothing hurts him, and a blow on him while he holds somebody frees nobody.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        _t = ctx.Tuning.Dave;
        Marked(ctx, by);
        int n = _blows[by] = _blows.GetValueOrDefault(by) + 1;
        Extra = n;
        Extra2 = by;
        if (Phase is SpinePhase.Grab or SpinePhase.Punish)
            return;
        if (n >= _t.Blows - 1 && Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph);
        if (n >= _t.Blows && _due < 0)
            _due = by;
    }

    public override bool Hit(EnemyContext ctx, int by, double damage) => false;

    protected override void Rescued(EnemyContext ctx, int by)
    {
        // At a crew of one the held may struggle (the solo rule); he lets them go, and they're still on their last warning.
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }

    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Dave);
        _blows.Remove(victim);
    }

    protected override Run.Incident? Punished(EnemyContext ctx) => null;

    /// <summary>
    /// Where Dave is tonight, if he's anywhere (enemies.json <c>dave</c>): on <see cref="DaveTuning.Chance"/> of nights, out
    /// past one of the line's stops on the side away from its yard and village, a clear stretch of open ground in front of
    /// him, facing away from the line toward what he's painting. Seeded from the route, so every run of a night has him in
    /// the same place; null for none (no stops, or no ground that'll do).
    /// </summary>
    public static (Double3 At, double Along, double Yaw)? Site(World world, Route.Route route, DaveTuning t)
    {
        var dice = Streams.Rng(route.Seed, "dave");
        if (!dice.Chance(t.Chance))
            return null;
        var stops = route.Features.Where(f => f.Stop is not null && f.Start > route.Gate + t.PastGate).ToList();
        if (stops.Count == 0)
            return null;
        var train = world.Train;
        var stop = stops[(int)(dice.NextDouble() * stops.Count)];
        var layout = stop.Stop!;
        // The open side: away from the village (or the yard), as he'd have it.
        int side = layout.HasVillage ? -layout.VillageSide : layout.HasYard ? -layout.YardSide : dice.Chance(0.5) ? 1 : -1;
        if (side == 0)
            side = 1;
        for (int tries = 0; tries < 16; tries++)
        {
            var p = new Stops.Pt(layout.StopPoint.S + dice.Range(t.Along), side * dice.Range(t.Out));
            var at = Run.Run.StopWorld(train.Line, stop, p);
            double hint = stop.Start + p.S;
            double ground = PlayerMotor.GroundAt(at, train.Line, ref hint);
            var spot = at with { Y = ground };
            if (Combat.Guns.Water(train, spot) is { } w && w > ground + 0.2)
                continue;
            if (Moose.TrackOff(train, spot, hint) < t.TrackClearance || world.InFort(spot) || !Open(train, spot, t.Room))
                continue;
            // Facing out, away from the line, at what he paints.
            var line = train.Line.Sample(hint);
            var right = Double3.Cross(line.Tangent, Double3.Up).Normalized * side;
            return (spot, hint, Math.Atan2(-right.X, -right.Z));
        }
        return null;
    }

    /// <summary>No stop building's wall within <paramref name="room"/> metres (level) of a spot.</summary>
    static bool Open(Train.TrainOnLine train, Double3 at, double room)
    {
        if (train.Walls is not { } walls)
            return true;
        foreach (double dx in (double[])[-room, 0, room])
            foreach (double dz in (double[])[-room, 0, room])
            {
                var p = at + new Double3(dx, 1, dz);
                foreach (var w in walls.Near(p))
                {
                    var local = w.ToLocal(p);
                    if (Math.Abs(local.X) <= w.HalfLength && Math.Abs(local.Z) <= w.HalfWidth && p.Y >= w.Bottom && p.Y <= w.Top)
                        return false;
                }
            }
        return true;
    }
}

/// <summary>enemies.json <c>dave</c> (note 483). Field docs live in that file.</summary>
public sealed record DaveTuning
{
    public double Chance { get; init; } = 0.3;
    public double PastGate { get; init; } = 1500;
    public double[] Out { get; init; } = [14, 26];
    public double[] Along { get; init; } = [-30, 30];
    public double TrackClearance { get; init; } = 9;
    public double Room { get; init; } = 3;
    public int Blows { get; init; } = 5;
    public double GrabSeconds { get; init; } = 0.4;
    public double MeleeRadius { get; init; } = 0.7;
}
