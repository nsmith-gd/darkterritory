using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>Stages 2–3 for the branches (plan §6.2, §8.2): alternates, dead lines and facility spurs, laid off the main line.</summary>
sealed partial class LineBuilder
{
    /// <summary>A turnout's two curves (out, and back to parallel) toward <paramref name="side"/> (+1 right), and back in.</summary>
    List<HPrim> TurnoutOut(int side, double radius, double length) =>
        [new HPrim(length, -side / radius, -side / radius), new HPrim(length, side / radius, side / radius)];

    List<HPrim> TurnoutIn(int side, double radius, double length) =>
        [new HPrim(length, side / radius, side / radius), new HPrim(length, -side / radius, -side / radius)];

    (double Advance, double Offset) TurnoutSize(double radius, double length) =>
        (2 * radius * DMath.Sin(length / radius), 2 * radius * (1 - DMath.Cos(length / radius)));

    Item Branchy(string type, double s0, double s1, List<HPrim> prims, bool level = true) => new()
    {
        Id = $"b{_itemCounter++}",
        Type = type,
        Kind = type,
        S0 = s0,
        S1 = s1,
        H = HShape.Straight,
        HardLevel = level,
        Grades = level ? [(0, 1, 0)] : null,
        Prims = prims,
    };

    /// <summary>
    /// An alternate (§6.2 step 4, §8.2): out through a turnout, round a bow of its own on the far side from the main
    /// line, carrying its trade-off, and back in through a trailing turnout onto the main line's pose at the rejoin,
    /// closed by a G1 connector.
    /// </summary>
    string? BuildAlternate(AltWindow w, int attempt)
    {
        var rng = Rng("alternate", w.Edge, attempt);
        var j = _t.Junctions;
        var e = new EdgeDraft { Id = w.Edge, Role = EdgeRole.Alternate, Toe = w.T, Side = w.Side, Rejoin = w.J };
        var pT = PoseOnMain(w.T);
        var pJ = PoseOnMain(w.J);
        double dx = pJ.X - pT.X, dz = pJ.Z - pT.Z;
        double chord = Math.Sqrt(dx * dx + dz * dz), window = w.J - w.T;
        var (adv, off) = TurnoutSize(j.TurnoutRadius, j.TurnoutLength);
        double wanted = w.Ratio * window;
        double least = chord * _t.Alternates.LengthOverChordMin * (1 + 0.04 * attempt);
        if (wanted < least)
        {
            if (w.Ratio < 1)
                Warn($"{w.Edge}: a {w.TradeOffId} can't be as short as {w.Ratio:0.00} of its window here; it's {least / window:0.00}");
            wanted = least;
        }
        double length = Math.Round(wanted);
        double turnout = 2 * j.TurnoutLength;
        double chordHeading = DMath.Atan2(-dx, -dz);
        double reserve = _t.Alternates.ClosureReserveM;
        double bodyEnd = length - turnout - reserve;
        // The bow is done by the end of the body, back alongside the main line for the closure to line it up with the
        // trailing turnout: over the body's length, it has to make the chord less what's left after it.
        double bodyChord = Math.Max(1, chord - (length - bodyEnd));
        double beta = BesselInverse(Math.Min(0.995, bodyChord / (bodyEnd - turnout)));
        // The bow's side: away from the main line's own bow, else as the graph chose.
        int side = w.MainBow != 0 ? -w.MainBow : w.Side;
        if (attempt % 2 == 1 && w.MainBow == 0)
            side = -side;
        e.Side = side;
        double Guide(double u) => chordHeading - side * beta * DMath.Sin(2 * Math.PI * Math.Clamp((u - turnout) / (bodyEnd - turnout), 0, 1));

        // The script: turnout, the trade-off in the middle, connectors, the closure reserve, the turnout back in.
        var items = new List<Item> { Branchy("turnout", 0, turnout, TurnoutOut(side, j.TurnoutRadius, j.TurnoutLength)) };
        var st = new Stretch { S0 = turnout, S1 = bodyEnd, Zone = "middle", Window = w };
        double s = turnout;
        var pieces = TradeOffPieces(w, ref rng);
        double pieceLength = 0;
        foreach (var id in pieces)
            pieceLength += Def(id).LengthM[0];
        double gap = Math.Max(150, (bodyEnd - turnout - pieceLength) / (pieces.Count + 1));
        foreach (var id in pieces)
        {
            double lead = Math.Min(gap, bodyEnd - s - Def(id).LengthM[0]);
            if (lead > 1)
            {
                items.Add(Connector(s, s + lead, st, ref rng));
                s += lead;
            }
            var piece = Piece(id, s, Math.Min(Def(id).LengthM[1], bodyEnd - s - 100), st, ref rng, null, null, purpose: $"{w.TradeOffId}:{id}");
            if (piece is null)
                continue;
            ApplyTradeOff(w, piece, ref rng);
            items.Add(piece);
            s = piece.S1;
        }
        if (bodyEnd - s > 1)
            items.Add(Connector(s, bodyEnd, st, ref rng));
        e.Items = items;

        // Horizontal: the turnout's prims are set; the rest follow the bow. Not just its heading: a bridge or a causeway
        // that can't turn lets the track drift off the bow, so each connector also steers back toward the bow's path.
        var reference = new List<Pose>();
        var rp = pT;
        for (double u = 0; u <= length + 10; u += 10)
        {
            reference.Add(rp);
            rp = Geometry.Advance(rp, new HPrim(10, 0, 0) with { K0 = 0, K1 = 0 });
            rp = rp with { Heading = Guide(u + 10) };
        }
        var pose = pT;
        foreach (var item in items)
        {
            if (item.Prims.Count == 0)
            {
                var here = pose;
                var at = reference[Math.Clamp((int)Math.Round(item.S0 / 10), 0, reference.Count - 1)];
                double cross = (here.X - at.X) * at.Right.X + (here.Z - at.Z) * at.Right.Z;
                // Right of the bow's path, turn left (heading up), over a few hundred metres.
                double correction = DMath.Atan2(cross, 700);
                Realise(item, ref pose, u => Guide(u) + Math.Clamp(correction, -0.6, 0.6), ref rng, _l.LineSpeed);
            }
            else
                pose = Geometry.Advance(pose, item.Prims);
        }
        // §8.2: close onto the pose the trailing turnout needs, parallel to the main line at the turnout's offset.
        var back = PoseOnMain(w.J - adv);
        var target = new Pose(back.X + back.Right.X * side * off, back.Z + back.Right.Z * side * off, back.Heading);
        var connector = Close(pose, target);
        _closures[e.Id] = (pose, target);
        if (connector is null)
            return $"{w.Edge} won't close onto the main line";
        double closeAt = items[^1].S1;
        var closure = Branchy("closure", closeAt, closeAt + connector.Sum(p => p.Length), connector, level: false);
        closure.Kind = "closure";
        items.Add(closure);
        items.Add(Branchy("turnout", closure.S1, closure.S1 + turnout, TurnoutIn(side, j.TurnoutRadius, j.TurnoutLength)));
        e.Prims = [.. items.SelectMany(i => i.Prims)];
        _edges[e.Id] = e;
        return Separated(e, _edges.Values.Where(o => o != e && _traces.ContainsKey(o.Id)));
    }

