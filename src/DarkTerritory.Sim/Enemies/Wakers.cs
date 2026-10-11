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
    public bool Holds => Phase == SpinePhase.Punish;

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

    /// <summary>How far short of the terminus's gate they stop: in under the guns, the crew's home.</summary>
    static double Wall(World world, WakersTuning t) => world.Run is { } run
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
                    // Whoever's in an eaten car goes with it; whoever's off the train is taken by the rest of them.
                    var gone = new HashSet<int>();
                    for (int i = 0; i < eaten; i++)
                        gone.Add(cars[cars.Count - 1 - i].Id);
                    foreach (var (p, _) in ctx.Crew)
                    {
                        var s = p.State;
                        if (!s.Alive)
                            continue;
                        if (gone.Contains(s.Parent) || s.Parent < 0 && PhaseSeconds >= t.GroundSeconds)
                            ctx.Harm(p.Id, int.MaxValue / 2, DeathCause.Woken);
                    }
                    if (eaten >= cars.Count)
                        ctx.World.TakenAtDawn = true;
                    return;
                }
        }
    }
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
    public double GroundSeconds { get; init; } = 8;

    /// <summary>How many rise for a night of <paramref name="tier"/>.</summary>
    public int For(RouteTier tier) => Count.Length == 0 ? 0 : Count[Math.Clamp((int)tier, 0, Count.Length - 1)];
}
