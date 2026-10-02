using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The train's own cues (tools/audio/cues.py "bed-*" and "state-*"): the bed (spec A.2) as recorded takes where they're
/// installed, the boiler's alarms, the brakes, damage, and the derailment. Where a recorded cue replaces one of the bed's
/// synthesised sounds (GameAudio.Bed), the synth stands down for it (<see cref="Synth"/>), and plays on as the fallback
/// where the cue isn't installed, so nothing ever goes silent. Everything here reads replicated state (out/audio/hooks-map.md):
/// the rakes, the vehicles, the boiler, the controls, and World.Derailed.
/// </summary>
public sealed partial class GameAudio
{
    // Which recorded cues stand in for each synthesised bed sound (any one installed, the synth stands down).
    static readonly Dictionary<string, string[]> SampledBy = new()
    {
        ["boiler-roar"] = ["bed-boiler-roar.roar-low", "bed-boiler-roar.roar-high"],
        ["chuff"] = ["bed-chuff.chuff", "bed-chuff.chuff-heavy"],
        ["wheel-rail"] = ["bed-wheel-rail.roll-slow", "bed-wheel-rail.roll-fast"],
        ["brake"] = ["bed-brake.drag", "bed-brake.drag-hot"],
        ["wind"] = ["bed-wind.wind-slow", "bed-wind.wind-fast"],
        ["safety-valve"] = ["state-valve.blow"],
        ["boiler-strain"] = ["state-strain.groan"],
        ["vent-hiss"] = ["bed-vent.blow"],
        ["boiler-burst"] = ["state-rupture.burst"],
    };

    /// <summary>A recorded cue is installed for this synthesised bed sound, so the synth stays quiet.</summary>
    bool Sampled(string synth) => SampledBy.TryGetValue(synth, out var cues) && cues.Any(HasCue);

    /// <summary>The bed's synthesised sound, unless a recorded cue has taken its place.</summary>
    SoundInstance? Synth(string name) => Sampled(name) ? null : Mixer.Play(name);

    // Four exhaust beats to a turn of the driving wheels, 1.7 m across (chuff.json); 12 m rails (wheel-rail.json); the trucks
    // 0.2 of a car's length in from each end (TrainOnLine.UpdatePoses), their axles a metre either side.
    const double DriverWheelM = 1.7, RailM = 12, AxleOffsetM = 1.0;

    readonly Pcg32Ish _trainRng = new(20261002);
    readonly List<(double At, string Cue, Double3 Where, float Occlusion, float Volume)> _cuesLater = new();
    readonly List<(double At, Double3 Where, float Volume, bool RunIn)> _slackQueue = new();
    readonly Dictionary<int, double> _axles = new();
    readonly Dictionary<int, double> _preDerail = new();
    readonly List<Wreck.Happening> _happened = new();
    bool _exposed, _trainPrimed, _derailedSeen;
    double _trainClock = double.NaN, _chuffBeats, _slackAccel, _valveLiftedAt = double.NegativeInfinity, _derailedAt;
    Wreck? _wreck;
    TrainDynamics? _wreckRake;

    /// <summary>The derailment being heard, if the train's off the rails (for the tests and `dt audio`).</summary>
    public Wreck? Derailment => _wreck;

    partial void EndNightTrain()
    {
        _cuesLater.Clear();
        _slackQueue.Clear();
        _axles.Clear();
        _preDerail.Clear();
        _happened.Clear();
        _trainPrimed = _derailedSeen = false;
        _trainClock = double.NaN;
        _chuffBeats = _slackAccel = _derailedAt = 0;
        _valveLiftedAt = double.NegativeInfinity;
        _wreck = null;
        _wreckRake = null;
    }

    /// <summary>+1 the tick <paramref name="now"/> turns true, −1 the tick it turns false, 0 otherwise. Nothing on the first tick heard.</summary>
    int Flipped(string key, int owner, bool now, bool primed)
    {
        bool was = _edges.GetValueOrDefault((key, owner));
        _edges[(key, owner)] = now;
        return !primed || was == now ? 0 : now ? 1 : -1;
    }

    /// <summary>A one-shot later, in seconds (debris after a burst, a coupling's clunk down the train).</summary>
    void CueLater(double seconds, string cue, Double3 at, float occlusion = 0, float volume = 1) => _cuesLater.Add((_time + seconds, cue, at, occlusion, volume));

