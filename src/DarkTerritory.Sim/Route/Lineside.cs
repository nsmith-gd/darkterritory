using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Route;

/// <summary>Mirror of content/tuning/sight.json. Field docs live in that file.</summary>
public sealed record SightTuning
{
    public const string File = "tuning/sight.json";

    public double LampSignRange { get; init; } = 350;
    public double DarkSignRange { get; init; } = 10;
    public double LampGreaseRange { get; init; } = 150;
    public double DarkGreaseRange { get; init; } = 15;
    public double BoardAhead { get; init; } = 450;
    public double CurveLateral { get; init; } = 0.4;
    public double PostBelow { get; init; } = 15;
    public double WeakBridgeLimit { get; init; } = 7;
    public double LurchOver { get; init; } = 1.5;
    public double ThrowOver { get; init; } = 3.5;
    public double DerailRatio { get; init; } = 1.55;
    public double CargoDamagePerSecond { get; init; } = 0.01;
    public double StrainPerSecond { get; init; } = 0.004;
    public double ThrowSpeed { get; init; } = 3;
    public double GreaseTraction { get; init; } = 0.25;
    public int StruckDamage { get; init; } = 200;
}

/// <summary>What a lineside board says (GDD §22: the line's own warnings, which the headlamp has to find).</summary>
public enum SignKind : byte
{
    /// <summary>A posted speed for the curve or weak bridge past it: over it, the train lurches and throws people off.</summary>
    SpeedLimit,
    /// <summary>A tunnel ahead: its mouth takes anyone standing on a roof.</summary>
    LowClearance,
}

/// <param name="Board">Where the board stands beside the main line.</param>
/// <param name="Start">The stretch it's about: from here…</param>
/// <param name="End">…to here.</param>
/// <param name="Limit">For a speed board, the posted speed (m/s).</param>
public sealed record Sign(int Id, SignKind Kind, double Board, double Start, double End, double Limit = 0)
{
    /// <summary>A board's figure, as it's painted: km/h, in fives.</summary>
    public int LimitKmh => (int)(Math.Floor(Limit * 3.6 / 5) * 5);
}

/// <summary>
/// The line's boards and what they warn of (the user's call after the 100-night playtest: running dark has to cost sign
/// sight as well as obstacle sight). The boards are worked out from the route, the same everywhere, so nothing about them
/// is sent. A board is read once the headlamp reaches it (<see cref="SightTuning.LampSignRange"/>); lamps down, its paint
/// is nothing in the dark, and what it warned of is only made out when it's almost on you (<see cref="SightTuning.DarkSignRange"/>
/// short of the curve, the bridge, the tunnel's mouth). The hazards themselves are the host's.
/// </summary>
public sealed class Lineside
{
    readonly List<Sign> _signs;
    readonly bool[] _read;
    readonly Route _route;
    readonly HashSet<(int Sign, int Player)> _thrown = [];

    public Lineside(SightTuning tuning, Route route)
    {
        Tuning = tuning;
        _route = route;
        _signs = [.. Boards(tuning, route)];
        _read = new bool[_signs.Count];
    }

    public SightTuning Tuning { get; }
    public IReadOnlyList<Sign> Signs => _signs;
    /// <summary>Boards read so far tonight: you know what's ahead once you've seen the board.</summary>
    public bool Read(int sign) => _read[sign];
    /// <summary>Boards first read this tick (the HUD's cue, the pacing log).</summary>
    public List<Sign> ReadThisTick { get; } = new();

    /// <summary>How far a board can be read from, lamp lit or not.</summary>
    public double SignRange(bool lamp) => lamp ? Tuning.LampSignRange : Tuning.DarkSignRange;

    /// <summary>Every board on a route: before each sharp curve and weak bridge, a speed; before each tunnel, the clearance.</summary>
    public static IEnumerable<Sign> Boards(SightTuning t, Route route)
    {
        var zones = new List<(SignKind Kind, double Start, double End, double Limit)>();
        double s = 0, curveStart = -1, curveLimit = double.MaxValue;
        foreach (var seg in route.Line.Segments)
        {
            double limit = seg.Radius == 0 ? double.MaxValue : Math.Sqrt(t.CurveLateral * Math.Abs(seg.Radius));
            if (limit < t.PostBelow)
            {
                if (curveStart < 0)
                    curveStart = s;
                curveLimit = Math.Min(curveLimit, limit);
            }
            else if (curveStart >= 0)
            {
                zones.Add((SignKind.SpeedLimit, curveStart, s, curveLimit));
                curveStart = -1;
                curveLimit = double.MaxValue;
            }
            s += seg.Length;
        }
        if (curveStart >= 0)
            zones.Add((SignKind.SpeedLimit, curveStart, s, curveLimit));
        foreach (var b in route.Of(FeatureKind.Bridge).Where(b => b.MaxCars > 0))
            zones.Add((SignKind.SpeedLimit, b.Start, b.End, t.WeakBridgeLimit));
        foreach (var tunnel in route.Of(FeatureKind.Tunnel))
            zones.Add((SignKind.LowClearance, tunnel.Start, tunnel.End, 0));
        int id = 0;
        foreach (var z in zones.OrderBy(z => z.Start).ThenBy(z => z.Kind))
            yield return new Sign(id++, z.Kind, Math.Max(0, z.Start - t.BoardAhead), z.Start, z.End, z.Limit);
    }

