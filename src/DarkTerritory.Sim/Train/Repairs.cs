using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>What's broken and can be mended where you stand (note 301).</summary>
public enum BreakKind : byte { None, Rupture, Breach, Dent, Lamp }

/// <summary>
/// The wrench is the repair tool (queue #39, the director, 7 Oct, GDD App. F.3: "repairing things in the Sea of Thieves
/// style"; ARCHITECTURE §8 note 301). With train.json <c>repair.wrench</c>, every break on the train is mended the same way:
/// the wrench in hand, Use at the break, a few seconds' hammering, and a break left half-mended keeps what was done while
/// you're still at it, so a few presses do it as well as one long hold (<see cref="CrewActions.Apply"/>). The breaks:
/// <list type="bullet">
/// <item>the ruptured boiler, at the firebox (T109, note 263: <see cref="BoilerTuning.RepairSeconds"/>; the repair kit no
/// longer mends it);</item>
/// <item>a breached car, at the hole, from inside (<see cref="Breaches"/>: <see cref="BreachTuning.BoardSeconds"/>);</item>
/// <item>a car battered below <see cref="RepairTuning.DentedBelow"/> by what it ran into or what ran into it (couplings,
/// hazards, strain), at its dent (<see cref="DentAt"/>): <see cref="RepairTuning.IntegrityPerSecond"/> back while it's worked,
/// never past what a Car Hugger ate.</item>
/// </list>
/// Everyone carries a wrench (player.json <c>kit</c>). Worked out alike on the host and a predicting client.
/// </summary>
public static class Repairs
{
    /// <summary>Whether the wrench is the repair tool on this train.</summary>
    public static bool ByWrench(TrainOnLine train) => train.Dynamics.Tuning.Repair.Wrench;

    /// <summary>The wrench in hand.</summary>
    public static bool WrenchInHand(in PlayerState s) => Kit.Held(s) == Tool.Wrench;

    /// <summary>
    /// Whether this player's hands mend the burst boiler: the wrench in hand (note 301), or the repair kit carried where the
    /// wrench isn't the tool (T109).
    /// </summary>
    public static bool MendsBoiler(in PlayerState s, TrainOnLine train) =>
        ByWrench(train) ? WrenchInHand(s) : s.Has(PlayerFlags.RepairKit);

    /// <summary>
    /// Where a car's dent is mended (car frame): its left wall a metre up, three quarters of the way back, clear of the end
    /// doors, the side doors in the middle, the crew lockers ahead of them and the cargo down the right. Null for a car
    /// with no room inside (the engine's is on its boiler: <see cref="DentAt(TrainOnLine, int)"/>).
    /// </summary>
    public static Double3? DentAt(CarShape shape) =>
        shape.Interior is { } room && shape.Cab is null
            ? new Double3(room.Min.X + 0.05, room.Min.Y + 1.0, room.Min.Z + 0.75 * (room.Max.Z - room.Min.Z)) : null;

    /// <summary>
    /// Where this vehicle's dent is mended (its frame). A car's is in its room (<see cref="DentAt(CarShape)"/>). The engine's
    /// (slice 2: it's what runs into things, hazards.json's Sleepers and the line's debris hit the front) is on the boiler's
    /// left flank, waist high over the running board, <see cref="RepairTuning.EngineDentBehind"/> behind the sandbox there:
    /// out of the cab and along the board to it, as the sander goes.
    /// </summary>
    public static Double3? DentAt(TrainOnLine train, int car)
    {
        if (car < 0 || car >= train.Frames.Count)
            return null;
        var shape = train.Frames[car].Shape;
        if (shape.Cab is null)
            return DentAt(shape);
        var sand = shape.Interactables.Where(i => i.Kind == InteractableKind.Sandbox).OrderBy(i => i.Position.X).FirstOrDefault();
        if (sand.Kind != InteractableKind.Sandbox)
            return null;
        var t = train.Dynamics.Tuning;
        return new Double3(-t.Geometry.Engine.BoilerHalfWidth - 0.05, sand.Position.Y + 0.9, sand.Position.Z + t.Repair.EngineDentBehind);
    }

