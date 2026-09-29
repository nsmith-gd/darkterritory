using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Lamplighters (T52, App. A.6, B.6): they come for the lamp. Rule: lamps down.</summary>
public class LamplighterTests
{
    static readonly LamplighterTuning L = Tuning.Enemies.Lamplighters;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public double Speed;

        public Night(double speed, EnemyTuning? enemies = null)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route: null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public int InCab()
        {
            Crew.Add(PlayerMotor.SpawnInCab(Train, Tuning.Player));
            Intents.Add(default);
            return Crew.Count;
        }

        public int OnRoof(int car)
        {
            Crew.Add(PlayerMotor.SpawnOnRoof(Train, car, 0, Tuning.Player));
            Intents.Add(default);
            return Crew.Count;
        }

        public Lamplighter Beside(int side = 1) => World.AddEnemy(id => Lamplighter.Beside(id, Train, side, L));

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

        public double FromLamp(Lamplighter l) => ((l.WorldPosition(Train) - Lamplighter.Lamp(Train)) with { Y = 0 }).Length;
    }

    [Fact]
    public void LampsDownItJustPacesTheTrainOutInTheDark()
    {
        var night = new Night(speed: 10);
        night.World.LampLit = false;
        var l = night.Beside();
        night.Run(30);
        Assert.Equal(SpinePhase.Dormant, l.Phase);
        Assert.False(l.Eyeshine);
        // Beside the engine, out past the lamp, keeping up with it.
        Assert.Equal(L.PaceLateral, l.Lateral, 1);
        Assert.InRange(night.Train.Dynamics.Distance - l.LineDistance, L.PaceBehind - 1, L.PaceBehind + 1);
    }

    [Fact]
    public void ALitLampDrawsItInAndItSmashesTheLampAndGoesForTheCab()
    {
        var night = new Night(speed: 10);
        int driver = night.InCab();
        var l = night.Beside();
        night.Run(SimConstants.TickSeconds);
        Assert.True(l.Eyeshine);
        // It comes in from the dark over seconds: the eyeshine is the warning.
        night.Run(Tuning.Enemies.MinReactionSeconds);
        Assert.True(night.World.LampLit);
        night.Run(8);
        Assert.False(night.World.LampLit);
        Assert.True(night.World.LampOutSeconds > L.RelightSeconds - 9);
        Assert.Equal(Tuning.Player.Health - L.BiteDamage, night.Crew[driver - 1].Health);
        Assert.False(l.Eyeshine);
    }

    [Fact]
    public void PutTheLampOutAsItComesAndItLosesTrack()
    {
        var night = new Night(speed: 10);
        int driver = night.InCab();
        var l = night.Beside();
        night.Run(2);
        Assert.True(l.Eyeshine);
        Assert.True(night.FromLamp(l) > L.StrikeReach);
        night.Intents[driver - 1] = new PlayerIntent { Lamp = LampSwitch.Off };
        night.Run(SimConstants.TickSeconds);
        night.Intents[driver - 1] = default;
        Assert.False(night.World.LampLit);
        Assert.Equal(SpinePhase.BreakOff, l.Phase);
        // It stands out at the lineside where it lost the light, and the train leaves it behind.
        double stood = l.LineDistance;
        night.Run(5);
        Assert.Equal(stood, l.LineDistance, 6);
        night.Run(25);
        Assert.True(l.Gone);
        Assert.Equal(Tuning.Player.Health, night.Crew[driver - 1].Health);
        Assert.Equal(0, night.World.LampOutSeconds);
    }

    [Fact]
    public void OnlyTheCabWorksTheLampAndASmashedOneStaysOutAWhile()
    {
        var night = new Night(speed: 10);
        int roof = night.OnRoof(2);
        int driver = night.InCab();
        night.Intents[roof - 1] = new PlayerIntent { Lamp = LampSwitch.Off };
        night.Run(SimConstants.TickSeconds);
        Assert.True(night.World.LampLit);
        night.World.SmashLamp(L.RelightSeconds);
        night.Intents[roof - 1] = default;
        night.Intents[driver - 1] = new PlayerIntent { Lamp = LampSwitch.On };
        night.Run(L.RelightSeconds - 1);
        Assert.False(night.World.LampLit);
        night.Run(1.1);
        Assert.True(night.World.LampLit);
    }

    [Fact]
    public void ATrainFasterThanItCanRunLeavesItBehind()
    {
        var night = new Night(speed: L.MaxSpeed + 4);
        var l = night.Beside();
        night.Run(60);
        Assert.True(l.Gone);
        Assert.DoesNotContain(l, night.World.ActiveEnemies);
    }

    [Fact]
    public void TheDirectorSendsThemOnlyToALitLamp()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "lamplighters" ? c.Value : 1e9) },
        };
        var night = new Night(speed: 10, only);
        night.World.LampLit = false;
        night.Run(120);
        Assert.DoesNotContain(night.World.ActiveEnemies, e => e.Kind == EnemyKind.Lamplighter);
        night.World.LampLit = true;
        night.Run(120);
        var sent = night.World.Director!.Log.Where(l => l.Kind == EnemyKind.Lamplighter).ToList();
        Assert.NotEmpty(sent);
        Assert.True(night.World.ActiveEnemies.Count(e => e.Kind == EnemyKind.Lamplighter) <= L.MaxActive);
    }

    [Fact]
    public void TheLampSwitchRidesInTheNotchByte()
    {
        foreach (var (notch, lamp) in new (sbyte, LampSwitch)[] { (-4, LampSwitch.Off), (3, LampSwitch.On), (0, LampSwitch.None), (-1, LampSwitch.On) })
        {
            var sent = new PlayerIntent { MoveX = 0.5f, ThrottleNotch = notch, Lamp = lamp, Buttons = PlayerButtons.Brake };
            var w = new NetWriter();
            Messages.WriteInput(w, [new InputFrame(7, sent)], 3);
            var r = new NetReader(w.Written);
            Assert.Equal((byte)MessageType.Input, r.U8());
            var read = new List<InputFrame>();
            Messages.ReadInput(ref r, read, out _);
            var got = read[0].Intent;
            Assert.Equal((notch, lamp, sent.Buttons), (got.ThrottleNotch, got.Lamp, got.Buttons));
        }
    }

    [Fact]
    public void AClientSeesTheEyesAndTheLampOut()
    {
        var night = new Night(speed: 10);
        night.InCab();
        var l = night.Beside();
        night.Run(2);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Lamplighter>(Assert.Single(client.ActiveEnemies));
        Assert.True(seen.Eyeshine);
        Assert.Equal(l.Lateral, seen.Lateral, 2);
        night.Run(8);
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        Assert.False(client.LampLit);
        Assert.Equal(night.World.LampOutSeconds, client.LampOutSeconds, 2);
    }

    [Fact]
    public void TheDriverBotPutsTheLampDownForTheEyesAndUpAgainAfter()
    {
        var night = new Night(speed: 10);
        int driver = night.InCab();
        var bot = new ConductorBot();
        var l = night.Beside();
        bool wentDark = false;
        for (int i = 0; i < 90 * SimConstants.TickRate; i++)
        {
            night.Intents[driver - 1] = bot.Decide(night.Crew[driver - 1], night.World, night.World.Tick, out _) with { ThrottleNotch = 0 };
            night.Run(SimConstants.TickSeconds);
            wentDark |= !night.World.LampLit;
            if (l.Gone)
                break;
        }
        Assert.True(wentDark);
        Assert.Equal(0, night.World.LampOutSeconds);
        Assert.Equal(Tuning.Player.Health, night.Crew[driver - 1].Health);
    }
}
