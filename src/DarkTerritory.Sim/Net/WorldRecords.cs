using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public enum RecordKind : byte { Rake = 1, Vehicle = 2, Boiler = 3, Controls = 4, Player = 5, World = 6, Enemy = 7, Run = 8, Body = 9, Holdout = 10, Switch = 11, Crane = 12 }

/// <summary>One replicated thing as fixed-point integers. <see cref="Key"/> is kind in the top byte, id below.</summary>
public readonly record struct WireRecord(uint Key, long[] Fields)
{
    public RecordKind Kind => (RecordKind)(Key >> 24);
    public int Id => (int)(Key & 0xFFFFFF);
    public static uint MakeKey(RecordKind kind, int id) => ((uint)kind << 24) | (uint)(id & 0xFFFFFF);
    public bool SameAs(in WireRecord other) => Key == other.Key && Fields.AsSpan().SequenceEqual(other.Fields);
}

/// <summary>
/// The replicated world as a sorted list of fixed-point records, plus the codec that sends it as a delta
/// against a snapshot the client already has (ARCHITECTURE §6.2).
/// <para>
/// The host quantises its own state to exactly these values every tick (<see cref="Quantise"/>), so what a
/// client adopts is bit-for-bit what the host goes on simulating from, and prediction stays exact.
/// </para>
/// </summary>
public static class WorldRecords
{
    // Fixed-point scales. Positions and speeds to 0.1 mm; angles to 10 µrad; slow scalars and timers to 1e-6.
    const double Pos = 1e4, Ang = 1e5, Fine = 1e6, Hint = 1e2, Cm = 1e2;
    /// <summary>A body record's fields before its particles: kind, parent, carrier, owner, asleep, yaw, count, second carrier.</summary>
    const int BodyParticles = 8;
    // The Run record's header (phase, end, clock, facility, chute, scavenged), and room in a crane record's id for each of a site's cranes.
    const int RunHead = 6, CranesPerSite = 16;

    static long Q(double v, double scale) => (long)Math.Round(v * scale);
    static double D(long q, double scale) => q / scale;

