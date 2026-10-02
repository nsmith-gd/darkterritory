using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// Drives the mixer from the world: the train bed from sim state (spec A.2), the telegraphs from enemy
/// state (spec A.4), actions from this tick's events. It reads state rather than host-only events, so a
/// client hears exactly what the host does from the replicated world.
/// </summary>
public sealed class GameAudio
{
    readonly HotData<MixDef> _mix;
    readonly Dictionary<int, EnemySound> _enemies = new();
    readonly Dictionary<int, double> _packs = new();
    readonly Dictionary<int, SoundInstance> _wheels = new();
    readonly List<(double At, Double3 Position, float Volume)> _slack = new();
    readonly List<SoundInstance> _choir = new();
    readonly Pcg32Ish _rng = new(20260929);
    SoundInstance? _roar, _chuff, _brake, _wind, _valve, _strain, _vent;
    bool _wasRuptured;
    // The derailment (T117): the grind while it slides, and each car's last velocity and when it last crashed.
    SoundInstance? _grind;
    readonly Dictionary<int, (Double3 Velocity, double Crashed)> _wreckCars = [];
    double _time, _lastAccel;
    int _space = PlayerMotor.Outside;

    /// <summary>Through the walls unless the listener is outside, or the sound is in their own space.</summary>
    float Occlusion(int soundSpace) => _space == PlayerMotor.Outside || soundSpace == _space ? 0 : 1;

    public GameAudio(string contentRoot)
    {
        Bank = new SoundBank(Path.Combine(contentRoot, "audio", "sounds"));
        _mix = new HotData<MixDef>(Path.Combine(contentRoot, MixDef.File));
        Mixer = new Mixer(Bank, _mix.Value);
    }

    public SoundBank Bank { get; }
    public Mixer Mixer { get; }

    sealed class EnemySound
    {
        public SpinePhase Phase;
        public SoundInstance? Loop;
        /// <summary>What <see cref="Loop"/> is: a creature whose tell changes with its phase changes loops.</summary>
        public string? Name;
        public double Next;
    }

    /// <summary>
    /// Call once per sim tick (or per frame with the frame's dt). <paramref name="exposed"/>: the listener is
    /// outside (roof, ladder, ground), so hears the wind. <paramref name="space"/>: the enclosed space they're
    /// in (<see cref="PlayerMotor.Space"/>); anything not in it is heard through the walls (spec A.5).
    /// </summary>
    public void Update(World world, in TrainControls controls, Listener listener, bool exposed, double dt, int space = PlayerMotor.Outside)
    {
        _space = space;
        _time += dt;
        if (_mix.Refresh())
            Mixer.Mix = _mix.Value;
        Bank.Refresh();
        Mixer.Listener = listener;
        var train = world.Train;
        Bed(train, controls, listener, exposed, dt);
        Enemies(world);
        Choir(world, train);
        Actions(world);
        Whistle(world, train);
    }

    SoundInstance? _whistle;