    /// <summary>§8.2's G1 connector at the largest radius that makes it, down to the tier's minimum.</summary>
    List<HPrim>? Close(Pose from, Pose to)
    {
        foreach (double factor in new[] { _t.Alignment.ConnectorRadiusFactor, 2.0, 1.6, 1.3, 1.0 })
        {
            double r = Math.Max(_l.MinRadius * factor, _l.MinRadius);
            var prims = Geometry.Connect(_t.Curves, from, to, r, _l.LineSpeed, 0.005);
            if (prims is not null && prims.Sum(p => p.Length) < 4000)
                return prims;
        }
        return null;
    }

    /// <summary>What an alternate carries for its trade-off (§6.2 step 4), plus a weak bridge or washout the graph put on it.</summary>
    List<string> TradeOffPieces(AltWindow w, ref Pcg32 rng)
    {
        var list = w.TradeOff.Pieces.Where(id => Def(id).MinD <= _p.D || id is "climb" or "riverCrossing" or "deadSettlement").ToList();
        if (w.TradeOffId == "highLine")
            list.Add("descent");
        if (w.Weak is not null && !list.Contains("trestle"))
            list.Insert(list.Count / 2, "trestle");
        // Low lines at the Local tier don't have causeways yet (D 1): a river crossing does the job.
        if (list.Count == 0)
            list.Add("riverCrossing");
        return list;
    }

    /// <summary>Makes a trade-off piece what the trade-off says: steeper, longer, one long tunnel, level.</summary>
    void ApplyTradeOff(AltWindow w, Item piece, ref Pcg32 rng)
    {
        double cap = w.WashoutOnMain ? _l.MainGrade : _l.BranchGrade;
        switch (piece.Kind)
        {
            case "climb" when w.TradeOffId == "highLine":
            case "descent" when w.TradeOffId == "highLine":
                {
                    // Steeper than the main line may go: the choice the route card spells out.
                    double g = cap * rng.Range(0.75, 1) * (piece.Kind == "climb" ? 1 : -1);
                    if (piece.Kind == "descent")
                        g = Math.Max(g, -_l.DescentGrade * 1.1);
                    piece.Grades = [(0, 1, Math.Round(g, 3))];
                    piece.Params["grade"] = Math.Round(g, 3);
                    break;
                }
            case "trestle" when w.Weak is { } weak:
                piece.Params["weakCars"] = weak.MaxCars;
                piece.Params["weakSpeed"] = weak.SpeedMs;
                break;
            case "tunnel" when w.TradeOffId == "tunnelCutoff":
                piece.Tags.Add("radio_blackout");
                break;
            case "settlement":
                piece.Params["town"] = 1;
                break;
        }
        if (w.TradeOff.Level && piece.Grades is null)
            piece.Grades = [(0, 1, 0)];
    }

