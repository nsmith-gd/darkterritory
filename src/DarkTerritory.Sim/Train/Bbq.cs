using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// The BBQ deck on the engine's front (ARCHITECTURE §8 note 688; the director, 13 Oct 2026: "a couple muskoka chairs that can
/// be sat in, a plastic cooler with beers, and a BBQ mounted on a stand on the deck. Each character needs to be able to
/// interact with each object so they can have a tiny "party" at the front ... its the BBQ Dads at the end of the world").
/// Its geometry is <see cref="CarShape.WithBbqDeck"/>; its numbers train.json <c>composition.bbq</c>. Worked alike on the host
/// and a predicting client; only handing over a beer or a sausage is the host's (World).
/// </summary>
public static class Bbq
{
    /// <summary>The deck's layout on <paramref name="vehicle"/>, if it has one fitted.</summary>
    public static BbqLayout? On(TrainOnLine train, int vehicle) =>
        vehicle >= 0 && vehicle < train.Frames.Count ? train.Frames[vehicle].Shape.Bbq : null;

    /// <summary>Whether a grill's lit (it has charcoal burning).</summary>
    public static bool Lit(Vehicle v) => v.Grill > 0;

    /// <summary>
    /// A Muskoka chair (each tick, after the gun's seat): the Seat press at a free chair sits you in it, facing down the line;
    /// the Seat press again, or Jump (the motor), gets you up. Sat, your feet stay on its seat as the train runs. Someone
    /// already in it keeps it. <paramref name="others"/> are the rest of the crew as they stand.
    /// </summary>
    public static void Sit(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, IEnumerable<PlayerState> others, HandTuning? hand = null)
    {
        bool lounging = s.Has(PlayerFlags.Lounging);
        var deck = On(train, s.Parent);
        if (lounging)
        {
            if (deck is null || !s.Alive || intent.Has(PlayerActions.Seat))
            {
                GetUp(ref s);
                return;
            }
            // Where they sat: the chair nearest the feet.
            var feet = s.Position;
            var seat = deck.Chairs.MinBy(c => (c - feet).Length);
            s.Position = seat;
            s.Velocity = default;
            return;
        }
        if (deck is null || !s.Alive || !intent.Has(PlayerActions.Seat) || s.Has(PlayerFlags.Held) || s.Has(PlayerFlags.Heavy) || s.Has(PlayerFlags.Seated))
            return;
        if (CrewActions.NearestInteractable(s, train, hand) is not ({ Kind: InteractableKind.Chair } chair, var v) || v != s.Parent)
            return;
        var at = deck.Chairs[chair.Index];
        foreach (var o in others)
            if (o.Parent == s.Parent && o.Has(PlayerFlags.Lounging) && (o.Position - at).Length < 0.3)
                return;
        s.Flags |= PlayerFlags.Lounging;
        s.Position = at;
        s.Velocity = default;
        // Down the line (the engine's −Z), the way a Muskoka chair's set: the line ahead coming at you.
        s.Yaw = 0;
        s.Pitch = 0;
    }

    /// <summary>Up out of the chair, where it is.</summary>
    public static void GetUp(ref PlayerState s) => s.Flags &= ~PlayerFlags.Lounging;

    /// <summary>
    /// Use held at the grill (CrewActions): out, it's lit once held <see cref="BbqTuning.LightSeconds"/> (a bag of charcoal:
    /// <see cref="BbqTuning.BurnSeconds"/>); lit, every <see cref="BbqTuning.GrillSeconds"/> held is a sausage done
    /// (<see cref="Vehicle.Cooked"/>: the host puts it in the cook's hands).
    /// </summary>
    public static void Work(ref PlayerState s, TrainOnLine train, int vehicle, double before, double dt)
    {
        var t = train.Dynamics.Tuning.Composition.Bbq;
        var grill = train.Vehicles[vehicle];
        s.ActionProgress += dt;
        if (!Lit(grill))
        {
            if (before < t.LightSeconds && s.ActionProgress >= t.LightSeconds)
            {
                grill.Grill = t.BurnSeconds;
                s.ActionProgress = 0;
            }
            return;
        }
        if (s.ActionProgress >= t.GrillSeconds)
        {
            grill.Cooked++;
            s.ActionProgress -= t.GrillSeconds;
        }
    }

    /// <summary>The charcoal burning down, each tick: out once it's gone.</summary>
    public static void Burn(TrainOnLine train, double dt)
    {
        foreach (var v in train.Vehicles)
            if (v is not null && v.Grill > 0)
                v.Grill = Math.Max(0, v.Grill - dt);
    }

    /// <summary>Within <see cref="BbqTuning.GrillReach"/> of a lit grill on the deck you're on: warm, as at the firebox.</summary>
    public static bool ByTheGrill(in PlayerState s, TrainOnLine train)
    {
        if (On(train, s.Parent) is not { } deck || !Lit(train.Vehicles[s.Parent]))
            return false;
        double reach = train.Dynamics.Tuning.Composition.Bbq.GrillReach;
        double dx = s.Position.X - deck.Grill.X, dz = s.Position.Z - deck.Grill.Z;
        return dx * dx + dz * dz <= reach * reach;
    }

    /// <summary>Any grill on the train lit (the Cinder Hounds come to it as to a hot boiler).</summary>
    public static bool AnyLit(TrainOnLine train) => train.Vehicles.Any(v => v is not null && Lit(v));
}
