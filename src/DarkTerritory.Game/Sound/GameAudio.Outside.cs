using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The world past the train and the places it stops (tools/audio/cues.py "world-*", "place-*"), the radio as a device
/// ("voice-radio-sfx"), and the Holdouts' prisoners calling out ("voice-callout", "voice-prisoner-sets"). All of it from
/// replicated state and the route every machine generates alike (out/audio/hooks-map.md): the run, the sites and their
/// cranes, the Holdouts, the weather, the line's features and structures. What the world has no state for (the far
/// things in the dark, thunder, a gust, a shutter banging) comes at random from where it would be.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The radio (voice chat), for its clicks, squelch and static; none in a game without the network.</summary>
    public VoiceChat? Voice { get; set; }

    readonly Pcg32Ish _outsideRng = new(20261003);
    readonly Dictionary<int, double> _sleepersSeen = new();
    readonly Dictionary<int, double> _smashAt = new();
    readonly Dictionary<int, double> _craneMoved = new();
    readonly Dictionary<(int, int), CastingState> _castings = new();
    double _earHint = double.NaN, _outsideClock = double.NaN, _engineFrontWas = double.NaN, _engineSpeedWas, _nextFar, _tenderAtPour, _rammedAgain;
    bool _outsidePrimed, _radioWas;
    // Where the grain spout's mouth was while it poured: it's cut off there when the train moves off and it's nowhere.
    // HoldLevel owners for a site's set pieces, clear of the vehicles' ids.
    const int HerdOwner = 10_000, HoseOwner = 11_000, HeapOwner = 12_000, GustOwner = 13_000;
    // The line's gust where the ear was last tick (PlayerMotor.Gust), for the gust cue as one rises.
    double _gustWas;
    Double3 _spoutAt;
    Places? _places;

    /// <summary>What a route has where, worked out once a night (the route is the same on every machine).</summary>
    sealed class Places
    {
        public required Route Route;
        public required RailLine Line;
        public LinePlan? Plan;
        public readonly List<(int Key, RouteFeature Feature)> Tunnels = [];
        /// <summary>Bridges: the plan's edge and span, which loop (or none for a masonry viaduct), and a weak span's limit.</summary>
        public readonly List<(int Key, string Edge, double S0, double S1, string? Loop, int MaxCars, double SpeedMs)> Bridges = [];
        public readonly List<PlanStructure> Brass = [];
        public readonly List<Double3> Houses = [];
        public readonly List<(FacilityKind Kind, Double3 At)> Works = [];
        public readonly int[] VoiceSets = new int[8];
    }

    partial void EndNightOutside()
    {
        _sleepersSeen.Clear();
        _smashAt.Clear();
        _startledUntil.Clear();
        _livestockAccel = double.NaN;
        _craneMoved.Clear();
        _castings.Clear();
        _earHint = _outsideClock = _engineFrontWas = double.NaN;
        _engineSpeedWas = _nextFar = _tenderAtPour = _rammedAgain = 0;
        _outsidePrimed = _radioWas = false;
        _spoutAt = default;
        _places = null;
    }

    double OutsideOdds() => _outsideRng.Next();
    bool Sometimes(double perSecond, double dt) => _outsideRng.Next() < perSecond * dt;

    partial void OutsideSounds(World world)
    {
        var train = world.Train;
        double dt = double.IsNaN(_outsideClock) ? SimConstants.TickSeconds : Math.Max(0, _time - _outsideClock);
        _outsideClock = _time;
        bool primed = _outsidePrimed;
        var ear = Mixer.Listener.Position;
        var line = train.Line;
        if (double.IsNaN(_earHint))
            _earHint = train.Dynamics.Distance;
        var (earPath, earAlong) = line.Nearest(ear, ref _earHint);
        double earMain = line.MainDistance(earPath, earAlong);
        var route = world.Run?.Route ?? world.Route;
        if (route is not null && _places?.Route != route)
            _places = Survey(route, line, world.TrackPlan ?? route.Plan);
        var run = world.Run;
        bool tunnel = route is not null && !double.IsNaN(earMain) && route.InTunnel(earMain);
        var earState = new PlayerState { Parent = PlayerState.World, Position = ear, LineHint = _earHint };
        bool underground = run is not null && run.Underground(earState, train);
        var engine = train.Dynamics;
        double front = line.MainDistance(engine.Path, engine.Distance);

        // Where the radio's dead is the host's test (HostSession.ForwardVoice): F.3's radio range carries it radioReach in
        // from a tunnel's mouth or a spur's points (note 196), so the static starts where the voices stop.
        double reach = engine.Tuning.Kit.RadioReach;
        bool radioDead = route is not null && !double.IsNaN(earMain) && route.DeepInTunnel(earMain, reach)
            || run is not null && run.Underground(earState, train, reach);
        RadioDevice(ear, radioDead, primed);
        RoofGust(train);
        if (_places is { } places)
        {
            WorldNight(world, run, ear, tunnel, underground, dt);
            WorldTunnels(train, places, ear, front, tunnel, dt, primed);
            WorldBridges(train, places, ear, dt);
            WorldWeather(world, train, places, ear, earMain, tunnel, underground, dt);
            WorldBrass(train, places, primed);
            PlaceThreshold(run, train, places, front, primed);
            PlaceWorks(world, run, train, places, ear, underground, dt, primed);
        }
        HoldoutCalls(world, _places, primed);
        WorldDebris(world, train, front);
        WorldLivestock(train, ear);
        _engineFrontWas = engine.Distance;
        _engineSpeedWas = engine.Speed;
        _outsidePrimed = true;
    }

    Places Survey(Route route, RailLine line, LinePlan? plan)
    {
        var p = new Places { Route = route, Line = line, Plan = plan };
        int key = 0;
        foreach (var f in route.Features)
        {
            key++;
            if (f.Kind == FeatureKind.Tunnel)
                p.Tunnels.Add((key, f));
            if (plan is null && f.Kind == FeatureKind.Bridge)
                p.Bridges.Add((key, "main", f.Start, f.End, "world-bridges.iron-drum", f.MaxCars, 7));
            if (f.Stop is { } stop)
                foreach (var b in stop.Buildings)
                {
                    var at = Sim.Run.Run.StopWorld(line, f, b.Centre);
                    if (b.Zone == StopZone.Village && b.Kind is BuildingKind.House or BuildingKind.Outbuilding or BuildingKind.Barn)
                        p.Houses.Add(at);
                    if (f.Facility is { } kind && b.Zone == StopZone.Yard && b.Kind is BuildingKind.Shed or BuildingKind.Hero)
                        p.Works.Add((kind, at));
                }
            // A facility without a laid-out stop: its works stand off the line's side, mid-zone.
            if (f.Kind == FeatureKind.Facility && f.Facility is { } k2 && f.Stop is null)
            {
                var t = line.Sample((f.Start + f.End) / 2);
                p.Works.Add((k2, t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * ((f.Side == 0 ? 1 : f.Side) * 25)));
            }
        }
        if (plan is not null)
            foreach (var st in plan.Structures)
            {
                key++;
                switch (st.Type)
                {
                    case StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Viaduct:
                        // Timber trestles (and anything built of timber) under the wheels; iron girders and trusses drum; a
                        // masonry viaduct is the ground going on (LineBuilder.Structures: timber weak, masonry viaduct, else iron).
                        string? loop = st.Type == StructureType.Trestle || st.Material == "timber" ? "world-bridges.timber"
                            : st.Type == StructureType.Viaduct || st.Material == "masonry" ? null : "world-bridges.iron-drum";
                        p.Bridges.Add((key, st.Edge, st.S0, st.S1, loop, st.Weak?.MaxCars ?? 0, st.Weak?.SpeedMs ?? 0));
                        break;
                    case StructureType.BrassField:
                        p.Brass.Add(st);
                        break;
                }
            }
        // Each prisoner calls in one voice for the whole run (voice-prisoner-sets): the eight sets dealt out by the night's
        // seed, a player's by their id, so no two on a run share one (ids are handed out in turn) and every machine agrees.
        var deal = new Pcg32Ish(route.Seed * 0x9E3779B97F4A7C15UL + 7);
        for (int i = 0; i < 8; i++)
            p.VoiceSets[i] = i + 1;
        for (int i = 7; i > 0; i--)
        {
            int j = (int)(deal.Next() * (i + 1));
            (p.VoiceSets[i], p.VoiceSets[j]) = (p.VoiceSets[j], p.VoiceSets[i]);
        }
        return p;
    }

    // ---- The radio ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// voice-radio-sfx, your own set: keyed and released at your chest, and static while you key it where it's dead (in a
    /// tunnel, down the mine spur: VoiceRouting sends nothing from there). A crewmate's coming over it is VoiceChat's own
    /// (RadioSet: their click, the static as the signal breaks up, the squelch tail).
    /// </summary>
    void RadioDevice(Double3 ear, bool deadZone, bool primed)
    {
        if (Voice is not { } voice)
            return;
        var chest = ear - Double3.Up * 0.4;
        bool held = voice.RadioHeld;
        if (primed && held != _radioWas)
            Cue(held ? "voice-radio-sfx.key-down" : "voice-radio-sfx.key-up", chest);
        _radioWas = held;
        if (held && deadZone)
            HoldLevel("voice-radio-sfx.static", 0, chest, 0, 1);
    }

    /// <summary>
    /// The wind on a roof's footing heard (GDD §22, spec B.2; note 201's push, note 239): up on a roof, the gust that's
    /// pushing you is a gale's roar on the side it blows from, as loud as it pushes (PlayerMotor.WindPush, the sim's own
    /// sum, so it's the gust you're in), so you hear it build before it walks you to the edge, and hear it ease off.
    /// </summary>
    void RoofGust(TrainOnLine train)
    {
        if (OwnId < 0 || PlayerTuning is not { } p)
            return;
        foreach (var (id, s) in CrewStates)
        {
            if (id != OwnId || s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
                continue;
            double push = PlayerMotor.WindPush(s, OwnIntent, train, p, train.Dynamics.Tuning);
            // The worst a roof gets: exposed track's 1.5× of a full night's wind, flat out.
            double worst = 1.5 * p.Wind.Drift;
            if (Math.Abs(push) < 0.02 * worst)
                return;
            // A push to the car's right comes off its left.
            var frame = train.Frames[s.Parent];
            HoldLevel("world-wind.gale", GustOwner, frame.ToWorld(s.Position + new Double3(-Math.Sign(push) * 5, 1.6, 0)), 0,
                Math.Clamp(Math.Abs(push) / worst, 0.15, 1));
            return;
        }
    }

    // ---- The world ---------------------------------------------------------------------------------------------------------

    /// <summary>world-night: the wilderness at night while the train's out in it (thinning towards dawn), and things far off in it.</summary>
    void WorldNight(World world, Run? run, Double3 ear, bool tunnel, bool underground, double dt)
    {
        if (run is null || run.Phase is not (RunPhase.Underway or RunPhase.AtFacility))
            return;
        double dawn = Math.Clamp(run.DawnIn / 300, 0.35, 1);
        HoldLevel("world-night.night", 0, ear + Double3.Up * 3, Occlusion(PlayerMotor.Outside), (tunnel || underground ? 0.2 : 1) * dawn);
        if (tunnel || underground || _time < _nextFar)
            return;
        if (_nextFar > 0)
        {
            double a = OutsideOdds() * 2 * Math.PI, r = 120 + 140 * OutsideOdds();
            Cue("world-night.far", ear + new Double3(Math.Cos(a) * r, 4 + 10 * OutsideOdds(), Math.Sin(a) * r), Occlusion(PlayerMotor.Outside),
                (float)(0.5 + 0.5 * OutsideOdds()));
        }
        _nextFar = _time + 10 + 20 * OutsideOdds();
    }

    /// <summary>world-tunnels: the engine going in at one portal and out of the other, the bore around you, and water dripping in it.</summary>
    void WorldTunnels(TrainOnLine train, Places places, Double3 ear, double front, bool inside, double dt, bool primed)
    {
        var engine = train.Dynamics;
        float volume = (float)Math.Clamp(engine.Speed / 12, 0.4, 1);
        foreach (var (key, f) in places.Tunnels)
        {
            int edge = Flipped("world-tunnels", key, !double.IsNaN(front) && front >= f.Start && front <= f.End, primed);
            if (edge == 0)
                continue;
            // In at the portal it was coming to; out at the one it's past.
            bool atStart = edge > 0 ? engine.Velocity >= 0 : front < f.Start;
            var portal = places.Line.Sample(atStart ? f.Start : f.End).Position + Double3.Up * 3;
            Cue(edge > 0 ? "world-tunnels.enter" : "world-tunnels.exit", portal, Occlusion(PlayerMotor.Outside), volume);
        }
        if (!inside)
            return;
        HoldLevel("world-tunnels.inside", 0, ear, 0, 1);
        if (Sometimes(0.4, dt))
        {
            var t = places.Line.Sample(_earHint + (OutsideOdds() * 2 - 1) * 12);
            var side = Double3.Cross(t.Tangent, Double3.Up).Normalized * ((OutsideOdds() * 2 - 1) * 2);
            Cue("world-tunnels.drip", t.Position + side + Double3.Up * 4.5, Occlusion(PlayerMotor.Outside), (float)(0.4 + 0.6 * OutsideOdds()));
        }
    }

    /// <summary>
    /// world-bridges: the wheels drumming on an iron span or rumbling over a timber trestle, from under the car on it nearest
    /// you; a weak span groaning under a train near its limit (cars over its limit, speed over its crossing speed).
    /// </summary>
    void WorldBridges(TrainOnLine train, Places places, Double3 ear, double dt)
    {
        if (places.Bridges.Count == 0)
            return;
        var cars = new List<(string Edge, double S, Double3 Under)>();
        foreach (var rake in train.Rakes)
        {
            if (rake.Speed < 0.3)
                continue;
            // Where each car is on the plan's track (the edge it's on, and how far along it), once a tick.
            cars.Clear();
            foreach (var v in rake.Consist.Vehicles)
            {
                var pose = train.Cars[v.Id];
                double centre = pose.FrontDistance - pose.Length / 2;
                var (edge, s) = places.Plan is { } plan ? TrackRules.Locate(plan, places.Line, rake.Path, centre) : ("main", places.Line.MainDistance(rake.Path, centre));
                cars.Add((edge, s, train.Frames[v.Id].ToWorld(new Double3(0, -0.6, 0))));
            }
            foreach (var bridge in places.Bridges)
            {
                var on = cars.Where(c => c.Edge == bridge.Edge && c.S >= bridge.S0 && c.S <= bridge.S1).ToList();
                if (on.Count == 0)
                    continue;
                var at = on.MinBy(c => (c.Under - ear).Length).Under;
                if (bridge.Loop is { } loop)
                    HoldLevel(loop, bridge.Key, at, Occlusion(PlayerMotor.Outside), Math.Clamp(rake.Speed / 10, 0.2, 1) * (0.6 + 0.4 * Math.Min(1, on.Count / 4.0)));
                if (bridge.MaxCars > 0)
                {
                    double stress = (double)rake.Consist.CarCount / bridge.MaxCars * (bridge.SpeedMs > 0 ? Math.Max(0.5, rake.Speed / bridge.SpeedMs) : 1);
                    if (Sometimes(0.2 + 1.2 * Math.Clamp(stress - 0.5, 0, 1.5), dt))
                        Cue("world-bridges.groan", at + Double3.Up * -1.5, Occlusion(PlayerMotor.Outside), (float)Math.Clamp(stress, 0.3, 1));
                }
            }
        }
    }

    /// <summary>
    /// world-rain (on the roof over you, or all round you out in it; thunder far off on a wet night; the drivers slipping
    /// where the rail's wet or greased and the engine's working hard) and world-wind (a gale, out in it, on a windy night's
    /// exposed stretches, and its gusts).
    /// </summary>
    void WorldWeather(World world, TrainOnLine train, Places places, Double3 ear, double earMain, bool tunnel, bool underground, double dt)
    {
        var weather = places.Route.Weather;
        float outside = Occlusion(PlayerMotor.Outside);
        if (weather.Wet && !underground)
        {
            if (!tunnel)
            {
                if (_exposed)
                    HoldLevel("world-rain.rain-out", 0, ear, 0, 1);
                else
                    HoldLevel("world-rain.rain-roof", 0, ear + Double3.Up * 2.5, 0, 1);
            }
            if (Sometimes((1 + 2 * weather.Wind) / 60, dt))
            {
                double a = OutsideOdds() * 2 * Math.PI, r = 300 + 250 * OutsideOdds();
                Cue("world-rain.thunder", ear + new Double3(Math.Cos(a) * r, 150, Math.Sin(a) * r), tunnel ? 1 : outside, (float)(0.5 + 0.5 * OutsideOdds()));
            }
        }
        // Slipping: the engine pulling hard on rail that won't hold it.
        var engine = train.Dynamics;
        var boiler = train.Boiler;
        if (train.BoilerTuning is { SteamDrive: true } bt && !world.Derailed && engine.Speed < 8)
        {
            double effort = Math.Clamp((boiler.SteamSpeed(bt, engine.Tuning.MaxSpeed) - engine.Speed) / bt.DriveSpeedBand, 0, 1);
            double grip = (train.Line.Conditions?.Adhesion(engine.Path, engine.Distance) ?? 1) * train.Traction;
            if (effort > 0.6 && grip < 0.7 && !boiler.Ruptured && Sometimes(0.4, dt))
            {
                var e = train.Frames[0];
                Cue("world-rain.slip", e.ToWorld(new Double3(1.3, 0.6, -e.Shape.HalfLength * 0.5)), Occlusion(0), (float)Math.Clamp(1.2 - grip, 0.4, 1));
            }
        }
        if (!_exposed || tunnel || underground)
            return;
        double exposure = 1;
        if (places.Plan is { } plan && !double.IsNaN(earMain))
            foreach (var x in plan.Exposure)
                if (x.Edge == "main" && earMain >= x.S0 && earMain <= x.S1)
                {
                    exposure = x.Wind;
                    break;
                }
        double wind = weather.Wind * exposure;
        if (wind > 0.4)
            HoldLevel("world-wind.gale", 0, ear + Double3.Up, 0, Math.Clamp((wind - 0.4) / 0.5, 0.25, 1));
        if (train.Line.Conditions is not null && PlayerTuning is { } pt && !double.IsNaN(earMain))
        {
            // The line's own gusts (note 201: the same field the sim pushes roof standers with), each heard as it rises,
            // from the side it blows from: a gust from the left comes off the train's left (note 239).
            double gust = PlayerMotor.Gust(earMain, pt.Wind.GustMetres);
            if (wind > 0.15 && Math.Abs(gust) > GustRises && Math.Abs(_gustWas) <= GustRises)
                Cue("world-wind.gust", ear - train.Frames[0].Right * (Math.Sign(gust) * 4) + Double3.Up, 0, (float)Math.Clamp((0.5 + 0.5 * wind) * Math.Abs(gust), 0.4, 1));
            _gustWas = gust;
        }
        else if (wind > 0.15 && Sometimes(0.02 + 0.15 * wind, dt))
            Cue("world-wind.gust", ear + Mixer.Listener.Right * (OutsideOdds() < 0.5 ? -4 : 4) + Double3.Up, 0, (float)Math.Clamp(0.5 + 0.5 * wind, 0.5, 1));
    }

    // How strong (of PlayerMotor.Gust's ±1) a gust is as it's heard rising.
    const double GustRises = 0.6;

    /// <summary>world-brass: the engine cutting through brass growth across the rail at a crawl, or ramming it faster than that (and paying).</summary>
    void WorldBrass(TrainOnLine train, Places places, bool primed)
    {
        if (places.Plan is not { } plan || places.Brass.Count == 0)
            return;
        var engine = train.Dynamics;
        var (edge, s) = TrackRules.Locate(plan, places.Line, engine.Path, engine.Distance);
        bool inField = places.Brass.Any(b => b.Edge == edge && s >= b.S0 && s <= b.S1) && engine.Speed > 0.3;
        var e = train.Frames[0];
        var nose = e.ToWorld(new Double3(0, 0.6, -e.Shape.HalfLength - 0.5));
        double cutting = plan.Rules.BrassCuttingSpeed;
        bool ramming = inField && engine.Speed > cutting;
        if (inField && !ramming)
            HoldLevel("world-brass.cut", 0, nose, Occlusion(PlayerMotor.Outside), Math.Clamp(engine.Speed / Math.Max(0.5, cutting), 0.3, 1));
        if (Flipped("world-brass.ram", 0, ramming, primed) > 0 || ramming && _time >= _rammedAgain)
        {
            Cue("world-brass.ram", nose, Occlusion(PlayerMotor.Outside), (float)Math.Clamp(engine.Speed / (2 * Math.Max(1, cutting)), 0.5, 1));
            _rammedAgain = _time + 1.2 + 0.8 * OutsideOdds();
        }
    }

    /// <summary>
    /// world-debris-hit: the Sleepers across the rail gone the tick the engine reached them (Hazards: they resolve and vanish
    /// together), taken slowly as a jolt, or fast (heavy damage, or the derailment).
    /// </summary>
    void WorldDebris(World world, TrainOnLine train, double front)
    {
        var seen = new HashSet<int>();
        foreach (var e in world.ActiveEnemies)
            if (e.Kind == EnemyKind.Sleepers && !e.Gone)
            {
                _sleepersSeen[e.Id] = e.LineDistance;
                seen.Add(e.Id);
            }
        foreach (var (id, at) in _sleepersSeen.Where(kv => !seen.Contains(kv.Key)).ToList())
        {
            _sleepersSeen.Remove(id);
            if (double.IsNaN(_engineFrontWas) || Math.Abs(_engineFrontWas - at) > 6)
                continue;
            double heavy = world.Enemies?.Sleepers.HeavyDamageAbove ?? 5.6;
            bool fast = world.Derailed || _engineSpeedWas > heavy;
            var e = train.Frames[0];
            Cue(fast ? "world-debris-hit.hit-fast" : "world-debris-hit.jolt", e.ToWorld(new Double3(0, 0.5, -e.Shape.HalfLength)), Occlusion(PlayerMotor.Outside),
                (float)Math.Clamp(0.5 + _engineSpeedWas / 20, 0.5, 1));
        }
    }

    /// <summary>
    /// world-livestock: animals in a car, never quiet (GDD App. B.9). The sim's cargo is "livestock"; which animals is the
    /// car's own, dealt from its id, so every machine hears the same pigs.
    /// </summary>
    void WorldLivestock(TrainOnLine train, Double3 ear)
    {
        string[] species = ["cattle", "pigs", "sheep"];
        // A hard jolt (the slack running in, the brakes biting): the animals startled, each car as the jolt reaches it, and
        // not again for a while (crew-mishaps, the director's call 3 Oct).
        double accel = (RakeOf(train, 0) ?? train.Rakes[0]).Acceleration;
        bool jolt = !double.IsNaN(_livestockAccel) && Math.Abs(accel - _livestockAccel) > StartleJolt;
        _livestockAccel = accel;
        int along = 0;
        foreach (var v in train.Vehicles)
        {
            along++;
            if (v is null || v.Cargo != CargoKind.Livestock || v.Load <= 0)
                continue;
            var frame = train.Frames[v.Id];
            if ((frame.Origin - ear).Length > 160)
                continue;
            ulong h = (ulong)(v.Id + 1) * 0x9E3779B97F4A7C15UL;
            string kind = species[(int)((h >> 33) % 3)];
            var pen = frame.ToWorld(new Double3(0, 1.4, 0));
            HoldLevel($"world-livestock.{kind}", v.Id, pen, Occlusion(v.Id), 0.5 + 0.5 * Math.Clamp(v.Load, 0, 1));
            if (jolt && _time >= _startledUntil.GetValueOrDefault(v.Id))
            {
                CrewAfter(StartleReact + along * 0.11, $"crew-mishaps.startle-{kind}", pen, Occlusion(v.Id));
                _startledUntil[v.Id] = _time + StartleRest;
            }
        }
    }

    double _livestockAccel = double.NaN;
    readonly Dictionary<int, double> _startledUntil = new();
    // A jolt that startles them (a change in the train's acceleration in a tick, m/s²: past the slack's own clunk threshold
    // of 0.12, so only a hard one), how long they take to react, and how long before they'll startle again (s).
    const double StartleJolt = 0.35, StartleReact = 0.15, StartleRest = 8;

    // ---- Places --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// place-threshold: the fortress all round you in its yard before the gates (and at a terminus that isn't dead), its
    /// gates opening as the engine comes up to them and shutting once the last car's through (out at the start of the
    /// night, in at the end).
    /// </summary>
    void PlaceThreshold(Run? run, TrainOnLine train, Places places, double front, bool primed)
    {
        if (run is null || double.IsNaN(front))
            return;
        var line = places.Line;
        double rear = line.MainDistance(train.Dynamics.Path, train.Dynamics.RearDistance);
        double gate = run.YardLength;
        Double3 Yard(double s, double side) => line.Sample(s) is var t ? t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * side + Double3.Up * 4 : default;
        if (run.Phase == RunPhase.Yard)
            HoldLevel("place-threshold.fortress", 0, Yard(Math.Max(0, Math.Min(front, gate) - 60), 30), Occlusion(PlayerMotor.Outside), 1);
        PlaceGates(0, gate, front, rear, line, primed);
        if (places.Plan?.Terminus is { Silent: false } terminus && terminus.GateM > 0)
        {
            if (front >= terminus.GateM - 300 || run.Phase == RunPhase.Arrived)
                HoldLevel("place-threshold.fortress", 1, Yard(terminus.GateM + 80, 30), Occlusion(PlayerMotor.Outside), 1);
            PlaceGates(1, terminus.GateM, front, rear, line, primed);
        }
    }

    void PlaceGates(int which, double gate, double front, double rear, RailLine line, bool primed)
    {
        var at = line.Sample(gate).Position + Double3.Up * 3;
        if (Flipped("place-threshold.open", which, front >= gate - 40 && front < gate + 5, primed) > 0)
            Cue("place-threshold.gate-open", at, Occlusion(PlayerMotor.Outside));
        if (!double.IsNaN(rear) && Flipped("place-threshold.shut", which, rear >= gate + 3, primed) > 0)
            Cue("place-threshold.gate-shut", at, Occlusion(PlayerMotor.Outside));
    }

    /// <summary>
    /// The facilities at work: the coaling chute (place-coaling), the grain elevator's spout (place-grain), the cranes
    /// (place-crane), the wreck yard's winch and its wrecks (place-wreck), the slaughterhouse and the chemical works near their
    /// buildings, the dead towns (place-villages), and the mine underground (place-mine).
    /// </summary>
    void PlaceWorks(World world, Run? run, TrainOnLine train, Places places, Double3 ear, bool underground, double dt, bool primed)
    {
        float outside = Occlusion(PlayerMotor.Outside);
        var line = places.Line;
        if (run is not null)
        {
            // The coaling chute: its lever, the coal pouring (into the tender, or onto the ballast), and the coal settling in.
            bool coaling = run.FacilityFeature is { Facility: FacilityKind.CoalingTower };
            bool open = coaling && run.ChuteOpen;
            if (run.FacilityFeature is { Facility: FacilityKind.CoalingTower } chute)
            {
                var (spout, lever) = run.ChuteAt(chute, line);
                int edge = Flipped("place-coaling", 0, open, primed);
                if (edge > 0)
                {
                    Cue("place-coaling.chute-open", lever, outside);
                    _tenderAtPour = train.Boiler.Tender;
                }
                if (open)
                    HoldLevel("place-coaling.coal-pour", 0, line.Sample(spout).Position + Double3.Up * 4.5, outside, 1);
                if (edge < 0)
                {
                    Cue("place-coaling.chute-shut", lever, outside);
                    if (train.Boiler.Tender > _tenderAtPour + 0.5)
                    {
                        var e = train.Frames[0];
                        var tender = e.ToWorld(new Double3(0, 3, e.Shape.HalfLength * 0.6));
                        CueLater(0.3, "place-coaling.coal-settle", tender, outside);
                        CueLater(1.4, "place-coaling.coal-settle", tender, outside, 0.6f);
                    }
                }
            }
            else
                Flipped("place-coaling", 0, false, primed);

            // The grain elevator's spout (GDD §18's set piece, note 185): swung over the car as the grain starts, the grain coming
            // down while the lever's held (fuller drumming into a car than hissing onto the ballast), cut off as it's let go or
            // the bin runs dry.
            var elevator = run.CurrentSite is { } here && here.Has(ModuleKind.Spout) ? here : null;
            if (elevator is { Pouring: true })
            {
                if (Flipped("place-grain", 0, true, primed) > 0)
                    Cue("place-grain.spout-swing", elevator.Spout, outside);
                bool intoCar = run.CarUnderSpout(train, elevator) is not null;
                HoldLevel("place-grain.grain-pour", 0, elevator.Spout - Double3.Up * 2, outside, intoCar ? 1 : 0.7);
                _spoutAt = elevator.Spout;
            }
            else if (Flipped("place-grain", 0, false, primed) < 0)
                Cue("place-grain.spout-stop", _spoutAt, outside);

            foreach (var site in run.Sites)
                if (site is not null)
                    PlaceSite(site, train, ear, dt, primed);
            if (underground)
            {
                HoldLevel("place-mine.underground", 0, ear, 0, 1);
                if (Sometimes(0.3, dt))
                    Cue("place-mine.drip", ear + new Double3((OutsideOdds() * 2 - 1) * 8, 3, (OutsideOdds() * 2 - 1) * 8), 0, (float)(0.4 + 0.6 * OutsideOdds()));
                if (Sometimes(0.1, dt))
                    Cue("place-mine.timber", ear + new Double3((OutsideOdds() * 2 - 1) * 10, 2.5, (OutsideOdds() * 2 - 1) * 10), 0, (float)(0.5 + 0.5 * OutsideOdds()));
            }
        }

        // Near their buildings: the slaughterhouse inside (and its hooks and chains), the chemical works leaking and dripping.
        foreach (var (kind, at) in places.Works.Where(w => w.Kind is FacilityKind.Slaughterhouse or FacilityKind.ChemicalWorks).OrderBy(w => (w.At - ear).Length).Take(1))
        {
            double far = (at - ear).Length;
            if (kind == FacilityKind.Slaughterhouse && far < 70)
            {
                HoldLevel("place-slaughterhouse.inside", 0, at + Double3.Up * 2, 0.5f, 1);
                if (Sometimes(0.15, dt))
                    Cue("place-slaughterhouse.hook-chain", at + Double3.Up * 3, 0.5f, (float)(0.5 + 0.5 * OutsideOdds()));
            }
            if (kind == FacilityKind.ChemicalWorks && far < 90)
            {
                HoldLevel("place-chemical.leak", 0, at + Double3.Up * 1.5, outside, 1);
                if (Sometimes(0.2, dt))
                    Cue("place-chemical.drip", at + new Double3((OutsideOdds() * 2 - 1) * 4, 0.5, (OutsideOdds() * 2 - 1) * 4), outside, (float)(0.4 + 0.6 * OutsideOdds()));
            }
        }

        // A dead town: the quiet of it (its houses near, or the line's tag), shutters banging and signs creaking in it.
        var houses = places.Houses.Where(h => (h - ear).Length < 120).ToList();
        if (houses.Count > 0 || world.InSettlement)
        {
            HoldLevel("place-villages.dead-town", 0, ear + Double3.Up * 2, outside, houses.Count > 0 ? 1 : 0.6);
            if (houses.Count > 0 && Sometimes(0.08, dt))
                Cue("place-villages.shutter", houses[(int)(OutsideOdds() * houses.Count) % houses.Count] + Double3.Up * 2.5, outside, (float)(0.5 + 0.5 * OutsideOdds()));
            if (houses.Count > 0 && Sometimes(0.06, dt))
                Cue("place-villages.sign", houses[(int)(OutsideOdds() * houses.Count) % houses.Count] + Double3.Up * 3, outside, (float)(0.5 + 0.5 * OutsideOdds()));
        }
    }

    /// <summary>
    /// A facility's site: each crane's motor while it moves, its chain while the hook does, a load creaking as the crane starts
    /// and stops under it, a casting set down on a car or the ground (or dropped); and at a wreck yard, the cargo dragged out
    /// of a wreck while the winch turns, the wreckage shifting as each sled comes in, and the wrecks creaking.
    /// </summary>
    void PlaceSite(Site site, TrainOnLine train, Double3 ear, double dt, bool primed)
    {
        float outside = Occlusion(PlayerMotor.Outside);
        for (int j = 0; j < site.Cranes.Count; j++)
        {
            var crane = site.Cranes[j];
            int key = site.Index * 16 + j;
            if ((crane.Cab - ear).Length > 300)
                continue;
            double hook = Math.Abs(Moved("place-crane.hook", key, crane.Hook));
            double moved = Math.Abs(Moved("place-crane.bridge", key, crane.Bridge)) + Math.Abs(Moved("place-crane.trolley", key, crane.Trolley)) + hook;
            if (moved > 1e-4)
                _craneMoved[key] = _time;
            bool running = _time - _craneMoved.GetValueOrDefault(key, double.NegativeInfinity) < 0.25;
            if (running)
                HoldLevel("place-crane.motor", key, crane.Cab, outside, 1);
            if (hook > 1e-4)
                HoldLevel("place-crane.chain", key, crane.HookAt, outside, 1);
            if (Flipped("place-crane.running", key, running, primed) != 0 && crane.Hooked is not null)
                Cue("place-crane.load-swing", crane.HookAt, outside);
            for (int c = 0; c < crane.Castings.Length; c++)
            {
                var casting = crane.Castings[c];
                var was = _castings.TryGetValue((key, c), out var w) ? w : casting.State;
                _castings[(key, c)] = casting.State;
                if (!primed || was != CastingState.Hooked || casting.State == CastingState.Hooked)
                    continue;
                bool onCar = casting.State == CastingState.Loaded && casting.Car >= 0 && casting.Car < train.Frames.Count;
                var at = onCar ? train.Frames[casting.Car].ToWorld(casting.At) : casting.At;
                Cue("place-crane.load-set", onCar ? "wood" : "ground", at, outside, casting.State == CastingState.Lost ? 1 : 0.7f);
            }
        }
        // The slaughterhouse's herd stirred up (spec D.2 "constant noise"): the cattle in the pen and on the ramp, louder while
        // they're being driven.
        if (site.Has(ModuleKind.Ramp) && site.Stirred && (site.Pen - ear).Length < 150)
            HoldLevel("world-livestock.cattle", HerdOwner + site.Index, (site.Pen + site.RampTop) * 0.5 + Double3.Up, outside, site.Herding ? 1 : 0.6);
        // The chemical works' hose (note 185): its leak hissing at the stand while the pressure's over or the hose is torn.
        if (site.Has(ModuleKind.Hose) && site.Leaking && (site.HoseStand - ear).Length < 150)
            HoldLevel("place-chemical.leak", HoseOwner + site.Index, site.HoseStand + Double3.Up * 1.5, outside, 1);
        // The wreck yard's heaps (note 187): a piece pulled out creaks it; its groan before it shifts (the tell, 3 s) is
        // heap-groan held for the whole warning (tier 1, note 198) with the creaking quickening and loud over it; the shift is
        // the wreckage going.
        foreach (var heap in site.Heaps)
        {
            if ((heap.Centre - ear).Length > 150)
                continue;
            int key = site.Index * 16 + heap.Index;
            var at = heap.Centre + Double3.Up * 1.5;
            if (Moved("place-wreck.stability", key, heap.Stability) < -1e-6 && primed)
                Cue("place-wreck.creak", at, outside, 0.7f);
            if (heap.Groan > 0)
                Hold("heap-groan", HeapOwner + key, at, outside);
            if (heap.Groan > 0 && Sometimes(3, dt))
                Cue("place-wreck.creak", at + new Double3((OutsideOdds() * 2 - 1) * 2, 0, (OutsideOdds() * 2 - 1) * 2), outside, 1);
            if (Moved("place-wreck.shifts", key, heap.Shifts) > 0 && primed)
                Cue("place-wreck.shift", at, outside, 1);
        }
        if (site.Feature.Facility != FacilityKind.WreckYard || (site.Capstan - ear).Length > 150)
            return;
        if (site.Turning)
            HoldLevel("place-wreck.cargo-pull", site.Index, site.Sled, outside, 1);
        if (Moved("place-wreck.sleds", site.Index, site.SledsLeft) < 0 && primed)
            Cue("place-wreck.shift", site.SledTo, outside);
        var wreck = site.SledFrom;
        if (Sometimes(0.12, dt))
            Cue("place-wreck.creak", wreck + new Double3((OutsideOdds() * 2 - 1) * 8, 1.5, (OutsideOdds() * 2 - 1) * 8), outside, (float)(0.4 + 0.6 * OutsideOdds()));
        if (Sometimes(0.04, dt))
            Cue("place-wreck.shift", wreck + new Double3((OutsideOdds() * 2 - 1) * 8, 1, (OutsideOdds() * 2 - 1) * 8), outside, (float)(0.5 + 0.5 * OutsideOdds()));
    }

    /// <summary>
    /// The Holdouts (GDD App. D.7): a lock smashed again and again while it's breached, or a barricade pried and giving
    /// way; and a dead player calling out from one, played where it is from its replicated count of calls (Holdout.Calls):
    /// a call or a shout in that prisoner's own voice for the run (voice-prisoner-sets), or a bout of banging on the walls
    /// (voice-callout). The host keeps it to one a Holdout per cooldown.
    /// </summary>
    void HoldoutCalls(World world, Places? places, bool primed)
    {
        if (world.Holdouts is not { } holdouts)
            return;
        float outside = Occlusion(PlayerMotor.Outside);
        foreach (var h in holdouts.All)
        {
            bool shelter = h.Layout.Kind == HoldoutKind.Shelter;
            bool breaking = h.State == HoldoutState.Breaching && Moved("place-breach.progress", h.Index, h.Progress) > 0;
            if (breaking && shelter)
                HoldLevel("place-breach.pry", h.Index, h.Door, outside, 1);
            if (breaking && !shelter && _time >= _smashAt.GetValueOrDefault(h.Index))
            {
                Cue("place-breach.smash", h.Door, outside, (float)(0.8 + 0.2 * OutsideOdds()));
                _smashAt[h.Index] = _time + 0.45 + 0.25 * OutsideOdds();
            }
            if (Flipped("place-breach.freed", h.Index, h.State == HoldoutState.Freed, primed) > 0)
                Cue(shelter ? "place-breach.pry-give" : "place-breach.smash", h.Door, outside);

            if (Moved("voice-callout.calls", h.Index, h.Calls) <= 0 || !primed)
                continue;
            var inside = h.Inside + Double3.Up * 1.3;
            // The night's deal of the sets (Survey, by the route's seed); with no route surveyed, set n for player n.
            int seat = (Math.Max(0, h.Occupant) % 8 + 8) % 8;
            int set = places?.VoiceSets[seat] ?? seat + 1;
            // What this call is, from the Holdout and its count: the same on every machine.
            ulong roll = ((ulong)h.Index * 0x9E3779B97F4A7C15UL ^ (ulong)h.Calls * 0xBF58476D1CE4E5B9UL) >> 33;
            int kind = (int)(roll % 20);
            // Where a voice set or the banging isn't installed, note 179's synthesised shout and bang stand in.
            if (kind < 14)
            {
                if (Cue($"voice-prisoner-sets.{(kind < 9 ? "call" : "shout")}.set{set}", inside, 0.35f) is null)
                    Mixer.Play("holdout-shout", inside)?.Also(v => v.Occlusion = 0.35f);
            }
            else if (!HasCue("voice-callout.bang"))
                Mixer.Play("holdout-bang", inside)?.Also(v => v.Occlusion = 0.35f);
            else
            {
                int bangs = 2 + (int)(roll / 20 % 3);
                for (int b = 0; b < bangs; b++)
                    CueLater(b * (0.28 + 0.06 * (b % 2)), "voice-callout.bang", inside, 0.35f, b == 0 ? 1 : 0.85f);
            }
        }
    }
}
