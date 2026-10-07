using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The switch audit (ARCHITECTURE §8 note 289; the director, 7 Oct: "in a recent playtest I felt like they weren't working").
/// Each case here reproduced something broken in the switch mechanics, end to end.
/// </summary>
public class SwitchAuditTests
{
    static (World World, TrainOnLine Train, int Branch) AtADeadLine(double ahead)
    {
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 4).Build();
        int branch = line.Branches.First(b => b.Kind == BranchKind.DeadLine).Index;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, line.Branches[branch].Toe - ahead);
        var world = new World(train, Tuning.Combat);
        world.EnableSwitches(Tuning.Route.Junctions);
        var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
        world.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        return (world, train, branch);
    }

    [Fact]
    public void SettingASwitchThatIsAlreadySetSaysItIsSet()
    {
        // World.SetSwitch is the hook for whatever sets a switch without a hand on the stand (the Switchman, set pieces): it
        // says whether the switch now stands as asked. It said "false" for one already set that way, which the Switchman read
        // as "a wheel's on the points" and gave up on the junction a tick after throwing it.
        var (world, train, branch) = AtADeadLine(800);
        Assert.True(world.SetSwitch(branch, true));
        Assert.True(world.SetSwitch(branch, true));
        Assert.True(train.Diverging(branch));
        // A wheel on the points: it can't be set the other way, and says so.
        var (held, heldTrain, b) = AtADeadLine(-10);
        Assert.False(held.SetSwitch(b, true));
        Assert.False(heldTrain.Diverging(b));
    }

    [Fact]
    public void ARoutingSwitchmanStaysAtTheLeverItThrewUntilTheTrainComes()
    {
        // App. A.7: it throws the dead line's points and stands at the stand with its lantern (the telegraph) until the train
        // is on it. Before the fix it threw them and was gone the next tick: the points stayed wrong and nothing was there.
        var (world, train, branch) = AtADeadLine(900);
        world.AddEnemy(i => Switchman.At(i, train.Line.Branches[branch], Tuning.Enemies.Switchman, Tuning.Route.Junctions.LeverOffset, derail: false));
        for (int i = 0; i < SimConstants.TickRate * 2; i++)
        {
            world.BeginTick();
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
        }
        Assert.True(train.Diverging(branch));
        var man = Assert.Single(world.ActiveEnemies.OfType<Switchman>());
        Assert.False(man.Gone);
        Assert.Equal(SpinePhase.Dormant, man.Phase);
    }

    [Fact]
    public void AClientThrowsASwitchByHandAndEveryoneSeesItAndTheTrainTakesIt()
    {
        // GDD §17 over the wire: a joining client stands at the stand and holds Use (intent only); the host throws it; the
        // other client's switch (its lamp and lever) follows, and the train the host runs goes where it's set.
        const double toe = 1000;
        var j = Tuning.Route.Junctions;
        RailLine Line() => new(new LineDefinition("switch", [new TrackSegment(4000)]),
            [new BranchDefinition(BranchKind.DeadLine, toe, +1,
                [new TrackSegment(j.DivergeLength, -j.DivergeRadius), new TrackSegment(j.DivergeLength, j.DivergeRadius), new TrackSegment(500)])]);
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), Line(), toe - 200);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, Tuning.Player);
        host.World.EnableBodies(); // the host world simulates (as NetPlaySession has it)
        var clients = new[] { new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player), new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player) };
        bool holding = false;
        void Run(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(i == 0 && holding ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
            }
        }
        Run(30);
        byte thrower = clients[0].PlayerId!.Value;
        var lever = host.World.Switches!.LeverAt(host.Train.Line, 0);
        var ground = PlayerMotor.SpawnOnGround(lever with { X = lever.X + 0.8 } - Double3.Up * 0.9, host.Train.Line, toe, Tuning.Player);
        host.SetPlayerState(thrower, ground);
        Run(30);
        // The thrower's own world offers the lever (the HUD's prompt reads it).
        Assert.Equal(0, clients[0].World.Switches!.InReach(clients[0].Predicted, clients[0].Train));
        holding = true;
        Run((int)((j.ThrowSeconds + 0.5) * SimConstants.TickRate));
        holding = false;
        Run(15);
        Assert.True(host.Train.Diverging(0), "the host never threw it");
        Assert.All(clients, c => Assert.True(c.Train.Diverging(0), "a client's switch didn't follow the host's"));
        // And the train goes down the branch, on the host and as every client sees it.
        host.Train.Dynamics.Velocity = 5;
        Run(SimConstants.TickRate * 60);
        Assert.Equal(0, host.Train.Dynamics.Path);
        Assert.All(clients, c => Assert.Equal(0, c.Train.Dynamics.Path));
    }

    [Fact]
    public void ABotDriverAtLineSpeedStopsShortOfAJunctionSetWrongAhead()
    {
        // frontier:7's harness night (8 bots, 9 cars) took both of its Switchmen's dead lines and backed out of each: the
        // driver planned its stop on the bare brake, but with steam driving (T97) the engine pulls against the brake, so it set
        // off for the points too late. Here the dead line's points are thrown 600 m ahead of a train at line speed, as the
        // Switchman throws them.
        var content = DataFile.FindContentRoot();
        var route = LineGen.Routes.Generate(content, "frontier:7", 9);
        var line = route.Build();
        var dead = line.Branches.First(b => b.Kind == BranchKind.DeadLine);
        bool thrown = false, tookIt = false;
        double speed = 0;
        Harness.Run(line, Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 8,
            Cars = 9,
            Seconds = 300,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            // Past the first facility's spur, so the run to the points is open line.
            StartDistance = dead.Toe - 1150,
            Combat = Tuning.Combat,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)),
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Holdouts = Tuning.Holdouts,
            Script = (_, w) =>
            {
                if (!thrown && dead.Toe - w.Train.Dynamics.Distance < 600)
                {
                    thrown = w.SetSwitch(dead.Index, true);
                    speed = w.Train.Dynamics.Speed;
                }
                tookIt |= w.Train.Dynamics.Path == dead.Index && w.Train.Dynamics.Distance > dead.Toe;
            },
            Until = w => w.Train.OnMain && w.Train.Dynamics.Distance > dead.Toe + 200,
        }, Tuning.Boiler);
        Assert.True(thrown);
        Assert.True(speed > 12, $"only {speed:0.0} m/s when the points were thrown");
        Assert.False(tookIt, "the train ran onto the dead line");
    }
}
