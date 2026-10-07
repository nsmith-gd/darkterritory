using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// A bend taken too fast, seen and felt before it has you off (App. F.1, the overspeed telegraph: "the train should show
/// it's taking a bend too fast before it comes off: the cars straining and leaning, flanges grinding, sparks"). Each car's
/// own strain on the bend under it, worked out as <see cref="TrackRules.Assess"/> works the train's (0 at the board's
/// speed, 1 at the derailing one), and which rail it's crowding: the outer. Presentation only, from the train and the
/// line, so every machine draws the same and nothing is sent; Art.Effects.Flanges draws it, and it shakes the eye.
/// </summary>
public static class BendStrain
{
    /// <summary>Per car (by its frame's index): its strain, and the side of the outer rail (+1 its right, −1 its left).</summary>
    public static (float Stress, int Outer)[] PerCar(TrainOnLine train, PlanRules r)
    {
        var frames = train.Frames;
        var strain = new (float, int)[frames.Count];
        var rake = train.Dynamics;
        double v = rake.Speed;
        if (v < 0.5 || train.Wreck is not null || r.ADerail <= 0)
            return strain;
        double postShare = Math.Clamp(r.APost / r.ADerail, 0, 0.99);
        foreach (var car in train.Cars)
        {
            if (rake.Consist.IndexOf(car.Index) < 0 || car.Index >= frames.Count)
                continue;
            double s = car.FrontDistance - car.Length / 2;
            var here = train.Line.Sample(rake.Path, s);
            double k = Math.Abs(here.Curvature);
            if (k < 1e-9)
                continue;
            double stress = Math.Clamp((v * v * k / r.ADerail - postShare) / (1 - postShare), 0, 1);
            if (stress <= 0)
                continue;
            // The line turns towards the bend's centre: the outer rail's the other side.
            var turn = train.Line.Sample(rake.Path, s + 5).Tangent - here.Tangent;
            int outer = Double3.Dot(turn, frames[car.Index].Right) > 0 ? -1 : 1;
            strain[car.Index] = ((float)stress, outer);
        }
        return strain;
    }

    /// <summary>The eye's offset this frame (m) for someone on a straining car: a judder, harder the nearer it is to off.</summary>
    public static Double3 Offset(float stress, double seconds)
    {
        if (stress <= 0.2f)
            return default;
        double a = 0.012 * Math.Pow((stress - 0.2) / 0.8, 1.5);
        return new Double3(a * Math.Sin(seconds * 41.3), a * 0.6 * Math.Sin(seconds * 63.1 + 0.7), 0);
    }
}
