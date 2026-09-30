using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Train;

public sealed record BoilerRun(int Cars, double Seconds, double MinPressure, double MaxPressure, double EndPressure,
    double CoalBurned, int Shovels, double SecondsPerShovel, double FiremanDuty, double MeanSpeed, bool Ruptured);

/// <summary>
/// Canned boiler runs with a scripted fireman (a player in the cab holding Use at the firebox),
/// at the real tick rate, through the real crew-action path. Used by tests and `dt boiler`.
/// </summary>
public static class BoilerScenarios
{
    /// <summary>Seconds per unit of coal that exactly hold pressure at full throttle, from the model.</summary>
    public static double HoldingSecondsPerUnit(BoilerTuning b, int cars) =>
        b.SteamPerUnit / (b.AuxiliaryDrain + b.HeatingPerCar * cars + b.FullThrottleDemand);

    public static double TenderEnduranceMinutes(BoilerTuning b, int cars) => b.TenderCapacity * HoldingSecondsPerUnit(b, cars) / 60;

    /// <summary>
    /// Runs the train on flat track. The fireman shovels whenever pressure is below <paramref name="fireAt"/>
    /// (null = nobody on the shovel).
    /// </summary>
    public static BoilerRun Run(TrainTuning t, BoilerTuning b, PlayerTuning p, int cars, double seconds, double throttle,
        double? fireAt = 88, double startPressure = 75, double startFirebox = 4, double startSpeed = 0, bool vent = false)
    {
        var line = new RailLine(new LineDefinition("flat", [new TrackSegment(200_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 1)), line, 1_000, b);
        train.Boiler.Pressure = startPressure;
        train.Boiler.Firebox = startFirebox;
        train.Dynamics.Velocity = startSpeed;
        var fireman = PlayerMotor.SpawnInCab(train, p);
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        var valve = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Vent).Position;
        fireman.Position = (vent ? valve : firebox) with { Y = fireman.Position.Y, Z = (vent ? valve : firebox).Z + 0.4 };

        var controls = new TrainControls { Throttle = throttle, Reverser = 1 };
        double min = double.MaxValue, max = 0, tenderStart = train.Boiler.Tender, speedSum = 0;
        int shovellingTicks = 0, ticks = (int)(seconds * SimConstants.TickRate);
        for (int i = 0; i < ticks && !train.Boiler.Ruptured; i++)
        {
            bool work = vent || fireAt is { } f && train.Boiler.Pressure < f;
            var intent = new PlayerIntent { Buttons = work ? PlayerButtons.Use : PlayerButtons.None };
            CrewActions.Apply(ref fireman, intent, train, SimConstants.TickSeconds);
            if (work && !vent)
                shovellingTicks++;
            train.Step(SimConstants.TickSeconds, controls);
            PlayerMotor.Step(ref fireman, intent, train, p, t, SimConstants.TickSeconds);
            min = Math.Min(min, train.Boiler.Pressure);
            max = Math.Max(max, train.Boiler.Pressure);
            speedSum += train.Dynamics.Speed;
        }
        int shovels = (int)Math.Round(tenderStart - train.Boiler.Tender);
        double burned = tenderStart - train.Boiler.Tender + (startFirebox - train.Boiler.Firebox);
        return new BoilerRun(cars, seconds, Math.Round(min, 2), Math.Round(max, 2), Math.Round(train.Boiler.Pressure, 2), Math.Round(burned, 2), shovels,
            shovels > 0 ? Math.Round(seconds / shovels, 2) : double.PositiveInfinity, Math.Round((double)shovellingTicks / ticks, 3),
            Math.Round(speedSum / ticks, 2), train.Boiler.Ruptured);
    }

    /// <summary>Seconds for a standing train, fire kept fed, to bring pressure from <paramref name="from"/> to the working band.</summary>
    public static double RebuildSeconds(TrainTuning t, BoilerTuning b, PlayerTuning p, int cars, double from = 0)
    {
        var line = new RailLine(new LineDefinition("flat", [new TrackSegment(10_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, 1)), line, 1_000, b);
        train.Boiler.Pressure = from;
        train.Boiler.Firebox = b.FireboxCapacity;
        int ticks = 0;
        while (train.Boiler.Pressure < b.WorkingBandMin && ticks < SimConstants.TickRate * 3600)
        {
            // Keep the box topped up the way a fireman would while it rebuilds.
            if (train.Boiler.Firebox <= b.FireboxCapacity - 1)
                train.Boiler.Shovel(b);
            // On its brake: with steam driving (T97), a standing engine with pressure would otherwise pull away.
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1, Brake = 1 });
            ticks++;
        }
        return ticks * SimConstants.TickSeconds;
    }
}