    /// <summary>A loop held at <paramref name="volume"/>, or let go if that's next to nothing.</summary>
    void HoldLevel(string name, int owner, Double3 at, float occlusion, double volume)
    {
        if (volume > 0.01 && Hold(name, owner, at, occlusion) is { } v)
            v.Volume = (float)volume;
    }

    /// <summary>
    /// Two loops crossfaded by <paramref name="x"/> (0 all <paramref name="a"/>, 1 all <paramref name="b"/>), at
    /// <paramref name="level"/>; with only one installed, it carries the whole level.
    /// </summary>
    void HoldCrossfade(string a, string b, int owner, double x, Double3 at, float occlusion, double level, string? param = null, double value = 0)
    {
        bool ha = HasCue(a), hb = HasCue(b);
        x = Math.Clamp(x, 0, 1);
        HoldLevel(a, owner, at, occlusion, level * (hb ? 1 - x : 1));
        HoldLevel(b, owner, at, occlusion, level * (ha ? x : 1));
        if (param is not null)
            foreach (var name in new[] { a, b })
                if (_held.TryGetValue((name, owner), out var v))
                    v.Params.Set(param, value);
    }

    /// <summary>A Poisson clock: true about <paramref name="perSecond"/> times a second.</summary>
    bool Odds(double perSecond, double dt) => _trainRng.Next() < perSecond * dt;

    partial void TrainSounds(World world)
    {
        var train = world.Train;
        double dt = double.IsNaN(_trainClock) ? SimConstants.TickSeconds : Math.Max(0, _time - _trainClock);
        _trainClock = _time;
        bool primed = _trainPrimed;
        StandDownSynths();
        var engine = train.Frames[0];
        var rake = train.Dynamics;
        bool derailed = world.Derailed;

        BedRoar(train, engine);
        BedExhaust(train, engine, rake, dt, derailed);
        BedWheels(world, train, dt, derailed);
        BedBrakes(train, rake, primed, derailed);
        BedAirflow(world, train, rake, dt, derailed);
        BedLabour(train, rake, dt, derailed);
        BoilerAlarms(train, engine, dt, primed);
        TrainDamage(train, engine, rake);
        Derailing(world, train, dt, primed);

        for (int i = _cuesLater.Count - 1; i >= 0; i--)
            if (_cuesLater[i].At <= _time)
            {
                var s = _cuesLater[i];
                _cuesLater.RemoveAt(i);
                Cue(s.Cue, s.Where, s.Occlusion, s.Volume);
            }
        _trainPrimed = true;
    }

    /// <summary>A synth whose cue was installed while it played (the bank hot-reloads) stops for good.</summary>
    void StandDownSynths()
    {
        void Stop(ref SoundInstance? v, string name)
        {
            if (v is not null && Sampled(name))
            {
                v.Stop();
                v = null;
            }
        }
        Stop(ref _roar, "boiler-roar");
        Stop(ref _chuff, "chuff");
        Stop(ref _brake, "brake");
        Stop(ref _wind, "wind");
        Stop(ref _valve, "safety-valve");
        Stop(ref _strain, "boiler-strain");
        Stop(ref _vent, "vent-hiss");
        if (_wheels.Count > 0 && Sampled("wheel-rail"))
        {
            foreach (var w in _wheels.Values)
                w.Stop();
            _wheels.Clear();
        }
    }

    // ---- The bed ---------------------------------------------------------------------------------------------------------

    /// <summary>bed-boiler-roar: the fire and the boiler, low at a working pressure, crossing to the high roar near the redline.</summary>
    void BedRoar(TrainOnLine train, CarFrame engine)
    {
        var boiler = train.Boiler;
        if (boiler.Ruptured)
            return;
        var bt = train.BoilerTuning;
        double redline = bt?.Redline ?? 95, fire = bt is null ? 0.7 : boiler.FireFraction(bt);
        double x = (boiler.Pressure - redline * 0.6) / (redline * 0.4);
        var at = engine.ToWorld(new Double3(0, 2.6, -engine.Shape.HalfLength * 0.4));
        HoldCrossfade("bed-boiler-roar.roar-low", "bed-boiler-roar.roar-high", 0, x, at, Occlusion(0), 0.45 + 0.55 * Math.Clamp(fire, 0, 1),
            "pressure", boiler.Pressure);
    }

