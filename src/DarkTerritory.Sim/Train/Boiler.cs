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
    /// <summary>
    /// The blower at a stand (the director, 21 Oct, note 969): the fire's draft while the engine stands, as a fraction of
    /// working draft. Standing is the only time it applies (<see cref="Boiler.Step"/>'s throttle and speed both nil), so the
    /// burn and endurance table under way (spec B.6) is untouched; it sets how fast a stopped engine rebuilds (spec C.2).
    /// Null: <see cref="IdleDraft"/>, as before.
    /// </summary>
    public double? StandingDraft { get; init; }
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
    /// <summary>
    /// Note 717: how far into a stroke at the firebox (Use held) the coal's on the shovel (<see cref="Player.PlayerFlags.Shovelful"/>),
    /// there to be thrown (horseplay) until it goes in at <see cref="ShovelSeconds"/>.
    /// </summary>
    public double ScoopSeconds { get; init; } = 0.6;

    /// <summary>
    /// The director's playtest of 13 Oct evening (line 19, note 903): "a shovel and one to three seconds later ... some sort of
    /// reaction for the locomotive". This share of each shovelful doesn't settle into the bed: it catches over
    /// <see cref="CatchSeconds"/> and flares over <see cref="FlareSeconds"/>, so its steam lands as a kick on the gauge a second
    /// or two after the stroke. The same coal makes the same steam (B.6's burn table holds); it's only sooner. 0: off.
    /// </summary>
    public double CatchShare { get; init; }
    public double CatchSeconds { get; init; } = 0.9;
    public double FlareSeconds { get; init; } = 1.2;
    /// <summary>Note 903: the engine's surge while a shovelful flares, m/s² per unit of pressure a second the flare makes.</summary>
    public double SurgePerPressure { get; init; }
    /// <summary>Note 903: the most that surge pushes (m/s²), however many shovelfuls are flaring at once.</summary>
    public double SurgeMax { get; init; }
    /// <summary>Note 903: the surge only from this speed (m/s): running, not shunting in a yard.</summary>
    public double SurgeFrom { get; init; } = 4;

    /// <summary>
    /// Note 903: how hard the coal's flaring, as one shovelful at its height is 1 (and more shovelfuls more): what the stack's
    /// smoke and the chuff answer with.
    /// </summary>
    public double FlareLook(in Boiler b) => CatchShare <= 0 ? 0 : b.Flare(this) / (CatchShare * SteamPerUnit * FlarePeak);

    /// <summary>
    /// A shovelful's flare at its height, per unit of its coal's steam: the two lags stepped as <see cref="Boiler.Step"/> steps
    /// them, a tick at a time, so 1 in <see cref="FlareLook"/> is one shovelful's peak exactly (and no platform math in the Sim).
    /// </summary>
    double FlarePeak
    {
        get
        {
            double dt = SimConstants.TickSeconds, green = 1, catching = 0, peak = 0;
            for (int i = 0; i < SimConstants.TickRate * 10; i++)
            {
                double caught = CatchSeconds > 0 ? green * Math.Min(1, dt / CatchSeconds) : green;
                green -= caught;
                catching += caught - (FlareSeconds > 0 ? catching * Math.Min(1, dt / FlareSeconds) : catching);
                peak = Math.Max(peak, catching / Math.Max(dt, FlareSeconds));
            }
            return peak;
        }
    }
    /// <summary>Note 903: the push (m/s²) a steam-driven engine gets from the coal flaring in its firebox.</summary>
    public double Surge(in Boiler b) => SteamDrive && !b.Ruptured ? Math.Min(SurgeMax, SurgePerPressure * b.Flare(this)) : 0;
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
    /// <summary>
    /// Note 903: coal just shovelled on that hasn't caught yet (<see cref="BoilerTuning.CatchShare"/> of each shovelful), and
    /// coal that's caught and flaring, its steam going into the gauge now. Both count as the fire's.
    /// </summary>
    public double Green;
    public double Catching;

    /// <summary>Note 903: the pressure a second the flaring coal is making (what the gauge's kick, the stack and the chuff show).</summary>
    public readonly double Flare(BoilerTuning t) => Ruptured || t.FlareSeconds <= 0 ? 0 : Catching / t.FlareSeconds * t.SteamPerUnit;

    /// <summary>Every unit of coal in the firebox: the bed, and what's catching on it (note 903).</summary>
    public readonly double Fire => Firebox + Green + Catching;

    /// <summary>T109: made good with the wrench after a rupture: whole again, but cold and empty.</summary>
    public void Repair()
    {
        Ruptured = false;
        Pressure = 0;
        Firebox = Green = Catching = 0;
        AtMaxSeconds = 0;
    }

    public static Boiler Fresh(BoilerTuning t) => new()
    {
        Pressure = t.StartPressure,
        Firebox = t.StartFirebox,
        Tender = t.TenderCapacity,
    };

    public readonly bool InWorkingBand(BoilerTuning t) => Pressure >= t.WorkingBandMin && Pressure <= t.WorkingBandMax;
    public readonly bool LowFire(BoilerTuning t) => Fire < t.FireboxCapacity * t.LowFireFraction;
    public readonly double FireFraction(BoilerTuning t) => Fire / t.FireboxCapacity;

    /// <summary>Fraction of full tractive effort the pressure supports: none below the floor, all from the working band.</summary>
    public readonly double PowerFactor(BoilerTuning t) =>
        Ruptured ? 0 : Math.Clamp((Pressure - t.PowerFloor) / (t.WorkingBandMin - t.PowerFloor), 0, 1);

    /// <summary>Coal burned per second at this firebox load and regulator opening.</summary>
    /// <param name="standing">The engine stands: the blower's draft (<see cref="BoilerTuning.StandingDraft"/>), not the exhaust's.</param>
    public readonly double BurnRate(BoilerTuning t, double throttle, bool standing = false) =>
        Ruptured ? 0 : Firebox / t.FireTimeConstant
            * (standing && t.StandingDraft is { } blower ? blower : t.IdleDraft + (1 - t.IdleDraft) * Math.Clamp(throttle, 0, 1));

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
        if (Ruptured || Tender < 1 || Fire > t.FireboxCapacity - 1)
            return false;
        Tender -= 1;
        double green = Math.Clamp(t.CatchShare, 0, 1);
        Firebox += 1 - green;
        Green += green;
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
        double burn = Math.Min(BurnRate(t, draw, standing: draw <= 0 && speedFraction <= 0) * dt, Firebox);
        Firebox -= burn;
        // Note 903: under way, the fresh coal catches, then flares in the exhaust's draw: every shovelful answers within a few
        // seconds. Standing, with no exhaust to draw it, it settles into the bed like the rest (so C.2's slow rebuild at a stop
        // holds: what limits it is the bed's burn, and a flare would get round that).
        bool working = draw > 0 || speedFraction > 0;
        double caught = t.CatchSeconds > 0 ? Green * Math.Min(1, dt / t.CatchSeconds) : Green;
        double flared = t.FlareSeconds > 0 ? Catching * Math.Min(1, dt / t.FlareSeconds) : Catching;
        Green -= caught;
        if (working)
            Catching += caught;
        else
            Firebox += caught;
        Catching -= flared;
        double gain = (burn + flared) * t.SteamPerUnit + ExternalHeat * dt;
        double vent = Venting ? t.VentRate * dt : 0;
        double loss = SteamDemand(t, throttle, cars, speedFraction) * dt + vent;
        double next = Pressure + gain - loss;
        // The engine's own demand is served first; with the gauge run down, the vent gets what's left of what the fire made.
        VentedSteam = Math.Clamp(vent + Math.Min(0, next), 0, vent);
        // Past the lift the valve blows off a flare's kick as well (note 903): a shovelful in the red lifts it, and doesn't hurry
        // the climb to a rupture (T109's time in the red to answer it holds).
        double shed = SafetyValveJammed ? 0 : Math.Clamp(next - t.SafetyValveLift, 0, t.SafetyValveCapacity * dt + flared * t.SteamPerUnit);
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
            Firebox = Green = Catching = 0;
            return true;
        }
        return false;
    }
}
