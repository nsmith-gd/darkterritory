using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>How clearly one telegraph reads over everything else, in its own band, while it's sounding.</summary>
/// <param name="OverBedDb">Tell level minus everything that isn't a tell (spec A.3: tier 1 is never masked).</param>
/// <param name="OverAllDb">Tell level minus everything else, other tells included.</param>
public sealed record TellLevel(string Sound, double BandLow, double BandHigh, double TellDb, double OverBedDb, double OverAllDb, double SoundingSeconds);

/// <summary>One tell's worst margin over the bed among the listeners who have to hear it.</summary>
public sealed record TellAudit(string Sound, string WorstListener, double WorstOverBedDb, double WorstOverAllDb);

public sealed record AudioSweep(string Scenario, int Cars, double Speed, IReadOnlyList<TellAudit> Audit, IReadOnlyList<AudioBenchReport> Listeners);

public sealed record AudioBenchReport(string Scenario, int Cars, double Speed, string Listener, double Seconds, double MixDb, int PeakVoices,
    IReadOnlyList<TellLevel> Tells, IReadOnlyDictionary<string, double> StemsDb, MusicReport? Music = null);

/// <summary>
/// The derailment's opera in a "wreck" render (GDD v1.4 App. E.6): which track the host drew, when it started (the replay's
/// first frame) and when its hit should land (the replay's moment of derailment) and did (the music stem's sharpest onset),
/// in render seconds; how loud it came out; how far it ducked under the dead channel's laughing (<see cref="DuckDb"/>, from
/// <see cref="VoiceFrom"/> to <see cref="VoiceTo"/>); and how far the rest of the game went into its low-pass.
/// </summary>
public sealed record MusicReport(string Track, string Mood, double StartedAt, double HitDueAt, double HitHeardAt, double MusicDb, double DuckDb,
    double VoiceFrom, double VoiceTo, double GameLowpass, double FadedBy);

/// <summary>
/// Renders a staged moment offline and measures it: spec A.3's "the agent harness should test [tier 1] by
/// generating maximum-chaos states and verifying tell audibility". Used by `dt audio render` and the tests.
/// </summary>
public static class AudioBench
{
    /// <summary>Each tell's band from spec A.4's collision table.</summary>
    public static readonly IReadOnlyDictionary<string, (double Low, double High)> TellBands = new Dictionary<string, (double, double)>
    {
        // The v1.1 roster's tells (GDD v1.1 §21; systems spec A.4's allocation): each in a band of its own among the tells
        // that can sound together, the bands the v1.0 roster freed reused where they fit.
        ["sleepers-writhe"] = (400, 2000),
        ["hound-howl"] = (500, 3000),
        ["dragger-scrape"] = (2000, 4000),
        ["climber-scrabble"] = (2000, 5000),
        ["tippy-tiptoe"] = (1000, 2000),
        ["stoker-hiss"] = (1000, 3000),
        ["doll-giggle"] = (3000, 6000),
        ["ribbit-swell"] = (100, 1000),
        ["grumbler-gnaw"] = (1400, 2200),
        ["choir-voice"] = (300, 4000),
        ["car-fire"] = (6000, 9000),
        ["fireflies-buzz"] = (9000, 12000),
        ["train-whistle"] = (200, 800),
        ["hugger-grind"] = (60, 300),
        // The marsh (the Drift, T63): the highest there is. A hiss the wind doesn't make, and a rhythm it doesn't have.
        ["drift-rustle"] = (12000, 15000),
        // GDD v1.5's three, met at stops: the Shy Thing's ringing (its victim's alone) and its jaw, the Huddle bristling,
        // the Mimic's lid and its breath.
        ["shy-hum"] = (5000, 6500),
        ["shy-unhinge"] = (2500, 4500),
        ["huddle-hiss"] = (7000, 9000),
        ["mimic-creak"] = (700, 1500),
        ["mimic-breath"] = (3000, 4500),
    };

