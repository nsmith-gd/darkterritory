using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 594 (queue #326; the director, the inbox, 12 Oct: "Lethal Company's four shared slots"): a hand-sized thing goes
/// into a slot, tools and finds share the four, the number keys bring each out, every slot full takes nothing, aboard a find
/// in hand is put down, and a death drops them all. (Off the train, Use with a find in hand takes the next: the bots' village
/// errand, StopCrewTests.AHandOutInTheVillageFillsItsSlotsBeforeItWalksBack.)
/// </summary>
public class SlotTests
{
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));

    /// <summary>A standing train at a village halt, a crewmate in a car's aisle with the night's kit (crowbar, wrench), and a find to copy.</summary>
    static (World World, PlayerState Crewmate, Body Model) InACar()
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Local, seed);
            if (route.Of(FeatureKind.Village).FirstOrDefault(v => v.Stop!.Containers.Count(c => c.Zone == StopZone.Village) > 1) is not { } halt)
                continue;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), (halt.Start + halt.End) / 2, Tuning.Boiler);
            var world = new World(train, Tuning.Combat);
            world.EnableBodies();
            world.Stock();
            world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, L);
            world.Run!.Stock(world.Bodies, world.Run.Stops.ToList().IndexOf(halt), searched: true);
            var f = train.Frames.First(c => c.Index > 0 && c.Shape.Interior is not null);
            var room = f.Shape.Interior!.Value;
            var s = new PlayerState
            {
                Parent = f.Index,
                Position = new Double3(0, 1.1, room.Max.Z - 1.5),
                Yaw = 0,
                Surface = Surface.Deck,
                Health = Tuning.Player.Health,
                Kit = Kit.Of([Tool.Crowbar, Tool.Wrench]),
            };
            return (world, s, world.Bodies.All.First(b => b.Kind == BodyKind.Loot));
        }
        throw new InvalidOperationException("no local route has a village halt with finds");
    }

    /// <summary>A find lying on the floor in front of the crewmate, in reach.</summary>
    static Body AtTheirFeet(World world, Body model, in PlayerState s)
    {
        var f = world.Train.Frames[s.Parent];
        return world.Bodies.SpawnLoot(f.ToWorld(s.Position + new Double3(0, 0.3, -0.6)), world.Train.Dynamics.Distance, model.Owner, model.Pbd.Particles[0].Radius);
    }

    static void Tick(World world, ref PlayerState s, PlayerIntent intent)
    {
        world.Train.Dynamics.Velocity = 0;
        world.BeginTick();
        Kit.Select(ref s, intent);
        world.CrewAct(ref s, intent, 1);
        world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        world.StepBodies([(1, s)]);
        world.StepRun([]);
    }

    static void Press(World world, ref PlayerState s, PlayerIntent intent)
    {
        Tick(world, ref s, intent);
        Tick(world, ref s, default);
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void FindsGoIntoTheEmptySlotsAndTheNumberKeysBringEachOut()
    {
        var (world, s, model) = InACar();
        var a = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        // The crowbar and the wrench are slots 1 and 2: the find's in 3, in hand.
        Assert.Equal(2, a.Pocket);
        Assert.Equal(2, s.HeldSlot);
        Assert.Same(a, world.Bodies.CarriedBy(1));
        // Aboard, a find in hand is put down by Use: the crowbar in hand first (key 1), and the next taken.
        Press(world, ref s, new PlayerIntent { Select = 1 });
        Assert.True(a.Stashed);
        var b = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        // The second into 4, and in hand; the first put away.
        Assert.Equal(3, b.Pocket);
        Assert.True(a.Stashed);
        Assert.Same(b, world.Bodies.CarriedBy(1));
        Assert.Equal(0b1100, s.Pockets);
        // Key 3: the first out again, the second away.
        Press(world, ref s, new PlayerIntent { Select = 3 });
        Assert.Same(a, world.Bodies.CarriedBy(1));
        Assert.True(b.Stashed);
        // Key 1: the crowbar in hand, both finds on them.
        Press(world, ref s, new PlayerIntent { Select = 1 });
        Assert.Null(world.Bodies.CarriedBy(1));
        Assert.Equal(Tool.Crowbar, Kit.Held(s));
        Assert.Equal(2, world.Bodies.Slotted(1).Count());
    }

    [Fact]
    public void EverySlotFullAFindIsntTakenAndAboardOneInHandIsPutDown()
    {
        var (world, s, model) = InACar();
        var a = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        Press(world, ref s, new PlayerIntent { Select = 1 });
        var b = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        // A tool in hand, all four full: a third isn't taken.
        Press(world, ref s, new PlayerIntent { Select = 1 });
        var c = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        Assert.Equal(-1, c.Carrier);
        Assert.Null(world.Bodies.CarriedBy(1));
        // The second find in hand: Use puts it down here in the car, and its slot's empty again.
        Press(world, ref s, new PlayerIntent { Select = 4 });
        Assert.Same(b, world.Bodies.CarriedBy(1));
        Press(world, ref s, Use);
        Assert.True(b.Carrier < 0 || !world.Bodies.All.Contains(b), "the find in hand wasn't put down");
        Assert.Equal(-1, c.Carrier);
        Assert.Equal(0b0100, s.Pockets);
        Assert.Equal(1, a.Carrier);
    }

    [Fact]
    public void ADeathDropsEverythingInTheSlots()
    {
        var (world, s, model) = InACar();
        var a = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        Press(world, ref s, new PlayerIntent { Select = 1 });
        var b = AtTheirFeet(world, model, s);
        Press(world, ref s, Use);
        s.Death = DeathCause.Mauled;
        s.Health = 0;
        Tick(world, ref s, default);
        Assert.Empty(world.Bodies.Slotted(1));
        Assert.Equal(0, s.Pockets);
        foreach (var find in new[] { a, b }.Where(world.Bodies.All.Contains))
        {
            Assert.Equal(-1, find.Carrier);
            Assert.False(find.Stashed);
        }
    }

    [Fact]
    public void AToolTakenUpGoesIntoASlotWithoutAFind()
    {
        // The shovel off the cab's rack (note 275) with a find in slot 3: into slot 4, not over the find.
        var (world, s, _) = InACar();
        s.Pockets = 0b0100;
        Assert.True(CrewActions.TakeShovel(ref s, world.Train));
        Assert.Equal(Tool.Shovel, Kit.At(s.Kit, 3));
        Assert.Equal(Tool.None, Kit.At(s.Kit, 2));
        Assert.Equal(3, s.HeldSlot);
    }
}