    /// <summary>The train's whistle, from the engine's dome, for as long as it blows (the cord, or the Whistler at it).</summary>
    void Whistle(World world, TrainOnLine train)
    {
        if (world.WhistleSeconds <= 0)
        {
            _whistle?.Stop();
            _whistle = null;
            return;
        }
        var engine = train.Frames[0];
        _whistle ??= Mixer.Play("train-whistle", engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, -2)));
        if (_whistle is not null)
        {
            _whistle.Position = engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, -2));
            _whistle.Occlusion = Occlusion(PlayerMotor.Outside);
        }
    }

    void Bed(TrainOnLine train, in TrainControls controls, Listener listener, bool exposed, double dt)
    {
        var engine = train.Frames[0];
        var d = RakeOf(train, 0) ?? train.Rakes[0];
        double speed = d.Speed;
        var bt = train.BoilerTuning;

        _roar ??= Mixer.Play("boiler-roar");
        if (_roar is not null)
        {
            _roar.Position = engine.ToWorld(new Double3(0, 2.6, -engine.Shape.HalfLength * 0.4));
            _roar.Occlusion = Occlusion(0);
            _roar.Params.Set("pressure", train.Boiler.Ruptured ? 0 : train.Boiler.Pressure);
            _roar.Params.Set("fire", bt is null ? 0.7 : train.Boiler.FireFraction(bt));
        }
        _chuff ??= Mixer.Play("chuff");
        if (_chuff is not null)
        {
            _chuff.Position = engine.ToWorld(new Double3(0, 3.5, -engine.Shape.HalfLength * 0.7));
            _chuff.Occlusion = Occlusion(0);
            _chuff.Params.Set("speed", speed);
            _chuff.Params.Set("throttle", controls.Throttle * (train.Boiler.Ruptured ? 0 : 1));
        }

        // Wheels under the few cars nearest the listener; the rest are beyond hearing anyway.
        var near = train.Frames.OrderBy(f => (f.Origin - listener.Position).Length).Take(4).Select(f => f.Index).ToHashSet();
        foreach (var id in _wheels.Keys.Where(k => !near.Contains(k)).ToList())
        {
            _wheels[id].Stop();
            _wheels.Remove(id);
        }
        foreach (int id in near)
        {
            if (!_wheels.TryGetValue(id, out var w) || w.Finished)
                _wheels[id] = w = Mixer.Play("wheel-rail")!;
            if (w is null)
                continue;
            var frame = train.Frames[id];
            w.Position = frame.ToWorld(new Double3(0, 0.5, 0));
            w.Params.Set("speed", RakeOf(train, id)?.Speed ?? speed);
        }

        double braking = controls.Brake * speed;
        if (braking > 0.3)
        {
            _brake ??= Mixer.Play("brake");
            if (_brake is not null)
            {
                _brake.Position = train.Frames.MinBy(f => (f.Origin - listener.Position).Length)!.ToWorld(new Double3(0, 0.5, 0));
                _brake.Params.Set("load", braking);
            }
        }
        else if (_brake is not null)
        {
            _brake.Stop();
            _brake = null;
        }

        _wind ??= Mixer.Play("wind");
        _wind?.Params.Set("wind", exposed ? speed : 0);

        // The safety valve lifting.
        if (train.Boiler.SafetyValveLifting && !train.Boiler.Ruptured)
        {
            _valve ??= Mixer.Play("safety-valve");
            if (_valve is not null)
                _valve.Position = engine.ToWorld(new Double3(0, 4.3, -engine.Shape.HalfLength * 0.1));
        }
        else if (_valve is not null)
        {
            _valve.Stop();
            _valve = null;
        }

        // T109: the boiler in the red, straining louder and higher towards the rupture; the vent's roar while it's held; and
        // the rupture itself, once.
        var boiler = train.Boiler;
        double strain = bt is null || boiler.Ruptured || boiler.Pressure < bt.Redline ? 0
            : 0.6 * Math.Clamp((boiler.Pressure - bt.Redline) / Math.Max(1, bt.PressureMax - bt.Redline), 0, 1) + 0.4 * Math.Clamp(boiler.AtMaxSeconds / bt.RuptureHoldSeconds, 0, 1);
        if (strain > 0)
        {
            _strain ??= Mixer.Play("boiler-strain");
            if (_strain is not null)
            {
                _strain.Position = engine.ToWorld(new Double3(0, 3.2, -engine.Shape.HalfLength * 0.3));
                _strain.Params.Set("strain", strain);
            }
        }
        else if (_strain is not null)
        {
            _strain.Stop();
            _strain = null;
        }
        if (boiler.Vented && !boiler.Ruptured)
        {
            _vent ??= Mixer.Play("vent-hiss");
            if (_vent is not null)
                _vent.Position = engine.ToWorld(new Double3(0, 4.0, -engine.Shape.HalfLength * 0.6));
        }
        else if (_vent is not null)
        {
            _vent.Stop();
            _vent = null;
        }
        if (boiler.Ruptured && !_wasRuptured && Mixer.Play("boiler-burst") is { } burst)
            burst.Position = engine.ToWorld(new Double3(0, 2.6, -engine.Shape.HalfLength * 0.4));
        _wasRuptured = boiler.Ruptured;

        // Slack action: a change in pull runs down the consist as one clunk per coupling (spec A.2, A.7).
        double accel = d.Acceleration;
        double jolt = Math.Abs(accel - _lastAccel);
        _lastAccel = accel;
        if (jolt > 0.12 && _slack.Count == 0)
        {
            float volume = (float)Math.Clamp(jolt / 0.8, 0.3, 1);
            int k = 0;
            foreach (var rake in train.Rakes)
                for (int i = 0; i + 1 < rake.Consist.Vehicles.Count; i++, k++)
                {
                    var frame = train.Frames[rake.Consist.Vehicles[i].Id];
                    _slack.Add((_time + k * 0.11, frame.ToWorld(new Double3(0, 1.0, frame.Shape.HalfLength)), volume));
                }
        }
        for (int i = _slack.Count - 1; i >= 0; i--)
            if (_slack[i].At <= _time)
            {
                Mixer.Play("slack-clunk", _slack[i].Position, _slack[i].Volume);
                _slack.RemoveAt(i);
            }
        Wreck(train.Wreck);
    }

    /// <summary>
    /// The derailment (T117): a crash wherever a car's velocity jumps (it hit the ground or another car), and the grind of
    /// steel through earth at the fastest car still sliding. Read off the poses, so a client hears what the host simulates.
    /// </summary>
    void Wreck(Wreck? wreck)
    {
        if (wreck is null)
        {
            _wreckCars.Clear();
            _grind?.Stop();
            _grind = null;
            return;
        }
        WreckBody? fastest = null;
        foreach (var b in wreck.Bodies)
        {
            if (_wreckCars.TryGetValue(b.Vehicle, out var last))
            {
                double jump = (b.Velocity - last.Velocity).Length;
                if (jump > 3.5 && _time - last.Crashed > 0.6)
                {
                    Mixer.Play("wreck-crash", b.Centre, (float)Math.Clamp(jump / 9, 0.35, 1));
                    last.Crashed = _time;
                }
            }
            _wreckCars[b.Vehicle] = (b.Velocity, last.Crashed);
            if (fastest is null || b.Velocity.Length > fastest.Velocity.Length)
                fastest = b;
        }
        double speed = fastest?.Velocity.Length ?? 0;
        if (!wreck.Settled && speed > 0.6)
        {
            _grind ??= Mixer.Play("wreck-grind");
            if (_grind is not null)
            {
                _grind.Position = fastest!.Centre;
                _grind.Volume = (float)Math.Clamp(speed / 12, 0.15, 1);
            }
        }
        else if (_grind is not null)
        {
            _grind.Stop();
            _grind = null;
        }
    }

    void Loop(EnemySound s, string sound, Double3 at, float occlusion)
    {
        // A tell that changes with the phase (the Shy Thing's ringing, then its jaw): the old one stops for the new.
        if (s.Loop is not null && s.Name != sound)
        {
            s.Loop.Stop();
            s.Loop = null;
        }
        s.Name = sound;
        s.Loop ??= Mixer.Play(sound, at);
        if (s.Loop is not null)
        {
            s.Loop.Position = at;
            s.Loop.Occlusion = occlusion;
        }
    }

    static TrainDynamics? RakeOf(TrainOnLine train, int vehicle) => train.Rakes.FirstOrDefault(r => r.Consist.Vehicles.Any(v => v.Id == vehicle));


    void Enemies(World world)
    {
        var train = world.Train;
        var live = new HashSet<int>();
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone)
                continue;
            live.Add(e.Id);
            if (!_enemies.TryGetValue(e.Id, out var s))
                _enemies[e.Id] = s = new EnemySound { Phase = SpinePhase.Dormant };
            bool entered = s.Phase != e.Phase;
            s.Phase = e.Phase;
            var at = e.WorldPosition(train);
            // Something on a car is heard through that car's walls; something out on the line through the outside's.
            float occlusion = Occlusion(e.Attached >= 0 ? e.Attached : PlayerMotor.Outside);
            switch (e.Kind)
            {
                case EnemyKind.Sleepers when e.Phase == SpinePhase.Telegraph:
                    // Braced: the writhe, again at uneven intervals so it never becomes wallpaper (spec A.4 rule 4).
                    if (entered || _time >= s.Next)
                    {
                        Mixer.Play("sleepers-writhe", at)?.Also(v => v.Occlusion = occlusion);
                        s.Next = _time + 1.6 + 2.2 * _rng.Next();
                    }
                    break;
                case EnemyKind.CinderHound when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab:
                    // The pack howls, not each hound: whoever leads it, every few seconds.
                    int pack = (int)e.Extra;
                    if (!_packs.TryGetValue(pack, out double next) || _time >= next)
                    {
                        Mixer.Play("hound-howl", at)?.Also(v => v.Occlusion = occlusion);
                        _packs[pack] = _time + 3 + 2.5 * _rng.Next();
                    }
                    break;
                case EnemyKind.Dragger when e.Phase == SpinePhase.Telegraph:
                    // The scrape at the lip while the limb comes up (spec A.4): out on the roof, not muffled by a car.
                    Loop(s, "dragger-scrape", at, 0);
                    break;
                case EnemyKind.Stoker when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish:
                    // In the firebox (App. A.5): the soot fall and the fire hissing wrong.
                    Loop(s, "stoker-hiss", at, occlusion);
                    break;
                case EnemyKind.Drift when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    // The rustle of it coming through the reeds (GDD v1.1 §22), from where it is.
                    Loop(s, "drift-rustle", at, occlusion);
                    break;
                case EnemyKind.CarHugger when ((CarHugger)e).Latched || e.Phase == SpinePhase.Telegraph:
                    // The grinding from the rear (App. A.3), for as long as it holds on.
                    Loop(s, "hugger-grind", at, occlusion);
                    break;
                case EnemyKind.Climber when e.Phase == SpinePhase.Telegraph:
                    // Scrabbling at the gap it's mounting (App. A.4), out in the gap.
                    Loop(s, "climber-scrabble", at, occlusion);
                    break;
                case EnemyKind.TrackDoll when e.Phase == SpinePhase.Punish:
                    // Haunting: it giggles in the car it's in, or in the cab at the controls (App. A.2). T118: now and then, a
                    // little demon boy's giggle, never twice alike in pitch or spacing.
                    if (entered || _time >= s.Next)
                    {
                        Mixer.Play("doll-giggle", at)?.Also(v =>
                        {
                            v.Occlusion = occlusion;
                            v.Params.Set("pitch", 0.92 + 0.18 * _rng.Next());
                        });
                        s.Next = _time + 7 + 9 * _rng.Next();
                    }
                    break;
                case EnemyKind.TippyToesie when e.Phase == SpinePhase.Telegraph:
                    Loop(s, "tippy-tiptoe", at, occlusion);
                    break;
                case EnemyKind.FireFlies when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit:
                    Loop(s, "fireflies-buzz", at, occlusion);
                    s.Loop?.Params.Set("progress", Math.Clamp(e.PhaseSeconds / 20, 0, 1));
                    break;
                case EnemyKind.Ribbit when e.Phase == SpinePhase.Telegraph:
                    // The pack croaks, not each toad: only its leader's heard (the lowest id, App. A.6).
                    if (((Ribbit)e).Pack == e.Id || !world.ActiveEnemies.Any(o => o is Ribbit r && r.Pack == ((Ribbit)e).Pack && r.Id < e.Id && !r.Gone))
                        Loop(s, "ribbit-swell", at, occlusion);
                    break;
                case EnemyKind.Grumbler when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    Loop(s, "grumbler-gnaw", at, occlusion);
                    break;
                case EnemyKind.SootChildren when ((SootChildren)e).Extra > 0.5:
                    // Calling for help (App. A.6): a real child and a Soot Child sound the same. The eyes are the tell.
                    Loop(s, "child-call", at, occlusion);
                    break;
                case EnemyKind.CarFire:
                    // Heard through the car's walls: the fire's crackle, more of it the further it's gone.
                    Loop(s, "car-fire", at, occlusion);
                    s.Loop?.Params.Set("progress", e.Extra);
                    break;
                case EnemyKind.ShyThing when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit:
                    // GDD v1.3 App. A.6: its victim's ears ringing, worse the longer they're under. Only their machine is sent it.
                    Loop(s, "shy-hum", at, 0);
                    s.Loop?.Params.Set("progress", Math.Clamp(e.Extra2 / 20, 0, 1));
                    break;
                case EnemyKind.ShyThing when e.Phase is SpinePhase.Grab:
                    // Its jaw coming apart, joint by joint, for everyone.
                    Loop(s, "shy-unhinge", at, occlusion);
                    s.Loop?.Params.Set("progress", Math.Clamp(e.PhaseSeconds / Math.Max(1, e.GrabWindow), 0, 1));
                    break;
                case EnemyKind.Huddle when e.Phase == SpinePhase.Dormant:
                    // Chirping, all the time, but for what's been hushed.
                    Loop(s, "huddle-chirp", at, occlusion);
                    s.Loop?.Params.Set("progress", e.Extra2);
                    break;
                case EnemyKind.Huddle when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab:
                    Loop(s, "huddle-hiss", at, occlusion);
                    break;
                case EnemyKind.Mimic when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab:
                    Loop(s, "mimic-creak", at, occlusion);
                    break;
                case EnemyKind.Mimic when ((Mimic)e).Breathing:
                    // Breathing, for whoever stands still beside it (a subtle tell, close in).
                    Loop(s, "mimic-breath", at, occlusion);
                    s.Loop?.Params.Set("progress", e.Extra2);
                    break;
                default:
                    s.Loop?.Stop();
                    s.Loop = null;
                    break;
            }
        }
        foreach (var id in _enemies.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _enemies[id].Loop?.Stop();
            _enemies.Remove(id);
        }
    }

    /// <summary>
    /// The Choir is heard as voices around the train, more and closer as it gathers (GDD v1.1 App. A.7: the long rising
    /// telegraph), all of them close once it's here.
    /// </summary>
    // A minor chord with its ninth, voice by voice (pitch 1 = A3): each new voice takes the next tone, barely detuned, so
    // the gathering swells as harmony.
    static readonly double[] ChoirChord = [1.0, 1.498, 1.189, 2.0, 0.749, 2.245, 1.335, 2.997];

    void Choir(World world, TrainOnLine train)
    {
        if (world.Combat is null)
            return;
        double build = world.Choir.Present ? 1 : world.Choir.Build;
        // T113 playtest ("annoying, too frequent"): a brief bit of noise isn't heard as it gathering; the HUD's meter shows
        // that. Past a sixth of the way the voices come in, and they're a sung chord, not a cluster.
        int voices = world.Choir.Present ? 8 : build <= 0.15 ? 0 : 1 + (int)(5 * build);
        while (_choir.Count > voices)
        {
            _choir[^1].Stop();
            _choir.RemoveAt(_choir.Count - 1);
        }
        while (_choir.Count < voices && Mixer.Play("choir-voice") is { } v)
        {
            v.Params.Set("pitch", ChoirChord[_choir.Count % ChoirChord.Length] * (1 + 0.006 * (_rng.Next() - 0.5)));
            _choir.Add(v);
        }
        // They come in from every side (App. A.6): spread along the train, alternating sides, closing from
        // 300 m out when distant to 25 m in the swarm, and drifting so they never sit still.
        double range = 300 - 275 * build;
        for (int i = 0; i < _choir.Count; i++)
        {
            var frame = train.Frames[Math.Min(train.Frames.Count - 1, (int)((i + 0.5) / _choir.Count * train.Frames.Count))];
            double side = i % 2 == 0 ? 1 : -1, drift = Math.Sin(_time * 0.11 + i * 1.7) * 12;
            _choir[i].Position = frame.Origin + frame.Right * (side * range) + frame.Back * drift + Double3.Up * 4;
            _choir[i].Occlusion = Occlusion(PlayerMotor.Outside);
        }
    }

    void Actions(World world)
    {
        foreach (var shot in world.Shots)
            Mixer.Play("gunshot", shot.Muzzle)?.Also(v => v.Occlusion = Occlusion(PlayerMotor.Outside));
    }

    /// <summary>Plays a one-shot at a point: crew actions the game knows about (a shovel of coal).</summary>
    public void Play(string name, Double3 at) => Mixer.Play(name, at);

    /// <summary>Tiny deterministic RNG for cue timing (the sim's Pcg32 is for the sim).</summary>
    sealed class Pcg32Ish(ulong seed)
    {
        ulong _s = seed;

        public double Next()
        {
            _s = _s * 6364136223846793005UL + 1442695040888963407UL;
            return (_s >> 11) * (1.0 / (1UL << 53));
        }
    }
}
