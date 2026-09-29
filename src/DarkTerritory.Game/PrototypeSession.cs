using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
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
    PlayerState _previousPlayer;

    public PrototypeSession(string contentRoot, string lineName = "test-loop", int cars = 6, double start = 600)
        : this(contentRoot, RailLine.Load(Path.Combine(contentRoot, "lines", lineName + ".json")), null, cars, start)
    {
    }

    /// <summary>Plays a generated route from the fortress yard to the terminus, against the dawn clock.</summary>
    public PrototypeSession(string contentRoot, Route route, int cars = 6)
        : this(contentRoot, route.Build(), route, cars, 0)
    {
    }

    PrototypeSession(string contentRoot, RailLine line, Route? route, int cars, double start)
    {
        Route = route;
        _trainTuning = new HotData<TrainTuning>(Path.Combine(contentRoot, TrainTuning.File));
        _playerTuning = new HotData<PlayerTuning>(Path.Combine(contentRoot, PlayerTuning.File));
        _boilerTuning = new HotData<BoilerTuning>(Path.Combine(contentRoot, BoilerTuning.File));
        var consist = Consist.Uniform(_trainTuning.Value, cars, 1);
        // On a route, start in the fortress yard with the whole train on the level.
        if (route is not null)
            start = consist.LengthMetres + 150;
        Train = new TrainOnLine(new TrainDynamics(consist), line, start, _boilerTuning.Value);
        Controls = new TrainControls { Reverser = 1 };
        Respawn(0);
    }

    public TrainOnLine Train { get; }
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
        CrewActions.Apply(ref Player, intent, Train, SimConstants.TickSeconds);
        Train.Step(SimConstants.TickSeconds, Controls);
        PlayerMotor.Step(ref Player, intent, Train, PlayerTuning, TrainTuning, SimConstants.TickSeconds);
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
               $"| {boiler} |{(Train.Rakes.Count > 1 ? $" {Train.Rakes.Count} rakes |" : "")} grade {Train.AverageGrade(),4:0.0}% | {d.Distance / 1000:0.00}/{Train.Line.Length / 1000:0.0} km | {state}" +
               RouteStatus() +
               (LastReloadError is null ? "" : $" | TUNING ERROR: {LastReloadError}");
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
