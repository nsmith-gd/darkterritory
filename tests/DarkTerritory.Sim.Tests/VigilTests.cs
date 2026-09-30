using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Spec C.2: the Vigil, over the real netcode (host and clients on the loopback).</summary>
public class VigilTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
    static readonly PlayerIntent Vent = new() { Buttons = PlayerButtons.Use };

    sealed class Crew(LoopbackNetwork net, HostSession host, List<ClientSession> clients)
    {
        public readonly HostSession Host = host;
        public readonly List<ClientSession> Clients = clients;
        public readonly PlayerIntent[] Intents = new PlayerIntent[clients.Count];
        public World World => Host.World;
        public Vigil Vigil => Host.World.Vigil!;
        public byte Id(int i) => Clients[i].PlayerId!.Value;
        public PlayerState State(int i) => Host.Players.First(p => p.Id == Id(i)).State;

        public void Run(double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                Host.Step();
                for (int i = 0; i < Clients.Count; i++)
                    Clients[i].Step(Intents[i]);
            }
        }
    }

    static Crew Session(VigilTuning? vigil = null)
    {
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line, 1500, Tuning.Boiler);
        var host = new HostSession(net.CreateHost(), Train(), T, P, Tuning.Combat);
        host.World.EnableBodies();
        host.World.EnableVigil(vigil ?? Tuning.Vigil);
        var clients = Enumerable.Range(0, 2).Select(_ =>
        {
            var c = new ClientSession(net.CreateClient(), Train(), T, P, Tuning.Combat);
            c.World.EnableVigil(vigil ?? Tuning.Vigil);
            return c;
        }).ToList();
        var crew = new Crew(net, host, clients);
        crew.Run(0.5);
        return crew;
    }

    /// <summary>Client 1 dies where it stands; client 0 goes to the vent valve in the cab.</summary>
    static void DieAndStandAtTheVent(Crew crew, PlayerState where)
    {
        crew.Host.SetPlayerState(crew.Id(1), where with { Health = 0, Death = DeathCause.Mauled });
        var train = crew.Host.Train;
        var atVent = PlayerMotor.SpawnInCab(train, P);
        var valve = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Vent).Position;
        atVent.Position = valve with { Y = atVent.Position.Y, Z = valve.Z + 0.4 };
        crew.Host.SetPlayerState(crew.Id(0), atVent);
        crew.Run(1);
    }

    static PlayerState InTheCab(TrainOnLine train) => PlayerMotor.SpawnInCab(train, P, localX: 0.4);

    [Fact]
    public void ABodyLaidInTheEngineComesBackAfterNinetySecondsCold()
    {
        var crew = Session();
        var train = crew.Host.Train;
        DieAndStandAtTheVent(crew, InTheCab(train));
        var body = Assert.Single(crew.World.Bodies.All);
        Assert.Equal(0, body.Parent);
        Assert.Equal(crew.Id(1), body.Owner);

        // Holding the vent begins it: the first Vigil of the run is 90 s (spec C.2).
        crew.Intents[0] = Vent;
        crew.Run(2);
        Assert.True(crew.Vigil.Active);
        Assert.InRange(crew.Vigil.Left, 89, 90);
        crew.Intents[0] = default;
        Assert.All(crew.Clients, c => Assert.True(c.World.EmergencyLights));

        // During: the boiler vents to nothing, the Choir is at its maximum, and the regulator does nothing.
        crew.Intents[0] = new PlayerIntent { ThrottleNotch = 4 };
        crew.Run(20);
        Assert.Equal(0, train.Boiler.Pressure);
        Assert.Equal(Tuning.Combat.Choir.MaxLoudness, crew.World.Choir.Loudness, 2);
        Assert.Equal(0, train.Dynamics.Speed);
        Assert.False(crew.World.LampShining);
        Assert.False(crew.State(1).Alive);

        crew.Intents[0] = new PlayerIntent { ThrottleNotch = -4 };
        crew.Run(70);
        var back = crew.State(1);
        Assert.True(back.Alive);
        Assert.True(PlayerMotor.InCab(back, train));
        Assert.True(back.Has(PlayerFlags.Revived));
        Assert.Empty(crew.World.Bodies.All);
        Assert.False(crew.Vigil.Active);
        Assert.Equal(1, crew.Vigil.Revivals);
        Assert.Equal([VigilOutcome.Began, VigilOutcome.Revived], crew.Host.VigilEvents.Select(e => e.Outcome));
        // The revived player's own machine agrees, and the next Vigil would take 120 s.
        crew.Run(0.5);
        Assert.True(crew.Clients[1].Predicted.Alive && crew.Clients[1].Predicted.Has(PlayerFlags.Revived));
        Assert.Equal(120, crew.Vigil.NextSeconds);
        // After it: the pressure has to be rebuilt from zero.
        Assert.True(train.Boiler.Pressure < Tuning.Boiler.WorkingBandMin);
    }

    [Fact]
    public void ItNeedsTheBodyInTheEngineAndTheTrainStopped()
    {
        // A body on a car roof won't do.
        var crew = Session();
        DieAndStandAtTheVent(crew, PlayerMotor.SpawnOnRoof(crew.Host.Train, 2, 0, P));
        crew.Intents[0] = Vent;
        crew.Run(3);
        Assert.False(crew.Vigil.Active);

        // Nor will a moving train.
        crew = Session();
        DieAndStandAtTheVent(crew, InTheCab(crew.Host.Train));
        crew.Host.Train.Dynamics.Velocity = 2;
        crew.Clients.ForEach(c => c.Train.Dynamics.Velocity = 2);
        crew.Intents[0] = Vent;
        crew.Run(1);
        Assert.False(crew.Vigil.Active);
    }

    [Fact]
    public void TakingTheBodyOutBreaksItAndThePressureIsGoneAnyway()
    {
        var crew = Session();
        DieAndStandAtTheVent(crew, InTheCab(crew.Host.Train));
        crew.Intents[0] = Vent;
        crew.Run(2);
        crew.Intents[0] = default;
        crew.Run(15);
        Assert.True(crew.Vigil.Active);
        crew.World.Bodies.All[0].Carrier = crew.Id(0);
        crew.Run(0.2);
        Assert.False(crew.Vigil.Active);
        Assert.Equal(VigilOutcome.Broken, crew.Host.VigilEvents[^1].Outcome);
        Assert.False(crew.State(1).Alive);
        Assert.Equal(0, crew.Vigil.Revivals);
        Assert.True(crew.Host.Train.Boiler.Pressure < 5);
    }

    [Fact]
    public void ThereIsNoFourthVigil()
    {
        // Three quick ones (shortened), each longer than the last in the real tuning; then no more.
        var crew = Session(Tuning.Vigil with { Seconds = [1, 1.5, 2] });
        var train = crew.Host.Train;
        for (int revival = 0; revival < 4; revival++)
        {
            DieAndStandAtTheVent(crew, InTheCab(train));
            crew.Intents[0] = Vent;
            crew.Run(2);
            crew.Intents[0] = default;
            crew.Run(3);
            Assert.Equal(revival < 3, crew.State(1).Alive);
        }
        Assert.Equal(3, crew.Vigil.Revivals);
        Assert.False(crew.Vigil.Permitted);
        Assert.Equal([90.0, 120.0, 150.0], Tuning.Vigil.Seconds);
    }

    [Fact]
    public void TheRevivedCarryLightThingsOnly()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), Line, 1500, Tuning.Boiler);
        var world = new World(train);
        world.EnableBodies();
        world.Stock();
        var guard = train.Dynamics.Consist.Vehicles.Last(v => v.Kind == VehicleKind.Guard).Id;
        bool Picks(BodyKind kind, PlayerFlags flags)
        {
            foreach (var b in world.Bodies.All)
                b.Carrier = -1;
            var item = world.Bodies.All.First(b => b.Kind == kind);
            var near = new PlayerState
            {
                Parent = guard,
                Position = item.Pbd.Particles[0].Position with { Y = train.Frames[guard].Shape.Interior!.Value.Min.Y } - new Double3(0, 0, -0.6),
                Surface = Surface.Deck,
                Health = P.Health,
                Flags = flags,
            };
            world.CrewAct(ref near, default, 7);
            world.CrewAct(ref near, new PlayerIntent { Buttons = PlayerButtons.Use }, 7);
            return item.Carrier == 7;
        }
        Assert.True(Picks(BodyKind.Crate, PlayerFlags.None));
        Assert.False(Picks(BodyKind.Crate, PlayerFlags.Revived));
        Assert.True(Picks(BodyKind.Lamp, PlayerFlags.Revived));
    }
}

