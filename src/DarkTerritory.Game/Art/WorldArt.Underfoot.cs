using System.Numerics;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What the ground is underfoot, by the choices the art draws it with: so what you hear under your boots off the train is
/// what you see there (the audio checklist's footstep surfaces; the sim knows only the ground's height, PlayerMotor.GroundAt).
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>
    /// The texture the art lays on the ground at a world point: <see cref="Track"/>'s bands across the line (ballast, the
    /// ditch's mud, dead grass, forest floor) or a generated line's biome ground, its second material where the land's
    /// steep or in patches, and its shore by the water; a bridge's deck, a bore's floor; a stop's roads, its halt's
    /// platform and its level crossing's boards; a generated line's country roads and crossings. Where two textures
    /// blend, the one that shows more. Inside a stop building's footprint (the art draws no floor in there) it's
    /// "concrete". In a walled town, its streets and lanes (<see cref="TownWay"/>). <paramref name="hint"/> is a main-line
    /// distance near the point (a player's line hint), refined as <see cref="RailLine.Nearest"/> refines it.
    /// </summary>
    public static string GroundTexture(RailLine line, Route? route, Double3 world, ref double hint, Sim.Towns.Town? town = null)
    {
        var (path, along) = line.Nearest(world, ref hint);
        double s = hint;
        var main = line.Sample(Math.Clamp(s, 0, line.Length));
        double lateralMain = Double3.Dot(world - main.Position, Double3.Cross(main.Tangent, Double3.Up).Normalized);
        // The bed is the nearest track's (a branch has its own), the land the main line's.
        var near = path == RailLine.MainPath ? main : line.Sample(path, along);
        float lateral = (float)Double3.Dot(world - near.Position, Double3.Cross(near.Tangent, Double3.Up).Normalized);
        float a = MathF.Abs(lateral);
        double overRail = world.Y - near.Position.Y;

        if (route is not null && Built(route, s, lateralMain) is { } built)
            return built;
        if (TownWay(town, s, lateralMain) is { } way)
            return way;
        // A bridge's deck (Bridge: a timber trestle where it's weak, a masonry viaduct's ballasted top where it's sound); down
        // off it, the gorge's floor.
        if (Gorge(route, s) > 0.5f && a < 3.7f)
            return overRail > -1 ? route?.BridgeAt(s) is { MaxCars: > 0 } ? "wood_sleeper" : "ballast" : "ground_mud";
        // In a bore, the bed; up on the hill over it (a generated line's land stands people there), its forest floor.
        Ridge(route, s, out bool bore);
        if (bore && a < 12)
            return overRail < 3 ? "ballast" : "ground_forest";

        var (bandA, bandB, band) = GroundBand(lateral);
        if (route?.Plan is not { } plan || line.Conditions is not PlanConditions conditions)
            return GroundBlend(band, lateral, s) < 0.5f ? bandA : bandB;
        // A generated line's (Track): the bed spills straight into the biome's ground, which goes to its second material on
        // the steep and in patches, and to its shore by the water.
        var def = plan.Rules.Biomes.GetValueOrDefault(BiomeOf(plan, s));
        string ground = BiomeTexture(def?.Ground), second = BiomeTexture(def?.Materials.FirstOrDefault() ?? "rock");
        if (band == 0)
            return GroundBlend(0, lateral, s) < 0.5f ? "ballast" : ground;
        var terrain = conditions.Terrain;
        if (a > 6 && terrain.WaterNear(world.X, world.Z, 30) is { } water)
            return SmoothStep(2.4f, 0.6f, (float)(world.Y - water.Level)) < 0.5f ? ground
                : water.Kind is "lake" or "sea" or "river" ? "shore_shingle" : "ground_red_clay";
        // The slope as the land's mesh has it, over a couple of metres (its grid).
        const double d = 1;
        double dx = (terrain.Height(world.X + d, world.Z) - terrain.Height(world.X - d, world.Z)) / (2 * d);
        double dz = (terrain.Height(world.X, world.Z + d) - terrain.Height(world.X, world.Z - d)) / (2 * d);
        float steep = SmoothStep(0.65f, 1.3f, (float)Math.Sqrt(dx * dx + dz * dz));
        float patch = Patches(new Vector3(W(world.X), W(world.Y), W(world.Z))) * SmoothStep(9, 20, a);
        return MathF.Max(steep, patch) < 0.5f ? ground : second;
    }

    /// <summary>
    /// A walled town's way at <paramref name="s"/> along the main line, <paramref name="lateral"/> across, as <see cref="Streets"/>
    /// draws it (queue #74, note 335): a street's beaten stones, a lane's mud; null off them. The director walked the stones
    /// and heard the grass under them (note 354: "a super weird squishy footstep sound when I walk on the stones in the town").
    /// </summary>
    public static string? TownWay(Sim.Towns.Town? town, double s, double lateral)
    {
        if (town?.Plan.Bounds is not { } b || !b.Holds(s, lateral))
            return null;
        foreach (var st in b.Streets)
            if (s >= st.S0 && s <= st.S1 && Math.Abs(lateral - st.D) <= st.Width / 2)
                return "ballast";
        foreach (var lane in b.Lanes)
            if (Math.Abs(s - lane.S) <= lane.Width / 2 && LaneMids(lane, TownChunk).Any(mid => Math.Abs(lateral - mid) <= TownChunk / 2))
                return "ground_mud";
        return null;
    }

    /// <summary>What's been built on the ground at <paramref name="s"/> along the main line, <paramref name="lateral"/> across, if anything.</summary>
    static string? Built(Route route, double s, double lateral)
    {
        // A generated line's country roads: gravel, crossing the line over plank decks (PlanArt's roads).
        if (route.Plan is { } plan)
        {
            foreach (var c in plan.Crossings)
                if (Math.Abs(s - c.S) <= 9 && Math.Abs(lateral) <= 2.2)
                    return "wood_sleeper";
            var rr = plan.Rules.Terrain.Roads;
            foreach (var road in plan.Roads)
                if (s >= road.S0 && s <= road.S1 && Math.Abs(lateral - TerrainField.RoadLateral(road, plan.Crossings, s, rr.RampM)) < rr.HalfWidthM - 0.3)
                    return "ballast";
        }
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || s < f.Start - 250 || s > f.End + 250)
                continue;
            var p = new Pt(s - f.Start, lateral);
            if (stop.Crossing is { } x && Math.Abs(p.S - x.S) <= CrossingHalf && Math.Abs(p.D) <= CrossingHalf)
                return "wood_sleeper";
            if (stop.Halt is { } h && Math.Abs(p.S - h.S) <= stop.HaltLength / 2)
            {
                // The platform (Halt): from a metre inside its line out past it, on its side of the track.
                double side = h.D < 0 ? -1 : 1, x0 = side * (Math.Abs(h.D) - 1.0), x1 = side * (Math.Abs(h.D) + 2.2);
                if (p.D >= Math.Min(x0, x1) && p.D <= Math.Max(x0, x1))
                    return "cobbles";
            }
            foreach (var b in stop.Buildings)
                if (Inside(b, p))
                    return "concrete";
            foreach (var road in stop.Roads)
            {
                double half = RoadHalfWidth(road.Kind);
                for (int i = 0; i + 1 < road.Points.Count; i++)
                {
                    var (q0, q1) = (road.Points[i], road.Points[i + 1]);
                    // Over the formation the track has it (Roads leaves it out).
                    if (Math.Abs(q0.D) < 3.2 && Math.Abs(q1.D) < 3.2)
                        continue;
                    if (DistanceToSegment(p, q0, q1) <= half)
                        return road.Kind == RoadKind.Street ? "cobbles" : "ground_mud";
                }
            }
        }
        return null;
    }

    static bool Inside(StopBuilding b, Pt p)
    {
        double ds = p.S - b.S, dd = p.D - b.D, c = Math.Cos(b.Yaw), n = Math.Sin(b.Yaw);
        double x = ds * c + dd * n, y = -ds * n + dd * c;
        if (b.Parts.Count == 0)
            return Math.Abs(x) <= b.Length / 2 && Math.Abs(y) <= b.Width / 2;
        foreach (var part in b.Parts)
            if (Math.Abs(x - part.X) <= part.Length / 2 && Math.Abs(y - part.Y) <= part.Width / 2)
                return true;
        return false;
    }

    static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        double len2 = ab.S * ab.S + ab.D * ab.D;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.S - a.S) * ab.S + (p.D - a.D) * ab.D) / len2, 0, 1);
        return Pt.Distance(p, a + ab * t);
    }
}
