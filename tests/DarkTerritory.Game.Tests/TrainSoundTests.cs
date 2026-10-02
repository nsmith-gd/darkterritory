using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The train's cues (GameAudio.Train, tools/audio/cues.py "bed-*" and "state-*"): the bed's recordings in place of its
/// synths where they're installed (and the synths where they aren't), the boiler's alarms on their edges, the slack running
/// in and out, and a derailment heard as what its wreck does (Wreck), not one sound.
/// </summary>
public class TrainSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly BoilerTuning Boilers = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly PlayerTuning Players = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    // Each synthesised bed sound and the recorded cues that stand in for it (GameAudio.Train's SampledBy).
    static readonly (string Synth, string[] Cues)[] Bed =
    [
        ("boiler-roar", ["bed-boiler-roar.roar-low", "bed-boiler-roar.roar-high"]),
        ("chuff", ["bed-chuff.chuff", "bed-chuff.chuff-heavy"]),
        ("wheel-rail", ["bed-wheel-rail.roll-slow", "bed-wheel-rail.roll-fast"]),
        ("wind", ["bed-wind.wind-slow", "bed-wind.wind-fast"]),
    ];

    static World Train(int cars = 6, double speed = 15, string line = "test-loop")
    {
        var rail = RailLine.Load(Path.Combine(Content, "lines", line + ".json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, cars, 1)), rail, 1200, Boilers);
        train.Dynamics.Velocity = speed;
        return new World(train);
    }

    static Listener OnRoof(TrainOnLine train, int car)
    {
        var s = PlayerMotor.SpawnOnRoof(train, car, 0, Players);
        var frame = train.Frames[s.Parent];
        return Listener.At(frame.ToWorld(s.Position + Double3.Up * 1.65), frame.Heading);
    }

    /// <summary>Steps the world (holding its speed) and the audio for <paramref name="ticks"/>, rendering as the game does; every voice started.</summary>
    static List<SoundInstance> Run(GameAudio audio, World world, int ticks, double? speed = null, int car = 2, TrainControls? controls = null)
    {
        var started = new List<SoundInstance>();
        var seen = new HashSet<int>();
        var block = new float[Audio.Block * 2];
        var held = controls ?? new TrainControls { Reverser = 1 };
        for (int i = 0; i < ticks; i++)
        {
            world.BeginTick();
            world.Step(held);
            if (speed is { } v)
                world.Train.Dynamics.Velocity = v;
            audio.Update(world, held, OnRoof(world.Train, car), exposed: true, SimConstants.TickSeconds);
            audio.Mixer.Render(block);
            started.AddRange(audio.Mixer.Voices.Where(x => seen.Add(x.Id)));
        }
        return started;
    }

    static bool Playing(GameAudio audio, string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished);

    /// <summary>A sound to stand in for a cue in a test, whatever is installed: a short tone, or a held one.</summary>
    static void Stand(GameAudio audio, params string[] cues)
    {
        foreach (var cue in cues)
            audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Duration: 0.2, MaxInstances: 64));
    }

    static void Held(GameAudio audio, params string[] cues)
    {
        foreach (var cue in cues)
            audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Loop: true, MaxInstances: 64));
    }

    /// <summary>A content root with only the synthesised bed (none of its recordings), the mix and the spaces.</summary>
    static string SynthesisedBedOnly()
    {
        var root = Directory.CreateTempSubdirectory("dt-synth-bed").FullName;
        var sounds = Directory.CreateDirectory(Path.Combine(root, "audio", "sounds")).FullName;
        foreach (var file in new[] { "mix.json", "spaces.json" })
            File.Copy(Path.Combine(Content, "audio", file), Path.Combine(root, "audio", file));
        foreach (var name in new[] { "boiler-roar", "chuff", "wheel-rail", "brake", "wind", "safety-valve", "boiler-strain", "vent-hiss", "boiler-burst", "slack-clunk", "rail-joint" })
            File.Copy(Path.Combine(Content, "audio", "sounds", name + ".json"), Path.Combine(sounds, name + ".json"));
        return root;
    }

    [Fact]
    public void TheBedPlaysItsRecordingsInsteadOfItsSynthsAndTheSynthsWhereThereAreNone()
    {
        // Whatever's installed: a recording plays in its synth's place, never on top of it; with none, the synth plays on.
        var audio = new GameAudio(Content);
        var world = Train();
        Run(audio, world, 45, speed: 15);
        foreach (var (synth, cues) in Bed)
        {
            bool recorded = cues.Any(audio.HasCue);
            Assert.Equal(!recorded, Playing(audio, synth));
            if (recorded && synth is not "chuff")
                Assert.Contains(audio.Mixer.Voices, v => cues.Contains(v.Name) && !v.Finished);
        }

        var bare = new GameAudio(SynthesisedBedOnly());
        Run(bare, Train(), 45, speed: 15);
        foreach (var (synth, cues) in Bed)
        {
            Assert.True(Playing(bare, synth), $"{synth} should play with no recording of it");
            Assert.DoesNotContain(bare.Mixer.Voices, v => cues.Contains(v.Name));
        }
    }

    [Fact]
    public void TheExhaustBeatsFourTimesATurnOfTheDrivingWheelsAndTheRailJointsUnderEachAxle()
    {
        var audio = new GameAudio(Content);
        Stand(audio, "bed-chuff.chuff", "bed-chuff.chuff-heavy", "bed-chuff.rod-clank", "bed-wheel-rail.joint");
        Held(audio, "bed-wheel-rail.roll-slow", "bed-wheel-rail.roll-fast");
        const double speed = 10;
        var started = Run(audio, Train(speed: speed), SimConstants.TickRate * 4, speed: speed);
        // Four beats a turn of 1.7 m wheels: 7.5 a second at 10 m/s; the rods once a turn.
        int beats = started.Count(v => v.Name is "bed-chuff.chuff" or "bed-chuff.chuff-heavy");
        Assert.InRange(beats, 26, 34);
        Assert.InRange(started.Count(v => v.Name == "bed-chuff.rod-clank"), 6, 9);
        // 12 m rails: every axle under the four cars heard over a joint every 1.2 s.
        Assert.InRange(started.Count(v => v.Name == "bed-wheel-rail.joint"), 16 * 3 - 4, 16 * 4 + 4);
    }

    [Fact]
    public void TheSlackRunsInUnderTheBrakeAndOutUnderPower()
    {
        var audio = new GameAudio(Content);
        Stand(audio, "bed-slack.run-in", "bed-slack.run-out");
        var world = Train(cars: 6, speed: 10);
        // Running, for long enough that whatever it started with has run down the train.
        Run(audio, world, SimConstants.TickRate * 2);
        var braking = Run(audio, world, SimConstants.TickRate * 2, controls: new TrainControls { Reverser = 1, Brake = 1 });
        Assert.True(braking.Count(v => v.Name == "bed-slack.run-in") >= world.Train.Vehicles.Count - 1);
        Assert.DoesNotContain(braking, v => v.Name == "bed-slack.run-out");
    }

    [Fact]
    public void TheBoilerIsHeardLiftingItsValveVentingStrainingAndBursting()
    {
        var audio = new GameAudio(Content);
        Stand(audio, "state-valve.lift", "state-valve.reseat", "bed-vent.open", "bed-vent.close", "state-rupture.burst", "state-rupture.debris",
            "state-strain.tick", "state-strain.rivet");
        Held(audio, "state-valve.blow", "bed-vent.blow", "state-rupture.steam-out", "state-strain.groan");
        var world = Train(speed: 0);
        var train = world.Train;
        var ear = OnRoof(train, 1);
        var started = new List<string>();
        var seen = new HashSet<int>();
        void Tick(Action<TrainOnLine>? set = null)
        {
            set?.Invoke(train);
            audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
            audio.Mixer.Render(new float[Audio.Block * 2]);
            started.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id)).Select(v => v.Name));
        }
        Tick();
        // The valve lifts (and lifts again, tick by tick, as it holds the pressure there): one lift, the blow held.
        for (int i = 0; i < 20; i++)
            Tick(t => t.Boiler.SafetyValveLifting = i % 3 != 2);
        Assert.Single(started, n => n == "state-valve.lift");
        Assert.True(Playing(audio, "state-valve.blow"));
        Assert.DoesNotContain("safety-valve", started);
        for (int i = 0; i < 30; i++)
            Tick(t => t.Boiler.SafetyValveLifting = false);
        Assert.Single(started, n => n == "state-valve.reseat");
        Assert.False(Playing(audio, "state-valve.blow"));

        // The vent held open, and let go.
        for (int i = 0; i < 10; i++)
            Tick(t => t.Boiler.Vented = true);
        Tick(t => t.Boiler.Vented = false);
        Assert.Single(started, n => n == "bed-vent.open");
        Assert.Single(started, n => n == "bed-vent.close");

        // In the red and held at the top: the plates ticking and the rivets going, more as it nears the rupture.
        for (int i = 0; i < SimConstants.TickRate * 4; i++)
            Tick(t => (t.Boiler.Pressure, t.Boiler.AtMaxSeconds) = (Boilers.PressureMax, Boilers.RuptureHoldSeconds * 0.9));
        Assert.True(Playing(audio, "state-strain.groan"));
        Assert.Contains("state-strain.tick", started);

        // It goes: the burst, the debris coming down after it, and the steam pouring out.
        Tick(t => (t.Boiler.Ruptured, t.Boiler.Pressure) = (true, 0));
        Assert.Single(started, n => n == "state-rupture.burst");
        Assert.DoesNotContain("boiler-burst", started);
        for (int i = 0; i < SimConstants.TickRate * 3; i++)
            Tick();
        Assert.InRange(started.Count(n => n == "state-rupture.debris"), 3, 5);
        Assert.True(Playing(audio, "state-rupture.steam-out"));
        Assert.False(Playing(audio, "state-strain.groan"));
    }

    static readonly string[] Derailing = ["state-derail.climb", "state-derail.collide", "state-derail.tear", "state-derail.tip", "state-derail.impact.ground",
        "state-derail.settle", "bed-slack.run-in", "bed-slack.run-out", "state-rupture.burst"];

    [Fact]
    public void ADerailmentIsTheManySoundsOfItsWreckWhereTheyHappen()
    {
        // GDD §23 and the director's note: not one sound, but each thing the wreck does where it does it. The sim stops the
        // train dead; what each car was doing the tick before decides the rest.
        var audio = new GameAudio(Content);
        Stand(audio, Derailing);
        Held(audio, "state-derail.rail-scrape", "state-derail.grind", "state-rupture.steam-out");
        var world = Train(cars: 8, speed: 18);
        Run(audio, world, 15, speed: 18);
        var frames = world.Train.Frames.ToList();
        world.Derail("a test");
        var heard = Run(audio, world, SimConstants.TickRate * 12, speed: 0);
        Assert.Contains(heard, v => v.Name == "state-derail.climb");
        Assert.Contains(heard, v => v.Name == "state-derail.collide");
        Assert.Contains(heard, v => v.Name == "state-derail.settle");
        Assert.Contains(heard, v => v.Name == "state-derail.rail-scrape" || v.Name == "state-derail.grind");
        Assert.True(Playing(audio, "state-rupture.steam-out"));
        // Each one at the train, where a car is.
        foreach (var v in heard.Where(v => v.Name.StartsWith("state-derail.")))
            Assert.True(frames.Min(f => (f.Origin - v.Position).Length) < 15, $"{v.Name} at {v.Position}, away from every car");
        // The wreck's played itself out: nothing still scraping or grinding, the hiss of the torn pipes going on.
        Assert.True(audio.Derailment!.Done);
        Assert.False(Playing(audio, "state-derail.rail-scrape"));
        Assert.False(Playing(audio, "state-derail.grind"));
        // And the bed's quiet: no slack clunking down a train that's stopped dead.
        Assert.DoesNotContain(heard, v => v.Name == "slack-clunk");
    }

    [Fact]
    public void AFasterDerailmentIsABiggerWreck()
    {
        int Count(double speed, Wreck.Kind kind) => Pile(speed, curve: 0).Count(h => h.Kind == kind);
        Assert.True(Count(20, Wreck.Kind.Collide) + Count(20, Wreck.Kind.Tear) > Count(4, Wreck.Kind.Collide) + Count(4, Wreck.Kind.Tear));
        Assert.True(Count(20, Wreck.Kind.Climb) > Count(4, Wreck.Kind.Climb), "more cars come off at speed");
        Assert.Equal(1, Count(1.5, Wreck.Kind.Climb));
    }

    [Fact]
    public void ACurveTakenTooFastThrowsTheCarsOver()
    {
        var thrown = Pile(18, curve: 1.3);
        Assert.Contains(thrown, h => h.Kind == Wreck.Kind.Tip);
        // Each car that goes over lands, after it starts to.
        foreach (var tip in thrown.Where(h => h.Kind == Wreck.Kind.Tip))
            Assert.Contains(thrown, h => h.Kind == Wreck.Kind.Impact && h.Car == tip.Car);
        Assert.DoesNotContain(Pile(4, curve: 0), h => h.Kind == Wreck.Kind.Tip);
    }

    [Fact]
    public void AWreckIsTheSameEveryTimeFromTheSameStart()
    {
        Assert.Equal(Pile(16, curve: 1.1), Pile(16, curve: 1.1));
        Assert.NotEqual(Pile(16, curve: 1.1), Pile(16, curve: 1.1, seed: 2));
    }

    [Fact]
    public void WhereTheTrackGoesTheCarsOnItFallAndLand()
    {
        var starts = Enumerable.Range(0, 6).Select(i => new Wreck.Start(i, i == 0 ? 90 : 40, i == 0 ? 20 : 14, 12, Drop: i is 2 or 3 ? 8 : 0)).ToList();
        var wreck = new Wreck(starts, 2, 1.5, 1);
        var heard = new List<(double At, Wreck.Happening What)>();
        var step = new List<Wreck.Happening>();
        for (int i = 0; i < SimConstants.TickRate * 15 && !wreck.Done; i++)
        {
            step.Clear();
            wreck.Advance(SimConstants.TickSeconds, step);
            heard.AddRange(step.Select(h => (wreck.Time, h)));
        }
        // Over the edge at once, and down 8 m in about 1.3 s.
        foreach (int car in new[] { 2, 3 })
        {
            Assert.Contains(heard, h => h.What.Kind == Wreck.Kind.Climb && h.What.Car == car && h.At < 0.1);
            Assert.Contains(heard, h => h.What.Kind == Wreck.Kind.Impact && h.What.Car == car && h.At > 1.1 && h.At < 1.5);
        }
        Assert.True(wreck.Done);
    }

    /// <summary>A loaded train of seven cars behind its engine derailing at a speed, on a curve of this severity (1: its limit).</summary>
    static List<Wreck.Happening> Pile(double speed, double curve, ulong seed = 1)
    {
        var starts = Enumerable.Range(0, 8).Select(i => new Wreck.Start(i, i == 0 ? 90 : 40, i == 0 ? 20 : 14, speed, curve, i == 0 ? 0.75 : 1.25)).ToList();
        var wreck = new Wreck(starts, 0, 1.5, seed);
        var heard = new List<Wreck.Happening>();
        for (int i = 0; i < SimConstants.TickRate * 20 && !wreck.Done; i++)
            wreck.Advance(SimConstants.TickSeconds, heard);
        Assert.True(wreck.Done);
        return heard;
    }
}