    /// <summary>
    /// bed-chuff: an exhaust beat four times a turn of the driving wheels while there's steam through the cylinders (heavy when
    /// it's pulling hard), and the side rods' knock once a turn whenever the wheels go round.
    /// </summary>
    void BedExhaust(TrainOnLine train, CarFrame engine, TrainDynamics rake, double dt, bool derailed)
    {
        if (!Sampled("chuff") || derailed)
            return;
        double speed = rake.Speed;
        double before = _chuffBeats;
        _chuffBeats += speed * dt * 4 / (Math.PI * DriverWheelM);
        if (Math.Floor(_chuffBeats) == Math.Floor(before))
            return;
        var boiler = train.Boiler;
        var bt = train.BoilerTuning;
        // The effort it's making: under steam drive (T97) as hard as it takes to make the speed its steam allows.
        double effort = bt is null ? train.LastControls.Throttle
            : bt.SteamDrive ? Math.Clamp((boiler.SteamSpeed(bt, rake.Tuning.MaxSpeed) - speed) / bt.DriveSpeedBand, 0, 1)
            : train.LastControls.Throttle * boiler.PowerFactor(bt);
        bool steam = !boiler.Ruptured && (bt is null || boiler.Pressure > bt.PowerFloor);
        float occlusion = Occlusion(0);
        if (steam)
        {
            string beat = effort > 0.6 && HasCue("bed-chuff.chuff-heavy") ? "bed-chuff.chuff-heavy" : "bed-chuff.chuff";
            Cue(beat, engine.ToWorld(new Double3(0, 3.5, -engine.Shape.HalfLength * 0.7)), occlusion, (float)(0.4 + 0.6 * effort));
        }
        if ((long)Math.Floor(_chuffBeats) % 4 == 0)
        {
            double side = (long)Math.Floor(_chuffBeats) % 8 == 0 ? 1 : -1;
            Cue("bed-chuff.rod-clank", engine.ToWorld(new Double3(side * 1.4, 0.9, -engine.Shape.HalfLength * 0.5)), occlusion,
                (float)Math.Clamp(speed / 10, 0.25, 0.9));
        }
    }

    /// <summary>
    /// bed-wheel-rail under the cars nearest the listener: the roll (slow crossing to fast), a click at every rail joint each
    /// axle runs over, and the flanges squealing on a curve; past what the curve will take, state-derail's flange scream,
    /// the derailment's own warning (GDD §23), from every car that's over.
    /// </summary>
    void BedWheels(World world, TrainOnLine train, double dt, bool derailed)
    {
        if (derailed)
            return;
        var ear = Mixer.Listener.Position;
        double aDerail = world.TrackPlan?.Rules.ADerail ?? 1.0;
        bool rolling = Sampled("wheel-rail");
        var near = train.Frames.OrderBy(f => (f.Origin - ear).Length).Take(4).Select(f => f.Index).ToHashSet();
        foreach (var rake in train.Rakes)
        {
            double speed = rake.Speed;
            foreach (var v in rake.Consist.Vehicles)
            {
                var frame = train.Frames[v.Id];
                var pose = train.Cars[v.Id];
                double length = pose.Length, centre = pose.FrontDistance - length / 2;
                double k = Math.Abs(train.Line.Sample(rake.Path, centre).Curvature);
                double pull = speed * speed * k / aDerail;
                double scream = Math.Max(Math.Clamp((pull - 0.85) / 0.25, 0, 1), BoardScream(world, train, rake, v, pose));
                if (scream > 0)
                    HoldLevel("state-derail.flange-scream", v.Id, frame.ToWorld(new Double3(0, 0.4, 0)), Occlusion(PlayerMotor.Outside), 0.4 + 0.6 * scream);
                if (!near.Contains(v.Id))
                    continue;
                var under = frame.ToWorld(new Double3(0, 0.5, 0));
                if (rolling)
                    HoldCrossfade("bed-wheel-rail.roll-slow", "bed-wheel-rail.roll-fast", v.Id, (speed - 8) / 10, under, 0,
                        Math.Pow(Math.Clamp(speed / 12, 0, 1), 0.8), "speed", speed);
                if (scream <= 0 && speed > 3 && pull > 0.35)
                    HoldLevel("bed-wheel-rail.flange", v.Id, under, 0, Math.Clamp((pull - 0.35) / 0.4, 0.15, 1));
                // The rail joints, axle by axle (the synth's clicks are in its own roll, while it plays).
                for (int a = 0; a < 4; a++)
                {
                    double z = (a < 2 ? 0.2 : 0.8) * length + (a % 2 == 0 ? -AxleOffsetM : AxleOffsetM);
                    double along = pose.FrontDistance - z;
                    int key = v.Id * 4 + a;
                    bool crossed = _axles.TryGetValue(key, out double was) && Math.Floor(was / RailM) != Math.Floor(along / RailM)
                        && Math.Abs(along - was) < RailM;
                    _axles[key] = along;
                    if (!crossed || !rolling || speed < 0.3)
                        continue;
                    var at = frame.ToWorld(new Double3(0, 0.3, z - length / 2));
                    float volume = (float)Math.Clamp(0.3 + speed / 20, 0.3, 1);
                    if (HasCue("bed-wheel-rail.joint"))
                        Cue("bed-wheel-rail.joint", at, 0, volume);
                    else
                        Mixer.Play("rail-joint", at, volume);
                }
            }
        }
    }

