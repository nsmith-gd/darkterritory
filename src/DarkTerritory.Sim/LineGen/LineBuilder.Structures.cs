using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 5/6a (plan §12.1, §12.3): the structures each piece carries (tunnels, bridges, causeways, walls, washouts,
/// brass, platforms) and the relief intent it writes for each side of the track: what the land does relative to the
/// rail, never where it is.
/// </summary>
sealed partial class LineBuilder
{
    void LayStructuresAndIntents()
    {
        var rng = Rng("structures");
        int tunnels = 0, bridges = 0;
        foreach (var e in _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch))
        {
            var spans = new List<PlanIntent>();
            foreach (var item in e.Items)
            {
                var (left, right) = IntentsFor(item, e, ref rng);
                // An S-bend's hill is on its first turn's inside, then on its second's (note 359): its land changes sides
                // halfway along the straight between.
                if (item.All().FirstOrDefault(i => i.Params.ContainsKey("sBend")) is { } sBend && SBendMiddle(sBend) is { } mid)
                {
                    spans.Add(new PlanIntent(e.Id, R(item.S0), R(mid), left, right, BiomeFor(e, item.S0)));
                    spans.Add(new PlanIntent(e.Id, R(mid), R(item.S1), right, left, BiomeFor(e, mid)));
                }
                else
                    spans.Add(new PlanIntent(e.Id, R(item.S0), R(item.S1), left, right, BiomeFor(e, item.S0)));
                foreach (var piece in item.All())
                    Structures(piece, e, ref rng, ref tunnels, ref bridges, spans);
            }
            // Junction pads (§12.2 step 4): flat either side of every junction on every edge that meets there.
            _intents.AddRange(Merge(spans));
            if (e.Role == EdgeRole.DeadLine)
                _structures.Add(new PlanStructure($"{e.Id}-stop", StructureType.BufferStop, e.Id, R(e.Length - 5), R(e.Length)));
        }
        // Washouts (§6.2 step 5): on one side of a fork, never a surprise (the junction's LINE CLOSED board, Form 19).
        foreach (var w in _alts)
        {
            double len = Math.Round(rng.Range(_t.Hazards.WashoutLengthM));
            if (w.WashoutOnMain)
            {
                double s = w.T + _t.Hazards.WashoutAfterJunctionM;
                _structures.Add(new PlanStructure($"{w.Edge}-washout", StructureType.Washout, "main", R(s), R(s + len)));
            }
            else if (w.WashoutOnAlt && _edges.TryGetValue(w.Edge, out var alt))
            {
                double s = Math.Min(_t.Hazards.WashoutAfterJunctionM, alt.Length / 2);
                _structures.Add(new PlanStructure($"{w.Edge}-washout", StructureType.Washout, w.Edge, R(s), R(s + len)));
            }
        }
    }

    static double R(double v) => Math.Round(v, 2);

    string BiomeFor(EdgeDraft e, double s) => BiomeAt(e.Role == EdgeRole.Main ? s : e.Toe + s * 0.8);

    /// <summary>Adjacent spans with the same intents become one.</summary>
    static IEnumerable<PlanIntent> Merge(List<PlanIntent> spans)
    {
        PlanIntent? run = null;
        foreach (var s in spans)
        {
            if (run is not null && run.Left == s.Left && run.Right == s.Right && run.Biome == s.Biome)
            {
                run = run with { S1 = s.S1 };
                continue;
            }
            if (run is not null)
                yield return run;
            run = s;
        }
        if (run is not null)
            yield return run;
    }

    /// <summary>§12.1's table: each piece's intent for each side. Connectors are plain land a metre or two either way.</summary>
    (SideIntent Left, SideIntent Right) IntentsFor(Item item, EdgeDraft e, ref Pcg32 rng)
    {
        SideIntent Plain(ref Pcg32 r) => new(IntentType.Plain, Math.Round(r.Range(-2, 2), 1));
        var pad = new SideIntent(IntentType.Pad, 0);
        if (item.Type is "fortress" or "pad" or "holding" or "stand" or "departure" or "home" or "arrival" or "turnout" or "spur")
            return (pad, pad);
        var top = item.All().FirstOrDefault(i => i.Kind is "tunnel" or "blindThroat" or "ledge" or "bend" or "trestle" or "river" or "causeway") ?? item;
        // A climb or descent laid round a hill carries a hard bend's land (note 278).
        switch (top.Params.ContainsKey("hardBend") ? "bend" : top.Kind)
        {
            case "blindThroat":
                {
                    double h = top.Params.GetValueOrDefault("wallM", 12);
                    return (new SideIntent(IntentType.Cutting, h), new SideIntent(IntentType.Cutting, Math.Round(h * rng.Range(0.8, 1.1), 1)));
                }
            case "ledge":
                {
                    // Cut into the slope on the curve's inside; the drop on the outside.
                    var up = new SideIntent(IntentType.LedgeUp, top.Params.GetValueOrDefault("upM", 25));
                    var drop = new SideIntent(IntentType.LedgeDrop, -top.Params.GetValueOrDefault("dropM", 35));
                    bool leftTurn = top.Prims.Sum(p => p.Deflection) > 0;
                    return leftTurn ? (up, drop) : (drop, up);
                }
            case "bend":
                {
                    // Note 278: the land says why the line bends: a hill it goes round on the inside (which also hides the
                    // way out of it), and the fall it would go off into on the outside.
                    var spur = new SideIntent(IntentType.LedgeUp, top.Params.GetValueOrDefault("spurM", 12));
                    var fall = new SideIntent(IntentType.Embankment, -top.Params.GetValueOrDefault("fallM", 3));
                    // An S-bend's by its first turn (its two cancel): the caller swaps them for its second.
                    bool leftTurn = top.Params.ContainsKey("sBend") ? top.Prims.FirstOrDefault(p => p.Deflection != 0).Deflection > 0 : top.Prims.Sum(p => p.Deflection) > 0;
                    return leftTurn ? (spur, fall) : (fall, spur);
                }
            case "tunnel":
                return (new SideIntent(IntentType.Mountain, _t.Terrain.TunnelCoverM), new SideIntent(IntentType.Mountain, _t.Terrain.TunnelCoverM));
            case "trestle":
                {
                    double h = top.Params.GetValueOrDefault("heightM", 25);
                    return (new SideIntent(IntentType.Ravine, -h), new SideIntent(IntentType.Ravine, -h));
                }
            case "river":
                {
                    double d = top.Params.GetValueOrDefault("depthM", 5);
                    return (new SideIntent(IntentType.River, -d), new SideIntent(IntentType.River, -d));
                }
            case "causeway":
                return (new SideIntent(IntentType.Marsh, -1.8), new SideIntent(IntentType.Marsh, -1.8));
        }
        return item.Kind switch
        {
            "climb" or "summit" when rng.Chance(0.4) => (new SideIntent(IntentType.Cutting, Math.Round(rng.Range(4, 9), 1)), new SideIntent(IntentType.Cutting, Math.Round(rng.Range(4, 9), 1))),
            "descent" or "drop" when rng.Chance(0.4) => (new SideIntent(IntentType.Embankment, -Math.Round(rng.Range(2, 6), 1)), new SideIntent(IntentType.Embankment, -Math.Round(rng.Range(2, 6), 1))),
            "momentum" => (new SideIntent(IntentType.Embankment, -3), new SideIntent(IntentType.Embankment, -3)),
            "settlement" => (pad, pad),
            _ => (Plain(ref rng), Plain(ref rng)),
        };
    }

    /// <summary>Halfway along an S-bend's straight between its turns (note 359); null if it was laid straight after all.</summary>
    static double? SBendMiddle(Item sBend)
    {
        double at = sBend.S0;
        bool turned = false;
        for (int i = 0; i < sBend.Prims.Count; i++)
        {
            var p = sBend.Prims[i];
            bool curved = p.K0 != 0 || p.K1 != 0;
            if (!curved && turned && sBend.Prims.Skip(i + 1).Any(q => q.K0 != 0 || q.K1 != 0))
                return at + p.Length / 2;
            turned |= curved;
            at += p.Length;
        }
        return null;
    }

    /// <summary>The structures a piece carries (§12.3), and the extra intents they want over part of it.</summary>
    void Structures(Item p, EdgeDraft e, ref Pcg32 rng, ref int tunnels, ref int bridges, List<PlanIntent> intents)
    {
        switch (p.Kind)
        {
            case "tunnel":
                {
                    double approach = p.Params.GetValueOrDefault("approachM", 150);
                    double a = p.S0 + approach, b = p.S1 - approach;
                    string material = _p.D < 1 ? "timber" : _p.D < 2.5 ? "stone" : "concrete";
                    _structures.Add(new PlanStructure($"tunnel{++tunnels}", StructureType.Tunnel, e.Id, R(a), R(b), 0, null, null, material));
                    // The approach cuttings deepen to meet the portal's face (§12.3).
                    Replace(intents, e.Id, p.S0, a, new SideIntent(IntentType.Cutting, 12), new SideIntent(IntentType.Cutting, 12));
                    Replace(intents, e.Id, b, p.S1, new SideIntent(IntentType.Cutting, 12), new SideIntent(IntentType.Cutting, 12));
                    break;
                }
            case "trestle":
                {
                    double span = p.Params.GetValueOrDefault("spanM", 120), mid = (p.S0 + p.S1) / 2;
                    double a = mid - span / 2, b = mid + span / 2;
                    WeakLimit? weak = p.Params.TryGetValue("weakCars", out var cars) ? new WeakLimit((int)cars, p.Params.GetValueOrDefault("weakSpeed", 8)) : null;
                    var type = weak is not null ? StructureType.Trestle : span > 250 ? StructureType.Viaduct : StructureType.Trestle;
                    _structures.Add(new PlanStructure($"bridge{++bridges}", type, e.Id, R(a), R(b), p.Params.GetValueOrDefault("heightM", 25), weak,
                        Material: weak is not null ? "timber" : type == StructureType.Viaduct ? "masonry" : "iron"));
                    // Embankments out to the abutments, the ravine only under the span.
                    Replace(intents, e.Id, p.S0, a, new SideIntent(IntentType.Embankment, -4), new SideIntent(IntentType.Embankment, -4));
                    Replace(intents, e.Id, b, p.S1, new SideIntent(IntentType.Embankment, -4), new SideIntent(IntentType.Embankment, -4));
                    break;
                }
            case "river":
                {
                    double span = p.Params.GetValueOrDefault("spanM", 80), mid = (p.S0 + p.S1) / 2, depth = p.Params.GetValueOrDefault("depthM", 5);
                    // A tidal river (maritime-rules.md §5): wide red mud banks, the channel deep at low water, crossed
                    // on a long iron truss (the Shubenacadie's, the Avon's).
                    bool tidal = _c.Config.Biomes.Biomes.TryGetValue(BiomeFor(e, mid), out var bd) && bd.TidalRivers;
                    if (tidal)
                        (span, depth) = (Math.Min(span * _t.Terrain.Shore.TidalSpanFactor, p.S1 - p.S0 - 200), depth * _t.Terrain.Shore.TidalDepthFactor);
                    double a = mid - span / 2, b = mid + span / 2;
                    _structures.Add(new PlanStructure($"bridge{++bridges}", span > 60 ? StructureType.Truss : StructureType.Girder, e.Id, R(a), R(b),
                        depth + 2, Material: "iron"));
                    // Flood-plain approaches: low ground, a metre or two under the rail.
                    Replace(intents, e.Id, p.S0, a, new SideIntent(IntentType.Plain, -2), new SideIntent(IntentType.Plain, -2));
                    Replace(intents, e.Id, b, p.S1, new SideIntent(IntentType.Plain, -2), new SideIntent(IntentType.Plain, -2));
                    if (tidal)
                        Replace(intents, e.Id, a, b, new SideIntent(IntentType.River, -depth), new SideIntent(IntentType.River, -depth));
                    double z = HeightOn(e, mid);
                    _water.Add(new PlanWater($"river{_water.Count + 1}", tidal ? "tidal" : "river", R(z - depth + (tidal ? 0.6 : 1.2)), e.Id, R(a), R(b),
                        tidal ? span * 0.45 : _t.Terrain.RiverWidthM));
                    break;
                }
            case "causeway":
                {
                    _structures.Add(new PlanStructure($"causeway{_structures.Count + 1}", StructureType.Causeway, e.Id, R(p.S0), R(p.S1), 1.5));
                    double z = HeightOn(e, (p.S0 + p.S1) / 2);
                    _water.Add(new PlanWater($"marsh{_water.Count + 1}", p.Tags.Contains("contaminated") ? "contaminatedMarsh" : "marsh", R(z - 1.5), e.Id, R(p.S0), R(p.S1), _t.Terrain.CorridorM));
                    break;
                }
            case "ledge":
                {
                    // A retaining wall where the up side's cut won't fit the slope beside the track (§12.3).
                    bool leftTurn = p.Prims.Sum(x => x.Deflection) > 0;
                    _structures.Add(new PlanStructure($"wall{_structures.Count + 1}", StructureType.RetainingWall, e.Id, R(p.S0 + 60), R(p.S1 - 60),
                        p.Params.GetValueOrDefault("upM", 20) * 0.4, Material: "masonry", Side: leftTurn ? -1 : 1));
                    break;
                }
            case "brass":
                {
                    double a = p.S0 + p.Params.GetValueOrDefault("rampM", 300), b = a + p.Params.GetValueOrDefault("fieldM", 100);
                    _structures.Add(new PlanStructure($"brass{_structures.Count + 1}", StructureType.BrassField, e.Id, R(a), R(b)));
                    break;
                }
            case "settlement":
                {
                    bool town = p.Params.GetValueOrDefault("town") > 0;
                    double mid = (p.S0 + p.S1) / 2, half = town ? 90 : 60;
                    int side = rng.Chance(0.5) ? 1 : -1;
                    _structures.Add(new PlanStructure($"platform{_structures.Count + 1}", StructureType.Platform, e.Id, R(mid - half), R(mid + half), 0.9, Side: side));
                    if (town)
                        _structures.Add(new PlanStructure($"platform{_structures.Count + 1}", StructureType.Platform, e.Id, R(mid - half), R(mid + half), 0.9, Side: -side));
                    break;
                }
        }
    }

    double HeightOn(EdgeDraft e, double s) => e.Role == EdgeRole.Main ? MainHeight(s) : e.Z0 + HeightAt(e.Grades, 0, s);

    /// <summary>Overwrites the intents over [a, b] of an edge's spans (cutting a span in two where needed).</summary>
    static void Replace(List<PlanIntent> spans, string edge, double a, double b, SideIntent left, SideIntent right)
    {
        if (b - a < 1)
            return;
        var result = new List<PlanIntent>();
        foreach (var s in spans)
        {
            if (s.S1 <= a || s.S0 >= b)
            {
                result.Add(s);
                continue;
            }
            if (s.S0 < a)
                result.Add(s with { S1 = R(a) });
            if (s.S1 > b)
                result.Add(s with { S0 = R(b) });
        }
        result.Add(new PlanIntent(edge, R(a), R(b), left, right, spans.FirstOrDefault(s => s.S0 <= a && s.S1 > a)?.Biome));
        result.Sort((x, y) => x.S0.CompareTo(y.S0));
        spans.Clear();
        spans.AddRange(result);
    }
}
