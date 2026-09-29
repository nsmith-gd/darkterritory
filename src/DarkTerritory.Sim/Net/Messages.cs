using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

public enum MessageType : byte
{
    /// <summary>Client → host, unreliable: the latest few inputs, redundantly, to ride out loss.</summary>
    Input = 1,
    /// <summary>Host → client, unreliable: authoritative world state for one tick.</summary>
    Snapshot = 2,
    /// <summary>Host → client, reliable: which player you are.</summary>
    Welcome = 3,
}

public readonly record struct InputFrame(uint Sequence, PlayerIntent Intent);

/// <summary>Everything a client needs to rebuild the train: it re-simulates it forward from here.</summary>
public struct TrainSnapshot
{
    public TrainState State;
    public TrainControls Controls;

    public static TrainSnapshot Capture(TrainOnLine train, in TrainControls controls) => new() { State = train.Capture(), Controls = controls };
}

public readonly record struct PlayerSnapshot(byte Id, PlayerState State);

/// <summary>
/// Wire format. Hand-written and flat for now; delta compression against the last acked
/// snapshot and interest management come next (ARCHITECTURE §6.2).
/// </summary>
public static class Messages
{
    /// <summary>How many past inputs each input packet repeats.</summary>
    public const int InputRedundancy = 4;

    public static void WriteInput(NetWriter w, ReadOnlySpan<InputFrame> frames, uint lastSnapshotTick)
    {
        w.Reset();
        w.U8((byte)MessageType.Input);
        w.U32(lastSnapshotTick);
        w.U8((byte)frames.Length);
        foreach (var f in frames)
        {
            w.U32(f.Sequence);
            WriteIntent(w, f.Intent);
        }
    }

    public static void ReadInput(ref NetReader r, List<InputFrame> into, out uint lastSnapshotTick)
    {
        lastSnapshotTick = r.U32();
        int n = r.U8();
        for (int i = 0; i < n; i++)
            into.Add(new InputFrame(r.U32(), ReadIntent(ref r)));
    }

    static void WriteIntent(NetWriter w, in PlayerIntent i)
    {
        w.F32(i.MoveX);
        w.F32(i.MoveZ);
        w.F32(i.LookYaw);
        w.F32(i.LookPitch);
        w.U8((byte)i.Buttons);
        w.I8(i.ThrottleNotch);
    }

    static PlayerIntent ReadIntent(ref NetReader r) => new()
    {
        MoveX = r.F32(),
        MoveZ = r.F32(),
        LookYaw = r.F32(),
        LookPitch = r.F32(),
        Buttons = (PlayerButtons)r.U8(),
        ThrottleNotch = r.I8(),
    };

    public static void WriteSnapshot(NetWriter w, uint tick, uint ackedInput, in TrainSnapshot train, ReadOnlySpan<PlayerSnapshot> players)
    {
        w.Reset();
        w.U8((byte)MessageType.Snapshot);
        w.U32(tick);
        w.U32(ackedInput);
        WriteTrain(w, train.State);
        w.F32((float)train.Controls.Throttle);
        w.F32((float)train.Controls.Brake);
        w.I8((sbyte)train.Controls.Reverser);
        w.U8((byte)players.Length);
        foreach (var p in players)
        {
            var s = p.State;
            w.U8(p.Id);
            w.I8((sbyte)s.Parent);
            w.Double3(s.Position);
            w.Double3(s.Velocity);
            w.F64(s.Yaw);
            w.F64(s.Pitch);
            w.U8((byte)s.Surface);
            w.U8((byte)Math.Clamp(s.Health, 0, 255));
            w.U8((byte)s.Death);
            w.F64(s.LineHint);
            w.F64(s.ActionProgress);
        }
    }

    public static void ReadSnapshot(ref NetReader r, out uint tick, out uint ackedInput, out TrainSnapshot train, List<PlayerSnapshot> players)
    {
        tick = r.U32();
        ackedInput = r.U32();
        train = new TrainSnapshot
        {
            State = ReadTrain(ref r),
            Controls = new TrainControls { Throttle = r.F32(), Brake = r.F32(), Reverser = r.I8() },
        };
        int n = r.U8();
        for (int i = 0; i < n; i++)
        {
            byte id = r.U8();
            var s = new PlayerState
            {
                Parent = r.I8(),
                Position = r.Double3(),
                Velocity = r.Double3(),
                Yaw = r.F64(),
                Pitch = r.F64(),
                Surface = (Surface)r.U8(),
                Health = r.U8(),
                Death = (DeathCause)r.U8(),
                LineHint = r.F64(),
                ActionProgress = r.F64(),
            };
            players.Add(new PlayerSnapshot(id, s));
        }
    }

    static void WriteTrain(NetWriter w, TrainState s)
    {
        w.U8((byte)s.Rakes.Length);
        foreach (var rake in s.Rakes)
        {
            w.U8((byte)rake.Vehicles.Length);
            foreach (int id in rake.Vehicles)
                w.U8((byte)id);
            w.F64(rake.Distance);
            w.F64(rake.Velocity);
            w.F64(rake.BrakeEfficiency);
            w.U8((byte)((rake.Handbrake ? 1 : 0) | (rake.FrontCouplerLocked ? 2 : 0)));
        }
        w.U8((byte)s.Vehicles.Length);
        foreach (var v in s.Vehicles)
        {
            w.U8((byte)v.Id);
            w.F64(v.Load);
            w.F64(v.Integrity);
            w.F64(v.CargoIntegrity);
        }
        WriteBoiler(w, s.Boiler);
    }

    static TrainState ReadTrain(ref NetReader r)
    {
        var rakes = new RakeState[r.U8()];
        for (int i = 0; i < rakes.Length; i++)
        {
            var ids = new int[r.U8()];
            for (int k = 0; k < ids.Length; k++)
                ids[k] = r.U8();
            double distance = r.F64(), velocity = r.F64(), brake = r.F64();
            byte flags = r.U8();
            rakes[i] = new RakeState(ids, distance, velocity, brake, (flags & 1) != 0, (flags & 2) != 0);
        }
        var vehicles = new VehicleState[r.U8()];
        for (int i = 0; i < vehicles.Length; i++)
            vehicles[i] = new VehicleState(r.U8(), r.F64(), r.F64(), r.F64());
        return new TrainState(rakes, vehicles, ReadBoiler(ref r));
    }

    static void WriteBoiler(NetWriter w, in Boiler b)
    {
        w.F64(b.Pressure);
        w.F64(b.Firebox);
        w.F64(b.Tender);
        w.F64(b.AtMaxSeconds);
        w.F64(b.LowFireSeconds);
        w.F64(b.ExternalHeat);
        w.F64(b.Efficiency);
        w.Bool(b.Ruptured);
        w.Bool(b.SafetyValveLifting);
        w.Bool(b.SafetyValveJammed);
    }

    static Boiler ReadBoiler(ref NetReader r) => new()
    {
        Pressure = r.F64(),
        Firebox = r.F64(),
        Tender = r.F64(),
        AtMaxSeconds = r.F64(),
        LowFireSeconds = r.F64(),
        ExternalHeat = r.F64(),
        Efficiency = r.F64(),
        Ruptured = r.Bool(),
        SafetyValveLifting = r.Bool(),
        SafetyValveJammed = r.Bool(),
    };

    public static void WriteWelcome(NetWriter w, byte playerId, uint tick)
    {
        w.Reset();
        w.U8((byte)MessageType.Welcome);
        w.U8(playerId);
        w.U32(tick);
    }
}
