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
    public double SandSeconds { get; init; } = 8;
    public double SandFadeSeconds { get; init; } = 3;
    public int StruckDamage { get; init; } = 200;
    public double[] DropSpacing { get; init; } = [350, 600];
    public double DropFrom { get; init; } = 900;
    public double DropClear { get; init; } = 80;
    public double DropBoardAhead { get; init; } = 400;
    public double CatchReach { get; init; } = 1.1;
    public Dictionary<string, double> DropWeights { get; init; } = new() { ["mail"] = 0.45, ["coal"] = 0.2, ["ammo"] = 0.2, ["spares"] = 0.15 };
    public double[] MailScrip { get; init; } = [20, 60];
    public double CoalUnits { get; init; } = 8;
    public double AmmoRounds { get; init; } = 40;
    public double SparesIntegrity { get; init; } = 0.25;
    public double TerminusBoard { get; init; } = 1400;
    public double HomeSignal { get; init; } = 600;
    public double PlatformBoard { get; init; } = 100;
}

/// <summary>What hangs on a lineside mail crane (the playtest's rewards): pay, coal, rounds, or spares for the worst car.</summary>
public enum DropKind : byte { Mail, Coal, Ammo, Spares }

/// <summary>A bag on a mail crane beside the line, at <paramref name="At"/>, on a side (+1 right), and what's in it.</summary>
public sealed record Drop(int Id, DropKind Kind, double At, int Side, double Amount);

/// <summary>What a lineside board says (GDD §22: the line's own warnings, which the headlamp has to find).</summary>
public enum SignKind : byte
{
    /// <summary>A posted speed for the curve or weak bridge past it: over it, the train lurches and throws people off.</summary>
    SpeedLimit,
    /// <summary>A tunnel ahead: its mouth takes anyone standing on a roof.</summary>
    LowClearance,
    /// <summary>A mail crane ahead, and which side: someone in a car's open side door with the hook out catches its bag.</summary>
    Drop,
    /// <summary>The terminus's distant board: the last run in.</summary>
    Terminus,
}

/// <param name="Board">Where the board stands beside the main line.</param>
/// <param name="Start">The stretch it's about: from here…</param>
/// <param name="End">…to here.</param>
/// <param name="Limit">For a speed board, the posted speed (m/s).</param>
public sealed record Sign(int Id, SignKind Kind, double Board, double Start, double End, double Limit = 0)
{
    /// <summary>A drop board's drop.</summary>
    public Drop? Drop { get; init; }
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
    readonly List<Drop> _drops;
    readonly byte[] _dropped;

    public Lineside(SightTuning tuning, Route route)
    {
        Tuning = tuning;
        _route = route;
        _drops = [.. Drops(tuning, route)];
        _dropped = new byte[_drops.Count];
        _signs = [.. Boards(tuning, route, _drops)];
        _read = new bool[_signs.Count];
    }

    /// <summary>Tonight's mail cranes, in order along the line.</summary>
    public IReadOnlyList<Drop> AllDrops => _drops;
    /// <summary>A drop's bag caught (host only: the crane's arm is empty on a client that didn't see it go).</summary>
    public bool Caught(int drop) => _dropped[drop] == 1;
    /// <summary>A drop the whole train has gone by (caught or not).</summary>
    public bool Passed(int drop) => _dropped[drop] != 0;
    /// <summary>Bags caught this tick, and cranes the train went by with nobody at a door (the pacing log, the HUD).</summary>
    public List<Drop> CaughtThisTick { get; } = new();
    public List<Drop> MissedThisTick { get; } = new();

