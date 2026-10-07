using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The director's spawn context tags (plan §15.1), the night's weather and the terrain's local change to it
/// (§14): what the sim, the audio and the director read along the line.
/// </summary>
sealed partial class LineBuilder
{
    void Tag(string tag, string edge, double s0, double s1)
    {
        if (s1 - s0 > 0.5)
            _tags.Add(new PlanTag(tag, edge, R(Math.Max(0, s0)), R(s1)));
    }

    void LayTags()
    {
        foreach (var e in _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch))
        {
            var line = LineOf(e);
            foreach (var item in e.Items)
                foreach (var p in item.All())
                {
                    if (p.Kind == "region")
                        continue;
                    foreach (var tag in p.Tags.Distinct())
                    {
                        switch (tag)
                        {
                            case "tunnel":
                                var bore = _structures.FirstOrDefault(s => s.Edge == e.Id && s.Type == StructureType.Tunnel && s.S0 >= p.S0 - 1 && s.S1 <= p.S1 + 1);
                                if (bore is not null)
                                {
                                    Tag("tunnel", e.Id, bore.S0, bore.S1);
                                    Tag("tunnel_exit", e.Id, bore.S1, bore.S1 + _t.Director.TunnelExitM);
                                }
                                break;
                            case "bridge":
                                var span = _structures.FirstOrDefault(s => s.Edge == e.Id && s.Type is StructureType.Trestle or StructureType.Viaduct or StructureType.Girder or StructureType.Truss && s.S0 >= p.S0 - 1 && s.S1 <= p.S1 + 1);
                                if (span is not null)
                                    Tag("bridge", e.Id, span.S0, span.S1);
                                break;
                            case "blind_curve_exit":
                                Tag("blind_curve_exit", e.Id, p.S1 - 60, p.S1 + 200);
                                break;
                            case "brass":
                                var field = _structures.FirstOrDefault(s => s.Edge == e.Id && s.Type == StructureType.BrassField && s.S0 >= p.S0 - 1 && s.S1 <= p.S1 + 1);
                                if (field is not null)
                                    Tag("brass", e.Id, field.S0, field.S1);
                                break;
                            case "pre_curve":
                                break; // from the geometry below
                            default:
                                Tag(tag, e.Id, p.S0, p.S1);
                                break;
                        }
                    }
                }
            // §15.1 straight_long: tangent for 800 m or more (a long clear straight for the Ferryman).
            GeometricTags(e, line);
        }
        foreach (var r in _regionItems)
            foreach (var tag in r.Tags)
                Tag(tag, "main", r.S0, r.S1);
        // Spawn bans and contexts.
        Tag("grace", "main", 0, _gate + _t.Budget.GraceM);
        Tag("terminus_safe", "main", _terminus - _t.Terminus.SpawnBanM, _end);
        foreach (var slot in _facilities)
            Tag("near_facility", "main", slot.LullStart - _t.Director.NearFacilityM + 1000, slot.LullEnd + _t.Director.NearFacilityM - 500);
        foreach (var w in _alts)
        {
            Tag("junction", "main", w.T - 300, w.T + 100);
            Tag("junction", "main", w.J - 100, w.J + 300);
        }
        foreach (var d in _deads)
            Tag("junction", "main", d.Toe - 300, d.Toe + 100);
        _tags.Sort((a, b) => a.Edge != b.Edge ? string.CompareOrdinal(a.Edge, b.Edge) : a.S0 != b.S0 ? a.S0.CompareTo(b.S0) : string.CompareOrdinal(a.Tag, b.Tag));
    }

    RailLine LineOf(EdgeDraft e) => e.Role == EdgeRole.Main ? _line! : _line!.Branches[e.Branch].Local;
    RailLine LineOf(string edge) => LineOf(_edges[edge]);

    /// <summary>Tags from the track itself: long straights, and the Long Whistle's windows before a grade or a limited curve.</summary>
    void GeometricTags(EdgeDraft e, RailLine line)
    {
        double start = -1;
        for (double s = 0; s <= line.Length; s += 10)
        {
            bool straight = Math.Abs(line.Sample(s).Curvature) < 1 / 5000.0;
            if (straight && start < 0)
                start = s;
            if ((!straight || s + 10 > line.Length) && start >= 0)
            {
                if (s - start >= 800)
                    Tag("straight_long", e.Id, start, s);
                start = -1;
            }
        }
        // pre_grade / pre_curve: 400–900 m before a climb of note or a curve that needs a limit (§15.1).
        var pre = _t.Director.PreGradeM;
        foreach (var item in e.Items.SelectMany(i => i.All()).Where(i => i.IsPiece))
        {
            if (item.Kind is "climb" or "summit" or "momentum" or "descent" or "drop")
                Tag("pre_grade", e.Id, item.S0 - pre[1], item.S0 - pre[0]);
            if (item.Kind is "drop" or "blindThroat" or "ledge")
            {
                double curve = item.Kind == "drop" ? item.S1 - item.Prims.Where(p => p.K0 != 0 || p.K1 != 0).Sum(p => p.Length) : item.S0;
                Tag("pre_curve", e.Id, curve - pre[1], curve - pre[0]);
            }
        }
    }

    bool HasTag(string tag, string edge, double s) => _tags.Any(t => t.Tag == tag && t.Edge == edge && s >= t.S0 && s <= t.S1);

    /// <summary>§14: the night's weather, rolled from tier and biome.</summary>
    void RollWeather()
    {
        var rng = Rng("weather");
        var w = _t.Weather;
        double fogMax = Math.Round(rng.Range(_l.Fog[0] + (_l.Fog[1] - _l.Fog[0]) * 0.5, _l.Fog[1]));
        double fogMin = Math.Round(rng.Range(_l.Fog[0], fogMax));
        bool rain = rng.Chance(_l.RainChance);
        double cold = Math.Round(rng.Range(_l.Cold), 2);
        double wind = Math.Round(rng.Range(_l.Wind), 2);
        int step = (int)Math.Floor(cold * 3.999);
        double density = Math.Round(w.FogDensityPerInverseMetre / ((fogMin + fogMax) / 2), 4);
        _weather = new PlanWeather(fogMin, fogMax, rain, cold, wind, step, density);
    }

    /// <summary>
    /// §14's per-segment modifiers, from the tags: fog ×1.3 in low ground and marsh, ×0.8 on crests; wind ×1.5 where
    /// exposed; adhesion worse on wet-biased track in rain; a cold step per 150 m climbed and on exposed track.
    /// </summary>
    void LayExposure()
    {
        var w = _t.Weather;
        foreach (var e in _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch))
        {
            if (e.Role == EdgeRole.Spur)
                continue;
            var line = LineOf(e);
            double step = 100;
            PlanExposure? run = null;
            for (double s = 0; s < line.Length; s += step)
            {
                double mid = Math.Min(line.Length, s + step / 2);
                double fog = 1, wind = 1, adhesion = 1;
                int cold = 0;
                var why = new List<string>();
                bool water = BesideWater(e.Id, mid, line.Sample(mid).Position);
                if (HasTag("low_ground", e.Id, mid) || HasTag("marsh", e.Id, mid) || water)
                {
                    // Fog fills the coves first (maritime-rules §4): water is as foggy as the low ground, and the two don't stack.
                    fog *= Math.Max(HasTag("low_ground", e.Id, mid) || HasTag("marsh", e.Id, mid) ? w.FogLowGround : 1, water ? w.FogWater : 1);
                    why.Add(water ? "water" : "low");
                }
                else if (HasTag("crest", e.Id, mid))
                {
                    fog *= w.FogCrest;
                    why.Add("crest");
                }
                bool exposed = HasTag("exposed", e.Id, mid) || HasTag("bridge", e.Id, mid) || HasTag("ledge", e.Id, mid) || HasTag("open", e.Id, mid) || HasTag("crest", e.Id, mid);
                if (exposed)
                {
                    wind *= w.WindExposed;
                    cold += (int)w.ColdExposedStep;
                    why.Add("exposed");
                }
                bool wetBias = HasTag("wet_bias", e.Id, mid) || HasTag("dark_forest", e.Id, mid) || HasTag("tunnel_exit", e.Id, mid) || HasTag("cutting", e.Id, mid);
                if (_weather!.Rain)
                    adhesion = wetBias ? w.WetBiasAdhesion : w.WetAdhesion;
                if (wetBias)
                    why.Add("wet");
                cold += (int)Math.Max(0, Math.Floor(line.Sample(mid).Position.Y / w.ColdStepPerM));
                var seg = new PlanExposure(e.Id, R(s), R(Math.Min(line.Length, s + step)), fog, wind, adhesion, cold, string.Join(",", why));
                if (run is not null && run.Fog == seg.Fog && run.Wind == seg.Wind && run.Adhesion == seg.Adhesion && run.ColdStep == seg.ColdStep && run.Why == seg.Why)
                    run = run with { S1 = seg.S1 };
                else
                {
                    if (run is not null)
                        _exposure.Add(run);
                    run = seg;
                }
            }
            if (run is not null)
                _exposure.Add(run);
        }
    }

    /// <summary>Whether the track at <paramref name="s"/> runs along a shore, or within the weather's fogWaterM of a lake's edge (or across one).</summary>
    bool BesideWater(string edge, double s, Double3 at)
    {
        if (_shores.Any(sh => sh.Edge == edge && s >= sh.S0 && s <= sh.S1))
            return true;
        double reach = _t.Weather.FogWaterM;
        foreach (var lake in _lakes)
        {
            double far = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) + reach;
            if (Math.Abs(at.X - lake.X) <= far && Math.Abs(at.Z - lake.Z) <= far && (TerrainField.LakeMetric(lake, at.X, at.Z) - 1) * lake.RadiusM < reach)
                return true;
        }
        return false;
    }

    /// <summary>The worst adhesion fairness plans against over [a, b] (§14: worst-case weather, so rain later can't make obeying unsafe).</summary>
    double WorstAdhesion(string edge, double a, double b)
    {
        var w = _t.Weather;
        // Rain is possible any night the tier rains: plan for it wherever the track is wet-biased.
        double worst = _l.RainChance > 0 ? w.WetAdhesion : 1;
        foreach (var x in _exposure)
            if (x.Edge == edge && x.S1 >= a && x.S0 <= b && x.Why.Contains("wet") && _l.RainChance > 0)
                worst = Math.Min(worst, w.WetBiasAdhesion);
        return worst;
    }
}
