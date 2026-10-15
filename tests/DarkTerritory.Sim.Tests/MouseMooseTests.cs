using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Mouse Moose (GDD §21; the director, 15 Oct 2026; ARCHITECTURE §8 note 790). Rule: keep an eye on the castings, and
/// stand by the one it wants. With nobody about it drags a casting out of the crane's reach (nobody can load it then) and on
/// to its hole (gone); within 6 m of a crewmate, or struck, it lets go and bolts, hides and comes back for the same one; the
/// fifth scare off one and it goes for another. It never hurts anyone, and it goes when the train does.
/// </summary>
public class MouseMooseTests
{
    static readonly MouseMooseTuning M = Tuning.Enemies.MouseMoose;
    static readonly CraneTuning C = FacilityTests.F.Crane;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>
    /// A gantry beside the standing train (its layout point 20 m back from the engine's front, the stack on the right), and a
    /// Mouse Moose come out of its hole for casting 0.
    /// </summary>
    static (Crane Crane, MouseMoose Moose) Stage(Night n)
    {
        var line = n.Train.Line;
        double origin = n.Train.Dynamics.Distance - 20;
        var crane = new Crane(C, (along, lateral, up) =>
        {
            var s = line.Sample(origin + along);
            var right = Double3.Cross(s.Tangent, Double3.Up).Normalized;
            var at = s.Position + right * lateral;
            double hint = origin + along;
            return at with { Y = PlayerMotor.GroundAt(at, line, ref hint) + up };
        });
        var (hole, hint) = MouseMoose.HoleFor(n.World, crane.Castings[0].At, M);
        var moose = n.World.AddEnemy(id => MouseMoose.At(id, crane, 0, hole, hint));
        return (crane, moose);
    }

    static Night Standing(MouseMooseTuning? m = null) =>
        new(4, speed: 0, enemies: m is null ? null : Tuning.Enemies with { MouseMoose = m });

    static PlayerState Ground(Night n, Double3 at) => PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    [Fact]
    public void WithNobodyAboutItDragsACastingOutOfTheCranesReachAndOffToItsHole()
    {
        var n = Standing();
        var (crane, moose) = Stage(n);
        var stacked = crane.Castings[0].At;
        Assert.True(crane.Reaches(stacked));
        // Out of its hole and in to the casting, its teeth in it, and dragging it: very slowly.
        double creep = Flat(moose.Local - stacked) / M.Creep;
        n.Run(creep + M.BiteSeconds + 2);
        Assert.Equal(MouseMooseMode.Drag, moose.Mode);
        Assert.InRange(Flat(crane.Castings[0].At - stacked), 0.05, 4 * M.Drag);
        // Dragged on until the hook can't be put to it: taken, nobody's to rig, and the crane has one fewer to load.
        for (int i = 0; i < 120 && crane.Castings[0].State == CastingState.Stacked; i++)
            n.Run(1);
        Assert.Equal(CastingState.Taken, crane.Castings[0].State);
        Assert.False(crane.Reaches(crane.Castings[0].At));
        Assert.Equal(C.Castings - 1, crane.Left);
        Assert.Null(crane.Riggable(crane.Castings[0].At));
        // On to its hole, and gone.
        n.Run(Flat(moose.Hole - crane.Castings[0].At) / M.DragAway + 2);
        Assert.Equal(CastingState.Lost, crane.Castings[0].State);
        Assert.Equal(1, n.World.MouseMooseTook);
        Assert.Equal(MouseMooseMode.Hide, moose.Mode);
        n.AssertFair();
    }

    [Fact]
    public void ACrewmateStandingByItsCastingKeepsItOffAndTheFifthScareSendsItForAnother()
    {
        // Hidden only a moment, so its five tries come round quickly.
        var n = Standing(M with { HideSeconds = 2 });
        var (crane, moose) = Stage(n);
        var guarded = crane.Castings[0].At;
        // Stood by the casting, on the side it comes from: it comes within 6 m of them on its way in, every time.
        var toHole = (moose.Hole - guarded) with { Y = 0 };
        n.Crew[1] = Ground(n, guarded + toHole.Normalized * 1.5);
        var seen = new List<int>();
        for (int i = 0; i < 400 && moose.Casting == 0; i++)
        {
            n.Run(0.5);
            if (seen.Count == 0 || seen[^1] != moose.Scares)
                seen.Add(moose.Scares);
        }
        // Scared off it four times, coming back for it each time; the fifth, and it's after the other one.
        Assert.Equal([0, 1, 2, 3, 4, 0], seen);
        Assert.Equal(1, moose.Casting);
        // The guarded casting never moved, and nobody was hurt.
        Assert.Equal(CastingState.Stacked, crane.Castings[0].State);
        Assert.Equal(0, Flat(crane.Castings[0].At - guarded), 6);
        Assert.Equal(P.Health, n.Crew[1].Health);
        n.AssertFair();
    }

