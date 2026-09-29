using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Soot Children (T40, App. A.4, B.6): calling from outside in the voice of someone who isn't there.</summary>
public class SootChildrenTests
{
    static readonly SootChildrenTuning S = Tuning.Enemies.SootChildren;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));

    sealed class Night
    {
        public readonly World World;
        public readonly int Guard;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<DamageEvent> Damage = new();
        public readonly List<(int Enemy, int Voice)> Calls = new();

        /// <summary>A standing train: player 1 shut in the guard van, player 2 up on car 1's roof, and player 2 has been talking.</summary>
        public Night()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 1_000);
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
            Guard = train.Vehicles.Count - 1;
            var room = train.Frames[Guard].Shape.Interior!.Value;
            Crew[1] = new PlayerState { Parent = Guard, Position = new Double3(0, room.Min.Y + 0.1, room.Centre.Z), Surface = Surface.Deck, Health = Tuning.Player.Health };
            Crew[2] = PlayerMotor.SpawnOnRoof(train, 1, 0, Tuning.Player);
            for (int i = 0; i < 40; i++)
                World.Voices.Hear(2, new byte[] { (byte)i, 7, 7 }, 0);
            Run(1 / 30.0);
        }

        public TrainOnLine Train => World.Train;

        public SootChildren Send(int voice = 2) => World.AddEnemy(id => SootChildren.At(id, Train, Guard, voice, S));

        public void Run(double seconds)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                World.BeginTick();
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, default, id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = 1 });
                Damage.AddRange(World.Damage);
                Calls.AddRange(World.Calls);
            }
        }
    }

    [Fact]
    public void ItCallsInTheVoiceOfSomeoneWhoIsNotThere()
    {
        var night = new Night();
        // The car with someone shut inside, and the voice of the one who isn't in it.
        Assert.Equal((night.Guard, 2), SootChildren.Choose(night.World, S, [.. night.Crew.Select(c => (c.Key, c.Value))]));
        var soot = night.Send();
        night.Run(1);
        Assert.Equal(SpinePhase.Telegraph, soot.Phase);
        Assert.Contains((soot.Id, 2), night.Calls);
        // It's out in the dark off the guard van's side.
        var guard = night.Train.Frames[night.Guard];
        double off = (soot.WorldPosition(night.Train) - guard.ToWorld(Double3.Zero)).Length;
        Assert.InRange(off, S.StandOff, S.StandOff + guard.Shape.HalfWidth + 0.5);
    }

    [Fact]
    public void ItNeedsAVoiceToStealAndSomeoneToFool()
    {
        var night = new Night();
        var crew = night.Crew.Select(c => (c.Key, c.Value)).ToList();
        // App. B.6: never for a solo player.
        Assert.Null(SootChildren.Choose(night.World, S, [crew[0]]));
        // Nobody's said anything lately: nothing to steal.
        night.World.Voices.Forget(2);
        Assert.Null(SootChildren.Choose(night.World, S, crew));
        // Only the one shut in has spoken: they'd know their own voice isn't outside.
        night.World.Voices.Hear(1, new byte[] { 1 }, night.World.Tick);
        Assert.Null(SootChildren.Choose(night.World, S, crew));
    }

    [Fact]
    public void OpenTheDoorToItAndItTakesYou()
    {
        var night = new Night();
        var soot = night.Send();
        // Opened at once there's no window yet (App. A.1): it can't take anyone for it.
        night.Train.Vehicles[night.Guard].ToggleDoor(0);
        night.Run(0.2);
        Assert.Empty(night.Damage);
        night.Train.Vehicles[night.Guard].ToggleDoor(0);
        night.Run(Tuning.Enemies.MinReactionSeconds + 1);
        // The one inside goes to the door for the friend calling, and opens it.
        var door = night.Train.Frames[night.Guard].Shape.Interactables.First(i => i.Kind == InteractableKind.Door && i.Index == 0);
        night.Crew[1] = night.Crew[1] with { Position = door.Position with { Z = door.Position.Z - Math.Sign(door.Position.Z) * 0.6 } };
        night.Train.Vehicles[night.Guard].ToggleDoor(0);
        night.Run(0.2);
        var taken = Assert.Single(night.Damage);
        Assert.Equal((1, DeathCause.Taken), (taken.PlayerId, taken.Cause));
        Assert.True(soot.Gone || soot.Phase == SpinePhase.Punish);
    }

    [Fact]
    public void GoOutToItAndItTakesYou()
    {
        var night = new Night();
        var soot = night.Send();
        night.Run(Tuning.Enemies.MinReactionSeconds + 1);
        var at = soot.WorldPosition(night.Train);
        night.Crew[2] = PlayerMotor.SpawnOnGround(at + new Double3(S.LureRadius - 2, 0, 0), night.Train.Line, 1_000, Tuning.Player);
        night.Run(0.2);
        Assert.Equal(2, Assert.Single(night.Damage).PlayerId);
    }

    [Fact]
    public void IgnoredItGivesUpAndMovesOn()
    {
        var night = new Night();
        var soot = night.Send();
        night.Run(S.IgnoredSeconds + 1);
        Assert.True(soot.Gone);
        Assert.Empty(night.Damage);
        // It called the whole time, every so often.
        Assert.Equal((int)Math.Ceiling(S.IgnoredSeconds / S.CallEverySeconds), night.Calls.Count(c => c.Enemy == soot.Id));
    }

    [Fact]
    public void TheHostPlaysTheCallInTheStolenVoiceAtOneLoudness()
    {
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 12, 1)), Line, 1_000);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, Tuning.Player);
        var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
        host.EnableEnemies(quiet, null, 1, 3);
        var talker = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player);
        var near = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player);
        var far = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player);
        void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                talker.Step(default);
                near.Step(default);
                far.Step(default);
            }
        }
        Step(30);
        host.SetPlayerState(near.PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, 6, 0, Tuning.Player));
        host.SetPlayerState(far.PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, 11, 0, Tuning.Player));
        Step(5);
        // The talker says something aloud: twenty frames the host keeps.
        for (ushort i = 1; i <= 20; i++)
        {
            talker.SendVoice(i, radio: false, new byte[] { (byte)i, 42 });
            Step(1);
        }
        near.VoiceFrames.Clear();
        far.VoiceFrames.Clear();
        var soot = host.World.AddEnemy(id => SootChildren.At(id, host.Train, 6, talker.PlayerId!.Value, S));
        Step(30);
        var heard = near.VoiceFrames.Where(f => f.Path.HasFlag(VoicePath.Mimic)).ToList();
        // Every frame of it, in the talker's voice, from the thing, in order and at the voice's own pace (not in a burst).
        Assert.Equal(20, heard.Count);
        Assert.All(heard, f => Assert.Equal((talker.PlayerId, soot.Id), (f.Speaker, f.Source)));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (byte)i), heard.Select(f => f.Opus[0]));
        // Beyond its call radius, nothing.
        Assert.DoesNotContain(far.VoiceFrames, f => f.Path.HasFlag(VoicePath.Mimic));
    }
}
