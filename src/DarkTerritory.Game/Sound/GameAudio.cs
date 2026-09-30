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
    SoundInstance? _roar, _chuff, _brake, _wind, _valve;
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
        CallOuts(world);
    }

    readonly Dictionary<int, int> _callOuts = new();

    /// <summary>
    /// GDD App. D.7 Call Out: a shout or a bout of banging from inside a Holdout, at its door, each time the host allows
    /// one (the count in its record goes up). Heard to callOut.audibleM, the sounds' own maxDistance. A Holdout first seen
    /// with call outs already made doesn't replay them.
    /// </summary>
    void CallOuts(World world)
    {
        if (world.Holdouts is not { } holdouts)
            return;
        var sets = holdouts.Tuning.Survivors.VoiceSets;
        foreach (var h in holdouts.All)
        {
            int seen = _callOuts.GetValueOrDefault(h.Index, h.CallOuts);
            _callOuts[h.Index] = h.CallOuts;
            if (h.CallOuts <= seen || sets.Length == 0)
                continue;
            var sounds = sets[Math.Clamp(h.VoiceSet, 0, sets.Length - 1)].Sounds;
            if (sounds.Length > 0)
                Mixer.Play(sounds[Math.Clamp(h.CallOutSound, 0, sounds.Length - 1)], h.Door + Double3.Up * 1.4)?.Also(v => v.Occlusion = Occlusion(PlayerMotor.Outside));
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
            // A Clinger is heard inside the car it's drilling (App. A.4); the Hollow is in the cab with you.
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
                case EnemyKind.CinderHound when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish:
                    // The pack howls, not each hound: whoever leads it, every few seconds.
                    int pack = (int)e.Extra;
                    if (!_packs.TryGetValue(pack, out double next) || _time >= next)
                    {
                        Mixer.Play("hound-howl", at)?.Also(v => v.Occlusion = occlusion);
                        _packs[pack] = _time + 3 + 2.5 * _rng.Next();
                    }
                    break;
                case EnemyKind.Clinger when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    s.Loop ??= Mixer.Play("clinger-drill", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                        s.Loop.Params.Set("progress", e.Phase == SpinePhase.Punish ? 1 : e.Extra);
                    }
                    break;
                case EnemyKind.Dragger when e.Phase == SpinePhase.Telegraph:
                    // The scrape at the lip while the limb comes up (spec A.4): out on the roof, not muffled by a car.
                    s.Loop ??= Mixer.Play("dragger-scrape", at);
                    if (s.Loop is not null)
                        s.Loop.Position = at;
                    break;
                case EnemyKind.Deadman when e.Phase == SpinePhase.Telegraph:
                case EnemyKind.Stoker when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish:
                    // In the cab (App. A.5): the controls clicking on their own; the firebox hissing wrong.
                    s.Loop ??= Mixer.Play(e.Kind == EnemyKind.Deadman ? "deadman-click" : "stoker-hiss", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                    }
                    break;
                case EnemyKind.Drift when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    // The rustle of it coming through the reeds and over the roofs (App. A.4), from where it is.
                    s.Loop ??= Mixer.Play("drift-rustle", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                    }
                    break;
                case EnemyKind.Weight when e.Phase == SpinePhase.Telegraph && e.Attached >= 0:
                    // The drag scrape under the rear coupling (App. A.3), for as long as it holds on.
                    s.Loop ??= Mixer.Play("weight-scrape", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                    }
                    break;
                case EnemyKind.Rattle when e.Phase == SpinePhase.Telegraph:
                    // The rattle in the coupling (App. A.5): all the tell there is. Out in the gap, so a car between you and it
                    // muffles it like anything else.
                    s.Loop ??= Mixer.Play("rattle", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                    }
                    break;
                case EnemyKind.CarFire:
                case EnemyKind.LooseLoad when e.Phase == SpinePhase.Telegraph:
                case EnemyKind.Gnawers when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    // Trouble in a car, heard through its walls: the fire's crackle, the straps groaning, the chittering.
                    s.Loop ??= Mixer.Play(e.Kind switch { EnemyKind.CarFire => "car-fire", EnemyKind.LooseLoad => "load-creak", _ => "gnawers" }, at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                        s.Loop.Params.Set("progress", e.Kind == EnemyKind.Gnawers ? e.Health : e.Extra);
                    }
                    break;
                case EnemyKind.LongWhistle when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit:
                    // A blast each time it sounds again (App. A.2's escalation), each louder than the last, from up the line
                    // ahead. Out in the open: a car's walls muffle it like anything else outside.
                    if (e.Extra > s.Next)
                    {
                        s.Next = e.Extra;
                        Mixer.Play("long-whistle", at, (float)Math.Min(1, 0.6 + 0.2 * (e.Extra - 1)))?.Also(v => v.Occlusion = occlusion);
                    }
                    break;
                case EnemyKind.Hollow when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                    s.Loop ??= Mixer.Play("hollow-gutter", at);
                    if (s.Loop is not null)
                    {
                        s.Loop.Position = at;
                        s.Loop.Occlusion = occlusion;
                    }
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

    /// <summary>The Choir is heard as voices around the train, more and closer as aggro climbs (App. A.6).</summary>
    void Choir(World world, TrainOnLine train)
    {
        if (world.Combat is not { } combat)
            return;
        var t = combat.Choir;
        double aggro = world.Choir.Aggro;
        int voices = aggro <= 0.5 ? 0 : world.Choir.Phase(t) switch
        {
            ChoirPhase.Distant => 1,
            ChoirPhase.Approach => 3 + (int)(3 * (aggro - t.ApproachThreshold) / Math.Max(1, t.SwarmThreshold - t.ApproachThreshold)),
            _ => 8,
        };
        while (_choir.Count > voices)
        {
            _choir[^1].Stop();
            _choir.RemoveAt(_choir.Count - 1);
        }
        while (_choir.Count < voices && Mixer.Play("choir-voice") is { } v)
        {
            v.Params.Set("pitch", 0.75 + 0.5 * _rng.Next());
            _choir.Add(v);
        }
        // They come in from every side (App. A.6): spread along the train, alternating sides, closing from
        // 300 m out when distant to 25 m in the swarm, and drifting so they never sit still.
        double range = 300 - 275 * Math.Clamp(aggro / Math.Max(1, t.SwarmThreshold), 0, 1);
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