    /// <summary>How far over a lineside speed board a car is, towards what derails it there (Lineside.Hazards): 0 within the lurch.</summary>
    static double BoardScream(World world, TrainOnLine train, TrainDynamics rake, Vehicle v, CarPose pose)
    {
        if (world.Lineside is not { } lineside || rake.Path != RailLine.MainPath)
            return 0;
        var t = lineside.Tuning;
        double front = pose.FrontDistance, rear = front - pose.Length, worst = 0;
        foreach (var sign in lineside.Signs)
            if (sign.Kind == Sim.Route.SignKind.SpeedLimit && front >= sign.Start && rear <= sign.End && sign.Limit > 0)
            {
                double lurch = sign.Limit + t.LurchOver, derail = sign.Limit * t.DerailRatio;
                if (rake.Speed > lurch && derail > lurch)
                    worst = Math.Max(worst, Math.Clamp((rake.Speed - lurch) / (derail - lurch), 0.05, 1));
            }
        return worst;
    }

    /// <summary>
    /// Slack action (spec A.2: "the highest-value single sound in the game"): a change in pull runs down the consist as one
    /// clunk per coupling, front to back. The cars close up (run-in) when the pull eases or the brake goes on, and stretch
    /// out (run-out) when it pulls harder; each from its own cue, or the synth's clunk where that one isn't installed.
    /// </summary>
    void Slack(TrainOnLine train, TrainDynamics d)
    {
        double accel = d.Acceleration;
        double change = accel - _slackAccel;
        _slackAccel = accel;
        if (Math.Abs(change) > 0.12 && _slackQueue.Count == 0 && !_derailedSeen)
        {
            double travel = d.Velocity != 0 ? Math.Sign(d.Velocity) : train.LastControls.Reverser >= 0 ? 1 : -1;
            bool runIn = change * travel < 0;
            float volume = (float)Math.Clamp(Math.Abs(change) / 0.8, 0.3, 1);
            int k = 0;
            foreach (var rake in train.Rakes)
                for (int i = 0; i + 1 < rake.Consist.Vehicles.Count; i++, k++)
                {
                    var frame = train.Frames[rake.Consist.Vehicles[i].Id];
                    _slackQueue.Add((_time + k * 0.11, frame.ToWorld(new Double3(0, 1.0, frame.Shape.HalfLength)), volume, runIn));
                }
        }
        for (int i = _slackQueue.Count - 1; i >= 0; i--)
            if (_slackQueue[i].At <= _time)
            {
                var s = _slackQueue[i];
                string cue = s.RunIn ? "bed-slack.run-in" : "bed-slack.run-out";
                Mixer.Play(HasCue(cue) ? cue : "slack-clunk", s.Where, s.Volume);
                _slackQueue.RemoveAt(i);
            }
    }

    /// <summary>
    /// bed-brake under the car nearest the listener: the shoes going on, dragging (harsher as they heat), state-brake-fade's
    /// glazing as they lose their bite on a long descent (TrainDynamics.BrakeEfficiency), and coming off.
    /// </summary>
    void BedBrakes(TrainOnLine train, TrainDynamics rake, bool primed, bool derailed)
    {
        double speed = rake.Speed, brake = train.LastControls.Brake;
        bool braking = brake > 0 && speed > 0.3 && !derailed;
        var ear = Mixer.Listener.Position;
        var at = train.Frames.MinBy(f => (f.Origin - ear).Length).ToWorld(new Double3(0, 0.5, 0));
        int edge = Flipped("bed-brake", 0, braking, primed);
        if (edge > 0)
            Cue("bed-brake.apply", at, 0, (float)Math.Clamp(speed / 10, 0.4, 1));
        else if (edge < 0)
            Cue("bed-brake.release", at);
        if (!braking)
            return;
        double min = rake.Tuning.BrakeFade.MinEfficiency;
        double heat = Math.Clamp((1 - rake.BrakeEfficiency) / Math.Max(1e-6, 1 - min), 0, 1);
        double hot = Math.Clamp(heat / 0.5, 0, 1), fading = HasCue("state-brake-fade.fade") ? Math.Clamp((heat - 0.5) / 0.5, 0, 1) : 0;
        double level = Math.Clamp(speed / 12, 0.3, 1) * Math.Clamp(brake, 0.3, 1);
        HoldCrossfade("bed-brake.drag", "bed-brake.drag-hot", 0, hot, at, 0, level * (1 - fading), "load", brake * speed);
        HoldLevel("state-brake-fade.fade", 0, at, 0, level * fading);
    }

