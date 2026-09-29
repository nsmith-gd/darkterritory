using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public enum RecordKind : byte { Rake = 1, Vehicle = 2, Boiler = 3, Controls = 4, Player = 5, World = 6, Enemy = 7, Run = 8 }

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
    const double Pos = 1e4, Ang = 1e5, Fine = 1e6, Hint = 1e2;

    static long Q(double v, double scale) => (long)Math.Round(v * scale);
    static double D(long q, double scale) => q / scale;

    public static List<WireRecord> Capture(World world, in TrainControls controls, IEnumerable<PlayerSnapshot> players)
    {
        var train = world.Train;
        var list = new List<WireRecord>();
        foreach (var rake in train.Rakes)
        {
            var ids = rake.Consist.Vehicles;
            var f = new long[ids.Count + 5];
            f[0] = ids.Count;
            for (int i = 0; i < ids.Count; i++)
                f[1 + i] = ids[i].Id;
            f[ids.Count + 1] = Q(rake.Distance, Pos);
            f[ids.Count + 2] = Q(rake.Velocity, Pos);
            f[ids.Count + 3] = Q(rake.BrakeEfficiency, Fine);
            f[ids.Count + 4] = (rake.Handbrake ? 1 : 0) | (rake.FrontCouplerLocked ? 2 : 0);
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Rake, ids[0].Id), f));
        }
        foreach (var v in train.Vehicles)
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Vehicle, v.Id),
                [Q(v.Load, Fine), Q(v.Integrity, Fine), Q(v.CargoIntegrity, Fine), v.Gun.Ammo, v.Gun.Cooldown, v.Gun.Jammed ? 1 : 0, v.Gun.LastShotTick, v.DoorsOpen]));
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.World, 0),
            [Q(world.Choir.Aggro, Fine), Q(world.Choir.SecondsSinceShot, Fine), Q(world.Choir.Floor, Fine), world.Derailed ? 1 : 0, world.LampLit ? 1 : 0]));
        foreach (var e in world.ActiveEnemies)
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Enemy, e.Id),
            [
                (long)e.Kind, (long)e.Phase, Q(e.PhaseSeconds, 1e3), Q(e.Health, 1e3), e.Attached,
                Q(e.Local.X, Pos), Q(e.Local.Y, Pos), Q(e.Local.Z, Pos), Q(e.LineDistance, Pos), Q(e.Lateral, Pos), Q(e.Height, Pos),
                Q(e.Extra, 1e3), Q(e.Extra2, 1e3),
            ]));
        var b = train.Boiler;
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Boiler, 0),
        [
            Q(b.Pressure, Fine), Q(b.Firebox, Fine), Q(b.Tender, Fine), Q(b.AtMaxSeconds, Fine), Q(b.LowFireSeconds, Fine),
            Q(b.ExternalHeat, Fine), Q(b.Efficiency, Fine),
            (b.Ruptured ? 1 : 0) | (b.SafetyValveLifting ? 2 : 0) | (b.SafetyValveJammed ? 4 : 0),
        ]));
        list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Controls, 0), [Q(controls.Throttle, Fine), Q(controls.Brake, Fine), controls.Reverser]));
        if (world.Run is { } run)
        {
            var f = new long[5 + run.FacilityCount];
            f[0] = (long)run.Phase;
            f[1] = (long)run.End;
            f[2] = Q(run.Seconds, Fine);
            f[3] = run.Facility;
            f[4] = run.ChuteOpen ? 1 : 0;
            for (int i = 0; i < run.FacilityCount; i++)
                f[5 + i] = Q(run.ChuteLeft(i), Fine);
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Run, 0), f));
        }
        foreach (var p in players)
        {
            var s = p.State;
            list.Add(new WireRecord(WireRecord.MakeKey(RecordKind.Player, p.Id),
            [
                s.Parent, Q(s.Position.X, Pos), Q(s.Position.Y, Pos), Q(s.Position.Z, Pos),
                Q(s.Velocity.X, Pos), Q(s.Velocity.Y, Pos), Q(s.Velocity.Z, Pos),
                Q(s.Yaw, Ang), Q(s.Pitch, Ang), (long)s.Surface, s.Health, (long)s.Death, Q(s.LineHint, Hint), Q(s.ActionProgress, Fine),
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
                    rakes.Add(new RakeState(ids, D(f[n + 1], Pos), D(f[n + 2], Pos), D(f[n + 3], Fine), (f[n + 4] & 1) != 0, (f[n + 4] & 2) != 0));
                    break;
                case RecordKind.Vehicle:
                    vehicles.Add(new VehicleState(r.Id, D(f[0], Fine), D(f[1], Fine), D(f[2], Fine),
                        new GunState { Ammo = (int)f[3], Cooldown = (int)f[4], Jammed = f[5] != 0, LastShotTick = (uint)f[6] }, f.Length > 7 ? (byte)f[7] : (byte)0));
                    break;
                case RecordKind.World:
                    world.Choir = new ChoirState { Aggro = D(f[0], Fine), SecondsSinceShot = D(f[1], Fine), Floor = D(f[2], Fine) };
                    world.SetDerailed(f[3] != 0);
                    world.LampLit = f[4] != 0;
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
                    };
                    break;
                case RecordKind.Controls:
                    controls = new TrainControls { Throttle = D(f[0], Fine), Brake = D(f[1], Fine), Reverser = (int)f[2] };
                    break;
                case RecordKind.Player:
                    players.Add(ToPlayer(r));
                    break;
                case RecordKind.Run when !world.Authority && world.Run is { } run:
                    run.Mirror((Run.RunPhase)f[0], (Run.RunEnd)f[1], D(f[2], Fine), (int)f[3], f[4] != 0,
                        [.. Enumerable.Range(0, f.Length - 5).Select(i => D(f[5 + i], Fine))]);
                    break;
            }
        }
        train.Restore(new TrainState([.. rakes], [.. vehicles], boiler));
        // The host owns its enemies' full state; only clients rebuild them from the wire.
        if (!world.Authority)
            world.MirrorEnemies(enemies);
    }

    static Enemy ToEnemy(in WireRecord r)
    {
        var f = r.Fields;
        Enemy e = (EnemyKind)f[0] switch
        {
            EnemyKind.Sleepers => new Sleepers(r.Id),
            EnemyKind.CinderHound => new CinderHound(r.Id, (int)D(f[11], 1e3)),
            EnemyKind.Clinger => new Clinger(r.Id),
            _ => new Hollow(r.Id),
        };
        e.Restore((SpinePhase)f[1], D(f[2], 1e3), D(f[3], 1e3), (int)f[4], new Double3(D(f[5], Pos), D(f[6], Pos), D(f[7], Pos)),
            D(f[8], Pos), D(f[9], Pos), D(f[10], Pos), D(f[11], 1e3), D(f[12], 1e3));
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
