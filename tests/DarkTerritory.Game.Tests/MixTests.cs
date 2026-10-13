using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The mix around the listener: each space sounding like itself (spec A.6, content/audio/spaces.json), a tunnel shutting
/// out the world outside (GDD §22), and the night's music on its own bottom tier (decided 1 Oct).
/// </summary>
public class MixTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning Players = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly BoilerTuning Boilers = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));

    /// <summary>A generated night with the engine <paramref name="into"/> metres into a feature of this kind (long enough for the train).</summary>
    static (World World, RouteFeature Feature) NightIn(FeatureKind kind, double into)
    {
        var routes = RouteTuning.Load(Content);
        foreach (var tier in Enum.GetValues<RouteTier>())
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var route = RouteGenerator.Generate(routes, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Kind == kind && f.Start > 600 && f.End - f.Start > into + 20) is not { } f)
                    continue;
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), f.Start + into, Boilers);
                var world = new World(train);
                world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, 600, authority: true);
                return (world, f);
            }
        throw new InvalidOperationException($"no generated route has a {kind} long enough");
    }

    static World OnTheTestLoop()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), line, 1200, Boilers));
    }

    static Double3 Ear(in PlayerState s, TrainOnLine train) => PlayerMotor.WorldPosition(s, train) + Double3.Up * 1.65;
    static Double3 Roof(TrainOnLine train, int car) => Ear(PlayerMotor.SpawnOnRoof(train, car, 0, Players), train);
    static Double3 Cab(TrainOnLine train) => Ear(PlayerMotor.SpawnInCab(train, Players), train);

    /// <summary>Standing in the middle of the first car that has an inside.</summary>
    static Double3 InACar(TrainOnLine train)
    {
        var frame = train.Frames.First(f => f.Index > 0 && f.Shape.Interior is not null);
        var room = frame.Shape.Interior!.Value;
        return frame.ToWorld(new Double3(0, room.Min.Y + 1.7, room.Centre.Z));
    }

    static string SpaceOf(World world, Double3 ear)
    {
        double hint = double.NaN;
        return GameAudio.SpaceOf(world, ear, ref hint);
    }

    [Fact]
    public void EverySpaceSoundsLikeItself()
    {
        var spaces = DataFile.Load<SpacesDef>(Path.Combine(Content, SpacesDef.File)).Spaces;
        Assert.Equal(["cab", "car", "facility", "mine", "outside", "room", "shed", "tunnel"], spaces.Keys.Order(StringComparer.Ordinal));
        // The open night is dry; every enclosed space has a response of its own, the tunnel's the longest by far.
        Assert.Null(spaces["outside"].Reverb);
        double Decay(string s) => spaces[s].Reverb!.Decay;
        Assert.True(Decay("cab") < Decay("car") && Decay("car") < Decay("facility") && Decay("facility") < Decay("tunnel"));
        // The mine's adit (note 250) is narrower than a tunnel: its tail's shorter, its first reflection sooner.
        Assert.True(Decay("facility") < Decay("mine") && Decay("mine") < Decay("tunnel"));
        Assert.True(spaces["mine"].Reverb!.Early![0][0] < spaces["tunnel"].Reverb!.Early![0][0]);
        // A stop's buildings (note 392): a small room between the cab's steel and a car's planks; a shed's iron hall past the
        // facility's yard and short of a tunnel, its walls answering later than a room's.
        Assert.True(Decay("cab") < Decay("room") && Decay("room") < Decay("car"));
        Assert.True(Decay("facility") < Decay("shed") && Decay("shed") < Decay("tunnel"));
        Assert.True(spaces["room"].Reverb!.Early![0][0] < spaces["shed"].Reverb!.Early![0][0]);
        foreach (var (name, space) in spaces)
        {
            if (space.Reverb is not { } reverb)
                continue;
            var ir = ImpulseResponse.Synthesize(reverb);
            Assert.True(ir.Left.Any(v => v != 0), name);
            // The cost (perf.json's frame budget): a partition per 5.3 ms of response; the tunnel's 1.8 s is 338.
            Assert.True(ir.Partitions <= 400, $"{name} is {ir.Partitions} partitions");
            // Music is never in the room; a tell goes in less than a voice, so its rhythm stays crisp.
            Assert.Equal(0, space.Send(7));
            Assert.True(space.Send(1) < space.Send(2), name);
        }
        // The tunnel and the mine have no outside (GDD §22) but keep their own sounds, and only they compress voice.
        foreach (var under in new[] { spaces["tunnel"], spaces["mine"] })
        {
            Assert.True(under.Mutes("wind") && under.Mutes("world-night.night") && under.Mutes("bed-wind.wind-fast") && under.Mutes("world-rain.rain-out"));
            Assert.False(under.Mutes("world-tunnels.inside") || under.Mutes("world-rain.rain-roof") || under.Mutes("wheel-rail") || under.Mutes("voice")
                || under.Mutes("place-mine.underground"));
        }
        Assert.Equal(["mine", "tunnel"], spaces.Where(s => s.Value.Voice is not null).Select(s => s.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MusicGivesWayToEveryOtherTierAndDucksNothing()
    {
        var mix = DataFile.Load<MixDef>(Path.Combine(Content, MixDef.File));
        Assert.Equal(7, Mixer.Tiers);
        for (int tier = 1; tier <= 6; tier++)
        {
            Assert.Contains(mix.Ducking, r => r.Tier == tier && r.Ducks.Contains(7) && r.Db < 0);
            Assert.Equal(0, mix.Fader(tier));
        }
        // Music ducks nothing at all, so never voice; and it sits low on its own fader.
        Assert.DoesNotContain(mix.Ducking, r => r.Tier == 7);
        Assert.True(mix.Fader(7) < 0);
        // A tell or a voice takes it further down than the bed does.
        double Under(int tier) => mix.Ducking.Where(r => r.Tier == tier && r.Ducks.Contains(7)).Min(r => r.Db);
        Assert.True(Under(1) < Under(5) && Under(2) < Under(5));
    }

    [Fact]
    public void NothingEverDucksVoice()
    {
        // Spec A.3 (decided 2 Oct): a crewmate calling out a tell is heard over the tell, so no tier ducks voice (tier 2), and
        // no per-sound rule ducks a voice's sound (the proximity voice, the radio, the dead channel).
        var mix = DataFile.Load<MixDef>(Path.Combine(Content, MixDef.File));
        Assert.DoesNotContain(mix.Ducking, r => r.Ducks.Contains(2));
        Assert.DoesNotContain(mix.SoundDucking ?? [], r => r.Ducks.Any(d => d == "voice" || d.StartsWith("voice-", StringComparison.Ordinal)));
        // The tells still take everything else down.
        Assert.Contains(mix.Ducking, r => r.Tier == 1 && new[] { 3, 4, 5, 6 }.All(r.Ducks.Contains) && r.Db < 0);
    }

    [Fact]
    public void TheListenerHearsTheSpaceTheyreIn()
    {
        var loop = OnTheTestLoop();
        Assert.Equal("cab", SpaceOf(loop, Cab(loop.Train)));
        Assert.Equal("car", SpaceOf(loop, InACar(loop.Train)));
        Assert.Equal("outside", SpaceOf(loop, Roof(loop.Train, 2)));

        // In a tunnel: on the roof, and in the open-backed cab, the bore's around you; inside a car its walls are.
        var (tunnel, bore) = NightIn(FeatureKind.Tunnel, into: 160);
        var train = tunnel.Train;
        Assert.True(bore.Contains(train.Cars[1].FrontDistance));
        Assert.Equal("tunnel", SpaceOf(tunnel, Roof(train, 1)));
        Assert.Equal("tunnel", SpaceOf(tunnel, Cab(train)));
        Assert.Equal("car", SpaceOf(tunnel, InACar(train)));
        // Up on the hill over the bore, 40 m off the line, it's the open night.
        var frame = train.Frames[1];
        Assert.Equal("outside", SpaceOf(tunnel, frame.Origin + frame.Right * 40 + Double3.Up * 20));

        // In a facility's yard, outside the train.
        var (facility, _) = NightIn(FeatureKind.Facility, into: 150);
        Assert.Equal("facility", SpaceOf(facility, Roof(facility.Train, 2)));
        Assert.Equal("cab", SpaceOf(facility, Cab(facility.Train)));
    }

    /// <summary>
    /// The space a listener at <paramref name="ear"/> hears in (one Update), then a gunshot 8 m ahead of them and nothing
    /// else: how long after the shot's done the mix stays within 60 dB of it.
    /// </summary>
    static (string Space, double Tail) AShotAndItsTail(World world, Double3 ear)
    {
        var audio = new GameAudio(Content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        audio.Update(world, new TrainControls(), Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
        audio.Mixer.StopAll(); // the bed and the wind: only the shot, and the room it's in
        var shot = audio.Mixer.Play("gunshot", ear + new Double3(0, 0, -8))!;
        var block = new float[Audio.Block * 2];
        var mix = new List<float>();
        while (!shot.Finished)
        {
            audio.Mixer.Render(block);
            mix.AddRange(block);
        }
        int end = mix.Count / 2;
        for (int b = 0; b < 3 * Audio.SampleRate / Audio.Block; b++)
        {
            audio.Mixer.Render(block);
            mix.AddRange(block);
        }
        var all = mix.ToArray();
        double loud = Meter.Db(all.AsSpan(0, end * 2));
        int window = Audio.SampleRate / 50;
        double last = 0;
        for (int at = end; at + window <= all.Length / 2; at += window)
            if (Meter.Db(all.AsSpan(at * 2, window * 2)) > loud - 60)
                last = (double)(at + window - end) / Audio.SampleRate;
        return (audio.Space, last);
    }

    [Fact]
    public void AShotInATunnelRingsOnLongAfterOneInTheOpen()
    {
        var loop = OnTheTestLoop();
        var (open, openTail) = AShotAndItsTail(loop, Roof(loop.Train, 2));
        var (tunnel, bore) = NightIn(FeatureKind.Tunnel, into: 160);
        var (inside, tunnelTail) = AShotAndItsTail(tunnel, Roof(tunnel.Train, 1));
        var (cab, cabTail) = AShotAndItsTail(loop, Cab(loop.Train));
        Assert.Equal(("outside", "tunnel", "cab"), (open, inside, cab));
        Assert.True(openTail < 0.05, $"in the open it rang {openTail} s");
        Assert.True(cabTail > openTail && cabTail < 0.5, $"in the cab it rang {cabTail} s");
        Assert.True(tunnelTail > 1.0 && tunnelTail > 4 * cabTail, $"in the tunnel it rang {tunnelTail} s");
    }

    [Fact]
    public void ATunnelShutsOutTheWindAndTheNight()
    {
        // GDD §22 "no exterior reference": at speed on the roof the wind roars past you; in the bore it's gone.
        double Wind(World world, Double3 ear)
        {
            world.Train.Dynamics.Velocity = 18;
            var audio = new GameAudio(Content);
            audio.Bank.Samples.InlineBytes = long.MaxValue;
            var tap = new MeterTap(Audio.SampleRate);
            var block = new float[Audio.Block * 2];
            long rendered = 0;
            for (int tick = 0; tick < 2 * SimConstants.TickRate; tick++)
            {
                // The second second is metered: the first is the wind coming up (or going, as you enter).
                audio.Mixer.Tap = tick >= SimConstants.TickRate ? tap : null;
                audio.Update(world, new TrainControls(), Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
                for (long until = (tick + 1L) * Audio.SampleRate / SimConstants.TickRate; rendered + Audio.Block <= until; rendered += Audio.Block)
                    audio.Mixer.Render(block);
            }
            // The synth's wind, or the recordings that take its place (GameAudio.Train).
            var winds = tap.Stems.Where(kv => kv.Key == "wind" || kv.Key.StartsWith("bed-wind.wind-")).Select(kv => kv.Value).ToList();
            if (winds.Count == 0)
                return -180;
            var wind = new float[winds[0].Length];
            foreach (var w in winds)
                for (int i = 0; i < wind.Length; i++)
                    wind[i] += w[i];
            return Meter.Db(wind);
        }
        var loop = OnTheTestLoop();
        var (tunnel, _) = NightIn(FeatureKind.Tunnel, into: 160);
        double open = Wind(loop, Roof(loop.Train, 2)), inside = Wind(tunnel, Roof(tunnel.Train, 1));
        Assert.True(open > -50, $"the wind in the open is {open} dB");
        Assert.True(inside < open - 60, $"the wind in the tunnel is {inside} dB, against {open} dB in the open");
    }

    [Fact]
    public void TheMusicPlaysFlatOnItsOwnTierFromTheStartOfTheNightUnderEverything()
    {
        var (world, _) = NightIn(FeatureKind.Facility, into: 150);
        var audio = new GameAudio(Content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        // The drone as tools/audio/install.py writes it, if it isn't installed yet: a flat loop on tier 7 (a sine for its takes).
        if (!audio.HasCue(GameAudio.MusicCue))
            audio.Bank.Add(GameAudio.MusicCue, new SoundDef(7, [new LayerDef(SourceKind.Sine, 0.05, 110)], Loop: true, Flat: true));
        var ear = Listener.At(Roof(world.Train, 2), 0);
        // In the yard, before the night's begun: nothing.
        Assert.Equal(RunPhase.Yard, world.Run!.Phase);
        audio.Update(world, new TrainControls(), ear, exposed: true, SimConstants.TickSeconds);
        Assert.Null(audio.Drone);
        // Under way: on, and it stays the one voice tick after tick.
        world.Run.Resume(0, -1, world.Train.Boiler.Tender, 0);
        audio.Update(world, new TrainControls(), ear, exposed: true, SimConstants.TickSeconds);
        var music = Assert.IsType<SoundInstance>(audio.Drone);
        audio.Update(world, new TrainControls(), ear, exposed: true, SimConstants.TickSeconds);
        Assert.Same(music, audio.Drone);
        Assert.Equal((7, true, true), (music.Def.Tier, music.Def.Flat, music.Def.Loop));
        var block = new float[Audio.Block * 2];
        for (int b = 0; b < 40; b++)
            audio.Mixer.Render(block);
        Assert.True(audio.Mixer.TierActive(7));
        // A tell comes in and the music gets out of its way (mix.json: -12 dB under tier 1).
        audio.Mixer.Play("hound-howl", ear.Position + new Double3(0, 0, -20));
        for (int b = 0; b < 80; b++)
            audio.Mixer.Render(block);
        Assert.True(Audio.GainToDb(audio.Mixer.TierGain(7)) < -10, $"music at {Audio.GainToDb(audio.Mixer.TierGain(7)):F1} dB under a howl");
        Assert.Equal(1, audio.Mixer.TierGain(1), 3);
    }
}
