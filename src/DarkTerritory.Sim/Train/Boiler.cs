namespace DarkTerritory.Sim.Train;

/// <summary>Mirror of content/tuning/boiler.json. Field docs live in that file.</summary>
public sealed record BoilerTuning(
    double PressureMax, double WorkingBandMin, double WorkingBandMax, double Redline, double RuptureHoldSeconds,
    double SafetyValveLift, double SafetyValveCapacity, double PowerFloor,
    int TenderCapacity, int FireboxCapacity, double ShovelSeconds,
    double FireTimeConstant, double IdleDraft, double SteamPerUnit, double AuxiliaryDrain, double HeatingPerCar, double FullThrottleDemand,
    double VentRate, double LowFireFraction, double StartPressure, double StartFirebox)
{
    public const string File = "tuning/boiler.json";
}

/// <summary>
/// The locomotive's boiler (spec B.6, GDD §12 Boiler role). State only; <see cref="Step"/> advances it.
/// A struct so it snapshots and restores by value for client prediction.
/// </summary>
public struct Boiler
{
    public double Pressure;
    /// <summary>Coal in the firebox, units.</summary>
    public double Firebox;
    /// <summary>Coal left in the tender, units.</summary>
    public double Tender;
    /// <summary>Seconds pressure has been held at maximum.</summary>
    public double AtMaxSeconds;
    /// <summary>Seconds the fire has been below <see cref="BoilerTuning.LowFireFraction"/>.</summary>
    public double LowFireSeconds;
    public bool Ruptured;

    /// <summary>The safety valve lifted this tick: loud, visible, and the gauge is in the red.</summary>
    public bool SafetyValveLifting;
    /// <summary>A jammed safety valve cannot lift (a hazard or enemy can set this).</summary>
    public bool SafetyValveJammed;

    /// <summary>Set by the crew each tick: someone is holding the vent valve open.</summary>
    public bool Venting;
    /// <summary>Extra pressure per second from outside the model: the Stoker (GDD App. A.5).</summary>
    public double ExternalHeat;
    /// <summary>Deep cold multiplies boiler efficiency (GDD §22 hazards). 1 is a normal night.</summary>
    public double Efficiency;
    /// <summary>
    /// The firebox door (GDD v1.1 App. A.5 Stoker, "keep it hot, keep it shut"): shovelling opens it, and it swings shut a
    /// few seconds after the last shovelful, so long as someone's in the cab to see to it. Left with the cab empty it
    /// stays open, and at a stop that's how the Stoker gets in. DESIGN-TODO: the GDD doesn't say how it's shut.
    /// </summary>
    public bool FireDoorOpen;
    /// <summary>Seconds since the last shovelful (the door's swing-shut clock).</summary>
    public double SinceShovel;

    public static Boiler Fresh(BoilerTuning t) => new()
    {
        Pressure = t.StartPressure,
        Firebox = t.StartFirebox,
        Tender = t.TenderCapacity,
        Efficiency = 1,
    };

    public readonly bool InWorkingBand(BoilerTuning t) => Pressure >= t.WorkingBandMin && Pressure <= t.WorkingBandMax;
    public readonly bool LowFire(BoilerTuning t) => Firebox < t.FireboxCapacity * t.LowFireFraction;
    public readonly double FireFraction(BoilerTuning t) => Firebox / t.FireboxCapacity;

    /// <summary>Fraction of full tractive effort the pressure supports: none below the floor, all from the working band.</summary>
    public readonly double PowerFactor(BoilerTuning t) =>
        Ruptured ? 0 : Math.Clamp((Pressure - t.PowerFloor) / (t.WorkingBandMin - t.PowerFloor), 0, 1);

    /// <summary>Coal burned per second at this firebox load and regulator opening.</summary>
    public readonly double BurnRate(BoilerTuning t, double throttle) =>
        Ruptured ? 0 : Firebox / t.FireTimeConstant * (t.IdleDraft + (1 - t.IdleDraft) * Math.Clamp(throttle, 0, 1));

    /// <summary>Steam drawn per second: cylinders at this throttle, auxiliaries, and heating every car.</summary>
    public readonly double SteamDemand(BoilerTuning t, double throttle, int cars) =>
        t.AuxiliaryDrain + t.HeatingPerCar * cars + t.FullThrottleDemand * Math.Clamp(throttle, 0, 1) * PowerFactor(t);

    /// <summary>A shovelful into the firebox, if there's room and coal. Returns whether it happened.</summary>
    public bool Shovel(BoilerTuning t)
    {
        FireDoorOpen = true;
        SinceShovel = 0;
        if (Ruptured || Tender < 1 || Firebox > t.FireboxCapacity - 1)
            return false;
        Tender -= 1;
        Firebox += 1;
        return true;
    }

    /// <returns>True on the tick the boiler ruptures.</returns>
    public bool Step(BoilerTuning t, double dt, double throttle, int cars)
    {
        if (Ruptured)
        {
            Venting = false;
            return false;
        }

        double burn = Math.Min(BurnRate(t, throttle) * dt, Firebox);
        Firebox -= burn;
        double gain = burn * t.SteamPerUnit * Efficiency + ExternalHeat * dt;
        double loss = SteamDemand(t, throttle, cars) * dt + (Venting ? t.VentRate * dt : 0);
        double next = Pressure + gain - loss;
        double shed = SafetyValveJammed ? 0 : Math.Clamp(next - t.SafetyValveLift, 0, t.SafetyValveCapacity * dt);
        SafetyValveLifting = shed > 0;
        Pressure = Math.Clamp(next - shed, 0, t.PressureMax);

        AtMaxSeconds = Pressure >= t.PressureMax ? AtMaxSeconds + dt : 0;
        LowFireSeconds = LowFire(t) ? LowFireSeconds + dt : 0;
        Venting = false;

        if (AtMaxSeconds >= t.RuptureHoldSeconds)
        {
            // Spec B.6 / GDD §23: loud, spectacular, run-changing. The engine is a coasting weight from here.
            Ruptured = true;
            Pressure = 0;
            Firebox = 0;
            return true;
        }
        return false;
    }
}
