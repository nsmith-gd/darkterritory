using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// What an odd outcome did (queue #464, ARCHITECTURE §8 note 730): the town bell rung, a townsperson waving back, Dave's
/// painting given, a find dropped down a well and its splash, the coin that comes back up, a wash at the well, a tin can
/// kicked and what answers it from the dark, a scarecrow found turned, a letter read from a postbox, a crewmate sooted
/// under the coaling chute, and a lurch on a switch's blades as it's thrown.
/// </summary>
public enum OddKind : byte { Bell, Wave, Painting, Drop, Splash, Coin, Wash, Kick, Answer, Turned, Letter, Sooted, Lurch }

/// <summary>
/// An odd outcome as the crew see and hear it (note 730): unique for the night (its record's key), the host tick it happened,
/// who did it (−1: nobody), the thing it happened at (an index into <see cref="Oddities.Things"/>, a townsperson's id for a
/// wave, a letter's number; −1: none) and where. The host's, kept a few seconds and replicated, as a lamp signal is.
/// </summary>
public readonly record struct OddEvent(int Id, uint Tick, OddKind Kind, int By, int Thing, Double3 At);

/// <summary>The odd things put out at the stops (note 730): a village's well, a tin can in its street, a scarecrow, a postbox.</summary>
public enum OddThingKind : byte { Well, Can, Scarecrow, Postbox }

/// <summary>
/// One odd thing at a stop (note 730): its index, kind, the route feature it's at, where it stands at nightfall (on the
/// ground) and which way it faces (level, a unit vector). Placed from the stop's own seed, the same on every machine.
/// </summary>
public sealed record OddThing(int Index, OddThingKind Kind, int Feature, Double3 At, Double3 Facing);

/// <summary>
/// The odd things in the towns and at the stops (queue #464, ARCHITECTURE §8 note 730; the director, 14 Oct 2026, App. F.6:
/// "things that answer when poked, never in the Wiki"; GDD §3). Where they stand is worked out on every machine alike from the
/// route (a village's wells as its layout has them, and on a stream of each stop's seed of its own, so nothing else at the
/// stop moves: a tin can in a street, a scarecrow at a farmstead, a postbox at a halt). What has happened to them (a can
/// kicked along, a scarecrow turned, a postbox emptied, the night's painting and coin given) is the host's, mirrored to
/// every client from one record (<see cref="Net.WorldRecords"/>). The rules that act on them are <see cref="World"/>'s.
/// </summary>
public sealed class Oddities
{
    const ulong Stream = 0x4f444453; // "ODDS"

    readonly Double3[] _at;
    readonly bool[] _done;

    Oddities(OddityTuning tuning, IReadOnlyList<OddThing> things, ulong seed)
    {
        Tuning = tuning;
        Things = things;
        Seed = seed;
        _at = [.. things.Select(t => t.At)];
        _done = new bool[things.Count];
    }

    public OddityTuning Tuning { get; }
    public IReadOnlyList<OddThing> Things { get; }
    /// <summary>The route's seed: what each machine draws the night's letters and the cans' kicks from.</summary>
    public ulong Seed { get; }

    /// <summary>Where a thing is now: a can where it was last kicked to, anything else where it stood.</summary>
    public Double3 At(int thing) => _at[thing];

    /// <summary>A scarecrow turned to face the line, or a postbox's letter taken.</summary>
    public bool Done(int thing) => _done[thing];

    /// <summary>Dave's painting given tonight (one a night, <c>oddities.painting</c>).</summary>
    public bool PaintingGiven { get; set; }

    /// <summary>The coin come back up a well tonight (once a night, <c>oddities.well.coin</c>).</summary>
    public bool CoinGiven { get; set; }

    /// <summary>The host's: a can kicked to <paramref name="to"/>.</summary>
    public void Move(int thing, Double3 to) => _at[thing] = to;

    /// <summary>The host's: a scarecrow turned, a postbox emptied.</summary>
    public void Finish(int thing) => _done[thing] = true;

