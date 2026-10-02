using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// Recorded takes as a layer source (<see cref="SourceKind.Sample"/>). The fixtures in Samples/ were made with
/// <c>ffmpeg -f lavfi -i "sine=frequency=F:duration=D" -ac 1 -ar 48000 -c:a libopus -b:a 64k -map_metadata -1
/// -fflags +bitexact -flags:a +bitexact</c>, as the game's takes are: tone-1k.opus is 1 kHz for 0.3 s; takes/ holds
/// three 0.1 s takes (500, 1500, 3000 Hz, at 32k).
/// </summary>
public class SampleTests
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Samples");
    const double ToneSeconds = 0.3;
    const double Block = (double)Audio.Block / Audio.SampleRate;

    static readonly MixDef Mix = new([], DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6, OcclusionDb: -12,
        OcclusionLowpass: 900, MasterDb: 0);

    static (Mixer Mixer, SoundBank Bank) Make(params (string Name, SoundDef Def)[] sounds)
    {
        var bank = new SoundBank(samples: Fixtures);
        foreach (var (name, def) in sounds)
            bank.Add(name, def);
        return (new Mixer(bank, Mix) { Listener = Listener.At(Double3.Zero, 0) }, bank);
    }

    static SoundDef Sample(string sample, bool loop = false, double rate = 1, double? duration = null, double pitchJitter = 0, params LayerDef[] more) =>
        new(4, [new LayerDef(SourceKind.Sample, 1, Sample: sample, Rate: rate, PitchJitter: pitchJitter), .. more], Loop: loop, Duration: duration);

    /// <summary>Renders until the voice is over (or the limit), returning how long it played.</summary>
    static double PlayOut(Mixer m, SoundInstance v, double limit = 3)
    {
        var block = new float[Audio.Block * 2];
        while (!v.Finished && v.Age < limit)
            m.Render(block);
        return v.Age;
    }

    /// <summary>Frequency by zero crossings: fine for one clean tone.</summary>
    static double Pitch(ReadOnlySpan<float> mono)
    {
        int first = -1, last = -1, crossings = 0;
        for (int i = 1; i < mono.Length; i++)
            if (mono[i - 1] < 0 && mono[i] >= 0)
            {
                if (first < 0)
                    first = i;
                else
                    crossings++;
                last = i;
            }
        return crossings * (double)Audio.SampleRate / (last - first);
    }

    static float[] Left(float[] stereo) => [.. Enumerable.Range(0, stereo.Length / 2).Select(i => stereo[i * 2])];

    [Fact]
    public void AnOpusTakeDecodesToItsTrueLengthAndPitch()
    {
        var pcm = OggOpus.Decode(File.ReadAllBytes(Path.Combine(Fixtures, "tone-1k.opus")), out float gain);
        // The file decodes to 15,360 samples: 312 of pre-skip at the front, and the encoder's padding at the end that the
        // last page's granule position (14,712) cuts off. What's left is the 0.3 s that was encoded.
        Assert.InRange(pcm.Length, ToneSeconds * Audio.SampleRate - 48, ToneSeconds * Audio.SampleRate + 48);
        Assert.Equal(1, gain);
        var mono = pcm.Select(s => s / 32768f).ToArray();
        Assert.InRange(Pitch(mono.AsSpan(2400, 9600)), 990, 1010);
        // In phase with the sine that went in (ffmpeg's starts at sin 0): with the pre-skip left in, it would be 312
        // samples late, which at 1 kHz is a 0.5 cycle out, and this correlation would be strongly negative.
        double dot = 0, a = 0, b = 0;
        for (int i = 480; i < pcm.Length - 480; i++)
        {
            double want = Math.Sin(2 * Math.PI * 1000 * i / Audio.SampleRate);
            dot += mono[i] * want;
            a += mono[i] * mono[i];
            b += want * want;
        }
        Assert.True(dot / Math.Sqrt(a * b) > 0.95, $"correlation with the source {dot / Math.Sqrt(a * b):F3}");
    }

    [Fact]
    public void TheOggReaderJoinsPacketsAcrossPages()
    {
        var file = File.ReadAllBytes(Path.Combine(Fixtures, "tone-1k.opus"));
        // The same packets with one lacing segment to a page: every packet over 254 bytes now spans pages.
        var split = Repage(file, segmentsPerPage: 1, out int continued);
        Assert.True(continued > 0);
        Assert.Equal(OggOpus.Packets(file, out long granule).Select(p => Convert.ToHexString(p)), OggOpus.Packets(split, out long splitGranule).Select(p => Convert.ToHexString(p)));
        Assert.Equal(granule, splitGranule);
        Assert.Equal(OggOpus.Decode(file, out _), OggOpus.Decode(split, out _));
    }

    [Fact]
    public void NotAnOpusFileIsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => OggOpus.Decode("RIFF....WAVEfmt this is not an ogg file at all"u8, out _));
        var file = File.ReadAllBytes(Path.Combine(Fixtures, "tone-1k.opus"));
        Assert.Throws<InvalidDataException>(() => OggOpus.Decode(file.AsSpan(0, file.Length - 100), out _));
    }

    [Fact]
    public void ASampleOneShotEndsWithItsTakeNotAtTheDefaultSecond()
    {
        var (m, bank) = Make(("tone", Sample("tone-1k")));
        var v = m.Play("tone")!;
        Assert.Null(bank.LastError);
        Assert.Equal(["tone-1k.opus"], v.Takes);
        var heard = new float[(int)(0.25 * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(heard);
        Assert.True(Meter.Db(heard) > -30);
        Assert.InRange(Pitch(Left(heard).AsSpan(480)), 990, 1010);
        Assert.InRange(PlayOut(m, v), ToneSeconds, ToneSeconds + 2 * Block);
    }

    [Fact]
    public void RateTwoPlaysAnOctaveUpInHalfTheTime()
    {
        var (m, _) = Make(("fast", Sample("tone-1k.opus", rate: 2)));
        var v = m.Play("fast")!;
        var heard = new float[(int)(0.12 * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(heard);
        Assert.InRange(Pitch(Left(heard).AsSpan(480)), 1980, 2020);
        Assert.InRange(PlayOut(m, v), ToneSeconds / 2, ToneSeconds / 2 + 2 * Block);
    }

    [Fact]
    public void ALoopedSampleKeepsPlayingWithoutASeam()
    {
        var (m, _) = Make(("hum", Sample("tone-1k", loop: true)));
        var v = m.Play("hum")!;
        var heard = new float[(int)(2.0 * Audio.SampleRate / Audio.Block) * Audio.Block * 2];
        m.Render(heard);
        Assert.False(v.Finished);
        var left = Left(heard);
        Assert.True(Meter.Db(heard.AsSpan(heard.Length - Audio.SampleRate)) > -30);
        // 0.3 s of 1 kHz is a whole number of cycles, so the wrap should be no steeper than the tone itself. A click at
        // each of the six seams would be a jump of up to the tone's full swing.
        static float Steepest(ReadOnlySpan<float> x)
        {
            float worst = 0;
            for (int i = 1; i < x.Length; i++)
                worst = Math.Max(worst, Math.Abs(x[i] - x[i - 1]));
            return worst;
        }
        float within = Steepest(left.AsSpan(2400, 9600)), across = Steepest(left.AsSpan(2400));
        Assert.True(across < within * 1.5f, $"steepest step {across} across the seams, {within} within the take");
    }

    [Fact]
    public void AFolderIsTakesPickedByTheSeed()
    {
        string[] Picks()
        {
            var (m, _) = Make(("step", Sample("takes")));
            return [.. Enumerable.Range(0, 12).Select(_ => m.Play("step")!.Takes[0]!)];
        }
        var picks = Picks();
        // Same seeds, same takes: a replayed night sounds the same.
        Assert.Equal(picks, Picks());
        // Different seeds, different takes, all of them in time.
        Assert.Equal(["takes/a.opus", "takes/b.opus", "takes/c.opus"], picks.Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(["takes/a.opus", "takes/b.opus", "takes/c.opus"], new SampleLibrary(Fixtures).Takes("takes"));
    }

    [Fact]
    public void TakesDecodeOnFirstUseOrWhenPreloaded()
    {
        var (m, bank) = Make(("step", Sample("takes")), ("tone", Sample("tone-1k")));
        Assert.Equal(0, bank.Samples.Held.Clips);
        m.Play("tone");
        Assert.Equal((1, ToneSeconds * Audio.SampleRate * sizeof(short)), (bank.Samples.Held.Clips, (double)bank.Samples.Held.Bytes));
        bank.Preload("step");
        Assert.Equal(4, bank.Samples.Held.Clips);
    }

    /// <summary>A worker the test steps by hand, so when a decode lands is up to the test, not the thread pool.</summary>
    sealed class Steps : TaskScheduler
    {
        readonly List<Task> _queued = [];
        public int Queued => _queued.Count;
        protected override IEnumerable<Task> GetScheduledTasks() => _queued;
        protected override void QueueTask(Task task) => _queued.Add(task);
        protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued) => (!previouslyQueued || _queued.Remove(task)) && TryExecuteTask(task);

        public void RunAll()
        {
            foreach (var task in _queued.ToList())
            {
                _queued.Remove(task);
                TryExecuteTask(task);
            }
        }
    }

    [Fact]
    public void ALongTakeDecodesOnAWorkerAndPlaysFromTheTopWhenItArrives()
    {
        // The take as a short one plays it: decoded inline, from the first block.
        var (now, _) = Make(("tone", Sample("tone-1k")));
        var first = now.Play("tone")!;
        var block = new float[Audio.Block * 2];
        var heard = new List<float>();
        while (!first.Finished)
        {
            now.Render(block);
            heard.AddRange(block);
        }

        // Every take counted long: it goes to a worker, and nothing is decoded on the mixing thread.
        var (m, bank) = Make(("tone", Sample("tone-1k")));
        var steps = new Steps();
        bank.Samples.InlineBytes = 0;
        bank.Samples.Scheduler = steps;
        var v = m.Play("tone")!;
        Assert.Equal((0, 1, 1), (bank.Samples.Held.Clips, bank.Samples.Pending, steps.Queued));
        // The pick is the seed's whether the take's decoded or not: a replayed night picks the same.
        Assert.Equal(["tone-1k.opus"], v.Takes);
        Assert.True(v.Waiting);
        // Waiting: silent, and not over (a one-shot mustn't end before its take has played).
        const int Late = 6;
        var wait = new float[Audio.Block * 2 * Late];
        m.Render(wait);
        Assert.All(wait, s => Assert.Equal(0, s));
        Assert.False(v.Finished);

        steps.RunAll();
        var late = new List<float>();
        while (!v.Finished)
        {
            m.Render(block);
            late.AddRange(block);
        }
        Assert.Equal(1, bank.Samples.Held.Clips);
        // As long as the take, from when it arrived: the whole of it, not the end of it.
        Assert.InRange(v.Age - Late * Block, ToneSeconds, ToneSeconds + 2 * Block);
        Assert.Equal(heard.Count, late.Count);
        // The same take from its first sample. (The voice's gain had settled while it waited, where the first one's
        // ramped up as it began, so they agree once the ramp's done.)
        int settled = 40 * Audio.Block * 2;
        double worst = 0;
        for (int i = settled; i < heard.Count; i++)
            worst = Math.Max(worst, Math.Abs(heard[i] - late[i]));
        Assert.True(worst < 1e-4, $"worst difference {worst}");
        Assert.InRange(Pitch(Left([.. late]).AsSpan(480)), 990, 1010);
    }

    [Fact]
    public void ALoopTheGameWillWantDecodesInTheBackgroundAndASettleWaitsForIt()
    {
        var (_, bank) = Make(("step", Sample("takes")));
        var steps = new Steps();
        bank.Samples.Scheduler = steps;
        bank.Prefetch("step");
        Assert.Equal((0, 3), (bank.Samples.Held.Clips, bank.Samples.Pending));
        steps.RunAll();
        bank.Samples.Settle();
        Assert.Equal((3, 0), (bank.Samples.Held.Clips, bank.Samples.Pending));
        // A short take asked for while its prefetch is still under way is waited for, never missed.
        var (m2, bank2) = Make(("tone", Sample("tone-1k")));
        bank2.Samples.Scheduler = new Steps();
        bank2.Prefetch("tone");
        Assert.False(m2.Play("tone")!.Waiting);
    }

    [Fact]
    public void PitchJitterIsPickedOncePerInstanceFromItsSeed()
    {
        // ±6 semitones on the rate: each instance's take lasts 0.3 s over its own factor, within ±√2.
        double[] Lengths()
        {
            var (m, _) = Make(("tone", Sample("tone-1k", pitchJitter: 6)));
            var voices = Enumerable.Range(0, 6).Select(_ => m.Play("tone")!).ToList();
            var ended = new double[voices.Count];
            var block = new float[Audio.Block * 2];
            for (int b = 0; b < Audio.SampleRate / Audio.Block; b++)
            {
                m.Render(block);
                for (int i = 0; i < voices.Count; i++)
                    if (ended[i] == 0 && voices[i].Finished)
                        ended[i] = voices[i].Age;
            }
            return ended;
        }
        var lengths = Lengths();
        Assert.Equal(lengths, Lengths());
        Assert.True(lengths.Distinct().Count() > 3);
        Assert.All(lengths, l => Assert.InRange(l, ToneSeconds / Math.Sqrt(2), ToneSeconds * Math.Sqrt(2) + 2 * Block));
    }

    [Fact]
    public void SynthLayersHoldAOneShotOpenOnlyForAGivenDuration()
    {
        var hum = new LayerDef(SourceKind.Sine, 0.1, 200);
        var (m, _) = Make(("plain", new SoundDef(4, [hum])), ("held", Sample("tone-1k", duration: 0.6, more: hum)),
            ("sweetened", Sample("tone-1k", more: hum)), ("short", Sample("tone-1k", duration: 0.1)));
        // Synth alone: its duration, a second if none is given (as before samples).
        Assert.InRange(PlayOut(m, m.Play("plain")!), 1, 1 + Block);
        // A sample and a synth layer with a duration: the longer of the two.
        Assert.InRange(PlayOut(m, m.Play("held")!), 0.6, 0.6 + Block);
        // ... and without one, the take is the length.
        Assert.InRange(PlayOut(m, m.Play("sweetened")!), ToneSeconds, ToneSeconds + 2 * Block);
        // All samples: the take is the length whatever the duration says.
        Assert.InRange(PlayOut(m, m.Play("short")!), ToneSeconds, ToneSeconds + 2 * Block);
    }

    [Fact]
    public void AVirtualSampleVoiceStillEnds()
    {
        // Out of earshot it's virtual (not rendered), but its take keeps time, or it would hold its instance forever.
        var (m, _) = Make(("tone", Sample("tone-1k") with { MaxDistance = 50 }));
        var v = m.Play("tone", new Double3(0, 0, -400))!;
        double length = PlayOut(m, v);
        Assert.True(v.Virtual);
        Assert.InRange(length, ToneSeconds, ToneSeconds + 2 * Block);
    }

    [Fact]
    public void AMissingSampleIsSilentAndReported()
    {
        var (m, bank) = Make(("ghost", Sample("no/such/folder")));
        Assert.Contains("no/such/folder", bank.LastError);
        var v = m.Play("ghost")!;
        Assert.Equal([null], v.Takes);
        var heard = new float[Audio.Block * 2 * 4];
        m.Render(heard);
        Assert.True(v.Finished);
        Assert.True(Meter.Db(heard) < -100);
        // A sample name can't reach outside the samples root (a mod's sound naming ../../something).
        Assert.Empty(bank.Samples.Takes("../Ballast.Audio.Tests.dll"));
        Assert.Single(bank.Samples.Takes("../Samples/tone-1k"));
        Assert.Empty(bank.Samples.Takes("/etc/passwd"));
    }

    [Fact]
    public void AnUndecodableTakeIsSilentAndReported()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ballast-samples-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "broken.opus"), "not opus");
            var bank = new SoundBank(samples: root);
            bank.Add("broken", Sample("broken"));
            Assert.Null(bank.LastError);
            var m = new Mixer(bank, Mix);
            var v = m.Play("broken")!;
            Assert.Contains("broken.opus", bank.LastError);
            m.Render(new float[Audio.Block * 2]);
            Assert.True(v.Finished);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SoundFilesParseSampleLayers()
    {
        const string json = """
            { "tier": 4,
              "layers": [ { "source": "sample", "sample": "crew-footsteps/walk/wood", "gain": 1, "rate": 1, "pitchJitter": 0.5, "gainJitter": 1.5 },
                          { "source": "sample", "sample": "wheel/flange", "gain": 0.5, "rate": { "param": "speed", "points": [[0, 0.5], [20, 1.5]] },
                            "filters": [ { "type": "highPass", "frequency": 200 } ], "delay": 0.1 } ] }
            """;
        var def = System.Text.Json.JsonSerializer.Deserialize<SoundDef>(json, DataFile.Options)!;
        Assert.Null(def.Duration);
        var step = def.Layers[0];
        Assert.Equal((SourceKind.Sample, "crew-footsteps/walk/wood", 1.0, 0.5, 1.5), (step.Source, step.Sample, step.Rate!.Value.Constant, step.PitchJitter, step.GainJitter));
        var p = new ParamSet();
        p.Set("speed", 10);
        Assert.Equal(1, def.Layers[1].Rate!.Value.Evaluate(p), 6);
    }

    [Fact]
    public void TheSamplesRootIsTheSoundsFoldersSibling()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ballast-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sounds"));
        try
        {
            Assert.Equal(Path.Combine(root, "samples"), new SoundBank(Path.Combine(root, "sounds") + Path.DirectorySeparatorChar).Samples.Root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rewrites an Ogg file's packets onto pages of at most <paramref name="segmentsPerPage"/> lacing values (CRCs left zero: the reader doesn't check them).</summary>
    static byte[] Repage(byte[] ogg, int segmentsPerPage, out int continuedPages)
    {
        var packets = OggOpus.Packets(ogg, out long granule);
        // Each lacing value with its bytes, and whether it ends its packet.
        var segments = new List<(byte Lacing, byte[] Bytes, bool Ends)>();
        foreach (var packet in packets)
            for (int at = 0; ; at += 255)
            {
                int n = Math.Min(255, packet.Length - at);
                segments.Add(((byte)n, packet[at..(at + n)], n < 255));
                if (n < 255)
                    break;
            }
        using var output = new MemoryStream();
        var w = new BinaryWriter(output);
        continuedPages = 0;
        bool inPacket = false;
        for (int page = 0, first = 0; first < segments.Count; page++, first += segmentsPerPage)
        {
            var these = segments.Skip(first).Take(segmentsPerPage).ToList();
            bool last = first + segmentsPerPage >= segments.Count;
            w.Write("OggS"u8);
            w.Write((byte)0);
            w.Write((byte)((inPacket ? 1 : 0) | (page == 0 ? 2 : 0) | (last ? 4 : 0)));
            continuedPages += inPacket ? 1 : 0;
            w.Write(last ? granule : these.Any(s => s.Ends) ? 0L : -1L);
            w.Write(0x1234u);
            w.Write((uint)page);
            w.Write(0u);
            w.Write((byte)these.Count);
            foreach (var s in these)
                w.Write(s.Lacing);
            foreach (var s in these)
                w.Write(s.Bytes);
            inPacket = !these[^1].Ends;
        }
        w.Flush();
        return output.ToArray();
    }
}