    /// <summary>
    /// Who has to hear each tell for its counter to be possible, where <see cref="Staging.Threats"/> puts it: the cab brakes
    /// for debris, fights the Stoker, and keeps the Track Doll off the controls; the rear answers hounds and the Car Hugger;
    /// car 1 has the Dragger, the fire and the Grumbler on the ground beside it; car 2's roof has Tippy Toesie behind them,
    /// Climbers at the gap behind it and Ribbits on the ground; the middle car has the Fire Flies. The whistle and the Choir
    /// are everybody's business.
    /// </summary>
    public static bool MustHear(string sound, int listenerCar, int cars) => sound switch
    {
        "sleepers-writhe" or "stoker-hiss" or "doll-giggle" => listenerCar == 0,
        "hound-howl" or "hugger-grind" => listenerCar == cars - 1,
        "drift-rustle" or "car-fire" or "dragger-scrape" or "grumbler-gnaw" => listenerCar == 1,
        "tippy-tiptoe" or "climber-scrabble" or "ribbit-swell" => listenerCar == 2,
        // The Shy Thing's victim off car 2's side; its jaw and the breathing crate by car 1; the Huddle in the cab.
        "shy-hum" or "mimic-creak" => listenerCar == 2,
        "shy-unhinge" or "mimic-breath" => listenerCar == 1,
        "huddle-hiss" => listenerCar == 0,
        "fireflies-buzz" => listenerCar == cars / 2,
        _ => true,
    };

    /// <summary>Renders the scenario from every listener that matters and reports each tell's worst case.</summary>
    public static AudioSweep Sweep(string content, string scenario = "chaos", int cars = 20, double speed = 22, double seconds = 6)
    {
        int[] listeners = [0, 1, 2, cars / 2, cars - 1];
        var reports = listeners.Select(l => Render(content, scenario, cars, speed, l, seconds).Report).ToList();
        var audit = new List<TellAudit>();
        foreach (var sound in TellBands.Keys)
        {
            var heard = listeners.Zip(reports).Where(x => MustHear(sound, x.First, cars))
                .Select(x => (x.Second.Listener, Level: x.Second.Tells.FirstOrDefault(t => t.Sound == sound))).ToList();
            if (heard.Count == 0 || heard.All(h => h.Level is null) && scenario == "bed")
                continue;
            var worst = heard.MinBy(h => h.Level?.OverBedDb ?? double.NegativeInfinity);
            audit.Add(new TellAudit(sound, worst.Listener, worst.Level?.OverBedDb ?? double.NegativeInfinity, worst.Level?.OverAllDb ?? double.NegativeInfinity));
        }
        return new AudioSweep(scenario, cars, speed, audit, reports);
    }