    /// <summary>bed-wind: the air past a listener out in it, slow crossing to fast with the train's speed and the night's wind; and gusts.</summary>
    void BedAirflow(World world, TrainOnLine train, TrainDynamics rake, double dt, bool derailed)
    {
        if (!_exposed)
            return;
        var ear = Mixer.Listener.Position;
        double night = (world.Run?.Route ?? world.Route)?.Weather.Wind ?? 0;
        double air = (derailed ? 0 : rake.Speed) + 8 * night;
        if (Sampled("wind"))
            HoldCrossfade("bed-wind.wind-slow", "bed-wind.wind-fast", 0, (air - 6) / 10, ear + Double3.Up * 0.5, 0, Math.Clamp(air / 10, 0, 1), "wind", air);
        if (air > 6 && Odds(0.03 + 0.1 * Math.Clamp(air / 22, 0, 1), dt))
            Cue("bed-wind.gust", ear + Mixer.Listener.Right * (_trainRng.Next() < 0.5 ? -3 : 3), 0, (float)(0.5 + 0.5 * _trainRng.Next()));
    }

    /// <summary>bed-groan: the whole train labouring up a grade; and the frames creaking, more on curves.</summary>
    void BedLabour(TrainOnLine train, TrainDynamics rake, double dt, bool derailed)
    {
        if (derailed)
            return;
        double speed = rake.Speed;
        double travel = rake.Velocity != 0 ? Math.Sign(rake.Velocity) : train.LastControls.Reverser >= 0 ? 1 : -1;
        double climb = train.AverageGrade() * travel;
        var ear = Mixer.Listener.Position;
        var nearest = train.Frames.MinBy(f => (f.Origin - ear).Length);
        if (climb > 0.8 && speed > 0.5 && speed < 12)
            HoldLevel("bed-groan.groan", 0, nearest.ToWorld(new Double3(0, 1.2, 0)), Occlusion(nearest.Index), Math.Clamp((climb - 0.8) / 2, 0.25, 1));
        if (speed < 0.5)
            return;
        double k = Math.Abs(train.Line.Sample(rake.Path, rake.Distance).Curvature);
        if (Odds(0.05 + Math.Min(0.6, speed * speed * k * 0.8) + Math.Max(0, climb - 1) * 0.1, dt))
        {
            double z = (_trainRng.Next() * 2 - 1) * nearest.Shape.HalfLength;
            Cue("bed-groan.creak", nearest.ToWorld(new Double3(0, 1.5, z)), Occlusion(nearest.Index), (float)(0.4 + 0.6 * _trainRng.Next()));
        }
    }

