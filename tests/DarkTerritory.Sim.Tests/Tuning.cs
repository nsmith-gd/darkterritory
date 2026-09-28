using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

static class Tuning
{
    static readonly string Content = DataFile.FindContentRoot();
    public static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    public static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
}
