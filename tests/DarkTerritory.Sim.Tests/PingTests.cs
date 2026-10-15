using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 739 (the director's second playtest, 14 Oct: "I'm going to want to use my middle mouse button to create some sort of
/// context ping for them ... a car on fire, they should understand, okay, go deal with that fire. If I point to the cab
/// controls, then it should understand that the cab controls are in need of being taken over"; GDD §12): the ping is what's
/// in the middle of the view; the crew see it; a bot takes it and does the job.
/// </summary>
public class PingTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Enemies.EnemyTuning Quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(60_000)])), 2_000, Tuning.Boiler);

    /// <summary><paramref name="s"/> looking at <paramref name="at"/> (in the world) from where they stand.</summary>
    static PlayerState Looking(PlayerState s, TrainOnLine train, Double3 at)
    {
        var f = train.Frames[s.Parent];
        var d = f.ToLocal(at) - (s.Position + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight);
        d = d * (1 / d.Length);
        return s with { Yaw = Math.Atan2(-d.X, -d.Z), Pitch = Math.Asin(d.Y) };
    }

    static Double3 Middle(TrainOnLine train, int car) => train.Frames[car].ToWorld(new Double3(0, train.Frames[car].Shape.RoofHeight * 0.5, 0));

    [Fact]
    public void APingIsWhatsInTheMiddleOfTheView()
    {
        var train = Train();
        var world = new World(train);
        world.EnableEnemies(Quiet, null, 1, 3, authority: true);
        world.AddEnemy(id => CarFire.In(id, train, 3, 2, Tuning.Enemies.CarFire));
        var t = P.Pings;
        var onCar2 = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        // At the car on fire behind: the fire, in car 3.
        var fire = Pings.Resolve(world, Looking(onCar2, train, Middle(train, 3)), t);
        Assert.Equal((PingKind.Fire, 3), (fire?.Kind, fire?.Target));
        // Forward at the engine's cab: its controls.
        var cab = Pings.Resolve(world, Looking(onCar2, train, train.Frames[0].ToWorld(train.Frames[0].Shape.Levers!.Value.Regulator)), t);
        Assert.Equal(PingKind.Cab, cab?.Kind);
        // Out at the dark beside the line: nothing a bot can do.
        Assert.Null(Pings.Resolve(world, onCar2 with { Yaw = Math.PI / 2, Pitch = 0 }, t));
        // From in the cab, at the regulator: the controls.
        var inCab = PlayerMotor.SpawnInCab(train, P, 0.5);
        Assert.Equal(PingKind.Cab, Pings.Resolve(world, Looking(inCab, train, train.Frames[0].ToWorld(train.Frames[0].Shape.Levers!.Value.Regulator)), t)?.Kind);
    }

    /// <summary>A host, its bots thinking as the app's do, and someone playing, over a loopback (HumanDriverDiesTests').</summary>
    sealed class Crew
    {
        public readonly HostSession Host;
        public readonly List<(ClientSession Session, IBot Bot)> Bots = [];
        public readonly ClientSession Player;
        public readonly CrewCalls Calls = new();
        readonly Ballast.Net.LoopbackNetwork _net = new();
        uint _t;

        public Crew(int bots)
        {
            Host = new HostSession(_net.CreateHost(), Train(), Tuning.Train, P, Tuning.Combat);
            for (int i = 0; i < bots; i++)
                Bots.Add((new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Bot = true }, BotCrew.Make(i, bots, Calls, Tuning.Combat, P, 1)));
            Player = new ClientSession(_net.CreateClient(), Train(), Tuning.Train, P, Tuning.Combat) { Name = "Priya" };
        }

        public byte PlayerId => Player.PlayerId!.Value;
        public PlayerState Of(byte id) => Host.Players.First(p => p.Id == id).State;

        public void Run(double seconds, Func<PlayerIntent>? hands = null, Func<bool>? until = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                _net.Advance(SimConstants.TickSeconds);
                Host.Step();
                Calls.Advance(_t);
                foreach (var (session, bot) in Bots)
                    session.Step(session.Connected ? BotCrew.Think(session, bot, _t, Calls) : default);
                Player.Step(hands?.Invoke() ?? default);
                _t++;
                if (until?.Invoke() == true)
                    return;
            }
        }

        /// <summary>One press of the ping, looking as they are.</summary>
        public void Ping()
        {
            Run(SimConstants.TickSeconds * 3, () => new PlayerIntent { Moves = PlayerMoves.Ping });
            Run(SimConstants.TickSeconds * 3);
        }
    }

    [Fact]
    public void ACarPingedOnFireIsTakenByAWalkerWhoGoesToIt()
    {
        var c = new Crew(bots: 4);
        c.Host.EnableEnemies(Quiet, null, 1, 5);
        c.Run(2);
        var train = c.Host.Train;
        c.Host.Train.Dynamics.Velocity = -12;
        c.Run(10);
        // Car 2 catches (its gun's car: the gunner's legs there claimed its first hand and sat on at the gun); the one playing
        // looks at it from car 1's roof and pings it.
        c.Host.World.AddEnemy(id => CarFire.In(id, train, 2, 2, Tuning.Enemies.CarFire));
        c.Host.SetPlayerState(c.PlayerId, Looking(PlayerMotor.SpawnOnRoof(train, 1, 0, P), train, Middle(train, 2)));
        c.Run(0.5);
        c.Ping();
        // The crew see it: a fire, in car 2, theirs.
        Assert.Contains(c.Host.World.Pings, p => p.Kind == PingKind.Fire && p.Target == 2 && p.By == c.PlayerId);
        Assert.Contains(c.Player.World.Pings, p => p.Kind == PingKind.Fire && p.Target == 2);
        // A walker takes it (one bot: a second ping would go to another), and is at the car within half a minute.
        int? taker = null;
        c.Run(5, until: () => (taker = c.Bots.Select(b => b.Session.PlayerId).Select(id => (int?)id).FirstOrDefault(id => c.Calls.OrderOf(id!.Value) is { Kind: PingKind.Fire, Target: 2 })) is not null);
        Assert.True(taker is not null, "no bot took the ping");
        Assert.IsType<RoofWalkerBot>(c.Bots.First(b => b.Session.PlayerId == taker).Bot);
        Assert.Single(c.Bots, b => c.Calls.OrderOf(b.Session.PlayerId!.Value) is not null);
        c.Run(30, until: () => c.Of((byte)taker!.Value).Parent == 2);
        Assert.Equal(2, c.Of((byte)taker!.Value).Parent);
        // And it's the fire's first hand: it goes in (the walkers' own way of fighting it, note 634).
        Assert.Equal(1, c.Calls.ClaimFire(2, taker.Value, c.Host.World.Tick));
    }

    [Fact]
    public void TheControlsPingedByTheirDriverAreLetGoAndABotTakesThem()
    {
        var c = new Crew(bots: 2);
        c.Run(2);
        var train = c.Host.Train;
        // The one playing drives off, the driver bot out at its work (note 574).
        c.Host.SetPlayerState(c.PlayerId, PlayerMotor.SpawnInCab(train, P, 0.5));
        int tick = 0;
        c.Run(60, () => ++tick % 20 == 0 ? new PlayerIntent { ThrottleNotch = 1 } : default);
        Assert.Equal(c.PlayerId, c.Host.World.ControlsHolder);
        // They look at the regulator and ping it: the controls are let go, and a bot comes for them.
        c.Host.SetPlayerState(c.PlayerId, Looking(c.Of(c.PlayerId), train, train.Frames[0].ToWorld(train.Frames[0].Shape.Levers!.Value.Regulator)));
        c.Run(0.5);
        c.Ping();
        Assert.Contains(c.Host.World.Pings, p => p.Kind == PingKind.Cab);
        Assert.NotEqual(c.PlayerId, c.Host.World.ControlsHolder);
        c.Run(120, until: () => c.Host.World.ControlsHolder >= 0 && c.Host.World.ControlsHolderBot);
        Assert.True(c.Host.World.ControlsHolderBot, $"no bot took the controls: the holder {c.Host.World.ControlsHolder}");
    }
}