    public static List<WireRecord> Capture(World world, in TrainControls controls, IEnumerable<PlayerSnapshot> players)
    {
        var train = world.Train;
        var list = new List<WireRecord>();
        foreach (var rake in train.Rakes)
        {
            var ids = rake.Consist.Vehicles;
            var f = new long[ids.Count + 6];
            f[0] = ids.Count;
            for (int i = 0; i < ids.Count; i++)
                f[1 + i] = ids[i].Id;
            f[ids.Count + 1] = Q(rake.Distance, Pos);
            f[ids.Count + 2] = Q(rake.Velocity, Pos);
            f[ids.Count + 3] = Q(rake.BrakeEfficiency, Fine);
            f[ids.Count + 4] = (rake.Handbrake ? 1 : 0) | (rake.FrontCouplerLocked ? 2 : 0);
            f[ids.Count + 5] = rake.Path;
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Rake, ids[0].Id), f));
        }
        for (int i = 0; i < train.Line.Branches.Count; i++)
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Switch, i), [train.Diverging(i) ? 1 : 0]));
        foreach (var v in train.Vehicles)
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Vehicle, v.Id),
                [Q(v.Load, Fine), Q(v.Integrity, Fine), Q(v.CargoIntegrity, Fine), v.Gun.Ammo, v.Gun.Cooldown, v.Gun.Jammed ? 1 : 0, v.Gun.LastShotTick, v.DoorsOpen, (long)v.Cargo,
                    v.LampLit ? 1 : 0, v.Gun.ReloadNeeded, Q(v.Gun.ReloadProgress, Fine),
                    // Where its gun is on its roof rail, if it has one (T93: guns are pushed from car to car).
                    v.Gun.Mounted ? 1 : 0, Q(v.Gun.Z, Fine), v.Gun.Facing,
                    // How much of it a Car Hugger has eaten (App. A.3 FEED: it's drawn gnawed away).
                    Q(v.Eaten, Fine)]));
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.World, 0),
            [Q(world.Choir.Loudness, Fine), Q(world.Choir.Build, Fine), Q(world.Choir.Floor, Fine), world.Derailed ? 1 : 0, world.LampLit ? 1 : 0, Q(world.LampOutSeconds, Fine), Q(train.Sand, Fine),
                (world.Choir.Present ? 1 : 0) | (world.Choir.Spent ? 2 : 0), Q(world.Choir.QuietSeconds, Fine), Q(world.WhistleSeconds, Fine), Q(world.Choir.Rest, Fine)]));
        foreach (var e in world.ActiveEnemies)
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Enemy, e.Id),
            [
                (long)e.Kind, (long)e.Phase, Q(e.PhaseSeconds, 1e3), Q(e.Health, 1e3), e.Attached,
                Q(e.Local.X, Pos), Q(e.Local.Y, Pos), Q(e.Local.Z, Pos), Q(e.LineDistance, Pos), Q(e.Lateral, Pos), Q(e.Height, Pos),
                Q(e.Extra, 1e3), Q(e.Extra2, 1e3), e.Holding, Q(e.GrabWindow, 1e3),
            ]));
        var b = train.Boiler;
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Boiler, 0),
        [
            Q(b.Pressure, Fine), Q(b.Firebox, Fine), Q(b.Tender, Fine), Q(b.AtMaxSeconds, Fine), Q(b.LowFireSeconds, Fine),
            Q(b.ExternalHeat, Fine), Q(b.Efficiency, Fine),
            (b.Ruptured ? 1 : 0) | (b.SafetyValveLifting ? 2 : 0) | (b.SafetyValveJammed ? 4 : 0) | (b.FireDoorOpen ? 8 : 0) | (b.Vented ? 16 : 0) | (b.WrenchOut ? 32 : 0),
            // The door's swing-shut clock: without it the host's own snap back onto the grid zeroed it every tick, and the
            // door never shut.
            Q(Math.Min(b.SinceShovel, 60), Fine),
        ]));
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Controls, 0), [Q(controls.Throttle, Fine), Q(controls.Brake, Fine), controls.Reverser]));
        if (world.Run is { } run)
        {
            // Per facility: the chute's coal left, then its loading modules (crates out, winch sled, sleds left, turning).
            const int Each = 7, Head = RunHead;
            var f = new long[Head + run.FacilityCount * Each];
            f[0] = (long)run.Phase;
            f[1] = (long)run.End;
            f[2] = Q(run.Seconds, Fine);
            f[3] = run.Facility;
            f[4] = run.ChuteOpen ? 1 : 0;
            f[5] = Q(run.Scavenged, Fine);
            for (int i = 0; i < run.FacilityCount; i++)
            {
                var site = i < run.Sites.Count ? run.Sites[i] : null;
                f[Head + i * Each] = Q(run.ChuteLeft(i), Fine);
                f[Head + 1 + i * Each] = (site?.Stocked == true ? 1 : 0) | (site?.Turning == true ? 2 : 0) | (site?.OutOfRhythm == true ? 4 : 0);
                f[Head + 2 + i * Each] = Q(site?.Progress ?? 0, Fine);
                f[Head + 3 + i * Each] = site?.SledsLeft ?? 0;
                f[Head + 4 + i * Each] = Q(site?.Crank ?? 0, Ang);
                f[Head + 5 + i * Each] = (long)(site?.Power ?? Stops.PowerState.Live);
                f[Head + 6 + i * Each] = Q(site?.Restart ?? 0, Fine);
            }
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Run, 0), f));
        }
        // A facility's gantry crane (T48): where it is, the rig, and each casting.
        if (world.Run is { } withSites)
            foreach (var site in withSites.Sites)
                for (int k = 0; site is not null && k < site.Cranes.Count; k++)
                {
                    var crane = site.Cranes[k];
                    var f = new List<long> { Q(crane.Bridge, Pos), Q(crane.Trolley, Pos), Q(crane.Hook, Pos), Q(crane.Rigging, Fine), crane.Castings.Length };
                    foreach (var c in crane.Castings)
                        f.AddRange([(long)c.State, c.Car, Q(c.At.X, Pos), Q(c.At.Y, Pos), Q(c.At.Z, Pos)]);
                    list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Crane, site.Index * CranesPerSite + k), [.. f]));
                }
        // GDD App. D: each Holdout's state, who's in it and how far the breach is (the lamps and the HUD).
        if (world.Holdouts is { } holdouts)
            foreach (var h in holdouts.All)
                list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Holdout, h.Index), [(int)h.State, h.Occupant, Q(h.Progress, Fine)]));
        foreach (var body in world.Bodies.All)
        {
            var ps = body.Pbd.Particles;
            var f = new long[BodyParticles + ps.Length * 3];
            f[0] = (long)body.Kind;
            f[1] = body.Parent;
            f[2] = body.Carrier;
            f[3] = body.Owner;
            f[4] = body.Pbd.Asleep ? 1 : 0;
            f[5] = Q(body.Yaw, Ang);
            f[6] = ps.Length;
            f[7] = body.Second;
            for (int i = 0; i < ps.Length; i++)
            {
                f[BodyParticles + i * 3] = Q(ps[i].Position.X, Pos);
                f[BodyParticles + 1 + i * 3] = Q(ps[i].Position.Y, Pos);
                f[BodyParticles + 2 + i * 3] = Q(ps[i].Position.Z, Pos);
            }
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Body, body.Id), f));
        }
        foreach (var p in players)
        {
            var s = p.State;
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Player, p.Id),
            [
                s.Parent, Q(s.Position.X, Pos), Q(s.Position.Y, Pos), Q(s.Position.Z, Pos),
                Q(s.Velocity.X, Pos), Q(s.Velocity.Y, Pos), Q(s.Velocity.Z, Pos),
                Q(s.Yaw, Ang), Q(s.Pitch, Ang), (long)s.Surface, s.Health, (long)s.Death, Q(s.LineHint, Hint), Q(s.ActionProgress, Fine),
                Q(s.Cold, Fine), (long)s.Flags, s.Placed,
                // A VR player's hands, on the centimetre grid they came in on (T47): the rest of the crew see their arms.
                Q(s.Hand.X, Cm), Q(s.Hand.Y, Cm), Q(s.Hand.Z, Cm), Q(s.OtherHand.X, Cm), Q(s.OtherHand.Y, Cm), Q(s.OtherHand.Z, Cm),
                // The hotbar (T108): what they carry, and which is in hand.
                (long)s.Kit, s.HeldSlot,
            ]));
        }
        list.Sort((a, c) => a.Key.CompareTo(c.Key));
        return list;
    }

    /// <summary>Writes records back into live state: the client adopting a snapshot, or the host adopting its own quantised state.</summary>
    public static void Apply(IReadOnlyList<WireRecord> records, World world, ref TrainControls controls, List<PlayerSnapshot> players)
    {
        var train = world.Train;
        players.Clear();
        var rakes = new List<RakeState>();
        var vehicles = new List<VehicleState>();
        var boiler = train.Boiler;
        var enemies = new List<Enemy>();
        var bodies = new List<Physics.Body>();
        foreach (var r in records)
        {
            var f = r.Fields;
            switch (r.Kind)
            {
                case RecordKind.Rake:
                    int n = (int)f[0];
                    var ids = new int[n];
                    for (int i = 0; i < n; i++)
                        ids[i] = (int)f[1 + i];
                    rakes.Add(new RakeState(ids, D(f[n + 1], Pos), D(f[n + 2], Pos), D(f[n + 3], Fine), (f[n + 4] & 1) != 0, (f[n + 4] & 2) != 0,
                        f.Length > n + 5 ? (int)f[n + 5] : Rail.RailLine.MainPath));
                    break;
                case RecordKind.Switch:
                    train.MirrorSwitch(r.Id, f[0] != 0);
                    break;
                case RecordKind.Vehicle:
                    vehicles.Add(new VehicleState(r.Id, D(f[0], Fine), D(f[1], Fine), D(f[2], Fine),
                        new GunState
                        {
                            Ammo = (int)f[3],
                            Cooldown = (int)f[4],
                            Jammed = f[5] != 0,
                            LastShotTick = (uint)f[6],
                            ReloadNeeded = f.Length > 10 ? (int)f[10] : 0,
                            ReloadProgress = f.Length > 11 ? D(f[11], Fine) : 0,
                            Mounted = f.Length > 12 && f[12] != 0,
                            Z = f.Length > 13 ? D(f[13], Fine) : 0,
                            Facing = f.Length > 14 ? (sbyte)f[14] : (sbyte)0,
                        }, f.Length > 7 ? (byte)f[7] : (byte)0,
                        f.Length > 8 ? (CargoKind)f[8] : CargoKind.None, f.Length <= 9 || f[9] != 0, f.Length > 15 ? D(f[15], Fine) : 0));
                    break;
                case RecordKind.World:
                    world.Choir = new ChoirState
                    {
                        Loudness = D(f[0], Fine),
                        Build = D(f[1], Fine),
                        Floor = D(f[2], Fine),
                        Present = f.Length > 7 && (f[7] & 1) != 0,
                        Spent = f.Length > 7 && (f[7] & 2) != 0,
                        QuietSeconds = f.Length > 8 ? D(f[8], Fine) : 0,
                        Rest = f.Length > 10 ? D(f[10], Fine) : 0,
                    };
                    world.WhistleSeconds = f.Length > 9 ? D(f[9], Fine) : 0;
                    world.SetDerailed(f[3] != 0);
                    world.LampLit = f[4] != 0;
                    world.LampOutSeconds = f.Length > 5 ? D(f[5], Fine) : 0;
                    world.Train.Sand = f.Length > 6 ? D(f[6], Fine) : 0;
                    break;
                case RecordKind.Enemy:
                    if (!world.Authority)
                        enemies.Add(ToEnemy(r));
                    break;
                case RecordKind.Boiler:
                    boiler = new Boiler
                    {
                        Pressure = D(f[0], Fine),
                        Firebox = D(f[1], Fine),
                        Tender = D(f[2], Fine),
                        AtMaxSeconds = D(f[3], Fine),
                        LowFireSeconds = D(f[4], Fine),
                        ExternalHeat = D(f[5], Fine),
                        Efficiency = D(f[6], Fine),
                        Ruptured = (f[7] & 1) != 0,
                        SafetyValveLifting = (f[7] & 2) != 0,
                        SafetyValveJammed = (f[7] & 4) != 0,
                        FireDoorOpen = (f[7] & 8) != 0,
                        WrenchOut = (f[7] & 32) != 0,
                        Vented = (f[7] & 16) != 0,
                        SinceShovel = f.Length > 8 ? D(f[8], Fine) : 0,
                    };
                    break;
                case RecordKind.Controls:
                    controls = new TrainControls { Throttle = D(f[0], Fine), Brake = D(f[1], Fine), Reverser = (int)f[2] };
                    break;
                case RecordKind.Player:
                    players.Add(ToPlayer(r));
                    break;
                case RecordKind.Body when !world.Authority:
                    bodies.Add(ToBody(r));
                    break;
                case RecordKind.Crane when !world.Authority && world.Run is { } craneRun && r.Id / CranesPerSite < craneRun.Sites.Count
                    && craneRun.Sites[r.Id / CranesPerSite] is { } craneSite && r.Id % CranesPerSite < craneSite.Cranes.Count
                    && craneSite.Cranes[r.Id % CranesPerSite] is { } crane:
                    int castings = (int)f[4];
                    crane.Mirror(D(f[0], Pos), D(f[1], Pos), D(f[2], Pos), D(f[3], Fine), [.. Enumerable.Range(0, castings).Select(i =>
                        ((Run.CastingState)f[5 + i * 5], (int)f[6 + i * 5], new Ballast.Double3(D(f[7 + i * 5], Pos), D(f[8 + i * 5], Pos), D(f[9 + i * 5], Pos))))]);
                    break;
                case RecordKind.Holdout when !world.Authority && world.Holdouts is { } holdouts:
                    holdouts.Mirror(r.Id, (Run.HoldoutState)f[0], (int)f[1], D(f[2], Fine));
                    break;
                case RecordKind.Run when !world.Authority && world.Run is { } run:
                    const int Each = 7, Head = RunHead;
                    int facilities = (f.Length - Head) / Each;
                    run.Mirror((Run.RunPhase)f[0], (Run.RunEnd)f[1], D(f[2], Fine), (int)f[3], f[4] != 0,
                        [.. Enumerable.Range(0, facilities).Select(i => D(f[Head + i * Each], Fine))],
                        [.. Enumerable.Range(0, facilities).Select(i => new Run.SiteState((f[Head + 1 + i * Each] & 1) != 0, D(f[Head + 2 + i * Each], Fine), (int)f[Head + 3 + i * Each],
                            (f[Head + 1 + i * Each] & 2) != 0, (f[Head + 1 + i * Each] & 4) != 0, D(f[Head + 4 + i * Each], Ang),
                            (Stops.PowerState)f[Head + 5 + i * Each], D(f[Head + 6 + i * Each], Fine)))], D(f[5], Fine));
                    break;
            }
        }
        train.Restore(new TrainState([.. rakes], [.. vehicles], boiler));
        // The host owns its enemies' full state; only clients rebuild them from the wire.
        if (!world.Authority)
        {
            world.MirrorEnemies(enemies);
            world.Bodies.Mirror(bodies);
            // Seen a radio once, a client knows they're things tonight (T41): no radio on you, no radio.
            if (bodies.Any(b => b.Kind == Physics.BodyKind.Radio))
                world.Bodies.RadiosCarried = true;
        }
    }

    /// <summary>A client-side stand-in for a host body: positions only, it isn't simulated here.</summary>
    static Physics.Body ToBody(in WireRecord r)
    {
        var f = r.Fields;
        int n = (int)f[6];
        var particles = new Ballast.Physics.Particle[n];
        for (int i = 0; i < n; i++)
            particles[i] = new Ballast.Physics.Particle(new Ballast.Double3(D(f[BodyParticles + i * 3], Pos), D(f[BodyParticles + 1 + i * 3], Pos), D(f[BodyParticles + 2 + i * 3], Pos)), 1, 0.1);
        var pbd = new Ballast.Physics.PbdBody(particles);
        // At rest on the host, at rest here: a bot's hand picks up a crate left on a car's steps once it's lain still (T50).
        if (f[4] != 0)
            pbd.Sleep();
        return new Physics.Body(r.Id, (Physics.BodyKind)f[0], (int)f[1], pbd)
        {
            Carrier = (int)f[2],
            Second = (int)f[7],
            Owner = (int)f[3],
            Yaw = D(f[5], Ang),
        };
    }

    static Enemy ToEnemy(in WireRecord r)
    {
        var f = r.Fields;
        Enemy e = (EnemyKind)f[0] switch
        {
            EnemyKind.Sleepers => new Sleepers(r.Id),
            EnemyKind.CinderHound => new CinderHound(r.Id, (int)D(f[11], 1e3)),
            EnemyKind.Switchman => new Switchman(r.Id),
            EnemyKind.SootChildren => new SootChildren(r.Id),
            EnemyKind.Dragger => new Dragger(r.Id),
            EnemyKind.Stoker => new Stoker(r.Id),
            EnemyKind.CarFire => new CarFire(r.Id),
            EnemyKind.Climber => new Climber(r.Id),
            EnemyKind.Gaunt => new Gaunt(r.Id),
            EnemyKind.Passenger => new Passenger(r.Id),
            EnemyKind.Follower => new Follower(r.Id),
            EnemyKind.Drift => new Drift(r.Id),
            EnemyKind.TrackDoll => new TrackDoll(r.Id),
            EnemyKind.CarHugger => new CarHugger(r.Id),
            EnemyKind.Whistler => new Whistler(r.Id),
            EnemyKind.TippyToesie => new TippyToesie(r.Id),
            EnemyKind.FireFlies => new FireFlies(r.Id),
            EnemyKind.Ribbit => new Ribbit(r.Id, 0),
            EnemyKind.Grumbler => new Grumbler(r.Id),
            _ => new ChoirGhost(r.Id),
        };

        e.Restore((SpinePhase)f[1], D(f[2], 1e3), D(f[3], 1e3), (int)f[4], new Double3(D(f[5], Pos), D(f[6], Pos), D(f[7], Pos)),
            D(f[8], Pos), D(f[9], Pos), D(f[10], Pos), D(f[11], 1e3), D(f[12], 1e3),
            f.Length > 13 ? (int)f[13] : -1, f.Length > 14 ? D(f[14], 1e3) : 0);
        return e;
    }

    /// <summary>
    /// Writes <paramref name="current"/> as a delta against <paramref name="baseline"/> (null: against nothing).
    /// Unchanged records cost nothing; changed ones send a field mask and zigzag deltas of the changed fields.
    /// </summary>
    public static void WriteDelta(NetWriter w, IReadOnlyList<WireRecord> current, IReadOnlyList<WireRecord>? baseline)
    {
        var old = baseline?.ToDictionary(r => r.Key) ?? new Dictionary<uint, WireRecord>();
        var keys = new HashSet<uint>(current.Select(r => r.Key));
        var removed = old.Keys.Where(k => !keys.Contains(k)).ToList();
        w.VarU((ulong)removed.Count);
        foreach (var k in removed)
            w.VarU(k);

        var changed = current.Where(r => !old.TryGetValue(r.Key, out var b) || !r.SameAs(b)).ToList();
        w.VarU((ulong)changed.Count);
        foreach (var r in changed)
        {
            w.VarU(r.Key);
            bool delta = old.TryGetValue(r.Key, out var b) && b.Fields.Length == r.Fields.Length && r.Fields.Length <= 64;
            w.VarU(((ulong)r.Fields.Length << 1) | (delta ? 1ul : 0));
            if (!delta)
            {
                foreach (long v in r.Fields)
                    w.VarS(v);
                continue;
            }
            ulong mask = 0;
            for (int i = 0; i < r.Fields.Length; i++)
                if (r.Fields[i] != b.Fields[i])
                    mask |= 1ul << i;
            w.VarU(mask);
            for (int i = 0; i < r.Fields.Length; i++)
                if ((mask & (1ul << i)) != 0)
                    w.VarS(r.Fields[i] - b.Fields[i]);
        }
    }

    public static List<WireRecord> ReadDelta(ref NetReader r, IReadOnlyList<WireRecord>? baseline)
    {
        var result = baseline?.ToDictionary(x => x.Key) ?? new Dictionary<uint, WireRecord>();
        int removed = (int)r.VarU();
        for (int i = 0; i < removed; i++)
            result.Remove((uint)r.VarU());
        int changed = (int)r.VarU();
        for (int i = 0; i < changed; i++)
        {
            var key = (uint)r.VarU();
            ulong header = r.VarU();
            int count = (int)(header >> 1);
            if (count > 1024)
                throw new InvalidDataException("record too long");
            var fields = new long[count];
            if ((header & 1) == 0)
            {
                for (int k = 0; k < count; k++)
                    fields[k] = r.VarS();
            }
            else
            {
                if (!result.TryGetValue(key, out var b) || b.Fields.Length != count)
                    throw new InvalidDataException("delta against a record the baseline doesn't have");
                ulong mask = r.VarU();
                for (int k = 0; k < count; k++)
                    fields[k] = (mask & (1ul << k)) != 0 ? b.Fields[k] + r.VarS() : b.Fields[k];
            }
            result[key] = new WireRecord(key, fields);
        }
        var list = result.Values.ToList();
        list.Sort((a, c) => a.Key.CompareTo(c.Key));
        return list;
    }

    /// <summary>Decodes only the player records (for interpolation buffers).</summary>
    public static void ApplyPlayers(IReadOnlyList<WireRecord> records, List<PlayerSnapshot> players)
    {
        players.Clear();
        foreach (var r in records)
            if (r.Kind == RecordKind.Player)
                players.Add(ToPlayer(r));
    }

    static PlayerSnapshot ToPlayer(in WireRecord r)
    {
        var f = r.Fields;
        return new PlayerSnapshot((byte)r.Id, new PlayerState
        {
            Parent = (int)f[0],
            Position = new Double3(D(f[1], Pos), D(f[2], Pos), D(f[3], Pos)),
            Velocity = new Double3(D(f[4], Pos), D(f[5], Pos), D(f[6], Pos)),
            Yaw = D(f[7], Ang),
            Pitch = D(f[8], Ang),
            Surface = (Surface)f[9],
            Health = (int)f[10],
            Death = (DeathCause)f[11],
            LineHint = D(f[12], Hint),
            ActionProgress = D(f[13], Fine),
            Cold = D(f[14], Fine),
            Flags = (PlayerFlags)f[15],
            Placed = (byte)f[16],
            Hand = f.Length > 22 ? new Double3(D(f[17], Cm), D(f[18], Cm), D(f[19], Cm)) : default,
            OtherHand = f.Length > 22 ? new Double3(D(f[20], Cm), D(f[21], Cm), D(f[22], Cm)) : default,
            Kit = f.Length > 24 ? (ulong)f[23] : 0,
            HeldSlot = f.Length > 24 ? (byte)f[24] : (byte)0,
        });
    }

    /// <summary>Snaps live host state onto the replication grid. Call at the end of every host tick.</summary>
    public static List<WireRecord> Quantise(World world, ref TrainControls controls, List<PlayerSnapshot> players)
    {
        var records = Capture(world, controls, players);
        Apply(records, world, ref controls, players);
        return records;
    }
}
