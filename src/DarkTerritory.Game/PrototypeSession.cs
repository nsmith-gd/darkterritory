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
    public PrototypeSession(string contentRoot, Route route, int cars = 6, bool enemies = true)
        : this(contentRoot, route.Build(), route, cars, 0)
    {
        if (enemies)
            World.EnableEnemies(DataFile.Load<EnemyTuning>(Path.Combine(contentRoot, EnemyTuning.File)), route, route.Seed, crew: 1, authority: true);
        var routeTuning = DataFile.Load<RouteTuning>(Path.Combine(contentRoot, RouteTuning.File));
        World.EnableSwitches(routeTuning.Junctions);
        World.EnableRun(DataFile.Load<RunTuning>(Path.Combine(contentRoot, RunTuning.File)), route,
            routeTuning.YardLength, authority: true,
            DataFile.Load<FacilityTuning>(Path.Combine(contentRoot, FacilityTuning.File)));
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
        var consist = Consist.Uniform(_trainTuning.Value, cars, route is null ? 1 : DataFile.Load<RunTuning>(Path.Combine(contentRoot, RunTuning.File)).DepartureLoad);
        // On a route, start in the fortress yard with the whole train on the level.
        if (route is not null)
            start = consist.LengthMetres + 150;
        Train = new TrainOnLine(new TrainDynamics(consist), line, start, _boilerTuning.Value);
        World = new World(Train, _combatTuning.Value);
        World.EnableBodies();
        World.Stock();
        Controls = new TrainControls { Reverser = 1 };
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
    public void Notch(int delta) => Controls.Throttle = Math.Clamp(Math.Round(Controls.Throttle * 4 + delta) / 4, 0, 1);

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
        _ => "board: low clearance ahead, off the roofs",
    };

    static string? Cue(in EnemyEvent e) => (e.Kind, e.To) switch
    {
        (EnemyKind.Sleepers, SpinePhase.Telegraph) => "the lamp catches ties that move, ahead",
        (EnemyKind.Sleepers, SpinePhase.Punish) => "the engine rides up over something",
        (EnemyKind.CinderHound, SpinePhase.Telegraph) => "howling behind, closing",
        (EnemyKind.CinderHound, SpinePhase.Punish) => "something lands on the rear car",
        (EnemyKind.CinderHound, SpinePhase.BreakOff) => "the howling falls away",
        (EnemyKind.Clinger, SpinePhase.Telegraph) => "scraping on a hull",
        (EnemyKind.Clinger, SpinePhase.Punish) => "a hull gives: cargo spilling",
        (EnemyKind.Clinger, SpinePhase.BreakOff) => "it comes away and drops",
        (EnemyKind.Hollow, SpinePhase.Telegraph) => "the fire gutters; soot falls in the cab",
        (EnemyKind.Hollow, SpinePhase.BreakOff) => "the heat drives it back up the stack",
        (EnemyKind.Switchman, SpinePhase.Telegraph) => "a figure at the points ahead; the switch lamp reads wrong",
        (EnemyKind.Switchman, SpinePhase.Punish) => "the train takes a dead line",
        (EnemyKind.Switchman, SpinePhase.BreakOff) => "the figure at the points slips away",
        // The voice is the telegraph; the cue only says the call's there (the listener has to notice it doesn't fall off).
        (EnemyKind.SootChildren, SpinePhase.Telegraph) => "someone outside is calling for help",
        (EnemyKind.SootChildren, SpinePhase.Punish) => "somebody answered the voice outside",
        (EnemyKind.SootChildren, SpinePhase.BreakOff) => "the voice outside gives up",
        (EnemyKind.Dragger, SpinePhase.Telegraph) => "a scrape at the roof's edge: something reaching over the lip",
        (EnemyKind.Dragger, SpinePhase.Punish) => "grabbed at the edge! get them free",
        (EnemyKind.Dragger, SpinePhase.BreakOff) => "it lets go and sinks back under the edge",
        (EnemyKind.Rattle, SpinePhase.Telegraph) => "a dry rattle in the coupling: don't cross there",
        (EnemyKind.Rattle, SpinePhase.Punish) => "pulled under between the cars",
        (EnemyKind.Rattle, SpinePhase.Dormant) => "the rattle in the coupling stops",
        (EnemyKind.Deadman, SpinePhase.Telegraph) => "the cab lamp's dimming and the controls are clicking on their own: get in the cab",
        (EnemyKind.Deadman, SpinePhase.Punish) => "something's taken the cab: the regulator's locked open",
        (EnemyKind.Deadman, SpinePhase.BreakOff) => "the cab is yours again",
        (EnemyKind.Stoker, SpinePhase.Telegraph) => "the pressure's climbing on its own and the fire's the wrong colour: vent it, or drive it out",
        (EnemyKind.Stoker, SpinePhase.Punish) => "the boiler's at its limit",
        (EnemyKind.Stoker, SpinePhase.BreakOff) => "driven out of the firebox",
        (EnemyKind.Ferryman, SpinePhase.Telegraph) => "a lantern on the line ahead, waving you down: don't slow down",
        (EnemyKind.Ferryman, SpinePhase.Commit) => "the lantern's coming down the line at you",
        (EnemyKind.Ferryman, SpinePhase.Punish) => "something's in the cab",
        (EnemyKind.Ferryman, SpinePhase.BreakOff) => "the lantern steps aside",
        (EnemyKind.CarFire, SpinePhase.Telegraph) => "smoke and a crackle from a car: get in there and beat it out (Use)",
        (EnemyKind.CarFire, SpinePhase.Punish) => "a car's alight: it'll take the next one",
        (EnemyKind.CarFire, SpinePhase.BreakOff) => "the fire's out",
        (EnemyKind.LooseLoad, SpinePhase.Telegraph) => "straps groaning in a car: a load's come loose, lash it (Use), and go easy on the brake",
        (EnemyKind.LooseLoad, SpinePhase.Punish) => "a load's come down across the aisle",
        (EnemyKind.LooseLoad, SpinePhase.BreakOff) => "the load's lashed",
        (EnemyKind.Gnawers, SpinePhase.Telegraph) => "chittering in a car's load: something's nesting in it",
        (EnemyKind.Gnawers, SpinePhase.Punish) => "they're out of the crates: stamp them out (Use)",
        (EnemyKind.Gnawers, SpinePhase.BreakOff) => "the last of them stamped out",
        (EnemyKind.Lamplighter, SpinePhase.Telegraph) => "eyes out in the dark, catching the lamp: lamps down (L)",
        (EnemyKind.Lamplighter, SpinePhase.Punish) => "the lamp's smashed",
        (EnemyKind.Lamplighter, SpinePhase.BreakOff) => "the eyes go back out into the dark",
        _ => null,
    };

    /// <summary>Recent cues plus anything still ongoing, e.g. a Clinger drilling.</summary>
    public string Threats()
    {
        var parts = _cues.Select(c => c.Text).Distinct().ToList();
        foreach (var e in World.ActiveEnemies)
        {
            if (e is Clinger { Phase: SpinePhase.Telegraph } c)
                parts.Add($"drilling on car {c.Attached} ({c.Extra:P0})");
            else if (e is Hollow { Phase: SpinePhase.Punish })
                parts.Add("SOMETHING IN THE CAB");
            else if (e is Switchman { Phase: SpinePhase.Telegraph })
                parts.Add("a switch ahead set for a dead line");
            else if (e is CinderHound { Phase: SpinePhase.Punish } h && !parts.Contains("hounds aboard"))
                parts.Add("hounds aboard");
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
            return PlayerMotor.Space(p, train) == PlayerMotor.Outside ? $"inside car {p.Parent}, door open" : $"inside car {p.Parent}, shut in";
        return p.Parent == 0 ? "engine" : $"car {p.Parent}";
    }

    string Gunnery()
    {
        var c = _combatTuning.Value;
        string gun = Guns.MannedGun(Player, Train, c.Guns) is { } g ? $" GUN {Train.Vehicles[g].Gun.Ammo} rds |" : "";
        return $"{gun} choir {World.Choir.Phase(c.Choir).ToString().ToLowerInvariant()} {World.Choir.Aggro:0} |";
    }

    string RouteStatus() => RouteStatus(Route, World, Train);

    /// <summary>The night so far: the dawn clock, where you are on the route, what's next, and how it ended.</summary>
    /// <summary>Cold and the revived's limits, for the status line (spec B.2, C.2).</summary>
    public static string Condition(in PlayerState p, PlayerTuning t)
    {
        string cold = PlayerMotor.Chilled(p, t) ? $" | COLD: {Math.Max(0, t.Cold.DeathSeconds - p.Cold):0}s — get inside" : "";
        string revived = p.Has(PlayerFlags.Revived) ? " | REVIVED: cold, light things only, no guns until the next stop" : "";
        return cold + revived;
    }

    /// <summary>A Vigil under way, or the hint that one could be held (spec C.2).</summary>
    public static string VigilStatus(World world) => world.Vigil switch
    {
        { Active: true } v => $" | VIGIL {v.Left:0}s — engine off, lights out, guns dead, the Choir is coming",
        _ => "",
    };

    /// <summary>What there is to load at a facility (spec D).</summary>
    static string SiteStatus(Site? site)
    {
        if (site is null)
            return " — nothing here to load";
        var parts = new List<string>();
        if (site.Has(ModuleKind.Crates))
            parts.Add(site.HeavyStack.Length > 0 ? "crates on the platform: carry them into the cars (the big ones take two)" : "crates on the platform: carry them into the cars");
        if (site.Crane is { } crane)
            parts.Add(crane.Left == 0 ? "the castings are loaded" : crane.Hooked is not null ? "crane: a casting on the hook" : $"crane: {crane.Left} castings to rig and lift (one in the cab, one on the ground)");
        if (site.Has(ModuleKind.Winch))
            parts.Add(site.SledsLeft == 0 ? "the winch is done" : site.Turning ? $"winch HAULING {site.Progress * 100:0}%" : site.OutOfRhythm ? "winch STALLED: out of rhythm" : $"winch: two on the capstan ({site.SledsLeft} sleds)");
        return " — " + string.Join(", ", parts);
    }

    public static string RouteStatus(Route? route, World world, TrainOnLine train)
    {
        if (route is null)
            return VigilStatus(world);
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
        string next = route.NextLandmark(s) is { } l
            ? $"{(l.Kind == FeatureKind.Facility ? $"{l.Facility}" : $"{l.Kind}").ToLowerInvariant()} in {(l.Start - s) / 1000:0.0} km"
            : "terminus ahead";
        string tunnel = route.InTunnel(s) ? " | IN TUNNEL" : "";
        return $" | {route.Name} | {clock} | {next}{tunnel}{stop}{VigilStatus(world)}";
    }
}
