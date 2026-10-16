using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

// The whimsies' sounds (ARCHITECTURE §8 note 715): the canary singing in car one (and, the tell, not), the calliope's notes on
// the whistle's pulls, the gramophone in the guard van playing the band's recordings through its horn.
public sealed partial class GameAudio
{
    /// <summary>
    /// AU1's calliope (queue #534, note 806): the pitch (MIDI) its takes are recorded at, C5, the middle of the tune played an
    /// octave down (tools/audio/recipes/upgrade_whimsy.py). An octave down because the calliope is the whistle, and the
    /// whistle's band is 200-800 Hz (spec A.4; AudioBench.TellBands): at the tune's own octave (G5-G6) it would sit in Tippy's,
    /// the Stoker's and the Grumbler's.
    /// </summary>
    const int CalliopeRecorded = 72, CalliopeDown = 12;
    SoundInstance? _canary, _gramophone;
    uint _woundHeard = uint.MaxValue;
    double _canaryNext;
    int _canaryPhrase, _calliopeNote = -1, _gramophoneRecord = -1;
    readonly Dictionary<int, AudioClip> _calliopeClips = [];
    readonly Dictionary<int, AudioClip> _canaryClips = [];
    Task<AudioClip>? _record;
    int _recordOf = -1;

    void Whimsies(World world, TrainOnLine train)
    {
        Canary(world, train);
        Gramophone(world, train);
        Pets(world, train);
    }

    // Each pet's mood when last heard, and when it last made a sound (this machine's clock).
    readonly Dictionary<int, (sbyte Mood, double At)> _petHeard = [];
    readonly Dictionary<string, AudioClip> _petClips = [];