/// <summary>Spec B.2: cold exposure. 600 s outside to onset, 1200 s to death, reset in 20 s near heat.</summary>
public class ColdTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), Line, 1500, Tuning.Boiler);

    static void Wait(ref PlayerState s, TrainOnLine train, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, default, train, P, T, SimConstants.TickSeconds);
    }

    [Fact]
    public void TheNumbersAreTheSpecs()
    {
        Assert.Equal(600, P.Cold.OnsetSeconds);
        Assert.Equal(1200, P.Cold.DeathSeconds);
        Assert.Equal(20, P.Cold.RecoverSecondsNearHeat);
        Assert.Equal(0.25, P.Cold.IndoorsRate);
        Assert.Equal(0.5, P.Cold.RevivedOnsetScale);
    }

    [Fact]
    public void OutsideTooLongKills()
    {
        var train = Train();
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Wait(ref s, train, 599);
        Assert.False(PlayerMotor.Chilled(s, P));
        Wait(ref s, train, 2);
        Assert.True(PlayerMotor.Chilled(s, P));
        Wait(ref s, train, 598);
        Assert.True(s.Alive);
        Wait(ref s, train, 2);
        Assert.Equal(DeathCause.Cold, s.Death);
    }

    [Fact]
    public void ChilledIsSlower()
    {
        var train = Train();
        double Walked(double cold)
        {
            var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
            s.Cold = cold;
            var start = s.Position;
            for (int i = 0; i < SimConstants.TickRate; i++)
                PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, T, SimConstants.TickSeconds);
            return (s.Position - start).Length;
        }
        Assert.Equal(Walked(0) * P.Cold.OnsetSpeedScale, Walked(P.Cold.OnsetSeconds + 1), 3);
    }

    [Fact]
    public void TheCabWarmsYouBackWithinTwentySeconds()
    {
        var train = Train();
        var s = PlayerMotor.SpawnInCab(train, P);
        s.Cold = P.Cold.DeathSeconds - 1;
        Wait(ref s, train, 20);
        Assert.Equal(0, s.Cold);
    }

    [Fact]
    public void AShutCarIsWarmOnlyWhileTheBoilerHeatsIt()
    {
        var train = Train();
        var car = train.Dynamics.Consist.Vehicles.Last(v => v.Kind == VehicleKind.Guard).Id;
        var room = train.Frames[car].Shape.Interior!.Value;
        var s = new PlayerState { Parent = car, Position = new Double3(0, room.Min.Y, 0), Surface = Surface.Deck, Health = P.Health, Cold = 100 };
        Assert.Equal(car, PlayerMotor.Space(s, train));
        Assert.True(PlayerMotor.NearHeat(s, train));
        // The Vigil's vent leaves the cars cold.
        train.Boiler.Pressure = 0;
        Assert.False(PlayerMotor.NearHeat(s, train));
    }

    [Fact]
    public void TheRevivedGetColdSooner()
    {
        var s = new PlayerState { Health = P.Health, Cold = P.Cold.OnsetSeconds * P.Cold.RevivedOnsetScale + 1, Flags = PlayerFlags.Revived };
        Assert.True(PlayerMotor.Chilled(s, P));
        Assert.False(PlayerMotor.Chilled(s with { Flags = PlayerFlags.None }, P));
    }
}
