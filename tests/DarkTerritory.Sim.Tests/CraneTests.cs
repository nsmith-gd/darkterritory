using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The gantry crane (T48, spec D.2): an operator at the controls drives it, the ground crew rigs, a casting set down on a
/// car is loaded, and one let go of high kills whoever's under it.
/// </summary>
public class CraneTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly CraneTuning C = F.Crane;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };

    static (FacilityTests.Stop Stop, Crane Crane) AtTheCrane()
    {
        var stop = new FacilityTests.Stop(ModuleKind.Crane);
        var crane = Assert.IsType<Crane>(stop.Site.Crane);
        stop.Crew.Add(stop.OnTheGround(crane.Controls));
        return (stop, crane);
    }

    static PlayerIntent Drive(float x = 0, float z = 0, PlayerButtons also = PlayerButtons.None) =>
        new() { MoveX = x, MoveZ = z, Buttons = PlayerButtons.Use | also };

    [Fact]
    public void TheOperatorDrivesItFromTheControlsAndStaysPut()
    {
        var (stop, crane) = AtTheCrane();
        var stand = stop.Crew[0].Position;
        double bridge = crane.Bridge, trolley = crane.Trolley, hook = crane.Hook;
        stop.Step(2, [Drive(z: 1)]);
        Assert.Equal(bridge + 2 * C.BridgeSpeed, crane.Bridge, 1);
        Assert.True(stop.Crew[0].Has(PlayerFlags.Operating));
        // The stick drives the crane, not the operator's feet.
        Assert.True((stop.Crew[0].Position - stand).Length < 0.01);
        stop.Step(1, [Drive(x: -1)]);
        Assert.Equal(trolley - C.TrolleySpeed, crane.Trolley, 1);
        stop.Step(1, [Drive(also: PlayerButtons.Brake)]);
        Assert.Equal(hook - C.HoistSpeed, crane.Hook, 1);
        // Let go of the controls and it stops where it is; walking away is walking again.
        bridge = crane.Bridge;
        stop.Step(1, [new PlayerIntent { MoveZ = 1 }]);
        Assert.Equal(bridge, crane.Bridge, 6);
        Assert.False(stop.Crew[0].Has(PlayerFlags.Operating));
        Assert.True((stop.Crew[0].Position - stand).Length > 1);
    }

    [Fact]
    public void RiggedOnTheGroundAndSetDownOnACarsRoofItsLoaded()
    {
        var (stop, crane) = AtTheCrane();
        var casting = crane.Castings[0];
        // The hook brought down over the first casting (as the operator would), and someone beside it holds on to rig it.
        var groundAt = casting.At;
        crane.Bridge = C.Along + (0 - (C.Castings - 1) * 0.5) * C.Spacing;
        crane.Trolley = C.StackLateral;
        crane.Hook = 1.8;
        stop.Crew.Add(stop.OnTheGround(groundAt + new Double3(0.8, 0, 0)));
        stop.Step(C.RigSeconds * 0.5, [default, Hold]);
        Assert.Equal(CastingState.Stacked, casting.State);
        stop.Step(C.RigSeconds * 0.5 + 0.2, [default, Hold]);
        Assert.Equal(CastingState.Hooked, casting.State);

        // Up, over the car under the gantry, and down onto its roof: then let go.
        stop.Step(C.Height / C.HoistSpeed, [Drive(also: PlayerButtons.Jump), default]);
        var car = stop.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo)
            .OrderBy(v => Math.Abs(stop.Train.Frames[v.Id].ToLocal(crane.HookAt).Z)).First();
        double before = car.Load;
        var over = stop.Train.Frames[car.Id].ToWorld(new Double3(0, 0, 0));
        // Where the bridge and trolley put the hook over the car's middle (the operator's eye, done by search), then the stick.
        var (bridgeNow, trolleyNow) = (crane.Bridge, crane.Trolley);
        (double Bridge, double Trolley, double Off) best = (0, 0, double.MaxValue);
        for (double b = C.Along - C.Length / 2; b <= C.Along + C.Length / 2; b += 0.1)
            for (double x = C.Span[0] + 0.5; x <= C.Span[1] - 0.5; x += 0.1)
            {
                (crane.Bridge, crane.Trolley) = (b, x);
                double off = ((crane.HookAt - over) with { Y = 0 }).Length;
                if (off < best.Off)
                    best = (b, x, off);
            }
        (crane.Bridge, crane.Trolley) = (bridgeNow, trolleyNow);
        Assert.True(best.Off < 0.5, $"the gantry doesn't reach car {car.Id} ({best.Off:0.0} m)");
        for (int i = 0; i < 40 * SimConstants.TickRate && (Math.Abs(crane.Bridge - best.Bridge) > 0.05 || Math.Abs(crane.Trolley - best.Trolley) > 0.05); i++)
            stop.Step(SimConstants.TickSeconds, [Drive((float)Math.Clamp((best.Trolley - crane.Trolley) * 5, -1, 1), (float)Math.Clamp((best.Bridge - crane.Bridge) * 5, -1, 1)), default]);
        Assert.Equal(car.Id, crane.Under(stop.Train).Car);
        stop.Step(C.Height / C.HoistSpeed, [Drive(also: PlayerButtons.Brake), default]);
        stop.Step(SimConstants.TickSeconds, [Drive(also: PlayerButtons.Fire), default]);
        Assert.Equal(CastingState.Loaded, casting.State);
        Assert.Equal(before + C.LoadPerCasting, car.Load, 6);
        Assert.Equal(car.Id, casting.Car);
        Assert.Equal(C.Castings - 1, crane.Left);
    }

    [Fact]
    public void LetGoOfHighItFallsAndKillsWhoeverIsUnder()
    {
        var (stop, crane) = AtTheCrane();
        var casting = crane.Castings[0];
        casting.State = CastingState.Hooked;
        crane.Bridge = C.Along;
        crane.Trolley = C.StackLateral;
        crane.Hook = C.Height;
        var under = crane.HookAt with { Y = crane.HookAt.Y - C.Height };
        stop.Crew.Add(stop.OnTheGround(under));
        stop.Crew.Add(stop.OnTheGround(under + new Double3(C.CrushRadius + 2, 0, 0)));
        stop.Step(SimConstants.TickSeconds, [Drive(), default, default]);
        stop.Step(SimConstants.TickSeconds, [Drive(also: PlayerButtons.Fire), default, default]);
        Assert.Equal(CastingState.Lost, casting.State);
        // It comes down on the one under it the next moment; the one a couple of metres off is spared.
        stop.Step(SimConstants.TickSeconds, [Drive(), default, default]);
        var crushed = Assert.Single(stop.World.Damage);
        Assert.Equal((2, DeathCause.Crushed), (crushed.PlayerId, crushed.Cause));
    }

    [Fact]
    public void NobodyAtTheControlsNothingMoves()
    {
        var (stop, crane) = AtTheCrane();
        // Standing at the stand without a hand on the controls.
        double bridge = crane.Bridge;
        stop.Step(1, [new PlayerIntent { Buttons = PlayerButtons.Jump }]);
        Assert.Equal(bridge, crane.Bridge);
        Assert.False(stop.Crew[0].Has(PlayerFlags.Operating));
        // And a hook up in the air can't be rigged from the ground.
        crane.Hook = C.Height;
        crane.Trolley = C.StackLateral;
        crane.Bridge = C.Along + (0 - (C.Castings - 1) * 0.5) * C.Spacing;
        stop.Crew.Add(stop.OnTheGround(crane.Castings[0].At + new Double3(0.8, 0, 0)));
        stop.Step(C.RigSeconds + 1, [default, Hold]);
        Assert.Equal(CastingState.Stacked, crane.Castings[0].State);
    }

    [Fact]
    public void AClientSeesTheCrane()
    {
        var (stop, crane) = AtTheCrane();
        stop.Step(1, [Drive(z: 1)]);
        crane.Castings[1].State = CastingState.Hooked;
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var seen = client.Run!.Sites[stop.Site.Index]!.Crane!;
        Assert.Equal(crane.Bridge, seen.Bridge, 3);
        Assert.Equal(crane.Trolley, seen.Trolley, 3);
        Assert.Equal(crane.Hook, seen.Hook, 3);
        Assert.Equal(CastingState.Hooked, seen.Castings[1].State);
        Assert.Equal(crane.Castings[0].At.X, seen.Castings[0].At.X, 3);
    }
}
