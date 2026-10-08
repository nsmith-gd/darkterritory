namespace DarkTerritory.Sim.Train;

/// <summary>Mirror of content/tuning/boiler.json. Field docs live in that file.</summary>
public sealed record BoilerTuning(
    double PressureMax, double WorkingBandMin, double WorkingBandMax, double Redline, double RuptureHoldSeconds,
    double SafetyValveLift, double SafetyValveCapacity, double PowerFloor,
    int TenderCapacity, int FireboxCapacity, double ShovelSeconds,
    double FireTimeConstant, double IdleDraft, double SteamPerUnit, double AuxiliaryDrain, double HeatingPerCar, double FullThrottleDemand,
    double VentRate, double LowFireFraction, double StartPressure, double StartFirebox)
{
    /// <summary>
    /// GDD §22 deep cold (note 183): each cold step takes this much off the boiler's efficiency (the steam a shovelful makes),
    /// down to <see cref="ColdEfficiencyFloor"/>.
    /// </summary>
    public double ColdEfficiencyPerStep { get; init; } = 0.06;
    public double ColdEfficiencyFloor { get; init; } = 0.7;

    /// <summary>The boiler's efficiency at <paramref name="coldStep"/>.</summary>
    public double ColdEfficiency(int coldStep) => Math.Max(ColdEfficiencyFloor, 1 - ColdEfficiencyPerStep * Math.Max(0, coldStep));

    public const string File = "tuning/boiler.json";

    /// <summary>
    /// Steam drives the train (T97 playtest): no regulator. The pressure sets the speed the engine can make, from none at
    /// <see cref="PowerFloor"/> to the line's top speed at <see cref="SafetyValveLift"/>; coal raises it, the vent drops it.
    /// </summary>
    public bool SteamDrive { get; init; }
    /// <summary>How far under the speed its steam can make the engine is before it pulls at full effort (m/s).</summary>
    public double DriveSpeedBand { get; init; } = 3;
    /// <summary>
    /// With steam driving, the fraction of top speed from which the cylinders draw their full steam: below it they draw
    /// with speed, or with the effort of pulling away, whichever is more.
    /// </summary>
    public double FullDemandAt { get; init; } = 0.85;
    /// <summary>T109: how hard a ruptured engine's seized cylinders drag the train down (m/s²), and to what speed (m/s).</summary>
    public double RuptureDecel { get; init; } = 1.5;
    public double RuptureCoastBelow { get; init; } = 4;
    /// <summary>
    /// With steam driving: how hard an engine short of steam holds the train back (m/s²), while it's going faster than its
    /// steam can make: none in the working band, all of it at the power floor (the director's test build, 7 Oct 2026).
    /// </summary>
    public double StarvedDecel { get; init; }

    /// <summary>The drag (m/s²) on an engine going <paramref name="speed"/> with this boiler (see <see cref="StarvedDecel"/>).</summary>
    public double StarvedDrag(in Boiler b, double speed, double topSpeed) =>
        SteamDrive && !b.Ruptured && speed > b.SteamSpeed(this, topSpeed) ? StarvedDecel * (1 - b.PowerFactor(this)) : 0;
    /// <summary>
    /// App. C.2, GDD §12 (note 275): coal goes on with the shovel in hand, the one off the cab's tool rack. Off, by hand as
    /// before (T29's stroke and the keyboard's hold alike).
    /// </summary>
    public bool ShovelInHand { get; init; } = true;
    /// <summary>T109: seconds of the wrench held at the firebox to repair a ruptured boiler.</summary>
    public double RepairSeconds { get; init; } = 25;
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
    /// <summary>The vent was open on the last step (T101): what the blow-off's steam and roar show, on every machine.</summary>
    public bool Vented;
    /// <summary>
    /// The pressure the vent actually let go on the last step: its full rate while there's steam to vent, and only what the fire
    /// made with the gauge at nothing. What a steam lift a vented engine stands by is wound with (note 368).
    /// </summary>
    public double VentedSteam;
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
    /// <summary>T109: the wrench is out of its rack in the cab, in someone's hands.</summary>
    public bool WrenchOut;
    /// <summary>
    /// The fireman's shovel is off its rack in the cab, in someone's kit or on a body (App. C.2, GDD §12; note 275): there's
    /// the one, and coal goes on with it in hand.
    /// </summary>
    public bool ShovelOut;

    /// <summary>T109: made good with the wrench after a rupture: whole again, but cold and empty.</summary>
    public void Repair()
    {
        Ruptured = false;
        Pressure = 0;
        Firebox = 0;
        AtMaxSeconds = 0;
    }

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

    /// <summary>
    /// Steam drawn per second: cylinders at this throttle, auxiliaries, and heating every car. With steam driving (T97)
    /// the cylinders draw with speed as well as effort, so a hot fire's pressure is spent in going fast.
    /// </summary>
    /// <param name="speedFraction">Speed over the line's top speed.</param>
    public readonly double SteamDemand(BoilerTuning t, double throttle, int cars, double speedFraction = 0)
    {
        double work = Math.Clamp(throttle, 0, 1);
        if (t.SteamDrive)
            work = Math.Max(work, Math.Clamp(speedFraction / t.FullDemandAt, 0, 1));
        return t.AuxiliaryDrain + t.HeatingPerCar * cars + t.FullThrottleDemand * work * PowerFactor(t);
    }

    /// <summary>With steam driving (T97): the speed this pressure lets the engine make, 0 at the power floor to top speed at the valve.</summary>
    public readonly double SteamSpeed(BoilerTuning t, double topSpeed) =>
        Ruptured ? 0 : topSpeed * Math.Clamp((Pressure - t.PowerFloor) / (t.SafetyValveLift - t.PowerFloor), 0, 1);

    /// <summary>With steam driving: the pressure at which the engine makes <paramref name="speed"/> (the bots fire to it).</summary>
    public static double PressureFor(BoilerTuning t, double speed, double topSpeed) =>
        t.PowerFloor + (t.SafetyValveLift - t.PowerFloor) * Math.Clamp(speed / topSpeed, 0, 1);

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
    /// <param name="speedFraction">The engine's speed over top speed (steam driving draws with it).</param>
    public bool Step(BoilerTuning t, double dt, double throttle, int cars, double speedFraction = 0)
    {
        if (Ruptured)
        {
            Venting = Vented = false;
            VentedSteam = 0;
            return false;
        }

        // With steam driving (T97) the exhaust draws the fire as hard as the cylinders are working: with effort, or speed.
        double draw = t.SteamDrive ? Math.Max(Math.Clamp(throttle, 0, 1), Math.Clamp(speedFraction / t.FullDemandAt, 0, 1)) : throttle;
        double burn = Math.Min(BurnRate(t, draw) * dt, Firebox);
        Firebox -= burn;
        double gain = burn * t.SteamPerUnit * Efficiency + ExternalHeat * dt;
        double vent = Venting ? t.VentRate * dt : 0;
        double loss = SteamDemand(t, throttle, cars, speedFraction) * dt + vent;
        double next = Pressure + gain - loss;
        // The engine's own demand is served first; with the gauge run down, the vent gets what's left of what the fire made.
        VentedSteam = Math.Clamp(vent + Math.Min(0, next), 0, vent);
        double shed = SafetyValveJammed ? 0 : Math.Clamp(next - t.SafetyValveLift, 0, t.SafetyValveCapacity * dt);
        SafetyValveLifting = shed > 0;
        Pressure = Math.Clamp(next - shed, 0, t.PressureMax);

        AtMaxSeconds = Pressure >= t.PressureMax ? AtMaxSeconds + dt : 0;
        LowFireSeconds = LowFire(t) ? LowFireSeconds + dt : 0;
        Vented = Venting;
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
