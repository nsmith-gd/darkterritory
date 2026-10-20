using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The spawn director overhaul's guard tests (queue #602, note 887; the brief's §8 "Fixed seeds produce deterministic results",
/// "Host-authoritative multiplayer spawning remains consistent", §9 "Do not break deterministic replay or host-authoritative
/// spawning"): a seeded night is the same night every time it's played, spawn for spawn; only the host's director spawns; and
/// every client is shown the host's spawns, kind for kind, and nothing of its own.
/// </summary>
public class DirectorGuardNetTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>Everything that came into the host's world, tick by tick, and the director's sends by every channel.</summary>
    sealed record NightRecord(List<string> Arrivals, List<string> Sends, HarnessReport Report);

    /// <summary>A four-bot night on frontier:7 (the yard, the gates, the line and its stops), its director's seed the night's.</summary>
    static NightRecord Night(int seed, double seconds = 420)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = Tuning.Run.DepartFrom(gate, Consist.Uniform(T, 6, 1).LengthMetres);
        var arrivals = new List<string>();
        var seen = new HashSet<int>();
        World? last = null;
        var report = Harness.Run(route.Build(), T, P, new HarnessOptions
        {
            Observe = (tick, _, world) =>
            {
                last = world;
                foreach (var e in world.ActiveEnemies)
                    if (seen.Add(e.Id))
                    {
                        var at = e.WorldPosition(world.Train);
                        arrivals.Add($"{tick} #{e.Id} {e.Kind} {at.X:0.000} {at.Y:0.000} {at.Z:0.000}");
                    }
            },
            Bots = 4,
            Cars = 6,
            Seconds = seconds,
            Seed = seed,
            Link = new LinkConditions(0.09, 0.02, 0.03),
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = DirectorGuardTests.Pressed(grace: null),
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Loot = DataFile.Load<Stops.LootTuning>(Path.Combine(Content, Stops.LootTuning.File)),
            YardLength = gate,
        });
        var d = last!.Director!;
        var sends = d.Log.Select(l => $"log {l.Tick} {l.Kind} {l.TrainDistance:0.000} {l.Paced} {l.Free} {l.Stop}")
            .Concat(d.HoundRuns.Select(h => $"run {h.Tick} {h.Pack} {h.Size}"))
            .Concat(d.Hunts.Select(h => $"hunt {h.Tick} {h.Player} {h.Pack} {h.Size}"))
            .Append($"truss {d.TrussDraggers}").ToList();
        return new NightRecord(arrivals, sends, report);
    }

    [Fact]
    public void AFixedSeedIsTheSameNightSpawnForSpawn()
    {
        var a = Night(seed: 2);
        var b = Night(seed: 2);
        // Something happened (a guard over an empty night guards nothing)...
        Assert.True(a.Sends.Count > 3, $"only {a.Sends.Count - 1} sends");
        Assert.True(a.Arrivals.Count > 3, $"only {a.Arrivals.Count} arrivals");
        // ...and it all happened again, at the same tick, where it did.
        Assert.Equal(a.Sends, b.Sends);
        Assert.Equal(a.Arrivals, b.Arrivals);
        Assert.Equal(a.Report.Threats!.Spawned, b.Report.Threats!.Spawned);
        Assert.Equal(a.Report.Threats.Director, b.Report.Threats.Director);
        Assert.Equal(a.Report.TrainDistance, b.Report.TrainDistance);
        Assert.Equal(a.Report.Deaths, b.Report.Deaths);
    }

    [Fact]
    public void OnlyTheHostsDirectorSpawnsAndEveryClientIsShownWhatItSpawned()
    {
        var line = new RailLine(new LineDefinition("guard", [new TrackSegment(40_000)]));
        var route = new Route.Route("guard", RouteTier.Frontier, 1, new LineDefinition("guard", [new TrackSegment(40_000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var enemies = DirectorGuardTests.Pressed();
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 2_000), T, P);
        host.EnableEnemies(enemies, route, 1, 3);
        // Every client in interest of everything: what one isn't shown is then a fault, not its distance.
        host.InterestRadius = 1e6;
        var clients = Enumerable.Range(0, 3)
            .Select(_ => new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 2_000), T, P))
            .ToArray();
        foreach (var c in clients)
            c.World.EnableEnemies(enemies, route, 1, 3, authority: false);
        // Who the host had, by id, and for how many ticks; who each client was shown.
        var hostKinds = new Dictionary<int, EnemyKind>();
        var hostTicks = new Dictionary<int, int>();
        var shown = clients.Select(_ => new Dictionary<int, EnemyKind>()).ToArray();
        for (int t = 0; t < 120 * SimConstants.TickRate; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Train.Dynamics.Velocity = 14;
            host.Step();
            foreach (var e in host.World.ActiveEnemies.Where(e => !e.Gone))
            {
                hostKinds[e.Id] = e.Kind;
                hostTicks[e.Id] = hostTicks.GetValueOrDefault(e.Id) + 1;
            }
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Step(default);
                foreach (var e in clients[i].World.ActiveEnemies.Where(e => !e.Gone))
                    shown[i][e.Id] = e.Kind;
            }
        }
        var d = host.World.Director!;
        Assert.True(d.Log.Count + d.HoundRuns.Count > 0, "the host sent nothing");
        for (int i = 0; i < clients.Length; i++)
        {
            // No director on a client: it can't send, by any channel.
            Assert.Null(clients[i].World.Director);
            Assert.False(clients[i].World.Authority);
            // Nothing of its own: whatever it's shown, the host had, the same kind under the same id.
            foreach (var (id, kind) in shown[i])
            {
                Assert.True(hostKinds.TryGetValue(id, out var hk), $"client {i} has #{id} {kind}, which the host never had");
                Assert.Equal(hk, kind);
            }
            // And everything the host had for a second or more, it was shown.
            foreach (var (id, kind) in hostKinds.Where(h => hostTicks[h.Key] >= SimConstants.TickRate))
                Assert.True(shown[i].ContainsKey(id), $"client {i} was never shown #{id} {kind}");
        }
    }

    [Fact]
    public void AWorldWithoutAuthoritySpawnsNothingHoweverItsDirectorTuningPresses()
    {
        // A client's world stepped on its own (its link gone quiet), the night's tuning as pressed as it goes: nothing comes.
        var line = new RailLine(new LineDefinition("guard", [new TrackSegment(40_000)]));
        var route = new Route.Route("guard", RouteTier.Frontier, 1, new LineDefinition("guard", [new TrackSegment(40_000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 2_000);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(DirectorGuardTests.Pressed(), route, 1, 4, authority: false);
        var crew = PlayerMotor.SpawnInCab(train, P);
        for (int t = 0; t < 120 * SimConstants.TickRate; t++)
        {
            train.Dynamics.Velocity = 14;
            world.BeginTick();
            world.CrewAct(ref crew, default, 1);
            world.Step(new TrainControls { Reverser = 1 });
        }
        Assert.Null(world.Director);
        Assert.Empty(world.ActiveEnemies);
    }
}
