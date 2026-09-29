using Ballast;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 5 (plan §10–11) with the naming of stage 7 (§13.3): the fortress and threshold, the facilities as handed to
/// the POI generator, the halts and dead towns, the terminus; every callable place named, and the route graph.
/// </summary>
sealed partial class LineBuilder
{
    readonly HashSet<string> _firstWords = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>§13.3: a name from its pattern, distinct in its first word within the run (so a callout can't be mistaken).</summary>
    string Name(string kind, ref Pcg32 rng, string? type = null, int number = 0)
    {
        var n = _c.Config.Names;
        var patterns = n.Patterns[kind];
        for (int tries = 0; tries < 60; tries++)
        {
            string pattern = rng.Pick(patterns);
            string text = pattern.Replace("{s}", rng.Pick(n.Surnames)).Replace("{f}", rng.Pick(n.Features)).Replace("{o}", rng.Pick(n.Owners))
                .Replace("{h}", rng.Pick(n.HaltSuffixes)).Replace("{k}", rng.Pick(n.BridgeKinds)).Replace("{t}", type ?? "").Replace("{n}", number.ToString());
            string first = text.Split(' ')[0];
            // "Mile 14 Halt": the number is the distinct part.
            if (first == "Mile")
                first = text;
            if (_firstWords.Add(first))
                return text.Trim();
        }
        return $"{kind} {number}";
    }

    void LayStations()
    {
        var rng = Rng("stations");
        var f = _t.Fortress;
        // The departure fortress (§10): template-assembled, a per-town identity.
        string fortressName = Name("fortress", ref rng);
        string identity = rng.Pick(f.Identities);
        var lights = new List<double[]>();
        for (double s = 60; s < _innerGate; s += 80)
            foreach (int side in new[] { -1, 1 })
                lights.Add(Light(s, side * 14, 9, 1));
        // Towers either side of the threshold, thinning out toward the outer gate (§10.2).
        for (double s = _innerGate; s < _gate; s += 120 + (s - _innerGate) * 0.25)
            foreach (int side in new[] { -1, 1 })
                lights.Add(Light(s, side * 16, 11, 0.8));
        lights.Add(Light(_gate + f.LastLightM, 6, 6, 0.6)); // the last light
        _fortress = new PlanFortress(fortressName, identity, R(_departureRoad), R(_innerGate), R(_gate), rng.RangeInclusive(f.ThroatSwitches[0], f.ThroatSwitches[1]), lights);
        _pads.Add(LongPad("fortress", 0, _gate, 70));
        _markers.Add(new PlanMarker("gate_inner", "main", R(_innerGate)));
        _markers.Add(new PlanMarker("gate_outer", "main", R(_gate)));
        _markers.Add(new PlanMarker("last_light", "main", R(_gate + f.LastLightM)));
        _markers.Add(new PlanMarker("kill_zone_end", "main", R(_gate + f.KillZoneM)));
        _markers.Add(new PlanMarker("dressing_full", "main", R(_gate + f.DressingTransitionM[1])));

        // The terminus (§11.4): live fortress or silent settlement, the sky glow before it, the home straight.
        var tt = _t.Terminus;
        bool silent = _p.Tier >= tt.SilentFromTier;
        if (silent)
            Warn($"§22.4: {Key(_p.Tier)} runs to a silent settlement; its gate is {(_t.Conflicts.SilentGateSafe ? "kept as a safe zone" : "not safe")} (design decides)");
        string terminusName = Name("fortress", ref rng);
        var tlights = new List<double[]>();
        for (double s = _terminus - tt.WallsResolveM; s < _end; s += 90)
            foreach (int side in new[] { -1, 1 })
                tlights.Add(Light(s, side * 14, 10, silent ? 0.05 : 1));
        _terminusPlan = new PlanTerminus(terminusName, silent, _t.Conflicts.SilentGateSafe, R(_terminus), R(_terminus - tt.SkyGlowM), R(_terminus - _t.Budget.HomeStraightM), tlights);
        _pads.Add(LongPad("terminus", _terminus - tt.WallsResolveM, _end, 70));
        _markers.Add(new PlanMarker("sky_glow", "main", R(_terminus - tt.SkyGlowM)));
        _markers.Add(new PlanMarker("home_straight", "main", R(_terminus - _t.Budget.HomeStraightM)));
        _markers.Add(new PlanMarker("yard_limit", "main", R(_terminus - tt.YardLimitBoardM)));
        _markers.Add(new PlanMarker("terminus_safe", "main", R(_terminus - tt.SpawnBanM)));
        _markers.Add(new PlanMarker("walls", "main", R(_terminus - tt.WallsResolveM)));
        _markers.Add(new PlanMarker("gate_terminus", "main", R(_terminus)));
        _routeName = $"{fortressName} to {terminusName}";

        // The facilities (§11.1), each handed to the POI generator: pad, junction, approach, holding, sub-seed.
        var r = _c.Config.Facilities.Slots;
        foreach (var slot in _facilities)
        {
            slot.Name = Name("facility", ref rng, slot.Def.Type);
            var at = _line!.Sample(slot.S);
            double z = at.Position.Y;
            // The pad beside the spur, on the facility's side, sized by the POI's scale.
            var right = new Double3(-at.Tangent.Z, 0, at.Tangent.X);
            double lateral = slot.OnSpur ? 8 + slot.PadRadius * 0.6 : 10 + slot.PadRadius * 0.5;
            var centre = at.Position + right * (slot.Side * lateral) + at.Tangent * (slot.OnSpur ? slot.SpurLength * 0.5 : 0);
            var pad = new PlanPad($"poi{slot.Index + 1}", R(centre.X), R(centre.Z), R(z), slot.PadRadius);
            _pads.Add(pad);
            var pickup = at.Position + right * (slot.Side * r.PickupFromTrackM) + at.Tangent * (slot.OnSpur ? 30 : 0);
            string node = $"fj{slot.Index + 1}";
            _pois.Add(new PlanPoi($"poi{slot.Index + 1}", slot.Kind, slot.Name, node, R(slot.S), slot.Side,
                new PlanRange("main", R(slot.LullStart), R(slot.S - slot.Holding)), new PlanRange("main", R(slot.S - slot.Holding), R(slot.S)), pad,
                slot.SpurEdge, slot.SpurGrade, $"0x{Streams.Mix(_seed, "poi", slot.Name, slot.Index):X16}",
                _c.Config.Facilities.PowerBiasByTier[Math.Min((int)_p.Tier, _c.Config.Facilities.PowerBiasByTier.Length - 1)],
                [R(pickup.X), R(z), R(pickup.Z)], slot.Def.MinePortal));
            _landmarks.Add(new PlanLandmark("facility", slot.Name, "main", R(slot.S - slot.Holding), R(slot.LullEnd), slot.Side));
            if (slot.Def.MinePortal && slot.SpurEdge is not null)
                _tags.Add(new PlanTag("mine_spur", slot.SpurEdge, 0, R(slot.SpurLength)));
        }

        // Halts and dead towns (§11.3): named, landmarks, flattened; a loot table left for design (off, §22.8).
        foreach (var e in _edges.Values.OrderBy(e => e.Role == EdgeRole.Main ? -1 : e.Branch))
            foreach (var item in e.Items.Where(i => i.Kind == "settlement"))
            {
                bool town = item.Params.GetValueOrDefault("town") > 0;
                int mile = (int)Math.Round(Km(e.Role == EdgeRole.Main ? item.S0 : e.Toe) / 1.609);
                string name = Name(town ? "town" : "halt", ref rng, number: mile);
                if (!town && !name.Split(' ')[^1].Equals("Halt") && !name.EndsWith("Siding") && !name.EndsWith("Crossing"))
                    name += " Halt";
                _landmarks.Add(new PlanLandmark(town ? "town" : "halt", name, e.Id, R(item.S0), R(item.S1), 0, _t.Conflicts.HaltScavenging));
                double mid = (item.S0 + item.S1) / 2;
                var line = LineOf(e);
                var p = line.Sample(mid);
                _pads.Add(new PlanPad($"town{_pads.Count}", R(p.Position.X), R(p.Position.Z), R(p.Position.Y), town ? 40 : 18, (item.Length) / 2,
                    Math.Round(Math.Atan2(-p.Tangent.X, -p.Tangent.Z) * 180 / Math.PI, 3)));
            }

        // Tunnels and bridges (§13.3): "Tunnel 2 — Blackwell", "Harrow Trestle".
        int tunnel = 0;
        for (int i = 0; i < _structures.Count; i++)
        {
            var st = _structures[i];
            if (st.Type == StructureType.Tunnel)
                _structures[i] = st with { Name = $"Tunnel {++tunnel} - {Name("tunnel", ref rng)}" };
            else if (st.Type is StructureType.Trestle or StructureType.Viaduct or StructureType.Girder or StructureType.Truss)
            {
                string kind = st.Type switch { StructureType.Viaduct => "Viaduct", StructureType.Trestle => "Trestle", StructureType.Truss => "Bridge", _ => "Girders" };
                string name = Name("bridge", ref rng);
                _structures[i] = st with { Name = string.Join(' ', name.Split(' ')[..^1].Append(kind)) };
            }
        }
        foreach (var st in _structures.Where(s => s.Name is not null))
            _landmarks.Add(new PlanLandmark(st.Type == StructureType.Tunnel ? "tunnel" : "bridge", st.Name!, st.Edge, st.S0, st.S1));

        LayGraph(ref rng);
    }

    PlanPad LongPad(string id, double s0, double s1, double radius)
    {
        var a = _line!.Sample(s0);
        var b = _line.Sample(s1);
        var mid = _line.Sample((s0 + s1) / 2);
        double heading = Math.Atan2(-(b.Position.X - a.Position.X), -(b.Position.Z - a.Position.Z)) * 180 / Math.PI;
        return new PlanPad(id, R(mid.Position.X), R(mid.Position.Z), R(mid.Position.Y), radius, R((s1 - s0) / 2), Math.Round(heading, 3));
    }

    /// <summary>A light beside the main line: [x, y, z, strength].</summary>
    double[] Light(double s, double lateral, double height, double strength)
    {
        var t = _line!.Sample(Math.Clamp(s, 0, _line.Length));
        var right = new Double3(-t.Tangent.Z, 0, t.Tangent.X);
        var p = t.Position + right * lateral;
        return [R(p.X), R(p.Y + height), R(p.Z), strength];
    }

    /// <summary>§6.1: the route graph's nodes and edges, junctions named and numbered (§13.3 "J4 — Carrow Junction").</summary>
    void LayGraph(ref Pcg32 rng)
    {
        _nodes.Add(new PlanNode("n0", NodeType.FortressDepart, R(_gate), _fortress!.Name));
        var mainEdge = new PlanEdge("main", EdgeRole.Main, "n0", "terminus", R(_end));
        foreach (var w in _alts)
        {
            string name = $"J{w.Number}" + (_p.Tier >= RouteTier.Frontier ? $" - {Name("junction", ref rng)}" : "");
            // The switch lies for the main line unless the main line's closed past it (§6.3, §6.2 step 5).
            _nodes.Add(new PlanNode($"j{w.Number}", NodeType.JunctionFacing, R(w.T), name, w.WashoutOnMain ? w.Edge : "main", w.Number));
            _nodes.Add(new PlanNode($"j{w.Number}t", NodeType.JunctionTrailing, R(w.J), name + " (trailing)", null, w.Number));
            _graphEdges.Add(new PlanEdge(w.Edge, EdgeRole.Alternate, $"j{w.Number}", $"j{w.Number}t", R(_edges[w.Edge].Length), _edges[w.Edge].Branch));
            _landmarks.Add(new PlanLandmark("junction", name, "main", R(w.T), R(w.T + 30)));
        }
        foreach (var d in _deads)
        {
            string name = $"J{d.Number}" + (_p.Tier >= RouteTier.Frontier ? $" - {Name("junction", ref rng)}" : "");
            _nodes.Add(new PlanNode($"j{d.Number}", NodeType.JunctionFacing, R(d.Toe), name, "main", d.Number));
            var e = _edges[d.Edge];
            string end = d.WreckYard ? Name("wreck", ref rng) : $"{d.Edge} buffer stop";
            _nodes.Add(new PlanNode($"{d.Edge}-end", NodeType.DeadEnd, R(e.Length), end));
            _graphEdges.Add(new PlanEdge(d.Edge, EdgeRole.DeadLine, $"j{d.Number}", $"{d.Edge}-end", R(e.Length), e.Branch));
            _landmarks.Add(new PlanLandmark("junction", name, "main", R(d.Toe), R(d.Toe + 30)));
            if (d.WreckYard)
                _landmarks.Add(new PlanLandmark("wreckYard", end, d.Edge, R(e.Length - 300), R(e.Length)));
        }
        foreach (var slot in _facilities)
        {
            _nodes.Add(new PlanNode($"fj{slot.Index + 1}", NodeType.FacilityJunction, R(slot.S), slot.Name, "main"));
            if (slot.SpurEdge is { } spur)
            {
                _nodes.Add(new PlanNode($"{spur}-end", NodeType.DeadEnd, R(_edges[spur].Length), $"{slot.Name} buffer stop"));
                _graphEdges.Add(new PlanEdge(spur, EdgeRole.Spur, $"fj{slot.Index + 1}", $"{spur}-end", R(_edges[spur].Length), _edges[spur].Branch));
            }
        }
        _nodes.Add(new PlanNode("terminus", NodeType.Terminus, R(_terminus), _terminusPlan!.Name));
        _graphEdges.Insert(0, mainEdge);
        _nodes.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : string.CompareOrdinal(a.Id, b.Id));
    }
}
