using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// A car's local coordinate frame: +X right, +Y up, −Z towards the front of the train, origin at
/// rail height under the car's centre (ARCHITECTURE §6.1). Anything standing on a car lives here,
/// so the train can do 22 m/s round a curve without the people on it noticing.
/// </summary>
public readonly record struct CarFrame(int Index, Double3 Origin, Double3 Right, Double3 Up, Double3 Back, Double3 Velocity, CarShape Shape)
{
    public static CarFrame From(in CarPose pose, double trainVelocity, CarShape shape) =>
        new(pose.Index, pose.Centre, pose.Right, pose.Up, pose.Forward * -1, pose.Forward * trainVelocity, shape);

    public Double3 ToWorld(Double3 local) => Origin + Right * local.X + Up * local.Y + Back * local.Z;
    public Double3 ToLocal(Double3 world)
    {
        var d = world - Origin;
        return new(Double3.Dot(d, Right), Double3.Dot(d, Up), Double3.Dot(d, Back));
    }

    public Double3 DirToWorld(Double3 local) => Right * local.X + Up * local.Y + Back * local.Z;
    public Double3 DirToLocal(Double3 world) => new(Double3.Dot(world, Right), Double3.Dot(world, Up), Double3.Dot(world, Back));

    /// <summary>World velocity of a body moving at <paramref name="local"/> relative to this car.</summary>
    public Double3 VelocityToWorld(Double3 local) => DirToWorld(local) + Velocity;
    public Double3 VelocityToLocal(Double3 world) => DirToLocal(world - Velocity);

    /// <summary>Heading of the car's front in radians; 0 faces world −Z, positive turns left.</summary>
    public double Heading => Math.Atan2(Back.X, Back.Z);
}

public readonly record struct Box(Double3 Min, Double3 Max)
{
    public bool ContainsXZ(Double3 p) => p.X >= Min.X && p.X <= Max.X && p.Z >= Min.Z && p.Z <= Max.Z;
}

public enum CarSurface { Roof, Coupler }

/// <summary>Greybox collision for one car in its own frame: the body, and the coupler plate behind it.</summary>
public sealed record CarShape(Box Body, Box? Coupler, IReadOnlyList<Double3> Ladders)
{
    public static CarShape Build(GeometryTuning g, bool isEngine, bool hasCarBehind)
    {
        double length = isEngine ? g.EngineLength : g.CarLength;
        double height = isEngine ? g.EngineHeight : g.CarHeight;
        double w = g.RoofWidth / 2, l = length / 2;
        var body = new Box(new Double3(-w, 0, -l), new Double3(w, height, l));
        Box? coupler = hasCarBehind
            ? new Box(new Double3(-g.CouplerWidth / 2, g.CouplerHeight - 0.1, l), new Double3(g.CouplerWidth / 2, g.CouplerHeight, l + g.CouplingGap))
            : null;
        double ladderZ = l - g.LadderInset;
        var ladders = isEngine ? Array.Empty<Double3>() : new[] { new Double3(w + 0.15, 0, ladderZ), new Double3(-w - 0.15, 0, ladderZ) };
        return new CarShape(body, coupler, ladders);
    }
}
