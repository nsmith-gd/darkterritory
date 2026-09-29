using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Single-player feel prototype (roadmap M1): one train, one player, live-reloaded tuning.
/// Exists to answer spec G.1 (does the 4:1 speed ratio feel right?) and G.2 (is a 94 s roof
/// traverse fun?). Networking replaces the direct sim calls in M2; the sim itself doesn't change.
/// </summary>
public sealed class PrototypeSession
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
    }

    PrototypeSession(string contentRoot, RailLine line, Route? route, int cars, double start)
    {
        Route = route;
        _trainTuning = new HotData<TrainTuning>(Path.Combine(contentRoot, TrainTuning.File));
        _playerTuning = new HotData<PlayerTuning>(Path.Combine(contentRoot, PlayerTuning.File));
        _boilerTuning = new HotData<BoilerTuning>(Path.Combine(contentRoot, BoilerTuning.File));
        _combatTuning = new HotData<CombatTuning>(Path.Combine(contentRoot, CombatTuning.File));
        var consist = Consist.Uniform(_trainTuning.Value, cars, 1);
        // On a route, start in the fortress yard with the whole train on the level.
        if (route is not null)
            start = consist.LengthMetres + 150;
        Train = new TrainOnLine(new TrainDynamics(consist), line, start, _boilerTuning.Value);
        World = new World(Train, _combatTuning.Value);
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

    public TrainTuning TrainTuning => _trainTuning.Value;
    public PlayerTuning PlayerTuning => _playerTuning.Value;

    public void Step(in PlayerIntent intent)
    {
        ReloadTuning();
        _previousPlayer = Player;
        World.BeginTick();
        World.CrewAct(ref Player, intent, 1);
        World.Step(Controls);
        World.ApplyDamage(id => id == 1 ? Player : null, (_, s) => Player = s, [1]);
        foreach (var e in World.EnemyEvents)
            if (Cue(e) is { } cue)
                _cues.Add((ElapsedSeconds, cue));
        _cues.RemoveAll(c => ElapsedSeconds - c.At > CueSeconds);
        PlayerMotor.Step(ref Player, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds, applyLook: false);
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
    public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch)
    {
        var cur = Player;
        var prev = _previousPlayer;
        var local = prev.Parent == cur.Parent ? Double3.Lerp(prev.Position, cur.Position, alpha) : cur.Position;
        var eyeLocal = local + Double3.Up * (cur.Alive ? 1.65 : 0.3);
        bool onCar = cur.Parent != PlayerState.World;
        var eye = onCar ? frames[cur.Parent].ToWorld(eyeLocal) : eyeLocal;
        double heading = onCar ? frames[cur.Parent].Heading : 0;
        return new Camera
        {
            Position = eye,
            Yaw = cur.Yaw + pendingYaw + heading,
            Pitch = Math.Clamp(cur.Pitch + pendingPitch, -1.5, 1.5),
            FovYDegrees = 75,
            Near = 0.05f,
            Far = 2000,
        };
    }

    public string Status()
    {
        var d = Train.Dynamics;
        string where = Player.Parent == PlayerState.World ? "ground" : PlayerMotor.InCab(Player, Train) ? "cab" : Player.Parent == 0 ? "engine" : $"car {Player.Parent}";
        var b = Train.Boiler;
        string boiler = b.Ruptured ? "BOILER RUPTURED" :
            $"P {b.Pressure,3:0}{(b.SafetyValveLifting ? " VALVE" : "")} fire {b.Firebox:0.0} tender {b.Tender:0}" +
            (Player.ActionProgress > 0 ? $" shovel {Player.ActionProgress:0.0}s" : "");
        string state = Player.Alive ? $"{Player.Surface} {where} hp {Player.Health}" : $"DEAD ({Player.Death}) — Backspace to respawn";
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
            else if (e is CinderHound { Phase: SpinePhase.Punish } h && !parts.Contains("hounds aboard"))
                parts.Add("hounds aboard");
        }
        return parts.Count == 0 ? "" : " | " + string.Join(" · ", parts);
    }

    string Gunnery()
    {
        var c = _combatTuning.Value;
        string gun = Guns.MannedGun(Player, Train, c.Guns) is { } g ? $" GUN {Train.Vehicles[g].Gun.Ammo} rds |" : "";
        return $"{gun} choir {World.Choir.Phase(c.Choir).ToString().ToLowerInvariant()} {World.Choir.Aggro:0} |";
    }

    string RouteStatus()
    {
        if (Route is null)
            return "";
        double dawn = Route.DawnSeconds - ElapsedSeconds;
        string clock = dawn > 0 ? $"dawn {(int)dawn / 60:00}:{(int)dawn % 60:00}" : "DAWN — the line is live";
        double s = Train.Dynamics.Distance;
        string next = Route.NextLandmark(s) is { } f
            ? $"{(f.Kind == FeatureKind.Facility ? $"{f.Facility}" : $"{f.Kind}").ToLowerInvariant()} in {(f.Start - s) / 1000:0.0} km"
            : "terminus ahead";
        string tunnel = Route.InTunnel(s) ? " | IN TUNNEL" : "";
        return $" | {Route.Name} | {clock} | {next}{tunnel}";
    }
}
