using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Train;

/// <summary>Where one car sits in the world this tick.</summary>
/// <param name="Index">0 is the engine + tender, then cars front to back.</param>
/// <param name="Centre">Centre of the car at rail height.</param>
/// <param name="Forward">Unit vector from rear bogie to front bogie.</param>
/// <param name="FrontDistance">Distance along the line of the car's front face.</param>
public readonly record struct CarPose(int Index, Double3 Centre, Double3 Forward, double Length, double FrontDistance)
{
    public Double3 Right => Double3.Cross(Forward, Double3.Up).Normalized;
    public Double3 Up => Double3.Cross(Right, Forward);
}

/// <summary>
/// The train on a real line: dynamics fed by the track under it, and the 3D pose of every car.
/// </summary>
public sealed class TrainOnLine
{
    readonly List<CarPose> _poses = new();
    readonly List<CarFrame> _frames = new();

    public TrainOnLine(TrainDynamics dynamics, RailLine line, double startDistance)
    {
        Dynamics = dynamics;
        Line = line;
        dynamics.Distance = startDistance;
        UpdatePoses();
    }

    public TrainDynamics Dynamics { get; }
    public RailLine Line { get; }
    public IReadOnlyList<CarPose> Cars => _poses;
    /// <summary>Local frames and collision shapes of every car, valid for the current tick.</summary>
    public IReadOnlyList<CarFrame> Frames => _frames;
    /// <summary>Traction multiplier for the whole train this tick (Grease sets it; 1 is dry rail).</summary>
    public double Traction { get; set; } = 1;

    public bool AtEndOfLine => Dynamics.Distance >= Line.Length || RearDistance <= 0;
    public double RearDistance => Dynamics.Distance - Dynamics.Consist.LengthMetres;

    public void Step(double dt, in TrainControls controls)
    {
        Dynamics.Step(dt, controls, new TrackConditions { GradePercent = AverageGrade(), Traction = Traction });
        // Buffer stops: the line ends are hard limits.
        double min = Dynamics.Consist.LengthMetres, max = Line.Length;
        if (Dynamics.Distance > max || Dynamics.Distance < min)
        {
            Dynamics.Distance = Math.Clamp(Dynamics.Distance, min, max);
            Dynamics.Velocity = 0;
        }
        UpdatePoses();
    }

    /// <summary>Mass-weighted grade under the whole consist; a long train straddling a summit feels both sides.</summary>
    public double AverageGrade()
    {
        var consist = Dynamics.Consist;
        var t = consist.Tuning;
        double weighted = 0, mass = 0;
        foreach (var pose in _poses)
        {
            double m = pose.Index == 0
                ? t.Mass.EngineTonnes
                : t.Mass.EmptyCarTonnes + consist.Loads[pose.Index - 1] * (t.Mass.LoadedCarTonnes - t.Mass.EmptyCarTonnes);
            weighted += m * Line.Sample(pose.FrontDistance - pose.Length / 2).GradePercent;
            mass += m;
        }
        return mass > 0 ? weighted / mass : 0;
    }

    void UpdatePoses()
    {
        _poses.Clear();
        var g = Dynamics.Tuning.Geometry;
        double front = Dynamics.Distance;
        for (int i = 0; i <= Dynamics.Consist.CarCount; i++)
        {
            double length = i == 0 ? g.EngineLength : g.CarLength;
            // Bogies sit a fifth of the way in from each end; the body is the chord between them.
            double inset = length * 0.2;
            var fb = Line.Sample(front - inset).Position;
            var rb = Line.Sample(front - length + inset).Position;
            var forward = (fb - rb).Length > 1e-9 ? (fb - rb).Normalized : Line.Sample(front).Tangent;
            _poses.Add(new CarPose(i, Double3.Lerp(fb, rb, 0.5), forward, length, front));
            front -= length + g.CouplingGap;
        }
        _frames.Clear();
        foreach (var pose in _poses)
            _frames.Add(CarFrame.From(pose, Dynamics.Velocity, CarShape.Build(g, pose.Index == 0, pose.Index < _poses.Count - 1)));
    }
}
