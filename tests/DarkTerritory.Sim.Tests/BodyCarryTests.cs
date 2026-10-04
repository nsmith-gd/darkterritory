using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. C.4, D.8, D.9 and Line Plan §12.6 (ARCHITECTURE §8 note 181): a body, a toy, a find or the child is carried
/// at 2.8 m/s with no climbing, but the last one standing can haul a body up a ladder slowly; a body or kit at rest outside
/// the walkable corridor is put back on the formation's edge; and a freed survivor stays that survivor into the next night.
/// </summary>
public class BodyCarryTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly Route.Route Route;
        public readonly List<(int Id, PlayerState State)> Crew = [];

        public Night()
        {
            Route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), Route.Build(), 3_000, Tuning.Boiler);
            World = new World(Train, Tuning.Combat);
            World.EnableBodies();
        }

        public Double3 Beside(double lateral, double along = 2_950)
        {
            var t = Train.Line.Sample(along);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var at = t.Position + right * lateral;
            double hint = along;
            return at with { Y = PlayerMotor.GroundAt(at, Train.Line, ref hint) };
        }

        public int Add(PlayerState s)
        {
            Crew.Add((Crew.Count, s));
            return Crew.Count - 1;
        }

        public Body Corpse(Double3 at, int owner = 9) =>
            World.Bodies.SpawnRagdoll(Train, owner, new PlayerState { Parent = PlayerState.World, Position = at + Double3.Up * 0.3, LineHint = 2_950 });

        public void Step(double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i].State;
                    World.CrewAct(ref s, default, i);
                    Crew[i] = (i, s);
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.StepBodies(Crew);
            }
        }
    }

    [Theory]
    [InlineData(BodyKind.Ragdoll)]
    [InlineData(BodyKind.Toy)]
    [InlineData(BodyKind.Loot)]
    [InlineData(BodyKind.Child)]
    public void HandLootIsCarriedSlowlyAndKeepsYouOffLadders(BodyKind kind)
    {
        var n = new Night();
        int a = n.Add(PlayerMotor.SpawnOnGround(n.Beside(3), n.Train.Line, 2_950, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P)); // someone else alive: not the solo remainer
        var b = kind == BodyKind.Ragdoll ? n.Corpse(n.Beside(3)) : n.World.Bodies.SpawnItem(n.Beside(3), 2_950, kind);
        b.Carrier = a;
        n.Step(0.1);
        Assert.True(n.Crew[a].State.Has(PlayerFlags.Heavy));
        Assert.False(n.Crew[a].State.Has(PlayerFlags.SoloCarry));
        // Running flat out: no faster than carryHeavy.
        var s = n.Crew[a].State;
        for (int i = 0; i < 30; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, n.Train, P, Tuning.Train, SimConstants.TickSeconds);
        Assert.True(new Double3(s.Velocity.X, 0, s.Velocity.Z).Length <= P.CarryHeavy + 1e-9);
    }

    [Fact]
    public void TheLastOneStandingMayClimbWithABodyAtAQuarterThePace()
    {
        var n = new Night();
        int a = n.Add(PlayerMotor.SpawnOnGround(n.Beside(3), n.Train.Line, 2_950, P));
        n.Add(PlayerMotor.SpawnInCab(n.Train, P) with { Health = 0, Death = DeathCause.Mauled });
        n.Corpse(n.Beside(3)).Carrier = a;
        n.Step(0.1);
        Assert.True(n.Crew[a].State.Has(PlayerFlags.Heavy));
        Assert.True(n.Crew[a].State.Has(PlayerFlags.SoloCarry));
        Assert.Equal(0.25, P.SoloBodyClimb / P.LadderClimb, 9);
        // A toy isn't a body: no exception for it.
        n.World.Bodies.CarriedBy(a)!.Carrier = -1;
        n.World.Bodies.SpawnItem(n.Beside(3), 2_950, BodyKind.Toy).Carrier = a;
        n.Step(0.1);
        Assert.False(n.Crew[a].State.Has(PlayerFlags.SoloCarry));
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(-90, 0)]
    [InlineData(10, -25)]
    public void ABodyOutsideTheWalkableCorridorIsPutBackOnTheFormationsEdge(double lateral, double drop)
    {
        var n = new Night();
        var body = n.Corpse(n.Beside(lateral) + Double3.Up * drop);
        // Below the rails it's a ravine floor or a river bed the ground function doesn't model: at rest there from the start.
        if (drop < 0)
            body.Pbd.Sleep();
        else
            n.Step(4);
        n.Step(0.1);
        // Measured from where on the line it's nearest (on a curve that isn't where it fell from).
        double hint = 2_950;
        var (path, along) = n.Train.Line.Nearest(body.Pbd.Centre, ref hint);
        var t = n.Train.Line.Sample(path, along);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        double now = Double3.Dot(body.Pbd.Centre - t.Position, right);
        var r = Tuning.Train.Recovery;
        Assert.InRange(Math.Abs(now), r.EdgeM - 1.5, r.EdgeM + 1.5);
        Assert.True(Math.Sign(lateral) == Math.Sign(now), $"now {now:0.0}, centre {body.Pbd.Centre}, rail {t.Position}, along {along:0}");
    }

    [Fact]
    public void ABodyInTheCorridorStaysWhereItFell()
    {
        var n = new Night();
        var body = n.Corpse(n.Beside(20));
        n.Step(4);
        var t = n.Train.Line.Sample(2_950);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        Assert.InRange(Double3.Dot(body.Pbd.Centre - t.Position, right), 17, 23);
    }

    [Fact]
    public void AFreedSurvivorIsCarriedIntoTheNextNight()
    {
        // D.8: freed tonight from a prison car or lockup, a prisoner from now on; the campaign keeps it by name.
        var report = new RunReport(RunEnd.Delivered, 100, 10, 3, 0, 1, 900, 10, 0, 0, 890, 3, 0)
        {
            Identities = new Dictionary<string, string> { ["Priya"] = Identity.Prisoner },
        };
        var campaign = new CampaignState { Cars = 4, Identities = new Dictionary<string, string> { ["Sam"] = Identity.Wildlander } };
        var next = Campaign.Campaign.Settle(campaign, report);
        Assert.Equal(Identity.Prisoner, next.Identities["Priya"]);
        Assert.Equal(Identity.Wildlander, next.Identities["Sam"]);
        // Next night: matched by name as it arrives, and every machine knows.
        var n = new Night();
        n.World.LooksByName = next.Identities;
        n.World.Names[2] = "Priya";
        n.World.Looks[2] = n.World.LooksByName["Priya"];
        Assert.Equal(Identity.Prisoner, Identity.Of(n.World, 2));
        Assert.Equal("", Identity.Of(n.World, 3));
        Assert.Equal(new Dictionary<string, string> { ["Priya"] = Identity.Prisoner }, Identity.ByName(n.World, [2, 3]));
    }
}
