namespace DarkTerritory.Sim.Train;

/// <summary>How far gone a car's or the engine's shell is (note 576): what its tells say, from the integrity alone.</summary>
public enum FailStage : byte
{
    /// <summary>Above <see cref="FailingTuning.Below"/>: knocked about, perhaps, but holding.</summary>
    Holding,
    /// <summary>Under it: working itself apart as it runs, its freight spilling (a car), its power and brake going (the engine).</summary>
    Failing,
    /// <summary>Under <see cref="FailingTuning.BreakingBelow"/>: the last warning.</summary>
    Breaking,
    /// <summary>Nothing left: a car comes apart (off its rails, parted from the train), the engine breaks down.</summary>
    Broken,
}

/// <summary>A failing vehicle as presentation sees it (note 576): which, how far gone, spilling its freight, and how fast its rake runs.</summary>
public readonly record struct FailingCar(int Vehicle, FailStage Stage, bool Spilling, double Speed);

/// <summary>
/// Damage that's let go costs the night (train.json <c>failing</c>; the director, 9 Oct 2026, note 576: "If players dont fix
/// the ship in sea of thieves the ship goes down and you lose everything. If players dont fix the train here seemingly
/// nothing happens"). Field docs live in train.json. Unset (<see cref="Enabled"/> false), a battered car only costs its repairs.
/// </summary>
public sealed record FailingTuning
{
    public bool Enabled { get; init; }
    public double Below { get; init; } = 0.5;
    public double BreakingBelow { get; init; } = 0.2;
    public double WorkPerSecond { get; init; } = 0.0025;
    public double SpillPerSecond { get; init; } = 0.005;
    public double AtSpeed { get; init; } = 15;
    public double MostFactor { get; init; } = 1.5;
    public double PowerAtBreak { get; init; } = 0.35;
    public double BrakeAtBreak { get; init; } = 0.4;
}

/// <summary>
/// A battered car or engine left unmended goes on to cost the night, Sea of Thieves style (note 576). Under
/// <see cref="FailingTuning.Below"/> a shell works itself apart as it runs, faster the faster it runs: a car's freight spills
/// out of it as it goes, and the engine's power and brake fall away with what's left of it. Nothing comes of it standing.
/// At nothing a car comes apart: off its rails, the train parted ahead of it (<see cref="World"/>, on the host), so it and
/// what's behind it are lost; the engine breaks down (no power), and once it's at a stand the night ends stranded
/// (<see cref="Run.Run"/>, GDD §23.2). The wrench at its dent mends it as any dent (<see cref="Repairs.MendDent"/>).
/// The working and the spilling run on every machine alike in <see cref="TrainOnLine.Step"/> (the engine's power is the
/// train's motion, predicted); the parting and the breakdown are the host's.
/// </summary>
public static class Failing
{
    /// <summary>Where a shell is: from its integrity, so every machine says the same from the replicated state.</summary>
    public static FailStage Stage(FailingTuning t, Vehicle v) => !t.Enabled || v.Derelict || v.Taken ? FailStage.Holding
        : v.Integrity <= 0 ? FailStage.Broken
        : v.Integrity < t.BreakingBelow ? FailStage.Breaking
        : v.Integrity < t.Below ? FailStage.Failing : FailStage.Holding;

    public static FailStage Stage(TrainOnLine train, int vehicle) =>
        vehicle >= 0 && vehicle < train.Vehicles.Count ? Stage(train.Dynamics.Tuning.Failing, train.Vehicles[vehicle]) : FailStage.Holding;

    /// <summary>How hard the running works a failing shell: by speed, to <see cref="FailingTuning.MostFactor"/>.</summary>
    static double Factor(FailingTuning t, double speed) => Math.Clamp(Math.Abs(speed) / t.AtSpeed, 0, t.MostFactor);

