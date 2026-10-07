using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A hit on a player from something in the world. The host applies these after the world steps.</summary>
/// <param name="Pull">A pull off the train (the Draggers, T46): this velocity outward in the world, on top of the train's.</param>
/// <param name="Lethal">
/// Only a punish after a grab may kill (App. A.1, "kills go through GRAB"), and the train's own dangers (fire, a fall). Any
/// other hurt leaves a player on their last point of health.
/// </param>
public readonly record struct DamageEvent(int PlayerId, int Amount, DeathCause Cause, Double3? Pull = null, bool Lethal = false);

/// <summary>What an enemy can see and do this tick.</summary>
public sealed class EnemyContext
{
    public required EnemyTuning Tuning { get; init; }
    public required World World { get; init; }
    public TrainOnLine Train => World.Train;
    public uint Tick => World.Tick;
    public List<EnemyEvent> Events { get; } = new();
    public List<DamageEvent> Damage { get; } = new();
    /// <summary>The crew as of the start of this tick, and what each is doing with their hands.</summary>
    public List<(PlayerSnapshot Player, PlayerIntent Intent)> Crew { get; } = new();

    public IEnumerable<(PlayerSnapshot Player, Double3 World)> LivingCrew() =>
        Crew.Where(c => c.Player.State.Alive).Select(c => (c.Player, PlayerMotor.WorldPosition(c.Player.State, Train)));

    /// <summary>A hurt that can't kill (it leaves a player on 1): the fairness contract's "the consequence starts now".</summary>
    public void Bite(int playerId, int amount, DeathCause cause) => Damage.Add(new DamageEvent(playerId, amount, cause));

    /// <summary>A hurt from the train itself (fire, burns at the firebox, a crush): these can kill, as falls do.</summary>
    public void Harm(int playerId, int amount, DeathCause cause) => Damage.Add(new DamageEvent(playerId, amount, cause, Lethal: true));

    /// <summary>Death, from an enemy's punish (<see cref="Enemy.Kill"/> is the only caller).</summary>
    internal void Kill(int playerId, DeathCause cause) => Damage.Add(new DamageEvent(playerId, int.MaxValue / 2, cause, Lethal: true));

    /// <summary>Players held this tick (App. A.1 GRAB): they can't move, only look, talk and struggle.</summary>
    public HashSet<int> Held { get; } = new();

    public void Hold(int playerId) => Held.Add(playerId);

    /// <summary>Held players being carried off, and to where in the world (the Whistler at a run, the Passenger to the caboose).</summary>
    public List<(int Player, Double3 To)> Carries { get; } = new();

    public void Carry(int playerId, Double3 to) => Carries.Add((playerId, to));

    /// <summary>Pulls a player off the train over the side (App. A.4, the Draggers): at speed, that's death.</summary>
    public void Pull(int playerId, Double3 outward) => Damage.Add(new DamageEvent(playerId, 0, DeathCause.Dragged, outward));

    /// <summary>Rounds fired lately (tick, muzzle), newest last, for enemies that react to sustained fire.</summary>
    public IReadOnlyList<(uint Tick, Double3 Muzzle)> RecentRounds { get; init; } = [];

    /// <summary>Where this tick's cannonballs came down (world): the hound run's runners scatter from one landing near (note 328).</summary>
    public List<Double3> Landed { get; } = new();

    /// <summary>Rounds fired within <paramref name="seconds"/> from a gun within <paramref name="range"/> of a point.</summary>
    public int RoundsNear(Double3 at, double range, double seconds)
    {
        uint since = Tick - (uint)Math.Min(Tick, Math.Round(seconds * SimConstants.TickRate));
        int n = 0;
        foreach (var (tick, muzzle) in RecentRounds)
            if (tick >= since && (muzzle - at).Length <= range)
                n++;
        return n;
    }
}
