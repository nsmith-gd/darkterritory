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
    IReadOnlyList<TellLevel> Tells, IReadOnlyDictionary<string, double> StemsDb);

/// <summary>
/// Renders a staged moment offline and measures it: spec A.3's "the agent harness should test [tier 1] by
/// generating maximum-chaos states and verifying tell audibility". Used by `dt audio render` and the tests.
/// </summary>
public static class AudioBench
{
    /// <summary>Each tell's band from spec A.4's collision table.</summary>
    public static readonly IReadOnlyDictionary<string, (double Low, double High)> TellBands = new Dictionary<string, (double, double)>
    {
        ["sleepers-writhe"] = (400, 2000),
        ["hound-howl"] = (500, 3000),
        ["clinger-drill"] = (3000, 6000),
        ["dragger-scrape"] = (2000, 4000),
        ["rattle"] = (2000, 5000),
        ["deadman-click"] = (1000, 2000),
        ["stoker-hiss"] = (1000, 3000),
        ["hollow-gutter"] = (100, 1000),
        ["choir-voice"] = (300, 4000),
    };

    /// <summary>
    /// Who has to hear each tell for its counter to be possible: the cab brakes for Sleepers and feeds the fire
    /// against the Hollow, the rear gun answers hounds, whoever's on that car prises off a Clinger, and the
    /// Choir is everybody's business. <see cref="Staging.Threats"/> puts the Clinger on car 1, a Dragger under its other edge,
    /// and the Rattle in the gap behind the middle car.
    /// </summary>
    public static bool MustHear(string sound, int listenerCar, int cars) => sound switch
    {
        "sleepers-writhe" or "hollow-gutter" => listenerCar == 0,
        // The engine's business: whoever's nearest the cab, which in the bench is the cab (T53).
        "deadman-click" or "stoker-hiss" => listenerCar == 0,
        "hound-howl" => listenerCar == cars - 1,
        "clinger-drill" => listenerCar == 1,
        // The one it's reaching for is on that car's roof: they're who has to hear it.
        "dragger-scrape" => listenerCar == 1,
        // Whoever's about to cross that gap: the Rattle sits behind the middle car.
        "rattle" => listenerCar == cars / 2,
        _ => true,
    };

    /// <summary>Renders the scenario from every listener that matters and reports each tell's worst case.</summary>
    public static AudioSweep Sweep(string content, string scenario = "chaos", int cars = 20, double speed = 22, double seconds = 6)
    {
        int[] listeners = [0, 1, cars / 2, cars - 1];
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

    /// <param name="scenario">"bed" (train only), "tells" (quiet train, every demo tell) or "chaos" (everything at once).</param>
    /// <param name="listenerCar">0 = in the cab; otherwise on that car's roof.</param>
    public static (AudioBenchReport Report, float[] Mix) Render(string content, string scenario = "chaos", int cars = 20, double speed = 22,
        int listenerCar = 5, double seconds = 6)
    {
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var playerTuning = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        var boilerTuning = DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File));
        var combat = DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File));
        var line = RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, cars, 1)), line, cars * 16.0 + 200, boilerTuning);
        train.Dynamics.Velocity = speed;
        var world = new World(train, combat);
        var audio = new GameAudio(content);
        bool chaos = scenario == "chaos", tells = chaos || scenario == "tells";
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
                    world.Choir = new ChoirState { Aggro = chaos ? combat.Choir.SwarmThreshold + 10 : combat.Choir.ApproachThreshold + 4, Floor = 0 };
                    world.MirrorEnemies(Staging.Threats(train));
                }
                world.Step(controls);
                train.Dynamics.Velocity = speed; // hold the moment still in speed
                if (chaos)
                    train.Boiler.SafetyValveLifting = true;
                var player = listenerCar == 0 ? PlayerMotor.SpawnInCab(train, playerTuning) : PlayerMotor.SpawnOnRoof(train, listenerCar, 0, playerTuning);
                var frame = train.Frames[player.Parent];
                var ear = frame.ToWorld(player.Position + Double3.Up * 1.65);
                audio.Update(world, controls, Listener.At(ear, frame.Heading + player.Yaw), exposed: listenerCar != 0, SimConstants.TickSeconds);
                if (chaos && (int)(simClock * 3) != (int)((simClock - SimConstants.TickSeconds) * 3))
                {
                    int guard = train.Dynamics.Consist.Vehicles[^1].Id;
                    audio.Play("gunshot", train.Frames[guard].ToWorld(train.Frames[guard].Shape.Gun!.Value.Position));
                }
            }
            audio.Mixer.Render(mix.AsSpan(b * Audio.Block * 2, Audio.Block * 2));
            peak = Math.Max(peak, audio.Mixer.RenderedVoices);
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
        return (new AudioBenchReport(scenario, cars, speed, where, seconds, Math.Round(Meter.Db(mix.AsSpan(skip)), 1), peak, levels, stems), mix);
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
