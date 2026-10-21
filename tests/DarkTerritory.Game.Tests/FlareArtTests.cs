using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The flare pistol drawn (note 964; the Art Checklist's <c>flare-pistol</c>): its shot at the muzzle for a moment, a flare's
/// streak and smoke while it flies and none at rest, and the pistol held in a crewmate's fist as a tool is, not carried in
/// the arms like a crate.
/// </summary>
public class FlareArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());
    static readonly Effects Fx = new(Look);
    static readonly Vector3 O = new(0, 1, -6);

    [Fact]
    public void TheShotFlashesAndLightsForAMomentThenOnlyItsSmokeAndThenNothing()
    {
        var flash = new MeshBuilder();
        Fx.FlareShot(flash, O, -Vector3.UnitZ, 0.02, 1);
        Assert.Single(flash.PointLights);
        Assert.NotEmpty(flash.AdditiveFx);
        var after = new MeshBuilder();
        Fx.FlareShot(after, O, -Vector3.UnitZ, 0.5, 1);
        Assert.Empty(after.PointLights);
        var gone = new MeshBuilder();
        Fx.FlareShot(gone, O, -Vector3.UnitZ, Effects.FlareShotSeconds + 0.01, 1);
        Assert.Empty(gone.AdditiveFx);
        Assert.Empty(gone.AlphaFx);
    }

    [Fact]
    public void AFlareInFlightTrailsSparksAndSmokeAlongTheWayItCameAndNoneAtRest()
    {
        var flying = new MeshBuilder();
        Fx.FlareFlight(flying, O, new Vector3(0, -5, -20), 1, 3);
        Assert.True(flying.AdditiveFx.Count >= 20, $"{flying.AdditiveFx.Count} sparks and glare");
        Assert.NotEmpty(flying.AlphaFx);
        var resting = new MeshBuilder();
        Fx.FlareFlight(resting, O, Vector3.Zero, 1, 3);
        Assert.Empty(resting.AdditiveFx);
    }

    [Fact]
    public void ACrewmateHoldsTheFlarePistolInTheirFistNotInTheirArms()
    {
        var trains = DataFile.Load<TrainTuning>(Path.Combine(DataFile.FindContentRoot(), TrainTuning.File));
        var player = DataFile.Load<PlayerTuning>(Path.Combine(DataFile.FindContentRoot(), PlayerTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trains, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 2_000);
        var world = new World(train);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, player);
        var pistol = world.Bodies.SpawnItem(PlayerMotor.WorldPosition(s, train), train.Dynamics.Distance, BodyKind.FlarePistol);
        (pistol.Carrier, pistol.Charge) = (1, 3);
        var drawn = CrewActs.Crewmate(1, s, world, train.Frames);
        Assert.True(drawn.Pistol);
        Assert.NotEqual(CrewPose.Carry, drawn.Act);
        // A crate's still carried in the arms.
        pistol.Carrier = -1;
        world.Bodies.SpawnCrate(train, 2, new Double3(0, 4, 0)).Carrier = 1;
        Assert.False(CrewActs.Crewmate(1, s, world, train.Frames).Pistol);
    }
}