    /// <summary>
    /// Which way a thing faces now: a scarecrow found turned faces the main line where it's nearest (the train, as it comes
    /// back past); anything else as it stood.
    /// </summary>
    public Double3 Facing(int thing, RailLine line)
    {
        var t = Things[thing];
        if (t.Kind != OddThingKind.Scarecrow || !_done[thing])
            return t.Facing;
        double hint = 0;
        var (_, along) = line.Nearest(t.At, ref hint);
        var to = (line.Sample(along).Position - t.At) with { Y = 0 };
        return to.Length > 1e-6 ? to.Normalized : t.Facing * -1;
    }

    /// <summary>The letter in a postbox (<c>oddities.postbox.letters</c>), by the night's seed and the box: who it's to, what it says.</summary>
    public OddLetter? LetterIn(int thing)
    {
        var letters = Tuning.Postbox.Letters;
        if (letters.Count == 0 || Things[thing].Kind != OddThingKind.Postbox)
            return null;
        var rng = new Pcg32(StopSeed.Of(Seed, Stream ^ 0x4c, (ulong)thing));
        return letters[(int)(rng.NextDouble() * letters.Count)];
    }

    /// <summary>The state for the wire: the night's flags, then each thing's done bit and, a can's, where it lies.</summary>
    public long[] Capture(Func<double, long> pos)
    {
        var f = new List<long> { (PaintingGiven ? 1 : 0) | (CoinGiven ? 2 : 0), Things.Count };
        for (int i = 0; i < Things.Count; i++)
        {
            f.Add(_done[i] ? 1 : 0);
            if (Things[i].Kind == OddThingKind.Can)
                f.AddRange([pos(_at[i].X), pos(_at[i].Y), pos(_at[i].Z)]);
        }
        return [.. f];
    }

    /// <summary>A client's copy of the host's state (<see cref="Capture"/>'s fields); ignored if it isn't this night's things.</summary>
    public void Mirror(long[] f, Func<long, double> pos)
    {
        if (f.Length < 2 || f[1] != Things.Count)
            return;
        PaintingGiven = (f[0] & 1) != 0;
        CoinGiven = (f[0] & 2) != 0;
        int k = 2;
        for (int i = 0; i < Things.Count && k < f.Length; i++)
        {
            _done[i] = f[k++] != 0;
            if (Things[i].Kind == OddThingKind.Can && k + 2 < f.Length)
            {
                _at[i] = new Double3(pos(f[k]), pos(f[k + 1]), pos(f[k + 2]));
                k += 3;
            }
        }
    }

    /// <summary>
    /// The night's odd things (note 730), from the route alone: at every stop with a village, its wells; on a stream of the
    /// stop's seed of their own, a tin can lying in one of its streets, a scarecrow at a farmstead, and a postbox at a halt's
    /// platform, each by its <c>oddities</c> chance. Null with <c>oddities.enabled</c> off.
    /// </summary>
    public static Oddities? Of(OddityTuning t, Route.Route route, RailLine line)
    {
        if (!t.Enabled)
            return null;
        var things = new List<OddThing>();
        for (int i = 0; i < route.Features.Count; i++)
        {
            var f = route.Features[i];
            if (f.Stop is not { HasVillage: true } stop)
                continue;
            var rng = new Pcg32(StopSeed.Of(stop.Seed, Stream));
            // Every draw is made whatever's switched off, so one thing's chance never moves another.
            bool can = rng.Chance(t.Can.Chance), crow = rng.Chance(t.Scarecrow.Chance), box = rng.Chance(t.Postbox.Chance);
            double pickCan = rng.NextDouble(), crowS = rng.NextDouble(), crowD = rng.NextDouble(), boxEnd = rng.NextDouble();
            var side = ToWorld(line, f, new Pt(0, 1)) - ToWorld(line, f, new Pt(0, 0));
            foreach (var b in stop.Buildings.Where(b => b.Kind == BuildingKind.Well))
                things.Add(new OddThing(things.Count, OddThingKind.Well, i, Ground(line, f, b.Centre), side.Normalized));
            if (can && CanSpot(stop, t.Can, pickCan) is { } c)
                things.Add(new OddThing(things.Count, OddThingKind.Can, i, Ground(line, f, c), side.Normalized));
            if (crow && stop.VillageForm == VillageForm.Farmsteads && ScarecrowSpot(stop, crowS, crowD) is { } s)
                // It stands looking out over its fields, its back to the line.
                things.Add(new OddThing(things.Count, OddThingKind.Scarecrow, i, Ground(line, f, s), (side * stop.VillageSide).Normalized));
            if (box && PostboxSpot(stop, boxEnd) is { } p)
                // Its slot to the line.
                things.Add(new OddThing(things.Count, OddThingKind.Postbox, i, Ground(line, f, p), (side * -Math.Sign(p.D)).Normalized));
        }
        return new Oddities(t, things, route.Seed);
    }

