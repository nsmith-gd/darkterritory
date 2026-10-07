using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Single-player feel prototype (roadmap M1): one train, one player, live-reloaded tuning.
/// Exists to answer spec G.1 (does the 4:1 speed ratio feel right?) and G.2 (is a 94 s roof
/// traverse fun?). Networking replaces the direct sim calls in M2; the sim itself doesn't change.
/// </summary>
public sealed class PrototypeSession : IPlaySession
{
    readonly HotData<TrainTuning> _trainTuning;
    readonly HotData<PlayerTuning> _playerTuning;
    readonly HotData<BoilerTuning> _boilerTuning;
    readonly HotData<CombatTuning> _combatTuning;
    PlayerState _previousPlayer;

    public PrototypeSession(string contentRoot, string lineName = "test-loop", int cars = 6, double start = 600)
        : this(contentRoot, RailLine.Load(Path.Combine(contentRoot, "lines", lineName + ".json")), null, cars, start)
    {
    }

    /// <summary>Plays a generated route from the fortress yard to the terminus, against the dawn clock.</summary>
    /// <param name="enemies">Run the pressure director and the route's Sleepers (GDD App. B).</param>
    /// <param name="crew">The crew the director plans the night for (App. B: what it fields scales with it); one, played
    /// solo. dt playthrough asks for more to meet the roster a bigger crew does.</param>
    /// <param name="at">Where the engine's front starts along the line (a staged moment, `dt screenshot --roof-warning`);
    /// null, at the fortress's gate.</param>
    public PrototypeSession(string contentRoot, Route route, int cars = 6, bool enemies = true, int crew = 1, double? at = null)
        : this(contentRoot, route.Build(), route, cars, at ?? double.NaN)
    {
        if (enemies)
            World.EnableEnemies(DataFile.Load<EnemyTuning>(Path.Combine(contentRoot, EnemyTuning.File)), route, route.Seed, crew: Math.Max(1, crew), authority: true);
        var routeTuning = RouteTuning.Load(contentRoot);
        World.EnableSwitches(routeTuning.Junctions);
        World.EnableRun(DataFile.Load<RunTuning>(Path.Combine(contentRoot, RunTuning.File)), route,
            route.GateOr(routeTuning.YardLength), authority: true,
            DataFile.Load<FacilityTuning>(Path.Combine(contentRoot, FacilityTuning.File)),
            DataFile.Load<Sim.Stops.LootTuning>(Path.Combine(contentRoot, Sim.Stops.LootTuning.File)));
        World.EnableLineside(DataFile.Load<SightTuning>(Path.Combine(contentRoot, SightTuning.File)), route);
    }

    PrototypeSession(string contentRoot, RailLine line, Route? route, int cars, double start)
    {
        Route = route;
        _trainTuning = new HotData<TrainTuning>(Path.Combine(contentRoot, TrainTuning.File));
        _playerTuning = new HotData<PlayerTuning>(Path.Combine(contentRoot, PlayerTuning.File));
        _boilerTuning = new HotData<BoilerTuning>(Path.Combine(contentRoot, BoilerTuning.File));
        _combatTuning = new HotData<CombatTuning>(Path.Combine(contentRoot, CombatTuning.File));
        // A night leaves the fortress part loaded; the facilities fill the rest (GDD §17-18).
        var runTuning = DataFile.Load<RunTuning>(Path.Combine(contentRoot, RunTuning.File));
        var consist = Consist.Uniform(_trainTuning.Value, cars, route is null ? 1 : runTuning.DepartureLoad);
        // On a route, start at the fortress's gate, ready to depart (run.json departShortOfGateM), the train in the yard.
        if (route is not null && double.IsNaN(start))
            start = runTuning.DepartFrom(route.GateOr(DataFile.Load<RouteTuning>(Path.Combine(contentRoot, RouteTuning.File)).YardLength), consist.LengthMetres);
        Train = new TrainOnLine(new TrainDynamics(consist), line, start, _boilerTuning.Value);
        World = new World(Train, _combatTuning.Value);
        World.EnableBodies();
        World.Stock();
        // With steam driving (T97), the train stands at the gate on its brake.
        Controls = new TrainControls { Reverser = 1, Brake = Train.BoilerTuning?.SteamDrive == true ? 1 : 0 };
        Respawn(0);
    }

