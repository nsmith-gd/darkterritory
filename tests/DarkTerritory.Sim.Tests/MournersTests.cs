using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Mourners (GDD §21, App. A.6, B.6; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 362). Rule: stand over
/// your dead, or carry them home. They come only after a death, never touch the living, drag a body left alone straight
/// off from the line, drop it and scatter for a crewmate close or a blow, die to a blow, follow a body carried, and a body
/// hauled far enough is gone.
/// </summary>
public class MournersTests
{
    static readonly MournersTuning M = Tuning.Enemies.Mourners;
    static readonly PlayerTuning P = Tuning.Player;

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Ground(Night n, Double3 at, Double3? toward = null)
    {
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        if (toward is { } to)
        {
            var d = to - at;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
        }
        return s;
    }

    static Body Corpse(Night n, Double3 at, int owner = 3) =>
        n.World.Bodies.SpawnRagdoll(n.Train, owner, new PlayerState { Parent = PlayerState.World, Position = at + Double3.Up * 0.3, LineHint = n.Train.Dynamics.Distance });

    /// <summary>The night with its bodies stepped too (what's hauled drags behind on the ground).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds, intent);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    static List<Mourner> Mourners(Night n) => [.. n.World.ActiveEnemies.OfType<Mourner>().Where(m => !m.Gone)];

    static double OffLine(Night n, Double3 at)
    {
        double hint = n.Train.Dynamics.Distance;
        var (path, d) = n.Train.Line.Nearest(at, ref hint);
        return ((at - n.Train.Line.Sample(path, d).Position) with { Y = 0 }).Length;
    }

    [Fact]
    public void NoneComeWithoutADeath()
    {
        var n = new Night(4, speed: 0);
        n.Crew[1] = Ground(n, Beside(n, 2, 6));
        Run(n, 60);
        Assert.Empty(Mourners(n));
    }

    [Fact]
    public void ABodyLeftLyingBringsAGroupByTheTierAndTheyNeverHarmAnyone()
    {
        var n = new Night(4, speed: 0);
        var body = Corpse(n, Beside(n, 2, 8));
        n.Crew[1] = Ground(n, Beside(n, 2, 8, 12));
        Run(n, M.After - 2);
        Assert.Empty(Mourners(n));
        Run(n, 3);
        var group = Mourners(n);
        Assert.Equal(M.CountFor(Route.RouteTier.Local), group.Count);
        Assert.All(group, m => Assert.Equal(body.Id, m.BodyId));
        // They come from out past the body, away from the line.
        Assert.All(group, m => Assert.True(OffLine(n, m.Local) > OffLine(n, Bodies.WorldCentre(body, n.Train))));
        Run(n, 60);
        Assert.Equal(P.Health, n.Crew[1].Health);
        Assert.True(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void LeftAloneTheyDragItStraightOffFromTheLineAndItsGone()
    {
        var n = new Night(4, speed: 0);
        var body = Corpse(n, Beside(n, 2, 8));
        Run(n, M.After + 1);
        Assert.NotEmpty(Mourners(n));
        double before = OffLine(n, Bodies.WorldCentre(body, n.Train));
        Run(n, 30);
        Assert.Contains(Mourners(n), m => body.TakenBy == m.Id);
        Assert.True(OffLine(n, Bodies.WorldCentre(body, n.Train)) > before + 15);
        // Far enough out, it's gone (its refund with it), and so are they.
        Run(n, (M.LostAt + 10) / M.Drag + M.LeaveSeconds);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Id == body.Id);
        Assert.Equal(1, n.World.MournersTook);
        Assert.Empty(Mourners(n));
    }

    [Fact]
    public void StandOverItAndTheyNeverTakeIt()
    {
        var n = new Night(4, speed: 0);
        var at = Beside(n, 2, 8);
        var body = Corpse(n, at);
        n.Crew[1] = Ground(n, Beside(n, 2, 8, 2));
        Run(n, M.After + 60);
        Assert.NotEmpty(Mourners(n));
        Assert.Equal(-1, body.TakenBy);
        // They keep off the living.
        Assert.All(Mourners(n), m => Assert.True(((m.Local - n.World.Bodies.All.First(b => b.Id == body.Id).Centre) with { Y = 0 }).Length >= 2));
    }

    [Fact]
    public void ComeUpOnTheDraggersAndTheyDropItAndScatter()
    {
        var n = new Night(4, speed: 0);
        var body = Corpse(n, Beside(n, 2, 8));
        Run(n, M.After + 25);
        var hauler = Mourners(n).Single(m => body.TakenBy == m.Id);
        var bodyAt = Bodies.WorldCentre(body, n.Train);
        n.Crew[1] = Ground(n, bodyAt + (Beside(n, 2, 0) - bodyAt).Normalized * (M.DropWithin - 0.5));
        Run(n, 0.5);
        Assert.Equal(-1, body.TakenBy);
        Assert.Equal(MournerMode.Startle, hauler.Mode);
    }

    [Fact]
    public void ABlowKillsOneAndTheRestScatter()
    {
        var n = new Night(4, speed: 0);
        var body = Corpse(n, Beside(n, 2, 8));
        Run(n, M.After + 25);
        var hauler = Mourners(n).Single(m => body.TakenBy == m.Id);
        // Stand at its back (it's facing the body) and swing: a crowbar's blow kills it.
        var at = hauler.Local;
        var facing = new Double3(-DMath.Sin(hauler.Lateral), 0, -DMath.Cos(hauler.Lateral));
        n.Crew[2] = Ground(n, at - facing * 1.4, toward: at);
        int before = Mourners(n).Count;
        Run(n, 0.3, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(hauler.Gone);
        Assert.Equal(before - 1, Mourners(n).Count);
        Assert.Equal(-1, body.TakenBy);
        Assert.All(Mourners(n), m => Assert.Equal(MournerMode.Startle, m.Mode));
    }

    [Fact]
    public void ABodyCarriedIsFollowedNeverTakenAndHomeSendsThemOff()
    {
        var n = new Night(4, speed: 0);
        var body = Corpse(n, Beside(n, 2, 8));
        Run(n, M.After + 3);
        Assert.NotEmpty(Mourners(n));
        // Picked up: carried, it's never theirs.
        n.Crew[1] = Ground(n, Bodies.WorldCentre(body, n.Train));
        body.Carrier = 1;
        Run(n, 20);
        Assert.Equal(-1, body.TakenBy);
        Assert.All(Mourners(n), m => Assert.Equal(MournerMode.Follow, m.Mode));
        // Home: into a car of the train.
        body.Carrier = -1;
        n.World.Bodies.TakeAlong(body, n.Train, -1, 2, new Double3(0, 1.4, 0), 0);
        n.World.Bodies.LetGo(body);
        Run(n, M.LeaveSeconds + 1);
        Assert.Empty(Mourners(n));
        Assert.Contains(n.World.Bodies.All, b => b.Id == body.Id);
    }

    [Fact]
    public void TheyCostTheDirectorsCapsNothing()
    {
        var n = new Night(4, speed: 0);
        Corpse(n, Beside(n, 2, 8));
        Run(n, M.After + 5);
        Assert.NotEmpty(Mourners(n));
        Assert.All(Mourners(n), m => Assert.False(Director.Engaged(m)));
    }
}