    // ---- The boiler ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The vent held open (bed-vent), the safety valve lifting at the redline (state-valve), the boiler straining in the red
    /// towards a rupture (state-strain: ticks and rivets pinging, more as it nears it), and the rupture (state-rupture:
    /// the burst, debris coming down, and the steam pouring out of the wreck of it).
    /// </summary>
    void BoilerAlarms(TrainOnLine train, CarFrame engine, double dt, bool primed)
    {
        var boiler = train.Boiler;
        var bt = train.BoilerTuning;
        double l = engine.Shape.HalfLength;
        float occlusion = Occlusion(0);

        var pipe = engine.ToWorld(new Double3(0, 4.0, -l * 0.6));
        bool vented = boiler.Vented && !boiler.Ruptured;
        int vent = Flipped("bed-vent", 0, vented, primed);
        if (vent > 0)
            Cue("bed-vent.open", pipe, occlusion);
        else if (vent < 0)
            Cue("bed-vent.close", pipe, occlusion);
        if (vented)
            HoldLevel("bed-vent.blow", 0, pipe, occlusion, 1);

        // It lifts and reseats tick by tick while it holds the pressure there; heard as one lift until it's been shut a moment.
        var valve = engine.ToWorld(new Double3(0, 4.3, -l * 0.1));
        if (boiler.SafetyValveLifting)
            _valveLiftedAt = _time;
        bool lifting = !boiler.Ruptured && _time - _valveLiftedAt < 0.6;
        int lift = Flipped("state-valve", 0, lifting, primed);
        if (lift > 0)
            Cue("state-valve.lift", valve, occlusion);
        else if (lift < 0 && !boiler.Ruptured)
            Cue("state-valve.reseat", valve, occlusion);
        if (lifting)
            HoldLevel("state-valve.blow", 0, valve, occlusion, 1);

        double strain = bt is null || boiler.Ruptured || boiler.Pressure < bt.Redline ? 0
            : 0.6 * Math.Clamp((boiler.Pressure - bt.Redline) / Math.Max(1, bt.PressureMax - bt.Redline), 0, 1) + 0.4 * Math.Clamp(boiler.AtMaxSeconds / bt.RuptureHoldSeconds, 0, 1);
        if (strain > 0)
        {
            Double3 Plate() => engine.ToWorld(new Double3((_trainRng.Next() * 2 - 1) * 0.8, 2.6 + 0.6 * _trainRng.Next(), -l * (0.15 + 0.6 * _trainRng.Next())));
            HoldLevel("state-strain.groan", 0, engine.ToWorld(new Double3(0, 3.2, -l * 0.3)), occlusion, 0.4 + 0.6 * strain);
            if (_held.TryGetValue(("state-strain.groan", 0), out var groan))
                groan.Params.Set("strain", strain);
            if (Odds(0.4 + 3 * strain, dt))
                Cue("state-strain.tick", Plate(), occlusion, (float)(0.5 + 0.5 * strain));
            if (Odds(0.05 + strain * strain, dt))
                Cue("state-strain.rivet", Plate(), occlusion, (float)(0.6 + 0.4 * strain));
        }

        var shell = engine.ToWorld(new Double3(0, 2.6, -l * 0.4));
        if (Flipped("state-rupture", 0, boiler.Ruptured, primed) > 0)
        {
            Cue("state-rupture.burst", shell, occlusion);
            // What it threw up coming down round the engine.
            int pieces = 3 + (int)(_trainRng.Next() * 3);
            for (int i = 0; i < pieces; i++)
                CueLater(0.5 + 2 * _trainRng.Next(), "state-rupture.debris",
                    engine.ToWorld(new Double3((_trainRng.Next() * 2 - 1) * 7, 0.2, (_trainRng.Next() * 2 - 1) * l * 1.2)), 0, (float)(0.5 + 0.5 * _trainRng.Next()));
        }
        if (boiler.Ruptured)
            HoldLevel("state-rupture.steam-out", 0, shell, occlusion, 1);
    }

    // ---- Damage ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// state-engine-damage off the engine's integrity (buffer stops, collisions, the Sleepers, brass, overfilling: all
    /// replicated): leaks, then a big leak, and the motion knocking while the wheels go round. (A car's breach and the gun's
    /// fouling are hooked with their own sim state, elsewhere.)
    /// </summary>
    void TrainDamage(TrainOnLine train, CarFrame engine, TrainDynamics rake)
    {
        double l = engine.Shape.HalfLength, integrity = train.Vehicles[0].Integrity;
        float occlusion = Occlusion(0);
        if (integrity < 0.8)
            HoldLevel("state-engine-damage.leak-small", 0, engine.ToWorld(new Double3(0.9, 2.2, -l * 0.5)), occlusion, Math.Clamp((0.8 - integrity) / 0.3, 0.3, 1));
        if (integrity < 0.45)
            HoldLevel("state-engine-damage.leak-large", 0, engine.ToWorld(new Double3(-0.9, 1.8, -l * 0.3)), occlusion, Math.Clamp((0.45 - integrity) / 0.3, 0.4, 1));
        if (integrity < 0.6 && rake.Speed > 0.5)
        {
            HoldLevel("state-engine-damage.knock", 0, engine.ToWorld(new Double3(1.3, 0.9, -l * 0.75)), occlusion, Math.Clamp(rake.Speed / 10, 0.3, 1));
            if (_held.TryGetValue(("state-engine-damage.knock", 0), out var knock))
                knock.Params.Set("speed", rake.Speed);
        }
    }