    /// <summary>
    /// A tick of it (both machines): each failing vehicle of a moving rake loses <see cref="FailingTuning.WorkPerSecond"/> of
    /// its shell (the engine only while it's pulling), and a failing cargo car <see cref="FailingTuning.SpillPerSecond"/> of
    /// its load, both by the running's factor. Not in the yard (GDD §9, the fortress is safe), nor a car off its rails, a
    /// derelict or a yard's standing car.
    /// </summary>
    public static void Step(TrainOnLine train, IEnumerable<TrainDynamics> rakes, TrainDynamics engineRake, double pulling, double dt)
    {
        var t = train.Dynamics.Tuning.Failing;
        if (!t.Enabled || train.HeldInYard)
            return;
        foreach (var rake in rakes)
        {
            double f = Factor(t, rake.Speed);
            if (f <= 0)
                continue;
            foreach (var v in rake.Consist.Vehicles)
            {
                if (v.OffRails || v.Integrity <= 0 || Stage(t, v) < FailStage.Failing || train.StandingCar(v.Id))
                    continue;
                double work = v.IsEngine ? (rake == engineRake ? pulling : 0) : 1;
                v.Integrity = Math.Max(0, v.Integrity - t.WorkPerSecond * f * work * dt);
                if (v.Integrity <= 0)
                    train.WornThrough.Add(v.Id);
                if (v.Kind == VehicleKind.Cargo && v.Load > 0)
                    v.Load = Math.Max(0, v.Load - t.SpillPerSecond * f * dt);
            }
        }
    }

    /// <summary>
    /// What's left of the engine's pull (1 holding): from 1 at <see cref="FailingTuning.Below"/> down to
    /// <see cref="FailingTuning.PowerAtBreak"/> as its shell goes, and nothing once it's broken down.
    /// </summary>
    public static double Power(FailingTuning t, Vehicle engine) => Stage(t, engine) switch
    {
        FailStage.Holding => 1,
        FailStage.Broken => 0,
        _ => t.PowerAtBreak + (1 - t.PowerAtBreak) * engine.Integrity / t.Below,
    };

    /// <summary>What's left of the engine's brake (1 holding): down to <see cref="FailingTuning.BrakeAtBreak"/>, which it keeps broken down, to come to a stand on.</summary>
    public static double Brake(FailingTuning t, Vehicle engine) => Stage(t, engine) switch
    {
        FailStage.Holding => 1,
        FailStage.Broken => t.BrakeAtBreak,
        _ => t.BrakeAtBreak + (1 - t.BrakeAtBreak) * engine.Integrity / t.Below,
    };

    /// <summary>
    /// Every failing vehicle of the train, how far gone, and whether it's spilling freight: what presentation draws and sounds
    /// (the cars shedding, the engine smoking). All from replicated state, so every client shows the same.
    /// </summary>
    public static List<FailingCar> Of(TrainOnLine train)
    {
        var list = new List<FailingCar>();
        var t = train.Dynamics.Tuning.Failing;
        if (!t.Enabled)
            return list;
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            var v = train.Vehicles[i];
            if (v.OffRails || Stage(t, v) is not (FailStage.Failing or FailStage.Breaking) && !(v.IsEngine && Stage(t, v) == FailStage.Broken))
                continue;
            list.Add(new FailingCar(i, Stage(t, v), v.Kind == VehicleKind.Cargo && v.Load > 0, train.RakeOf(i).Speed));
        }
        return list;
    }

    /// <summary>The engine's broken down (note 576): its shell gone, it makes no power. With the rules on.</summary>
    public static bool BrokenDown(TrainOnLine train) => train.Vehicles.Count > 0 && Stage(train, 0) == FailStage.Broken;

    /// <summary>
    /// Host, after the step: a car of the engine's rake worn through by running failing (<see cref="TrainOnLine.WornThrough"/>)
    /// comes apart. A car taken to nothing at once by something with an end of its own (the powder blast, note 182; the Car
    /// Hugger's take, note 109) keeps that end. Its freight's gone
    /// (spilled), it's off its rails (it holds fast where it is, <see cref="TrainOnLine.OffRailsDrag"/>), and the train's
    /// parted ahead of it, so it and everything behind it are left on the line. Never the engine (it breaks down instead).
    /// </summary>
    /// <returns>The car that came apart this tick, or −1.</returns>
    public static int Apart(TrainOnLine train)
    {
        var t = train.Dynamics.Tuning.Failing;
        if (!t.Enabled)
            return -1;
        var rake = train.Dynamics.Consist.Vehicles;
        for (int i = 1; i < rake.Count; i++)
        {
            var v = rake[i];
            if (Stage(t, v) != FailStage.Broken || v.OffRails || v.IsEngine || !train.WornThrough.Contains(v.Id))
                continue;
            if (!train.Uncouple(rake[i - 1].Id))
                return -1;
            v.Load = 0;
            v.CargoIntegrity = 0;
            v.OffRails = true;
            return v.Id;
        }
        return -1;
    }
}