    public TrainOnLine Train { get; }
    public World World { get; }
    public Route? Route { get; }
    public double ElapsedSeconds => Tick * SimConstants.TickSeconds;
    public PlayerState Player;
    public TrainControls Controls;
    public long Tick { get; private set; }
    public string? LastReloadError { get; private set; }

    PlayerState IPlaySession.Player => Player;
    TrainControls IPlaySession.Controls => Controls;
    public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];

    public TrainTuning TrainTuning => _trainTuning.Value;
    public PlayerTuning PlayerTuning => _playerTuning.Value;

    double? _greaseCued;

    public void Step(in PlayerIntent intent)
    {
        ReloadTuning();
        _previousPlayer = Player;
        World.BeginTick();
        // The keyboard drives this session's cab directly (Notch, FlipReverser); a headset's levers come as intent (T29).
        Sim.Net.CabControls.Apply(ref Controls, intent, Player, Train);
        World.CrewAct(ref Player, intent, 1);
        World.Step(Controls);
        World.ApplyDamage(id => id == 1 ? Player : null, (_, s) => Player = s, [1]);
        foreach (var e in World.EnemyEvents)
            if (Cue(e) is { } cue)
                _cues.Add((ElapsedSeconds, cue));
        foreach (var sign in World.Lineside?.ReadThisTick ?? [])
            _cues.Add((ElapsedSeconds, Board(sign)));
        foreach (var drop in World.Lineside?.CaughtThisTick ?? [])
            _cues.Add((ElapsedSeconds, Caught(drop)));
        // Grease's telegraph (App. A.2): "lamp reflection off the slicked rail; a sharp chemical smell in the cab".
        if (World.Lineside?.GreaseAhead(Train, World.LampShining) is { } grease && grease != _greaseCued)
        {
            _greaseCued = grease;
            _cues.Add((ElapsedSeconds, "a sharp chemical smell in the cab, and the rail ahead shines: grease. sand it from the running boards"));
        }
        _cues.RemoveAll(c => ElapsedSeconds - c.At > CueSeconds);
        PlayerMotor.Step(ref Player, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds, applyLook: false);
        World.StepBodies([(1, Player)]);
        World.StepRun([Player]);
        Tick++;
    }

    void ReloadTuning()
    {
        if (_trainTuning.Refresh(e => LastReloadError = e.Message))
        {
            Train.Dynamics.Consist.Tuning = _trainTuning.Value;
            LastReloadError = null;
        }
        if (_playerTuning.Refresh(e => LastReloadError = e.Message))
            LastReloadError = null;
        World.Hand = _playerTuning.Value.Hand;
        World.Bodies.FullHealth = _playerTuning.Value.Health; // a healing find is used only short of it (note 272)
        if (_combatTuning.Refresh(e => LastReloadError = e.Message))
        {
            World.Combat = _combatTuning.Value;
            LastReloadError = null;
        }
        if (_boilerTuning.Refresh(e => LastReloadError = e.Message))
        {
            Train.BoilerTuning = _boilerTuning.Value;
            LastReloadError = null;
        }
    }

    /// <summary>Regulator in quarter notches, like a real throttle quadrant.</summary>
    public void Notch(int delta)
    {
        Controls.Throttle = Math.Clamp(Math.Round(Controls.Throttle * 4 + delta) / 4, 0, 1);
        // T97: with steam driving there's no regulator; a notch up lets the brake off.
        if (delta > 0)
            Controls.Brake = 0;
    }

    /// <summary>The brake key this frame: held, it's on; let go, it comes off while moving, and stays on at a stand (T97).</summary>
    public void BrakeHeld(bool held)
    {
        if (held)
            Controls.Brake = 1;
        else if (Sim.Net.CabControls.Clears(Controls, Train, released: false))
            Controls.Brake = 0;
    }

    /// <summary>The reverser only moves with the train stopped.</summary>
    public void FlipReverser()
    {
        if (Train.Dynamics.Speed < 0.05)
            Controls.Reverser = -Controls.Reverser;
    }

    /// <summary>Car 0 is the cab; any other number is that car's roof.</summary>
    public void Respawn(int car)
    {
        car = Math.Clamp(car, 0, Train.Frames.Count - 1);
        Player = car == 0 ? PlayerMotor.SpawnInCab(Train, PlayerTuning) : PlayerMotor.SpawnOnRoof(Train, car, 0, PlayerTuning);
        _previousPlayer = Player;
    }

    readonly List<CarFrame> _renderFrames = new();

    /// <summary>Vehicle frames between the previous and current tick, for smooth rendering at any frame rate.</summary>
    public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha)
    {
        Train.FramesAt(alpha, _renderFrames);
        return _renderFrames;
    }

    /// <summary>First-person eye, interpolated in the player's own frame so riding a car at speed is smooth.</summary>
    public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) =>
        Eyes.Operator(Player, World) ?? Eyes.From(Player, _previousPlayer, frames, alpha, pendingYaw, pendingPitch);

    public string Status()
    {
        var d = Train.Dynamics;
        string where = Where(Player, Train);
        var b = Train.Boiler;
        string boiler = b.Ruptured ? "BOILER RUPTURED" :
            $"P {b.Pressure,3:0}{(b.SafetyValveLifting ? " VALVE" : "")} fire {b.Firebox:0.0} tender {b.Tender:0}" +
            (Player.ActionProgress > 0 ? $" shovel {Player.ActionProgress:0.0}s" : "");
        string state = Player.Alive ? $"{Player.Surface} {where} hp {Player.Health}{Condition(Player, PlayerTuning)}" : $"DEAD ({Player.Death}) — Backspace to respawn";
        return $"{d.Speed,5:0.0} m/s {SpeedBands.Classify(TrainTuning, d.Speed),-7} | thr {Controls.Throttle:0.00} brk {Controls.Brake:0} rev {(Controls.Reverser > 0 ? "F" : "R")} " +
               $"| {boiler} |{Gunnery()}{(Train.Rakes.Count > 1 ? $" {Train.Rakes.Count} rakes |" : "")} grade {Train.AverageGrade(),4:0.0}% | {d.Distance / 1000:0.00}/{Train.Line.Length / 1000:0.0} km | {state}" +
               RouteStatus() + Threats() +
               (LastReloadError is null ? "" : $" | TUNING ERROR: {LastReloadError}");
    }

    const double CueSeconds = 4;
    readonly List<(double At, string Text)> _cues = new();

    /// <summary>
    /// Stand-ins for the audio telegraphs until the mixer exists (spec §2: the tell is a sound). Each is
    /// what you'd hear or see at that transition, worded so it's clear what the answer is.
    /// </summary>
    /// <summary>A board the lamp has found, as the driver would call it back down the train.</summary>
    public static string Board(Sign sign) => sign.Kind switch
    {
        SignKind.SpeedLimit => $"board: {sign.LimitKmh} km/h ahead",
        SignKind.Drop => $"board: a mail crane ahead on the {(sign.Drop!.Side > 0 ? "right" : "left")}: open that side door and hook it (left mouse)",
        SignKind.Terminus => "board: the terminus ahead",
        _ => "board: low clearance ahead, off the roofs",
    };

    /// <summary>A bag off a crane, as whoever hooked it would call it.</summary>
    public static string Caught(Drop drop) => drop.Kind switch
    {
        DropKind.Mail => $"hooked the mail: {drop.Amount:0} scrip at the terminus",
        DropKind.Coal => "hooked a sack of coal for the tender",
        DropKind.Ammo => "hooked a case of rounds for the guns",
        _ => "hooked a bag of spares: the worst car's patched up",
    };

    static string? Cue(in EnemyEvent e) => (e.Kind, e.To) switch
    {
        // GDD v1.1 §21-22: each line is what the crew would say they saw or heard, and the rule that answers it.
        (EnemyKind.Sleepers, SpinePhase.Telegraph) => "the lamp catches debris on the line ahead: brake",
        (EnemyKind.Sleepers, SpinePhase.Punish) => "the engine rides up over something",
        (EnemyKind.Drift, SpinePhase.Telegraph) => "the reeds rustle toward someone: stand still",
        (EnemyKind.Drift, SpinePhase.Punish) => "it's on them. stop moving",
        (EnemyKind.Drift, SpinePhase.BreakOff) => "the reeds go quiet",
        (EnemyKind.TrackDoll, SpinePhase.Telegraph) => "a white face on the rails in the lamp: stop before you hit it",
        (EnemyKind.TrackDoll, SpinePhase.BreakOff) => "the doll's gone",
        (EnemyKind.TrackDoll, SpinePhase.Punish) => "giggling in the cars: keep someone in the cab",
        (EnemyKind.CinderHound, SpinePhase.Telegraph) => "howling behind, closing: crew the rear cannon",
        (EnemyKind.CinderHound, SpinePhase.Commit) => "hounds aboard the rear car: club them together",
        (EnemyKind.CinderHound, SpinePhase.Grab) => "a hound has someone pinned: club it off",
        (EnemyKind.CinderHound, SpinePhase.BreakOff) => "the howling falls away",
        (EnemyKind.CarHugger, SpinePhase.Telegraph) => "something low beside the line, keeping pace at the rear",
        (EnemyKind.CarHugger, SpinePhase.Commit) => "grinding at the rear: it's latched on. cut the caboose or beat it off",
        (EnemyKind.CarHugger, SpinePhase.Grab) => "it's swallowing someone at the rear: pull them out",
        (EnemyKind.CarHugger, SpinePhase.Punish) => "the rear car's eaten through and gone",
        (EnemyKind.CarHugger, SpinePhase.BreakOff) => "the grinding at the rear stops",
        (EnemyKind.Dragger, SpinePhase.Telegraph) => "a limb over the roof's edge: get away from the edge",
        (EnemyKind.Dragger, SpinePhase.Grab) => "someone's hanging off the side: haul them up",
        (EnemyKind.Dragger, SpinePhase.BreakOff) => "it lets go and sinks back under the edge",
        (EnemyKind.Whistler, SpinePhase.Telegraph) => "the whistle blew and nobody's on the cord: check the gaps, in pairs",
        (EnemyKind.Whistler, SpinePhase.Grab) => "something's run off with one of the crew: after it",
        (EnemyKind.Whistler, SpinePhase.BreakOff) => "the thing in the gap runs",
        (EnemyKind.Climber, SpinePhase.Telegraph) => "scrabbling at a coupling gap: outnumber them there",
        (EnemyKind.Climber, SpinePhase.Commit) => "something's up on the roofs, heading for the engine",
        (EnemyKind.Climber, SpinePhase.Grab) => "a Climber's got someone in a car: club it off",
        (EnemyKind.Climber, SpinePhase.BreakOff) => "it drops back off the train",
        (EnemyKind.Stoker, SpinePhase.Telegraph) => "something on the coal, coming for the fire: club it before it's in",
        (EnemyKind.Stoker, SpinePhase.Commit) => "it's in the firebox: don't open the door. Vent and starve it (no coal), or hose it",
        (EnemyKind.Stoker, SpinePhase.Punish) => "too fast for the line",
        (EnemyKind.Stoker, SpinePhase.BreakOff) => "starved out: it's going back the way it came",
        (EnemyKind.TippyToesie, SpinePhase.Grab) => "someone's gone quiet: something's got its hand over their mouth",
        (EnemyKind.TippyToesie, SpinePhase.BreakOff) => "it tiptoes off",
        (EnemyKind.FireFlies, SpinePhase.Telegraph) => "glow and buzzing round a lamp: lamps off",
        (EnemyKind.FireFlies, SpinePhase.Punish) => "the car's caught",
        (EnemyKind.FireFlies, SpinePhase.BreakOff) => "the flies scatter",
        (EnemyKind.Ribbit, SpinePhase.Telegraph) => "throats swelling in a line: stick together, or run",
        (EnemyKind.Ribbit, SpinePhase.Grab) => "a tongue's got someone: get to them",
        (EnemyKind.Ribbit, SpinePhase.BreakOff) => "the toads hop off",
        (EnemyKind.Gaunt, SpinePhase.Alert) => "something woke and it's following someone: keep talking to it",
        (EnemyKind.Gaunt, SpinePhase.Telegraph) => "it's leaning in, head tilted: talk",
        (EnemyKind.Gaunt, SpinePhase.BreakOff) => "the thin thing's leaving with something: run it down before it's off the train",
        // A Follower's lump is on its host's back: they can't see it, so no cue until it's off them (GDD v1.1 A.6).
        (EnemyKind.Follower, SpinePhase.Punish) => "something's nesting in the loot: find it, bludgeon it",
        (EnemyKind.Follower, SpinePhase.BreakOff) => "the parasite's dead",
        (EnemyKind.SootChildren, SpinePhase.Telegraph) => "a child calling for help out there: check its eyes from five metres",
        (EnemyKind.SootChildren, SpinePhase.Grab) => "it's got someone and it's drinking: kill it",
        (EnemyKind.SootChildren, SpinePhase.BreakOff) => "the calling stops",
        // The Passenger's telegraph is silence (App. A.8): it says nothing, and neither does this, until it moves.
        (EnemyKind.Passenger, SpinePhase.Grab) => "someone's being dragged toward the caboose: kill it",
        (EnemyKind.Passenger, SpinePhase.Punish) => "the caboose is rolling away with someone",
        (EnemyKind.Passenger, SpinePhase.BreakOff) => "the one who never spoke is dead",
        (EnemyKind.Switchman, SpinePhase.Telegraph) => "a figure at the points ahead; the switch lamp reads wrong: shoot it or stop",
        (EnemyKind.Switchman, SpinePhase.Punish) => "the train takes the wrong line",
        (EnemyKind.Switchman, SpinePhase.BreakOff) => "the figure at the points is down",
        (EnemyKind.Grumbler, SpinePhase.Telegraph) => "gnawing in the crates: check before every lift",
        (EnemyKind.Grumbler, SpinePhase.Commit) => "it's feral: gang up on it",
        (EnemyKind.Grumbler, SpinePhase.Punish) => "it came aboard with a crate: it's eating the cargo",
        (EnemyKind.Grumbler, SpinePhase.BreakOff) => "the Grumbler's dead",
        (EnemyKind.Choir, SpinePhase.Grab) => "the ghosts have someone: hush and shut the doors",
        (EnemyKind.CarFire, SpinePhase.Telegraph) => "smoke and a crackle from a car: get the extinguisher (Fire)",
        (EnemyKind.CarFire, SpinePhase.Punish) => "a car's alight: it'll take the next one",
        (EnemyKind.CarFire, SpinePhase.BreakOff) => "the fire's out",
        _ => null,
    };

    /// <summary>Recent cues plus anything still ongoing, e.g. the Choir gathering.</summary>
    public string Threats()
    {
        var parts = _cues.Select(c => c.Text).Distinct().ToList();
        if (World.Choir.Present)
            parts.Add("THE CHOIR IS HERE: hush");
        else if (World.Choir.Build > 0.3)
            parts.Add($"something's gathering to the noise ({World.Choir.Build:P0})");
        foreach (var e in World.ActiveEnemies)
        {
            if (e is Switchman { Phase: SpinePhase.Telegraph })
                parts.Add("a switch ahead set wrong");
            else if (e is CarHugger { Phase: SpinePhase.Commit or SpinePhase.Grab } && !parts.Contains("the rear car's being eaten"))
                parts.Add("the rear car's being eaten");
            else if (e is TrackDoll { Tampering: true } && !parts.Contains("THE DOLL'S IN THE CAB"))
                parts.Add("THE DOLL'S IN THE CAB");
            else if (e is TrackDoll { Restless: true } doll && !parts.Contains("the doll's restless: get her off the train"))
                parts.Add(doll.Stage == 1 ? "the doll's restless: she's eyeing the cab" : "the doll's restless: get her off the train");
        }
        return parts.Count == 0 ? "" : " | " + string.Join(" · ", parts);
    }

    /// <summary>Where a player is, the way the crew would say it: the cab, on car 4, inside car 4 with the doors shut.</summary>
    public static string Where(in PlayerState p, TrainOnLine train)
    {
        if (p.Parent == PlayerState.World)
            return "ground";
        if (PlayerMotor.InCab(p, train))
            return "cab";
        if (PlayerMotor.Indoors(p, train))
            return PlayerMotor.Space(p, train) == PlayerMotor.Outside ? $"inside car {p.Parent}, {(train.Vehicles[p.Parent].Breached ? "breached" : "door open")}" : $"inside car {p.Parent}, shut in";
        return p.Parent == 0 ? "engine" : $"car {p.Parent}";
    }

    string Gunnery()
    {
        var c = _combatTuning.Value;
        string gun = Guns.MannedGun(Player, Train, c.Guns) is { } g ? $" GUN {Train.Vehicles[g].Gun.Ammo} rds{(Train.Vehicles[g].Gun.ReloadNeeded > 0 ? " RELOAD (hold Use)" : "")} |" : "";
        return $"{gun} choir {World.Choir.Phase(c.Choir).ToString().ToLowerInvariant()} {World.Choir.Loudness:0.0} |";
    }

    string RouteStatus() => RouteStatus(Route, World, Train);

    /// <summary>The night so far: the dawn clock, where you are on the route, what's next, and how it ended.</summary>
    /// <summary>Cold and being held, for the status line (spec B.2; GDD v1.1 App. C.1).</summary>
    public static string Condition(in PlayerState p, PlayerTuning t)
    {
        string cold = PlayerMotor.Chilled(p, t) ? $" | COLD: {Math.Max(0, t.Cold.DeathSeconds - p.Cold):0}s — get inside" : "";
        // GDD v1.1 App. C.1: held, a friend has to hit it or pull you free; alone, you struggle (hold Use).
        string held = p.Has(PlayerFlags.Held) ? " | HELD: shout for help (alone: hold Use to struggle)" : "";
        return cold + held;
    }

    /// <summary>Holdouts lit along the line (GDD App. D): somebody's waiting to be picked up.</summary>
    public static string HoldoutStatus(World world) =>
        world.Holdouts?.All.Count(h => h.Lit) is > 0 and var lit ? $" | {lit} HOLDOUT{(lit == 1 ? "" : "S")} LIT — someone's waiting" : "";

    /// <summary>What there is to load at a facility (spec D).</summary>
    static string SiteStatus(Site? site)
    {
        if (site is null)
            return " — nothing here to load";
        var parts = new List<string>();
        // The yard's power (level-design D.2): its cranes wait on it.
        if (site.Power != Sim.Stops.PowerState.Live && site.Cranes.Count > 0)
            parts.Add(site.Power == Sim.Stops.PowerState.Dead ? "POWER DEAD: the cranes won't run until someone restarts the generator at the powerhouse"
                : "power low: the cranes run at half speed (restart the generator at the powerhouse)");
        if (site.Has(ModuleKind.Crates))
            parts.Add(site.HeavyStack.Length > 0 ? "crates on the platform: carry them into the cars (the big ones take two)" : "crates on the platform: carry them into the cars");
        if (site.Cranes.Count > 0)
        {
            // Every gantry here (level-design P18): the facility's own and the yard's.
            int left = site.Cranes.Sum(c => c.Left);
            string gantries = site.Cranes.Count > 1 ? $"{site.Cranes.Count} cranes" : "crane";
            parts.Add(left == 0 ? "the castings are loaded" : site.Cranes.Any(c => c.Hooked is not null) ? $"{gantries}: a casting on the hook"
                : $"{gantries}: {left} castings to rig and lift (one in the cab, one on the ground)");
        }
        // GDD §18's set pieces (note 185).
        if (site.Has(ModuleKind.Spout))
            parts.Add(site.Bin <= 0 ? "the elevator's bin is empty" : site.Pouring ? $"spout POURING ({site.Bin:0.0} loads left)"
                : $"one spout: walk each car under it, someone on its lever ({site.Bin:0.0} loads)");
        if (site.Has(ModuleKind.Ramp))
            parts.Add(site.Head == 0 ? "the herd's aboard" : site.Herding ? $"herd going up the ramp ({site.Head} left), LOUD"
                : $"{site.Head} head in the pen: two to drive them up the ramp");
        if (site.Has(ModuleKind.Hose))
            parts.Add(site.Leaking ? "HOSE LEAKING: get clear, or get to the stand" : site.HoseCar >= 0 ? $"hose on, pressure {site.Pressure * 100:0}%: someone stay by the stand"
                : "hose stand: put it on a car, mind it, take it off (and do not fire the guns in here)");
        // GDD §18's switchyard and wreck yard (note 187).
        if (site.Has(ModuleKind.Rakes))
            parts.Add("cars standing on the sidings: throw each switch, couple up and bring them out (they come away ahead of the engine)");
        if (site.Heaps.Count > 0)
        {
            int dark = site.Heaps.Count(h => !h.Found && h.Salvage > 0);
            parts.Add(site.Heaps.Any(h => h.Groan > 0) ? "THE WRECK'S GOING: get clear of it"
                : dark > 0 ? $"wreck: {dark} of {site.Heaps.Count} heaps not yet seen (no lamps here: take one to them)" : "wreck: carry the salvage to the cars, gently");
        }
        if (site.Feature.Facility == FacilityKind.MilitaryDepot && site.Has(ModuleKind.Crates))
            parts.Add("powder kegs: set them down, never throw or drop them");
        if (site.Has(ModuleKind.Winch))
            parts.Add(site.SledsLeft == 0 ? "the winch is done" : site.Turning ? $"winch HAULING {site.Progress * 100:0}%" : site.OutOfRhythm ? "winch STALLED: out of rhythm" : $"winch: two on the capstan ({site.SledsLeft} sleds)");
        return " — " + string.Join(", ", parts);
    }

    /// <summary>
    /// On a generated line, the next place by its name, as the route card has it (linegen plan §13.3), and how far: what
    /// the HUD's strip across the top says (note 264).
    /// </summary>
    public static string NextPlace(Route route, double s) =>
        route.Plan?.Landmarks.Where(p => p.Edge == "main" && p.S0 > s).MinBy(p => p.S0) is { } place
            ? $"{place.Name} in {(place.S0 - s) / 1000:0.0} km"
            : route.Plan is { } plan ? $"{plan.Terminus.Name} in {Math.Max(0, plan.Terminus.GateM - s) / 1000:0.0} km"
            : route.NextLandmark(s) is { } l
            ? $"{(l.Kind == FeatureKind.Facility ? $"{l.Facility}" : $"{l.Kind}").ToLowerInvariant()} in {(l.Start - s) / 1000:0.0} km"
            : "terminus ahead";

    public static string RouteStatus(Route? route, World world, TrainOnLine train)
    {
        if (route is null)
            return HoldoutStatus(world);
        var run = world.Run;
        if (run?.Report is { } r)
            return r.End == RunEnd.Delivered
                ? $" | DELIVERED {r.CarsDelivered} cars ({r.CargoDelivered:0.0} loads), {r.CarsLost} lost | gross {r.Gross:0} − coal {r.CoalCost:0} − ammo {r.AmmoCost:0} − repairs {r.RepairCost:0} = {r.Net:0} scrip | crew home {r.CrewHome}"
                : $" | RUN LOST: {r.End switch { RunEnd.Derailed => "derailed", RunEnd.CrewLost => "the whole crew is dead", _ => "still out when the line went live" }} | {r.DistanceKm:0.0} km in {r.Seconds / 60:0} min";
        double dawn = run?.DawnIn ?? route.DawnSeconds;
        string clock = run?.Phase == RunPhase.Yard ? "in the yard: gates ahead"
            : dawn > 0 ? $"dawn {(int)dawn / 60:00}:{(int)dawn % 60:00}" : "DAWN — the line is live, get in";
        string stop = run?.FacilityFeature is { } f
            ? $" | STOPPED AT {f.Facility.ToString()!.ToUpperInvariant()}" + (f.Facility == FacilityKind.CoalingTower
                ? run.ChuteOpen ? $" — chute POURING ({run.ChuteLeft(run.Facility):0} left)" : run.ChuteLeft(run.Facility) > 0 ? " — lever on the ground, hold E" : " — chute empty"
                : SiteStatus(run.CurrentSite))
            : "";
        double s = train.Dynamics.Distance;
        // Pulled up by a facility that's down a spur (GDD §17): say how much of the train it takes.
        if (stop.Length == 0 && run is not null && train.OnMain && train.Dynamics.Speed < 0.5)
            for (int i = 0; i < run.FacilityCount; i++)
                if (run.SpurOf(i) is var b and >= 0 && b < train.Line.Branches.Count && route.Of(FeatureKind.Facility).ElementAt(i) is var zone && zone.Contains(s))
                {
                    int fit = SpurDrill.Capacity(train.Dynamics.Tuning.Geometry, train.Line.Branches[b], world.Switches?.Tuning.PointsLength ?? 12);
                    stop = $" | {zone.Facility.ToString()!.ToUpperInvariant()} IS DOWN THE SPUR: ENGINE + {fit} CARS FIT" +
                        (train.Dynamics.Consist.CarCount > fit ? ", CUT THE REST" : "");
                }
        string next = NextPlace(route, s);
        string tunnel = route.InTunnel(s) ? " | IN TUNNEL" : "";
        return $" | {route.Name} | {clock} | {next}{tunnel}{stop}{HoldoutStatus(world)}";
    }
}
