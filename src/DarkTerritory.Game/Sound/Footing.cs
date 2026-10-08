using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// What's underfoot, as the audio's surface materials (tools/audio/cues.py MATERIALS: wood, grate, plate, roof, coal, and
/// the ground's ballast, dirt, grass, mud, cobbles, concrete): the crew's footsteps and whatever's dropped. The sim keeps
/// only <see cref="Surface"/> (a roof, a deck), so this goes back to the solid underfoot and the texture the art gives it
/// (out/audio/hooks-map.md §1), and off the train to the ground the art draws there (<see cref="WorldArt.GroundTexture"/>).
/// </summary>
public static class Footing
{
    /// <summary>How far above or below a solid's top feet (or something lying there) still count as on it.</summary>
    const double OnTop = 0.08;

    /// <summary>What a player's feet are on, or null for nothing (in the air, on a ladder).</summary>
    public static string? Under(in PlayerState s, World world)
    {
        if (s.Surface is Surface.Air or Surface.Ladder)
            return null;
        if (s.Parent == PlayerState.World)
        {
            double hint = s.LineHint;
            return Ground(world, s.Position, ref hint);
        }
        return s.Parent < world.Train.Frames.Count ? OnCar(world.Train, s.Parent, s.Position, s.Surface) : null;
    }

    /// <summary>What lies under a point on a vehicle (its frame): the solid whose top it's at, as the art has it (TrainKit).</summary>
    /// <param name="surface">What the sim says it is, for when no solid's top is there (between two, on an edge).</param>
    /// <param name="below">How far under the point a top can be and still be what it's on.</param>
    public static string OnCar(TrainOnLine train, int vehicle, Double3 local, Surface surface = Surface.Deck, double below = OnTop)
    {
        var shape = train.Frames[vehicle].Shape;
        var v = train.Vehicles[vehicle];
        Solid? best = null;
        foreach (var solid in shape.Solids)
            if (solid.Present(v) && solid.Box.ContainsXZ(local) && solid.Box.Max.Y <= local.Y + OnTop && solid.Box.Max.Y >= local.Y - below
                && (best is null || solid.Box.Max.Y > best.Value.Box.Max.Y))
                best = solid;
        if (best is not { } on)
            return surface switch
            {
                Surface.Roof => "roof",
                Surface.Coupler => "grate",
                _ => v.IsEngine ? "plate" : "wood",
            };
        return on.Part switch
        {
            // The floors: a car's planked (wood_floor), the cab's too, but for the iron shovelling plate at the tender's front;
            // ahead of the cab the engine's deck is iron, behind it the tender's gangway grated.
            PartKind.Chassis when v.IsEngine => EngineDeck(shape, local),
            PartKind.Chassis or PartKind.Steps or PartKind.Cargo or PartKind.Locker or PartKind.Wall => "wood",
            // The engine's running boards and the gangway's footplate are chequer plate (steel_grate), as are the coupler
            // plates and the guard van's rear platform.
            PartKind.RunningBoard or PartKind.Coupler => "grate",
            PartKind.Tender => "coal",
            PartKind.Boiler or PartKind.Stack or PartKind.CabWall or PartKind.GunMount => "plate",
            // Car roofs, the cab roof and the hatch's lid: corrugated iron or plate, the roof walk's tin.
            _ => "roof",
        };
    }

    /// <summary>The engine's deck at a point: the cab's floor, its shovelling plate, the tender's gangway, or the iron ahead.</summary>
    static string EngineDeck(CarShape shape, Double3 local)
    {
        if (shape.Cab is not { } cab)
            return "plate";
        // TrainKit's shovelling plate: the coal gate's width and a bit, 0.7 m forward of the tender's front.
        if (local.Z >= cab.Max.Z - 0.7 && local.Z <= cab.Max.Z && Math.Abs(local.X) <= 0.65)
            return "plate";
        if (local.Z >= cab.Min.Z && local.Z <= cab.Max.Z)
            return "wood";
        return local.Z > cab.Max.Z ? "grate" : "plate";
    }

    /// <summary>The ground at a world point, as a surface material.</summary>
    public static string Ground(World world, Double3 at, ref double hint) =>
        OfTexture(WorldArt.GroundTexture(world.Train.Line, world.Route ?? world.Run?.Route, at, ref hint, world.Town));

    /// <summary>
    /// A ground texture as it sounds underfoot (hooks-map §1): loose stone, mud and bog, grass and heath, the forest's floor,
    /// setts, timber. Bare rock has no footstep set of its own: it's hard stone, the setts' nearest. Anything else is
    /// "ground", which falls back to the nearest that has a sound (GameAudio's surfaces).
    /// </summary>
    public static string OfTexture(string texture) => texture switch
    {
        "ballast" or "slag" or "shore_shingle" => "ballast",
        "ground_mud" or "bog_sphagnum" or "marsh" or "ground_red_clay" => "mud",
        "ground_grass" or "ground_heath" => "grass",
        "ground_needles" or "ground_forest" => "dirt",
        "cobbles" or "rock_cliff" or "granite_lichen" or "stone_block" => "cobbles",
        "wood_sleeper" or "wood_grey" or "planks" => "wood",
        "concrete" => "concrete",
        _ => "ground",
    };

    /// <summary>
    /// Where a loose body has come to rest (the bottom of it, in its frame): on a car, the solid under it (a body settles
    /// a little into or over what it's on); on the ground, the ground.
    /// </summary>
    public static string UnderBody(World world, int parent, Double3 bottom, ref double hint)
    {
        var train = world.Train;
        if (parent != PlayerState.World && parent < train.Frames.Count)
            return OnCar(train, parent, bottom + Double3.Up * 0.05, Surface.Deck, below: 0.4);
        return Ground(world, bottom, ref hint);
    }
}