    /// <summary>
    /// The yard cat and the dog (note 745): the tells. Warning of what's aboard, the cat hisses (again every few seconds while it
    /// lasts) and the dog barks, a bark every ~0.6 s; otherwise the cat meows now and then.
    /// </summary>
    void Pets(World world, TrainOnLine train)
    {
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind is not (Sim.Physics.BodyKind.Cat or Sim.Physics.BodyKind.Dog))
                continue;
            bool cat = b.Kind == Sim.Physics.BodyKind.Cat;
            var mood = (PetMood)b.Tipped;
            var was = _petHeard.GetValueOrDefault(b.Id, ((sbyte)-1, -100));
            double every = mood == PetMood.Warn ? (cat ? 2.5 : 0.6) : cat ? 23 : double.MaxValue;
            bool fresh = mood == PetMood.Warn && was.Mood != (sbyte)PetMood.Warn;
            if (!fresh && _time - was.At < every || mood is PetMood.Sleep || !cat && mood != PetMood.Warn)
            {
                _petHeard[b.Id] = (b.Tipped, was.At);
                continue;
            }
            int take = (int)(_time * 7) % 3;
            string name = cat ? mood == PetMood.Warn ? "hiss" : "meow" : "bark";
            var at = Sim.Physics.Bodies.WorldCentre(b, train) + Double3.Up * 0.2;
            float occ = Occlusion(PlayerMotor.Space(new PlayerState { Parent = b.Parent, Position = b.Centre }, train));
            _petHeard[b.Id] = (b.Tipped, _time);
            // AU1's recorded cat and dog where they're installed (queue #534, note 806); the made sound otherwise.
            if (Cue($"up-pets.{name}", at, occ) is not null)
                continue;
            if (!_petClips.TryGetValue($"{name}{take}", out var clip))
                _petClips[$"{name}{take}"] = clip = new AudioClip(name switch
                {
                    "hiss" => BandSynth.Hiss(take),
                    "meow" => BandSynth.Meow(take),
                    _ => BandSynth.Bark(take),
                }, Audio.SampleRate);
            Mixer.Play(WhimsySound($"whimsy-{name}", 3, 1.5, cat ? 22 : 60, cat ? -2 : 0), at)?.Also(v =>
            {
                v.Clip = clip;
                v.Occlusion = occ;
            });
        }
    }

    /// <summary>
    /// The canary (note 715): a phrase every few seconds from its cage while all's well; nothing while something's aboard near
    /// it, or once it's dead. The silence is the tell.
    /// </summary>
    void Canary(World world, TrainOnLine train)
    {
        var composition = train.Dynamics.Tuning.Composition;
        if (!composition.Dressed("canary") || world.Whimsy.Canary != CanaryMood.Singing || train.Frames.Count < 2)
        {
            _canary?.Stop();
            _canary = null;
            return;
        }
        int car = Whimsy.CanaryCar(train);
        var at = train.Frames[car].ToWorld(Whimsy.Cage(train) + new Double3(0, -0.3, 0));
        if (_canary is { Finished: false })
        {
            _canary.Position = at;
            return;
        }
        // A recorded phrase's length isn't known ahead: the pause is counted from when it ends.
        if (double.IsPositiveInfinity(_canaryNext))
            _canaryNext = _time + 1.5 + _canaryPhrase % 3;
        if (_time < _canaryNext)
            return;
        int phrase = _canaryPhrase++ % 6;
        float occ = Occlusion(PlayerMotor.Space(new PlayerState { Parent = car, Position = Whimsy.Cage(train) }, train));
        // AU1's recorded canary where it's installed (queue #534, note 806); the made song otherwise.
        if (HasCue("up-whimsy.song"))
        {
            _canary = Cue("up-whimsy.song", at, occ);
            _canaryNext = _canary is null ? _time + 3 : double.PositiveInfinity;
            return;
        }
        if (!_canaryClips.TryGetValue(phrase, out var clip))
            _canaryClips[phrase] = clip = new AudioClip(BandSynth.Canary(phrase), Audio.SampleRate);
        _canary = Mixer.Play(WhimsySound("whimsy-canary", 4, 1, 18, -8), at)?.Also(v =>
        {
            v.Clip = clip;
            v.Occlusion = occ;
        });
        _canaryNext = _time + clip.Seconds + 1.5 + phrase % 3;
    }

    /// <summary>
    /// The calliope on the whistle's pull (note 715): the tune's next note while the whistle blows, the pull's count the host's
    /// (<see cref="WhimsyState.CalliopeNote"/>), so every machine hears the same note. True when it played instead of the whistle.
    /// </summary>
    bool Calliope(World world, TrainOnLine train)
    {
        if (!train.Dynamics.Tuning.Composition.Dressed("calliope"))
            return false;
        var engine = train.Frames[0];
        var at = engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, -2));
        if (world.WhistleSeconds <= 0)
        {
            _whistle?.Stop();
            _whistle = null;
            _calliopeNote = -1;
            return true;
        }
        int note = world.Whimsy.CalliopeNote;
        if (_whistle is null || note != _calliopeNote)
        {
            _whistle?.Stop();
            _calliopeNote = note;
            int pitch = BandSynth.CalliopeTune[((note - 1) % BandSynth.CalliopeTune.Length + BandSynth.CalliopeTune.Length) % BandSynth.CalliopeTune.Length];
            // AU1's recorded pipe where it's installed (queue #534, note 806): one note, C5, played at the tune's pitch an
            // octave down.
            if (HasCue("up-whimsy.calliope"))
                _whistle = Cue("up-whimsy.calliope", at)?.Also(v => v.Params.Set("pitch", Math.Pow(2, (pitch - CalliopeDown - CalliopeRecorded) / 12.0)));
            else
            {
                if (!_calliopeClips.TryGetValue(pitch, out var clip))
                    _calliopeClips[pitch] = clip = new AudioClip(BandSynth.Calliope(pitch, 2.5), Audio.SampleRate);
                var whistle = Bank.Get("train-whistle");
                _whistle = Mixer.Play(WhimsySound("whimsy-calliope", whistle?.Tier ?? 1, whistle?.MinDistance ?? 10, whistle?.MaxDistance ?? 900, whistle?.GainDb ?? 0), at)
                    ?.Also(v => v.Clip = clip);
            }
        }
        if (_whistle is not null)
        {
            _whistle.Position = at;
            _whistle.Occlusion = Occlusion(PlayerMotor.Outside);
        }
        return true;
    }

    /// <summary>
    /// The gramophone (note 715): while it plays, its record (the band's tune the host wound it to, every part on the band's
    /// recordings) through its horn: the top and the bottom gone, a needle's crackle, from where the host's clock has it.
    /// </summary>
    void Gramophone(World world, TrainOnLine train)
    {
        var w = world.Whimsy;
        // Wound (queue #534, note 806): the crank's ratchet as it's wound and set going, once a winding (the host's tick of
        // it); what's there on the first update is old news.
        if (w.Playing && w.WoundAt != _woundHeard && train.Frames.Count >= 2)
        {
            if (_woundHeard != uint.MaxValue)
            {
                int guard = Whimsy.GramophoneCar(train);
                var table = Whimsy.Gramophone(train.Frames[guard].Shape);
                Cue("up-whimsy.wind", train.Frames[guard].ToWorld(table + new Double3(0.16, Whimsy.Table + 0.3, 0.06)),
                    Occlusion(PlayerMotor.Space(new PlayerState { Parent = guard, Position = table + Double3.Up }, train)));
            }
            _woundHeard = w.WoundAt;
        }
        else if (_woundHeard == uint.MaxValue)
            _woundHeard = w.WoundAt;
        if (!train.Dynamics.Tuning.Composition.Dressed("gramophone") || !w.Playing || world.BandTuning is not { } band || w.Record >= band.Scores.Count
            || train.Frames.Count < 2)
        {
            _gramophone?.Stop();
            _gramophone = null;
            _gramophoneRecord = -1;
            return;
        }
        if (_recordOf != w.Record)
        {
            _recordOf = w.Record;
            int record = w.Record;
            _record = Task.Run(() => Record(band, record));
        }
        if (_record is not { IsCompletedSuccessfully: true } ready)
            return;
        var clip = ready.Result;
        int car = Whimsy.GramophoneCar(train);
        var van = train.Frames[car];
        var foot = Whimsy.Gramophone(van.Shape);
        var at = van.ToWorld(foot + new Double3(0.16, Whimsy.Table + 0.54, 0.06));
        double into = ((HostTick ?? world.Tick) - (long)w.WoundAt) * SimConstants.TickSeconds;
        if (_gramophone is { Finished: true } || _gramophoneRecord != w.Record + (int)(w.WoundAt % 100000) * 64)
        {
            _gramophone?.Stop();
            _gramophoneRecord = w.Record + (int)(w.WoundAt % 100000) * 64;
            _gramophone = Mixer.Play(WhimsySound("whimsy-gramophone", 4, 2, 30, -2), at)?.Also(v =>
            {
                v.Clip = clip;
                v.ClipSeconds = into % clip.Seconds;
            });
        }
        if (_gramophone is not null)
        {
            _gramophone.Position = at;
            _gramophone.Occlusion = Occlusion(PlayerMotor.Space(new PlayerState { Parent = car, Position = foot + Double3.Up }, train));
        }
    }

    /// <summary>A record (note 715): every instrument's part of tune <paramref name="tune"/> mixed, then the gramophone's horn and needle.</summary>
    AudioClip Record(BandTuning band, int tune)
    {
        var score = band.Scores[tune];
        var mix = new float[(int)((score.Seconds + 2) * Audio.SampleRate)];
        for (int i = 0; i < band.Instruments.Length; i++)
        {
            float level = (float)Math.Pow(10, band.Instruments[i].LevelDb / 20);
            foreach (var n in score.Part(band.Instruments[i]))
            {
                var note = BandClip(band, i, n, score.EighthSeconds).Samples;
                int from = (int)(n.At * score.EighthSeconds * Audio.SampleRate);
                for (int s = 0; s < note.Length && from + s < mix.Length; s++)
                    mix[from + s] += note[s] * level;
            }
        }
        var low = new Biquad();
        low.Set(FilterType.HighPass, 380, 0.9);
        var high = new Biquad();
        high.Set(FilterType.LowPass, 3200, 0.9);
        var horn = new Biquad();
        horn.Set(FilterType.Peak, 1300, 1.2, 6);
        var noise = new Noise((uint)(tune * 31 + 7));
        float peak = 1e-6f;
        for (int i = 0; i < mix.Length; i++)
        {
            float x = horn.Process(high.Process(low.Process(mix[i])));
            // The needle: a soft hiss, and a tick now and then.
            float n = noise.Next();
            x += n * 0.012f + (Math.Abs(n) > 0.9993f ? n * 0.4f : 0);
            mix[i] = x;
            peak = Math.Max(peak, Math.Abs(x));
        }
        for (int i = 0; i < mix.Length; i++)
            mix[i] *= 0.7f / peak;
        return new AudioClip(mix, Audio.SampleRate);
    }

    /// <summary>A whimsy's own sound (its own name for the meter): a clip voice at its tier and reach.</summary>
    string WhimsySound(string name, int tier, double near, double far, double gainDb)
    {
        if (Bank.Get(name) is null)
            Bank.Add(name, new SoundDef(tier, [new LayerDef(SourceKind.Sample, 1)], MaxInstances: 1, MinDistance: near, MaxDistance: far, GainDb: gainDb));
        return name;
    }
}
