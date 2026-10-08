using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// Where the repair kit is (GDD v1.4 §12's engineering kit; the build's carried item, ARCHITECTURE note 150): in a living
/// crewmate's hands, lying somewhere (a car, the line), or lost to the Territory. <see cref="None"/>: the train was never
/// stocked with one, so nothing can strand it.
/// </summary>
public enum KitPlace : byte { None, Carried, Lying, Lost }

/// <summary>How the kit was lost (GDD v1.4 §23.2's table), for the end screen and the incident report (App. C.9).</summary>
public enum KitLoss : byte
{
    None,
    /// <summary>In a car the Car Hugger finished, or the caboose the Passenger rolled away.</summary>
    CarTaken,
    /// <summary>Gone from the world: carried off (the Gaunt leaves for the run with what it takes).</summary>
    Taken,
    /// <summary>In a car cut loose on the main line, further than <see cref="StrandedTuning.KitLostBeyond"/> from every living crewmate.</summary>
    LeftBehind,
}

/// <param name="Body">The kit's body (it's carried as one), for <see cref="KitPlace.Carried"/> and <see cref="KitPlace.Lying"/>.</param>
/// <param name="Vehicle">The car it's lying in, or <see cref="PlayerState.World"/> on the ground.</param>
public readonly record struct KitWhere(KitPlace Place, int Body = -1, int Vehicle = PlayerState.World, KitLoss Loss = KitLoss.None)
{
    public bool Lost => Place == KitPlace.Lost;
}

/// <summary>Stranded (GDD v1.4 §23.2): run.json "stranded".</summary>
/// <param name="KitLostBeyond">A cut car on the main line is gone this far (m) from every living crewmate: D.13's Holdout release distance.</param>
/// <param name="RecoveryFee">The dawn freight's bill for towing a stranded train in, in the tier's per-car values (E.10: 0.25–1.0).</param>
/// <param name="LostForSeconds">The kit has to be lost this long before it counts, so a hand-off between ticks can't strand a night.</param>
public sealed record StrandedTuning(double KitLostBeyond = 400, double RecoveryFee = 0.5, double LostForSeconds = 1);

