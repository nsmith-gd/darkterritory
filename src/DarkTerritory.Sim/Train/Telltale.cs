namespace DarkTerritory.Sim.Train;

/// <summary>
/// The end-of-train telltale (queue #753, ARCHITECTURE §8 note 1052; docs/design/upgrades.md §7.3: "a lamp in the cab that goes
/// out when the train parts behind it"): wired to the train's tail, its last car. Worked out alike on every machine from the
/// rakes, which every machine has, so it needs nothing of its own on the wire.
/// </summary>
public static class Telltale
{
    /// <summary>
    /// Whether the cab's telltale is lit: the train's tail coupled to the engine's rake. Null when there's no telltale fitted
    /// (train.json <c>kit.telltale</c>) or no car behind the engine to wire it to.
    /// </summary>
    public static bool? Lit(TrainOnLine train)
    {
        if (!train.Dynamics.Tuning.Kit.Telltale || Tail(train) is not { } tail)
            return null;
        return train.RakeOf(tail) == train.Dynamics;
    }

    /// <summary>
    /// The train's tail: its last car, the one furthest back of the night's train (not a car standing in a yard, nor the
    /// handcar hung on the van). Fixed for the night: a car keeps its id when the ones ahead of it are cut away.
    /// </summary>
    public static int? Tail(TrainOnLine train)
    {
        int tail = -1;
        foreach (var v in train.Vehicles)
            if (v is { IsEngine: false, YardCar: false, IsHandcar: false } && v.Id > tail)
                tail = v.Id;
        return tail > 0 ? tail : null;
    }
}
