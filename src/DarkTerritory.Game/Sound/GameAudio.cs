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
public sealed partial class GameAudio
{
    readonly HotData<MixDef> _mix;
    // The walls' numbers; a content root without walls.json (a test's own) has the defaults.
    readonly HotData<WallsTuning>? _walls;
    readonly Dictionary<int, EnemySound> _enemies = new();
    readonly Dictionary<int, double> _packs = new();
    readonly Dictionary<int, SoundInstance> _packHowls = new();
    readonly Dictionary<int, SoundInstance> _wheels = new();
    readonly List<SoundInstance> _choir = new();
    readonly Pcg32Ish _rng = new(20260929);
    SoundInstance? _roar, _chuff, _brake, _wind, _valve, _strain, _vent;
    bool _wasRuptured;
    double _time;
    int _space = PlayerMotor.Outside;

    /// <summary>Through the walls unless the listener is outside, or the sound is in their own space.</summary>
    float Occlusion(int soundSpace) => _space == PlayerMotor.Outside || soundSpace == _space ? 0 : 1;

    public GameAudio(string contentRoot)
    {
        Bank = new SoundBank(Path.Combine(contentRoot, "audio", "sounds"));
        _mix = new HotData<MixDef>(Path.Combine(contentRoot, MixDef.File));
        string walls = Path.Combine(contentRoot, WallsTuning.File);
        _walls = File.Exists(walls) ? new HotData<WallsTuning>(walls) : null;
        Mixer = new Mixer(Bank, _mix.Value);
        PrepareMix(contentRoot);
        Opera = new Opera(contentRoot);
        Clerk = new ClerkVoice(Bank.Samples);
    }

    public SoundBank Bank { get; }
    /// <summary>The yard's voice on the radio (note 240): what it can say, and how long a line takes it.</summary>
    public ClerkVoice Clerk { get; }
    public Mixer Mixer { get; }
    /// <summary>The derailment's music (GDD v1.4 App. E.6): every track loaded now, at startup.</summary>
    public Opera Opera { get; }

    /// <summary>
    /// Every frame: the derailment's opera, the host's draw (<see cref="World.DerailMusic"/>) at
    /// <paramref name="sequenceSeconds"/> into the sequence (negative with no derailment). It starts on the replay.
    /// </summary>
    public void Music(uint track, double sequenceSeconds, WreckTuning tuning, double end = -1, double hitAt = -1) =>
        Opera.Update(Mixer, track, sequenceSeconds, tuning, end, hitAt);

    // The film's own wreck heard (note 251): where its shot has got to in the recording, and when it last crashed.
    double _filmAt = double.NaN, _filmCrashed = double.NegativeInfinity;
    SoundInstance? _filmGrind;

