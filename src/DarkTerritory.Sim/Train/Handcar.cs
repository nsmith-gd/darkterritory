using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// The handcar (GDD §24.1; the director, 14 Oct 2026, App. F.6; ARCHITECTURE §8 note 729): a two-person pump trolley hung on
/// the back of the guard van, set on the rails at a stand and pumped down the line. Field docs live in train.json
/// <c>handcar</c>.
/// </summary>
public sealed record HandcarTuning
{
    /// <summary>Every night has one, bought or not (for the tests and the views); the fortress sells it otherwise (campaign.json).</summary>
    public bool Always { get; init; }
    public double Length { get; init; } = 3.0;
    public double Width { get; init; } = 1.7;
    public double DeckHeight { get; init; } = 0.55;
    /// <summary>A step on each side at this height, half way up to the deck, so a crewmate walks aboard (player.json stepUp).</summary>
    public double StepHeight { get; init; } = 0.28;
    public double MassTonnes { get; init; } = 0.5;
    /// <summary>Metres run for one full swing of the handle, down and up (the gearing: it goes round faster the faster it runs).</summary>
    public double StrokeMetres { get; init; } = 5.0;
    /// <summary>A pumper's push at a walk, as an acceleration of the car (m/s²): what one stroke in time does from a stand.</summary>
    public double PumpAccel { get; init; } = 0.9;
    /// <summary>A pumper's power, per tonne of car (m²/s³): their push falls off as the car runs faster than PumpPower / PumpAccel.</summary>
    public double PumpPower { get; init; } = 1.8;
    /// <summary>How many hands the handle has room for: more at it than this add nothing.</summary>
    public int Pumpers { get; init; } = 2;
    /// <summary>A stroke's least effect, at the top and bottom of the swing (where the lever has no purchase), as a share of its best.</summary>
    public double Floor { get; init; } = 0.3;
    /// <summary>Rolling resistance (m/s²) and air (m/s² per (m/s)²).</summary>
    public double Rolling { get; init; } = 0.04;
    public double Air { get; init; } = 0.005;
    /// <summary>The fastest it runs (m/s), down a long bank with nobody on the brake.</summary>
    public double MaxSpeed { get; init; } = 14;
    /// <summary>The brake lever hard on (m/s²).</summary>
    public double BrakeDecel { get; init; } = 3.0;
    /// <summary>With nobody aboard, its brake's latched on: left standing, it stays where it was left.</summary>
    public bool Parked { get; init; } = true;
    /// <summary>Seconds of held Use by two lifters to set it on the rails or put it back; one alone takes SoloLiftScale times as long.</summary>
    public double LiftSeconds { get; init; } = 4;
    public double SoloLiftScale { get; init; } = 3;
    /// <summary>How near it a lifter stands (m, across the ground).</summary>
    public double LiftReach { get; init; } = 2.5;
    /// <summary>Set on the rails this far behind the van's rear platform (m).</summary>
    public double SetBehind { get; init; } = 1.5;
    /// <summary>The train (and the handcar, putting it back) standing: slower than this (m/s).</summary>
    public double StandBelow { get; init; } = 0.3;
    /// <summary>Put back only from within this of the van's back (m, its middle to the platform's end).</summary>
    public double StowReach { get; init; } = 6;
    /// <summary>How near an end of the handle a rider stands to take it (m).</summary>
    public double HandleReach { get; init; } = 1.0;
}

/// <summary>
/// The handcar's own state (note 729), beside its vehicle and rake in <see cref="TrainOnLine"/>: the host's, replicated, and
/// what every machine runs the trolley's step from.
/// </summary>
public sealed class Handcar(int id)
{
    /// <summary>Its vehicle id: past the train's own (<see cref="TrainOnLine.OwnVehicles"/>), before any yard's standing cars.</summary>
    public int Id { get; } = id;
    /// <summary>Hung on the back of the guard van (true), or on the rails as a rake of its own.</summary>
    public bool Carried { get; set; } = true;
    /// <summary>The handle's swing, radians, 0 to 2π: geared to the wheels, it goes round as the car runs (<see cref="HandcarTuning.StrokeMetres"/>).</summary>
    public double Phase { get; set; }
    /// <summary>The pumpers' push this tick (m/s², signed along the line): the host's, from their strokes.</summary>
    public double Drive { get; set; }
    /// <summary>The brake lever, 0 off to 1 hard on.</summary>
    public double Brake { get; set; }
    /// <summary>How far a lift is under way, 0 to 1: it's set on the rails (or put back) at 1.</summary>
    public double Lift { get; set; }
    /// <summary>How many are at the handle this tick (presentation, and the HUD's "pumping with").</summary>
    public int Pumpers { get; set; }

    /// <summary>Which end of the handle a place on the car is at: −1 the front's (−Z), +1 the back's.</summary>
    public static int EndAt(double localZ) => localZ < 0 ? -1 : 1;

    /// <summary>Where a pumper stands at an end of the handle, in the car's frame (feet on the deck, facing the handle).</summary>
    public static Double3 Place(HandcarTuning t, int end) => new(0, t.DeckHeight, end * (t.Length / 2 - 0.45));

    /// <summary>
    /// How high an end of the handle is, −1 (down) to 1 (up), at a swing: the front end's at the top at 0. The two ends are a
    /// see-saw, one up as the other's down.
    /// </summary>
    public static double EndHeight(double phase, int end) => end < 0 ? DMath.Cos(phase) : -DMath.Cos(phase);

