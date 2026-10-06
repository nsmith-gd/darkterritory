using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Build 1121's playtest (ARCHITECTURE §8 note 263), on the real hosted night the app runs: the fortress yard is a safe space
/// until the run begins (run.json yardIsSafe, the director's decision of 6 Oct 2026), a train nobody's driving never moves
/// off by itself, and a crewmate left behind by the train is still told where the repair kit is.
/// </summary>
public class SafeYardTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    [Fact]
    public void HalfAnHourAfkInTheYardAndNothingHappens()
    {
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate * 2; t++)
        {
            host.Step(default);
            joiner.Step(default);
        }
        var h = host.Host!;
        var world = h.World;
        var train = h.Train;
        Assert.Equal(2, h.Players.Count());
        double distance = train.Dynamics.Distance, pressure = train.Boiler.Pressure, firebox = train.Boiler.Firebox, tender = train.Boiler.Tender;
        var enemies = world.ActiveEnemies.Select(e => e.Id).ToHashSet();
        var before = h.Players.ToDictionary(p => p.Id, p => p.State);
        int maxEnemies = enemies.Count;
        bool redline = false;
        // The director's half hour: the host in the cab, the joiner out on a roof in the cold, neither touching anything.
        for (int t = 0; t < SimConstants.TickRate * 60 * 30; t++)
        {
            host.Step(default);
            joiner.Step(default);
            redline |= train.Boiler.AtMaxSeconds > 0 || train.Boiler.SafetyValveJammed;
            maxEnemies = Math.Max(maxEnemies, world.ActiveEnemies.Count(e => !e.Gone));
        }
        Assert.Equal(RunPhase.Yard, world.Run!.Phase);
        Assert.Equal(distance, train.Dynamics.Distance, 6);
        Assert.Equal(0, train.Dynamics.Speed, 6);
        Assert.Equal(pressure, train.Boiler.Pressure, 6);
        Assert.Equal(firebox, train.Boiler.Firebox, 6);
        Assert.Equal(tender, train.Boiler.Tender, 6);
        Assert.False(train.Boiler.Ruptured);
        Assert.False(redline);
        Assert.Equal(enemies.Count, maxEnemies);
        Assert.All(world.ActiveEnemies, e => Assert.Contains(e.Id, enemies));
        Assert.Equal(0, world.Choir.Build, 6);
        foreach (var p in h.Players)
        {
            Assert.True(p.State.Alive);
            Assert.Equal(before[p.Id].Health, p.State.Health);
            Assert.Equal(0, p.State.Cold, 6);
        }
        Assert.DoesNotContain(world.Attribution.Log, i => i.Kind is IncidentKind.Death or IncidentKind.Grab or IncidentKind.Rupture or IncidentKind.Runaway);
    }

    [Fact]
    public void ALoneHostAtTheGunAndTheTrainNeverLeavesWithoutThem()
    {
        // Build 1121: hosted alone, no bots; up to the roof gun, sat in it, sat there. The train took off by itself (a Stoker,
        // come down the stack for the fire left to burn low in the yard, let it off the brake) and ran away to a rupture.
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0);
        var h = host.Host!;
        var train = h.Train;
        int gunCar = Enumerable.Range(0, train.Vehicles.Count).First(i => train.Vehicles[i].HasGun);
        var gun = Guns.Mount(train, gunCar)!.Value;
        double distance = train.Dynamics.Distance;
        for (int t = 0; t < SimConstants.TickRate * 600; t++)
        {
            if (t == SimConstants.TickRate * 3)
                h.SetPlayerState(h.Players.First().Id, PlayerMotor.SpawnOnRoof(train, gunCar, gun.Position.Z + 0.6, P));
            host.Step(t > SimConstants.TickRate * 4 && t < SimConstants.TickRate * 5 ? new PlayerIntent { Actions = PlayerActions.Seat } : default);
        }
        Assert.True(h.Players.First().State.Has(PlayerFlags.Seated));
        Assert.Equal(distance, train.Dynamics.Distance, 6);
        Assert.Equal(RunPhase.Yard, h.World.Run!.Phase);
        Assert.False(train.Boiler.Ruptured);
        Assert.DoesNotContain(h.World.ActiveEnemies, e => e.Kind == Sim.Enemies.EnemyKind.Stoker);
    }

    [Fact]
    public void LeftBehindTheTrainYouAreStillToldWhereTheKitIs()
    {
        // Build 1121: the kit aboard, the director off the train and far behind it, the boiler ruptured: "the train has none".
        // A machine is only sent the bodies within its interest radius; the host's reckoning of the kit now rides the run record.
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        var h = host.Host!;
        h.InterestRadius = 520;
        var train = h.Train;
        var kit = Assert.Single(h.World.Bodies.All, b => b.Kind == BodyKind.RepairKit);
        int car = train.Dynamics.Consist.IndexOf(kit.Parent);
        Assert.True(car > 0);
        // Out through the gate and stopped (the run under way, as the director's was), then a kilometre back down the line.
        train.Dynamics.Restore(h.World.Run!.YardLength + 200, 0, 1);
        for (int t = 0; t < SimConstants.TickRate; t++)
            host.Step(default);
        Assert.Equal(RunPhase.Underway, h.World.Run.Phase);
        var back = train.Frames[train.Frames.Count - 1].ToWorld(new Double3(4, 0, 1_000));
        h.SetPlayerState(h.Players.First().Id, new PlayerState { Parent = PlayerState.World, Position = back, Surface = Surface.Ground, Health = P.Health });
        train.Boiler.Ruptured = true;
        for (int t = 0; t < SimConstants.TickRate * 2; t++)
            host.Step(default);
        Assert.DoesNotContain(host.World.Bodies.All, b => b.Kind == BodyKind.RepairKit);
        Assert.Equal(KitPlace.Lying, host.World.Run!.Kit.Place);
        Assert.Equal($"THE REPAIR KIT MENDS IT. IT'S IN CAR {car}", Hud.RepairKitWhere(host.World, host.PlayerId));
    }
}
