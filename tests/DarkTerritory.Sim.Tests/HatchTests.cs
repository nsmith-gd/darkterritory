using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T99 (playtest: "a way to open the roofs of cars up so we can use the crane to lower crates in. Crates cannot be lowered
/// to a point where they block the roof from closing"): a cargo car's roof hatch, opened from the roof, that the crane
/// lowers a casting in through; one low over its edge is eased in (note 581), one it can't set down under the roof line stays on
/// the hook, and the lid won't shut on one.
/// </summary>
public class HatchTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly TrainTuning T = Tuning.Train;
    static readonly CraneTuning C = FacilityTests.F.Crane;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };

    static PlayerState AtTheHandle(TrainOnLine train, int car)
    {
        var handle = train.Frames[car].Shape.Interactables.Single(i => i.Kind == InteractableKind.Hatch);
        return PlayerMotor.SpawnOnRoof(train, car, handle.Position.Z, P, handle.Position.X);
    }

    [Fact]
    public void OnlyACargoCarHasOne()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 0)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 2_000);
        foreach (var v in train.Vehicles)
            Assert.Equal(v.Kind == VehicleKind.Cargo, train.Frames[v.Id].Shape.Hatch is not null);
    }

    [Fact]
    public void HeldOpenFromTheRoofItsAHoleIntoTheCar()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 0)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 2_000);
        var world = new World(train);
        int car = 1;
        var hatch = train.Frames[car].Shape.Hatch!.Value;
        // Shut, the lid's roof you walk on.
        var over = PlayerMotor.SpawnOnRoof(train, car, hatch.Centre.Z, P, -0.6);
        Step(world, ref over, default, 1);
        Assert.Equal(Surface.Roof, over.Surface);
        Assert.Equal(hatch.Max.Y, over.Position.Y, 3);

        var hand = AtTheHandle(train, car);
        Step(world, ref hand, Hold, T.Geometry.Interior!.DoorSeconds + 0.1);
        Assert.True(train.Vehicles[car].DoorOpen(CarShape.HatchBit));
        // Open, whoever was standing on it drops in onto the floor (under the lethal fall), and it's no longer sealed.
        Step(world, ref over, default, 2);
        Assert.Equal(car, over.Parent);
        Assert.Equal(Surface.Deck, over.Surface);
        Assert.Equal(T.Geometry.Interior.FloorHeight, over.Position.Y, 2);
        Assert.True(over.Alive);
        // Held again, it shuts.
        Step(world, ref hand, default, 0.1);
        Step(world, ref hand, Hold, T.Geometry.Interior.DoorSeconds + 0.1);
        Assert.False(train.Vehicles[car].DoorOpen(CarShape.HatchBit));
    }

    static void Step(World world, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, world.Train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
    }

    /// <summary>The crane stop, a casting on the hook, hung high over a cargo car's hatch (at a point in its frame).</summary>
    static (FacilityTests.Stop Stop, Crane Crane, int Car) OverTheHatch(Func<Box, Double3>? at = null)
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        var crane = stop.Site.Crane!;
        crane.Castings[0].State = CastingState.Hooked;
        foreach (var v in stop.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
        {
            var frame = stop.Train.Frames[v.Id];
            var hatch = frame.Shape.Hatch!.Value;
            var (bridge, trolley, off) = crane.Over(frame.ToWorld((at ?? (h => h.Centre))(hatch) with { Y = 0 }));
            if (off > 0.05)
                continue;
            (crane.Bridge, crane.Trolley, crane.Hook) = (bridge, trolley, C.Height);
            return (stop, crane, v.Id);
        }
        throw new InvalidOperationException("the gantry reaches no hatch");
    }

    [Fact]
    public void ShutTheCastingGoesOnTheRoofOpenItGoesDownInside()
    {
        var (stop, crane, car) = OverTheHatch();
        var frame = stop.Train.Frames[car];
        var hatch = frame.Shape.Hatch!.Value;
        Assert.Equal(frame.ToWorld(frame.ToLocal(crane.HookAt) with { Y = frame.Shape.RoofHeight }).Y, crane.Under(stop.Train).Y, 3);

        stop.Train.Vehicles[car].ToggleDoor(CarShape.HatchBit);
        var (under, floor) = crane.Under(stop.Train);
        Assert.Equal(car, under);
        Assert.InRange(floor - frame.ToWorld(Double3.Zero).Y, T.Geometry.Interior!.FloorHeight - 0.01, hatch.Min.Y - 2 * crane.CastingHalf);
        // Lowered all the way (the hook stops over what's inside), and let go: in and loaded, under the roof line.
        crane.Hook = crane.Lowest(stop.Train);
        double load = stop.Train.Vehicles[car].Load;
        Assert.NotNull(crane.Release(stop.Train));
        var c = crane.Castings[0];
        Assert.Equal(CastingState.Loaded, c.State);
        Assert.Equal(car, c.Car);
        Assert.Equal(load + C.LoadPerCasting, stop.Train.Vehicles[car].Load, 6);
        Assert.True(c.At.Y + 2 * crane.CastingHalf <= hatch.Min.Y, $"its top at {c.At.Y + 2 * crane.CastingHalf:0.00} is under the roof at {hatch.Min.Y:0.00}");
    }

    [Fact]
    public void HungOverTheHatchsEdgeItsEasedInAndLetGoThereItGoesIn()
    {
        // Its middle over the opening's back edge: half of it over the roof. T99 kept it on the hook there, let go or not,
        // with nothing said; the director, 9 Oct 2026 (note 581): "There's no way it seems to actually unhook loot when its in
        // the car finally. Also it's extremely difficult to get loot into the cars, the game should be more forgiving".
        var (stop, crane, car) = OverTheHatch(h => h.Centre with { Z = h.Max.Z });
        var frame = stop.Train.Frames[car];
        stop.Train.Vehicles[car].ToggleDoor(CarShape.HatchBit);
        // Up high, nothing guides it: the hook stops over the roof.
        Assert.Null(crane.Guided(stop.Train));
        Assert.Equal(frame.ToWorld(frame.ToLocal(crane.HookAt) with { Y = frame.Shape.RoofHeight }).Y, crane.Under(stop.Train).Y, 3);
        // Brought down onto the roof by the opening, it's eased over it as the operator holds the stick still ...
        crane.Hook = crane.Lowest(stop.Train);
        Assert.NotNull(crane.Guided(stop.Train));
        double before = stop.Train.Vehicles[car].Load;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            crane.Drive(default, stop.Train, SimConstants.TickSeconds);
        Assert.True(crane.Guided(stop.Train) is null, $"hook local {frame.ToLocal(crane.HookAt)}, fit {(crane.Guided(stop.Train) is { } g ? frame.ToLocal(g) : default)}, hatch {frame.Shape.Hatch}, bridge {crane.Bridge} trolley {crane.Trolley}");
        Assert.True(crane.Hatch(stop.Train, car, frame.ToLocal(crane.HookAt)) is { Fits: true }, "eased, and still not clear of the opening");

        // ... and let go at the edge without waiting, it's put through and set down inside, not kept on the hook.
        var (again, crane2, car2) = OverTheHatch(h => h.Centre with { Z = h.Max.Z });
        again.Train.Vehicles[car2].ToggleDoor(CarShape.HatchBit);
        crane2.Hook = crane2.Lowest(again.Train);
        var landed = crane2.Release(again.Train);
        Assert.NotNull(landed);
        Assert.False(landed!.Value.Fell, "dropped through it");
        Assert.Equal(CastingState.Loaded, crane2.Castings[0].State);
        Assert.Equal(car2, crane2.Castings[0].Car);
        Assert.True(crane2.Castings[0].At.Y < again.Train.Frames[car2].Shape.Hatch!.Value.Min.Y, "set down standing in the opening");
        Assert.Equal(before + C.LoadPerCasting, again.Train.Vehicles[car2].Load, 6);
    }

    [Fact]
    public void TheLidWontShutOnACastingHangingInIt()
    {
        var (stop, crane, car) = OverTheHatch();
        stop.Train.Vehicles[car].ToggleDoor(CarShape.HatchBit);
        stop.Step(0.1, []);
        // Down into the opening, half in, half out.
        crane.Hook = crane.Lowest(stop.Train) + 1.2;
        Assert.True(crane.InHatch(stop.Train, car));
        stop.Crew.Add(AtTheHandle(stop.Train, car));
        stop.Step(T.Geometry.Interior!.DoorSeconds + 0.2, [Hold]);
        Assert.True(stop.Train.Vehicles[car].DoorOpen(CarShape.HatchBit));
        // Hoisted clear, it shuts.
        crane.Hook = C.Height;
        stop.Step(0.1, [default]);
        stop.Step(T.Geometry.Interior.DoorSeconds + 0.2, [Hold]);
        Assert.False(stop.Train.Vehicles[car].DoorOpen(CarShape.HatchBit));
    }
}
