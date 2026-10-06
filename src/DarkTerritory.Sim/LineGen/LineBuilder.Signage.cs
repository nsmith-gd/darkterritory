using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// §9.5 tells and §9.7 paperwork. Required tells are never removed: the tier sets how many redundant ones there are
/// (a board, a repeat board, a Form 19 entry), and only non-required signage falls or goes missing. Deep territory
/// feels dark because nothing is repeated and the crew reads the paper and counts the posts.
/// </summary>
sealed partial class LineBuilder
{
    /// <summary>
    /// A speed as the boards and the paper give it: km/h, as the cab's speedometer reads (the HUD's), down to the five
    /// below so a crew holding the figure is always within the limit.
    /// </summary>
    static string Kmh(double ms) => $"{Math.Max(5, Math.Floor(ms * 3.6 / 5) * 5):0}";

    void LaySignage()
    {
        _signs.Clear();
        _form19.Clear();
        var rng = Rng("signage");
        var sg = _c.Config.Signage;
        int side = 1; // boards on the driver's side
        SignState State(bool required, ref Pcg32 r)
        {
            if (required || r.Chance(_l.SignageSurvival))
                return SignState.Intact;
            double fallen = sg.StateWeights.GetValueOrDefault("fallen", 0.5), missing = sg.StateWeights.GetValueOrDefault("missing", 0.5);
            return r.Chance(fallen / (fallen + missing)) ? SignState.Fallen : SignState.Missing;
        }
        void Sign(string type, string edge, double s, string text, bool required, double? value, string? of, ref Pcg32 r, int onSide = 0)
        {
            // A branch's board before its own points stands on the main line coming up to them.
            if (s < 0 && edge != "main")
                (edge, s) = ("main", _edges[edge].Toe + s);
            if (s < 0)
                return;
            _signs.Add(new PlanSign(type, edge, R(s), onSide == 0 ? side : onSide, text, State(required, ref r), required, value, of));
        }

        // Kilometre posts every 1 km, always required: the reference for all paperwork. Minor posts at the half.
        for (double km = 0; _gate + km * 1000 <= _terminus + 1; km += 0.5)
        {
            bool major = Math.Abs(km - Math.Round(km)) < 1e-9;
            Sign(major ? "kmPost" : "minorPost", "main", _gate + km * 1000 - 3, major ? $"{km:0}" : $"{km:0.0}", major, km, null, ref rng, -side);
        }
        foreach (var e in _edges.Values.Where(e => e.Role == EdgeRole.Alternate))
            for (double km = 1; km * 1000 < e.Length; km++)
                Sign("kmPost", e.Id, km * 1000, $"{e.Id.ToUpperInvariant()} {km:0}", true, km, null, ref rng, -side);

        // Each demand's tells, as the tier's redundancy has them (§9.5): 3 = board + repeat + Form 19; 2 = board +
        // Form 19; 1 = exactly one (a board, or for Sleeper country the paper alone).
        // T111 playtest: every curve that would derail the engine on full steam has its board, at its posted speed, even where
        // that's over the line speed (so no demand made one): the line speed's a rule, not a governor (T97). Always standing.
        foreach (var e in Routable())
        {
            var line = LineOf(e);
            var c = _t.Curves;
            double start = -1, minR = double.MaxValue;
            for (double s = 0; s <= line.Length + 5; s += 5)
            {
                double k = s <= line.Length ? Math.Abs(line.Sample(s).Curvature) : 0;
                if (k > 1e-9 && Math.Sqrt(c.ADerail / k) < c.BoardDerailBelow)
                {
                    if (start < 0)
                        start = s;
                    minR = Math.Min(minR, 1 / k);
                    continue;
                }
                if (start < 0)
                    continue;
                double posted = Math.Floor(Math.Sqrt(c.APost * minR));
                // A lower limit already over it has its own board (a demand's).
                if (!_limits.Any(l => l.Edge == e.Id && l.Source == LimitSource.Curve && l.S0 <= start + 5 && l.S1 >= start && l.VMs <= posted + 1e-6))
                    Sign("speedBoard", e.Id, start - _t.Authority.BoardBeforeM, Kmh(posted), true, posted, null, ref rng);
                start = -1;
                minR = double.MaxValue;
            }
        }

        foreach (var d in _demands)
        {
            double board = d.TellAt - _t.Authority.BoardBeforeM;
            string km = $"{Km(d.Edge == "main" ? d.SReq : _edges[d.Edge].Toe + d.SReq):0.0}";
            switch (d.Type)
            {
                case DemandType.Curve:
                case DemandType.WeakBridge:
                case DemandType.Brass:
                    {
                        var limit = _limits.First(l => l.Edge == d.Edge && Math.Abs(l.S0 - d.SReq) < 1);
                        bool paper = _l.Redundancy >= 2;
                        Sign("speedBoard", d.Edge, board, Kmh(d.VReq), true, d.VReq, d.Id, ref rng);
                        if (_l.Redundancy >= 3)
                            Sign("speedBoard", d.Edge, board - _c.Config.Signage.RepeatBoardGapM, Kmh(d.VReq), false, d.VReq, d.Id, ref rng);
                        Sign("resumeBoard", d.Edge, limit.S1, Kmh(Communicated(d.Edge, limit.S1 + 5)), false, Communicated(d.Edge, limit.S1 + 5), d.Id, ref rng);
                        if (d.Type == DemandType.WeakBridge && _structures.FirstOrDefault(s => s.Edge == d.Edge && Math.Abs(s.S0 - d.SReq) < 1) is { Weak: { } weak } bridge)
                        {
                            Sign("bridgeLimit", d.Edge, board + 5, $"MAX {weak.MaxCars} CARS", true, weak.MaxCars, d.Id, ref rng);
                            _form19.Add(new CardLine("bridge", Card(d.Edge, d.SReq), $"{bridge.Name}: max {weak.MaxCars} cars, {Kmh(weak.SpeedMs)} km/h"));
                        }
                        else
                        {
                            // Note 266: a limit that isn't a bend's says what it's for, so a "20" on straight track isn't
                            // taken for a bend that isn't there (build 1121).
                            if (d.Type == DemandType.Brass)
                                Sign("limitReason", d.Edge, board + 5, "BRASS", true, null, d.Id, ref rng);
                            if (paper)
                                _form19.Add(new CardLine("limit", Card(d.Edge, d.SReq), $"{Kmh(d.VReq)} km/h at km {km} ({limit.Why})", Card(d.Edge, limit.S1)));
                        }
                        break;
                    }
                case DemandType.Restricted:
                    {
                        var zone = _restricted.First(r => r.Edge == d.Edge && Math.Abs(r.S0 - d.SReq) < 1);
                        bool boardToo = _l.Redundancy >= 2 || rng.Chance(0.5);
                        if (boardToo)
                        {
                            Sign("restricted", d.Edge, board, "R", true, d.VReq, d.Id, ref rng);
                            Sign("endRestricted", d.Edge, zone.S1, "END R", false, null, d.Id, ref rng);
                        }
                        if (_l.Redundancy >= 2 || !boardToo)
                            _form19.Add(new CardLine("restricted", Card(d.Edge, zone.S0), $"Restricted speed {Kmh(zone.VMs)} km/h, km {Card(d.Edge, zone.S0):0.0} to {Card(d.Edge, zone.S1):0.0} ({zone.Reason})", Card(d.Edge, zone.S1)));
                        break;
                    }
                case DemandType.FacilityStop:
                    {
                        var slot = _facilities.First(f => Math.Abs((f.OnSpur ? f.S - 10 : f.S) - d.SReq) < 1);
                        // 2 km and 1 km out, and the far one farther if a heavy train needs more warning than that.
                        foreach (double before in new[] { _c.Config.Facilities.Slots.BoardFarM, _c.Config.Facilities.Slots.BoardNearM })
                        {
                            double at = before == _c.Config.Facilities.Slots.BoardFarM ? Math.Min(slot.S - before, board) : slot.S - before;
                            Sign("facility", "main", at, $"{slot.Name} {(slot.S - at) / 1000:0.0} KM", true, Math.Round((slot.S - at) / 1000, 1), d.Id, ref rng);
                        }
                        break;
                    }
                case DemandType.FacingJunction:
                    {
                        var node = _nodes.First(x => x.Type == NodeType.JunctionFacing && Math.Abs(x.S - d.SReq) < 1);
                        foreach (double before in new[] { _t.Junctions.BoardFarM, _t.Junctions.BoardNearM })
                        {
                            double at = before == _t.Junctions.BoardFarM ? Math.Min(d.SReq - before, board) : d.SReq - before;
                            Sign("junction", "main", at, $"J{node.Number} {(d.SReq - at) / 1000:0.0} KM", true, Math.Round((d.SReq - at) / 1000, 1), d.Id, ref rng);
                        }
                        break;
                    }
                case DemandType.Washout:
                    {
                        var w = _alts.First(a => a.Edge == (d.Edge == "main" ? _alts.First(x => x.WashoutOnMain && d.SReq > x.T && d.SReq < x.J).Edge : d.Edge));
                        // The washout itself is never a surprise: flag and board at the junction, and the paper.
                        Sign("lineClosed", "main", w.T - 40, "LINE CLOSED", true, null, d.Id, ref rng, w.WashoutOnMain ? side : w.Side);
                        string other = w.WashoutOnMain ? $"take the {Summary(w)} at J{w.Number}" : $"keep to the main line at J{w.Number}";
                        _form19.Add(new CardLine("washout", Card(d.Edge, d.SReq), $"Line closed km {Card(d.Edge, d.SReq):0.0} ({(w.WashoutOnMain ? "main line" : Summary(w))}), {other}"));
                        break;
                    }
                case DemandType.YardLimit:
                    Sign("yardLimit", "main", Math.Min(_terminus - _t.Terminus.YardLimitBoardM, board), "YARD LIMIT", true, _t.Terminus.YardLimitSpeed, d.Id, ref rng);
                    break;
            }
        }

        // Gradient posts: at the foot of any climb near g_main, at the top of any descent where fade matters.
        foreach (var e in Routable())
            foreach (var p in e.Items.SelectMany(i => i.All()).Where(i => i.Grades is not null))
                foreach (var (f0, _, g) in p.Grades!)
                {
                    double s = p.S0 + f0 * p.Length;
                    if (g >= sg.GradientPostMinShare * _l.MainGrade || -g >= sg.GradientPostMinShare * _l.DescentGrade)
                        Sign("gradientPost", e.Id, s - 20, $"{(g > 0 ? "UP" : "DOWN")} {Math.Abs(g):0.0}", true, g, p.Id, ref rng);
                }
        // Whistle boards before tunnels and dead settlements; bridge plates; station name boards; dead signals.
        foreach (var st in _structures)
        {
            if (st.Type == StructureType.Tunnel)
                Sign("whistle", st.Edge, st.S0 - sg.WhistleBeforeM, "W", false, null, st.Id, ref rng);
            if (st.Name is not null && st.Type != StructureType.Tunnel)
            {
                Sign("bridge", st.Edge, st.S0 - 8, st.Name, false, null, st.Id, ref rng);
                Sign("bridge", st.Edge, st.S1 + 8, st.Name, false, null, st.Id, ref rng, -side);
            }
        }
        foreach (var l in _landmarks.Where(l => l.Type is "halt" or "town"))
        {
            Sign("whistle", l.Edge, l.S0 - sg.WhistleBeforeM, "W", false, null, l.Name, ref rng);
            Sign("stationName", l.Edge, (l.S0 + l.S1) / 2, l.Name.ToUpperInvariant(), false, null, l.Name, ref rng, -side);
        }
        for (double s = _gate + 3000; s < _terminus - 2000; s += rng.Range(2500, 5000))
            Sign("deadSignal", "main", s, "", false, null, null, ref rng);
        _signs.Sort((a, b) => a.Edge != b.Edge ? string.CompareOrdinal(a.Edge, b.Edge) : a.S != b.S ? a.S.CompareTo(b.S) : string.CompareOrdinal(a.Type, b.Type));
    }

