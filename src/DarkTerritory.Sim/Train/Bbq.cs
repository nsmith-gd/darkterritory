using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// The BBQ deck on the engine's front (ARCHITECTURE §8 note 698; the director, 13 Oct 2026: "a couple muskoka chairs that can
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
    /// A Muskoka chair (each tick, after the gun's seat), or the hammock or the hot tub (note 744): the Seat press at a free one
    /// sits you in it, facing its way (a chair down the line); the Seat press again, or Jump (the motor), gets you up. Sat,
    /// your feet stay in it as the train runs. Someone already in it keeps it. <paramref name="others"/> are the rest of the
    /// crew as they stand.
    /// </summary>
    public static void Sit(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, IEnumerable<PlayerState> others, HandTuning? hand = null)
    {
        bool lounging = s.Has(PlayerFlags.Lounging);
        var seats = Seats(train, s.Parent);
        if (lounging)
        {
            if (seats.Count == 0 || !s.Alive || intent.Has(PlayerActions.Seat))
            {
                GetUp(ref s);
                return;
            }
            // Where they sat: the seat nearest the feet.
            var feet = s.Position;
            s.Position = seats.MinBy(c => (c.At - feet).Length).At;
            s.Velocity = default;
            return;
        }
        if (seats.Count == 0 || !s.Alive || !intent.Has(PlayerActions.Seat) || s.Has(PlayerFlags.Held) || s.Has(PlayerFlags.Heavy) || s.Has(PlayerFlags.Seated))
            return;
        if (CrewActions.NearestInteractable(s, train, hand) is not ({ } seat, var v) || v != s.Parent || !IsSeat(seat.Kind))
            return;
        var shape = train.Frames[v].Shape;
        var (at, yaw) = seat.Kind == InteractableKind.Chair ? (shape.Bbq!.Chairs[seat.Index], 0.0)
            : (shape.Lounges[seat.Index].At, shape.Lounges[seat.Index].Yaw);
        foreach (var o in others)
            if (o.Parent == s.Parent && o.Has(PlayerFlags.Lounging) && (o.Position - at).Length < 0.3)
                return;
        s.Flags |= PlayerFlags.Lounging;
        s.Position = at;
        s.Velocity = default;
        // A Muskoka chair's set down the line (the engine's −Z), the line ahead coming at you; the hammock across the car, the
        // tub's seats facing each other.
        s.Yaw = yaw;
        s.Pitch = 0;
    }

    /// <summary>Whether <paramref name="kind"/> is somewhere to sit or lie (a chair, the hammock, the hot tub).</summary>
    public static bool IsSeat(InteractableKind kind) => kind is InteractableKind.Chair or InteractableKind.Hammock or InteractableKind.HotTub;

    /// <summary>Every seat on <paramref name="vehicle"/>: its chairs (down the line) and its lounges.</summary>
    public static List<(Double3 At, double Yaw)> Seats(TrainOnLine train, int vehicle)
    {
        var seats = new List<(Double3 At, double Yaw)>();
        if (vehicle < 0 || vehicle >= train.Frames.Count)
            return seats;
        var shape = train.Frames[vehicle].Shape;
        if (shape.Bbq is { } deck)
            seats.AddRange(deck.Chairs.Select(c => (c, 0.0)));
        seats.AddRange(shape.Lounges.Select(l => (l.At, l.Yaw)));
        return seats;
    }

    /// <summary>The hammock or the hot tub <paramref name="s"/> is in (note 744), or null (a chair, or standing).</summary>
    public static Lounge? LoungingIn(in PlayerState s, TrainOnLine train)
    {
        if (!s.Has(PlayerFlags.Lounging) || s.Parent < 0 || s.Parent >= train.Frames.Count)
            return null;
        var feet = s.Position;
        foreach (var l in train.Frames[s.Parent].Shape.Lounges)
            if ((l.At - feet).Length < 0.05)
                return l;
        return null;
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
