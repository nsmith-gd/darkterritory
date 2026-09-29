using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/vigil.json. Field docs live in that file.</summary>
public sealed record VigilTuning(double[] Seconds, double StillBelowSpeed, double BeginHoldSeconds, double NoiseSpawnMultiplier)
{
    public const string File = "tuning/vigil.json";
}

public enum VigilOutcome : byte { Began, Revived, Broken }

/// <summary>What the Vigil did this tick, for the host to act on (a revival) and for the harness to count.</summary>
public readonly record struct VigilEvent(VigilOutcome Outcome, int PlayerId);

/// <summary>
/// Spec C.2, the Vigil: revival that's possible, expensive, and capable of ending a run. A dead crewmate's body laid
/// aboard the engine, the train stopped dead, and someone holding the vent in the cab: the boiler dumps to zero and a
/// 90/120/150 s clock starts (there's no fourth). While it runs the engine is off, the lights are emergency only,
/// the guns are dead, the Choir is at its maximum and the director comes twice as often (applied by
/// <see cref="World"/>, identically on every machine). Moving the body out of the engine breaks it; the pressure's
/// gone either way. Host-authoritative; clients mirror it for the effects and the HUD.
/// </summary>
public sealed class Vigil(VigilTuning tuning)
{
    readonly Dictionary<int, double> _hold = new();
    // Where each revived player came back, so "until the next POI" means a different stop from this one.
    readonly Dictionary<int, int> _revivedAt = new();

    public VigilTuning Tuning { get; set; } = tuning;
    public bool Active { get; private set; }
    /// <summary>Seconds left of the Vigil under way.</summary>
    public double Left { get; private set; }
    /// <summary>Revivals completed this run.</summary>
    public int Revivals { get; private set; }
    /// <summary>The body being revived, and whose it is (−1 when there's no Vigil).</summary>
    public int Body { get; private set; } = -1;
    public int For { get; private set; } = -1;

    public bool Permitted => Revivals < Tuning.Seconds.Length;
    /// <summary>How long the next Vigil would take.</summary>
    public double NextSeconds => Tuning.Seconds[Math.Min(Revivals, Tuning.Seconds.Length - 1)];

    /// <summary>The body that could be revived now: a dead crewmate's, laid (not carried) aboard the engine.</summary>
    public static Body? Candidate(World world, Func<int, PlayerState?> crew) =>
        world.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Ragdoll && b.Parent == 0 && b.Carrier < 0 && crew(b.Owner) is { Alive: false });

    public bool Still(TrainOnLine train) => Math.Abs(train.Dynamics.Speed) < Tuning.StillBelowSpeed;

    /// <summary>A player's hands this tick (host, from <see cref="World.CrewAct"/>): are they holding the vent?</summary>
    public void CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train)
    {
        bool holding = s.Alive && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5 && PlayerMotor.InCab(s, train)
            && CrewActions.Nearest(s, train) == InteractableKind.Vent;
        _hold[playerId] = holding ? _hold.GetValueOrDefault(playerId) + SimConstants.TickSeconds : 0;
    }

    /// <summary>Host, after the world and bodies have stepped. Returns what happened, if anything.</summary>
    public VigilEvent? Step(World world, Func<int, PlayerState?> crew, double dt)
    {
        if (!Active)
        {
            if (!Permitted || !_hold.Values.Any(h => h >= Tuning.BeginHoldSeconds) || !Still(world.Train) || Candidate(world, crew) is not { } body)
                return null;
            Active = true;
            Left = NextSeconds;
            Body = body.Id;
            For = body.Owner;
            return new VigilEvent(VigilOutcome.Began, For);
        }
        var laid = world.Bodies.All.FirstOrDefault(b => b.Id == Body);
        if (laid is null || laid.Parent != 0 || laid.Carrier >= 0 || crew(For) is not { Alive: false })
        {
            int who = For;
            Stop();
            return new VigilEvent(VigilOutcome.Broken, who);
        }
        Left -= dt;
        if (Left > 0)
            return null;
        int revived = For;
        Revivals++;
        world.Bodies.Remove(laid);
        _revivedAt[revived] = world.Run?.Facility ?? -1;
        Stop();
        return new VigilEvent(VigilOutcome.Revived, revived);
    }

    void Stop()
    {
        Active = false;
        Left = 0;
        Body = For = -1;
    }

    /// <summary>
    /// "They cannot operate the guns until the next POI": true once the run has reached a stop other than the one
    /// they came back at (or the terminus).
    /// </summary>
    public bool RecoveredAt(int playerId, Run? run)
    {
        if (run is null || !_revivedAt.TryGetValue(playerId, out int at))
            return false;
        bool recovered = run.Phase == RunPhase.Arrived || run.Phase == RunPhase.AtFacility && run.Facility != at;
        if (recovered)
            _revivedAt.Remove(playerId);
        return recovered;
    }

    /// <summary>Client side: adopts the host's Vigil.</summary>
    public void Mirror(bool active, double left, int revivals, int body, int forPlayer)
    {
        Active = active;
        Left = left;
        Revivals = revivals;
        Body = body;
        For = forPlayer;
    }
}
