using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The train in a Waker's hands, as drawn (docs/design/creatures/wakers.md §5; ARCHITECTURE §8 notes 588, 902; the director's
/// playtest of 16 Oct 2026, line 29: "We want to see the waker lift the train off the ground, sort of like stop behind the
/// train and like scoop it up with its hands and pick it up like it's like a two-handed hot dog and just start eating the
/// cars"). He kneels on the line <see cref="WakersTuning.CatchShort"/> behind the car he's taking, gets both hands under it
/// where it stands on the rails, tears it off its coupling and lifts it to his mouth, its end to his jaws and its far end
/// tipped down, and takes it in three bites, sliding it on into his mouth as each comes off; then his hands go down to the
/// next, and he shuffles on after it on his knees. Only the car in his hands leaves the rails: the rest of the train stands
/// where it is. The timing is the sim's (<see cref="Waker.Raised"/>, <see cref="Waker.Reached"/>, <see cref="Waker.Bitten"/>).
/// Presentation only, as <see cref="TippleTilt"/>: the sim's frames never leave the rails, so whoever's in the car stays
/// stood where they were in it and their eyes (drawn from these frames) go up with it. Every machine draws it alike from the
/// Waker's replicated state.
/// </summary>
public static class WakerLift
{
    /// <summary>Where he holds the car he's eating, from where he kneels (m): its bitten end this far out in front of him, and this high over its rails.</summary>
    public const double HoldOut = 13, HoldUp = 23;

    /// <summary>How far its far end dips, held (radians): a hot dog to his mouth, not a plank.</summary>
    public const double HoldPitch = 0.21;

    /// <summary>How high up a car's end (m over its floor's frame) he takes it in his mouth: the middle of its body.</summary>
    public const double MouthHeight = 2.6;

    /// <summary>How far over the car his mouth bows while his hands are down on it at the rails (m).</summary>
    public const double BowOver = 10;

    /// <summary>How long a bite's shove into his mouth takes (of the car's turn).</summary>
    const double Shove = 0.06;

    /// <summary>
    /// The car he has, this frame.
    /// </summary>
    /// <param name="Car">Its frame's index (its vehicle id).</param>
    /// <param name="FromRear">Which it is from the rear (0 the last car).</param>
    /// <param name="Raised">How far it's up off the rails to his mouth (0..1, eased).</param>
    /// <param name="Reached">How far his hands are down to it from the last one's stub at his mouth (0..1, eased).</param>
    /// <param name="Bitten">The bites taken of it, and how many it takes (the last eats it).</param>
    /// <param name="Gone">How far into it (m from its rear end) it's bitten off, as drawn (sliding on into his mouth with each bite).</param>
    /// <param name="Drawn">Where it's drawn: on the rails, in his hands, or between.</param>
    /// <param name="Stand">Where he kneels: <see cref="WakersTuning.CatchShort"/> behind it on the line (between the last car's place and this one's, while he shuffles on).</param>
    /// <param name="Mouth">Where his mouth goes: onto its bitten end, or bowed over it while his hands are down on it.</param>
    /// <param name="Right">His right hand's place on it (its right side, nearer him), as drawn; <paramref name="Left"/> his left (its left side, further on).</param>
    public readonly record struct Hold(int Car, int FromRear, double Raised, double Reached, int Bitten, int Bites, double Gone,
        CarFrame Drawn, Double3 Stand, Double3 Mouth, Double3 Right, Double3 Left);

    /// <summary>Lifts the held car of <paramref name="frames"/> (the train's, by vehicle id) in place, and drops the eaten ones out of sight.</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, World world) =>
        Apply(frames, train, world.ActiveEnemies, world.Enemies?.Wakers);

    /// <summary>The same, from <paramref name="enemies"/> (a staged scene's).</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, IEnumerable<Enemy> enemies, WakersTuning? t)
    {
        if (t is null || Holding(enemies) is not { } waker)
            return;
        var cars = train.Dynamics.Consist.Vehicles;
        int eaten = waker.EatenCars(t, cars.Count);
        // (Reckoned off the frames as they come in, on the rails, before any is moved.)
        var hold = Of(frames, train, waker, t);
        for (int k = 0; k < eaten; k++)
        {
            int id = cars[cars.Count - 1 - k].Id;
            // In his mouth: out of sight.
            if (id >= 0 && id < frames.Count)
                frames[id] = frames[id] with { Origin = frames[id].Origin - Double3.Up * 500 };
        }
        if (hold is { } h)
            frames[h.Car] = h.Drawn;
    }

    /// <summary>The lead Waker holding the train, if one is.</summary>
    public static Waker? Holding(IEnumerable<Enemy> enemies) => enemies.OfType<Waker>().FirstOrDefault(w => w.Lead && w.Holds && !w.Gone);

