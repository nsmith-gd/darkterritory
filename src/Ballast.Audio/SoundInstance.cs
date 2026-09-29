using Ballast;

namespace Ballast.Audio;

/// <summary>
/// One playing sound: synthesises its layers block by block from the definition and live parameters.
/// Position is in world space (double, like everything else 40 km down the line); the mixer spatialises it.
/// </summary>
public sealed class SoundInstance
{
    readonly LayerState[] _layers;
    readonly float[] _scratch = new float[Audio.Block];
    double _crushPhase;
    float _crushHeld;

    internal SoundInstance(int id, string name, SoundDef def, uint seed)
    {
        Id = id;
        Name = name;
        Def = def;
        _layers = new LayerState[def.Layers.Length];
        for (int i = 0; i < _layers.Length; i++)
            _layers[i] = new LayerState(def.Layers[i], seed * 7919u + (uint)i * 104729u + 1);
    }

    /// <summary>Samples for <see cref="SourceKind.Stream"/> layers; set for voice chat.</summary>
    public StreamBuffer? Stream { get; set; }
    readonly float[] _streamBlock = new float[Audio.Block];

    public int Id { get; }
    public string Name { get; }
    public SoundDef Def { get; internal set; }
    public ParamSet Params { get; } = new();
    public Double3 Position { get; set; }
    /// <summary>0 = clear line to the listener, 1 = fully behind a car wall (spec A.5: −12 dB, 900 Hz lowpass).</summary>
    public float Occlusion { get; set; }
    /// <summary>Extra gain the game applies (e.g. a clinger's drill louder as it works through).</summary>
    public float Volume { get; set; } = 1;
    public double Age { get; private set; }
    public bool Stopped { get; private set; }
    public bool Finished => Stopped || !Def.Loop && Age >= Def.Duration;

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
    internal float LastAudibleGain;
    /// <summary>Past the voice budget last block: keeping time, not rendered.</summary>
    public bool Virtual { get; internal set; }

    /// <summary>Synthesises the next block, mono, into <paramref name="output"/> (overwritten).</summary>
    internal void Render(Span<float> output)
    {
        output.Clear();
        // One read per block, shared by every stream layer (a radio is the stream plus its static).
        if (Stream is not null)
            Stream.Read(_streamBlock.AsSpan(0, output.Length));
        foreach (var layer in _layers)
            layer.Render(output, _scratch, Params, Age, Def.CycleSeconds, _streamBlock);
        if (Def.Crush is { } crush)
            Crush(output, crush);
        Age += (double)output.Length / Audio.SampleRate;
    }

    /// <summary>Advances time without producing sound (a virtualised voice keeps its place).</summary>
    internal void Skip(int samples)
    {
        Age += (double)samples / Audio.SampleRate;
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

    sealed class LayerState(LayerDef def, uint seed)
    {
        Noise _noise = new(seed);
        readonly Biquad[] _filters = new Biquad[def.Filters?.Length ?? 0];
        // An impulse fires on the first sample, so a one-shot clunk lands when it's played.
        double _phase = def.Source == SourceKind.Impulse ? 1 - 1e-9 : 0, _tremoloPhase, _vibratoPhase;
        double _tremoloRate, _gateJitter;
        bool _filtersSet;

        /// <param name="cycleSeconds">If positive, envelopes repeat on this period (a loop's breathing, a pack's howls).</param>
        public void Render(Span<float> output, float[] scratch, ParamSet p, double age, double cycleSeconds, float[] stream)
        {
            double t = age - def.Delay;
            if (t < 0)
                return;
            double EnvelopeTime(double at) => cycleSeconds > 0 ? at % cycleSeconds : at;
            var buffer = scratch.AsSpan(0, output.Length);
            double gain = def.Gain.Evaluate(p);
            double frequency = (def.Frequency ?? 440).Evaluate(p);
            if (def.PitchEnvelope is { } pe)
                frequency *= Breakpoints(pe, EnvelopeTime(t));
            if (gain <= 0)
                return;

            // Filters retune per block: a filter frequency on a curve follows its parameter.
            if (def.Filters is { } filters)
                for (int f = 0; f < filters.Length; f++)
                {
                    var fd = filters[f];
                    double ff = fd.Frequency.Evaluate(p);
                    if (!_filtersSet || fd.Frequency.Param is not null)
                        _filters[f].Set(fd.Type, ff, fd.Q, fd.GainDb);
                }
            _filtersSet = true;

            double dt = 1.0 / Audio.SampleRate;
            double vibRate = def.Vibrato?.Rate.Evaluate(p) ?? 0, vibDepth = def.Vibrato?.Depth ?? 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                double f = frequency;
                if (vibDepth > 0)
                {
                    _vibratoPhase += vibRate * dt;
                    f *= 1 + vibDepth * Math.Sin(2 * Math.PI * _vibratoPhase);
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
                    SourceKind.Stream => stream[i],
                    // A click when the phase wraps, with a little noise so repeats aren't identical.
                    _ => _phase < prev ? 1f + 0.3f * _noise.Next() : 0f,
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
