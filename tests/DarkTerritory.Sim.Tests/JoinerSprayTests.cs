using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 851 (the director's playtest of 16 Oct evening, line 5, the first two-player night: "The fire in the rear car didn't
/// go out ever despite both of us fighting it"): a joiner's extinguisher works on the host's fire. Their Fire intent crosses
/// the link with an extinguisher in their hands, and the host cools the cells they're aimed at and spends their charge.
/// </summary>
public class JoinerSprayTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning Quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(60_000)])), 2_000, Tuning.Boiler);

    [Fact]
    public void AJoinersSprayReachesTheHostsFire()
    {
        var net = new Ballast.Net.LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P, Tuning.Combat);
        var joiner = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Name = "Priya" };
        host.EnableEnemies(Quiet, null, 1, 2);
        void Run(double seconds, Func<PlayerIntent>? hands = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                joiner.Step(hands?.Invoke() ?? default);
            }
        }
        Run(2);
        var train = host.Train;
        var t = Tuning.Enemies.CarFire;
        int car = train.Dynamics.Consist.Vehicles[^1].Id;
        var room = train.Frames[car].Shape.Interior!.Value;
        var fire = (CarFire)host.World.AddEnemy(id => CarFire.In(id, train, car, 0, t, FireCause.Set).Ablaze(1.0));
        Run(SimConstants.TickSeconds);
        var grid = FireGrid.Of(train, car, t.CellSize)!;
        int cell = grid.FloorAt(new Double3(0, room.Min.Y, 0));
        var at = grid.Centre[cell];
        // The joiner in the car, two metres from the cell, looking at it, an extinguisher in their hands.
        var feet = new Double3(room.Centre.X, room.Min.Y, at.Z - 2);
        var d = at - (feet + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight);
        byte me = joiner.PlayerId!.Value;
        host.SetPlayerState(me, new PlayerState
        {
            Parent = car, Position = feet, Surface = Surface.Deck, Health = P.Health,
            Yaw = Math.Atan2(-d.X, -d.Z), Pitch = Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)),
        });
        var ext = host.World.Bodies.SpawnCrate(train, car, feet with { Y = room.Min.Y + 0.1 }, BodyKind.Extinguisher);
        ext.Carrier = me;
        ext.Claimed = true;
        Run(0.5);
        double was = fire.Heat[cell];
        Assert.True(was > 0.9, $"the cell's at {was:0.00}");
        Run(2, () => new PlayerIntent { Buttons = PlayerButtons.Fire });
        // The stretch they're aimed at is out, and their charge spent on it (the host's say).
        Assert.Equal(0, fire.Heat[cell]);
        Assert.Single(fire.Sprayed);
        Assert.True(ext.Charge < 1 - 1.5 / t.ChargeSeconds, $"charge {ext.Charge:0.00}");
        Assert.False(fire.Gone);
    }
}
