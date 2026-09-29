using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Named camera setups for screenshots, so an agent or a designer can ask for "the roof of car 2"
/// instead of coordinates. Each view also places the engine's headlamp.
/// </summary>
public static class Views
{
    public static readonly string[] Names = ["trackside", "roof", "cab", "chase", "ahead"];

    public static Camera Get(string name, TrainOnLine train, int car = 2)
    {
        var engine = train.Frames[0];
        var target = train.Frames[Math.Min(car, train.Frames.Count - 1)];
        double roof = target.Shape.RoofHeight;
        double engineHalf = engine.Shape.HalfLength;
        return name switch
        {
            "trackside" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.7, -engineHalf - 25)), target.ToWorld(new Double3(0, 2.5, 0)), 60),
            "roof" => Camera.LookAt(target.ToWorld(new Double3(0.2, roof + 1.65, 3)), target.ToWorld(new Double3(0, roof + 1.2, -40)), 75),
            "cab" => CabCamera(engine),
            "chase" => ChaseCamera(train),
            "ahead" => Camera.LookAt(engine.ToWorld(new Double3(1.5, 2.2, -engineHalf - 70)), engine.ToWorld(new Double3(0, 2.2, 0)), 55),
            _ => throw new ArgumentException($"unknown view '{name}' (known: {string.Join(", ", Names)})"),
        };
    }

    /// <summary>Standing in the cab beside the boiler, looking past it down the line.</summary>
    static Camera CabCamera(in CarFrame engine)
    {
        var cab = engine.Shape.Cab!.Value;
        var eye = new Double3(cab.Max.X - 0.35, cab.Min.Y + 1.75, cab.Centre.Z + 0.6);
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(eye + new Double3(0.1, -0.4, -60)), 75);
    }

    static Camera ChaseCamera(TrainOnLine train)
    {
        var last = train.Frames[^1];
        var mid = train.Frames[train.Frames.Count / 2];
        return Camera.LookAt(last.ToWorld(new Double3(-12, 14, last.Shape.HalfLength + 30)), mid.ToWorld(new Double3(0, 2, 0)), 60);
    }

    public static FrameLighting Lighting(TrainOnLine train, Look? look = null) => Lighting(train.Frames[0], look);

    /// <param name="look">The art pass's atmosphere (look.json) over the night's defaults, when there is one.</param>
    public static FrameLighting Lighting(in CarFrame engine, Look? look = null)
    {
        var light = look?.Apply(FrameLighting.Night) ?? FrameLighting.Night;
        light.LampPosition = engine.ToWorld(new Double3(0, 2.8, -engine.Shape.HalfLength - 0.3));
        var fwd = engine.Back * -1;
        light.LampDirection = Vector3.Normalize(new Vector3((float)fwd.X, (float)fwd.Y - 0.04f, (float)fwd.Z));
        return light;
    }
}
