using System.Diagnostics;
using Ballast;
using Ballast.Audio;

namespace Ballast.Audio.Tests;

/// <summary>
/// The speaker fed from a thread of its own (note 667; the director, 13 Oct: "I'm getting audio skips"): a slow frame in the
/// game is no gap at a device playing in real time, the game and the device's thread share the mix without tripping over
/// each other, and a device that still runs dry grows the lead only while the game says so.
/// </summary>
public class AudioPumpTests
{
    static readonly MixDef Mix = new([], DuckAttack: 0.02, DuckRelease: 0.3, MaxVoices: 64, TellOcclusionFloorDb: -6,
        OcclusionDb: -12, OcclusionLowpass: 900, MasterDb: 0);

    /// <summary>
    /// A device playing in real time, as SDL's thread does: what isn't queued when it's due, it plays as silence and
    /// counts as starved.
    /// </summary>
    sealed class Device
    {
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly object _lock = new();
        long _written;
        bool _started;

        public long Starved { get; private set; }
        public long Blocks { get; private set; }

        long Played => (long)(_clock.Elapsed.TotalSeconds * Audio.SampleRate);

        public double Queued()
        {
            lock (_lock)
            {
                CatchUp();
                return (_written - Played) / (double)Audio.SampleRate;
            }
        }

        public void Write(float[] block)
        {
            lock (_lock)
            {
                CatchUp();
                _written += block.Length / 2;
                _started = true;
                Blocks++;
            }
        }

        void CatchUp()
        {
            long played = Played;
            if (played <= _written)
                return;
            if (_started)
                Starved += played - _written;
            _written = played;
        }
    }

    static SoundBank Bank()
    {
        var bank = new SoundBank();
        bank.Add("hum", new SoundDef(5, [new LayerDef(SourceKind.Sine, 0.2, 220)], Loop: true, Flat: true));
        bank.Add("tick", new SoundDef(4, [new LayerDef(SourceKind.Noise, 0.3)], Duration: 0.05, MaxInstances: 8));
        return bank;
    }

    [Fact]
    public void ASlowFrameIsNoGapAtTheDevice()
    {
        var mixer = new Mixer(Bank(), Mix);
        mixer.Play("hum");
        var device = new Device();
        // A 300 ms frame against a 200 ms lead: fed from the frame, the device would play 100 ms of silence each time.
        using (new AudioPump(mixer, device.Queued, device.Write, leadSeconds: 0.2, maxLeadSeconds: 0.2))
        {
            for (int frame = 0; frame < 4; frame++)
            {
                mixer.Play("tick", new Double3(frame, 0, -2));
                Thread.Sleep(300);
            }
        }
        Assert.True(device.Blocks > 0, "nothing reached the device");
        Assert.Equal(0, device.Starved);
    }

    [Fact]
    public void TheGameAndTheDeviceShareTheMixWithoutTrippingOverEachOther()
    {
        var bank = Bank();
        bank.Add("talk", new SoundDef(2, [new LayerDef(SourceKind.Stream, 1)], Loop: true, Flat: true));
        var mixer = new Mixer(bank, Mix);
        var stream = new StreamBuffer(prebufferSamples: 0);
        mixer.Play("talk")!.Stream = stream;
        // A device that always wants more: the pump renders flat out, as hard as the game changes the mix.
        long blocks = 0;
        var speech = new float[480];
        var clock = Stopwatch.StartNew();
        using (new AudioPump(mixer, () => 0, _ => Interlocked.Increment(ref blocks), leadSeconds: 0.05, maxLeadSeconds: 0.05))
        {
            for (int i = 0; clock.Elapsed.TotalSeconds < 0.5; i++)
            {
                // Voices started, stolen (8 at most) and stopped; their parameters moved; the list gone through; speech in.
                var tick = mixer.Play("tick", new Double3(i % 7, 0, -3));
                tick?.Params.Set($"p{i % 13}", i);
                if (i % 5 == 0)
                    tick?.Stop();
                foreach (var v in mixer.Voices)
                    v.Position += new Double3(0.01, 0, 0);
                stream.Write(speech);
            }
        }
        Assert.True(Interlocked.Read(ref blocks) > 10, "the pump hardly ran");
    }

    [Fact]
    public void ADeviceThatRunsDryGrowsTheLeadOnlyWhileAdapting()
    {
        var mixer = new Mixer(Bank(), Mix);
        // A device that's always empty: every look at it is a gap.
        using (var held = new AudioPump(mixer, () => 0, _ => { }, leadSeconds: 0.06, maxLeadSeconds: 0.15))
        {
            SpinWait.SpinUntil(() => held.Underruns >= 10, TimeSpan.FromSeconds(5));
            Assert.Equal(0.06, held.Lead, 6);
        }
        using var adapting = new AudioPump(mixer, () => 0, _ => { }, leadSeconds: 0.06, maxLeadSeconds: 0.15) { Adapt = true };
        // Counted from when it's adapting: it was already running dry before.
        int from = adapting.Underruns;
        SpinWait.SpinUntil(() => adapting.Underruns >= from + 10, TimeSpan.FromSeconds(5));
        // 20 ms a gap, and no further than the most.
        Assert.Equal(0.15, adapting.Lead, 6);
    }
}
