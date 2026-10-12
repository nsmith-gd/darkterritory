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
    public double PlanPostBelow { get; init; } = 22;
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
    public RoofWarningTuning RoofWarning { get; init; } = new();
}

/// <summary>Mirror of sight.json's <c>roofWarning</c> (GDD App. A.1's fairness contract, note 260). Field docs live in that file.</summary>
public sealed record RoofWarningTuning
{
    public double LeadSeconds { get; init; } = 10;
    public double LeadMargin { get; init; } = 30;
    public double MinSpeed { get; init; } = 3;
    public double CurveHeadroom { get; init; } = 1.5;
    public bool RoofOnly { get; init; } = true;
    public double RepeatSeconds { get; init; } = 4;
    public bool CrouchClearsMouth { get; init; }
    public double CrouchHead { get; init; } = 1.0;
    public string LowClearanceSound { get; init; } = "warn-low-clearance";
    public string CurveSound { get; init; } = "warn-curve";
}

/// <summary>
/// A lineside hazard that will take whoever's up on the roofs, coming (note 260): which board's stretch, how far off the
/// leading car it still is (0 once it's in it) and, at the speed now, in how long.
/// </summary>
public readonly record struct RoofWarning(SignKind Kind, int Sign, double Metres, double Seconds, int LimitKmh, bool Bridge = false);

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
    /// <summary>A speed board for a weak bridge, not a bend.</summary>
    public bool Bridge { get; init; }
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
    // Note 260: how many ticks running each board's roof warning has been up (0: it isn't), on every machine.
    readonly int[] _warnTicks;
    readonly HashSet<(int Sign, int Player)> _spared = [];
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
        _warnTicks = new int[_signs.Count];
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
        for (double s = t.DropFrom; s < route.RunLength - t.TerminusBoard; s += rng.Range(t.DropSpacing[0], t.DropSpacing[1]))
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
        var zones = new List<(SignKind Kind, double Start, double End, double Limit, bool Bridge)>();
        double s = 0, curveStart = -1, curveLimit = double.MaxValue;
        // Note 266: a generated line's bends are boarded by the plan (§8.5, LineBuilder.Signage: posted at √(aPost R), every bend
        // that derails under planPostBelow), and these boards carry the same figure, so the roof warning, the HUD and the
        // board by the line agree. A hand-laid route keeps this file's own reckoning.
        var plan = route.Plan;
        foreach (var seg in route.Line.Segments)
        {
            double r = Math.Abs(seg.Radius);
            double limit = seg.Radius == 0 ? double.MaxValue : plan is null ? Math.Sqrt(t.CurveLateral * r) : Math.Floor(Math.Sqrt(plan.Rules.APost * r));
            bool posted = plan is null ? limit < t.PostBelow : seg.Radius != 0 && Math.Sqrt(plan.Rules.ADerail * r) < t.PlanPostBelow;
            if (posted)
            {
                if (curveStart < 0)
                    curveStart = s;
                curveLimit = Math.Min(curveLimit, limit);
            }
            else if (curveStart >= 0)
            {
                zones.Add((SignKind.SpeedLimit, curveStart, s, curveLimit, false));
                curveStart = -1;
                curveLimit = double.MaxValue;
            }
            s += seg.Length;
        }
        if (curveStart >= 0)
            zones.Add((SignKind.SpeedLimit, curveStart, s, curveLimit, false));
        foreach (var b in route.Of(FeatureKind.Bridge).Where(b => b.MaxCars > 0))
            zones.Add((SignKind.SpeedLimit, b.Start, b.End, t.WeakBridgeLimit, true));
        foreach (var tunnel in route.Of(FeatureKind.Tunnel))
            zones.Add((SignKind.LowClearance, tunnel.Start, tunnel.End, 0, false));
        var boards = zones.Select(z => new Sign(0, z.Kind, Math.Max(0, z.Start - t.BoardAhead), z.Start, z.End, z.Limit) { Bridge = z.Bridge })
            .Concat(drops.Select(d => new Sign(0, SignKind.Drop, Math.Max(0, d.At - t.DropBoardAhead), d.At, d.At) { Drop = d }))
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.RunLength - t.TerminusBoard), route.RunLength, route.RunLength))
            // And the home signal and the platform's board, close in: the last run in has its moments too.
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.RunLength - t.HomeSignal), route.RunLength, route.RunLength))
            .Append(new Sign(0, SignKind.Terminus, Math.Max(0, route.RunLength - t.PlatformBoard), route.RunLength, route.RunLength));
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
        // The roof warnings (note 260), lamp or no lamp: how long each has been up is what the host's hazards ask.
        for (int i = 0; i < _signs.Count; i++)
            _warnTicks[i] = Warns(_signs[i], train) ? _warnTicks[i] + 1 : 0;
        // Sanding from the running boards (App. A.2: "restores traction over ~8s"): the grip comes back while sand's going
        // down, and goes again once it stops (the wheels roll on off what's been laid).
        double dt = SimConstants.TickSeconds;
        train.Sand = train.Sanding ? Math.Min(1, train.Sand + dt / Tuning.SandSeconds) : Math.Max(0, train.Sand - dt / Tuning.SandFadeSeconds);
        train.Sanding = false;
        bool greased = main && _route.Features.Any(f => f.Kind == FeatureKind.Grease && f.Contains(front));
        train.Traction = greased ? Tuning.GreaseTraction + (1 - Tuning.GreaseTraction) * train.Sand : 1;
    }

    /// <summary>
    /// The speed over which a posted stretch throws whoever's up on the roofs over the side (sight.json <c>throwOver</c>; with
    /// roof handrails, spec F.3 and note 184, it takes a harder lean).
    /// </summary>
    public double ThrowsAbove(Sign sign, TrainOnLine train)
    {
        var fit = train.Dynamics.Tuning.Composition;
        return sign.Limit + Tuning.ThrowOver * (fit.Handrails ? fit.Rails.ThrowOver : 1);
    }

    /// <summary>
    /// Note 260 (GDD App. A.1: "TELEGRAPH always precedes COMMIT"): a board's stretch will take whoever's up on the roofs, and
    /// it's near enough to say so. A tunnel's mouth within <see cref="RoofWarningTuning.LeadSeconds"/> of the leading car at
    /// the speed now (plus <see cref="RoofWarningTuning.LeadMargin"/>, for a train gathering speed), or a posted stretch as
    /// near with the train within <see cref="RoofWarningTuning.CurveHeadroom"/> of throwing them off it; until the last car's
    /// through. From the route and the train alone, lamp or no lamp: running dark costs the driver the boards, not the roof
    /// riders their warning. Every machine works it out the same, so nothing's sent.
    /// </summary>
    public bool Warns(Sign sign, TrainOnLine train)
    {
        var engine = train.Dynamics;
        if (engine.Path != RailLine.MainPath || sign.Kind is not (SignKind.LowClearance or SignKind.SpeedLimit))
            return false;
        var w = Tuning.RoofWarning;
        double speed = engine.Speed;
        if (sign.End < engine.RearDistance || sign.Start - engine.Distance > Math.Max(speed, w.MinSpeed) * w.LeadSeconds + w.LeadMargin)
            return false;
        return sign.Kind == SignKind.LowClearance || speed > ThrowsAbove(sign, train) - w.CurveHeadroom;
    }

    /// <summary>The most pressing roof warning now (the nearest; a tunnel before a bend at the same spot), or null.</summary>
    public RoofWarning? Warning(TrainOnLine train)
    {
        RoofWarning? best = null;
        double front = train.Dynamics.Distance, speed = train.Dynamics.Speed;
        foreach (var sign in _signs)
        {
            if (!Warns(sign, train))
                continue;
            double metres = Math.Max(0, sign.Start - front);
            if (best is { } b && (b.Metres < metres || b.Metres == metres && b.Kind == SignKind.LowClearance))
                continue;
            best = new RoofWarning(sign.Kind, sign.Id, metres, speed > 0.1 ? metres / speed : double.PositiveInfinity, sign.LimitKmh, sign.Bridge);
        }
        return best;
    }

    /// <summary>How long a board's roof warning has been up, running (0: it isn't).</summary>
    public double WarnedSeconds(int sign) => _warnTicks[sign] * SimConstants.TickSeconds;

    /// <summary>
    /// Whether a board's warning has been up its full lead: only then may its hazard take anyone (App. A.1). As shown: this is
    /// counted before the train steps, and the HUD has it after, a tick on, so a full lead on screen is a tick more here.
    /// </summary>
    public bool WarnedInTime(int sign) => WarnedSeconds(sign) >= Tuning.RoofWarning.LeadSeconds + SimConstants.TickSeconds / 2;

    /// <summary>
    /// Who a roof warning is for: up on a roof, or on an end ladder (on the way up, or down), and not a gun's crew down behind
    /// its shield (clear of both hazards). With <see cref="RoofWarningTuning.RoofOnly"/> off, anyone alive aboard.
    /// </summary>
    public bool For(in PlayerState s, World world)
    {
        if (!s.Alive || s.Parent < 0)
            return false;
        if (!Tuning.RoofWarning.RoofOnly)
            return true;
        return s.Surface is Surface.Roof or Surface.Ladder
            && !(world.Combat is { } c && Combat.Guns.MannedGun(s, world.Train, c.Guns) is not null);
    }

    /// <summary>
    /// Host: a hazard that would have taken someone with its warning not up its full lead, and didn't (once a board and
    /// player). The fairness contract held structurally; a night's count of these should be zero (note 260).
    /// </summary>
    public int Spared => _spared.Count;

    /// <summary>
    /// Host: each time a hazard took someone up top, on what tick, whom, and which board's (the mouth's blow, the throw):
    /// what `dt playthrough` audits against the warning it watched go up (note 260).
    /// </summary>
    public List<(uint Tick, int Player, int Sign, SignKind Kind)> Commits { get; } = new();

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
                    // On a generated line the bend's derailment is TrackRules' (plan §8.5, warned: note 265), not this.
                    if (world.TrackPlan is null && rake.Speed >= sign.Limit * Tuning.DerailRatio && !world.Derailed)
                        world.Overspeed($"took the {sign.LimitKmh} km/h bend at {rake.Speed * 3.6:0} km/h, {rake.Speed * 3.6 - sign.LimitKmh:0} km/h too fast");
                    if (over > Tuning.LurchOver)
                    {
                        // The frames strain (repairs, spec F.1) and the loads shift about.
                        v.Integrity = Math.Max(0, v.Integrity - Tuning.StrainPerSecond * over * dt);
                        if (v.Kind == VehicleKind.Cargo)
                            v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - Tuning.CargoDamagePerSecond * over * dt);
                    }
                    // Roof handrails (spec F.3, note 184): there's something to hold, so it takes a harder lean to throw you.
                    if (rake.Speed <= ThrowsAbove(sign, train))
                        continue;
                }
                var w = Tuning.RoofWarning;
                foreach (var (id, s, _) in crew)
                {
                    if (!s.Alive || s.Parent != v.Id || s.Surface != Surface.Roof || world.Combat is { } c && Combat.Guns.MannedGun(s, train, c.Guns) is not null)
                        continue;
                    if (sign.Kind == SignKind.LowClearance)
                    {
                        // Where along the line they stand: a car's −Z is its front.
                        double along = carFront - (s.Position.Z + train.Frames[v.Id].Shape.HalfLength);
                        if (along < sign.Start || along > sign.End)
                            continue;
                        // Note 260: whether a crouch gets you under the mouth isn't in the spec. Reading kept: it doesn't, unless
                        // tuned to (a headset rider down under crouchHead; the keyboard's body has no crouch).
                        if (w.CrouchClearsMouth && s.Head > 0 && s.Head <= w.CrouchHead)
                            continue;
                        // App. A.1: no commit without its telegraph, the warning up its whole lead.
                        if (!WarnedInTime(sign.Id))
                        {
                            _spared.Add((sign.Id, id));
                            continue;
                        }
                        damage.Add(new DamageEvent(id, Tuning.StruckDamage, DeathCause.Struck, Lethal: true)); // the line itself, not an enemy (App. A.1)
                        Commits.Add((world.Tick, id, sign.Id, sign.Kind));
                    }
                    else if (!WarnedInTime(sign.Id))
                        _spared.Add((sign.Id, id));
                    else if (_thrown.Add((sign.Id, id)))
                    {
                        // Out, away from the curve's centre: the side the train leans away from.
                        double curvature = train.Line.Sample(RailLine.MainPath, carFront).Curvature;
                        var frame = train.Frames[v.Id];
                        var outward = frame.DirToWorld(new Double3(curvature >= 0 ? 1 : -1, 0, 0));
                        damage.Add(new DamageEvent(id, 0, DeathCause.Thrown, outward * Tuning.ThrowSpeed));
                        Commits.Add((world.Tick, id, sign.Id, sign.Kind));
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
