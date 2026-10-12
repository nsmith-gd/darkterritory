using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Art;

/// <summary>How a house a creature lives in is drawn (G1's house creatures, notes 584–586).</summary>
public enum HouseForm
{
    /// <summary>As the village has it (<see cref="TownKit.OpenHouse"/>): a Hollow House still, or sinking.</summary>
    Standing,
    /// <summary>A Hollow House come down (<see cref="TownKit.CollapsedHouse"/>).</summary>
    Collapsed,
    /// <summary>The Householder's: kept, nothing underfoot but its own, its table stood (the heavy furniture still where the sim has it).</summary>
    Kept,
}

/// <summary>
/// The houses the house creatures live in (G1, notes 584–586). A Hollow House is its house: it shakes, sinks and comes
/// down, so its house can't stay cooked into the line's cells. Lifted, a house is left out of its cell (the cell's cooked
/// again without it) and the scene draws it itself, as one piece in its own frame moved however the creature has it; the
/// Householder's is lifted to be drawn kept. Lifting is per route: a new route lifts nothing.
/// </summary>
public sealed partial class WorldArt
{
    Route? _liftRoute;
    readonly HashSet<(int Feature, int Building)> _lifted = [];
    readonly Dictionary<(int Feature, int Building, HouseForm Form), MeshAsset> _houses = [];

    /// <summary>Whether building <paramref name="building"/> of the route's <paramref name="feature"/> is drawn by the scene, not its cell.</summary>
    public bool Lifted(Route? route, int feature, int building) =>
        _lifted.Count > 0 && ReferenceEquals(route, _liftRoute) && _lifted.Contains((feature, building));

    /// <summary>
    /// Takes a stop's building out of its cell, for the scene to draw (<see cref="House"/>); the cell it was in is cooked
    /// again without it the next time it's drawn. Call it before the frame's cells are drawn.
    /// </summary>
    public void Lift(Route route, int feature, int building)
    {
        if (!ReferenceEquals(route, _liftRoute))
        {
            _lifted.Clear();
            _houses.Clear();
            _liftRoute = route;
        }
        if (!_lifted.Add((feature, building)) || route.Features[feature] is not { Stop: { } stop } f)
            return;
        _cells.Remove((long)Math.Floor((f.Start + stop.Buildings[building].S) / CellLength));
    }

    /// <summary>
    /// A stop building's frame as the cells draw it (<see cref="Building"/>): its middle on the levelled ground (0.15 m under
    /// it), camera-relative, the house's x on −Z and its y on +X.
    /// </summary>
    public static Matrix4x4 HouseFrame(RailLine line, Route route, RouteFeature f, int building, Double3 eye, float valleyDepth)
    {
        var b = f.Stop!.Buildings[building];
        double along = f.Start + b.S;
        var t = line.Sample(Math.Clamp(along, 0, line.Length));
        var at = Sim.Run.Run.StopWorld(line, f, b.Centre, Ground(route, along, (float)b.D, valleyDepth) - 0.15);
        return Basis(t.Tangent, at, eye, (float)-b.Yaw);
    }

    /// <summary>
    /// A lifted house, drawn at <paramref name="frame"/> (its <see cref="HouseFrame"/>, moved as its creature has it): as it
    /// stands, collapsed, or kept. Cooked once a form.
    /// </summary>
    public void House(MeshBuilder mesh, Route route, int feature, int building, HouseForm form, in Matrix4x4 frame)
    {
        if (route.Features[feature] is not { Stop: { } stop })
            return;
        if (!_houses.TryGetValue((feature, building, form), out var asset))
        {
            var b = stop.Buildings[building];
            var k = new Kit(_look);
            k.Reseed(b.Variant * 7.1f + (float)(b.S * 0.13));
            int index = building;
            var clutter = Sim.Run.StopWalls.ClutterOf(stop, index);
            switch (form)
            {
                case HouseForm.Collapsed:
                    TownKit.CollapsedHouse(k, b, feature * 1021 + building);
                    break;
                case HouseForm.Kept:
                    // Nothing kicked about: only what stands in the way (the sim's solid pieces), and its table on its legs.
                    TownKit.OpenHouse(k, b, stop.Containers.Where(c => c.Building == index),
                        [.. clutter.Where(c => c.Solid).Select(c => c.Kind == Sim.Run.ClutterKind.Table ? c with { Tipped = false } : c)], null);
                    break;
                default:
                    TownKit.OpenHouse(k, b, stop.Containers.Where(c => c.Building == index), clutter, Sim.Run.StopWalls.Nest(stop, index));
                    break;
            }
            _houses[(feature, building, form)] = asset = k.Build($"house-{feature}-{building}-{form}");
        }
        mesh.Instances.Add(new MeshInstance(asset, frame));
    }
}