    /// <summary>
    /// What a stroke does at this swing (note 729): pushing your end down (<paramref name="stroke"/> +1, W) or pulling it up
    /// (−1, S) drives the car one way or the other, as hard as the lever has purchase there, least at the top and bottom of the
    /// swing (<see cref="HandcarTuning.Floor"/>, never nothing, so a car stopped at dead centre still starts). Positive is up
    /// the line. Geared to the wheels, a push on your end while it's coming down (a pull while it's going up) always drives the
    /// car on the way it's going: in time with the handle. Against it, it brakes, or fights the other pumper.
    /// </summary>
    public static double Effort(HandcarTuning t, double phase, int end, double stroke)
    {
        double s = DMath.Sin(phase);
        double purchase = Math.Max(Math.Clamp(t.Floor, 0, 1), Math.Min(1, 2 * Math.Abs(s)));
        // The front end goes down (cos falling) as the swing goes forward through 0..π; the back end the other way.
        double sign = s >= 0 ? 1 : -1;
        return Math.Clamp(stroke, -1, 1) * -end * sign * purchase;
    }

    /// <summary>The stroke that's in time to drive the car <paramref name="way"/> (+1 up the line, −1 down it) from an end: what a bot pumps.</summary>
    public static double InTime(double phase, int end, int way) => DMath.Sin(phase) >= 0 ? -end * Math.Sign(way) : end * Math.Sign(way);

    /// <summary>
    /// The car's acceleration from the pumpers' efforts (each <see cref="Effort"/>) at <paramref name="speed"/>: each pushes at
    /// most <see cref="HandcarTuning.PumpAccel"/>, and no more than their power makes at the speed. More hands than the handle
    /// has room for share its places.
    /// </summary>
    public static double DriveFrom(HandcarTuning t, double speed, IReadOnlyList<double> efforts)
    {
        if (efforts.Count == 0)
            return 0;
        double sum = 0;
        foreach (double e in efforts)
            sum += e;
        if (efforts.Count > Math.Max(1, t.Pumpers))
            sum *= Math.Max(1, t.Pumpers) / (double)efforts.Count;
        double each = Math.Min(t.PumpAccel, t.PumpPower / Math.Max(1e-3, Math.Abs(speed)));
        return sum * each;
    }

    /// <summary>
    /// One tick of the trolley on the rails (note 729): the pumpers' drive, the grade (uphill hard, downhill a runaway), rolling
    /// and air, and the brake, which holds it and can't send it back. Returns the new velocity (m/s along the line).
    /// </summary>
    public static double Step(HandcarTuning t, double velocity, double drive, double brake, double gradePercent, double gravity, double drag, double dt)
    {
        double a = drive - gravity * DMath.Sin(DMath.Atan(gradePercent / 100.0));
        double hold = Math.Clamp(brake, 0, 1) * t.BrakeDecel + t.Rolling + t.Air * velocity * velocity + Math.Max(0, drag);
        double v = velocity;
        if (v != 0)
        {
            double dir = Math.Sign(v);
            double next = v + (a - dir * hold) * dt;
            v = Math.Sign(next) != dir && Math.Abs(a) <= hold ? 0 : next;
        }
        else if (Math.Abs(a) > hold)
            v += (a - Math.Sign(a) * hold) * dt;
        return Math.Clamp(v, -t.MaxSpeed, t.MaxSpeed);
    }

    /// <summary>The swing on, for a run of <paramref name="metres"/> (signed): the handle's geared to the wheels.</summary>
    public static double Swing(HandcarTuning t, double phase, double metres)
    {
        double p = phase + metres / Math.Max(0.1, t.StrokeMetres) * DMath.Tau;
        p %= DMath.Tau;
        return p < 0 ? p + DMath.Tau : p;
    }

    /// <summary>
    /// Its collision on the rails (note 729): a deck at <see cref="HandcarTuning.DeckHeight"/>, wider than the gauge, and a step
    /// half way up on each side. The handle's post and the see-saw are drawn, not stood on. Hung on the van it has none: it's out
    /// behind the platform's rail, where nobody walks.
    /// </summary>
    public static CarShape Shape(HandcarTuning t, bool onRails)
    {
        double w = t.Width / 2, l = t.Length / 2, h = t.DeckHeight;
        var bounds = new Box(new Double3(-w, 0, -l), new Double3(w, h + 1.1, l));
        if (!onRails)
            return new CarShape(bounds, [], [], [], null);
        var solids = new List<Solid>
        {
            new(new Box(new Double3(-w, h - 0.12, -l), new Double3(w, h, l)), SurfaceKind.Deck, PartKind.Chassis),
            new(new Box(new Double3(-w - 0.3, t.StepHeight - 0.05, -0.4), new Double3(-w, t.StepHeight, 0.4)), SurfaceKind.Deck, PartKind.Chassis),
            new(new Box(new Double3(w, t.StepHeight - 0.05, -0.4), new Double3(w + 0.3, t.StepHeight, 0.4)), SurfaceKind.Deck, PartKind.Chassis),
        };
        return new CarShape(bounds, solids, [], [], null);
    }

    /// <summary>
    /// Where it hangs on the van (note 729): stood on its end behind the rear platform's rail, its deck facing out, its wheels
    /// clear of the ground. Its frame from the van's: right as the van's, its up the van's back, so its rear end is down.
    /// </summary>
    public static CarFrame Hung(HandcarTuning t, GeometryTuning g, in CarFrame van, int id)
    {
        double back = van.Shape.Platform is { } p ? p.Max.Z : van.Shape.HalfLength;
        var origin = van.ToWorld(new Double3(0, g.CouplerHeight - 0.4 + t.Length / 2, back + 0.6));
        var right = van.Right;
        var up = van.Back;
        var backAxis = Double3.Cross(right, up);
        // The frame's origin is its rail-height centre: hung up, the deck's middle stands DeckHeight out from it.
        return new CarFrame(id, origin - up * t.DeckHeight, right, up, backAxis, van.Velocity, Shape(t, onRails: false));
    }
}
