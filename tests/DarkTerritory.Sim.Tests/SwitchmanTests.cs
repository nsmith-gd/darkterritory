using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Switchman (T37, App. A.7): throws a junction ahead for its dead line; costs the clock, never a life.</summary>
public class SwitchmanTests
{
    static readonly SwitchmanTuning S = Tuning.Enemies.Switchman;

    /// <summary>A frontier route with a junction network (App. B.7: three dead lines or more), and its first dead line.</summary>
    static (Route.Route Route, Branch DeadLine) Network()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            if (route.Branches.Count(b => b.Kind == BranchKind.DeadLine) < S.MinJunctions)
                continue;
            var line = route.Build();
            return (route, line.Branches.First(b => b.Kind == BranchKind.DeadLine));
        }
        throw new InvalidOperationException("no frontier route has a junction network");
    }

    sealed class Line
    {
        public readonly World World;
        public readonly Branch DeadLine;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<EnemyEvent> Events = new();

        /// <summary>Running at <paramref name="speed"/> toward the first dead line's points, <paramref name="short"/> metres short.</summary>
        public Line(double speed, double @short)
        {
            var (route, deadLine) = Network();
            var line = route.Build();
            DeadLine = line.Branches[deadLine.Index];
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, DeadLine.Toe - @short);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            World.EnableSwitches(Tuning.Route.Junctions);
            // No level content (the route's Sleepers) and the director held off: just the Switchman, sent by the test.
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 4, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public Switchman Send() => World.AddEnemy(id => Switchman.At(id, DeadLine, S, Tuning.Route.Junctions.LeverOffset));

        public void Run(double seconds, bool holdSpeed = true)
        {
            double speed = Train.Dynamics.Velocity;
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                if (holdSpeed)
                    Train.Dynamics.Velocity = speed;
                World.BeginTick();
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, default, id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                Events.AddRange(World.EnemyEvents);
            }
        }
    }

    [Fact]
    public void ItThrowsTheJunctionAheadForItsDeadLine()
    {
        var line = new Line(speed: 14, @short: 900);
        Assert.Equal(line.DeadLine.Index, Switchman.Junction(line.World, S)?.Index);
        var switchman = line.Send();
        line.Run(0.5);
        // The telegraph: the switch reads wrong from the cab, and it stands there beside the stand.
        Assert.True(line.Train.Diverging(line.DeadLine.Index));
        Assert.Equal(SpinePhase.Telegraph, switchman.Phase);
        var stand = line.World.Switches!.LeverAt(line.Train.Line, line.DeadLine.Index);
        Assert.InRange((switchman.WorldPosition(line.Train) - stand).Length, 0.5, 3);
    }

    [Fact]
    public void LeftAloneTheTrainTakesTheDeadLineAndNobodyDies()
    {
        var line = new Line(speed: 14, @short: 900);
        var switchman = line.Send();
        line.Run(900 / 14.0 + 3);
        Assert.Equal(line.DeadLine.Index, line.Train.Dynamics.Path);
        Assert.Equal(SpinePhase.Punish, switchman.Phase);
        // App. A.1: it telegraphed for at least the reaction window before committing (a whole minute, at cruise).
        var commit = Assert.Single(line.Events, e => e.EnemyId == switchman.Id && e.To == SpinePhase.Commit);
        Assert.Equal(SpinePhase.Telegraph, commit.From);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds, $"telegraphed {commit.SecondsInFrom:0.0} s");
        Assert.False(line.World.Derailed);
        // Its work's done; it lingers, then it's gone.
        line.Run(S.LingerSeconds + 1, holdSpeed: false);
        Assert.True(switchman.Gone);
    }

    [Fact]
    public void ApproachedOnTheGroundItGoesAndLeavesTheSwitchWrong()
    {
        var line = new Line(speed: 0, @short: 900);
        var switchman = line.Send();
        line.Run(0.5);
        var at = switchman.WorldPosition(line.Train);
        line.Crew[1] = PlayerMotor.SpawnOnGround(at + new Double3(S.FleeRadius - 5, 0, 0), line.Train.Line, line.DeadLine.Toe, Tuning.Player);
        line.Run(0.5);
        Assert.True(switchman.Gone);
        Assert.Contains(line.Events, e => e.EnemyId == switchman.Id && e.To == SpinePhase.BreakOff);
        // Chasing it off doesn't set the switch back: somebody still has to throw it.
        Assert.True(line.Train.Diverging(line.DeadLine.Index));
    }

    [Fact]
    public void SetBackItsLostThatOne()
    {
        var line = new Line(speed: 14, @short: 900);
        var switchman = line.Send();
        line.Run(0.5);
        line.World.SetSwitch(line.DeadLine.Index, false);
        line.Run(0.5);
        Assert.True(switchman.Gone);
        line.Run(900 / 14.0 + 3);
        Assert.Equal(RailLine.MainPath, line.Train.Dynamics.Path);
    }

    [Fact]
    public void ItOnlyGoesWhereThereIsTimeToSeeIt()
    {
        // Inside the window's near edge there's no junction for it: the lamp couldn't be seen and the train stopped in time.
        var near = new Line(speed: 14, @short: S.Ahead[0] - 50);
        Assert.Null(Switchman.Junction(near.World, S));
        var far = new Line(speed: 14, @short: S.Ahead[1] + 50);
        Assert.NotEqual(far.DeadLine.Index, Switchman.Junction(far.World, S)?.Index);
    }

    [Fact]
    public void BackedOffTheDeadLineWellShortOfTheHoldTheTrainStillStandsForTheSwitch()
    {
        // T59: backing off a dead line, the driver stops wherever the train's clear of the points, which can be well short of
        // the hold. The hand setting the switch back has to count that as standing for it, or the night never goes on.
        var line = new Line(speed: 0, @short: 60);
        var plan = new Bots.SwitchPlan(line.DeadLine, line.DeadLine.Toe - Tuning.Route.Junctions.PointsLength - 2);
        Assert.True(plan.Hold - line.Train.Dynamics.Distance > 3);
        Assert.True(plan.StandingAt(line.Train));
        // Over the points: not standing for it.
        var over = new Line(speed: 0, @short: -5);
        Assert.False(plan.StandingAt(over.Train));
    }
}