    /// <summary>Km on the card: the main line's from the gate; an alternate's from where it leaves.</summary>
    double Card(string edge, double s) => Math.Round(edge == "main" ? Km(s) : Km(_edges[edge].Toe) + s / 1000, 1);

    string Summary(AltWindow w) => w.TradeOffId switch
    {
        "highLine" => "high line",
        "lowLine" => "low line",
        "tunnelCutoff" => "tunnel cut-off",
        _ => "settlement line",
    };

    /// <summary>§9.7 the route card: timetable, Form 19 slow orders, the known grades.</summary>
    void LayRouteCard()
    {
        _timetable.Clear();
        _knownGrades.Clear();
        _timetable.Add(new CardLine("depart", 0, $"Depart {_fortress!.Name}, outer gate"));
        var places = new List<CardLine>();
        foreach (var l in _landmarks.Where(l => l.Edge == "main"))
            places.Add(new CardLine(l.Type, Card("main", l.S0), l.Name));
        foreach (var n in _nodes.Where(n => n.Type == NodeType.JunctionFacing))
        {
            var w = _alts.FirstOrDefault(a => Math.Abs(a.T - n.S) < 1);
            if (w is null)
                continue;
            var e = _edges[w.Edge];
            string structures = string.Join(", ", _structures.Where(s => s.Edge == w.Edge && s.Name is not null).Select(s => s.Name));
            double ruling = Ruling(LineOf(e));
            places.Add(new CardLine("alternate", Card("main", w.T),
                $"{char.ToUpperInvariant(Summary(w)[0])}{Summary(w)[1..]} via {n.Name!.Split(' ')[0]}: {w.TradeOff.Summary}, {e.Length / 1000:0.0} km, {ruling:0.0}% ruling"
                + (structures.Length > 0 ? $", {structures}" : "") + (n.DefaultEdge == w.Edge ? " (switch set for it: main line closed)" : ""), Card("main", w.J)));
        }
        places.Sort((a, b) => a.Km != b.Km ? a.Km.CompareTo(b.Km) : string.CompareOrdinal(a.Text, b.Text));
        _timetable.AddRange(places);
        _timetable.Add(new CardLine("terminus", Card("main", _terminus), $"Arrive {_terminusPlan!.Name}" + (_terminusPlan.Silent ? " (not answering)" : "")));
        _timetable.Add(new CardLine("dawn", Card("main", _terminus), $"Dawn in {_dawn / 60:0} minutes"));
        // Momentum banks on the paper (§9.7): rush them.
        foreach (var p in Main.Items.SelectMany(i => i.All()).Where(i => i.Kind == "momentum"))
            _form19.Add(new CardLine("momentum", Card("main", p.S0), $"Momentum bank km {Card("main", p.S0 + p.Params["runUpM"]):0.0}: {p.Params["grade"]:0.0}% for {p.Params["bankM"]:0} m, take it at line speed"));
        _form19.Sort((a, b) => a.Km.CompareTo(b.Km));
        _knownGrades.Add(new KnownGrade("main line", Math.Round(Ruling(_line!), 2), Math.Round((_terminus - _gate) / 1000, 1), true, $"line speed {Kmh(_l.LineSpeed)} km/h"));
        foreach (var w in _alts)
        {
            // The validator's verdict on it for the train that's leaving (§8.4: the route card must show a side it can't take).
            bool passable = _knownGradesPassable.GetValueOrDefault(w.Edge, true) && !w.WashoutOnAlt;
            string note = w.WashoutOnAlt ? "line closed" : passable ? w.TradeOff.Summary : $"{w.TradeOff.Summary}; not for {_p.Cars} cars loaded";
            _knownGrades.Add(new KnownGrade($"{Summary(w)} ({w.Edge})", Math.Round(Ruling(LineOf(w.Edge)), 2), Math.Round(_edges[w.Edge].Length / 1000, 1), passable, note));
        }
    }

    /// <summary>The ruling grade: the steepest climb sustained over 400 m.</summary>
    static double Ruling(RailLine line)
    {
        double worst = 0;
        for (double s = 0; s + 400 <= line.Length; s += 50)
            worst = Math.Max(worst, (line.Sample(s + 400).Position.Y - line.Sample(s).Position.Y) / 4);
        return worst;
    }
}
