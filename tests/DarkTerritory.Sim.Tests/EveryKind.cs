using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The game's enemy tuning with every kind about tonight, for a test of one creature's workings or of the director's own
/// rules: the director's roster (<c>director.nightChance</c>, note 652) leaves each kind out on some nights, so on a test's
/// seed the creature it's about may not be there at all.
/// </summary>
static class EveryKind
{
    public static EnemyTuning Of(EnemyTuning t) => t with { Director = t.Director with { NightChance = [] } };

    public static EnemyTuning Enemies => Of(Tuning.Enemies);
}
