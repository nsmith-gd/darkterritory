using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The water heard (queue #165, ARCHITECTURE §8 note 429; the line plan's water, maritime-rules.md §2-5): rivers, lakes and
/// shores, which lay silent. Each kind's nearest to the ear, held where its water is, read off the plan and the line's
/// terrain, alike on every machine: a river running under its span (<see cref="LinePlan.Water"/>) and beside the line up
/// its valley (a river shore), a lake lapping at the shore nearest the ear (<see cref="LinePlan.Lakes"/>), the sea's surf
/// at its waterline and Fundy's tide out on its flats (<see cref="LinePlan.Shores"/>). The lake and the sea louder with
/// the night's wind. Nothing in a tunnel or down the mine.
/// </summary>
public sealed partial class GameAudio
{
    // How far from its water each is listened for (each cue's maxDistance, install.py CUE_DEF), and how often the nearest
    // water's looked up again (the water doesn't move; the ear does, at the train's speed at most).
    const double RiverReach = 120, LakeReach = 50, SurfReach = 400, TideReach = 250, WaterLook = 0.25;
    // A river under its span: how far either side of the line its course is heard from (a river runs across the corridor).
    const double RiverAcross = 25;

    readonly List<(string Cue, Double3 At, double Level)> _waterNear = [];
    double _waterLookAt = double.NegativeInfinity;

    void WaterSounds(Places places, Double3 ear, double earMain, bool tunnel, bool underground)
    {
        if (places.Plan is not { } plan || Terrain(places.Line) is not { } terrain)
            return;
        if (_time - _waterLookAt >= WaterLook || _time < _waterLookAt)
        {
            _waterLookAt = _time;
            LookForWater(plan, terrain, places.Line, ear, earMain, places.Route.Weather.Wind);
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

    /// <summary>The nearest water of each kind to the ear, where it's heard from and how loud.</summary>
    void LookForWater(LinePlan plan, TerrainField terrain, RailLine line, Double3 ear, double earMain, double wind)
    {
        _waterNear.Clear();
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
        Double3? river = null, surf = null, tide = null;
        void Nearest(ref Double3? best, Double3 at, double reach)
        {
            if ((at - ear).Length <= reach && (best is null || (at - ear).Length < (best.Value - ear).Length))
                best = at;
        }
        // A river under its span: where its course crosses the line, as near the ear as its banks go.
        foreach (var w in plan.Water)
            if (w.Edge == "main" && w.Type is "river" or "tidal")
                Nearest(ref river, OnMain(Math.Clamp(earMain, w.S0, w.S1), Math.Clamp(lateral, -RiverAcross, RiverAcross), w.LevelM), RiverReach);
        foreach (var sh in plan.Shores)
        {
            if (sh.Edge != "main")
                continue;
            double s = Math.Clamp(earMain, sh.S0, sh.S1);
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
            }
        }
        if (river is { } r)
            _waterNear.Add(("world-water.river", r, 1));
        if (surf is { } u)
            _waterNear.Add(("world-water.surf", u, 0.7 + 0.3 * Math.Clamp(wind, 0, 1)));
        if (tide is { } t)
            _waterNear.Add(("world-water.tide", t, 0.85));
    }
}
