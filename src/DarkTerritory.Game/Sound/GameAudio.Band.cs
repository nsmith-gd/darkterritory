using System.Collections.Concurrent;
using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

// The band (ARCHITECTURE §8 note 700): every crewmate playing heard where they stand, each their own instrument's part of the
// tune the host named, in time on the host's clock, coming in on the next bar after they start.
public sealed partial class GameAudio
{
    /// <summary>
    /// The host's tick as this machine has it (<see cref="IPlaySession.HostTick"/>), set by the caller before each
    /// <see cref="Update"/>: the band's clock. A client's own world counts its ticks from when it joined; null where they're one
    /// (the host, a bench), and the world's tick is used.
    /// </summary>
    public long? HostTick { get; set; }

    /// <summary>How far ahead of the clock a note is put down (it starts a block after, the same delay for every note).</summary>
    const double BandLead = SimConstants.TickSeconds;

    BandState _bandWas = BandState.Silent;
    // Where the band's got to in its tune (s), run on this machine's ticks and kept to the host's; how far notes are down.
    double _bandNow = double.NaN, _bandDown;
    // Each player playing, and the bar they came in on.
    readonly Dictionary<int, int> _bandIn = [];
    readonly Dictionary<(int Tune, int Instrument), BandNote[]> _bandParts = [];
    readonly ConcurrentDictionary<(int Instrument, int Pitch, int Ms, int Velocity), Lazy<AudioClip>> _bandClips = [];

    /// <summary>The band's tune and where it's got to (s), as heard here; null when it's quiet. The HUD's line, the bench's check.</summary>
    public (BandScore Score, double Seconds)? BandHeard { get; private set; }

    void Band(World world, TrainOnLine train, double dt)
    {
        if (world.BandTuning is not { } tuning || !world.Band.Playing || world.Band.Tune >= tuning.Scores.Count)
        {
            EndNightBand();
            return;
        }
        var band = world.Band;
        var score = tuning.Scores[band.Tune];
        double host = ((HostTick ?? world.Tick) - (long)band.StartTick) * SimConstants.TickSeconds;
        if (band != _bandWas || double.IsNaN(_bandNow) || Math.Abs(host - _bandNow) > 0.25)
        {
            // A tune starting (or this machine finding it already going): everyone comes in again on its next bar.
            if (band != _bandWas)
                _bandIn.Clear();
            _bandWas = band;
            _bandNow = host;
            _bandDown = host;
        }
        else
            // On by this machine's tick, eased towards the host's as its snapshots come in.
            _bandNow += dt + (host - _bandNow - dt) * 0.1;
        BandHeard = _bandNow >= 0 && _bandNow < score.Seconds ? (score, _bandNow) : null;
        double from = _bandDown, to = _bandNow + BandLead;
        _bandDown = Math.Max(_bandDown, to);
        var playing = new HashSet<int>();
        foreach (var (id, s) in CrewStates)
        {
            if (!s.Alive || !s.Has(PlayerFlags.Playing) || world.Bodies.CarriedBy(id) is not { Kind: Sim.Physics.BodyKind.Instrument } held
                || held.Instrument >= tuning.Instruments.Length)
                continue;
            playing.Add(id);
            var instrument = tuning.Instruments[held.Instrument];
            if (!_bandIn.TryGetValue(id, out int bar))
            {
                // In on the next bar (or this one, caught at its very start).
                _bandIn[id] = bar = (int)Math.Ceiling(Math.Max(0, _bandNow) / score.BarSeconds - 0.05);
                Warm(tuning, band.Tune, held.Instrument);
            }
            if (to <= from)
                continue;
            var at = PlayerMotor.WorldPosition(s, train) + Double3.Up * 1.2;
            float occlusion = Occlusion(PlayerMotor.Space(s, train));
            foreach (var n in Part(tuning, band.Tune, held.Instrument))
            {
                double when = n.At * score.EighthSeconds;
                if (when < from || when >= to || when < bar * score.BarSeconds)
                    continue;
                var clip = BandClip(tuning, held.Instrument, n, score.EighthSeconds);
                string sound = BandSound(instrument.Id);
                Mixer.Play(sound, at)?.Also(v =>
                {
                    v.Clip = clip;
                    // Put down ahead of the clock by the lead, and as far into it as the note falls.
                    v.ClipSeconds = -(when - _bandNow);
                    v.Occlusion = occlusion;
                });
            }
        }
        foreach (int gone in _bandIn.Keys.Where(k => !playing.Contains(k)).ToList())
            _bandIn.Remove(gone);
    }

    BandNote[] Part(BandTuning tuning, int tune, int instrument)
    {
        if (!_bandParts.TryGetValue((tune, instrument), out var part))
            _bandParts[(tune, instrument)] = part = [.. tuning.Scores[tune].Part(tuning.Instruments[instrument]).OrderBy(n => n.At)];
        return part;
    }

    AudioClip BandClip(BandTuning tuning, int instrument, BandNote n, double eighth)
    {
        var key = (instrument, n.Pitch, (int)Math.Round(n.Length * eighth * 100) * 10, (int)Math.Round(n.Velocity * 20));
        string id = tuning.Instruments[instrument].Id;
        return _bandClips.GetOrAdd(key, k => new Lazy<AudioClip>(() =>
            new AudioClip(BandSynth.Render(id, k.Pitch, k.Ms / 1000.0, k.Velocity / 20.0), Audio.SampleRate))).Value;
    }

    /// <summary>A player's just started: their part's notes made off the frame loop, ahead of their bar.</summary>
    void Warm(BandTuning tuning, int tune, int instrument)
    {
        var notes = Part(tuning, tune, instrument);
        double eighth = tuning.Scores[tune].EighthSeconds;
        _ = Task.Run(() =>
        {
            foreach (var n in notes.DistinctBy(n => (n.Pitch, Math.Round(n.Length * eighth * 100), Math.Round(n.Velocity * 20))))
                BandClip(tuning, instrument, n, eighth);
        });
    }

    /// <summary>
    /// Each instrument's sound (tier 4, a player's action, as the noisy toys): its own name so the meter has it alone, and its
    /// level against the others (the drums under the tune, the whistle a touch under the fiddle).
    /// </summary>
    string BandSound(string instrument)
    {
        string name = "band-" + instrument;
        if (Bank.Get(name) is null)
            Bank.Add(name, new SoundDef(4, [new LayerDef(SourceKind.Sample, 1)],
                // A melody's note rings into the next and no further; a strum's strings and its bass ring together.
                MaxInstances: instrument == "guitar" ? 6 : 3, MinDistance: 2, MaxDistance: 40,
                // Balanced by `dt audio render --scenario band` (a band round the listener on the roof): the tune's three within
                // a dB or two, the whistle over them by its pitch not its level, the banjo cutting, the guitar and drums under.
                GainDb: instrument switch
                {
                    "fiddle" or "accordion" => -6,
                    "whistle" => -12,
                    "banjo" => 1,
                    "guitar" => -5,
                    "bodhran" => -3,
                    "spoons" => 6,
                    "uglyStick" => 4,
                    _ => -6,
                }));
        return name;
    }

    void EndNightBand()
    {
        _bandIn.Clear();
        _bandNow = double.NaN;
        _bandWas = BandState.Silent;
        BandHeard = null;
    }
}
