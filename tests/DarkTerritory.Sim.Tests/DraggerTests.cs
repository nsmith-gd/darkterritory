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
            Director = d with { GraceMinSeconds = 0, GraceMaxSeconds = 0, Pressure = Tuning.Eager, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "draggers" ? c.Value : 1e9) },
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
}
