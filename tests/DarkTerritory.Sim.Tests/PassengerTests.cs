using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Passenger (T61, App. A.7, B.7): aboard at a stop wearing a crewmate's face, never a word, the same job over and over,
/// one too many. It takes someone alone in a car. Counter: a head count, make everyone speak, and call it out.
/// </summary>
public class PassengerTests
{
    static readonly PassengerTuning P = Tuning.Enemies.Passenger;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public readonly List<EnemyEvent> Events = [];

        public Night()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(quiet, null, 1, crew: 4, authority: true);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>The cars with a room, front to back.</summary>
        public List<int> Rooms => Train.Dynamics.Consist.Vehicles.Skip(1).Where(v => Train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).ToList();

        /// <summary>Standing in a car's room, <paramref name="z"/> from its middle, looking forward (yaw 0) or back (π).</summary>
        public int InRoom(int car, double z = 0, double yaw = 0)
        {
            var s = PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player);
            var room = Train.Frames[car].Shape.Interior!.Value;
            s.Position = room.Centre with { Y = room.Min.Y, Z = room.Centre.Z + z };
            s.Surface = Surface.Deck;
            s.Yaw = yaw;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public int OnRoof(int car)
        {
            Crew.Add(PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player));
            Intents.Add(default);
            return Crew.Count;
        }

        public Passenger Aboard(int car, int looks = 1) => World.AddEnemy(id => Passenger.Aboard(id, Train, car, looks));

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                Train.Dynamics.Velocity = 10;
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
    public void WithNobodyAloneItGoesAboutItsLoopCarToCarAndHurtsNobody()
    {
        var night = new Night();
        var rooms = night.Rooms;
        Assert.True(rooms.Count >= 2);
        // Two together in one car, the rest out on the roofs: nobody alone in a room.
        night.InRoom(rooms[0], -1);
        night.InRoom(rooms[0], 1);
        night.OnRoof(rooms[^1]);
        var p = night.Aboard(rooms[^1]);
        var visited = new HashSet<int>();
        var ends = new List<double>();
        night.Run(240, () =>
        {
            visited.Add(p.Attached);
            if (ends.Count == 0 || Math.Abs(ends[^1] - p.Local.Z) > 1)
                ends.Add(p.Local.Z);
        });
        Assert.False(p.Gone);
        Assert.Equal(SpinePhase.Telegraph, p.Phase);
        Assert.True(visited.Count >= 2, $"{visited.Count} cars");
        Assert.All(night.Crew, c => Assert.Equal(Tuning.Player.Health, c.Health));
        Assert.DoesNotContain(night.Events, e => e.EnemyId == p.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void SomeoneAloneInACarIsTakenAndItWearsTheirFaceAfter()
    {
        var night = new Night();
        var rooms = night.Rooms;
        night.OnRoof(rooms[0]);
        night.OnRoof(rooms[0]);
        int alone = night.InRoom(rooms[0]);
        var p = night.Aboard(rooms[^1], looks: 1);
        // Keeping up the act at first, whoever's alone.
        night.Run(P.RestSeconds - 1);
        Assert.Equal(Tuning.Player.Health, night.Crew[alone - 1].Health);
        night.Run(60);
        Assert.False(night.Crew[alone - 1].Alive);
        Assert.Equal(DeathCause.Replaced, night.Crew[alone - 1].Death);
        // It's them now, and it carries on.
        Assert.Equal(alone, p.Looks);
        Assert.False(p.Gone);
        var commit = Assert.Single(night.Events, e => e.EnemyId == p.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void SomeoneComingInBeforeItStrikesSavesThem()
    {
        var night = new Night();
        var rooms = night.Rooms;
        int alone = night.InRoom(rooms[0]);
        int friend = night.OnRoof(rooms[0]);
        var p = night.Aboard(rooms[0], looks: friend);
        night.Run(P.RestSeconds + 1);
        // In with them now: moments from it. Their friend comes in off the roof.
        var room = night.Train.Frames[rooms[0]].Shape.Interior!.Value;
        night.Crew[friend - 1] = night.Crew[friend - 1] with { Position = room.Centre with { Y = room.Min.Y, Z = room.Max.Z - 1 }, Surface = Surface.Deck };
        night.Run(60);
        Assert.True(night.Crew[alone - 1].Alive);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == p.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void FacedAndCalledOutItRuns()
    {
        var night = new Night();
        var rooms = night.Rooms;
        var p = night.Aboard(rooms[0]);
        // Behind it (+Z), looking the other way: Use does nothing.
        int caller = night.InRoom(rooms[0], z: 2, yaw: Math.PI);
        night.Intents[caller - 1] = new PlayerIntent { Buttons = PlayerButtons.Use };
        night.Run(0.5);
        Assert.False(p.Gone);
        // Turned to face it: called out.
        night.Intents[caller - 1] = default;
        night.Run(0.1);
        night.Crew[caller - 1] = night.Crew[caller - 1] with { Yaw = 0 };
        night.Intents[caller - 1] = new PlayerIntent { Buttons = PlayerButtons.Use };
        night.Run(0.2);
        Assert.True(p.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == p.Id && e.To == SpinePhase.BreakOff);
        Assert.True(night.Crew[caller - 1].Alive);
    }

    [Fact]
    public void ABotAloneWithItCallsItOutBeforeItStrikes()
    {
        var night = new Night();
        var rooms = night.Rooms;
        // Alone in the car, looking the other way; it comes in to them.
        int bot = night.InRoom(rooms[0], z: -1, yaw: 0);
        var p = night.Aboard(rooms[0]);
        night.Run(P.RestSeconds + P.StalkSeconds + 10, () =>
        {
            var intent = Bots.Heed.Passengers(default, night.Crew[bot - 1], night.World);
            night.Intents[bot - 1] = intent;
            // The look, as the motor turns it.
            night.Crew[bot - 1] = night.Crew[bot - 1] with { Yaw = night.Crew[bot - 1].Yaw + intent.LookYaw, Pitch = night.Crew[bot - 1].Pitch + intent.LookPitch };
        });
        Assert.True(p.Gone);
        Assert.True(night.Crew[bot - 1].Alive);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == p.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void ItHasNoVoiceToSpeakWith()
    {
        // The tell is silence (App. A.7): the voice paths are between players, and it isn't one.
        var night = new Night();
        var p = night.Aboard(night.Rooms[0], looks: 2);
        night.Run(5);
        Assert.Empty(night.World.Voices.Utterance(p.Id, 50));
    }

    static readonly PlayerIntent[] Idle = [default, default, default];

    /// <summary>At a facility stop with three of the crew in the cab: faces to wear.</summary>
    static FacilityTests.Stop Crewed()
    {
        var stop = new FacilityTests.Stop(Run.ModuleKind.Crates);
        for (int i = 0; i < 3; i++)
            stop.Crew.Add(PlayerMotor.SpawnInCab(stop.Train, Tuning.Player));
        return stop;
    }

    static EnemyTuning Only()
    {
        var d = Tuning.Enemies.Director;
        return Tuning.Enemies with
        {
            // Nothing saved for the Gaunt, whose price here is out of reach.
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], SaveFor = [], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "passenger" ? 0.5 : 1e9) },
        };
    }

    [Fact]
    public void TheDirectorPutsItAboardAtAFacilityStopOnTheDeadLinesToACrewOfThree()
    {
        Assert.Equal(5, Tuning.Enemies.Director.Costs["passenger"]);
        var (route, _) = FacilityTests.With(Run.ModuleKind.Crates);
        Route.Route Tier(RouteTier t) => route with { Tier = t };

        var stop = Crewed();
        stop.World.EnableEnemies(Only(), Tier(RouteTier.DeadLines), 1, crew: 3, authority: true);
        stop.Step(30, Idle);
        var p = Assert.IsType<Passenger>(Assert.Single(stop.World.ActiveEnemies, e => e.Kind == EnemyKind.Passenger));
        Assert.Contains(p.Attached, stop.Train.Dynamics.Consist.Vehicles.Skip(1).Select(v => v.Id));
        Assert.Single(stop.World.Director!.Log, l => l.Kind == EnemyKind.Passenger);

        // Not on the Frontier, not to a crew of two.
        var frontier = Crewed();
        frontier.World.EnableEnemies(Only(), Tier(RouteTier.Frontier), 1, crew: 3, authority: true);
        frontier.Step(30, Idle);
        Assert.DoesNotContain(frontier.World.Director!.Log, l => l.Kind == EnemyKind.Passenger);
        var two = Crewed();
        two.World.EnableEnemies(Only(), Tier(RouteTier.DeadLines), 1, crew: 2, authority: true);
        two.Step(30, Idle);
        Assert.DoesNotContain(two.World.Director!.Log, l => l.Kind == EnemyKind.Passenger);

        // Nor out on the line between stops.
        var night = new Night();
        night.World.EnableEnemies(Only(), Tier(RouteTier.DeadLines), 1, crew: 3, authority: true);
        night.Run(30);
        Assert.DoesNotContain(night.World.Director!.Log, l => l.Kind == EnemyKind.Passenger);
    }

    [Fact]
    public void AClientSeesWhoItLooksLikeAndWhereItIs()
    {
        var night = new Night();
        var p = night.Aboard(night.Rooms[1], looks: 3);
        night.Run(3);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Passenger>(Assert.Single(client.ActiveEnemies));
        Assert.Equal(3, seen.Looks);
        Assert.Equal(p.Attached, seen.Attached);
        Assert.Equal(p.Local.Z, seen.Local.Z, 1);
    }
}
