using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Draggers (T46, App. A.4, B.4): reaching up from beneath a car's edge. Rule: stay off the edges.</summary>
public class DraggerTests
{
    static readonly DraggerTuning D = Tuning.Enemies.Draggers;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));
    const int Car = 2;

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public double Speed;

        public Night(double speed)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;
        public CarShape Shape => Train.Frames[Car].Shape;

        /// <summary>Someone on car 2's roof, this far across from its centreline (+ its right), this far along.</summary>
        public int OnRoof(double x, double z = 0)
        {
            Crew.Add(PlayerMotor.SpawnOnRoof(Train, Car, z, Tuning.Player, x));
            Intents.Add(default);
            return Crew.Count;
        }

        public Dragger Under(int side, double along = 0) => World.AddEnemy(id => Dragger.Under(id, Train, Car, side, along));

        public void Run(double seconds)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    PlayerMotor.Step(ref s, Intents[c], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = s;
                }
            }
        }
    }

    /// <summary>Well within grab range of the right edge.</summary>
    static double NearTheEdge(Night n) => n.Shape.HalfWidth - D.GrabRange * 0.5;

    [Fact]
    public void WalkTheCentrelineAndItNeverReaches()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1);
        night.OnRoof(0);
        night.Run(6);
        Assert.Equal(SpinePhase.Dormant, dragger.Phase);
        Assert.True(night.Crew[0].Alive);
        Assert.Equal(Surface.Roof, night.Crew[0].Surface);
    }

    [Fact]
    public void NearTheEdgeItReachesOverTheLipThenPullsYouOffAndAtSpeedThatKills()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1, along: 3);
        int id = night.OnRoof(NearTheEdge(night), z: 0);
        // It follows the walker along under the edge, then the limb comes up: the telegraph, with the reaction window.
        for (int i = 0; i < 5 * SimConstants.TickRate && dragger.Phase == SpinePhase.Dormant; i++)
            night.Run(SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Telegraph, dragger.Phase);
        Assert.InRange(dragger.Local.Z, 0, D.ReachAlong + 0.05);
        Assert.Equal(id, dragger.Target);
        night.Run(Tuning.Enemies.MinReactionSeconds - 0.3);
        Assert.Equal(Surface.Roof, night.Crew[0].Surface);
        // Grabbed (GDD v1.1 App. A.4): hanging off the side for a few seconds, and with nobody to haul them up, dragged under.
        night.Run(0.5);
        Assert.Equal(SpinePhase.Grab, dragger.Phase);
        Assert.True(night.Crew[0].Has(PlayerFlags.Held));
        night.Run(D.HangSeconds);
        Assert.Equal(DeathCause.Dragged, night.Crew[0].Death);
        // It never leaves its car: back under the edge.
        Assert.Equal(Car, dragger.Attached);
        Assert.False(dragger.Gone);
    }

    [Fact]
    public void StepBackFromTheEdgeInTimeAndItSinksBackUnder()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1);
        night.OnRoof(NearTheEdge(night));
        night.Run(0.5);
        Assert.Equal(SpinePhase.Telegraph, dragger.Phase);
        night.Crew[0] = night.Crew[0] with { Position = night.Crew[0].Position with { X = 0 } };
        night.Run(0.2);
        Assert.Equal(SpinePhase.Dormant, dragger.Phase);
        // And for a while it won't reach again, even at the edge.
        night.Crew[0] = night.Crew[0] with { Position = night.Crew[0].Position with { X = NearTheEdge(night) } };
        night.Run(D.RearmSeconds * 0.5);
        Assert.Equal(SpinePhase.Dormant, dragger.Phase);
        Assert.True(night.Crew[0].Alive);
    }

    [Fact]
    public void WithSomeoneNearTheyHaveTheHangToHaulYouUp()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1);
        night.OnRoof(NearTheEdge(night));
        night.OnRoof(0, z: 1.0);
        night.Run(Tuning.Enemies.MinReactionSeconds + 0.3);
        Assert.Equal(SpinePhase.Grab, dragger.Phase);
        // Hanging there: the window is open.
        night.Run(2);
        Assert.Equal(Surface.Roof, night.Crew[0].Surface);
        // The mate takes hold (Use, close by): it lets go.
        night.Intents[1] = new PlayerIntent { Buttons = PlayerButtons.Use };
        night.Run(0.2);
        night.Intents[1] = default;
        Assert.Equal(SpinePhase.Dormant, dragger.Phase);
        night.Run(2);
        Assert.True(night.Crew[0].Alive);
        Assert.Equal(Surface.Roof, night.Crew[0].Surface);
    }

    [Fact]
    public void WithSomeoneNearWhoDoesNothingItStillTakesYou()
    {
        var night = new Night(speed: 14);
        night.Under(side: 1);
        night.OnRoof(NearTheEdge(night));
        night.OnRoof(0, z: 1.0);
        night.Run(Tuning.Enemies.MinReactionSeconds + 0.3 + D.HangSeconds + 0.3);
        Assert.Equal(DeathCause.Dragged, night.Crew[0].Death);
        Assert.True(night.Crew[1].Alive);
    }

    [Fact]
    public void AtMaxSpeedItReachesFarther()
    {
        // Spec B.3: "Max 22 m/s: Draggers +50% grab range".
        Assert.Equal(D.GrabRange, D.GrabAt(10), 9);
        Assert.Equal(D.GrabRange * D.FastGrabScale, D.GrabAt(22), 9);
        var night = new Night(speed: 22);
        var dragger = night.Under(side: 1);
        night.OnRoof(night.Shape.HalfWidth - D.GrabRange * 1.3);
        night.Run(0.5);
        Assert.Equal(SpinePhase.Telegraph, dragger.Phase);
        var slow = new Night(speed: 12);
        var there = slow.Under(side: 1);
        slow.OnRoof(slow.Shape.HalfWidth - D.GrabRange * 1.3);
        slow.Run(0.5);
        Assert.Equal(SpinePhase.Dormant, there.Phase);
    }

    [Fact]
    public void TheOtherEdgeIsNotItsEdge()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1);
        night.OnRoof(-NearTheEdge(night));
        night.Run(4);
        Assert.Equal(SpinePhase.Dormant, dragger.Phase);
        Assert.True(night.Crew[0].Alive);
    }

    [Fact]
    public void TheDirectorPutsThemUnderTheCarsOnlyOnASlowTrainAndTheyWaitForSomeoneOnTheRoofs()
    {
        // Boarding-first (GDD App. F.1, note 286): "slowing opens the doors". At speed none get on, whoever's up top; slow,
        // they get under the cars with nobody up, and lie there till someone walks a roof over one.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
        var world = new World(train, Tuning.Combat);
        // Only Draggers on offer: a director allowed to spend on nothing else.
        var d = Tuning.Enemies.Director;
        var t = Tuning.Enemies with
        {
            Director = d with { GraceMinSeconds = 0, GraceMaxSeconds = 0, Pressure = Tuning.Eager, Draw = Tuning.Unheld, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "draggers" ? c.Value : 1e9) },
        };
        world.EnableEnemies(t, route: null, 1, crew: 2, authority: true);
        void Run(PlayerState s, double seconds, double speed)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                train.Dynamics.Velocity = speed;
                world.BeginTick();
                world.CrewAct(ref s, default, 1);
                world.Step(new TrainControls { Reverser = 1 });
            }
        }
        var up = PlayerMotor.SpawnOnRoof(train, 3, 0, Tuning.Player);
        Run(up, 120, D.BoardBelow + 6);
        Assert.DoesNotContain(world.ActiveEnemies, e => e.Kind == EnemyKind.Dragger);
        var inside = new PlayerState { Parent = 0, Position = train.Frames[0].Shape.Cab!.Value.Centre, Surface = Surface.Deck, Health = 100 };
        Run(inside, 120, D.BoardBelow - 4);
        var waiting = world.ActiveEnemies.Where(e => e.Kind == EnemyKind.Dragger).ToList();
        Assert.NotEmpty(waiting);
        Assert.InRange(waiting.Count, 1, D.MaxAttached);
        Assert.All(waiting, e => Assert.Equal(SpinePhase.Dormant, e.Phase));
    }

    [Fact]
    public void AClientSeesTheLimbAndWhoItsGot()
    {
        var night = new Night(speed: 14);
        var dragger = night.Under(side: 1);
        int id = night.OnRoof(NearTheEdge(night));
        night.Run(0.5);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 2, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Dragger>(Assert.Single(client.ActiveEnemies));
        Assert.Equal((SpinePhase.Telegraph, id, 1, Car), (seen.Phase, seen.Target, seen.Side, seen.Attached));
        Assert.Equal(dragger.Local.Z, seen.Local.Z, 2);
    }

    // ---- Off a truss (note 435, orchestrator.md §5.2 S4) ----

    /// <summary>A Dragger perched on a truss's top chord this far ahead of the engine, on a side.</summary>
    static Dragger OnTruss(Night n, double ahead, int side = 1) =>
        n.World.AddEnemy(id => Dragger.OnTruss(id, n.Train.Dynamics.Distance + ahead, side, D.Drop));

    [Fact]
    public void ADraggerOnATrussScrapesThenDropsOnWhoeverIsOnTheRoofPassingUnder()
    {
        // At 20 m/s, a Dragger on the chord 150 m ahead; a crewmate on car 2's roof, on the centreline (out of any Dragger's
        // reach from under the edge: it's the drop from above that takes them).
        var n = new Night(speed: 20);
        int id = n.OnRoof(0);
        var dragger = OnTruss(n, 150);
        Assert.True(dragger.Perched);
        double scraping = -1, dropped = -1;
        for (int i = 0; i < 20 * SimConstants.TickRate && dropped < 0; i++)
        {
            n.Run(SimConstants.TickSeconds);
            double now = i * SimConstants.TickSeconds;
            if (scraping < 0 && dragger.Phase == SpinePhase.Telegraph)
                scraping = now;
            if (!dragger.Perched)
                dropped = now;
        }
        Assert.True(scraping >= 0 && dropped > scraping, $"scraping at {scraping}, dropped at {dropped}");
        // The tell: the scrape on the steel from about tellSeconds before the engine's under it, never under the reaction window.
        Assert.True(dropped - scraping >= Tuning.Enemies.MinReactionSeconds, $"{dropped - scraping:0.00} s of scraping");
        Assert.Equal(Car, dragger.Attached);
        Assert.Equal(SpinePhase.Grab, dragger.Phase);
        Assert.Equal(id, dragger.Target);
        Assert.True(n.Crew[id - 1].Has(PlayerFlags.Held));
        // At the car's edge, on its chord's side: hanging them over the side as a Dragger does (App. A.4).
        Assert.True(Math.Abs(dragger.Local.X) > n.Shape.HalfWidth);
    }

    [Fact]
    public void AWalkerHearsTheScrapeAndIsOffTheRoofBeforeItDrops()
    {
        // Note 442 (note 435's "not yet"): a bot walker on car 2's roof at 20 m/s, a Dragger on the chord 150 m ahead. The scrape
        // is a roof warning, as a tunnel's mouth is: off the roof (into the gap or a car) before the car's under it, so it
        // drops on nobody (on its own, it's into car 1 and the door shut); and back up on the roofs after.
        var n = new Night(speed: 20);
        int id = n.OnRoof(0, z: 3);
        var walker = new Bots.RoofWalkerBot(5, Tuning.Player.Cold, new Bots.StopHand(Bots.StopJob.None, new Bots.CrewCalls(), 1, Tuning.Player.Cold)) { Me = id };
        var dragger = OnTruss(n, 150);
        bool grabbed = false, off = false, backUp = false;
        double through = 150 / 20.0 + n.Train.Dynamics.Consist.LengthMetres / 20 + 2;
        for (int i = 0; i < (through + 20) * SimConstants.TickRate; i++)
        {
            walker.Crew = [(id, n.Crew[id - 1])];
            n.Intents[id - 1] = walker.Decide(n.Crew[id - 1], n.World, (uint)i, out _);
            n.Run(SimConstants.TickSeconds);
            grabbed |= dragger.Target == id || n.Crew[id - 1].Has(PlayerFlags.Held);
            off |= dragger.Phase == SpinePhase.Telegraph && n.Crew[id - 1].Surface != Surface.Roof;
            backUp |= dragger.Gone && n.Crew[id - 1] is { Surface: Surface.Roof, Parent: > 0 };
        }
        var s = n.Crew[id - 1];
        Assert.False(grabbed, "the Dragger dropped on the walker");
        Assert.True(off, "never off the roof while it scraped");
        Assert.True(dragger.Gone);
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.True(backUp, $"not back up on the roofs after: {s.Surface} on {s.Parent}");
    }

    [Fact]
    public void NobodyOnTheRoofsAndItDropsOnTheBallastAndIsGone()
    {
        var n = new Night(speed: 20);
        var dragger = OnTruss(n, 150);
        n.Run(150 / 20.0 + n.Train.Dynamics.Consist.LengthMetres / 20 + 2);
        Assert.True(dragger.Gone);
        Assert.True(dragger.Attached < 0);
    }

    [Fact]
    public void ARunFastTrainFindsADraggerOnFrontier7sTruss()
    {
        // The director puts one on the chord of a through-truss coming up within `ahead` of a train at fromSpeed or more.
        var content = DataFile.FindContentRoot();
        var route = LineGen.Routes.Generate(content, "frontier:7", 10);
        var truss = route.Plan!.Structures.First(st => st.Type == LineGen.StructureType.Truss && st.Edge == "main");
        var e = Tuning.Enemies with
        {
            Director = HoundRunTests.Quiet.Director with { Run = HoundRunTests.Quiet.Director.Run with { On = false } },
            Draggers = D with { Drop = D.Drop with { Chance = 1 } },
        };
        var n = new global::DarkTerritory.Sim.Tests.Night(4, 21, route, enemies: e);
        var d = n.World.Director!;
        for (int s = 0; s < 900 && d.TrussDraggers == 0 && n.Train.Dynamics.Distance < truss.S0; s++)
            n.Run(1);
        Assert.Equal(1, d.TrussDraggers);
        var perched = Assert.Single(n.World.ActiveEnemies.OfType<Dragger>(), x => x.Perched);
        Assert.InRange(perched.LineDistance, truss.S0, truss.S1);
        Assert.InRange(truss.S0 - n.Train.Dynamics.Distance, 0, D.Drop.Ahead);
    }
}