    [Fact]
    public void ScaredOffWhileDraggingItLetsGoAndComesBackForTheSameOne()
    {
        var quick = M with { HideSeconds = 3 };
        var n = Standing(quick);
        var (crane, moose) = Stage(n);
        n.Run(Flat(moose.Local - crane.Castings[0].At) / M.Creep + M.BiteSeconds + 4);
        Assert.Equal(MouseMooseMode.Drag, moose.Mode);
        var dropped = crane.Castings[0].At;
        // A crewmate walks up within 6 m: it bolts, the casting left where it was.
        n.Crew[1] = Ground(n, moose.Local + new Double3(0, 0, M.ScareWithin - 1));
        n.Run(0.2);
        Assert.Equal(MouseMooseMode.Flee, moose.Mode);
        Assert.Equal(1, moose.Scares);
        n.Run(1);
        Assert.Equal(0, Flat(crane.Castings[0].At - dropped), 3);
        // They walk off; it comes back out for the same casting.
        n.Crew.Remove(1);
        n.Run(Flat(moose.Hole - moose.Local) / M.Run + quick.HideSeconds + 1);
        Assert.Equal(MouseMooseMode.Creep, moose.Mode);
        Assert.Equal(0, moose.Casting);
        Assert.Equal(1, moose.Scares);
    }

    [Fact]
    public void ABlowScaresItOffAndNeverKillsIt()
    {
        var n = Standing();
        var (_, moose) = Stage(n);
        var ctx = new EnemyContext { Tuning = Tuning.Enemies, World = n.World };
        double health = moose.Health;
        for (int i = 0; i < 6; i++)
            moose.Struck(ctx, by: 1, damage: 10);
        Assert.False(moose.Gone);
        Assert.Equal(health, moose.Health);
        Assert.Equal(MouseMooseMode.Flee, moose.Mode);
        // One blow, one scare: blows while it's running don't count.
        Assert.Equal(1, moose.Scares);
    }

    [Fact]
    public void ABotOnFootNearItsCastingGoesAtItAndItBolts()
    {
        var n = Standing();
        var (crane, moose) = Stage(n);
        n.Run(Flat(moose.Local - crane.Castings[0].At) / M.Creep + M.BiteSeconds + 3);
        Assert.Equal(MouseMooseMode.Drag, moose.Mode);
        // A bot on the ground 20 m off, along the line from the casting: it goes at it, and it lets go and bolts.
        var off = crane.Castings[0].At + n.Train.Line.Sample(n.Train.Dynamics.Distance).Tangent * 20;
        n.Crew[2] = Ground(n, off);
        bool bolted = false;
        for (int i = 0; i < 40 && !bolted; i++)
        {
            n.Run(0.25, id => Bots.Heed.MouseMoose(default, n.Crew[id], n.World));
            bolted = moose.Mode == MouseMooseMode.Flee;
        }
        Assert.True(bolted, $"the Mouse Moose is {moose.Mode}");
        Assert.Equal(CastingState.Stacked, crane.Castings[0].State);
        Assert.Equal(P.Health, n.Crew[2].Health);
    }

    [Fact]
    public void ItGoesWhenTheTrainLeavesTheYard()
    {
        var n = Standing();
        var (_, moose) = Stage(n);
        n.Run(5);
        Assert.False(moose.Gone);
        n.Train.Dynamics.Velocity = 20;
        n.Run(30);
        Assert.True(moose.Gone);
    }

    [Fact]
    public void TheDirectorSendsItToAStandingTrainAtAYardWithCastingsToLoad()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        stop.World.EnableEnemies(Tuning.Enemies, null, 1, crew: 4, authority: true);
        var ctx = new SpawnContext(stop.World, Tuning.Enemies, stop.World.Director!);
        var rule = Spawns.For(EnemyKind.MouseMoose)!;
        Assert.NotNull(rule.Weight(ctx));
        Assert.True(rule.Spawn(ctx));
        var moose = Assert.Single(stop.World.ActiveEnemies.OfType<MouseMoose>());
        var crane = stop.Site.Crane!;
        Assert.Same(crane, moose.Crane);
        // Out past the castings, out of the hook's reach: it comes in to them.
        Assert.False(crane.Reaches(moose.Hole));
        Assert.Equal(CastingState.Stacked, crane.Castings[moose.Casting].State);
        // One at a stop.
        Assert.Null(rule.Weight(ctx));
    }
}