    /// <summary>
    /// The mail cranes along a route (the playtest's rewards, so there's something to reach for between the threats): every
    /// <see cref="SightTuning.DropSpacing"/> from the yard to the terminus's board, on either side, clear of tunnels, bridges,
    /// facilities and junctions. From the route's seed, so every machine has the same.
    /// </summary>
    public static IEnumerable<Drop> Drops(SightTuning t, Route route)
    {
        var rng = new Pcg32(route.Seed, 0xD209);
        var kinds = new (DropKind Kind, double Weight)[]
        {
            (DropKind.Mail, t.DropWeights.GetValueOrDefault("mail")), (DropKind.Coal, t.DropWeights.GetValueOrDefault("coal")),
            (DropKind.Ammo, t.DropWeights.GetValueOrDefault("ammo")), (DropKind.Spares, t.DropWeights.GetValueOrDefault("spares")),
        };
        double total = kinds.Sum(k => k.Weight);
        int id = 0;
        for (double s = t.DropFrom; s < route.Length - t.TerminusBoard; s += rng.Range(t.DropSpacing[0], t.DropSpacing[1]))
        {
            if (route.Features.Any(f => f.Kind is FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Facility or FeatureKind.Junction
                    && s >= f.Start - t.DropClear && s <= f.End + t.DropClear)
                || route.Branches.Any(b => Math.Abs(s - b.Toe) < t.DropClear + 40))
                continue;
            double pick = rng.NextDouble() * total;
            var kind = kinds[^1].Kind;
            foreach (var (k, w) in kinds)
            {
                if (pick < w)
                {
                    kind = k;
                    break;
                }
                pick -= w;
            }
            int side = rng.Chance(0.5) ? 1 : -1;
            double amount = kind switch
            {
                DropKind.Mail => Math.Round(rng.Range(t.MailScrip[0], t.MailScrip[1])),
                DropKind.Coal => t.CoalUnits,
                DropKind.Ammo => t.AmmoRounds,
                _ => t.SparesIntegrity,
            };
            yield return new Drop(id++, kind, Math.Round(s, 1), side, amount);
        }
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
    public static IEnumerable<Sign> Boards(SightTuning t, Route route) => Boards(t, route, [.. Drops(t, route)]);

    public static IEnumerable<Sign> Boards(SightTuning t, Route route, IReadOnlyList<Drop> drops)
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
        var boards = zones.Select(z => new Sign(0, z.Kind, Math.Max(0, z.Start - t.BoardAhead), z.Start, z.End, z.Limit))
            .Concat(drops.Select(d => new Sign(0, SignKind.Drop, Math.Max(0, d.At - t.DropBoardAhead), d.At, d.At) { Drop = d }))
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.Length - t.TerminusBoard), route.Length, route.Length))
            // And the home signal and the platform's board, close in: the last run in has its moments too.
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.Length - t.HomeSignal), route.Length, route.Length))
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.Length - t.PlatformBoard), route.Length, route.Length));
        int id = 0;
        foreach (var b in boards.OrderBy(b => b.Start).ThenBy(b => b.Kind))
            yield return b with { Id = id++ };
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
        // Sanding from the running boards (App. A.2: "restores traction over ~8s"): the grip comes back while sand's going
        // down, and goes again once it stops (the wheels roll on off what's been laid).
        double dt = SimConstants.TickSeconds;
        train.Sand = train.Sanding ? Math.Min(1, train.Sand + dt / Tuning.SandSeconds) : Math.Max(0, train.Sand - dt / Tuning.SandFadeSeconds);
        train.Sanding = false;
        bool greased = main && _route.Features.Any(f => f.Kind == FeatureKind.Grease && f.Contains(front));
        train.Traction = greased ? Tuning.GreaseTraction + (1 - Tuning.GreaseTraction) * train.Sand : 1;
    }

    /// <summary>The engine's on greased rail now (what the drivers feel: they slip).</summary>
    public bool OnGrease(TrainOnLine train) =>
        train.Dynamics.Path == RailLine.MainPath && _route.Features.Any(f => f.Kind == FeatureKind.Grease && f.Contains(train.Dynamics.Distance));

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
    public void Hazards(World world, IReadOnlyList<(int Id, PlayerState State, PlayerIntent Intent)> crew, List<DamageEvent> damage)
    {
        var train = world.Train;
        double dt = SimConstants.TickSeconds;
        Catch(world, crew);
        foreach (var sign in _signs)
        {
            if (sign.Kind is SignKind.Drop or SignKind.Terminus)
                continue;
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
                foreach (var (id, s, _) in crew)
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

    /// <summary>
    /// The host: each car's side doors going by a crane, with someone in the doorway on its side holding the hook out
    /// (Fire, off a gun), and the bag's theirs; once the last car's by, it's missed. Pay's counted in with the night's
    /// (paid at the terminus, spec F.1); coal goes in the tender, rounds on the guns, spares on the worst-off car.
    /// </summary>
    void Catch(World world, IReadOnlyList<(int Id, PlayerState State, PlayerIntent Intent)> crew)
    {
        CaughtThisTick.Clear();
        MissedThisTick.Clear();
        var train = world.Train;
        var engine = train.Dynamics;
        if (engine.Path != RailLine.MainPath)
            return;
        double moved = engine.Distance - engine.PreviousDistance;
        var layout = engine.Tuning.Geometry.Interior;
        foreach (var d in _drops)
        {
            if (_dropped[d.Id] != 0 || d.At > engine.Distance + 1)
                continue;
            if (d.At < engine.RearDistance - 1)
            {
                _dropped[d.Id] = 2;
                MissedThisTick.Add(d);
                continue;
            }
            foreach (var v in engine.Consist.Vehicles)
            {
                var shape = train.Frames[v.Id].Shape;
                double door = train.Cars[v.Id].FrontDistance - shape.HalfLength;
                // The car's middle (its side doors) went by the crane this tick.
                if (layout is null || moved <= 0 || !(door >= d.At && door - moved < d.At))
                    continue;
                int? sideDoor = Bots.StopHand.SideDoor(shape, d.Side);
                bool caught = sideDoor is { } sd && v.DoorOpen(sd) && crew.Any(c => c.State.Alive && c.State.Parent == v.Id
                    && PlayerMotor.Indoors(c.State, train) && c.Intent.Has(PlayerButtons.Fire)
                    && d.Side * c.State.Position.X >= shape.HalfWidth - Tuning.CatchReach
                    && Math.Abs(c.State.Position.Z) <= layout.SideDoorWidth / 2 + 0.3);
                if (!caught)
                    continue;
                _dropped[d.Id] = 1;
                CaughtThisTick.Add(d);
                Reward(world, d);
                break;
            }
        }
    }

    static void Reward(World world, Drop d)
    {
        var train = world.Train;
        switch (d.Kind)
        {
            case DropKind.Mail:
                world.Run?.AddSalvage(d.Amount);
                break;
            case DropKind.Coal when train.BoilerTuning is { } bt:
                train.Boiler.Tender = Math.Min(bt.TenderCapacity, train.Boiler.Tender + d.Amount);
                break;
            case DropKind.Ammo when world.Combat is { } c:
                foreach (var v in train.Vehicles.Where(v => v.HasGun))
                    v.Gun.Ammo = Math.Min(c.Guns.Ammo, v.Gun.Ammo + (int)d.Amount);
                break;
            case DropKind.Spares:
                var worst = train.Dynamics.Consist.Vehicles.MinBy(v => v.Integrity);
                if (worst is not null)
                    worst.Integrity = Math.Min(1, worst.Integrity + d.Amount);
                break;
        }
    }
}
