using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Followers (T62, App. A.3, B.3): on the grounds at a stop, it comes up at someone's back and keeps there, seen by
/// everyone but them; aboard with them, it nests in a dark cargo car. Counter: someone calls it, they stand still where it
/// can be seen, and it lets go; nested, a lamp.
/// </summary>
public class FollowerTests
{
    static readonly FollowerTuning F = Tuning.Enemies.Followers;
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
            World.EnableBodies();
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(quiet, null, 1, crew: 3, authority: true);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>On the ground beside the train, <paramref name="along"/> back from the engine, facing forward.</summary>
        public int OnGround(double along, double side = 3.5, double yaw = 0)
        {
            var at = Line.Sample(Train.Dynamics.Distance - along);
            var right = Double3.Cross(at.Tangent, Double3.Up).Normalized;
            var s = PlayerMotor.SpawnOnGround(at.Position + right * side, Line, Train.Dynamics.Distance - along, Tuning.Player);
            s.Yaw = yaw;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public int InRoom(int car)
        {
            var s = PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player);
            var room = Train.Frames[car].Shape.Interior!.Value;
            s.Position = room.Centre with { Y = room.Min.Y };
            s.Surface = Surface.Deck;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public Follower On(int player) => World.AddEnemy(id => Follower.Behind(id, Train, Crew[player - 1], player, F));

        public Double3 Feet(int player) => PlayerMotor.WorldPosition(Crew[player - 1], Train);

        /// <summary>Steps the world; <paramref name="move"/> lets the ground crew walk (the motor), else they stand as put.</summary>
        public void Run(double seconds, Action? each = null, bool move = false)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                Train.Dynamics.Velocity = 0;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    if (move)
                        PlayerMotor.Step(ref s, Intents[c], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                Events.AddRange(World.EnemyEvents);
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
            }
        }