    /// <summary>The engine's dent is worked from its running board, out of the cab; a car's from its floor, inside.</summary>
    static bool CanReach(in PlayerState s, TrainOnLine train) =>
        train.Frames[s.Parent].Shape.Cab is null ? PlayerMotor.Indoors(s, train) : s.Surface == Surface.Deck && !PlayerMotor.InCab(s, train);

    /// <summary>The most a car's shell comes back to: whole, less what a Car Hugger ate (it's gone, not dented).</summary>
    public static double Mendable(Vehicle v) => Math.Max(0, 1 - v.Eaten);

    /// <summary>
    /// Whether this car shows its dent (the callout): battered below <see cref="RepairTuning.DentedBelow"/> of what it can come
    /// back to. Under that it's still mended at the dent (<see cref="Mendable(TrainOnLine, int)"/>), whole again if it's worked
    /// on, but nothing calls you to it.
    /// </summary>
    public static bool Dented(TrainOnLine train, int car) =>
        Mendable(train, car) && train.Vehicles[car].Integrity < train.Dynamics.Tuning.Repair.DentedBelow * Mendable(train.Vehicles[car]);

    /// <summary>Whether this vehicle's shell can be mended at all now: a car with a room, or the engine, short of what it can come back to.</summary>
    public static bool Mendable(TrainOnLine train, int car) =>
        ByWrench(train) && car >= 0 && car < train.Frames.Count && car < train.Vehicles.Count && !train.Vehicles[car].Taken
        && DentAt(train, car) is not null && train.Vehicles[car].Integrity < Mendable(train.Vehicles[car]) - 1e-6;

    /// <summary>
    /// The dented car this player stands at, if any: inside it on its floor, within <see cref="RepairTuning.DentReach"/> of the
    /// dent across the floor. Whatever's in hand (the prompt asks for the wrench); <see cref="Dent"/> wants it.
    /// </summary>
    public static int? AtDent(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (!s.Alive || !Mendable(train, s.Parent) || !CanReach(s, train))
            return null;
        var from = (hand is not null && s.Hand != default ? PlayerMotor.HandAt(s) : null) ?? s.Position;
        var dent = DentAt(train, s.Parent)!.Value;
        double dx = from.X - dent.X, dz = from.Z - dent.Z, reach = train.Dynamics.Tuning.Repair.DentReach;
        return dx * dx + dz * dz <= reach * reach ? s.Parent : null;
    }

