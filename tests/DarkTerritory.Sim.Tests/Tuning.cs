using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

static class Tuning
{
    static readonly string Content = DataFile.FindContentRoot();
    public static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    public static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    public static readonly DarkTerritory.Sim.Route.RouteTuning Route = DataFile.Load<DarkTerritory.Sim.Route.RouteTuning>(Path.Combine(Content, DarkTerritory.Sim.Route.RouteTuning.File));
    public static readonly DarkTerritory.Sim.Combat.CombatTuning Combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(Content, DarkTerritory.Sim.Combat.CombatTuning.File));
    public static readonly DarkTerritory.Sim.Enemies.EnemyTuning Enemies = DataFile.Load<DarkTerritory.Sim.Enemies.EnemyTuning>(Path.Combine(Content, DarkTerritory.Sim.Enemies.EnemyTuning.File));
    public static readonly BoilerTuning Boiler = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    public static readonly DarkTerritory.Sim.Run.RunTuning Run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(Content, DarkTerritory.Sim.Run.RunTuning.File));
}