    /// <summary>
    /// The car <paramref name="waker"/> has, reckoned off <paramref name="rails"/> (the train's frames on the rails, by vehicle
    /// id); null once he's eaten them all, or with nothing held.
    /// </summary>
    public static Hold? Of(IReadOnlyList<CarFrame> rails, TrainOnLine train, Waker waker, WakersTuning t)
    {
        var cars = train.Dynamics.Consist.Vehicles;
        if (!waker.Holds || cars.Count == 0)
            return null;
        double extra = waker.Extra;
        int k = waker.EatenCars(t, cars.Count);
        if (k >= cars.Count)
            return null;
        int id = cars[cars.Count - 1 - k].Id;
        if (id < 0 || id >= rails.Count)
            return null;
        var rail = rails[id];
        double raised = Ease(Waker.Raised(extra, k, t)), reached = Ease(Waker.Reached(extra, k, t));
        int bitten = Waker.Bitten(extra, k, t), bites = Waker.BitesACar(t);
        double length = RearZ(rail.Shape) + rail.Shape.HalfLength;
        // Each bite shoved on into his mouth as it comes off.
        double gone = length * bitten / bites;
        if (bitten > 0)
            gone -= length / bites * (1 - Ease(Math.Clamp((Waker.Through(extra, k, t) - t.Bites[bitten - 1]) / Shove, 0, 1)));
        var stand = StandBehind(rail, t);
        // His hands down from the last car's stub at his mouth to this one, shuffling on after it on his knees.
        Hold? last = null;
        if (k > 0 && reached < 1 && cars[cars.Count - k].Id is var was && was >= 0 && was < rails.Count)
        {
            var before = rails[was];
            var stood = StandBehind(before, t);
            double all = RearZ(before.Shape) + before.Shape.HalfLength;
            var stub = Held(before, stood, 1, all);
            last = new Hold(was, k - 1, 1, 1, bites, bites, all, stub, stood, Bitten(stub, all), Grip(stub, 1, -1), Grip(stub, -1, -1));
            stand = stood + (stand - stood) * reached;
        }
        var held = Held(rail, stand, raised, gone);
        var drawn = Between(rail, held, raised);
        var mouth = Bitten(drawn, gone) + Double3.Up * (BowOver * (1 - raised));
        var right = Grip(drawn, 1, gone);
        var left = Grip(drawn, -1, gone);
        if (last is { } l)
        {
            mouth = l.Mouth + (mouth - l.Mouth) * reached;
            right = l.Right + (right - l.Right) * reached;
            left = l.Left + (left - l.Left) * reached;
        }
        return new Hold(id, k, raised, reached, bitten, bites, gone, drawn, stand, mouth, right, left);
    }

    /// <summary>Where he kneels for a car standing at <paramref name="rail"/>: <see cref="WakersTuning.CatchShort"/> behind its rear end, on the line.</summary>
    static Double3 StandBehind(in CarFrame rail, WakersTuning t)
    {
        var back = Flat(rail.Back);
        return rail.ToWorld(new Double3(0, 0, RearZ(rail.Shape))) + back * t.CatchShort;
    }

    /// <summary>
    /// The car at <paramref name="rail"/> in his hands, as he kneels at <paramref name="stand"/>: its end <paramref name="gone"/>
    /// m in (where it's bitten to) at <see cref="HoldOut"/> in front of him and <see cref="HoldUp"/> over the rails, its far
    /// end tipped down <see cref="HoldPitch"/>, still pointing up the line the way it stood.
    /// </summary>
    static CarFrame Held(in CarFrame rail, Double3 stand, double raised, double gone)
    {
        var ahead = Flat(rail.Back) * -1;
        var at = stand + ahead * HoldOut + rail.Up * HoldUp;
        double c = Math.Cos(HoldPitch), s = Math.Sin(HoldPitch);
        var back = (rail.Back * c + rail.Up * s).Normalized;
        var up = (rail.Up * c - rail.Back * s).Normalized;
        var f = rail with { Back = back, Up = up };
        var end = f.DirToWorld(new Double3(0, MouthHeight, RearZ(rail.Shape) - gone));
        return f with { Origin = at - end };
    }

    /// <summary>The car <paramref name="raised"/> (eased) of the way from the rails to his hands, turned between.</summary>
    static CarFrame Between(in CarFrame rail, in CarFrame held, double raised)
    {
        if (raised <= 0)
            return rail;
        if (raised >= 1)
            return held;
        var back = (rail.Back + (held.Back - rail.Back) * raised).Normalized;
        var right = rail.Right;
        var up = Double3.Cross(back, right).Normalized;
        if (Double3.Dot(up, rail.Up) < 0)
            up = up * -1;
        // Up off the rails first (out of the couplings), then in to his mouth.
        double rise = Math.Clamp(raised / 0.6, 0, 1);
        var origin = rail.Origin + (held.Origin - rail.Origin) with { Y = 0 } * Ease(Math.Clamp((raised - 0.25) / 0.75, 0, 1))
            + Double3.Up * ((held.Origin.Y - rail.Origin.Y) * Ease(rise));
        return rail with { Origin = origin, Back = back, Up = up };
    }

    /// <summary>Where the car's bitten end is (its mouth's place on it): <paramref name="gone"/> m in from its rear end.</summary>
    static Double3 Bitten(in CarFrame f, double gone = 0) => f.ToWorld(new Double3(0, MouthHeight, RearZ(f.Shape) - gone));

    /// <summary>
    /// A hand's place on the car (<paramref name="side"/> +1 its right side, −1 its left), low on its side under the body: the
    /// right a third of the way on from the bitten end, the left two thirds, on what's left of it (<paramref name="gone"/>
    /// m bitten off; negative, all of it but a stub at the end).
    /// </summary>
    static Double3 Grip(in CarFrame f, int side, double gone)
    {
        double rear = RearZ(f.Shape), front = -f.Shape.HalfLength;
        double from = gone < 0 ? front + 1.5 : rear - gone;
        double z = from + (front - from) * (side > 0 ? 0.3 : 0.65);
        return f.ToWorld(new Double3(side * (f.Shape.HalfWidth + 0.2), 0.8, z));
    }

    static double RearZ(CarShape shape) => shape.Platform is { } p ? p.Max.Z : shape.HalfLength;

    static Double3 Flat(Double3 v) => (v with { Y = 0 }).Length > 1e-6 ? (v with { Y = 0 }).Normalized : new Double3(0, 0, 1);

    static double Ease(double p) => p * p * (3 - 2 * p);
}