    /// <summary>A dead line (§6.2 step 7): out through a turnout, curving away to 300 m clear within 2 km, to a buffer stop.</summary>
    string? BuildDeadLine(DeadLinePlan d, int attempt)
    {
        var rng = Rng("deadline", d.Edge, attempt);
        var j = _t.Junctions;
        int side = attempt % 2 == 1 ? -d.Side : d.Side;
        var e = new EdgeDraft { Id = d.Edge, Role = EdgeRole.DeadLine, Toe = d.Toe, Side = side };
        var p0 = PoseOnMain(d.Toe);
        double turnout = 2 * j.TurnoutLength;
        double away = rng.Range(_t.DeadLines.AngleDeg) * Math.PI / 180;
        // Curves a misrouted train can take at the line's speed (the Switchman costs the clock, never the train).
        double safe = _l.LineSpeed * _l.LineSpeed / _t.Curves.ADerail * 1.25;
        double radius = Math.Max(_l.MinRadius * 2, safe);
        var items = new List<Item> { Branchy("turnout", 0, turnout, TurnoutOut(side, j.TurnoutRadius, j.TurnoutLength)) };
        double turnLen = Geometry.TurnLength(_t.Curves, away, radius, _l.LineSpeed) + 40;
        var turn = new Item
        {
            Id = $"b{_itemCounter++}",
            Type = "connector",
            Kind = "straight",
            S0 = turnout,
            S1 = turnout + turnLen,
            H = HShape.Turn,
            Radius = radius,
            Deflection = away,
            DriftCap = _t.DeadLines.MaxGrade,
        };
        items.Add(turn);
        double s = turn.S1;
        var st = new Stretch { S0 = s, S1 = d.Length, Zone = "middle" };
        while (d.Length - s > 1)
        {
            double len = Math.Min(d.Length - s, rng.Range(400, 1200));
            if (d.Length - s - len < 200)
                len = d.Length - s;
            var c = Connector(s, s + len, st, ref rng);
            c.H = HShape.Free;
            c.MinRadius = radius;
            c.DriftCap = _t.DeadLines.MaxGrade;
            items.Add(c);
            s += len;
        }
        e.Items = items;
        var pose = p0;
        double heading0 = p0.Heading - side * away;
        foreach (var item in items)
        {
            if (item.Prims.Count == 0)
            {
                // The turn away is toward the branch's own side (right is a heading decrease).
                if (item.H == HShape.Turn)
                {
                    item.Prims = [.. Geometry.Turn(_t.Curves, -side * away, radius, _l.LineSpeed)];
                    item.Prims.Add(HPrim.Tangent(item.Length - item.Prims.Sum(p => p.Length)));
                    pose = Geometry.Advance(pose, item.Prims);
                }
                else
                    Realise(item, ref pose, _ => heading0, ref rng, _l.LineSpeed);
            }
            else
                pose = Geometry.Advance(pose, item.Prims);
        }
        e.Prims = [.. items.SelectMany(i => i.Prims)];
        _edges[e.Id] = e;
        return Separated(e, _edges.Values.Where(o => o != e && _traces.ContainsKey(o.Id)));
    }

    /// <summary>A facility's spur (§11.1, GDD §17): route.json's turnout kit off the level departure, and alongside to the buffer stop.</summary>
    void BuildSpur(FacilitySlot slot)
    {
        var k = _c.Route.Junctions;
        string id = $"spur{slot.Index + 1}";
        slot.SpurEdge = id;
        var e = new EdgeDraft { Id = id, Role = EdgeRole.Spur, Toe = slot.S, Side = slot.Side };
        double turnout = 2 * k.SpurDiverge;
        var items = new List<Item>
        {
            Branchy("turnout", 0, turnout, TurnoutOut(slot.Side, k.SpurRadius, k.SpurDiverge)),
            Branchy("spur", turnout, Math.Max(turnout + 10, slot.SpurLength), [HPrim.Tangent(Math.Max(10, slot.SpurLength - turnout))], level: slot.SpurGrade == 0),
        };
        if (slot.SpurGrade != 0)
            items[1].Grades = [(0, 1, slot.SpurGrade)];
        e.Items = items;
        e.Prims = [.. items.SelectMany(i => i.Prims)];
        _edges[id] = e;
        _traces[id] = (PoseOnMain(slot.S), Trace(PoseOnMain(slot.S), e.Prims));
    }
}
