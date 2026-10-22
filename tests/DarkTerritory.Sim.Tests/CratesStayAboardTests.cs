using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 838 (the director's 16 Oct playtest, line 21: "Where are the crates that I loaded up? In car one, they're no longer
/// here"): crates lying in car 1 with its side doors open stay where they lie through the night's running: up to speed, round
/// a tight bend each way and braked hard to a stand. (What the director lost was the Foundry's casting, drawn only near its
/// crane: <c>CastingsRideOnTests</c>.)
/// </summary>
public class CratesStayAboardTests
{
    [Fact]
    public void CratesLyingInACarWithItsDoorsOpenStayThroughBendsAndAHardStop()
    {
        var t = Tuning.Train;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(1500), new TrackSegment(800, 180), new TrackSegment(800, -180), new TrackSegment(4000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 0)), line, 1_400);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        train.Vehicles[1].DoorsOpen = 0b1111;
        double floor = t.Geometry.Interior!.FloorHeight;
        var crates = Enumerable.Range(0, 6)
            .Select(i => world.Bodies.SpawnCrate(train, 1, new Double3(i % 2 == 0 ? -0.6 : 0.6, floor, -3 + i * 1.2), BodyKind.Cargo)).ToList();
        var placed = crates.Select(c => c.Pbd.Particles[0].Position).ToList();
        const double Dt = SimConstants.TickSeconds;
        for (double s = 0; s < 150; s += Dt)
        {
            // Settled at a stand, then up to 16 m/s through the bends, and from two minutes in braked hard (2.5 m/s²) to a stand.
            double v = train.Dynamics.Velocity;
            train.Dynamics.Velocity = s < 3 ? 0 : s > 120 ? Math.Max(0, v - 2.5 * Dt) : Math.Min(16, v + Dt);
            train.Step(Dt, default);
            world.StepBodies([]);
        }
        for (int i = 0; i < crates.Count; i++)
        {
            Assert.Contains(crates[i], world.Bodies.All);
            Assert.Equal(1, crates[i].Parent);
            Assert.True((crates[i].Pbd.Particles[0].Position - placed[i]).Length < 0.5, $"crate {i} moved from {placed[i]} to {crates[i].Pbd.Particles[0].Position}");
        }
    }
}
