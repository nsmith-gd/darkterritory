using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Combat;

/// <summary>What landed a blow: a tool swung (App. C.2) or a cannon's ball (App. C.3).</summary>
public enum HitSource : byte { Melee = 1, Cannon = 2 }

/// <summary>
/// A blow or a ball that landed on a creature (T121 playtest: "all creatures need hit confirm feedback"). Host-authored as
/// the strike is resolved and replicated as a record while it's recent, so every client draws the flinch and plays the
/// thud at <see cref="At"/>, and the one who landed it (<see cref="By"/>) gets the crosshair's marker. Presentation only:
/// nothing in the sim reads it back.
/// </summary>
/// <param name="Id">Unique for the night (the record's key).</param>
/// <param name="Tick">The host tick it landed on.</param>
/// <param name="At">Where it landed (world).</param>
/// <param name="From">Which way it was going (world, unit): the flinch goes with it.</param>
/// <param name="Killed">That blow finished it (or sent it off for good).</param>
public readonly record struct HitConfirm(int Id, uint Tick, int EnemyId, EnemyKind Kind, int By, HitSource Source, Double3 At, Double3 From, bool Killed);

/// <summary>What a cannonball came down on (T121 playtest: "every cannonball should have an impact explosion").</summary>
public enum ImpactSurface : byte { Ground = 1, Water = 2, Structure = 3, Train = 4, Creature = 5 }

/// <summary>
/// Where a cannon's ball landed: the creature it struck, the train's own body in its way, a building's wall, water, or the
/// ground (a ball that hits nothing in range comes down where its range runs out). Host-authored, replicated while recent,
/// for the explosion, the boom and the flash's light on every client.
/// </summary>
/// <param name="Direction">The way the ball was going (world, unit).</param>
/// <param name="Struck">What it struck, when <see cref="Surface"/> is <see cref="ImpactSurface.Creature"/> (porcelain for the doll).</param>
public readonly record struct CannonImpact(int Id, uint Tick, Double3 At, Double3 Direction, ImpactSurface Surface, int Shooter, EnemyKind Struck = 0);

/// <summary>How long hits and impacts stay in the replicated world (combat.json <c>hits</c>). Field docs live there.</summary>
public sealed record HitTuning
{
    public double KeepSeconds { get; init; } = 1;
    public double ImpactKeepSeconds { get; init; } = 8;
}