    static Double3 ToWorld(RailLine line, Route.RouteFeature f, Pt p) => Run.StopWorld(line, f, p);

    static Double3 Ground(RailLine line, Route.RouteFeature f, Pt p)
    {
        var at = Run.StopWorld(line, f, p);
        double hint = f.Start + p.S;
        return at with { Y = Player.PlayerMotor.GroundAt(at, line, ref hint) };
    }

    /// <summary>Whether a point's clear of the stop's buildings by <paramref name="pad"/> and off the main line's ballast.</summary>
    static bool Clear(StopLayout stop, Pt p, double pad) =>
        Math.Abs(p.D) > ClearOfLine && !stop.Buildings.Any(b => Plan.Inside(p, b, pad));

    /// <summary>How far from the main line's middle an odd thing stands at least (m): off its ballast and the train's side. Not a design number.</summary>
    const double ClearOfLine = 5;

    /// <summary>The village's houses: where its people lived, what its odd things stand among.</summary>
    static IEnumerable<StopBuilding> Houses(StopLayout stop) => stop.Buildings.Where(b => b.Zone == StopZone.Village && b.Kind == BuildingKind.House);

    /// <summary>A point on one of the village's roads within <c>can.nearHouseM</c> of one of its houses, clear of them all.</summary>
    static Pt? CanSpot(StopLayout stop, CanTuning t, double pick)
    {
        var houses = Houses(stop).ToList();
        var spots = stop.Roads.SelectMany(r => Plan.Samples(r.Points, 3))
            .Where(p => houses.Any(h => Plan.Distance(p, h) <= t.NearHouseM) && Clear(stop, p, 1.5))
            .ToList();
        return spots.Count == 0 ? null : spots[Math.Min(spots.Count - 1, (int)(pick * spots.Count))];
    }

    /// <summary>A farmstead's scarecrow: between the line and its houses, abreast of them, in the open, off the roads.</summary>
    static Pt? ScarecrowSpot(StopLayout stop, double s, double d)
    {
        var houses = Houses(stop).ToList();
        double mid = houses.Count > 0 ? houses.Average(h => h.S) : stop.StopPoint.S;
        double far = Math.Max(ClearOfLine + 4, houses.Count > 0 ? houses.Min(h => Math.Abs(h.D)) - 6 : Math.Abs(stop.VillageOffset) * 0.6);
        for (int tries = 0; tries < 16; tries++)
        {
            // Each try further round the first draw, so the same stop finds the same place.
            double ss = (s + tries * 0.37) % 1, dd = (d + tries * 0.61) % 1;
            var p = new Pt(mid + (ss - 0.5) * 50, stop.VillageSide * (ClearOfLine + 3 + dd * Math.Max(1, far - ClearOfLine - 3)));
            if (Clear(stop, p, 3) && !stop.Roads.Any(r => Plan.PolylineDistance(p, r.Points) < 3))
                return p;
        }
        return null;
    }