    // ---- Derailment ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// state-derail: on the tick World.Derailed comes on, the wreck the train was in for (<see cref="Wreck"/>) from each car's
    /// speed the tick before, its curve, its mass and load, and the track under it; then each thing that happens in it as
    /// its own sound, where it happens: wheels climbing the rail, cars slamming together and tearing, going over and
    /// hitting the ground, scraping on the rails and grinding along the ballast, settling. The engine's pipes tear as the
    /// art shows them (Art/Effects.Derailment), so it bursts and hisses on.
    /// </summary>
    void Derailing(World world, TrainOnLine train, double dt, bool primed)
    {
        int edge = Flipped("state-derail", 0, world.Derailed, primed);
        if (!world.Derailed)
        {
            _wreck = null;
            _derailedSeen = false;
            foreach (var rake in train.Rakes)
                foreach (var v in rake.Consist.Vehicles)
                    _preDerail[v.Id] = rake.Velocity;
            return;
        }
        _derailedSeen = true;
        if (edge > 0)
        {
            _derailedAt = _time;
            _slackQueue.Clear();
            (_wreck, _wreckRake) = StartWreck(world, train);
            if (!train.Boiler.Ruptured)
                Cue("state-rupture.burst", train.Frames[0].ToWorld(new Double3(0, 2.6, -train.Frames[0].Shape.HalfLength * 0.4)), Occlusion(0),
                    (float)Math.Clamp(Math.Abs(_preDerail.GetValueOrDefault(0)) / 12, 0.5, 1));
        }
        // The hiss of the torn pipes: hard for the first seconds, then on and on (as the art's steam goes).
        if (edge > 0 || _wreck is not null)
        {
            double since = _time - _derailedAt;
            var e = train.Frames[0];
            HoldLevel("state-rupture.steam-out", 1, e.ToWorld(new Double3(0, 2.4, -e.Shape.HalfLength * 0.4)), Occlusion(0), since < 3 ? 1 - 0.65 * since / 3 : 0.35);
        }
        if (_wreck is not { } wreck || _wreckRake is not { } wreckRake)
            return;
        _happened.Clear();
        wreck.Advance(dt, _happened);
        double travel = _preDerail.GetValueOrDefault(wreckRake.Consist.Vehicles.Count > 0 ? wreckRake.Consist.Vehicles[0].Id : 0) >= 0 ? 1 : -1;
        foreach (var h in _happened)
            WreckHeard(train, wreck, h, travel);
        for (int i = 0; i < wreck.Cars.Count; i++)
        {
            var c = wreck.Cars[i];
            var frame = train.Frames[c.From.Id];
            if (c.Scraping)
                HoldLevel("state-derail.rail-scrape", c.From.Id, frame.ToWorld(new Double3(0, 0.3, 0)), Occlusion(c.From.Id), Math.Clamp(c.V / 10, 0.25, 1));
            if (c.Grinding)
                HoldLevel("state-derail.grind", c.From.Id, frame.ToWorld(new Double3(0, 0.6, 0)), Occlusion(c.From.Id), Math.Clamp(c.V / 8, 0.3, 1));
        }
    }

    /// <summary>One thing the wreck did, as its sound, where it happened on the train.</summary>
    void WreckHeard(TrainOnLine train, Wreck wreck, Wreck.Happening h, double travel)
    {
        var car = wreck.Cars[h.Car];
        var frame = train.Frames[car.From.Id];
        double hl = frame.Shape.HalfLength;
        float volume = (float)h.Strength, occlusion = Occlusion(car.From.Id);
        // Its front in the way it was going (a car's −Z is its front), and the coupling to the car ahead of it.
        double lead = travel > 0 ? -1 : 1;
        Double3 Coupling() => frame.ToWorld(new Double3(0, 1.0, lead * (hl + 0.75)));
        switch (h.Kind)
        {
            case Wreck.Kind.Climb:
                Cue("state-derail.climb", frame.ToWorld(new Double3(0, 0.4, lead * hl * 0.65)), occlusion, volume);
                break;
            case Wreck.Kind.Collide:
                Cue("state-derail.collide", Coupling(), occlusion, volume);
                break;
            case Wreck.Kind.RunIn:
                Mixer.Play(HasCue("bed-slack.run-in") ? "bed-slack.run-in" : "slack-clunk", Coupling(), volume);
                break;
            case Wreck.Kind.RunOut:
                Mixer.Play(HasCue("bed-slack.run-out") ? "bed-slack.run-out" : "slack-clunk", Coupling(), volume);
                break;
            case Wreck.Kind.Tear:
                Cue("state-derail.tear", h.Other >= 0 ? Coupling() : frame.ToWorld(new Double3(0, 2, 0)), occlusion, volume);
                break;
            case Wreck.Kind.Tip:
                Cue("state-derail.tip", frame.ToWorld(new Double3(0, 2, 0)), occlusion, volume);
                break;
            case Wreck.Kind.Impact:
                Cue("state-derail.impact", "ground", frame.ToWorld(new Double3(0, 0.5, 0)), occlusion, volume);
                break;
            case Wreck.Kind.Settle:
                Cue("state-derail.settle", frame.ToWorld(new Double3(0, 1.5, (_trainRng.Next() * 2 - 1) * hl)), occlusion, volume);
                break;
        }
    }