    /// <summary>
    /// Every frame: the derailment film's shot and how far into it (<see cref="WreckFilm.CutAt"/>), or null outside the film.
    /// The film's wreck is what's on screen, so it's what's heard: each recorded hit (<see cref="FilmFrame.Impacts"/>) as the
    /// shot plays through it, a shot that goes back over a moment playing its crash again from its own angle, and the grind
    /// of the fastest car still sliding. The live wreck goes quiet meanwhile; it's somewhere else by then.
    /// </summary>
    public void Film(WreckFilm? film, (FilmShot Shot, double Into)? at)
    {
        if (film is null || at is not { } now)
        {
            _filmAt = double.NaN;
            _filmGrind?.Stop();
            _filmGrind = null;
            return;
        }
        double recorded = now.Shot.At(now.Into);
        // A cut (to a new shot, back or on in the recording): from here, not everything between.
        if (double.IsNaN(_filmAt) || recorded < _filmAt || recorded - _filmAt > 0.2)
            _filmAt = recorded;
        int from = (int)Math.Floor(_filmAt * WreckFilm.Rate) + 1, to = Math.Min(film.Frames.Count - 1, (int)Math.Floor(recorded * WreckFilm.Rate));
        WreckImpact? hardest = null;
        for (int f = Math.Max(1, from); f <= to; f++)
            foreach (var hit in film.Frames[f].Impacts)
                if (hit.Speed > (hardest?.Speed ?? 3))
                    hardest = hit;
        if (hardest is { } h && _time - _filmCrashed > 0.2)
        {
            Mixer.Play("wreck-crash", h.At, (float)Math.Clamp(h.Speed / 9, 0.35, 1));
            _filmCrashed = _time;
        }
        // Each body's landings (the director, 5 Oct 2026): a thud where it hit, as loud as it hit, off the ground or a car's
        // planks; the hardest few in the stretch, so a pile-up isn't a wall of them.
        int thuds = 0;
        for (int f = Math.Max(1, from); f <= to; f++)
            foreach (var land in film.Frames[f].Landings.OrderByDescending(l => l.Speed))
                if (thuds++ < 3)
                    Mixer.Play(land.Car ? "crew-carry.body-land.wood" : "crew-carry.body-land.ground", land.At, (float)Math.Clamp(land.Speed / 10, 0.3, 1));
        _filmAt = recorded;
        // The grind: the fastest car in the recording at this moment, by its travel between keyframes.
        var (a, b, _) = film.At(recorded);
        int fastest = -1;
        double speed = 0;
        for (int c = 0; c < a.Cars.Count && c < b.Cars.Count; c++)
        {
            double v = (b.Cars[c].Origin - a.Cars[c].Origin).Length * WreckFilm.Rate;
            if (v > speed)
                (fastest, speed) = (c, v);
        }
        if (fastest >= 0 && speed > 0.6)
        {
            _filmGrind ??= Mixer.Play("wreck-grind");
            if (_filmGrind is not null)
            {
                _filmGrind.Position = a.Cars[fastest].Origin;
                _filmGrind.Volume = (float)Math.Clamp(speed / 12, 0.15, 1);
            }
        }
        else if (_filmGrind is not null)
        {
            _filmGrind.Stop();
            _filmGrind = null;
        }
    }

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
        MixAround(world, listener);
        var train = world.Train;
        // Inside a breached car the wind blows in as on the roof (decided 1 Oct; GameAudio.Faults.cs).
        exposed |= BreachedAround(world, listener.Position) is not null;
        _exposed = exposed;
        Bed(train, controls, listener, exposed, dt);
        Enemies(world);
        Choir(world, train);
        Actions(world);
        Whistle(world, train);
        Cues(world);
        RoofWarning(world);
        Toys(world, train);
        Ride(train);
        HearWalls(world);
    }

    // The one-shots started on a car, riding it: the voice, the car, and where on it (the car's frame).
    readonly List<(SoundInstance Voice, int Car, Double3 Local)> _riding = [];
    int _lastVoice;
    /// <summary>How far outside a car's bounds a one-shot still counts as on it (a boot on the step, a hand on the rail).</summary>
    const double OnCar = 0.5;

    /// <summary>The world's own sounds (a tunnel's mouth, a bridge, a facility's works, the wreck yard's heap): where they are, whatever passes.</summary>
    static bool OfTheWorld(string name) =>
        name.StartsWith("world-", StringComparison.Ordinal) || name.StartsWith("place-", StringComparison.Ordinal) || name.StartsWith("heap-", StringComparison.Ordinal);

    /// <summary>
    /// A one-shot started on a car rides with it (spec A.4 "spatially precise"): it was played at a world point, and at speed
    /// the train ran on from under it (at 22 m/s a one-second giggle in the cab ended up a car and a half back). Each new
    /// positioned one-shot on a car is kept in the car's frame and put back where it is on the car every frame until it's
    /// done. Loops follow their owners already; one-shots off the train, and the world's own (<see cref="OfTheWorld"/>: a
    /// tunnel's mouth the engine's passing), stay put.
    /// </summary>
    void Ride(TrainOnLine train)
    {
        foreach (var v in Mixer.Voices)
        {
            if (v.Id <= _lastVoice)
                continue;
            if (v.Def.Loop || v.Def.Flat || v.Def.Tier == Mixer.MusicTier || OfTheWorld(v.Name))
                continue;
            for (int car = 0; car < train.Frames.Count; car++)
            {
                var local = train.Frames[car].ToLocal(v.Position);
                var b = train.Frames[car].Shape.Bounds;
                if (local.X >= b.Min.X - OnCar && local.X <= b.Max.X + OnCar && local.Y >= b.Min.Y - OnCar && local.Y <= b.Max.Y + OnCar
                    && local.Z >= b.Min.Z - OnCar && local.Z <= b.Max.Z + OnCar)
                {
                    _riding.Add((v, car, local));
                    break;
                }
            }
        }
        if (Mixer.Voices.Count > 0)
            _lastVoice = Math.Max(_lastVoice, Mixer.Voices.Max(v => v.Id));
        _riding.RemoveAll(r => r.Voice.Finished || r.Car >= train.Frames.Count);
        foreach (var (voice, car, local) in _riding)
            voice.Position = train.Frames[car].ToWorld(local);
    }

    /// <summary>
    /// The walls between the ear and each sound (spec A.5, A.7; <see cref="Sound.Walls"/>, note 248), from where both are
    /// this frame. Not the train's bed (tier 5): that's the car itself, which the space (spaces.json) has. Not a sound a
    /// caller put part-way behind something (a shout through a holdout's door): that's its own judgement.
    /// </summary>
    static readonly WallsTuning DefaultWalls = new();

    void HearWalls(World world)
    {
        _walls?.Refresh();
        var tuning = _walls?.Value ?? DefaultWalls;
        var ear = Mixer.Listener.Position;
        foreach (var v in Mixer.Voices)
            v.Walls = v.Def.Flat || v.Def.Tier is 5 or Mixer.MusicTier || v.Occlusion is > 0 and < 1 ? 0
                : Sound.Walls.Between(world.Train, ear, v.Position, tuning);
    }

    readonly Dictionary<int, SoundInstance> _toys = [];
    // Toys jostled as they landed, and until when they sound for it.
    readonly Dictionary<int, double> _jostled = [];
    /// <summary>How long a noisy toy sounds when it's jostled: a squeak, a few notes of the music box, a roll of the drum.</summary>
    const double JostleSeconds = 0.7;

    /// <summary>
    /// A noisy toy in someone's hands (GDD v1.4 §19, App. C item 4): its squeak, tune or drum for as long as it's carried, in
    /// the carrier's car (and in the carrier's name on the Choir's meter, host-side), and a moment as it's jostled landing.
    /// Lying still, it's quiet.
    /// </summary>
    void Toys(World world, TrainOnLine train)
    {
        var carried = new HashSet<int>();
        foreach (var b in world.Bodies.All)
        {
            bool jostled = _jostled.TryGetValue(b.Id, out double until) && _time < until;
            if (b.Kind != Sim.Physics.BodyKind.Toy || b.Carrier < 0 && !jostled || b.Noise == Sim.Physics.ToyNoise.None)
                continue;
            carried.Add(b.Id);
            if (!_toys.TryGetValue(b.Id, out var voice) || voice.Finished)
            {
                string sound = b.Noise switch
                {
                    Sim.Physics.ToyNoise.Squeaker => "toy-squeaker",
                    Sim.Physics.ToyNoise.MusicBox => "toy-musicbox",
                    _ => "toy-drummer",
                };
                if (Mixer.Play(sound) is not { } played)
                    continue;
                _toys[b.Id] = voice = played;
            }
            voice.Position = Sim.Physics.Bodies.WorldCentre(b, train);
            voice.Occlusion = Occlusion(b.Parent >= 0 && b.Parent < train.Frames.Count ? b.Parent : PlayerMotor.Outside);
        }
        foreach (var id in _toys.Keys.Where(id => !carried.Contains(id)).ToList())
        {
            _toys[id].Stop();
            _toys.Remove(id);
        }
        foreach (var id in _jostled.Where(j => _time >= j.Value).Select(j => j.Key).ToList())
            _jostled.Remove(id);
    }

    SoundInstance? _whistle, _radioVoice, _radioSays, _clerkSays;
    IReadOnlyList<string>? _radioLines;
    int _radioSaid;
    string? _clerkSaid;
    // The fraction of the working band's bottom under which the whistle only wheezes.
    const double WheezeBelow = 0.5;

    /// <summary>
    /// The fortress reading over the radio (GDD §9; notes 178, 240): the set's static for as long as it's on the air, and
    /// each of <paramref name="reading"/>'s lines said as it comes on (<paramref name="onAir"/> of them so far, from
    /// Sim.Run.Radio.Reading at the voice's own pace), in the clerk's voice through the set. Null, nobody's on the air.
    /// </summary>
    public void Radio(IReadOnlyList<string>? reading, int onAir)
    {
        if (reading is null)
        {
            _radioSays?.Stop();
            _radioSays = null;
            _radioLines = null;
            // The static stays under a clerk's line still being said (ClerkLine).
            if (_clerkSays is null or { Finished: true })
            {
                _radioVoice?.Stop();
                _radioVoice = null;
            }
            return;
        }
        _radioVoice ??= Mixer.Play("radio-clerk");
        if (!ReferenceEquals(reading, _radioLines))
        {
            _radioLines = reading;
            _radioSaid = 0;
        }
        for (; _radioSaid < Math.Min(onAir, reading.Count); _radioSaid++)
        {
            // Behind (a stalled frame, or joined mid-reading): only the newest line is said, not a pile of them at once.
            if (_radioSaid < onAir - 1 || Clerk.Render(reading[_radioSaid]) is not { } said)
                continue;
            _radioSays?.Stop();
            _radioSays = Mixer.Play("radio-clerk-voice");
            if (_radioSays is not null)
                _radioSays.Clip = said;
        }
    }

    /// <summary>
    /// The clerk's one line over a moment (notes 240, 242): the derail film's cause card (GDD v1.4 App. E.5) and the Stranded
    /// pull-back's report (E.9), said once in the yard's voice through the set as it comes on, with the channel's static
    /// under it till it's done. Null, or the same line again, says nothing new.
    /// </summary>
    public void ClerkLine(string? line)
    {
        if (line is null || line == _clerkSaid)
        {
            // Said: the static goes with it, unless a reading has the air.
            if (_clerkSays is { Finished: true })
            {
                _clerkSays = null;
                if (_radioLines is null)
                {
                    _radioVoice?.Stop();
                    _radioVoice = null;
                }
            }
            return;
        }
        _clerkSaid = line;
        if (Clerk.Render(line) is not { } said)
            return;
        _clerkSays?.Stop();
        _clerkSays = Mixer.Play("radio-clerk-voice");
        if (_clerkSays is not null)
            _clerkSays.Clip = said;
        _radioVoice ??= Mixer.Play("radio-clerk");
    }

    /// <summary>The train's whistle, from the engine's dome, for as long as it blows (the cord, or the Whistler at it).</summary>
    void Whistle(World world, TrainOnLine train)
    {
        // The cord's own whistle, once it's installed, is the crew's (crew-cab-controls, GameAudio.Crew.cs).
        if (world.WhistleSeconds <= 0 || CordWhistles(world))
        {
            // A held whistle stops with the blowing; a recorded blast (a whole take, start to release) rings out.
            if (Bank.Get("train-whistle") is { Loop: true })
                _whistle?.Stop();
            _whistle = null;
            return;
        }
        var engine = train.Frames[0];
        if (_whistle is { Finished: true })
            _whistle = null;    // a recorded blast that's ended while the cord's still held: another
        // On low steam the whistle can barely speak: a thin, flat wheeze (crew-mishaps). The cord's and the Whistler's alike,
        // since it's the same whistle (spec A.4), and on tier 1 as the whistle is.
        string whistle = train.BoilerTuning is { } bt && train.Boiler.Pressure < bt.WorkingBandMin * WheezeBelow
            && HasCue("crew-mishaps.whistle-wheeze") ? "crew-mishaps.whistle-wheeze" : "train-whistle";
        _whistle ??= Mixer.Play(whistle, engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, -2)));
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

        _roar ??= Synth("boiler-roar");
        if (_roar is not null)
        {
            _roar.Position = engine.ToWorld(new Double3(0, 2.6, -engine.Shape.HalfLength * 0.4));
            _roar.Occlusion = Occlusion(0);
            _roar.Params.Set("pressure", train.Boiler.Ruptured ? 0 : train.Boiler.Pressure);
            _roar.Params.Set("fire", bt is null ? 0.7 : train.Boiler.FireFraction(bt));
        }
        _chuff ??= Synth("chuff");
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
            {
                if (Synth("wheel-rail") is not { } fresh)
                    continue;
                _wheels[id] = w = fresh;
            }
            var frame = train.Frames[id];
            w.Position = frame.ToWorld(new Double3(0, 0.5, 0));
            w.Params.Set("speed", RakeOf(train, id)?.Speed ?? speed);
        }

        double braking = controls.Brake * speed;
        if (braking > 0.3)
        {
            _brake ??= Synth("brake");
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

        _wind ??= Synth("wind");
        _wind?.Params.Set("wind", exposed ? speed : 0);

        // The safety valve lifting.
        if (train.Boiler.SafetyValveLifting && !train.Boiler.Ruptured)
        {
            _valve ??= Synth("safety-valve");
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
            _strain ??= Synth("boiler-strain");
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
            _vent ??= Synth("vent-hiss");
            if (_vent is not null)
                _vent.Position = engine.ToWorld(new Double3(0, 4.0, -engine.Shape.HalfLength * 0.6));
        }
        else if (_vent is not null)
        {
            _vent.Stop();
            _vent = null;
        }
        if (boiler.Ruptured && !_wasRuptured && Synth("boiler-burst") is { } burst)
            burst.Position = engine.ToWorld(new Double3(0, 2.6, -engine.Shape.HalfLength * 0.4));
        _wasRuptured = boiler.Ruptured;

        // Slack action: a change in pull runs down the consist as one clunk per coupling (spec A.2, A.7; GameAudio.Train).
        Slack(train, d);
    }

    void Loop(EnemySound s, string sound, Double3 at, float occlusion)
    {
        s.Loop ??= Mixer.Play(sound, at);
        if (s.Loop is not null)
        {
            s.Loop.Position = at;
            s.Loop.Occlusion = occlusion;
        }
    }

    /// <summary>
    /// A tell held on like a loop whose sound can be single steps or bursts instead (its kept takes from the audio checklist,
    /// tools/audio/install.py): a loop is held; a one-shot is fired again, once the last has ended, after an uneven gap, so
    /// it never settles into a rhythm (spec A.4 rule 4).
    /// </summary>
    void Repeat(EnemySound s, string sound, Double3 at, float occlusion, double gap, double jitter)
    {
        if (Bank.Get(sound) is not { Loop: false })
        {
            Loop(s, sound, at, occlusion);
            return;
        }
        if (s.Loop is { Finished: false } playing)
        {
            playing.Position = at;
            playing.Occlusion = occlusion;
            return;
        }
        if (_time < s.Next)
            return;
        s.Loop = Mixer.Play(sound, at)?.Also(v => v.Occlusion = occlusion);
        s.Next = _time + gap + jitter * _rng.Next();
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
                    // The pack howls, not each hound: whoever leads it, every few seconds (close behind, the near howl).
                    int pack = (int)e.Extra;
                    // A recorded howl can run longer than the gap: the next waits for the last to end and a breath after
                    // it, so they never pile up into a wall.
                    if (_packHowls.TryGetValue(pack, out var howling))
                    {
                        if (!howling.Finished)
                            break;
                        _packHowls.Remove(pack);
                        _packs[pack] = Math.Max(_packs.GetValueOrDefault(pack), _time + 1.5 + 2.5 * _rng.Next());
                    }
                    if (!_packs.TryGetValue(pack, out double next) || _time >= next)
                    {
                        if (Mixer.Play(HowlFor(train, e), at) is { } howl)
                        {
                            howl.Occlusion = occlusion;
                            _packHowls[pack] = howl;
                        }
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
                    // Scrabbling at the gap it's mounting (App. A.4), out in the gap: in uneven bursts.
                    Repeat(s, "climber-scrabble", at, occlusion, 0.15, 0.7);
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
                case EnemyKind.TippyToesie when e.Phase is SpinePhase.Telegraph or SpinePhase.Commit:
                    // Faint tiptoeing for as long as it creeps up (App. A.5): a step every half second or so at its creep
                    // (0.6 m/s, enemies.json), on whatever it's crossing.
                    Repeat(s, Surfaced("tippy-tiptoe", SurfaceOf(e, train)) ?? "tippy-tiptoe", at, occlusion, 0.45, 0.35);
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
                    // Call after call, with a frightened wait for an answer between.
                    Repeat(s, "child-call", at, occlusion, 1.6, 2.4);
                    break;
                case EnemyKind.CarFire:
                    // Heard through the car's walls: the fire's crackle, more of it the further it's gone.
                    Loop(s, "car-fire", at, occlusion);
                    s.Loop?.Params.Set("progress", e.Extra);
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
        // The shots are heard from the guns' replicated state, everyone's (GameAudio.Crew.cs, CrewGuns): world.Shots is only
        // ever this machine's own predicted one.
        Strikes(world);
    }

    readonly HashSet<int> _heardHits = [], _heardImpacts = [];
    bool _strikesPrimed;

    /// <summary>
    /// What landed (T121), from the replicated world, each once: a ball's boom where it came down (a splash in water, and the
    /// porcelain going when it was the Track Doll), and a blow or a ball on a creature at the hit point. A blow is the tool
    /// in the hitter's hand on flesh (crew-melee), or a bare slap; a ball, or a blow with no take of its own, the thud of
    /// hit-confirm. On the first update, what's already there is old news: it's marked heard, not played.
    /// </summary>
    void Strikes(World world)
    {
        foreach (var i in world.Impacts)
        {
            if (!_heardImpacts.Add(i.Id) || !_strikesPrimed)
                continue;
            // Nobody's shot (World.Blast: a powder keg or a powder car going up, notes 182 and 185) is its own, bigger blast.
            string boom = i.Shooter < 0 && Bank.Get("powder-blast") is not null ? "powder-blast"
                : i.Surface == ImpactSurface.Water ? "cannon-splash" : "cannon-impact";
            Mixer.Play(boom, i.At)?.Also(v => v.Occlusion = Occlusion(PlayerMotor.Outside));
            if (i.Struck == Sim.Enemies.EnemyKind.TrackDoll)
                Mixer.Play("doll-shatter", i.At)?.Also(v => v.Occlusion = Occlusion(PlayerMotor.Outside));
        }
        foreach (var h in world.Hits)
        {
            if (!_heardHits.Add(h.Id) || !_strikesPrimed)
                continue;
            float occlusion = Occlusion(world.ActiveEnemies.FirstOrDefault(e => e.Id == h.EnemyId) is { Attached: >= 0 } on ? on.Attached : PlayerMotor.Outside);
            string? blow = null;
            if (h.Source == HitSource.Melee)
                foreach (var (id, s) in CrewStates)
                    if (id == h.By)
                        blow = Kit.Held(s) is var tool and not Tool.None ? $"crew-melee.{ToolName(tool)}-hit-flesh" : "crew-mishaps.bare-slap";
            if (blow is not null && HasCue(blow))
                Cue(blow, h.At, occlusion);
            else
                Mixer.Play("hit-confirm", h.At)?.Also(v => v.Occlusion = occlusion);
        }
        _strikesPrimed = true;
        // Forget what's gone off the wire (ids aren't reused for a long while: a night's worth).
        if (_heardImpacts.Count > 64)
            _heardImpacts.IntersectWith(world.Impacts.Select(i => i.Id));
        if (_heardHits.Count > 64)
            _heardHits.IntersectWith(world.Hits.Select(h => h.Id));
    }

    SoundInstance? _cooling;
    int _lampsOut;

    /// <summary>
    /// GDD v1.4 App. E.9, the Stranded outro: "wind and the boiler ticking as it cools. No music." The dead boiler's iron
    /// ticking at the firebox, slower as the outro goes on, and each car's lamp guttering out as the shot pulls back, the
    /// last car first and the engine last (<see cref="Views.StrandedLampsOut"/>, the picture's own count). The app calls it
    /// each frame with the outro's seconds, or −1 when there's none.
    /// </summary>
    public void Stranded(TrainOnLine train, StrandedOutroTuning t, double seconds)
    {
        if (seconds < 0)
        {
            _cooling?.Stop();
            _cooling = null;
            _lampsOut = 0;
            return;
        }
        var engine = train.Frames[0];
        var firebox = engine.ToWorld(new Double3(0, 1.6, engine.Shape.HalfLength * 0.5));
        _cooling ??= Mixer.Play("boiler-tick", firebox);
        if (_cooling is not null)
        {
            _cooling.Position = firebox;
            _cooling.Params.Set("cool", Math.Clamp(seconds / Math.Max(1e-6, t.Seconds), 0, 1));
        }
        for (int gone = Views.StrandedLampsOut(train.Frames.Count, t, seconds); _lampsOut < Math.Min(gone, train.Frames.Count); _lampsOut++)
        {
            var car = train.Frames[train.Frames.Count - 1 - _lampsOut];
            Mixer.Play("lamp-out", car.ToWorld(new Double3(0, car.Shape.RoofHeight * 0.8, 0)));
        }
    }

    /// <summary>
    /// A night is over (back to the menus): every sound it started stops, and what's remembered of it goes, so the menus are
    /// quiet under their own sound and the next night starts its bed, loops and edges afresh.
    /// </summary>
    public void EndNight()
    {
        Mixer.StopAll();
        _roar = _chuff = _brake = _wind = _valve = _strain = _vent = _whistle = _radioVoice = _radioSays = _clerkSays = null;
        _radioLines = null;
        _clerkSaid = null;
        _enemies.Clear();
        _packs.Clear();
        _packHowls.Clear();
        _wheels.Clear();
        _choir.Clear();
        _toys.Clear();
        _heardHits.Clear();
        _heardImpacts.Clear();
        _strikesPrimed = false;
        _cooling = null;
        _lampsOut = 0;
        _wasRuptured = false;
        _space = PlayerMotor.Outside;
        EndNightCues();
        EndNightAreas();
    }

    /// <summary>Plays a one-shot at a point: crew actions the game knows about (a shovel of coal).</summary>
    public void Play(string name, Double3 at) => Mixer.Play(name, at);

    /// <summary>A sound with no place (it's flat): the dead channel's chime (D.11), and the like.</summary>
    public void Play(string name) => Mixer.Play(name);

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
