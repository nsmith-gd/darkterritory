using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The cars leaning out on a bend taken too fast (ARCHITECTURE §8 note 370; App. F.1's overspeed telegraph: "the train
/// should show it's taking a bend too fast before it comes off: the cars straining and leaning, flanges grinding,
/// sparks"). Each car rolls about its outer rail by its own strain on the bend under it (<see cref="BendStrain"/>), so its
/// inner wheels come up off the rail: nothing at the board's speed, train.json <c>overspeed.leanDegrees</c> at the
/// derailing one, and near that a rocking on its springs. Presentation only: the frames drawn lean (the car, whoever's on
/// it, the eye riding it, the replay that records them), the sim's never do, so nothing is sent and nothing is predicted;
/// and once the train's off the rails the lean's gone, the wreck's own frames drawn as they are (note 330).
/// </summary>
public sealed class CarLean
{
    double[] _lean = [];
    double _last = double.NaN;

    /// <summary>
    /// Leans <paramref name="frames"/> (the train's, as drawn this frame) in place, eased from how they leaned the last
    /// time (<paramref name="seconds"/> is the presentation's clock). Without a line plan to know the bends by, none.
    /// </summary>
    public void Apply(List<CarFrame> frames, TrainOnLine train, PlanRules? rules, double seconds) =>
        Apply(frames, train, rules is null ? null : BendStrain.PerCar(train, rules), seconds);

    /// <summary>The same, by each car's strain (<see cref="BendStrain.PerCar"/>; null: none).</summary>
    public void Apply(List<CarFrame> frames, TrainOnLine train, IReadOnlyList<(float Stress, int Outer)>? strain, double seconds)
    {
        var t = train.Dynamics.Tuning.Overspeed;
        if (_lean.Length != frames.Count)
            _lean = new double[frames.Count];
        double dt = double.IsNaN(_last) ? 0 : Math.Clamp(seconds - _last, 0, 0.25);
        _last = seconds;
        if (strain is null || t.LeanDegrees <= 0 || train.Wreck is not null)
        {
            Array.Clear(_lean);
            return;
        }
        double ease = t.LeanSeconds > 0 ? 1 - Math.Exp(-dt / t.LeanSeconds) : 1;
        for (int i = 0; i < frames.Count; i++)
        {
            var (stress, outer) = i < strain.Count ? strain[i] : default;
            _lean[i] += (outer * Angle(stress, t) - _lean[i]) * ease;
            double lean = _lean[i] + outer * Rock(stress, t, seconds, i);
            if (Math.Abs(lean) > 1e-5)
                frames[i] = Lean(frames[i], lean);
        }
    }

    /// <summary>How far a car leans out at <paramref name="stress"/> (radians): none at the board, all of it at the limit.</summary>
    public static double Angle(float stress, OverspeedTuning t) =>
        stress <= 0 ? 0 : t.LeanDegrees * Math.PI / 180 * Math.Pow(Math.Min(1, stress), 1.5);

    /// <summary>The rock on its springs over the top (radians): from half way to the limit, a car's own beat.</summary>
    public static double Rock(float stress, OverspeedTuning t, double seconds, int car)
    {
        double from = Math.Clamp((stress - 0.5) / 0.5, 0, 1);
        return from <= 0 ? 0 : t.LeanDegrees * Math.PI / 180 * t.LeanRock * from * from * Math.Sin(seconds * 8.8 + car * 1.7);
    }

    /// <summary>
    /// <paramref name="f"/> rolled by <paramref name="radians"/> about the rail on the side it leans to (positive: its top
    /// towards its right, about its right-hand rail), so that rail stays where it is and the other comes up.
    /// </summary>
    public static CarFrame Lean(in CarFrame f, double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians), side = Math.Sign(radians) * Art.TrainKit.HalfGauge;
        var right = f.Right * c - f.Up * s;
        var up = f.Up * c + f.Right * s;
        var pivot = f.Origin + f.Right * side;
        return f with { Origin = pivot - right * side, Right = right, Up = up };
    }
}