    /// <summary>
    /// The wreck the train's in for, from the tick before it came off: the engine's rake front to back the way it was
    /// going, each car's speed, how hard its curve was throwing it out, how top-heavy it is, and whether the track's gone
    /// from under it (a washout, or a weak span over its limit: TrackRules). The first car over its curve's limit comes off
    /// first; failing that, one where the track's gone; failing that, the leading one (the Sleepers, the Switchman's points).
    /// </summary>
    (Wreck?, TrainDynamics?) StartWreck(World world, TrainOnLine train)
    {
        var rake = train.Dynamics;
        var t = rake.Tuning;
        var plan = world.TrackPlan;
        double aDerail = plan?.Rules.ADerail ?? 1.0;
        var vehicles = rake.Consist.Vehicles;
        if (vehicles.Count == 0)
            return (null, null);
        double velocity = _preDerail.GetValueOrDefault(vehicles[0].Id);
        bool forward = velocity >= 0;
        var order = forward ? vehicles.ToList() : vehicles.Reverse().ToList();
        var starts = new List<Wreck.Start>();
        foreach (var v in order)
        {
            var pose = train.Cars[v.Id];
            double speed = Math.Abs(_preDerail.GetValueOrDefault(v.Id, velocity));
            double centre = pose.FrontDistance - pose.Length / 2;
            double k = Math.Abs(train.Line.Sample(rake.Path, centre).Curvature);
            double lateral = speed * speed * k / aDerail;
            if (world.Lineside is { } lineside && rake.Path == RailLine.MainPath)
                foreach (var sign in lineside.Signs)
                    if (sign.Kind == Sim.Route.SignKind.SpeedLimit && sign.Limit > 0 && centre >= sign.Start && centre <= sign.End)
                        lateral = Math.Max(lateral, Math.Pow(speed / (sign.Limit * lineside.Tuning.DerailRatio), 2));
            double top = v.IsEngine ? 0.75 : 0.9 + 0.35 * v.Load + (v.Cargo is CargoKind.Livestock or CargoKind.Grain ? 0.15 : 0);
            starts.Add(new Wreck.Start(v.Id, v.MassTonnes(t), v.Length(t), speed, lateral, top, TrackGone(world, train, plan, rake, centre), CoupledAhead: true));
        }
        int origin = starts.FindIndex(s => s.Lateral >= 0.98);
        if (origin < 0)
            origin = starts.FindIndex(s => s.Drop > 0);
        ulong seed = (ulong)Math.Abs(Math.Round(rake.Distance)) * 2654435761UL;
        // What's still on the rails has its brakes on (the train line's parted): as hard as this train's brakes stop it.
        double brake = Math.Clamp(rake.MaxBrakeForce / Math.Max(1, rake.Consist.MassTonnes), 0.5, 4);
        return (new Wreck(starts, Math.Max(0, origin), t.Geometry.CouplingGap, seed, brake), rake);
    }

    /// <summary>How far a car falls where the track's gone from under it: into a washout, or with a weak span over its limit.</summary>
    static double TrackGone(World world, TrainOnLine train, Sim.LineGen.LinePlan? plan, TrainDynamics rake, double centre)
    {
        if (plan is null)
            return 0;
        var (edge, s) = Sim.LineGen.TrackRules.Locate(plan, train.Line, rake.Path, centre);
        foreach (var st in plan.Structures)
        {
            if (st.Edge != edge || s < st.S0 || s > st.S1)
                continue;
            if (st.Type == Sim.LineGen.StructureType.Washout)
                return 2.5;
            if (st.Weak is { } weak && plan.Rules.WeakBridgeCollapses && rake.Consist.CarCount > weak.MaxCars)
                return Math.Max(3, st.HeightM);
        }
        return 0;
    }
}