/// <summary>
/// The repair kit, as §23.2 reads it: lost only when the Territory has taken it. A kit lying on the line, or in a reachable
/// car (on its floor or in its locker, note 173), is never lost, however far back it is. Somebody walks. With more than one
/// kit (a spare from the fortress, one found at a stop: E.12 question 4), it takes losing every one of them. A kit found at
/// a stop counts once the crew have picked it up; one lying unfound in a village they've left isn't theirs to walk back for.
/// </summary>
public static class EngineeringKit
{
    /// <summary>Where the kit is now (host: bodies are the host's). <paramref name="stocked"/>: the night ever had one.</summary>
    public static KitWhere Where(World world, IEnumerable<PlayerState> crew, StrandedTuning t, bool stocked)
    {
        var kits = world.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit && b.Claimed).OrderBy(b => b.Id).ToList();
        if (kits.Count == 0)
            return stocked ? new KitWhere(KitPlace.Lost, Loss: KitLoss.Taken) : new KitWhere(KitPlace.None);
        if (kits.FirstOrDefault(k => k.Carrier >= 0) is { } carried)
            return new KitWhere(KitPlace.Carried, carried.Id);
        KitWhere? lost = null;
        foreach (var kit in kits)
        {
            var train = world.Train;
            if (kit.Parent < 0 || kit.Parent >= train.Vehicles.Count)
                return new KitWhere(KitPlace.Lying, kit.Id);
            int car = kit.Parent;
            if (train.Vehicles[car].Taken)
                lost ??= new KitWhere(KitPlace.Lost, kit.Id, car, KitLoss.CarTaken);
            else if (Behind(world, crew, car, t))
                lost ??= new KitWhere(KitPlace.Lost, kit.Id, car, KitLoss.LeftBehind);
            else
                return new KitWhere(KitPlace.Lying, kit.Id, car);
        }
        return lost!.Value;
    }

    /// <summary>
    /// The wrenches, as §23.2 reads them where the wrench is the repair tool (note 301; the director, 8 Oct 2026: "If every
    /// crew member drops their wrench off the train then leaves them behind and then the train breaks down they could be
    /// stranded"). A wrench is aboard in a living crewmate's kit, on the cab's rack, or on a body (a fallen crewmate keeps
    /// their tools, D.2) in a car that's still reachable or lying on the line within <see cref="StrandedTuning.KitLostBeyond"/>
    /// of a living crewmate. One in a car the Territory took, or cut loose and left behind, or on a body left that far back on
    /// the line, or carried off with it, is lost. <see cref="KitWhere.Body"/> is the body's id for one on a body, else -1;
    /// <see cref="KitWhere.Vehicle"/> where it lies or was lost.
    /// </summary>
    public static KitWhere Wrench(World world, IEnumerable<PlayerState> crew, StrandedTuning t)
    {
        var train = world.Train;
        var living = crew.Where(c => c.Alive).ToList();
        if (living.Any(c => Kit.Has(c.Kit, Tool.Wrench)))
            return new KitWhere(KitPlace.Carried);
        if (train.BoilerTuning is not null && !train.Boiler.WrenchOut)
            return new KitWhere(KitPlace.Lying, Vehicle: 0);
        KitWhere? lost = null;
        foreach (var body in world.Bodies.All.Where(b => b.HasTool(Tool.Wrench)).OrderBy(b => b.Id))
        {
            int car = body.Parent;
            if (car >= 0 && car < train.Vehicles.Count)
            {
                if (train.Vehicles[car].Taken)
                    lost ??= new KitWhere(KitPlace.Lost, body.Id, car, KitLoss.CarTaken);
                else if (Behind(world, crew, car, t))
                    lost ??= new KitWhere(KitPlace.Lost, body.Id, car, KitLoss.LeftBehind);
                else
                    return new KitWhere(KitPlace.Lying, body.Id, car);
            }
            else if (living.Any(c => (PlayerMotor.WorldPosition(c, train) - Bodies.WorldCentre(body, train)).Length <= t.KitLostBeyond))
                return new KitWhere(KitPlace.Lying, body.Id);
            else
                lost ??= new KitWhere(KitPlace.Lost, body.Id, PlayerState.World, KitLoss.LeftBehind);
        }
        return lost ?? new KitWhere(KitPlace.Lost, Loss: KitLoss.Taken);
    }

    /// <summary>
    /// A car cut loose on the main line, beyond reach of every living crewmate: gone into the Territory (§24). A car on a
    /// branch (a facility's pad or siding) is exempt: it's parked, not lost.
    /// </summary>
    static bool Behind(World world, IEnumerable<PlayerState> crew, int car, StrandedTuning t)
    {
        var train = world.Train;
        if (train.Dynamics.Consist.IndexOf(car) >= 0)
            return false;
        var rake = train.Rakes.FirstOrDefault(r => r.Consist.IndexOf(car) >= 0);
        if (rake is null || rake.Path != RailLine.MainPath)
            return false;
        var at = train.Frames[car].Origin;
        return !crew.Any(c => c.Alive && (PlayerMotor.WorldPosition(c, train) - at).Length <= t.KitLostBeyond);
    }

    /// <summary>The end screen's line for how it was lost (<paramref name="wrench"/>: the last wrench, note 301).</summary>
    public static string Line(KitLoss loss, bool wrench = false) => wrench ? loss switch
    {
        KitLoss.CarTaken => "THE LAST WRENCH WENT WITH THE CAR",
        KitLoss.Taken => "THE LAST WRENCH WAS CARRIED OFF",
        KitLoss.LeftBehind => "THE LAST WRENCH WAS LEFT BEHIND ON THE LINE",
        _ => "THERE'S NO WRENCH LEFT",
    } : loss switch
    {
        KitLoss.CarTaken => "THE REPAIR KIT WENT WITH THE CAR",
        KitLoss.Taken => "THE REPAIR KIT WAS CARRIED OFF",
        KitLoss.LeftBehind => "THE REPAIR KIT WAS LEFT BEHIND ON THE LINE",
        _ => "THE REPAIR KIT IS GONE",
    };
}
