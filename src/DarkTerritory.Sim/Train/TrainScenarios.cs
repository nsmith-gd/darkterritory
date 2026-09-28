namespace DarkTerritory.Sim.Train;

public sealed record StopResult(int Cars, double FromSpeed, double Seconds, double Metres);
public sealed record ClimbResult(int Cars, double GradePercent, bool Holds, double MaxGradePercent, double SpeedAfter60s);

/// <summary>
/// Canned runs of the real train sim at the real tick rate. Used by tests and by `dt train`
/// so a designer or agent can see what the tuning numbers actually produce.
/// </summary>
public static class TrainScenarios
{
    public static StopResult StopFrom(TrainTuning t, int cars, double fromSpeed, double load = 1, double gradePercent = 0)
    {
        var train = new TrainDynamics(Consist.Uniform(t, cars, load)) { Velocity = fromSpeed };
        var controls = new TrainControls { Brake = 1, Reverser = 1 };
        var track = new TrackConditions { GradePercent = gradePercent, Traction = 1 };
        int ticks = 0;
        const int limit = SimConstants.TickRate * 60 * 30;
        while (train.Speed > 0 && ticks < limit)
        {
            train.Step(SimConstants.TickSeconds, controls, track);
            ticks++;
        }
        return new StopResult(cars, fromSpeed, ticks * SimConstants.TickSeconds, train.Distance);
    }

    public static ClimbResult Climb(TrainTuning t, int cars, double gradePercent, double startSpeed = 10, double load = 1)
    {
        var train = new TrainDynamics(Consist.Uniform(t, cars, load)) { Velocity = startSpeed };
        var controls = new TrainControls { Throttle = 1, Reverser = 1 };
        var track = new TrackConditions { GradePercent = gradePercent, Traction = 1 };
        for (int i = 0; i < SimConstants.TickRate * 60; i++)
            train.Step(SimConstants.TickSeconds, controls, track);
        return new ClimbResult(cars, gradePercent, train.Velocity >= startSpeed - 1e-6, train.MaxClimbableGradePercent(), train.Velocity);
    }
}
