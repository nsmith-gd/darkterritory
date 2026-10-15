using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The tiny caboose (ARCHITECTURE §8 note 713; the director, 14 Oct 2026: "let's absolutely add collision for the little
/// caboose"): a body in the sim towed on its string behind the guard van, on the ground, off the rails on a bend, dragged.
/// </summary>
public class CabooseTests
{
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly TrainTuning Fitted = Campaign.Campaign.Apply(C, ["tinyCaboose"], new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;
    static CabooseTuning T => Fitted.Composition.Caboose;

    static World Night(TrainTuning t, RailLine line, double at = 1_000)
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, at), Tuning.Combat);
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static void Run(World w, double speed, double seconds, Action? each = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.Train.Dynamics.Velocity = speed;
            w.Step(new TrainControls { Reverser = 1 });
            w.Train.Dynamics.Velocity = speed;
            w.StepBodies([]);
            each?.Invoke();
        }
    }

    static Body Toy(World w) => Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.Caboose);

    [Fact]
    public void ItsTowedOnItsStringAlongTheGroundAtSpeed()
    {
        var straight = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        Assert.DoesNotContain(Night(Tuning.Train, straight).Bodies.All, b => b.Kind == BodyKind.Caboose);
        var w = Night(Fitted, straight);
        var toy = Toy(w);
        Assert.Equal(PlayerState.World, toy.Parent);
        Run(w, 15, 10);
        var tie = Bodies.TowPoint(w.Train, T)!.Value;
        var front = toy.Pbd.Particles[0].Position;
        // Behind the van on its string, kept up with the train, never further than the string.
        Assert.InRange((front - tie).Length, T.Rope - 0.2, T.Rope + 1e-6);
        Assert.True(Double3.Dot(front - tie, w.Train.Frames[^1].Back) > 0, "it's behind the train");
        // Kept up with: a second's run is the train's.
        var was = toy.Pbd.Particles[0].Position;
        Run(w, 15, 1);
        Assert.InRange((toy.Pbd.Particles[0].Position - was).Length, 14, 16);
        front = toy.Pbd.Particles[0].Position;
        tie = Bodies.TowPoint(w.Train, T)!.Value;
        // On the ground, not through it: resting on its wheels.
        double hint = w.Train.Dynamics.Distance;
        double ground = PlayerMotor.GroundAt(front, w.Train.Line, ref hint);
        Assert.InRange(front.Y - ground, T.Radius - 0.05, T.Radius + 0.05);
        Assert.Equal(0, toy.Tipped);
        // Nobody picks it up: it's for dragging.
        var s = PlayerMotor.SpawnOnRoof(w.Train, 3, 0, Tuning.Player) with { Parent = PlayerState.World, Position = front + Double3.Up * 0.5 };
        Assert.NotEqual(toy, w.Bodies.InReach(s, w.Train));
    }

    [Fact]
    public void OnABendTooFastItComesOffAndIsDraggedTillACrewmatePutsItBack()
    {
        // A 120 m bend at 14 m/s: 1.6 m/s² sideways, more than the toy can take. Slowly (5 m/s: 0.2), it stays on.
        var bend = new RailLine(new LineDefinition("t", [new TrackSegment(1_200), new TrackSegment(500, Radius: 120), new TrackSegment(5_000)]));
        var slow = Night(Fitted, bend);
        bool offSlow = false;
        Run(slow, 5, 120, () => offSlow |= Toy(slow).Tipped != 0);
        Assert.False(offSlow, "it came off at a crawl");
        var w = Night(Fitted, bend);
        bool off = false;
        Run(w, 14, 60, () => off |= Toy(w).Tipped != 0);
        Assert.True(off, "it never came off on the bend");
        Assert.True(off, "it never came off on the bend");
        // Off, it stays off, dragged on its string behind the train.
        var toy = Toy(w);
        Assert.NotEqual(0, toy.Tipped);
        Assert.InRange((toy.Pbd.Particles[0].Position - Bodies.TowPoint(w.Train, T)!.Value).Length, 0, T.Rope + 1e-6);
        // Stopped, a crewmate off the platform's end puts it back on the rails: on the line's middle again.
        Run(w, 0, 3);
        var s = new PlayerState { Parent = PlayerState.World, Position = toy.Pbd.Particles[0].Position + new Double3(0.3, 0, 0.3), Health = 100 };
        Assert.NotNull(w.Bodies.CabooseOff(s, w.Train, T));
        Assert.True(w.Bodies.Rerail(s, w.Train, T));
        Assert.Equal(0, toy.Tipped);
        Run(w, 3, 2);
        Assert.Equal(0, toy.Tipped);
        Assert.False(w.Bodies.Rerail(s, w.Train, T));
    }

    [Fact]
    public void ItStopsAtAWallAsAnyBodyDoesAndEveryClientSeesItOver()
    {
        // The string only pulls: the train stopped, the toy rolls to rest and stays, the string slack.
        var straight = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = Night(Fitted, straight);
        Run(w, 10, 3);
        Run(w, 0, 4);
        var tie = Bodies.TowPoint(w.Train, T)!.Value;
        Assert.True((Toy(w).Pbd.Particles[0].Position - tie).Length <= T.Rope + 1e-6);
        // Over the wire: a client's body is over the same way.
        Toy(w).Tipped = -1;
        var controls = new TrainControls();
        var records = Net.WorldRecords.Capture(w, controls, []);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Fitted, 4, 1)), straight, 1_000), Tuning.Combat);
        Net.WorldRecords.Apply(records, client, ref controls, []);
        Assert.Equal(-1, Assert.Single(client.Bodies.All, b => b.Kind == BodyKind.Caboose).Tipped);
    }

    [Fact]
    public void AResumedNightHasItBackOnItsString()
    {
        var straight = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = Night(Fitted, straight);
        var again = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Fitted, 4, 1)), straight, 1_000), Tuning.Combat);
        again.EnableBodies();
        again.Restock(w.Aboard());
        Assert.Single(again.Bodies.All, b => b.Kind == BodyKind.Caboose);
    }
}