    /// <summary>
    /// A postbox: at a halt, just past one end of its platform on its far side from the line; else where the village's road
    /// comes in nearest the line, beside it.
    /// </summary>
    static Pt? PostboxSpot(StopLayout stop, double end)
    {
        if (stop.Halt is { } halt)
        {
            int side = halt.D < 0 ? -1 : 1;
            foreach (int way in end < 0.5 ? new[] { 1, -1 } : [-1, 1])
            {
                var p = new Pt(halt.S + way * (stop.HaltLength / 2 + 1.2), side * (Math.Abs(halt.D) + 1.6));
                if (Clear(stop, p, 0.8))
                    return p;
            }
        }
        var houses = Houses(stop).ToList();
        if (houses.Count == 0)
            return null;
        // The road samples on the village's side, nearest the line first, a step off the road's middle.
        foreach (var p in stop.Roads.SelectMany(r => Plan.Samples(r.Points, 2))
                     .Where(p => Math.Sign(p.D) == stop.VillageSide && houses.Any(h => Plan.Distance(p, h) <= 30))
                     .OrderBy(p => Math.Abs(p.D)).ThenBy(p => p.S))
        {
            var beside = p + new Pt(end < 0.5 ? 2.2 : -2.2, 0);
            if (Clear(stop, beside, 0.8) && Math.Abs(beside.D) > ClearOfLine + 2)
                return beside;
        }
        return null;
    }
}

/// <summary>A letter in a postbox (note 730): who it's to, and what it says.</summary>
public sealed record OddLetter(string To = "", string Text = "");

/// <summary>Mirror of run.json <c>oddities</c> (queue #464, note 730). Field docs live in that file.</summary>
public sealed record OddityTuning
{
    public bool Enabled { get; init; } = true;
    /// <summary>How long a happening stays on the wire (s), for a dropped snapshot not to lose it.</summary>
    public double KeepSeconds { get; init; } = 4;
    public TownBellTuning Bell { get; init; } = new();
    public WaveTuning Wave { get; init; } = new();
    public PaintingTuning Painting { get; init; } = new();
    public WellTuning Well { get; init; } = new();
    public CanTuning Can { get; init; } = new();
    public ScarecrowTuning Scarecrow { get; init; } = new();
    public PostboxTuning Postbox { get; init; } = new();
    public ChuteSootTuning Chute { get; init; } = new();
    public LurchTuning Lurch { get; init; } = new();
}

public sealed record TownBellTuning(double HoldSeconds = 1.2, double Reach = 1.3, double EarshotM = 90, double LookSeconds = 6, double EverySeconds = 20);
public sealed record WaveTuning(double ReachM = 12, double ConeDegrees = 30, double Seconds = 2.5);
public sealed record PaintingTuning(double HoldSeconds = 10, double Reach = 3, string Item = "davePainting", double PerCar = 0.08);
public sealed record WellTuning(double Reach = 1.6, double SplashSeconds = 2.8, double CoinSeconds = 6, string CoinItem = "oldCoin", double CoinPerCar = 0.04,
    double WashSeconds = 2);
public sealed record CanTuning(double Chance = 0.6, double NearHouseM = 25, double Reach = 0.5, double KickSpeed = 2, double[]? KickM = null,
    double[]? AnswerSeconds = null, double AnswerM = 90)
{
    public double[] Kick => KickM is { Length: 2 } k ? k : [3, 6];
    public double[] Answer => AnswerSeconds is { Length: 2 } a ? a : [1.2, 2.4];
}
public sealed record ScarecrowTuning(double Chance = 0.8, double NearM = 15, double TurnBeyondM = 30, double LookDegrees = 60);
public sealed record PostboxTuning
{
    public double Chance { get; init; } = 0.5;
    public double HoldSeconds { get; init; } = 1.5;
    public double Reach { get; init; } = 1.3;
    public IReadOnlyList<OddLetter> Letters { get; init; } = [];
}
public sealed record ChuteSootTuning(double Reach = 1.8);
public sealed record LurchTuning(double BladesHalfWidth = 1.0, double Speed = 1.6, double Lift = 1.4, double StaggerSeconds = 0.5);
