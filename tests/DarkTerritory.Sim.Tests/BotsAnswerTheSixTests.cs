using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The crew bots answer the six creatures of 8 Oct (ARCHITECTURE §8 note NNN; the director: "have the bots handle the six new
/// creatures too"), each by the counter-play on its design page: the Mourners scattered off a body, Tower Jaw driven off and
/// its wreck cleared with the driver stopped short of it, the Brakeman's brakes unwound and the man cornered by two, the
/// Knotter's gap never walked and the Knotter killed slack at a stand and the cars coupled up, the Freight Beetle driven
/// off, and a Hotbox stopped for, prised out from the side and its axle freed.
/// </summary>
public class BotsAnswerTheSixTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    /// <summary>The director quiet (its grace never over): only what each test puts there.</summary>
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    static StopHand Hand(int id) => new(StopJob.None, new CrewCalls(), id, P.Cold);

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Ground(Night n, Double3 at) => PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    static List<(int Id, PlayerState State)> Others(Night n, int id) => [.. n.Crew.Where(c => c.Key != id).Select(c => (c.Key, c.Value))];

    /// <summary>The night, its bodies stepped too (what's hauled or pushed is their own physics).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent> intent, Func<bool>? until = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate && !(until?.Invoke() ?? false); i++)
        {
            n.Run(SimConstants.TickSeconds, intent);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    /// <summary>
    /// The driver (id 1, in the cab) at the controls as the host works them from its intent, the rest by
    /// <paramref name="walkers"/>; the train's speed its own. Stops early once <paramref name="until"/>.
    /// </summary>
    static void Drive(Night n, ConductorBot driver, Func<int, PlayerIntent> walkers, double seconds, Func<bool>? until = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate && !(until?.Invoke() ?? false); i++)
        {
            driver.Crewmates = [.. n.Crew.Where(c => c.Key != 1).Select(c => c.Value)];
            var intent = driver.Decide(n.Crew[1], n.World, n.World.Tick, out _);
            if (CabControls.Clears(n.Controls, n.Train, CabControls.ReleasesBrake(intent, n.Crew[1], n.Train)))
                n.Controls.Brake = 0;
            CabControls.Apply(ref n.Controls, intent, n.Crew[1], n.Train);
            n.Run(SimConstants.TickSeconds, id => id == 1 ? intent : walkers(id), holdSpeed: false);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    [Fact]
    public void ABotGoesToABodyTheMournersAreDraggingAndClubsThemOffIt()
    {
        var n = new Night(4, speed: 0, enemies: Quiet);
        var at = Beside(n, 2, 8);
        var body = n.World.Bodies.SpawnRagdoll(n.Train, 3, new PlayerState { Parent = PlayerState.World, Position = at + Double3.Up * 0.3, LineHint = n.Train.Dynamics.Distance });
        Run(n, E.Mourners.After + 20, _ => default);
        Assert.Contains(n.World.ActiveEnemies.OfType<Mourner>(), m => body.TakenBy == m.Id);
        int came = n.World.ActiveEnemies.OfType<Mourner>().Count(m => !m.Gone);
        // A crewmate on the ballast by the train, 20 m off it: it goes, and clubs them.
        var from = Beside(n, 3, 3);
        var bodyAt = Bodies.WorldCentre(body, n.Train);
        n.Crew[2] = Ground(n, bodyAt + ((from - bodyAt) with { Y = 0 }).Normalized * 20);
        var hand = Hand(2);
        Assert.True((Bodies.WorldCentre(body, n.Train) - n.Crew[2].Position).Length < E.CrewBots.MournersWithin);
        Run(n, 60, id => Heed.Mourners(default, n.Crew[id], n.World, id, hand));
        Assert.Equal(-1, body.TakenBy);
        Assert.Equal(0, n.World.MournersTook);
        Assert.Contains(n.World.Bodies.All, b => b.Id == body.Id);
        Assert.True(n.World.ActiveEnemies.OfType<Mourner>().Count(m => !m.Gone) < came, "it never killed one");
        Assert.Equal(P.Health, n.Crew[2].Health);
        n.AssertFair();
    }

    [Fact]
    public void ABotOnFootClubsTheFreightBeetleOffItsLoad()
    {
        var n = new Night(4, speed: 0, enemies: Quiet);
        var at = Beside(n, 2, 14);
        var crate = n.World.Bodies.SpawnCargo(at, n.Train.Dynamics.Distance);
        var beetle = n.World.AddEnemy(id => FreightBeetle.At(id, at + new Double3(2.5, 0, 0), n.Train.Dynamics.Distance, 0, E.FreightBeetle));
        n.Crew[2] = Ground(n, Beside(n, 2, 4, 6));
        bool off = false;
        Run(n, 30, id => Heed.Beetle(default, n.Crew[id], n.World, id), until: () => off |= beetle.Mode == BeetleMode.Away || beetle.Gone);
        Assert.True(off, $"the beetle's {beetle.Mode}, {beetle.Health} health");
        Assert.True(n.Crew[2].Alive);
        Assert.Equal(P.Health, n.Crew[2].Health);
    }

    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
    static readonly RouteFeature Tower = Frontier.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);

    /// <summary>frontier:7 with its stops, the engine's front at <paramref name="front"/>, Tower Jaw at the coaling tower.</summary>
    static (Night N, TowerJaw Jaw) AtTheTower(double front, double speed, double gnawed)
    {
        var n = new Night(4, speed, Frontier, enemies: Quiet, front: front);
        n.World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
        int facility = n.World.Run!.Facilities.ToList().IndexOf(Tower);
        var jaw = n.World.AddEnemy(id => TowerJaw.AtCoalingTower(id, n.World, facility, E.TowerJaw, gnawed));
        return (n, jaw);
    }

    /// <summary>A roof walker (no stop part) whose legs are its own, as a crew bot's are.</summary>
    static RoofWalkerBot Walker(int id, int head = -1)
    {
        var w = new RoofWalkerBot(id, P.Cold) { Me = id };
        w.Head(head);
        return w;
    }

    [Fact]
    public void BotsOnFootDriveTowerJawOffItsPostAndItNeverKillsThem()
    {
        var (n, jaw) = AtTheTower(Tower.Start - 40, 0, gnawed: 0.3);
        var at = jaw.Local;
        // Two of the crew down by the train, 15 m and 20 m off it: both go and club it.
        n.Crew[2] = Ground(n, at + new Double3(15, 0, 0));
        n.Crew[3] = Ground(n, at + new Double3(0, 0, 20));
        var hands = new Dictionary<int, StopHand> { [2] = Hand(2), [3] = Hand(3) };
        Assert.All(n.Crew.Values, c => Assert.True(Flat(c.Position - at) <= E.CrewBots.TowerJawWithin));
        bool away = false;
        Run(n, 40, id => Heed.TowerJaw(default, n.Crew[id], n.World, id, hands[id]), until: () => away |= jaw.Mode == TowerJawMode.Away);
        Assert.True(away, $"Tower Jaw's {jaw.Mode}, {jaw.Health} health");
        Assert.All(n.Crew.Values, c => Assert.True(c.Alive));
        Assert.True(jaw.Gnawed < 0.5);
        n.AssertFair();
    }

    [Fact]
    public void TheDriverStopsShortOfTowerJawsWreckTheBotsClearItAndTheTrainGoesOn()
    {
        var (n, jaw) = AtTheTower(Tower.Start - 700, 12, gnawed: 0.97);
        n.Run(0.05 * E.TowerJaw.GnawFor(RouteTier.Frontier) + 0.5);
        Assert.Equal(TowerJawMode.Wreck, jaw.Mode);
        double along = jaw.Blocks!.Value;
        var driver = new ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        var legs = new Dictionary<int, RoofWalkerBot> { [2] = Walker(2), [3] = Walker(3) };
        var hands = new Dictionary<int, StopHand> { [2] = Hand(2), [3] = Hand(3) };
        PlayerIntent Walk(int id) =>
            Heed.TowerJaw(legs[id].Decide(n.Crew[id], n.World, n.World.Tick, out _), n.Crew[id], n.World, id, hands[id]);
        double nearest = double.MaxValue;
        Drive(n, driver, Walk, 400, until: () =>
        {
            if (!jaw.Gone)
                nearest = Math.Min(nearest, along - E.TowerJaw.WreckHalf - n.Train.Dynamics.Distance);
            return n.Train.Dynamics.Distance > along + 50;
        });
        Assert.True(jaw.Gone, $"the wreck's {jaw.Cleared:0} crew-seconds cleared; the train {along - n.Train.Dynamics.Distance:0} m short");
        Assert.Equal(1, n.World.TowersCleared);
        // Stopped short of it (never run into it: nothing hurt), and on past where it lay once it was cleared, the crew aboard.
        Assert.InRange(nearest, 1, E.CrewBots.WreckStopShort + E.CrewBots.WreckHoldWithin);
        Assert.Equal(1, n.Train.Vehicles[0].Integrity);
        Assert.True(n.Train.Dynamics.Distance > along + 50, $"the train's {along - n.Train.Dynamics.Distance:0} m short ({driver.SixStep}; " + string.Join(", ", n.Crew.Select(c => $"{c.Key}: {c.Value.Parent} {c.Value.Surface} {c.Value.Position.X:0.0},{c.Value.Position.Z:0.0} {legs.GetValueOrDefault(c.Key)?.Job?.Doing}")) + ")");
        Assert.All(n.Crew.Values, c => Assert.True(c.Alive && c.Parent != PlayerState.World));
    }

    static Brakeman AtTail(Night n) => n.World.AddEnemy(id =>
    {
        var last = n.Train.Cars[^1];
        return Brakeman.Up(id, n.Train, last.FrontDistance - last.Length + 0.5, 1, E.Brakeman);
    });

    [Fact]
    public void ARoofBotUnwindsTheWoundCarsAtTheirWheelsAndNeverWindsTheRakesOn()
    {
        var n = new Night(6, speed: 10, enemies: Quiet);
        n.Train.Vehicles[4].Wound = true;
        n.Train.Vehicles[5].Wound = true;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        Run(n, 60, id => Heed.Brakeman(default, n.Crew[id], n.World, id, Others(n, id), null),
            until: () => n.Train.Vehicles.All(v => !v.Wound));
        Assert.All(n.Train.Vehicles, v => Assert.False(v.Wound));
        // Held a while longer at the last wheel, as a late snapshot would have it: still nothing wound on.
        Run(n, 5, id => Heed.Brakeman(default, n.Crew[id], n.World, id, Others(n, id), null));
        Assert.False(n.Train.Dynamics.Handbrake);
        Assert.All(n.Train.Vehicles, v => Assert.False(v.Wound));
        Assert.True(n.Crew[2].Alive);
    }

    [Fact]
    public void ALoneBotKeepsUnwindingAndNeverChasesHim()
    {
        var n = new Night(6, speed: 10, enemies: Quiet);
        var b = AtTail(n);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        int unwound = 0;
        var was = n.Train.Vehicles.Select(v => v.Wound).ToArray();
        for (int t = 0; t < 120 * SimConstants.TickRate; t++)
        {
            Run(n, SimConstants.TickSeconds, id => Heed.Brakeman(default, n.Crew[id], n.World, id, Others(n, id), null));
            for (int c = 0; c < was.Length; c++)
            {
                unwound += was[c] && !n.Train.Vehicles[c].Wound ? 1 : 0;
                was[c] = n.Train.Vehicles[c].Wound;
            }
            Assert.NotEqual(BrakemanMode.Cornered, b.Mode);
        }
        Assert.True(unwound >= 2, $"{unwound} unwound");
        Assert.False(b.Gone);
        Assert.False(n.Train.Dynamics.Handbrake);
    }

    [Fact]
    public void TwoRoofBotsCornerTheBrakemanFromBothSidesAndKillHim()
    {
        var n = new Night(6, speed: 10, enemies: Quiet);
        var b = AtTail(n);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 4, 0, P);
        bool cornered = false;
        Run(n, 300, id => Heed.Brakeman(default, n.Crew[id], n.World, id, Others(n, id), null), until: () =>
        {
            cornered |= b.Mode == BrakemanMode.Cornered;
            return b.Gone;
        });
        Assert.True(cornered, $"never cornered: {b.Mode}");
        Assert.True(b.Gone, $"he's {b.Mode}, {b.Health} health");
        Assert.Contains(EnemyKind.Brakeman, n.World.Slain);
        Assert.All(n.Crew.Values, c => Assert.True(c.Alive));
        Assert.False(n.Train.Dynamics.Handbrake);
        n.AssertFair();
    }

    [Fact]
    public void ARoofWalkerNeverWalksOrJumpsAKnottersGap()
    {
        var n = new Night(6, speed: 12, enemies: Quiet);
        var k = n.World.AddEnemy(id => Knotter.Into(id, n.Train, 3, E.Knotter));
        n.Run(E.Knotter.CreepSeconds + E.Knotter.ForceSeconds + 0.5);
        Assert.Equal(KnotterMode.Taut, k.Mode);
        // One each side of it, walking at it.
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        var legs = new Dictionary<int, RoofWalkerBot> { [2] = Walker(2, +1), [3] = Walker(3, -1) };
        int turnedAt2 = 0, turnedAt4 = 0;
        for (int t = 0; t < 90 * SimConstants.TickRate; t++)
        {
            n.Run(SimConstants.TickSeconds, id => legs[id].Decide(n.Crew[id], n.World, n.World.Tick, out _));
            // On its own side of the knot (in the air only over a gap it jumped), never on the knot's back nor in its coil.
            Assert.True(n.Crew[2].Parent is >= 0 and <= 3 || n.Crew[2].Surface == Surface.Air, $"walker 2 on {n.Crew[2].Parent}");
            Assert.True(n.Crew[3].Parent is >= 4 || n.Crew[3].Surface == Surface.Air, $"walker 3 on {n.Crew[3].Parent}");
            Assert.All(n.Crew.Values, c => Assert.True(c.Alive && !c.Has(PlayerFlags.Held) && c.Surface is not (Surface.Coupler or Surface.Ground)));
            turnedAt2 += n.Crew[2].Parent == 3 ? 1 : 0;
            turnedAt4 += n.Crew[3].Parent == 4 ? 1 : 0;
        }
        Assert.True(turnedAt2 > 0 && turnedAt4 > 0, "a walker never came to the knot");
        Assert.DoesNotContain(n.Events, e => e.Kind == EnemyKind.Knotter && e.To == SpinePhase.Grab);
    }

    [Fact]
    public void TheDriverStopsForTheKnotterTheBotsKillItSlackAndTheTrainCouplesUpAndGoesOn()
    {
        var n = new Night(6, speed: 12, enemies: Quiet);
        var k = n.World.AddEnemy(id => Knotter.Into(id, n.Train, 3, E.Knotter));
        n.Run(E.Knotter.CreepSeconds + E.Knotter.ForceSeconds + 0.5);
        Assert.Equal(KnotterMode.Taut, k.Mode);
        int vehicles = n.Train.Dynamics.Consist.Vehicles.Count;
        var driver = new ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        var legs = new Dictionary<int, RoofWalkerBot> { [2] = Walker(2), [3] = Walker(3) };
        var hands = new Dictionary<int, StopHand> { [2] = Hand(2), [3] = Hand(3) };
        PlayerIntent Walk(int id) =>
            Heed.Knotter(legs[id].Decide(n.Crew[id], n.World, n.World.Tick, out _), n.Crew[id], n.World, id, hands[id]);
        bool slack = false, split = false;
        Drive(n, driver, Walk, 300, until: () =>
        {
            slack |= k.Mode == KnotterMode.Slack;
            split |= n.Train.TrainRakes > 1;
            return k.Gone && n.Train.TrainRakes == 1 && n.Train.Dynamics.Speed > 4;
        });
        Assert.True(slack, "it never went slack");
        Assert.True(k.Gone, $"the Knotter's {k.Mode}, {k.Health} health");
        Assert.True(split, "killed, it never left the cars uncoupled");
        Assert.Equal(1, n.Train.TrainRakes);
        Assert.Equal(vehicles, n.Train.Dynamics.Consist.Vehicles.Count);
        Assert.True(n.Train.Dynamics.Speed > 4, $"standing at {n.Train.Dynamics.Speed:0.0} m/s ({driver.SixStep})");
        Assert.All(n.Train.Vehicles, v => Assert.Equal(0, v.Knot));
        Assert.All(n.Crew.Values, c => Assert.True(c.Alive && c.Parent != PlayerState.World));
        n.AssertFair();
    }

    [Fact]
    public void TheDriverStopsForAHotboxABotPrisesItOutFromTheSideAndFreesTheAxle()
    {
        var t = E.Hotbox;
        var n = new Night(6, speed: 14, enemies: Quiet);
        var h = n.World.AddEnemy(id => Hotbox.In(id, n.Train, 3, rear: true, 1, t));
        // Hot enough to have seized its axle: the train dragging, the glow seen.
        h.Extra = t.KnockSeconds + t.GlowSeconds + 1;
        n.Run(0.5);
        Assert.Equal(HotboxMode.Seized, h.Mode);
        Assert.True(n.Train.Vehicles[3].Seized);
        var driver = new ConductorBot(null, 0);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        var legs = new Dictionary<int, RoofWalkerBot> { [2] = Walker(2) };
        var hands = new Dictionary<int, StopHand> { [2] = Hand(2) };
        PlayerIntent Walk(int id) =>
            Heed.Hotbox(legs[id].Decide(n.Crew[id], n.World, n.World.Tick, out _), n.Crew[id], n.World, id, hands[id], Others(n, id));
        bool prised = false, stood = false;
        Drive(n, driver, Walk, 300, until: () =>
        {
            prised |= h.Mode == HotboxMode.Prised;
            stood |= n.Train.Dynamics.Speed < 0.05;
            return h.Gone && !n.Train.Vehicles[3].Seized && n.Train.Dynamics.Speed > 4;
        });
        Assert.True(stood, "the driver never stopped");
        Assert.True(prised, $"never prised: {h.Mode}");
        Assert.True(h.Gone);
        Assert.False(n.Train.Vehicles[3].Seized);
        Assert.True(n.Train.Dynamics.Speed > 4, $"standing at {n.Train.Dynamics.Speed:0.0} m/s ({driver.SixStep})");
        // From the side: out of its bite all along.
        Assert.Equal(P.Health, n.Crew[2].Health);
        Assert.True(n.Crew[2].Parent != PlayerState.World);
    }

    [Fact]
    public void ABotCrewOverTheNetworkStopsForAHotboxPrisesItAndFreesTheAxle()
    {
        // The whole crew as a night runs it (each bot a client, over lossy loopback, reading only what's sent): 30 s in, a
        // Hotbox has seized car 3's axle. The driver stops, a walker gets down and prises it out, the axle's freed, and on
        // (stood at 39 s, freed at 68, on at 77, when it was written).
        var t = E.Hotbox;
        Hotbox? h = null;
        double? stood = null, freed = null, on = null;
        CrewOfTwoTests.Night("frontier:7", 6, 110, null, start: 2500, bots: 3, each: w =>
        {
            if (h is null && w.ElapsedSeconds >= 30)
            {
                h = w.AddEnemy(id => Hotbox.In(id, w.Train, 3, rear: true, 1, t));
                h.Extra = t.KnockSeconds + t.GlowSeconds + 1;
            }
            if (h is null)
                return;
            if (stood is null && w.Train.Dynamics.Speed < 0.05)
                stood = w.ElapsedSeconds;
            if (stood is not null && freed is null && h.Gone && !w.Train.Vehicles[3].Seized)
                freed = w.ElapsedSeconds;
            if (freed is not null && on is null && w.Train.Dynamics.Speed > 4)
                on = w.ElapsedSeconds;
        });
        Assert.NotNull(h);
        Assert.True(stood is not null, "the driver never stopped");
        Assert.True(freed is not null, $"never freed: the Hotbox {h!.Mode}, car 3 seized {h.Gone}");
        Assert.True(on is not null, $"freed at {freed:0} s, never on again");
    }
}
