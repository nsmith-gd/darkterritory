using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 §23.2, Stranded, unable to repair: a ruptured boiler with the repair kit (§12's engineering kit, the build's
/// carried item: ARCHITECTURE note 150) lost to the Territory ends the night once the train is at rest. A kit that's
/// merely far away is never lost (E.11 "no reachable kit ever does"). And D.2: a body keeps the tools its player carried.
/// </summary>
public class StrandedTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly List<(int Id, PlayerState State)> Crew = [];
        public PlayerIntent[] Intents = new PlayerIntent[4];

        /// <param name="spares">Spare repair kits from the fortress (E.12 question 4), in the lockers beside the first.</param>
        public Night(bool stocked = true, int spares = 0)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            var tuning = Tuning.Train with { Kit = Tuning.Train.Kit with { SpareKits = spares } };
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 0)), route.Build(), 3_000, Tuning.Boiler);
            World = new World(Train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, route, 600, authority: true);
            World.Run!.Resume(900, -1, Train.Boiler.Tender, 0);
            World.EnableHoldouts(Tuning.Holdouts, route);
            if (stocked)
                World.Stock();
        }

        public Run.Run Run => World.Run!;
        public Body Kit => World.Bodies.All.Single(b => b.Kind == BodyKind.RepairKit);
        public List<Body> Kits => [.. World.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit).OrderBy(b => b.Id)];

        public int AddInCab()
        {
            Crew.Add((Crew.Count, PlayerMotor.SpawnInCab(Train, P)));
            return Crew.Count - 1;
        }

        public PlayerState this[int id]
        {
            get => Crew[id].State;
            set => Crew[id] = (id, value);
        }

        public void Kill(int id) => this[id] = this[id] with { Death = DeathCause.Mauled, Health = 0 };

        public Body BodyOf(int id) => World.Bodies.All.Single(b => b.Kind == BodyKind.Ragdoll && b.Owner == id);

        public void Step(double seconds, double speed = 0)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                Train.Dynamics.Velocity = speed;
                World.BeginTick();
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i].State;
                    World.CrewAct(ref s, Intents[i], i);
                    Crew[i] = (i, s);
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.StepBodies(Crew);
                World.StepRun([.. Crew.Select(c => c.State)]);
            }
        }
    }

    [Fact]
    public void AKitLyingInItsCarIsNeitherCarriedNorLost()
    {
        var n = new Night();
        n.AddInCab();
        n.Train.Boiler.Ruptured = true;
        n.Step(2);
        Assert.Equal(KitPlace.Lying, n.Run.Kit.Place);
        Assert.Equal(World.RepairKitCar(n.Train), n.Run.Kit.Vehicle);
        Assert.False(n.Run.Over);
    }

    [Fact]
    public void ATrainNeverStockedWithAKitCantBeStranded()
    {
        var n = new Night(stocked: false);
        n.AddInCab();
        n.Train.Boiler.Ruptured = true;
        n.Step(3);
        Assert.Equal(KitPlace.None, n.Run.Kit.Place);
        Assert.False(n.Run.Over);
    }

    [Fact]
    public void AKitInACarTheTerritoryTookStrandsARupturedNightOnceItStops()
    {
        var n = new Night();
        n.AddInCab();
        n.AddInCab();
        int car = World.RepairKitCar(n.Train)!.Value;
        n.Train.Uncouple(n.Train.VehicleAhead(car));
        n.Train.Vehicles[car].Taken = true; // the Car Hugger's FINISH, or the Passenger's caboose
        n.Train.Boiler.Ruptured = true;
        n.Step(2, speed: 3);
        Assert.Equal(KitLoss.CarTaken, n.Run.Kit.Loss);
        Assert.False(n.Run.Over); // still rolling: the crew gets the whole coast

        n.Step(0.5);
        Assert.True(n.Run.Over);
        var r = n.Run.Report!;
        Assert.Equal(RunEnd.Stranded, r.End);
        Assert.Equal(KitLoss.CarTaken, r.KitLoss);
        // §23.2's settlement: no cargo, the tow billed, the living home and not charged.
        Assert.Equal(0, r.Gross);
        Assert.Equal(Math.Round(Tuning.Run.Stranded.RecoveryFee * 700), r.Recovery);
        Assert.Equal(2, r.CrewHome);
        Assert.Equal(0, r.CrewLossFees);
    }

    [Fact]
    public void LosingTheKitWithoutARuptureEndsNothingUntilOne()
    {
        var n = new Night();
        n.AddInCab();
        n.World.Bodies.Remove(n.Kit); // carried off
        n.Step(3);
        Assert.Equal(KitLoss.Taken, n.Run.Kit.Loss);
        Assert.False(n.Run.Over);
        n.Train.Boiler.Ruptured = true;
        n.Step(0.5);
        Assert.Equal(RunEnd.Stranded, n.Run.End);
    }

    [Fact]
    public void ACutCarIsLostOnlyPastTheReleaseDistance()
    {
        var n = new Night();
        n.AddInCab();
        n.Train.Uncouple(0); // the kit's car and everything behind it, cut from the engine
        n.Train.Boiler.Ruptured = true;
        double cut = n.Train.Rakes.First(r => r != n.Train.Dynamics).Distance;
        // The engine (and the living, in its cab) pull away: still within reach, it's somebody's walk.
        n.Train.Dynamics.Distance = cut + Tuning.Run.Stranded.KitLostBeyond - 100;
        n.Step(1.5);
        Assert.Equal(KitPlace.Lying, n.Run.Kit.Place);
        Assert.False(n.Run.Over);
        n.Train.Dynamics.Distance = cut + Tuning.Run.Stranded.KitLostBeyond + 250;
        n.Step(1.5);
        Assert.Equal(KitLoss.LeftBehind, n.Run.Kit.Loss);
        Assert.Equal(RunEnd.Stranded, n.Run.End);
    }

    [Fact]
    public void AKitLyingOnTheLineIsNeverLostHoweverFarBack()
    {
        var n = new Night();
        n.AddInCab();
        var kit = n.Kit;
        var back = n.Train.Line.Sample(n.Train.Dynamics.Distance - 2_500).Position + Double3.Up * 0.2;
        kit.Locker = -1; // out of its locker (note 173), and dropped far back down the line
        kit.Parent = PlayerState.World;
        kit.Pbd.Particles[0].Position = back;
        kit.Pbd.Particles[0].Previous = back;
        n.Train.Boiler.Ruptured = true;
        n.Step(3);
        Assert.Equal(KitPlace.Lying, n.Run.Kit.Place);
        Assert.False(n.Run.Over);
    }

    [Fact]
    public void AKitInItsLockerInACoupledCarIsReachable()
    {
        var n = new Night();
        n.AddInCab();
        n.Train.Boiler.Ruptured = true;
        n.Step(2, speed: 3);
        n.Step(1);
        Assert.True(n.Kit.Stowed);
        Assert.Equal(KitPlace.Lying, n.Run.Kit.Place);
        Assert.False(n.Run.Over);
    }

    [Fact]
    public void WithASpareItTakesLosingEveryKitToStrandANight()
    {
        // GDD v1.4 §23.2 with E.12 question 4 answered: the fortress's spare rides in the lockers beside the first.
        var n = new Night(spares: 1);
        n.AddInCab();
        var kits = n.Kits;
        Assert.Equal(2, kits.Count);
        Assert.All(kits, k => Assert.True(k.Stowed && k.Claimed));
        // One carried off by the Territory: the other's still in its locker, and that's a mend.
        n.World.Bodies.Remove(kits[0]);
        n.Train.Boiler.Ruptured = true;
        n.Step(2);
        Assert.Equal(KitPlace.Lying, n.Run.Kit.Place);
        Assert.Equal(kits[1].Id, n.Run.Kit.Body);
        Assert.False(n.Run.Over);
        // Then the car with the lockers is finished (the Car Hugger's FINISH): every kit's gone, and the night with them.
        int car = World.RepairKitCar(n.Train)!.Value;
        n.Train.Uncouple(n.Train.VehicleAhead(car));
        n.Train.Vehicles[car].Taken = true;
        n.Step(2);
        Assert.True(n.Run.Over);
        Assert.Equal(RunEnd.Stranded, n.Run.End);
        Assert.Equal(KitLoss.CarTaken, n.Run.Report!.KitLoss);
        Assert.Equal(0, n.Run.Report.SpareKitsHome);
    }

    [Fact]
    public void ASpareInHandSavesTheNightWhenTheLockersCarIsTaken()
    {
        var n = new Night(spares: 1);
        int me = n.AddInCab();
        var spare = n.Kits[1];
        spare.Locker = -1;
        spare.Carrier = me;
        int car = World.RepairKitCar(n.Train)!.Value;
        n.Train.Uncouple(n.Train.VehicleAhead(car));
        n.Train.Vehicles[car].Taken = true;
        n.Train.Boiler.Ruptured = true;
        n.Step(2);
        Assert.Equal(KitPlace.Carried, n.Run.Kit.Place);
        Assert.False(n.Run.Over);
    }

    [Fact]
    public void TheBodyKeepsTheToolsAndLiftingItTakesThem()
    {
        var n = new Night();
        int dead = n.AddInCab();
        int finder = n.AddInCab();
        n[dead] = n[dead] with { Kit = Player.Kit.Of([Tool.Crowbar, Tool.Shovel]) };
        n.Kill(dead);
        n.Step(0.1);
        var body = n.BodyOf(dead);
        Assert.True(body.HasTool(Tool.Shovel));
        // Lifting the body takes the tools off it (the spare crowbar stays on it: the finder has one).
        n[finder] = n[finder] with { Parent = body.Parent, Position = body.Centre + new Double3(0.6, 0, 0) };
        n.Intents[finder] = new PlayerIntent { Buttons = PlayerButtons.Use };
        n.Step(1.0 / SimConstants.TickRate);
        Assert.True(body.HeldBy(finder));
        Assert.True(Player.Kit.Has(n[finder].Kit, Tool.Shovel));
        Assert.False(body.HasTool(Tool.Shovel));
        Assert.True(body.HasTool(Tool.Crowbar));
    }
}