        public List<int> CargoRooms => Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && Train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).ToList();
    }

    [Fact]
    public void ItComesUpBehindThemAndKeepsToTheirBlindSpotAtTheirPace()
    {
        var night = new Night();
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        Assert.Equal(SpinePhase.Dormant, f.Phase);
        Assert.Equal(F.StalkDistance, (f.Local - night.Feet(carrier)).Length, 1);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        Assert.Equal(SpinePhase.Telegraph, f.Phase);
        // Walking on (forward, −Z), it's still right behind them.
        night.Intents[carrier - 1] = new PlayerIntent { MoveZ = 1 };
        night.Run(4, move: true);
        var to = f.Local - night.Feet(carrier);
        // A tick behind them: it's put where they were as the tick began.
        Assert.InRange((to with { Y = 0 }).Length, F.BlindSpot - 0.05, F.BlindSpot + 0.2);
        Assert.True(to.Z > 1, $"{to}");
        Assert.Equal(Tuning.Player.Health, night.Crew[carrier - 1].Health);
    }

    [Fact]
    public void StoodStillWhereSomeoneCanSeeItItLetsGo()
    {
        var night = new Night();
        int carrier = night.OnGround(40, yaw: 0);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        Assert.Equal(SpinePhase.Telegraph, f.Phase);
        // Standing still with nobody looking: it stays.
        night.Run(5);
        Assert.False(f.Gone);
        // Someone further back, looking up the line at them (forward, yaw 0): it's seen, they're still, and it lets go.
        night.OnGround(52, yaw: 0);
        night.Run(F.HaltSeconds + 0.5);
        Assert.True(f.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.BreakOff);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
    }

    [Fact]
    public void SeenButStillWalkingItStaysOn()
    {
        var night = new Night();
        int carrier = night.OnGround(40, yaw: 0);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        int watcher = night.OnGround(52, yaw: 0);
        night.Intents[carrier - 1] = new PlayerIntent { MoveZ = 1 };
        night.Run(F.HaltSeconds * 2, () =>
        {
            // The watcher keeps pace behind.
            var w = night.Crew[watcher - 1];
            night.Crew[watcher - 1] = w with { Position = night.Feet(carrier) + new Double3(0, 0, 12) };
        }, move: true);
        Assert.False(f.Gone);
    }

    [Fact]
    public void BackAboardItGoesWithThemAndNestsInADarkCargoCar()
    {
        var night = new Night();
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        // Up onto a car.
        int car = night.Train.Dynamics.Consist.Vehicles[2].Id;
        night.Crew[carrier - 1] = PlayerMotor.SpawnOnRoof(night.Train, car, 0, Tuning.Player);
        night.Run(0.2);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        night.Run(F.BoardSeconds + 0.5);
        Assert.True(f.Nested);
        Assert.Contains(f.Attached, night.CargoRooms);
        var commit = Assert.Single(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void StillComingUpWhenTheyGetAboardItsLostThem()
    {
        var night = new Night();
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(2);
        night.Crew[carrier - 1] = PlayerMotor.SpawnOnRoof(night.Train, night.Train.Dynamics.Consist.Vehicles[2].Id, 0, Tuning.Player);
        night.Run(0.2);
        Assert.True(f.Gone);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
    }

    static Follower Nest(Night night, int car)
    {
        var room = night.Train.Frames[car].Shape.Interior!.Value;
        var f = night.World.AddEnemy(id => new Follower(id) { Attached = car, Local = new Double3(room.Min.X + 0.4, room.Min.Y, room.Max.Z - 0.5), Extra = 99 });
        return f;
    }

    [Fact]
    public void NestedItBitesWhoeverStaysInItsCar()
    {
        var night = new Night();
        int car = night.CargoRooms[0];
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        night.Crew[carrier - 1] = PlayerMotor.SpawnOnRoof(night.Train, car, 0, Tuning.Player);
        night.Run(F.BoardSeconds + 0.5);
        Assert.True(f.Nested);
        int inside = night.InRoom(f.Attached);
        night.Run(F.NestBiteSeconds - 0.5);
        Assert.Equal(Tuning.Player.Health, night.Crew[inside - 1].Health);
        night.Run(1);
        Assert.Equal(Tuning.Player.Health - F.NestDamage, night.Crew[inside - 1].Health);
        night.Run(F.NestBiteSeconds * 5);
        Assert.False(night.Crew[inside - 1].Alive);
        Assert.Equal(DeathCause.Nested, night.Crew[inside - 1].Death);
    }

    [Fact]
    public void ALampBroughtInDrivesItOut()
    {
        var night = new Night();
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        night.Crew[carrier - 1] = PlayerMotor.SpawnOnRoof(night.Train, night.CargoRooms[0], 0, Tuning.Player);
        night.Run(F.BoardSeconds + 0.5);
        Assert.True(f.Nested);
        var room = night.Train.Frames[f.Attached].Shape.Interior!.Value;
        night.World.Bodies.SpawnCrate(night.Train, f.Attached, room.Centre with { Y = room.Min.Y + 0.1 }, Physics.BodyKind.Lamp);
        night.Run(0.5);
        Assert.True(f.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.BreakOff);
    }

    [Fact]
    public void ItWontNestWhereThereIsALamp()
    {
        var night = new Night();
        // A lamp in every cargo car's room: there's nowhere dark.
        foreach (int car in night.CargoRooms)
        {
            var room = night.Train.Frames[car].Shape.Interior!.Value;
            night.World.Bodies.SpawnCrate(night.Train, car, room.Centre with { Y = room.Min.Y + 0.1 }, Physics.BodyKind.Lamp);
        }
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(F.StalkDistance / F.StalkSpeed + 1);
        night.Crew[carrier - 1] = PlayerMotor.SpawnOnRoof(night.Train, night.CargoRooms[0], 0, Tuning.Player);
        night.Run(F.BoardSeconds + 0.5);
        Assert.True(f.Gone);
    }

    [Fact]
    public void BotsCallItAndTheOneItsOnStandsStill()
    {
        var night = new Night();
        int carrier = night.OnGround(40, yaw: 0);
        int watcher = night.OnGround(44, side: -3.5, yaw: Math.PI); // facing away, back down the line
        var f = night.On(carrier);
        var calls = new CrewCalls();
        night.Run(F.StalkDistance / F.StalkSpeed + 10, () =>
        {
            for (int id = 1; id <= 2; id++)
            {
                var want = id == carrier ? new PlayerIntent { MoveZ = 0.5f } : default;
                var intent = Heed.Followers(want, night.Crew[id - 1], night.World, id, calls, night.World.Tick);
                // The motor walks them and turns their look (the watcher stops to watch).
                night.Intents[id - 1] = intent;
            }
        }, move: true);
        Assert.True(f.Gone);
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.BreakOff);
    }

    [Fact]
    public void TheDirectorSendsOneOntoSomeoneOnTheGroundAtAStop()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], SaveFor = [], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "followers" ? 0.5 : 1e9) },
        };
        Assert.Equal(3, d.Costs["followers"]);
        // Everyone aboard: nothing.
        var aboard = new FacilityTests.Stop(Run.ModuleKind.Crates);
        aboard.World.EnableEnemies(only, null, 1, crew: 2, authority: true);
        aboard.Crew.Add(PlayerMotor.SpawnInCab(aboard.Train, Tuning.Player));
        aboard.Step(10, [default]);
        Assert.DoesNotContain(aboard.World.Director!.Log, l => l.Kind == EnemyKind.Follower);

        // Someone down on the grounds: one comes for them.
        var stop = new FacilityTests.Stop(Run.ModuleKind.Crates);
        stop.World.EnableEnemies(only, null, 1, crew: 2, authority: true);
        stop.Crew.Add(PlayerMotor.SpawnInCab(stop.Train, Tuning.Player));
        var near = stop.Train.Line.Sample(stop.Train.Dynamics.Path, stop.Train.Dynamics.Distance - 20);
        stop.Crew.Add(PlayerMotor.SpawnOnGround(near.Position + Double3.Cross(near.Tangent, Double3.Up).Normalized * 3.5, stop.Train.Line, stop.Train.Dynamics.Distance - 20, Tuning.Player));
        stop.Step(10, [default, default]);
        var f = Assert.IsType<Follower>(stop.World.ActiveEnemies.First(e => e.Kind == EnemyKind.Follower));
        Assert.Equal(2, f.Carrier);
    }

    [Fact]
    public void AClientSeesItWhereItStandsOnTheGround()
    {
        var night = new Night();
        int carrier = night.OnGround(40);
        var f = night.On(carrier);
        night.Run(3);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Follower>(Assert.Single(client.ActiveEnemies));
        Assert.Equal(Enemy.Loose, seen.Attached);
        Assert.Equal(carrier, seen.Carrier);
        Assert.True((seen.WorldPosition(client.Train) - f.WorldPosition(night.Train)).Length < 0.01);
    }
}
