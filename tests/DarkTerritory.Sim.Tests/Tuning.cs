using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

static class Tuning
{
    static readonly string Content = DataFile.FindContentRoot();
    public static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    public static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    public static readonly DarkTerritory.Sim.Route.RouteTuning Route = DarkTerritory.Sim.Route.RouteTuning.Load(Content);
    public static readonly DarkTerritory.Sim.Combat.CombatTuning Combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(Content, DarkTerritory.Sim.Combat.CombatTuning.File));
    public static readonly DarkTerritory.Sim.Enemies.EnemyTuning Enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(Content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
    /// <summary>
    /// The director's pressure for tests of what it sends rather than when (note 266): nothing to wait for, never pressed (so the
    /// cooldown and the budget's curve hold).
    /// </summary>
    public static readonly DarkTerritory.Sim.Enemies.PressureTuning Eager = Enemies.Director.Pressure with { Threshold = 0, PressAt = 1e9 };
    /// <summary>Note 287: the first threat not held for a draw, for the director's weights tests that spend as soon as they can.</summary>
    public static readonly DarkTerritory.Sim.Enemies.DrawTuning Unheld = Enemies.Director.Draw with { HoldFirst = false };
    public static readonly BoilerTuning Boiler = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    public static readonly DarkTerritory.Sim.Run.RunTuning Run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(Content, DarkTerritory.Sim.Run.RunTuning.File));
    public static readonly DarkTerritory.Sim.Run.HoldoutTuning Holdouts = DataFile.Load<DarkTerritory.Sim.Run.HoldoutTuning>(Path.Combine(Content, DarkTerritory.Sim.Run.HoldoutTuning.File));
}
