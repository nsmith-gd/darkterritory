using Ballast;

namespace Ballast.Audio;

/// <summary>
/// One playing sound: synthesises (or plays back) its layers block by block from the definition and live parameters.
/// Position is in world space (double, like everything else 40 km down the line); the mixer spatialises it.
/// </summary>
public sealed class SoundInstance
{
    readonly LayerState[] _layers;
    readonly float[] _scratch = new float[Audio.Block];
    readonly bool _hasSamples, _hasSynth;
    double _crushPhase;
    float _crushHeld;

    /// <param name="samples">Where sample layers find their takes; without it they're silent.</param>
    internal SoundInstance(int id, string name, SoundDef def, uint seed, SampleLibrary? samples = null)
    {
        Id = id;
        Name = name;
        Def = def;
        _layers = new LayerState[def.Layers.Length];
        var takes = new string?[_layers.Length];
        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i] = new LayerState(def.Layers[i], seed * 7919u + (uint)i * 104729u + 1, samples);
            takes[i] = _layers[i].TakeName;
            if (def.Layers[i].Source == SourceKind.Sample)
                _hasSamples = true;
            else
                _hasSynth = true;
        }
        Takes = takes;
    }

    /// <summary>
    /// The take each sample layer picked (its path under the samples root), from the seed alone, so the same whether it was
    /// decoded at once or is still on a worker; null for other layers, or a sample that isn't there.
    /// </summary>
    public IReadOnlyList<string?> Takes { get; }

    /// <summary>A sample layer is still waiting for its take to decode (<see cref="SampleLibrary.Request"/>).</summary>
    public bool Waiting => _layers.Any(l => l.Waiting);

    /// <summary>Samples for <see cref="SourceKind.Stream"/> layers; set for voice chat.</summary>
    public StreamBuffer? Stream { get; set; }
    readonly float[] _streamBlock = new float[Audio.Block];

    /// <summary>
    /// How fast it plays, 1 as made: below 1 everything in it (pitch, envelopes, tremolo, takes) runs slower and lower, as a
    /// tape does. The mixer sets it (GDD v1.4 App. E.6: the game at half speed under the opera); a stream or a clip keeps its own.
    /// </summary>
    public double Rate { get; set; } = 1;

    /// <summary>The recording a <see cref="SourceKind.Sample"/> layer plays (App. E.6's music).</summary>
    public AudioClip? Clip { get; set; }
    /// <summary>Where in <see cref="Clip"/> the next block starts, in the clip's seconds: set it to start from an in-point.</summary>
    public double ClipSeconds { get; set; }

    public int Id { get; }
    public string Name { get; }
    public SoundDef Def { get; internal set; }
    public ParamSet Params { get; } = new();
    public Double3 Position { get; set; }
    /// <summary>0 = clear line to the listener, 1 = fully behind a car wall (spec A.5: −12 dB, 900 Hz lowpass).</summary>
    public float Occlusion { get; set; }
    /// <summary>
    /// What the game's geometry found between the ear and this voice (the walls in the way), 0..1, set each frame. The mixer
    /// hears the greater of it and <see cref="Occlusion"/>, so a caller's own judgement is never undone.
    /// </summary>
    public float Walls { get; set; }
    /// <summary>Extra gain the game applies (e.g. a clinger's drill louder as it works through).</summary>
    public float Volume { get; set; } = 1;
    public double Age { get; private set; }
    public bool Stopped { get; private set; }

    /// <summary>
    /// A loop plays until it's stopped. A one-shot is over when its length has passed. Synth layers have no length of
    /// their own, so a sound of only those lasts <see cref="SoundDef.Duration"/> (1 s if not given). A sample layer lasts
    /// as long as its take at its rate, after its delay, and after however long its take took to decode, if it was long
    /// enough to go to a worker (it starts from the top when it arrives). With any sample layers, the sound is over once they all have
    /// played out, and not before its duration only if it has synth layers too and gives one. An all-sample one-shot
    /// ignores the duration: its takes are its length, so a 0.2 s footstep frees its voice at 0.2 s (not after 0.8 s of
    /// silence holding a voice and an instance), and a 3 s take isn't cut off at a default second. A sound playing its
    /// <see cref="Clip"/> (the opera) is over when the clip is.
    /// </summary>
    public bool Finished => Stopped || !Def.Loop && Ended;

    bool Ended
    {
        get
        {
            if (Clip is { } clip)
                return ClipSeconds >= clip.Seconds;
            if (!_hasSamples)
                return Age >= (Def.Duration ?? 1);
            foreach (var layer in _layers)
                if (!layer.PlayedOut)
                    return false;
            return !_hasSynth || Def.Duration is not { } duration || Age >= duration;
        }
    }

    /// <summary>Configures a voice inline: <c>mixer.Play(...)?.Also(v => v.Occlusion = 1)</c>.</summary>
    public SoundInstance Also(Action<SoundInstance> configure)
    {
        configure(this);
        return this;
    }

    /// <summary>Stops a loop (or cuts a one-shot short) at the end of this block.</summary>
    public void Stop() => Stopped = true;

    // Mixer-side state.
    internal Smoothed LeftGain, RightGain;
    internal Biquad OcclusionFilter;
    // Through the listener's head (MixDef.Head): made the first block the voice is heard positioned.
    internal HeadState? Head;
    // The formant shift (SoundDef.Formant), made the first block it renders.
    FormantShifter? _formant;
    // The music bus's low-pass on the rest of the game (App. E.6), two stages for a clear muffle.
    internal Biquad GameFilterA, GameFilterB;
    internal float LastAudibleGain;
    // The listener's space: shut out by it (ramped, not cut), and a voice's compressor (its level at the ear in dB, and the
    // gain it's giving, makeup included).
    internal float SpaceGain = 1;
    internal float CompressorEnvelopeDb = -120, CompressorGain = 1;
    internal SpaceDef? MutedFor;
    internal bool Muted;
    /// <summary>Past the voice budget last block: keeping time, not rendered.</summary>
    public bool Virtual { get; internal set; }

    /// <summary>Synthesises the next block, mono, into <paramref name="output"/> (overwritten).</summary>
    internal void Render(Span<float> output)
    {
        output.Clear();
        // One read per block, shared by every stream layer (a radio is the stream plus its static).
        if (Stream is not null)
            Stream.Read(_streamBlock.AsSpan(0, output.Length));
        else if (Clip is not null)
            ReadClip(output.Length);
        double rate = Stream is null && Clip is null ? Rate : 1;
        foreach (var layer in _layers)
            layer.Render(output, _scratch, Params, Age, Def.CycleSeconds, _streamBlock, Def.Loop, rate);
        if (Def.Formant is { } formant)
            (_formant ??= new FormantShifter(formant)).Process(output);
        if (Def.Crush is { } crush)
            Crush(output, crush);
        Age += rate * output.Length / Audio.SampleRate;
    }

    /// <summary>The clip's next block at the mixer's rate, into the stream block (sample layers read it there).</summary>
    void ReadClip(int count)
    {
        var clip = Clip!;
        double step = (double)clip.SampleRate / Audio.SampleRate, at = ClipSeconds * clip.SampleRate;
        for (int i = 0; i < count; i++)
        {
            if (Def.Loop && at >= clip.Samples.Length)
                at -= clip.Samples.Length;
            _streamBlock[i] = clip.At(at);
            at += step;
        }
        ClipSeconds += (double)count / Audio.SampleRate;
        if (Def.Loop && ClipSeconds >= clip.Seconds)
            ClipSeconds -= clip.Seconds;
    }

    /// <summary>Advances time without producing sound (a virtualised voice keeps its place).</summary>
    internal void Skip(int samples)
    {
        double rate = Stream is null && Clip is null ? Rate : 1;
        foreach (var layer in _layers)
            layer.Skip((int)Math.Round(samples * rate), Params, Age, Def.CycleSeconds, Def.Loop);
        Age += rate * samples / Audio.SampleRate;
        if (Clip is not null)
            ClipSeconds += (double)samples / Audio.SampleRate;
        // A virtual stream still consumes, or it would play stale speech when it comes back.
        Stream?.Read(_streamBlock.AsSpan(0, Math.Min(samples, _streamBlock.Length)));
    }

    void Crush(Span<float> buffer, CrushDef crush)
    {
        float levels = (1 << Math.Clamp(crush.Bits, 2, 24) - 1);
        double step = (double)crush.Rate / Audio.SampleRate;
        for (int i = 0; i < buffer.Length; i++)
        {
            _crushPhase += step;
            if (_crushPhase >= 1)
            {
                _crushPhase -= 1;
                _crushHeld = MathF.Round(buffer[i] * levels) / levels;
            }
            buffer[i] = _crushHeld;
        }
    }

    sealed class LayerState
    {
        readonly LayerDef def;
        readonly SampleLibrary? samples;
        Noise _noise;
        readonly Biquad[] _filters;
        // An impulse fires on the first sample, so a one-shot clunk lands when it's played.
        double _phase, _tremoloPhase, _vibratoPhase;
        double _tremoloRate, _gateJitter, _filterRate = 1;
        bool _filtersSet;
        // Picked once from the seed, each with its own hash of it: drawing them doesn't move a noise layer's sequence,
        // so a sound without jitter or takes renders exactly as it did before they existed.
        readonly double _pitch;
        readonly float _gain;
        // A sample layer's take (null until it's decoded), and where it's up to in it (in the take's samples).
        SampleClip? _take;
        double _position;
        bool _playedOut;
        // Seconds the layer waited for its take after it was due: its own clock runs that much behind the sound's, so it
        // plays the take from the top, exactly as if it had been decoded in time, only later.
        double _late;

        public LayerState(LayerDef def, uint seed, SampleLibrary? samples)
        {
            this.def = def;
            this.samples = samples;
            _noise = new Noise(seed);
            _filters = new Biquad[def.Filters?.Length ?? 0];
            _phase = def.Source == SourceKind.Impulse ? 1 - 1e-9 : 0;
            _pitch = Math.Pow(2, def.PitchJitter * Uniform(seed, 1) / 12);
            _gain = Audio.DbToGain(def.GainJitter * Uniform(seed, 2));
            TakeName = def.Source == SourceKind.Sample ? Pick(def.Sample, samples, Hash(seed ^ 0x5BD1E995u)) : null;
            // Asked for at once: a short take decodes now, a long one starts on a worker while the sound's delay runs.
            Resolve();
        }

        /// <summary>The take this layer picked (by the seed), decoded yet or not.</summary>
        public string? TakeName { get; }

        /// <summary>Its take is still decoding on a worker.</summary>
        public bool Waiting => TakeName is not null && _take is null;

        /// <summary>A one-shot's take has played to its end (or there's nothing to play); always true for other layers.</summary>
        public bool PlayedOut => !Waiting && (_take is not { Length: > 0 } || _playedOut);

        /// <summary>Picks up the take if it's arrived; true while it's still on its way.</summary>
        bool Resolve()
        {
            if (Waiting)
                _take = samples!.Request(TakeName!);
            return Waiting;
        }

        /// <param name="cycleSeconds">If positive, envelopes repeat on this period (a loop's breathing, a pack's howls).</param>
        /// <param name="loop">The sound loops, so a take wraps round instead of ending.</param>
        /// <param name="rate">How fast it plays (<see cref="SoundInstance.Rate"/>): its time, pitch and filters all scaled.</param>
        public void Render(Span<float> output, float[] scratch, ParamSet p, double age, double cycleSeconds, float[] stream, bool loop, double rate = 1)
        {
            double t = age - def.Delay - _late;
            if (t < 0)
                return;
            if (Resolve())
            {
                // Due, but the take's still decoding: silence, and the take starts that much later.
                _late += rate * output.Length / Audio.SampleRate;
                return;
            }
            // A take played out is silence; a sample layer naming no take plays the instance's clip (the stream block).
            if (def.Source == SourceKind.Sample && def.Sample is not null && PlayedOut)
                return;
            double EnvelopeTime(double at) => cycleSeconds > 0 ? at % cycleSeconds : at;
            var buffer = scratch.AsSpan(0, output.Length);
            double gain = def.Gain.Evaluate(p) * _gain;
            // Hz for an oscillator; for a sample, the playback rate.
            double frequency = Pitch(p, EnvelopeTime(t));
            if (gain <= 0)
            {
                // Silent this block, but a take plays on: a one-shot still has to reach its end, a loop to keep its place.
                Advance(frequency * buffer.Length * rate, loop);
                return;
            }

            // Filters retune per block: a filter frequency on a curve follows its parameter, and all of them the rate (a
            // slowed sound's resonances go down with it).
            if (def.Filters is { } filters)
                for (int f = 0; f < filters.Length; f++)
                {
                    var fd = filters[f];
                    double ff = fd.Frequency.Evaluate(p) * rate;
                    if (!_filtersSet || fd.Frequency.Param is not null || rate != _filterRate)
                        _filters[f].Set(fd.Type, ff, fd.Q, fd.GainDb);
                }
            _filtersSet = true;
            _filterRate = rate;

            double dt = rate / Audio.SampleRate;
            double vibRate = def.Vibrato?.Rate.Evaluate(p) ?? 0, vibDepth = def.Vibrato?.Depth ?? 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                double f = frequency;
                if (vibDepth > 0)
                {
                    _vibratoPhase += vibRate * dt;
                    f *= 1 + vibDepth * Math.Sin(2 * Math.PI * _vibratoPhase);
                }
                if (_take is not null)
                {
                    buffer[i] = Read(_take, f * rate, loop);
                    continue;
                }
                double prev = _phase;
                _phase += f * dt;
                if (_phase >= 1)
                    _phase -= Math.Floor(_phase);
                buffer[i] = def.Source switch
                {
                    SourceKind.Noise => _noise.Next(),
                    SourceKind.Sine => (float)Math.Sin(2 * Math.PI * _phase),
                    SourceKind.Saw => (float)(2 * _phase - 1),
                    SourceKind.Square => _phase < 0.5 ? 1f : -1f,
                    SourceKind.Stream or SourceKind.Sample => stream[i],
                    // A click when the phase wraps, with a little noise so repeats aren't identical.
                    SourceKind.Impulse => _phase < prev ? 1f + 0.3f * _noise.Next() : 0f,
                    _ => 0f,
                };
            }
            foreach (ref var filter in _filters.AsSpan())
                filter.Process(buffer);

            var tremolo = def.Tremolo;
            double tremRate = tremolo?.Rate.Evaluate(p) ?? 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                double amp = gain;
                if (def.Envelope is { } env)
                    amp *= Breakpoints(env, EnvelopeTime(t + i * dt));
                if (tremolo is not null)
                    amp *= Modulate(tremolo, tremRate, dt);
                output[i] += (float)(buffer[i] * amp);
            }
        }

        /// <summary>Keeps a take's place while the voice is virtual: it isn't heard, but a one-shot still has to end.</summary>
        public void Skip(int samples, ParamSet p, double age, double cycleSeconds, bool loop)
        {
            double t = age - def.Delay - _late;
            if (t < 0)
                return;
            if (Resolve())
                _late += (double)samples / Audio.SampleRate;
            else if (_take is not null)
                Advance(Pitch(p, cycleSeconds > 0 ? t % cycleSeconds : t) * samples, loop);
        }

        /// <summary>An oscillator's frequency, or a sample's playback rate (1 = as recorded), with the jitter and pitch envelope on it.</summary>
        double Pitch(ParamSet p, double envelopeTime)
        {
            double f = (def.Source == SourceKind.Sample ? def.Rate ?? 1 : def.Frequency ?? 440).Evaluate(p) * _pitch;
            if (def.PitchEnvelope is { } pe)
                f *= Breakpoints(pe, envelopeTime);
            return f;
        }

        /// <summary>
        /// The take at the read position, linearly interpolated, then steps on by <paramref name="rate"/>. A loop's take
        /// wraps without a seam (the sample after its last is its first); a one-shot's last sample fades to nothing.
        /// </summary>
        float Read(SampleClip take, double rate, bool loop)
        {
            if (_playedOut)
                return 0;
            var pcm = take.Pcm;
            int i0 = (int)_position, i1 = i0 + 1;
            float a = pcm[i0], b = i1 < pcm.Length ? pcm[i1] : loop ? pcm[0] : 0;
            float value = (a + (b - a) * (float)(_position - i0)) * take.Scale;
            Advance(rate, loop);
            return value;
        }

        /// <summary>Moves the read position on (never back: a rate at or under zero holds it).</summary>
        void Advance(double samples, bool loop)
        {
            if (_take is not { Length: > 0 } take || _playedOut)
                return;
            _position += Math.Max(0, samples);
            if (_position < take.Length)
                return;
            if (loop)
                _position %= take.Length;
            else
                _playedOut = true;
        }

        /// <summary>One of the sample's takes, by the seed alone; null if it names nothing.</summary>
        static string? Pick(string? sample, SampleLibrary? samples, uint dice)
        {
            if (samples?.Takes(sample) is not { Count: > 0 } takes)
                return null;
            return takes[(int)(dice % (uint)takes.Count)];
        }

        /// <summary>Uniform in [−1, 1), from the seed and a salt (one per thing drawn).</summary>
        static double Uniform(uint seed, uint salt) => Hash(seed ^ salt * 0x9E3779B9u) / 2147483648.0 - 1;

        /// <summary>A full-avalanche integer hash (Wellons' lowbias32): the layer seeds of successive instances differ by a constant step.</summary>
        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Tremolo (sine) or rhythmic gate (duty set), with optional per-cycle jitter so it never quite repeats.</summary>
        double Modulate(ModDef m, double rate, double dt)
        {
            if (_tremoloRate == 0)
                _tremoloRate = rate;
            _tremoloPhase += _tremoloRate * dt;
            if (_tremoloPhase >= 1)
            {
                _tremoloPhase -= 1;
                // New cycle: pick up rate changes, and jitter (spec A.4 rule 4: non-repeating).
                _gateJitter = m.Jitter * _noise.Next();
                _tremoloRate = rate * (1 + _gateJitter);
            }
            if (m.Duty > 0)
                return _tremoloPhase < m.Duty ? 1 : 1 - m.Depth;
            return 1 - m.Depth * 0.5 * (1 - Math.Cos(2 * Math.PI * _tremoloPhase));
        }

        static double Breakpoints(double[][] points, double t)
        {
            if (points.Length == 0)
                return 1;
            if (t <= points[0][0])
                return points[0][1];
            for (int i = 1; i < points.Length; i++)
                if (t <= points[i][0])
                {
                    double u = (t - points[i - 1][0]) / Math.Max(1e-9, points[i][0] - points[i - 1][0]);
                    return points[i - 1][1] + u * (points[i][1] - points[i - 1][1]);
                }
            return points[^1][1];
        }
    }
}
