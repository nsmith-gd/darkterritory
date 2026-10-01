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
    public static readonly string[] Names = ["trackside", "roof", "cab", "fireman", "chase", "ahead", "gap", "gangway", "gun"];

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
            // The fireman's side (T101): out of the left window down the running board to the blow-off by the smokebox.
            "fireman" => CabCamera(engine, -1),
            "chase" => ChaseCamera(train),
            "ahead" => Camera.LookAt(engine.ToWorld(new Double3(1.5, 2.2, -engineHalf - 70)), engine.ToWorld(new Double3(0, 2.2, 0)), 55),
            "gap" => GapCamera(train, car),
            // From over the car behind, down at a cargo car's roof hatch (T99): its lid, shut, or open down the side.
            "hatch" => Camera.LookAt(target.ToWorld(new Double3(4.2, roof + 2.2, 9.5)), target.ToWorld(new Double3(0.6, roof - 1.4, 3.2)), 70),
            // Over the last car's roof, looking back at its gun on its rail (T93).
            "gun" => GunCamera(train),
            // On the plate behind the tender, looking up its gangway into the cab and at the ladder to the cab roof (T90).
            "gangway" => Camera.LookAt(engine.ToWorld(new Double3(-0.2, 2.9, engineHalf + 1.4)), engine.ToWorld(new Double3(-0.9, 0.6, engineHalf - 8)), 75),
            _ => throw new ArgumentException($"unknown view '{name}' (known: {string.Join(", ", Names)})"),
        };
    }

    /// <summary>Standing in the cab beside the boiler, looking past it down the line.</summary>
    static Camera CabCamera(in CarFrame engine, int side = 1)
    {
        var eye = CabEye(engine.Shape, side);
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(eye + new Double3(-side * 3, -2.5, -60)), 75);
    }

    /// <summary>
    /// A standing eye at the driver's place (T101), in the engine's frame: by the brake and the reverser on the right
    /// (<paramref name="side"/> 1), in line with the window beside the boiler; −1 is the fireman's, across the cab.
    /// </summary>
    public static Double3 CabEye(CarShape engine, int side = 1)
    {
        var cab = engine.Cab!.Value;
        var levers = engine.Levers!.Value;
        // As far out as a player stands: against the cab side (its wall 0.1 thick, a player 0.3 round).
        return new Double3(side * (engine.HalfWidth - 0.4), cab.Min.Y + 1.75, levers.Reverser.Z + 0.55);
    }

    /// <summary>
    /// Standing in the coupling gap behind <paramref name="car"/>, on the car behind's side of it, looking at the end door the
    /// plate leads to (GDD §32 "readable gap and coupling danger"): the plate underfoot, the doorway, the ballast either side.
    /// </summary>
    static Camera GapCamera(TrainOnLine train, int car)
    {
        var ahead = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double l = ahead.Shape.HalfLength;
        return Camera.LookAt(ahead.ToWorld(new Double3(0.35, 2.75, l + 1.3)), ahead.ToWorld(new Double3(-0.4, 1.4, l)), 80);
    }

    static Camera GunCamera(TrainOnLine train)
    {
        var last = train.Frames[^1];
        double roof = last.Shape.RoofHeight;
        return Camera.LookAt(last.ToWorld(new Double3(1.6, roof + 2.2, -2)), last.ToWorld(new Double3(0, roof + 0.4, last.Shape.HalfLength - 2.5)), 60);
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
        light.LampPosition = Sim.World.LampPosition(engine);
        var fwd = engine.Back * -1;
        light.LampDirection = Vector3.Normalize(new Vector3((float)fwd.X, (float)fwd.Y - 0.04f, (float)fwd.Z));
        return light;
    }
}
