namespace DarkTerritory.Sim.Train;

public enum SpeedBand { Stopped, Yard, Slow, Working, Cruise, Max }

/// <summary>The spec's speed bands (B.3) and the single boarding/jumping rule built on them.</summary>
public static class SpeedBands
{
    /// <summary>"If you could catch it, you can leave it." One threshold for boarding and jumping.</summary>
    public static bool JumpOffIsLethal(TrainTuning t, double speed) => Math.Abs(speed) > t.SpeedBands.JumpOffLethal;

    /// <summary>A player on foot can run the train down and board it.</summary>
    public static bool CanBeCaughtOnFoot(TrainTuning t, double speed) => Math.Abs(speed) < t.PlayerRunSpeed;

    public static SpeedBand Classify(TrainTuning t, double speed)
    {
        var b = t.SpeedBands;
        speed = Math.Abs(speed);
        if (speed < 0.05) return SpeedBand.Stopped;
        if (speed <= b.Yard) return SpeedBand.Yard;
        if (speed < b.WorkingMin) return SpeedBand.Slow;
        if (speed < b.Cruise) return SpeedBand.Working;
        if (speed < t.MaxSpeed - 0.05) return SpeedBand.Cruise;
        return SpeedBand.Max;
    }
}