    /// <summary>
    /// Every machine, each tick: boards the lamp (or, lamps down, the eye) has reached are read, and the rail's grip set
    /// for where the engine is (Grease, App. A.2: the drivers slip, the brakes barely bite).
    /// </summary>
    public void See(TrainOnLine train, bool lamp)
    {
        ReadThisTick.Clear();
        var engine = train.Dynamics;
        double front = engine.Distance, range = SignRange(lamp);
        bool main = engine.Path == RailLine.MainPath;
        for (int i = 0; i < _signs.Count; i++)
            if (!_read[i] && main && (lamp ? _signs[i].Board : _signs[i].Start) - front <= range && _signs[i].End > front)
            {
                _read[i] = true;
                ReadThisTick.Add(_signs[i]);
            }
        train.Traction = main && _route.Features.Any(f => f.Kind == FeatureKind.Grease && f.Contains(front)) ? Tuning.GreaseTraction : 1;
    }

    /// <summary>Grease on the rail ahead that the lamp (or the eye, lamps down) can make out: its start, if any.</summary>
    public double? GreaseAhead(TrainOnLine train, bool lamp)
    {
        double front = train.Dynamics.Distance, range = lamp ? Tuning.LampGreaseRange : Tuning.DarkGreaseRange;
        return _route.Features.Where(f => f.Kind == FeatureKind.Grease && f.End > front && f.Start - front <= range)
            .Select(f => (double?)f.Start).Min();
    }

    /// <summary>
    /// The host, after the train steps: a posted curve taken too fast lurches the cargo about and throws whoever's up on
    /// the roofs over the side (far too fast and it's off the rails); a tunnel's mouth takes anyone standing on a roof.
    /// A mounted gun's crew are down behind its shield, clear of both.
    /// </summary>
    public void Hazards(World world, IReadOnlyList<(int Id, PlayerState State)> crew, List<DamageEvent> damage)
    {
        var train = world.Train;
        double dt = SimConstants.TickSeconds;
        foreach (var sign in _signs)
        {
            foreach (var v in train.Dynamics.Consist.Vehicles)
            {
                var rake = train.RakeOf(v.Id);
                if (rake.Path != RailLine.MainPath)
                    continue;
                double carFront = train.Cars[v.Id].FrontDistance, carRear = carFront - v.Length(train.Dynamics.Tuning);
                if (carFront < sign.Start || carRear > sign.End)
                    continue;
                if (sign.Kind == SignKind.SpeedLimit)
                {
                    double over = rake.Speed - sign.Limit;
                    if (rake.Speed >= sign.Limit * Tuning.DerailRatio && !world.Derailed)
                        world.Derail();
                    if (over > Tuning.LurchOver)
                    {
                        // The frames strain (repairs, spec F.1) and the loads shift about.
                        v.Integrity = Math.Max(0, v.Integrity - Tuning.StrainPerSecond * over * dt);
                        if (v.Kind == VehicleKind.Cargo)
                            v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - Tuning.CargoDamagePerSecond * over * dt);
                    }
                    if (over <= Tuning.ThrowOver)
                        continue;
                }
                foreach (var (id, s) in crew)
                {
                    if (!s.Alive || s.Parent != v.Id || s.Surface != Surface.Roof || world.Combat is { } c && Combat.Guns.MannedGun(s, train, c.Guns) is not null)
                        continue;
                    if (sign.Kind == SignKind.LowClearance)
                    {
                        // Where along the line they stand: a car's −Z is its front.
                        double along = carFront - (s.Position.Z + train.Frames[v.Id].Shape.HalfLength);
                        if (along >= sign.Start && along <= sign.End)
                            damage.Add(new DamageEvent(id, Tuning.StruckDamage, DeathCause.Struck));
                    }
                    else if (_thrown.Add((sign.Id, id)))
                    {
                        // Out, away from the curve's centre: the side the train leans away from.
                        double curvature = train.Line.Sample(RailLine.MainPath, carFront).Curvature;
                        var frame = train.Frames[v.Id];
                        var outward = frame.DirToWorld(new Double3(curvature >= 0 ? 1 : -1, 0, 0));
                        damage.Add(new DamageEvent(id, 0, DeathCause.Thrown, outward * Tuning.ThrowSpeed));
                    }
                }
            }
        }
    }
}
