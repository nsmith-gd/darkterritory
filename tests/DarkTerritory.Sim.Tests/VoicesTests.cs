using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Voices as tools (the director's calls of 14 Oct 2026, friendslop.md queue #454; ARCHITECTURE §8 note 720): the speaking
/// tube from the cab to the guard van, singing together (which never feeds the Choir, settles the livestock and talks the
/// Gaunt down faster), and lamp signals the bot driver obeys.
/// </summary>
public class VoicesTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly VoicesTuning V = Tuning.Player.Voices;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine LongTrain(int cars = 20) => new(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), TestLoop, 1500);

    // ---- the speaking tube

    [Fact]
    public void TheTubeRunsFromTheCabToTheGuardVanHoweverLongTheTrain()
    {
        foreach (int cars in new[] { 3, 20 })
        {
            var train = LongTrain(cars);
            var ends = SpeakingTube.Ends(train);
            Assert.Equal(2, ends.Count);
            Assert.Equal(0, ends[0].Car);
            Assert.Equal(VehicleKind.Guard, train.Vehicles[ends[1].Car].Kind);
            Assert.Equal(train.Dynamics.Consist.Vehicles[^1].Id, ends[1].Car);
            // In the cab, and in the van's room.
            Assert.True(train.Frames[0].Shape.Cab!.Value.Contains(ends[0].Mouth));
            Assert.True(train.Frames[ends[1].Car].Shape.Interior!.Value.Contains(ends[1].Mouth));
        }
    }

    [Fact]
    public void SaidIntoOneMouthpieceItsHeardAtTheOtherAndNotOtherwise()
    {
        var train = LongTrain();
        var ends = SpeakingTube.Ends(train);
        var cab = SpeakingTube.AtMouth(train, 0, P);
        var van = SpeakingTube.AtMouth(train, 1, P, off: 1.2, along: 2.5);
        // Cab to van: out of the van's horn, which the one in the van is near; 300 m from the cab, so not in the air.
        var heard = SpeakingTube.Heard(cab, train, V.Tube);
        Assert.NotNull(heard);
        Assert.Equal(1, heard.Value.End);
        Assert.True(VoiceRouting.HearsTube(van, heard.Value.At, train, V.Tube));
        Assert.Equal(VoicePath.None, VoiceRouting.Route(cab, van, radio: false, train));
        // And back the other way.
        Assert.Equal(0, SpeakingTube.Heard(van with { Position = van.Position with { X = ends[1].Mouth.X - 0.4, Z = ends[1].Mouth.Z } }, train, V.Tube)?.End);
        // Not at the mouthpiece: across the cab, up on its roof over it, or dead there.
        Assert.Null(SpeakingTube.Heard(PlayerMotor.SpawnInCab(train, P, 0.9), train, V.Tube));
        Assert.Null(SpeakingTube.Heard(cab with { Position = cab.Position with { Y = train.Frames[0].Shape.Bounds.Max.Y } }, train, V.Tube));
        Assert.Null(SpeakingTube.Heard(cab with { Health = 0, Death = DeathCause.Mauled }, train, V.Tube));
        // Too far from the far horn to hear it: on the van's roof at its far end, or ten cars up the train.
        Assert.False(VoiceRouting.HearsTube(PlayerMotor.SpawnOnRoof(train, 10, 0, P), heard.Value.At, train, V.Tube));
    }

    [Fact]
    public void FittedTheTubesCarryAVoiceToTheNeighbouringCarsEitherWay()
    {
        // Note 978 (upgrades.md §6's speaking tubes: "Voice carries between neighbouring cars as in one"): a horn in every car
        // with a room, joined to the next car's either way along the train, as well as the cab's to the van's.
        var plain = LongTrain(6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train with { Kit = Tuning.Train.Kit with { SpeakingTubes = true } }, 6, 1)), TestLoop, 1500);
        var ends = SpeakingTube.Ends(train);
        Assert.Equal(2, SpeakingTube.Ends(plain).Count);
        // The cab's and the van's first, as ever; then the cars between, in the train's order, each in its room.
        Assert.Equal(SpeakingTube.Ends(plain), ends.Take(2));
        var between = train.Dynamics.Consist.Vehicles.Where(v => v.Id != 0 && v.Id != ends[1].Car && train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).ToList();
        Assert.NotEmpty(between);
        Assert.Equal(between, ends.Skip(2).Select(e => e.Car));
        Assert.All(ends.Skip(2), e => Assert.True(train.Frames[e.Car].Shape.Interior!.Value.Contains(e.Mouth)));
        // Said into a car's horn, it's out of the car ahead's and the car behind's, and heard by someone at each.
        int middle = 2 + between.Count / 2;
        var heard = SpeakingTube.HeardAll(SpeakingTube.AtMouth(train, middle, P), train, V.Tube);
        Assert.Equal(2, heard.Count);
        int at = between.IndexOf(ends[middle].Car);
        int ahead = at == 0 ? 0 : 2 + at - 1, behind = at == between.Count - 1 ? 1 : 2 + at + 1;
        Assert.Equal(new[] { ahead, behind }.Order(), heard.Select(h => h.End).Order());
        Assert.All(heard, h => Assert.True(VoiceRouting.HearsTube(SpeakingTube.AtMouth(train, h.End, P, off: 1.2), h.At, train, V.Tube)));
        // The cab's still reaches the van down the old pipe, and the first car beside it now too.
        Assert.Equal(new[] { 1, 2 }, SpeakingTube.HeardAll(SpeakingTube.AtMouth(train, 0, P), train, V.Tube).Select(h => h.End).Order());
    }

    [Fact]
    public void AVanCutOffTakesItsEndOfTheTubeWithIt()
    {
        var train = LongTrain(6);
        var van = train.Dynamics.Consist.Vehicles[^1].Id;
        train.Uncouple(train.Dynamics.Consist.Vehicles.Count - 2);
        Assert.DoesNotContain(train.Dynamics.Consist.Vehicles, v => v.Id == van);
        Assert.Empty(SpeakingTube.Ends(train));
        Assert.Null(SpeakingTube.Heard(SpeakingTube.AtMouth(LongTrain(6), 0, P), train, V.Tube));
    }

    [Fact]
    public void TheHostSendsTheTubesVoiceToWhoeverIsByTheFarHornAndSaysWhichEnd()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), LongTrain(), Tuning.Train, P);
        var talker = new ClientSession(net.CreateClient(), LongTrain(), Tuning.Train, P);
        var inVan = new ClientSession(net.CreateClient(), LongTrain(), Tuning.Train, P);
        var onRoof = new ClientSession(net.CreateClient(), LongTrain(), Tuning.Train, P);
        void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                talker.Step(default);
                inVan.Step(default);
                onRoof.Step(default);
            }
        }
        Step(30);
        host.SetPlayerState(talker.PlayerId!.Value, SpeakingTube.AtMouth(host.Train, 0, P));
        host.SetPlayerState(inVan.PlayerId!.Value, SpeakingTube.AtMouth(host.Train, 1, P, off: 1.2, along: 2.5));
        host.SetPlayerState(onRoof.PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, 10, 0, P));
        Step(5);
        (VoicePath, int) Said()
        {
            inVan.VoiceFrames.Clear();
            onRoof.VoiceFrames.Clear();
            talker.SendVoice(1, radio: false, new byte[] { 1, 2, 3 });
            Step(3);
            Assert.Empty(onRoof.VoiceFrames);
            var f = Assert.Single(inVan.VoiceFrames);
            return (f.Path, f.Tube);
        }
        Assert.Equal((VoicePath.Tube, 1), Said());
        // Stepped back from the mouthpiece, nothing reaches the van.
        host.SetPlayerState(talker.PlayerId!.Value, PlayerMotor.SpawnInCab(host.Train, P, 0.9));
        Step(2);
        inVan.VoiceFrames.Clear();
        talker.SendVoice(2, radio: false, new byte[] { 1, 2, 3 });
        Step(3);
        Assert.Empty(inVan.VoiceFrames);
    }

    // ---- singing together

    /// <summary>A voice rising and falling on a beat of <paramref name="beat"/> seconds, <paramref name="phase"/> of a beat in.</summary>
    static byte Sung(int tick, double beat = 1.3, double phase = 0) =>
        (byte)Math.Clamp(130 + 90 * Math.Sin(2 * Math.PI * (tick * SimConstants.TickSeconds / beat + phase)), 0, 255);

    static byte[] Envelope(Func<int, byte> voice) => [.. Enumerable.Range(0, (int)(V.Singing.WindowSeconds * SimConstants.TickRate)).Select(voice)];

    [Fact]
    public void TwoVoicesInTimeAreSingingAndOutOfTimeOrAloneTheyArent()
    {
        var s = V.Singing;
        Assert.True(Singing.InTime(Envelope(t => Sung(t)), Envelope(t => Sung(t)), s));
        // One a little louder and a shade behind is still in time.
        Assert.True(Singing.InTime(Envelope(t => Sung(t)), Envelope(t => (byte)Math.Min(255, Sung(t, phase: -0.06) + 20)), s));
        // Against each other (talking over each other, turn and turn about).
        Assert.False(Singing.InTime(Envelope(t => Sung(t)), Envelope(t => Sung(t, phase: 0.5)), s));
        // To another tune.
        Assert.False(Singing.InTime(Envelope(t => Sung(t)), Envelope(t => Sung(t, beat: 0.83)), s));
        // One voice, the other quiet; or a held drone together, which isn't a song.
        Assert.False(Singing.InTime(Envelope(t => Sung(t)), Envelope(_ => 0), s));
        Assert.False(Singing.InTime(Envelope(_ => 150), Envelope(_ => 150), s));
        // Murmured under the floor.
        Assert.False(Singing.InTime(Envelope(t => (byte)(Sung(t) / 8)), Envelope(t => (byte)(Sung(t) / 8)), s));
    }

    [Fact]
    public void ACrewSingsTogetherOnlyWithinEarshotAndAlive()
    {
        var singing = new Singing();
        var near = new Double3(0, 0, 0);
        for (int t = 0; t < 5 * SimConstants.TickRate; t++)
            singing.Step([(1, near, true, Sung(t)), (2, near + new Double3(3, 0, 0), true, Sung(t)), (3, near + new Double3(V.Singing.Earshot + 5, 0, 0), true, Sung(t)),
                (4, near + new Double3(1, 0, 0), true, 0)], V.Singing);
        Assert.Equal(new[] { 1, 2 }, singing.Singers.Order());
        for (int t = 0; t < SimConstants.TickRate; t++)
            singing.Step([(1, near, true, Sung(t)), (2, near + new Double3(3, 0, 0), false, 0)], V.Singing);
        Assert.Empty(singing.Singers);
    }

    static Night Building(Night n)
    {
        n.World.Choir = new ChoirState { Build = 0.5, Loudness = Tuning.Combat.Choir.MaxLoudness };
        return n;
    }

    [Fact]
    public void SingingDoesntFeedTheChoirButTheSameVoicesOutOfTimeDo()
    {
        double Share(double phase)
        {
            var n = new Night(6, speed: 10);
            n.World.Movement = P;
            n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
            n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 3, P);
            int tick = 0;
            PlayerIntent Voice(int id) => new() { Voice = Sung(tick, phase: id == 2 ? phase : 0) };
            // Warmed up over the window (not gathering: nobody's to blame yet), then the Choir gathering.
            for (int i = 0; i < 5 * SimConstants.TickRate; i++, tick++)
                n.Run(SimConstants.TickSeconds, Voice);
            Building(n);
            for (int i = 0; i < 2 * SimConstants.TickRate; i++, tick++)
                n.Run(SimConstants.TickSeconds, Voice);
            return n.World.ChoirShare(1) + n.World.ChoirShare(2);
        }
        // The director: "Singing does not fight the choir."
        Assert.Equal(0, Share(0));
        // Ordinary loud talk, the same voices out of time with each other, feeds it.
        Assert.True(Share(0.5) > 0.5 * Tuning.Combat.Choir.VoicePerPlayer, $"{Share(0.5)}");
    }

    [Fact]
    public void SingingAboardSettlesTheLivestock()
    {
        var n = new Night(6, speed: 10);
        n.World.Movement = P;
        foreach (var v in n.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            v.Cargo = CargoKind.Livestock;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 3, P);
        int tick = 0;
        n.Run(1, _ => new PlayerIntent { Voice = 0 });
        Assert.Equal(Tuning.Combat.Choir.LivestockFloor, n.World.Choir.Floor, 6);
        Assert.Equal(0, n.World.LivestockCalm);
        for (int i = 0; i < 5 * SimConstants.TickRate; i++, tick++)
            n.Run(SimConstants.TickSeconds, _ => new PlayerIntent { Voice = Sung(tick) });
        Assert.Equal(new[] { 1, 2 }, n.World.Singers.Order());
        Assert.True(n.World.SingingAboard);
        Assert.Equal(V.Singing.CalmsLivestock, n.World.LivestockCalm, 6);
        Assert.Equal(Tuning.Combat.Choir.LivestockFloor * (1 - V.Singing.CalmsLivestock), n.World.Choir.Floor, 6);
    }

    /// <summary>How long (s) until a Gaunt at its attack threshold is talked off by two crewmates near it, singing or not.</summary>
    static double TalkedOff(bool inTime)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(10_000)])), 2_000);
        var world = new World(train, Tuning.Combat) { Movement = P };
        var ctx = new EnemyContext { Tuning = Tuning.Enemies, World = world };
        var g = world.AddEnemy(id => Gaunt.Asleep(id, new Double3(0, 0, 1.2), Tuning.Enemies.Gaunt));
        g.Restore(SpinePhase.Commit, 5, Tuning.Enemies.Gaunt.Health, Enemy.Loose, new Double3(0, 0, 1.2), 0, 0, 0, extra: 1, extra2: Tuning.Enemies.Gaunt.AttackAt);
        PlayerState Ground(double x) => new() { Parent = PlayerState.World, Position = new Double3(x, 0, 0), Surface = Surface.Ground, Health = P.Health };
        var a = Ground(0);
        var b = Ground(2);
        for (int tick = 0; tick < 300 * SimConstants.TickRate; tick++)
        {
            // Both talking all the while (over the Gaunt's floor), in time or turn about.
            byte va = (byte)Math.Max(60, (int)Sung(tick)), vb = (byte)Math.Max(60, (int)Sung(tick, phase: inTime ? 0 : 0.5));
            world.Singing.Step([(1, a.Position, true, va), (2, b.Position, true, vb)], V.Singing);
            ctx.Crew.Clear();
            ctx.Crew.Add((new PlayerSnapshot(1, a), new PlayerIntent { Voice = va }));
            ctx.Crew.Add((new PlayerSnapshot(2, b), new PlayerIntent { Voice = vb }));
            world.Tick++;
            g.Step(ctx);
            if (g.Phase is SpinePhase.BreakOff or SpinePhase.Gone)
                return tick * SimConstants.TickSeconds;
        }
        return double.PositiveInfinity;
    }

    [Fact]
    public void ACrewSingingTalksTheGauntDownFaster()
    {
        double talking = TalkedOff(inTime: false), singing = TalkedOff(inTime: true);
        Assert.True(double.IsFinite(talking), "talked to, it leaves");
        // gaunt.singingTalks times as fast, near enough (the window's first seconds are talk until the song's heard).
        Assert.InRange(talking / singing, Tuning.Enemies.Gaunt.SingingTalks * 0.75, Tuning.Enemies.Gaunt.SingingTalks * 1.05);
    }

    // ---- lamp signals

    [Fact]
    public void ALampSignalRidesTheIntentsThirdByteBesideTheMoves()
    {
        foreach (var signal in Enum.GetValues<LampSignal>())
        {
            var w = new NetWriter();
            Messages.WriteInput(w, [new InputFrame(7, new PlayerIntent { MoveZ = 1, Signal = signal, Moves = PlayerMoves.Crouch | PlayerMoves.Push })], 0);
            var r = new NetReader(w.Written);
            r.U8();
            var frames = new List<InputFrame>();
            Messages.ReadInput(ref r, frames, out _);
            Assert.Equal(signal, frames[0].Intent.Signal);
            Assert.Equal(PlayerMoves.Crouch | PlayerMoves.Push, frames[0].Intent.Moves);
        }
    }

    [Fact]
    public void ALampSignalIsSeenByTheCrewForItsLengthAndOnlyWithALampInHand()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), LongTrain(6), Tuning.Train, P);
        host.World.EnableBodies();
        var clients = new[] { new ClientSession(net.CreateClient(), LongTrain(6), Tuning.Train, P), new ClientSession(net.CreateClient(), LongTrain(6), Tuning.Train, P) };
        void Run(double seconds, Func<int, int, PlayerIntent>? intent = null)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(intent?.Invoke(i, t) ?? default);
            }
        }
        Run(1);
        byte signaller = clients[0].PlayerId!.Value;
        // No lamp in the fist: nothing to signal with.
        Run(0.5, (i, t) => new PlayerIntent { Signal = i == 0 && t == 0 ? LampSignal.Stop : LampSignal.None });
        Assert.Empty(host.World.Signals);
        var lamp = host.World.Bodies.SpawnCrate(host.Train, 2, new Double3(0, 1.2, 0), BodyKind.Lamp);
        lamp.Carrier = signaller;
        Run(0.5, (i, t) => new PlayerIntent { Signal = i == 0 && t == 0 ? LampSignal.Back : LampSignal.None });
        var seen = Assert.Single(host.World.Signals);
        Assert.Equal((signaller, LampSignal.Back), (seen.By, seen.Kind));
        Assert.Equal(host.World.Signals, clients[1].World.Signals);
        // Kept for voices.lampSignals.seconds, then gone everywhere.
        Run(V.LampSignals.Seconds);
        Assert.Empty(host.World.Signals);
        Assert.Empty(clients[1].World.Signals);
    }

    [Fact]
    public void ALampSignalMovesNothing()
    {
        PlayerState[] Night(bool signal)
        {
            var net = new LoopbackNetwork();
            var host = new HostSession(net.CreateHost(), LongTrain(6), Tuning.Train, P);
            host.World.EnableBodies();
            var clients = new[] { new ClientSession(net.CreateClient(), LongTrain(6), Tuning.Train, P), new ClientSession(net.CreateClient(), LongTrain(6), Tuning.Train, P) };
            for (int t = 0; t < 3 * SimConstants.TickRate; t++)
            {
                if (t == 30 && clients[0].PlayerId is { } id)
                    host.World.Bodies.SpawnCrate(host.Train, 2, new Double3(0, 1.2, 0), BodyKind.Lamp).Carrier = id;
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(new PlayerIntent { MoveZ = i == 1 ? 1 : 0, Signal = signal && i == 0 && t % 30 == 0 ? LampSignal.Stop : LampSignal.None });
            }
            return [.. host.Players.Select(p => p.State)];
        }
        Assert.Equal(Night(false), Night(true));
    }

    [Fact]
    public void AStopSignalThatStandsTheTrainIsNamedInTheReport()
    {
        var route = Route.RouteGenerator.Generate(Tuning.Route, Route.RouteTier.Frontier, 1);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0.5)), route.Build(), 3000, Tuning.Boiler), Tuning.Combat) { Movement = P };
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        world.Names[3] = "Priya";
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        world.Bodies.SpawnCrate(world.Train, 2, new Double3(0, 1.2, 0), BodyKind.Lamp).Carrier = 3;
        void Tick(double speed, LampSignal signal = LampSignal.None)
        {
            world.Train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.CrewAct(ref s, new PlayerIntent { Signal = signal }, 3);
            world.Step(new TrainControls { Reverser = 1 });
        }
        // Shown to a train that's standing already: nothing to say.
        Tick(0, LampSignal.Stop);
        for (int i = 0; i < 30; i++)
            Tick(0);
        Assert.DoesNotContain(world.Attribution.Log, i => i.Kind == Run.IncidentKind.Signalled);
        // Shown to a moving train, which stands a few seconds later.
        Tick(8, LampSignal.Stop);
        for (int i = 0; i < 90; i++)
            Tick(8 - i * 0.1);
        for (int i = 0; i < 5; i++)
            Tick(0);
        var line = Assert.Single(Run.IncidentLog.Lines(world, 0, 0, _ => false), l => l.Kind == Run.IncidentKind.Signalled);
        Assert.StartsWith("Stopped by a lamp signal", line.Text);
        Assert.EndsWith("Signalled by Priya.", line.Text);
    }

    /// <summary>The bot driver and a crewmate signalling from the ballast beside the cab, the train running at <paramref name="speed"/>.</summary>
    sealed class Signalled
    {
        public readonly TrainOnLine Train;
        public readonly World World;
        public readonly ConductorBot Bot = new(new CrewCalls(), 0);
        public TrainControls Controls = new() { Reverser = 1 };
        PlayerState _driver;
        public PlayerState Signaller;
        public double Slowest = double.MaxValue;

        public Signalled(double speed)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(80_000)]));
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 5_000, Tuning.Boiler);
            Train.Dynamics.Velocity = speed;
            World = new World(Train, Tuning.Combat) { Movement = P };
            _driver = PlayerMotor.SpawnInCab(Train, P);
            Signaller = PlayerMotor.SpawnOnRoof(Train, 1, 0, P);
        }

        public void Signal(LampSignal kind) => World.Signals.Add(new SignalEvent(World.Signals.Count + 100 + (int)World.Tick, World.Tick, 5, kind));

        public void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                // (Not among those it stops for left behind: only who it can see signalling.)
                Bot.Crewmates = [];
                Bot.Crew = [(5, Signaller)];
                var intent = Bot.Decide(_driver, World, World.Tick, out _);
                if (CabControls.CanDrive(_driver, Train) && CabControls.Clears(Controls, Train, CabControls.ReleasesBrake(intent, _driver, Train)))
                    Controls.Brake = 0;
                CabControls.Apply(ref Controls, intent, _driver, Train);
                World.BeginTick();
                World.CrewAct(ref _driver, intent, 1);
                World.Step(Controls);
                PlayerMotor.Step(ref _driver, intent, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                Slowest = Math.Min(Slowest, Train.Dynamics.Velocity);
            }
        }
    }

    [Fact]
    public void TheDriverStopsOnALampSignalAndGoesOnAgainOnGo()
    {
        var n = new Signalled(10);
        n.Run(5);
        Assert.True(n.Train.Dynamics.Speed > 5, "running");
        n.Signal(LampSignal.Stop);
        n.Run(45);
        Assert.Equal(LampSignal.Stop, n.Bot.Obeying);
        Assert.True(n.Train.Dynamics.Speed < 0.1, $"stood at {n.Train.Dynamics.Speed:0.0} m/s");
        // Still standing a while after the lamp's put down.
        n.Run(20);
        Assert.True(n.Train.Dynamics.Speed < 0.1);
        n.Signal(LampSignal.Go);
        n.Run(40);
        Assert.Equal(LampSignal.None, n.Bot.Obeying);
        Assert.True(n.Train.Dynamics.Velocity > 2, $"under way again at {n.Train.Dynamics.Velocity:0.0} m/s");
    }

    [Fact]
    public void TheDriverBacksUpOnALampSignalThenStandsAndGoesForwardOnGo()
    {
        var n = new Signalled(0);
        n.Controls.Brake = 1;
        n.Run(2);
        double from = n.Train.Dynamics.Distance;
        n.Signal(LampSignal.Back);
        n.Run(70);
        Assert.True(n.Slowest < -0.5, "it set back");
        Assert.Equal(-1, n.Controls.Reverser);
        Assert.InRange(from - n.Train.Dynamics.Distance, V.LampSignals.BackMetres - 6, V.LampSignals.BackMetres + 6);
        Assert.True(n.Train.Dynamics.Speed < 0.1, "stood after setting back");
        n.Signal(LampSignal.Go);
        n.Run(40);
        Assert.Equal(1, n.Controls.Reverser);
        Assert.True(n.Train.Dynamics.Velocity > 2, $"forward again at {n.Train.Dynamics.Velocity:0.0} m/s");
    }

    [Fact]
    public void ASignalTheDriverCantSeeIsntObeyed()
    {
        var n = new Signalled(10);
        // Out on the ground a long way off the line, the far side of a field.
        n.Signaller = new PlayerState { Parent = PlayerState.World, Position = n.Train.Frames[0].Origin + new Double3(V.LampSignals.SeenWithin + 40, 0, 0), Surface = Surface.Ground, Health = P.Health };
        n.Run(3);
        n.Signal(LampSignal.Stop);
        n.Run(30);
        Assert.Equal(LampSignal.None, n.Bot.Obeying);
        Assert.True(n.Train.Dynamics.Speed > 5, $"ran on at {n.Train.Dynamics.Speed:0.0} m/s");
    }
}