    /// <summary>The dented car this player mends now: at its dent with the wrench in hand.</summary>
    public static int? Dent(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        WrenchInHand(s) ? AtDent(s, train, hand) : null;

    /// <summary>The engine's forward lamp is smashed and wants mending (slice 2: where the wrench is the repair tool).</summary>
    public static bool LampSmashed(TrainOnLine train) => ByWrench(train) && train.LampOut?.Invoke() > 0;

    /// <summary>Where the forward lamp is (the engine's frame): on the cab's nose under its front windows (<see cref="World.LampPosition"/>).</summary>
    public static Double3 LampAt(TrainOnLine train) => new(0, World.LampHeight, -train.Frames[0].Shape.HalfLength - 0.3);

    /// <summary>Inside the cab, on its front windows' sill: where the smashed lamp's mended from, and called out for the mender.</summary>
    public static Double3? LampSillAt(TrainOnLine train) =>
        train.Frames[0].Shape.Cab is { } cab ? new Double3(-0.55, cab.Min.Y + 1.5, cab.Min.Z + 0.15) : null;

    /// <summary>
    /// At the smashed lamp, whatever's in hand: in the cab, at its front (note 280 put the bunker, the firebox and the console
    /// under the front windows, so it's from the floor behind them, within <see cref="RepairTuning.DentReach"/> of where a
    /// crewmate stands at them), the lamp just the other side of the glass.
    /// </summary>
    public static bool AtLamp(in PlayerState s, TrainOnLine train) =>
        s.Alive && s.Parent == 0 && LampSmashed(train) && train.Frames[0].Shape.Cab is not null && PlayerMotor.InCab(s, train)
        && s.Position.Z - PlayerMotor.CabFloorZ(train.Frames[0].Shape) <= train.Dynamics.Tuning.Repair.DentReach;

    /// <summary>Mending the smashed lamp now: at it with the wrench in hand.</summary>
    public static bool Lamp(in PlayerState s, TrainOnLine train) => WrenchInHand(s) && AtLamp(s, train);

    /// <summary>
    /// The break this player's at, whatever's in hand: what the HUD names and the callout's prompt asks the wrench for. The
    /// breach first (it's the one thing worked at the hole), then the boiler, then a dent.
    /// </summary>
    public static BreakKind At(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (Breaches.AtHole(s, train, hand) is not null)
            return BreakKind.Breach;
        if (CrewActions.AtTheRupture(s, train, hand))
            return BreakKind.Rupture;
        if (AtLamp(s, train))
            return BreakKind.Lamp;
        return AtDent(s, train, hand) is not null ? BreakKind.Dent : BreakKind.None;
    }

    /// <summary>
    /// Whether a half-done mend is kept with Use let go (note 301: a few presses, Sea of Thieves style): the wrench in hand,
    /// at a break it mends. Walk off, or put the wrench away, and it's started over.
    /// </summary>
    public static bool Keeps(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        ByWrench(train) && WrenchInHand(s) && At(s, train, hand) is BreakKind.Breach or BreakKind.Rupture;

    /// <summary>The number key that puts the wrench in hand (its slot's), or 0: it's in hand already, or there's none.</summary>
    public static byte WrenchKey(in PlayerState s)
    {
        if (WrenchInHand(s))
            return 0;
        for (int i = 0; i < Kit.Slots; i++)
            if (Kit.At(s.Kit, i) == Tool.Wrench)
                return (byte)(i + 1);
        return 0;
    }

    /// <summary>A dent worked for <paramref name="dt"/>: its shell back by <see cref="RepairTuning.IntegrityPerSecond"/>, up to what it can come back to.</summary>
    public static void MendDent(TrainOnLine train, int car, double dt)
    {
        var v = train.Vehicles[car];
        v.Integrity = Math.Min(Mendable(v), v.Integrity + train.Dynamics.Tuning.Repair.IntegrityPerSecond * dt);
    }
}

/// <summary>A break on the train, where its callout's drawn (car frame of <see cref="Vehicle"/>; note 301).</summary>
public readonly record struct BreakCallout(BreakKind Kind, int Vehicle, Double3 At);

public static class RepairCallouts
{
    /// <summary>
    /// Every break the crew can mend now, and where (note 301: "every repairable breakage has a clear VFX callout"): the burst
    /// boiler at its fire door, a breach at its hole, a battered car at its dent. Presentation reads it; it's all replicated
    /// state, so every client calls out the same breaks.
    /// </summary>
    public static List<BreakCallout> Of(TrainOnLine train)
    {
        var list = new List<BreakCallout>();
        if (train.BoilerTuning is not null && train.Boiler.Ruptured && train.Frames.Count > 0
            && train.Frames[0].Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Firebox) is { } fire)
            list.Add(new BreakCallout(BreakKind.Rupture, 0, fire.Position + Double3.Up * fire.Aim));
        // The lamp's called out twice: on the cab's nose where it is (seen from outside), and on the front windows' sill inside,
        // where it's mended from (the nose is below the glass from in there).
        if (train.Frames.Count > 0 && Repairs.LampSmashed(train))
        {
            list.Add(new BreakCallout(BreakKind.Lamp, 0, Repairs.LampAt(train)));
            if (Repairs.LampSillAt(train) is { } sill)
                list.Add(new BreakCallout(BreakKind.Lamp, 0, sill));
        }
        for (int car = 0; car < train.Vehicles.Count && car < train.Frames.Count; car++)
        {
            var v = train.Vehicles[car];
            if (v.Taken)
                continue;
            if (car > 0 && v.Breached)
                list.Add(new BreakCallout(BreakKind.Breach, car, v.BreachAt));
            if (Repairs.Dented(train, car))
                list.Add(new BreakCallout(BreakKind.Dent, car, Repairs.DentAt(train, car)!.Value));
        }
        return list;
    }
}