    /// <param name="scenario">"bed" (train only), "tells" (quiet train, every demo tell), "chaos" (everything at once) or
    /// "wreck" (the train derails, heard from beside the line), "toys" (the noisy toys carried on the listener's roof).</param>
    /// <param name="listenerCar">0 = in the cab; otherwise on that car's roof.</param>
    /// <param name="track">"wreck": the music track to play (a manifest id); null, the host's draw from a fresh bag.</param>
    public static (AudioBenchReport Report, float[] Mix) Render(string content, string scenario = "chaos", int cars = 20, double speed = 22,
        int listenerCar = 5, double seconds = 6, string? track = null)
    {
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var playerTuning = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        var boilerTuning = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
        var combat = DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File));
        var line = RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, cars, 1)), line, cars * 16.0 + 200, boilerTuning);
        train.Dynamics.Velocity = speed;
        var world = new World(train, combat) { WreckTuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File)) };
        var audio = new GameAudio(content);
        bool chaos = scenario == "chaos", tells = chaos || scenario == "tells";
        // "wreck" (T117): the train comes off half a second in, heard from beside the line where the engine left it.
        bool wreck = scenario == "wreck";
        // "toys" (note 175): the squeaker, the music box and the wind-up drummer, each in a crewmate's hands on the listener's
        // roof, a few metres apart: what the meter's being fed, heard over the train.
        if (scenario == "toys")
        {
            int car = Math.Clamp(listenerCar, 1, train.Frames.Count - 1);
            double roof = train.Frames[car].Shape.RoofHeight + 1.0;
            var noises = new[] { Sim.Physics.ToyNoise.Squeaker, Sim.Physics.ToyNoise.MusicBox, Sim.Physics.ToyNoise.Drummer };
            for (int i = 0; i < noises.Length; i++)
            {
                var toy = world.Bodies.SpawnCrate(train, car, new Double3(0.3, roof, (i - 1) * 3.0), Sim.Physics.BodyKind.Toy);
                toy.Noise = noises[i];
                toy.Carrier = i + 1;
            }
        }
        Double3? trackside = null;
        // E.6: the host's draw, from a fresh bag (or the one track asked for), and the dead channel laughing over the replay.
        var wreckTuning = world.WreckTuning;
        double derailedAt = double.NaN;
        StreamBuffer? deadChannel = null;
        var laughter = new Laughter();
        double duck = 1, lowpass = 0, startedAt = double.NaN;
        (double From, double To) voice = (wreckTuning.FirstPersonSeconds + 5, wreckTuning.FirstPersonSeconds + 8);
        if (wreck)
        {
            var tracks = audio.Opera.Manifest.Tracks.Where(t => track is null || t.Id == track).ToList();
            world.Music = new Sim.Music.MusicRotation(tracks, wreckTuning.Music);
            deadChannel = new StreamBuffer();
            audio.Mixer.Play("voice-dead")?.Also(v => v.Stream = deadChannel);
        }
        var controls = new TrainControls { Throttle = chaos ? 1 : 0.5, Reverser = 1 };
        listenerCar = Math.Clamp(listenerCar, 0, train.Frames.Count - 1);

        int blocks = (int)Math.Ceiling(seconds * Audio.SampleRate / Audio.Block);
        var mix = new float[blocks * Audio.Block * 2];
        var tap = new MeterTap(blocks * Audio.Block);
        audio.Mixer.Tap = tap;
        double blockSeconds = (double)Audio.Block / Audio.SampleRate, simClock = 0;
        int peak = 0;
        for (int b = 0; b < blocks; b++)
        {
            double now = b * blockSeconds;
            // The sim at 30 Hz, audio updates with it, the mixer in between (as in the game).
            while (simClock <= now)
            {
                simClock += SimConstants.TickSeconds;
                world.BeginTick();
                if (chaos)
                {
                    // Surging regulator and brake for slack action, the valve lifting, the Choir in full swarm.
                    controls.Throttle = (int)(simClock / 1.5) % 2 == 0 ? 1 : 0;
                    controls.Brake = (int)(simClock / 2.5) % 2 == 1 ? 1 : 0;
                }
                if (tells)
                {
                    world.Choir = chaos ? new ChoirState { Present = true, Build = 1, Loudness = combat.Choir.MaxLoudness } : new ChoirState { Build = 0.6, Loudness = combat.Choir.Threshold };
                    world.Whistled(1); // the Whistler at the cord (App. A.4), or someone on it
                    world.MirrorEnemies(Staging.Threats(train));
                }
                if (wreck && train.Wreck is null && simClock >= 0.5)
                {
                    world.Derail("dt audio render wreck");
                    derailedAt = simClock;
                    trackside = train.Frames[0].ToWorld(new Double3(14, 1.7, 30));
                }
                if (wreck)
                {
                    double sequence = double.IsNaN(derailedAt) ? -1 : simClock - derailedAt;
                    audio.Music(world.DerailMusic, sequence, wreckTuning);
                    if (audio.Opera.Playing is not null && double.IsNaN(startedAt))
                        startedAt = now;
                    var words = new float[(int)(SimConstants.TickSeconds * Audio.SampleRate)];
                    if (sequence >= voice.From && sequence < voice.To)
                        laughter.Fill(words);
                    deadChannel!.Write(words);
                }
                world.Step(controls);
                if (train.Wreck is null)
                    train.Dynamics.Velocity = speed; // hold the moment still in speed
                if (chaos)
                    train.Boiler.SafetyValveLifting = true;
                var player = listenerCar == 0 ? PlayerMotor.SpawnInCab(train, playerTuning) : PlayerMotor.SpawnOnRoof(train, listenerCar, 0, playerTuning);
                var frame = train.Frames[player.Parent];
                var ear = trackside ?? frame.ToWorld(player.Position + Double3.Up * 1.65);
                audio.Update(world, controls, Listener.At(ear, frame.Heading + player.Yaw), exposed: listenerCar != 0 || trackside is not null, SimConstants.TickSeconds);
                if (chaos && (int)(simClock * 3) != (int)((simClock - SimConstants.TickSeconds) * 3))
                {
                    int guard = train.Dynamics.Consist.Vehicles[^1].Id;
                    audio.Play("gunshot", train.Frames[guard].ToWorld(Sim.Combat.Guns.Mount(train, guard)!.Value.Position));
                    // Its ball coming down behind the train (T121): the boom, and its thud on a hound, mustn't drown a tell either.
                    var landed = train.Frames[guard].ToWorld(new Double3(0, 0, train.Frames[guard].Shape.HalfLength + 45));
                    audio.Play("cannon-impact", landed);
                    audio.Play("hit-confirm", landed + Double3.Up * 0.6);
                }
            }
            audio.Mixer.Render(mix.AsSpan(b * Audio.Block * 2, Audio.Block * 2));
            peak = Math.Max(peak, audio.Mixer.RenderedVoices);
            if (wreck)
            {
                duck = Math.Min(duck, audio.Mixer.MusicDuck);
                lowpass = Math.Max(lowpass, audio.Mixer.GameLowpass);
            }
        }

        // Skip the first half second: everything is still fading in.
        int skip = Math.Min(tap.Total.Length, (int)(0.5 * Audio.SampleRate) * 2);
        var bed = (float[])tap.Total.Clone();
        foreach (var name in TellBands.Keys)
            if (tap.Stems.TryGetValue(name, out var stem))
                for (int i = 0; i < bed.Length; i++)
                    bed[i] -= stem[i];
        var levels = new List<TellLevel>();
        foreach (var (name, band) in TellBands)
        {
            if (!tap.Stems.TryGetValue(name, out var stem))
                continue;
            var (tellDb, overBed, sounding) = Contrast(stem.AsSpan(skip), bed.AsSpan(skip), band);
            var (_, overAll, _) = Contrast(stem.AsSpan(skip), tap.AllBut(name).AsSpan(skip), band);
            if (sounding > 0)
                levels.Add(new TellLevel(name, band.Low, band.High, Math.Round(tellDb, 1), Math.Round(overBed, 1), Math.Round(overAll, 1), Math.Round(sounding, 2)));
        }
        var stems = tap.Stems.ToDictionary(kv => kv.Key, kv => Math.Round(Meter.Db(kv.Value.AsSpan(skip)), 1));
        string where = listenerCar == 0 ? "cab" : $"roof of car {listenerCar}";
        MusicReport? music = null;
        if (wreck && audio.Opera.Manifest.ByKey(world.DerailMusic) is { } drawn && tap.Stems.TryGetValue(Opera.Sound, out var score))
        {
            double due = derailedAt + wreckTuning.FirstPersonSeconds + wreckTuning.ReplayLeadSeconds;
            double heard = SharpestOnset(score, due - 1, due + 1);
            double fadedBy = derailedAt + wreckTuning.SequenceSeconds;
            music = new MusicReport(drawn.Id, drawn.Mood.ToString(), Math.Round(startedAt, 3), Math.Round(due, 3), Math.Round(heard, 3),
                Math.Round(Meter.Db(score.AsSpan(skip)), 1), Math.Round(Audio.GainToDb(duck), 2), Math.Round(derailedAt + voice.From, 2),
                Math.Round(derailedAt + voice.To, 2), Math.Round(lowpass, 2), Math.Round(fadedBy, 2));
        }
        return (new AudioBenchReport(scenario, cars, speed, where, seconds, Math.Round(Meter.Db(mix.AsSpan(skip)), 1), peak, levels, stems, music), mix);
    }

    /// <summary>
    /// The time (render seconds) of the biggest rise in level between 10 ms blocks of a stem, within a window, heard above
    /// 4 kHz: every track's hit has the crash on it (`dt audio opera`), and nothing else in the bars around it does.
    /// </summary>
    public static double SharpestOnset(float[] stem, double from, double to)
    {
        int block = Audio.SampleRate / 100, frames = stem.Length / 2;
        Biquad h1 = default, h2 = default;
        h1.Set(FilterType.HighPass, 4000, 0.707);
        h2.Set(FilterType.HighPass, 4000, 0.707);
        double best = double.NegativeInfinity, at = double.NaN, last = -120;
        int first = Math.Max(0, (int)(from * Audio.SampleRate));
        for (int start = first; start + block <= Math.Min(frames, (int)(to * Audio.SampleRate)); start += block)
        {
            double e = 0;
            for (int i = start; i < start + block; i++)
            {
                float x = h2.Process(h1.Process(0.5f * (stem[i * 2] + stem[i * 2 + 1])));
                e += x * x;
            }
            double db = 10 * Math.Log10(e / block + 1e-12);
            if (start > first && db - last > best)
            {
                best = db - last;
                at = (double)start / Audio.SampleRate;
            }
            last = db;
        }
        return at;
    }

    /// <summary>
    /// Someone on the dead channel laughing (App. E.6: "so the laughing stays audible"), for the wreck render: "ha-ha-ha" at
    /// four and a half a second, a buzz through the formants of "ah" with breath in it, each "ha" a little lower.
    /// </summary>
    sealed class Laughter
    {
        Biquad _f1, _f2;
        Noise _breath = new(77);
        double _t, _phase;

        public Laughter()
        {
            _f1.Set(FilterType.BandPass, 750, 4);
            _f2.Set(FilterType.BandPass, 1250, 5);
        }

        public void Fill(Span<float> into)
        {
            for (int i = 0; i < into.Length; i++, _t += 1.0 / Audio.SampleRate)
            {
                double syllable = _t * 4.5 % 1, n = Math.Floor(_t * 4.5);
                double env = syllable < 0.55 ? Math.Pow(Math.Sin(Math.PI * syllable / 0.55), 2) : 0;
                _phase += (230 - 6 * (n % 6)) / Audio.SampleRate;
                _phase -= Math.Floor(_phase);
                float buzz = (float)(2 * _phase - 1) + 0.35f * _breath.Next();
                into[i] = (float)(env * 0.9 * (_f1.Process(buzz) + 0.6f * _f2.Process(buzz)));
            }
        }
    }

    /// <summary>Band-limited level of a tell and its margin over a masker, counted only while the tell is sounding.</summary>
    static (double TellDb, double MarginDb, double SoundingSeconds) Contrast(ReadOnlySpan<float> tell, ReadOnlySpan<float> masker, (double Low, double High) band)
    {
        Biquad[] ft = Band(band), fm = Band(band);
        double tellEnergy = 0, maskEnergy = 0;
        int sounding = 0, frames = tell.Length / 2;
        for (int start = 0; start + Audio.Block <= frames; start += Audio.Block)
        {
            double te = 0, me = 0;
            for (int i = start; i < start + Audio.Block; i++)
            {
                float t = Filter(ft, 0.5f * (tell[i * 2] + tell[i * 2 + 1]));
                float m = Filter(fm, 0.5f * (masker[i * 2] + masker[i * 2 + 1]));
                te += t * t;
                me += m * m;
            }
            if (te / Audio.Block < 1e-7) // quieter than −70 dBFS in band: not sounding this block
                continue;
            tellEnergy += te;
            maskEnergy += me;
            sounding += Audio.Block;
        }
        if (sounding == 0)
            return (double.NegativeInfinity, double.NegativeInfinity, 0);
        double tellDb = 10 * Math.Log10(tellEnergy / sounding);
        double maskDb = 10 * Math.Log10(Math.Max(maskEnergy, 1e-20) / sounding);
        return (tellDb, tellDb - maskDb, (double)sounding / Audio.SampleRate);
    }

    static Biquad[] Band((double Low, double High) band)
    {
        var f = new Biquad[4];
        f[0].Set(FilterType.HighPass, band.Low, 0.707);
        f[1].Set(FilterType.HighPass, band.Low, 0.707);
        f[2].Set(FilterType.LowPass, band.High, 0.707);
        f[3].Set(FilterType.LowPass, band.High, 0.707);
        return f;
    }

    static float Filter(Biquad[] chain, float x)
    {
        for (int i = 0; i < chain.Length; i++)
            x = chain[i].Process(x);
        return x;
    }
}
