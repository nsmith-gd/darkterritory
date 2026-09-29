using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Climbers (T58, App. A.4, B.4): on at the coupling gaps, along the roofs, into an empty car. Counter: hold the gap.</summary>
public class ClimberTests
{
    static readonly ClimberTuning C = Tuning.Enemies.Climbers;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public readonly List<EnemyEvent> Events = [];
        public double Speed;

        public Night(double speed, int cars = 5, EnemyTuning? enemies = null)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), Line, 2_000);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route: null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public int Add(PlayerState s)
        {
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        /// <summary>Stood inside a car, on its floor.</summary>
        public int InCar(int car)
        {
            var room = Train.Frames[car].Shape.Interior!.Value;
            var s = PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player);
            s.Position = room.Centre with { Y = room.Min.Y };
            s.Surface = Surface.Deck;
            return Add(s);
        }

        /// <summary>Stood in the gap behind a car, on the coupler plate.</summary>
        public int InGap(int car)
        {
            var gap = Rattle.In(0, Train, car, Train.Dynamics.Tuning.Geometry.CouplingGap).Local;
            var s = PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player);
            s.Position = gap with { Y = 0.9 };
            s.Surface = Surface.Coupler;
            return Add(s);
        }

        public Climber At(int car, int side = 1) => World.AddEnemy(id => Climber.Pacing(id, Train, car, side, C));

        /// <summary>Crew held where they are (the rig stands them still; the enemy side is what's under test).</summary>
        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                Events.AddRange(World.EnemyEvents);
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
            }
        }
    }

    [Fact]
    public void ItPacesToItsGapThenScrabblesThereBeforeItsUp()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 2);
        night.Run(1);
        Assert.Equal(SpinePhase.Dormant, c.Phase);
        Assert.Equal(-1, c.Attached);
        // Out beside the train, level with the gap.
        Assert.True(Math.Abs(c.Lateral) > night.Train.Frames[2].Shape.HalfWidth + 1);
        night.Run(C.PaceSeconds);
        Assert.True(c.Scrabbling);
        Assert.Equal(2, c.Attached);
        Assert.True(c.HitRadius > 0);
        night.Run(C.ScrabbleSeconds + 0.1);
        Assert.Equal(SpinePhase.Commit, c.Phase);
        var commit = Assert.Single(night.Events, e => e.EnemyId == c.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
        // Up on the roof of the car ahead of the gap.
        Assert.Equal(night.Train.Frames[2].Shape.RoofHeight, c.Local.Y, 3);
    }

    [Fact]
    public void AlongTheRoofsItGetsIntoTheFirstCarNobodysIn()
    {
        var night = new Night(speed: 12);
        // Someone in car 2, so it goes on past to car 1.
        int inside = night.InCar(2);
        var c = night.At(car: 2);
        night.Run(C.PaceSeconds + C.ScrabbleSeconds + 30);
        Assert.True(c.Inside);
        Assert.Equal(1, c.Attached);
        Assert.Equal(Tuning.Player.Health, night.Crew[inside - 1].Health);
    }

    [Fact]
    public void AnEmptyCarItsOnItGetsInto()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 3);
        night.Run(C.PaceSeconds + C.ScrabbleSeconds + 20);
        Assert.True(c.Inside);
        Assert.Equal(3, c.Attached);
    }

    [Fact]
    public void InsideItGoesForWhoeverComesInAndLeftAloneItLeaves()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 3);
        night.Run(C.PaceSeconds + C.ScrabbleSeconds + 20);
        Assert.True(c.Inside);
        int walkedIn = night.InCar(3);
        night.Run(C.BiteEvery * 2 + 0.1);
        Assert.True(night.Crew[walkedIn - 1].Health < Tuning.Player.Health);
        // Out again, and left alone.
        night.Crew[walkedIn - 1] = night.Crew[walkedIn - 1] with { Parent = 1 };
        night.Run(C.BoredSeconds + 1);
        Assert.True(c.Gone);
    }

    [Fact]
    public void SomeoneInTheGapHoldsItAndItTriesAnother()
    {
        var night = new Night(speed: 12);
        night.InGap(2);
        var c = night.At(car: 2);
        night.Run(C.PaceSeconds + 0.5);
        Assert.NotEqual(2, c.Gap);
        Assert.False(c.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == c.Id && e.To == SpinePhase.BreakOff);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == c.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void GettingIntoTheGapWhileItScrabblesStopsIt()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 2);
        night.Run(C.PaceSeconds + 0.5);
        Assert.True(c.Scrabbling);
        night.InGap(2);
        night.Run(0.1);
        Assert.False(c.Scrabbling);
        Assert.NotEqual(2, c.Gap);
    }

    [Fact]
    public void EveryGapHeldAndItGivesUp()
    {
        var night = new Night(speed: 12, cars: 3);
        foreach (int g in Climber.Gaps(night.Train))
            night.InGap(g);
        var c = night.At(car: Climber.Gaps(night.Train)[0]);
        night.Run((C.PaceSeconds + 2) * C.MaxTries + 5);
        Assert.True(c.Gone);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == c.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void OnTheRoofsTheGunsCanTakeIt()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 2);
        night.Run(C.PaceSeconds + C.ScrabbleSeconds + 1);
        Assert.Equal(SpinePhase.Commit, c.Phase);
        Assert.Contains(night.World.Targets, t => t.Id == c.Id);
    }

    [Fact]
    public void TheGapsAreTheCouplingsBehindTheCarsAndTheDirectorWeighsThem()
    {
        Assert.Equal([1, 2, 3, 4], Climber.Gaps(new Night(speed: 12, cars: 5).Train));
        // A car and the engine: no gap to mount.
        Assert.Empty(Climber.Gaps(new Night(speed: 12, cars: 1).Train));
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "climbers" ? 0.5 : 1e9) },
        };
        var slow = new Night(speed: C.MinSpeed - 2, cars: 5, only);
        slow.Run(10);
        Assert.DoesNotContain(slow.World.Director!.Log, l => l.Kind == EnemyKind.Climber);
        var short2 = new Night(speed: 12, cars: 2, only);
        short2.Run(10);
        Assert.DoesNotContain(short2.World.Director!.Log, l => l.Kind == EnemyKind.Climber);
        var night = new Night(speed: 12, cars: 5, only);
        night.Run(10);
        Assert.Contains(night.World.Director!.Log, l => l.Kind == EnemyKind.Climber);
        Assert.True(night.World.ActiveEnemies.Count(e => e.Zone == PressureZone.Flank) <= d.MaxConcurrentZone);
        Assert.Equal(3, d.Costs["climbers"]);
    }

    [Fact]
    public void AClientSeesItAtTheGap()
    {
        var night = new Night(speed: 12);
        var c = night.At(car: 2);
        night.Run(C.PaceSeconds + 0.5);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Climber>(Assert.Single(client.ActiveEnemies));
        Assert.True(seen.Scrabbling);
        Assert.Equal(2, seen.Attached);
        Assert.Equal(c.Local.X, seen.Local.X, 2);
    }
}
