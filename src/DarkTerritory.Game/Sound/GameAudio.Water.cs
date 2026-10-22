using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The water heard (queue #165, ARCHITECTURE §8 note 429; the line plan's water, maritime-rules.md §2-5): rivers, lakes and
/// shores, which lay silent. Each kind's nearest to the ear, held where its water is, read off the plan and the line's
/// terrain, alike on every machine: a river running under its span (<see cref="LinePlan.Water"/>) and beside the line up
/// its valley (a river shore), a lake lapping at the shore nearest the ear (<see cref="LinePlan.Lakes"/>), the sea's surf
/// at its waterline and Fundy's tide out on its flats (<see cref="LinePlan.Shores"/>). The lake and the sea louder with
/// the night's wind. Nothing in a tunnel or down the mine. A town type's own water (note 692: a harbour's basin, a tidewater
/// town's channels, a dykeland's marsh) laps as a lake does, the nearer of the two heard (queue #526, note 799). A bog's
/// standing water either side of its causeway (the plan's marsh water) and a dykeland's salt marsh out past its dyke (a
/// <see cref="ShoreKind.Dyke"/> shore), which lay silent, each their own (queue #690, note 990).
/// </summary>
public sealed partial class GameAudio
{
    // How far from its water each is listened for (each cue's maxDistance, install.py CUE_DEF), and how often the nearest
    // water's looked up again (the water doesn't move; the ear does, at the train's speed at most).
    const double RiverReach = 120, LakeReach = 50, SurfReach = 400, TideReach = 250, MarshReach = 60, SaltReach = 200, WaterLook = 0.25;
    // A river under its span: how far either side of the line its course is heard from (a river runs across the corridor).
    const double RiverAcross = 25;
    // A bog by its causeway: its water from this far out either side (the causeway's bank between), to the corridor's edge.
    const double MarshFrom = 12;

    readonly List<(string Cue, Double3 At, double Level)> _waterNear = [];
    double _waterLookAt = double.NegativeInfinity;
    // A town type's water as its ground's rectangles (the raster's whole grid walked once a town, not each look).
    readonly Dictionary<TownGround, IReadOnlyList<(double S0, double S1, double D0, double D1, double Level)>> _townWater = [];

    void WaterSounds(World world, Places places, Double3 ear, double earMain, bool tunnel, bool underground)
    {
        if (_time - _waterLookAt >= WaterLook || _time < _waterLookAt)
        {
            _waterLookAt = _time;
            _waterNear.Clear();
            double wind = places.Route.Weather.Wind;
            if (places.Plan is { } plan && Terrain(places.Line) is { } terrain)
                LookForWater(plan, terrain, places.Line, ear, earMain, wind);
            foreach (var town in (Town?[])[world.Town, world.Arrival])
                if (TownWaterNear(town, ear) is { } at
                    && _waterNear.FindIndex(w => w.Cue == "world-water.lake") is var i
                    && (i < 0 || (at - ear).Length < (_waterNear[i].At - ear).Length))
                {
                    if (i >= 0)
                        _waterNear.RemoveAt(i);
                    _waterNear.Add(("world-water.lake", at, 0.55 + 0.45 * Math.Clamp(wind, 0, 1)));
                }
        }
        if (tunnel || underground)
            return;
        float outside = Occlusion(PlayerMotor.Outside);
        foreach (var (cue, at, level) in _waterNear)
            HoldLevel(cue, 0, at, outside, level);
    }

    /// <summary>The line's terrain, under the hazards' wrapper where there is one (as <c>Guns.Water</c> finds it).</summary>
    static TerrainField? Terrain(RailLine line) =>
        (line.Conditions is HazardConditions h ? h.Inner : line.Conditions) is PlanConditions plan ? plan.Terrain : null;

    /// <summary>A town type's water nearest the ear, within a lake's reach: the nearest point of its basins, at their surface.</summary>
    Double3? TownWaterNear(Town? town, Double3 ear)
    {
        if (town is null || town.Ground.Flat)
            return null;
        if (!_townWater.TryGetValue(town.Ground, out var rects))
        {
            if (_townWater.Count >= 4)
                _townWater.Clear();
            _townWater[town.Ground] = rects = town.Ground.WaterRects();
        }
        if (rects.Count == 0)
            return null;
        var o = town.Anchor();
        var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var right = Double3.Cross(along, Double3.Up).Normalized;
        var p = ear - o.Position;
        double s = Double3.Dot(p, along), d = Double3.Dot(p, right);
        Double3? best = null;
        foreach (var (s0, s1, d0, d1, level) in rects)
        {
            var at = o.Position + along * Math.Clamp(s, s0, s1) + right * Math.Clamp(d, d0, d1);
            at = at with { Y = o.Position.Y + level };
            if ((at - ear).Length <= LakeReach && (best is null || (at - ear).Length < (best.Value - ear).Length))
                best = at;
        }
        return best;
    }

    /// <summary>The nearest water of each kind to the ear, where it's heard from and how loud.</summary>
    void LookForWater(LinePlan plan, TerrainField terrain, RailLine line, Double3 ear, double earMain, double wind)
    {
        // A lake: at the shore nearest the ear (its metric is 1 on the shore, so the way out from its middle scaled by the
        // ear's metric; the wobble's slow enough for that), or under the ear on a causeway across it.
        Double3? lake = null;
        foreach (var l in plan.Lakes)
        {
            double m = TerrainField.LakeMetric(l, ear.X, ear.Z);
            var at = m <= 1 ? new Double3(ear.X, l.LevelM, ear.Z)
                : new Double3(l.X + (ear.X - l.X) / m, l.LevelM, l.Z + (ear.Z - l.Z) / m);
            if ((at - ear).Length <= LakeReach && (lake is null || (at - ear).Length < (lake.Value - ear).Length))
                lake = at;
        }
        if (lake is { } shore)
            _waterNear.Add(("world-water.lake", shore, 0.55 + 0.45 * Math.Clamp(wind, 0, 1)));
        if (double.IsNaN(earMain))
            return;
        // Along the main line: the ear's place on it and its side (TerrainField's lateral: right of travel).
        var here = line.Sample(earMain);
        var right = Double3.Cross(here.Tangent, Double3.Up).Normalized;
        double lateral = Double3.Dot(ear - here.Position, right);
        Double3 OnMain(double s, double across, double y)
        {
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var p = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * across;
            return p with { Y = y };
        }
        Double3? river = null, surf = null, tide = null, marsh = null, salt = null;
        void Nearest(ref Double3? best, Double3 at, double reach)
        {
            if ((at - ear).Length <= reach && (best is null || (at - ear).Length < (best.Value - ear).Length))
                best = at;
        }
        // Along a stretch of the line, as near the ear as it goes. Either way round: a tidal river's span can come out of the
        // plan with its ends reversed (local:40's, note 990), which Math.Clamp throws on.
        static double Along(double s, double a, double b) => Math.Clamp(s, Math.Min(a, b), Math.Max(a, b));
        // A river under its span: where its course crosses the line, as near the ear as its banks go.
        foreach (var w in plan.Water)
            if (w.Edge == "main" && w.Type is "river" or "tidal")
                Nearest(ref river, OnMain(Along(earMain, w.S0, w.S1), Math.Clamp(lateral, -RiverAcross, RiverAcross), w.LevelM), RiverReach);
            else if (w.Edge == "main" && w.Type is "marsh" or "contaminatedMarsh")
                // A bog's water either side of its causeway: on the ear's side, off the bank, as near as the corridor goes.
                Nearest(ref marsh, OnMain(Along(earMain, w.S0, w.S1),
                    (lateral < 0 ? -1 : 1) * Math.Clamp(Math.Abs(lateral), MarshFrom, Math.Max(MarshFrom, w.WidthM)), w.LevelM), MarshReach);
        foreach (var sh in plan.Shores)
        {
            if (sh.Edge != "main")
                continue;
            double s = Along(earMain, sh.S0, sh.S1);
            double edge = terrain.ShoreEdge(sh, s);
            switch (sh.Kind)
            {
                case ShoreKind.River:
                    // Up its valley beside the line: mid-stream, its level how far under the rail.
                    Nearest(ref river, OnMain(s, sh.Side * (edge + sh.FlatM / 2), line.Sample(s).Position.Y - sh.LevelM), RiverReach);
                    break;
                case ShoreKind.Sea:
                    Nearest(ref surf, OnMain(s, sh.Side * edge, sh.LevelM), SurfReach);
                    break;
                case ShoreKind.Fundy:
                    // Out on the flats, the tide's wash beyond them and the mud seeping across them.
                    Nearest(ref tide, OnMain(s, sh.Side * (edge + sh.FlatM / 2), sh.LevelM), TideReach);
                    break;
                case ShoreKind.Dyke:
                    // Out on the salt marsh between the dyke and the water, its creeks at the tide's level.
                    Nearest(ref salt, OnMain(s, sh.Side * (sh.DykeM + edge) / 2, sh.LevelM), SaltReach);
                    break;
            }
        }
        if (river is { } r)
            _waterNear.Add(("world-water.river", r, 1));
        if (surf is { } u)
            _waterNear.Add(("world-water.surf", u, 0.7 + 0.3 * Math.Clamp(wind, 0, 1)));
        if (tide is { } t)
            _waterNear.Add(("world-water.tide", t, 0.85));
        if (marsh is { } b)
            _waterNear.Add(("world-water.marsh", b, 1));
        if (salt is { } sm)
            _waterNear.Add(("world-water.saltmarsh", sm, 0.7 + 0.3 * Math.Clamp(wind, 0, 1)));
    }
}
