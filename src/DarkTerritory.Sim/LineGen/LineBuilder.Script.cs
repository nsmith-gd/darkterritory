using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>Stage 2 (plan §7): each edge's script of set pieces and connectors, spent against the budget curve.</summary>
sealed partial class LineBuilder
{
    /// <summary>A stretch of main line between the fixed zones, to be filled with a sawtooth.</summary>
    sealed class Stretch
    {
        public double S0, S1;
        public string Zone = "";
        public double Budget;
        public bool AfterFacility, BeforeStop, Spike;
        public AltWindow? Window;
        public FacilitySlot? NearFacility;
        public readonly List<string[]> Must = new();
        public readonly List<string> MustIds = new();
        public double Reserved;
        public double Length => S1 - S0;
    }

    /// <summary>A lineside region piece (§7.2 Dark Forest, Open Plain): no geometry of its own, it bends the connectors in it.</summary>
    sealed record Region(string Id, double S0, double S1, bool Straight);

    readonly List<Region> _regions = new();
    readonly List<Item> _regionItems = new();
    double _driftPhase1, _driftPhase2, _driftL1, _driftL2;
    double _budgetTotal, _budgetSpent;
    int _itemCounter;

    PieceDef Def(string id) => _c.Config.SetPieces.Pieces.First(p => p.Id == id);

    /// <summary>§8.3: the slow regional rise and fall of the land the set pieces sit on.</summary>
    double Drift(double s)
    {
        double a = _t.Profile.DriftAmplitudeM;
        return a * (0.6 * DMath.Sin(2 * Math.PI * s / _driftL1 + _driftPhase1) + 0.4 * DMath.Sin(2 * Math.PI * s / _driftL2 + _driftPhase2))
            - a * (0.6 * DMath.Sin(_driftPhase1) + 0.4 * DMath.Sin(_driftPhase2));
    }

    Item Fixed(string type, double s0, double s1, HShape h, bool level, double minRadius = 0, double driftCap = double.PositiveInfinity) => new()
    {
        Id = $"z{_itemCounter++}",
        Type = type,
        Kind = type,
        S0 = s0,
        S1 = s1,
        H = h,
        HardLevel = level,
        MinRadius = minRadius,
        Grades = level ? [(0, 1, 0)] : null,
        DriftCap = driftCap,
    };

    /// <summary>The main line's script: fixed zones from the graph, then every stretch between them filled.</summary>
    void ScriptMain()
    {
        var rng = Rng("script", "main");
        _driftL1 = rng.Range(_t.Profile.DriftWavelengthKm) * 1000;
        _driftL2 = rng.Range(_t.Profile.DriftWavelengthKm) * 1000 * 0.45;
        _driftPhase1 = rng.Range(0, 2 * Math.PI);
        _driftPhase2 = rng.Range(0, 2 * Math.PI);
        _budgetTotal = _l.BudgetPerKm * (_terminus - _gate) / 1000;

        var f = _t.Fortress;
        var items = new List<Item>
        {
            Fixed("fortress", 0, _gate, HShape.Straight, level: true),
            Fixed("threshold", _gate, _gate + _t.Budget.GraceM, HShape.Gentle, level: false, f.ThresholdMinRadius, f.MaxThresholdGrade),
        };
        var r = _c.Config.Facilities.Slots;
        foreach (var slot in _facilities)
        {
            if (slot.OnSpur)
            {
                items.Add(Fixed("approach", slot.LullStart, slot.S - slot.Holding, HShape.Straight, false, r.ApproachMinRadius, _t.Profile.ApproachGrade));
                items.Add(Fixed("holding", slot.S - slot.Holding, slot.S, HShape.Straight, level: true));
                items.Add(Fixed("departure", slot.S, slot.LullEnd, HShape.Straight, level: true));
            }
            else
            {
                // The tower's zone either side of the chute; the holding track (the whole train, level) runs up to it.
                double half = _c.Route.PoiZoneHalfLength, before = Math.Max(half, slot.Holding);
                items.Add(Fixed("approach", slot.LullStart, slot.S - before, HShape.Straight, false, r.ApproachMinRadius, _t.Profile.ApproachGrade));
                items.Add(Fixed("stand", slot.S - before, slot.S + half, HShape.Straight, level: true));
                items.Add(Fixed("departure", slot.S + half, slot.LullEnd, HShape.Straight, level: true));
            }
        }
        double pad = _t.Junctions.PadM;
        foreach (var a in _alts)
        {
            items.Add(Fixed("pad", a.T - pad, a.T + pad, HShape.Straight, level: true));
            items.Add(Fixed("pad", a.J - pad, a.J + pad, HShape.Straight, level: true));
        }
        foreach (var d in _deads)
            items.Add(Fixed("pad", d.Toe - pad, d.Toe + pad, HShape.Straight, level: true));
        items.Add(Fixed("home", _terminus - _t.Budget.HomeStraightM, _terminus, HShape.Gentle, level: true, _t.Terminus.HomeStraightMinRadius));
        items.Add(Fixed("arrival", _terminus, _end, HShape.Straight, level: true));
        items.Sort((x, y) => x.S0.CompareTo(y.S0));
        foreach (var (a, b) in items.Zip(items.Skip(1)))
            if (b.S0 < a.S1 - 1e-6)
                throw new InvalidOperationException($"fixed zones overlap: {a} and {b}");

        var stretches = Stretches(items);
        AssignMust(stretches, ref rng);
        var all = new List<Item>(items);
        foreach (var st in stretches)
        {
            var filled = Fill(st, ref rng);
            // Across a window where the main line bows round what its alternate cuts through, connectors are free to
            // turn and short enough to follow the bow (an open plain's straightness gives way to the hill).
            if (st.Window is { MainBow: not 0 })
                filled = [.. filled.SelectMany(i => Split(i, 450))];
            all.AddRange(filled);
        }
        all.Sort((x, y) => x.S0.CompareTo(y.S0));
        CarveBends(all);
        Main.Items = all;
    }

    /// <summary>
    /// Note 278: any share of the night still without its hard bend (the stretches were too full when the bends were
    /// handed out: each must reserves a whole recovery it seldom uses) gets one where the line already is: cut into a
    /// plain connector, with straight track either side, or laid on a climb, descent, summit or roller as the line
    /// going round a hill. Nearest the share's middle; the longest there. A share with no room borrows the nearest, which
    /// can be in the next share, and that one's then counted laid: what's still owed after the shares goes where the line
    /// has room, as far from the bends already laid as it can be (deepTerritory:4 was one short on its first attempt).
    /// </summary>
    void CarveBends(List<Item> all)
    {
        var rng = Rng("script", "carve");
        var def = Def("hardBend");
        // The straights either side as short as BendShape takes them where room is short.
        double from = _gate + _t.Budget.GraceM, to = _terminus - _t.Budget.HomeStraightM, run = def.Range("tangentM")[0] * 0.5;
        double least = 2 * run + def.LengthM[0];
        int n = _bendsWanted;
        double share = (to - from) / Math.Max(1, n);
        static bool Plain(Item c) => c is { Type: "recovery", Kind: "straight", H: HShape.Free or HShape.Straight } && !c.Params.ContainsKey("window");
        static bool Carries(Item c) => c is { IsPiece: true, Kind: "climb" or "descent" or "summit" or "roller", H: HShape.Free, Signature: null, Overlays.Count: 0 }
            && !c.Params.ContainsKey("hardBend");
        bool Room(Item c) => (Plain(c) || Carries(c)) && c.Length >= least && c.S0 >= from && c.S1 <= to;
        bool Lay(Item best, ref Pcg32 rng)
        {
            if (BendShape(def, best.Length - 2 * run, ref rng) is not { } fit)
                return false;
            if (Carries(best))
            {
                // The climb or descent goes round its hill: its grades as they were, its line one hard turn.
                Bend(best, fit);
                best.Cost += def.Cost;
            }
            else
            {
                var bend = Make(def, best.S0 + (best.Length - fit.Length) / 2, fit.Length);
                Bend(bend, fit);
                var after = new Item
                {
                    Id = $"c{_itemCounter++}",
                    Type = best.Type,
                    Kind = best.Kind,
                    Def = best.Def,
                    S0 = bend.S1,
                    S1 = best.S1,
                    H = best.H,
                    Wander = best.Wander,
                    MinRadius = best.MinRadius,
                    Params = new(best.Params),
                    Tags = new(best.Tags),
                    DriftCap = best.DriftCap,
                };
                best.S1 = bend.S0;
                all.InsertRange(all.IndexOf(best) + 1, [bend, after]);
            }
            _budgetSpent += def.Cost;
            return true;
        }
        int laid = all.Count(x => x.Params.ContainsKey("hardBend"));
        for (int i = 0; i < n && laid < n; i++)
        {
            double a = from + i * share, b = a + share, mid = (a + b) / 2;
            if (all.Any(x => x.Params.ContainsKey("hardBend") && (x.S0 + x.S1) / 2 >= a && (x.S0 + x.S1) / 2 < b))
                continue;
            Item? best = null;
            double bestScore = double.MaxValue;
            foreach (var c in all.Where(Room))
            {
                double off = Math.Max(0, Math.Max(c.S0 - mid, mid - c.S1)), score = Math.Max(0, off - share / 2) - c.Length * 0.01;
                if (score < bestScore)
                    (best, bestScore) = (c, score);
            }
            if (best is null)
            {
                Warn($"nowhere to lay a hard bend near km {Km(mid):0.0}");
                continue;
            }
            if (Lay(best, ref rng))
                laid++;
        }
        // Still owed: as far from the others as there's room, never nearer one than a share's third.
        while (laid < n)
        {
            var bends = all.Where(x => x.Params.ContainsKey("hardBend")).Select(x => (x.S0 + x.S1) / 2).ToList();
            Item? best = null;
            double far = share / 3;
            foreach (var c in all.Where(Room))
            {
                double d = bends.Count == 0 ? double.MaxValue : bends.Min(x => Math.Abs(x - (c.S0 + c.S1) / 2));
                if (d > far)
                    (best, far) = (c, d);
            }
            if (best is null || !Lay(best, ref rng))
                break;
            laid++;
        }
    }

    List<Stretch> Stretches(List<Item> fixedItems)
    {
        var list = new List<Stretch>();
        double spike0 = _terminus - _t.Budget.SpikeWindowM[0], spike1 = _terminus - _t.Budget.SpikeWindowM[1];
        void Add(double a, double b)
        {
            if (b - a < 1)
                return;
            // The spike window is its own stretch.
            if (a < spike0 && b > spike0 + 300)
            {
                Add(a, spike0);
                Add(spike0, b);
                return;
            }
            var st = new Stretch { S0 = a, S1 = b, Spike = a >= spike0 - 1 && b <= spike1 + 1 };
            st.Window = _alts.FirstOrDefault(w => a >= w.T && b <= w.J);
            double firstF = _facilities.Count > 0 ? _facilities[0].S : _gate + (_terminus - _gate) / 3;
            double lastF = _facilities.Count > 0 ? _facilities[^1].S : _gate + 2 * (_terminus - _gate) / 3;
            double mid = (a + b) / 2;
            st.Zone = mid < firstF ? "opening" : mid > lastF ? "final" : "middle";
            st.AfterFacility = _facilities.Any(f => Math.Abs(f.LullEnd - a) < 1);
            st.BeforeStop = _facilities.Any(f => Math.Abs(f.LullStart - b) < 1) || _alts.Any(w => Math.Abs(w.T - _t.Junctions.PadM - b) < 1);
            st.NearFacility = _facilities.FirstOrDefault(f => Math.Abs(f.LullStart - b) < 1 || Math.Abs(f.LullEnd - a) < 1);
            list.Add(st);
        }
        for (int i = 0; i + 1 < fixedItems.Count; i++)
            Add(fixedItems[i].S1, fixedItems[i + 1].S0);
        // §3.3 budget shares by zone, each zone's spread over its stretches by length; the spike takes a double share.
        var shares = new Dictionary<string, double> { ["opening"] = _t.Budget.Opening, ["middle"] = _t.Budget.Middle, ["final"] = _t.Budget.FinalApproach };
        foreach (var zone in shares.Keys)
        {
            var inZone = list.Where(s => s.Zone == zone).ToList();
            double weight = inZone.Sum(s => s.Length * (s.Spike ? 2 : 1));
            foreach (var s in inZone)
                s.Budget = weight <= 0 ? 0 : _budgetTotal * shares[zone] * s.Length * (s.Spike ? 2 : 1) / weight;
        }
        return list;
    }

    int StackCapAt(Stretch st) => st.Zone == "opening" ? 1 : _l.StackCap;

    /// <summary>
    /// What a run must contain (§15.3 quotas, the tier's counts of momentum banks, brass fields and weak bridges, §7.4's
    /// signature stacks, §11.4's terminal spike), handed out to stretches that can take them.
    /// </summary>
    void AssignMust(List<Stretch> stretches, ref Pcg32 rng)
    {
        var must = new List<(string Name, string[] Chain, Func<Stretch, bool> Fits, double MinLength)>();
        double D = _p.D;
        bool Allowed(string id) => Def(id.Split('+')[0]).MinD <= D && id.Split('+').All(x => Def(x).MinD <= D);

        // §11.4: the run's largest stack, deliberately, in the spike window.
        var spike = stretches.FirstOrDefault(s => s.Spike);
        var sigs = _c.Config.SetPieces.Signatures.Where(g => g.MinD <= D && g.Chain.All(Allowed)).ToList();
        if (spike is not null)
        {
            var fitting = sigs.Where(g => !g.AfterFacility && MinLen(g.Chain) <= spike.Length && Depth(g.Chain) <= StackCapAt(spike)).OrderByDescending(g => g.MinD).ToList();
            string[] chain = fitting.Count > 0 ? fitting[0].Chain : [BiggestCrunch(spike.Length)];
            spike.Must.Add(chain);
            spike.MustIds.Add(fitting.Count > 0 ? fitting[0].Id : "spike");
            spike.Reserved += MinLen(chain);
            if (fitting.Count > 0)
                sigs.Remove(fitting[0]);
        }
        // In order of need: the §15.3 quotas (acceptance checks, each tier adding to the ones before, the largest
        // count of each kind holding), the tier's counts of hazards, then the §7.4 signatures where there's room.
        var quotas = QuotaRows();
        var spikeChain = spike?.Must.FirstOrDefault() ?? [];
        if (quotas.Any(q => q.DeadSettlementNearFacility > 0) && _facilities.Count > 0)
            must.Add(("settlementNearFacility", ["deadSettlement"], s => s.NearFacility is not null && (s.BeforeStop || s.AfterFacility), 700));
        bool contaminated = quotas.Any(q => q.Tags.ContainsKey("contaminated"));
        // A causeway from D 2 is contaminated marsh: one serves both quotas.
        bool causewayContaminated = D >= Def("causeway").Param("contaminatedFromD");
        if (quotas.Any(q => q.Tags.ContainsKey("marsh_or_water")))
            must.Add(("water", [Def("causeway").MinD <= D && (contaminated && causewayContaminated || rng.Chance(0.5)) ? "causeway" : "riverCrossing"], _ => true, 900));
        if (contaminated && !(causewayContaminated && must.Any(m => m.Chain[0] == "causeway")))
            must.Add(("contaminated", ["causeway"], _ => true, 900));
        // The quotas are placed for themselves: what the spike carries is on top (its pieces may not all fit).
        int tunnels = quotas.Count == 0 ? 0 : quotas.Max(q => q.Tunnels);
        for (int i = 0; i < tunnels && Allowed("tunnel"); i++)
            must.Add(("tunnel", ["tunnel"], _ => true, 800));
        int climbLong = quotas.Count == 0 ? 0 : quotas.Max(q => q.Tags.GetValueOrDefault("climb_long"));
        for (int i = 0; i < climbLong; i++)
            must.Add(("climb_long", ["climb"], _ => true, 1500));
        if (quotas.Any(q => q.Tags.ContainsKey("climb")) && climbLong <= 0)
            must.Add(("climb", ["climb"], _ => true, 800));
        foreach (var _ in _mainWeakBridges)
            must.Add(("weakTrestle", ["trestle"], _ => true, 700));
        int momentum = D >= Def("momentumBank").MinD && _l.MomentumGrade > 0 ? rng.Count(_l.MomentumBanks[0], _l.MomentumBanks[1]) - (spikeChain.Any(c => c.Contains("momentumBank")) ? 1 : 0) : 0;
        for (int i = 0; i < momentum; i++)
            must.Add(("momentumBank", ["momentumBank"], s => s.Zone != "opening", 1600));
        int brass = D >= Def("brassField").MinD ? rng.Count(_l.BrassFields[0], _l.BrassFields[1]) : 0;
        for (int i = 0; i < brass; i++)
            must.Add(("brassField", ["brassField"], _ => true, 700));
        foreach (var g in sigs)
        {
            Func<Stretch, bool> fits = g.AfterFacility ? s => s.AfterFacility
                : g.Id == "junctionAtTheBottom" ? s => s.BeforeStop
                : _ => true;
            must.Add((g.Id, g.Chain, s => fits(s) && Depth(g.Chain) <= StackCapAt(s), MinLen(g.Chain)));
        }

        foreach (var m in must)
        {
            var candidates = stretches.Where(s => !s.Spike && m.Fits(s) && s.Length - s.Reserved - (s.Must.Count + 1) * RecoveryLength() >= m.MinLength).ToList();
            if (candidates.Count == 0)
                candidates = stretches.Where(s => m.Fits(s) && s.Length - s.Reserved >= m.MinLength).ToList();
            if (candidates.Count == 0)
            {
                // A signature is placed where the tier allows and there's room; a quota's absence fails validation.
                Warn($"no room on the main line for {m.Name}");
                continue;
            }
            // The roomiest, so the line stays spread out; ties broken by the stream.
            Stretch best = candidates[0];
            double bestRoom = double.MinValue;
            foreach (var c in candidates)
                if (c.Length - c.Reserved + rng.NextDouble() is var room && room > bestRoom)
                    (best, bestRoom) = (c, room);
            best.Must.Add(m.Chain);
            best.MustIds.Add(m.Name);
            best.Reserved += m.MinLength + RecoveryLength();
        }
        AssignBends(stretches);
    }

    /// <summary>
    /// Note 278: the night's hard bends, bends that derail the train under its top speed, spread along it: one in each
    /// equal share of the line between the threshold and the home straight, in the stretch with room nearest the
    /// share's middle. After everything else, so they take the room the quotas and signatures leave (the bends are what
    /// the validator holds the night to; a signature only goes where there's room). Counted on their own stream: how many
    /// a night has is the tier's, whatever else the script rolled.
    /// </summary>
    void AssignBends(List<Stretch> stretches)
    {
        var rng = Rng("script", "bends");
        _bendsWanted = rng.Count(_l.Bends[0], _l.Bends[1]);
        // The piece, and the shortest connector Fill leads into it with.
        double from = _gate + _t.Budget.GraceM, to = _terminus - _t.Budget.HomeStraightM, least = MinLen(["hardBend"]) + 150;
        for (int i = 0; i < _bendsWanted; i++)
        {
            double target = from + (i + 0.5) * (to - from) / _bendsWanted;
            var candidates = stretches.Where(s => !s.Spike && s.Length - s.Reserved - (s.Must.Count + 1) * RecoveryLength() >= least).ToList();
            if (candidates.Count == 0)
                candidates = stretches.Where(s => s.Length - s.Reserved >= least).ToList();
            // None: CarveBends lays it where the line already is.
            if (candidates.Count == 0)
                continue;
            Stretch best = candidates[0];
            double bestOff = double.MaxValue;
            foreach (var c in candidates)
                if (Math.Max(0, Math.Max(c.S0 - target, target - c.S1)) + rng.NextDouble() is var off && off < bestOff)
                    (best, bestOff) = (c, off);
            best.Must.Add(["hardBend"]);
            best.MustIds.Add("hardBend");
            // The run-up a crowded stretch still gives before a piece (Fill's recovery shortens to a floor before a must is
            // squeezed out); the bend isn't a crunch, so it owes no recovery after it.
            best.Reserved += least + Math.Min(RecoveryLength(), 400);
        }
    }

    /// <summary>
    /// Note 278: which way a hard bend over <paramref name="s0"/>..<paramref name="s1"/> must turn, or 0 for either. Past a
    /// dead line's toe, away from it: the main line leaves it behind. In an alternate's window, towards the alternate's side:
    /// a line that turns only one way lies the other side of its chord, and the alternate bows out on its own side of that
    /// chord, so the two stay apart; turned away, the main line bowed over towards the alternate and crossed its way back in
    /// (frontier:7). The separation check lets track be within 2 km of a junction two edges share, so <see cref="CrossesMain"/>
    /// refuses what this misses. A branch's side, right +1 (its turnout first bends right); turning away from the right is a
    /// left turn, a positive deflection.
    /// </summary>
    int AwayFromBranches(double s0, double s1)
    {
        foreach (var w in _alts)
            if (s1 > w.T - 200 && s0 < w.J + 200)
                return w.MainBow != 0 ? w.MainBow : -w.Side;
        foreach (var d in _deads)
            if (s1 > d.Toe - 200 && s0 < d.Toe + _t.Curves.BendDeadLineClearM)
                return d.Side;
        return 0;
    }

    double MinLen(string[] chain) => chain.Sum(c => c.Split('+').Max(x => Def(x).LengthM[0])) + 200;

    List<QuotaRow> QuotaRows()
    {
        var rows = new List<QuotaRow>();
        foreach (var t in Enum.GetValues<Route.RouteTier>().Where(t => t <= _p.Tier))
            if (_t.Director.Quotas.TryGetValue(Key(t), out var q))
                rows.Add(q);
        return rows;
    }

    static int Depth(string[] chain) => chain.Max(c => c.Split('+').Length) + (chain.Length > 1 ? 1 : 0);

    string BiggestCrunch(double room)
    {
        var options = _c.Config.SetPieces.Pieces.Where(p => p.Crunch && p.MinD <= _p.D && p.LengthM[0] + 200 <= room && p.Weight > 0).OrderByDescending(p => p.Cost).ToList();
        return options.Count > 0 ? options[0].Id : "climb";
    }

    double RecoveryLength() => _l.RecoveryMin;

    /// <summary>§7.1's sawtooth: recovery connector, build-up, crunch or stack, recovery, over the stretch.</summary>
    List<Item> Fill(Stretch st, ref Pcg32 rng)
    {
        var list = new List<Item>();
        double s = st.S0, budget = st.Budget;
        LayRegion(st, ref rng);
        var queue = new List<(string[] Chain, string Id)>(st.Must.Zip(st.MustIds));
        // Hounds' Hill starts as the train pulls out of the facility; Junction at the Bottom ends at the next stop's approach.
        var first = queue.FirstOrDefault(q => q.Id == "houndsHill" || q.Id == "settlementNearFacility" && st.AfterFacility && !st.BeforeStop);
        var last = queue.FirstOrDefault(q => q.Id == "junctionAtTheBottom" || q.Id == "settlementNearFacility" && st.BeforeStop);
        if (first.Chain is not null)
            queue.Remove(first);
        if (last.Chain is not null)
            queue.Remove(last);
        double lastLen = 0;
        List<Item>? lastUnit = null;
        if (last.Chain is not null)
        {
            lastUnit = Unit(last.Chain, last.Id, 0, st.Length * 0.5, st, ref rng);
            lastLen = lastUnit?.Sum(i => i.Length) ?? 0;
        }
        double end = st.S1 - lastLen;
        if (first.Chain is not null && Unit(first.Chain, first.Id, s, end - s, st, ref rng) is { } opener)
        {
            Place(opener, list, ref s, ref budget);
            // Signature tags: the climb out of the stop.
            opener[0].Tags.Add("climb_long");
        }
        // §11.3: every 4–8 km of main line, counted along the whole line (a stretch is often shorter than that).
        if (double.IsNaN(_nextSettlement))
            _nextSettlement = NextSettlement(_gate + _t.Budget.GraceM, ref rng);
        int guard = 0;
        while (end - s > 1 && guard++ < 200)
        {
            double room = end - s;
            // Recovery: a connector long enough to outrun what's chasing (§7.1), shorter only when the stretch is.
            double rec = Math.Min(room, list.Count == 0 && first.Chain is null ? rng.Range(200, 700) : rng.Range(RecoveryLength(), RecoveryLength() * 1.5));
            // What must still go in this stretch keeps its room: recovery shortens (to a floor) before a must is squeezed out.
            double owed = Owed(queue);
            if (queue.Count > 0)
                rec = Math.Max(Math.Min(rec, room - owed), Math.Min(room, 150));
            bool anyLeft = queue.Count > 0 || budget > 0.4;
            if (!anyLeft || room < rec + 400 && queue.Count == 0)
                rec = room;
            list.Add(Connector(s, s + rec, st, ref rng));
            s += rec;
            if (end - s < 300)
                break;
            // Dead settlements every 4-8 km (§11.3), and one within 2 km of a facility from the Frontier on (§15.3).
            if (s >= _nextSettlement && st.Window is null)
            {
                if (Settlement(s, end - s, ref rng) is { } town)
                {
                    Place([town], list, ref s, ref budget);
                    _nextSettlement = NextSettlement(s, ref rng);
                    continue;
                }
            }
            List<Item>? unit = null;
            if (queue.Count > 0)
            {
                var (chain, id) = queue[0];
                queue.RemoveAt(0);
                // Sized to leave what's still queued its room (a piece takes as much as it's given, up to its longest).
                unit = Unit(chain, id, s, end - s - Owed(queue) - Math.Min(200, RecoveryLength()), st, ref rng);
                if (unit is null)
                    Warn($"{id} didn't fit its stretch at km {Km(s):0.0}");
            }
            else if (budget > 0.4)
                unit = RandomUnit(s, end - s - Math.Min(300, RecoveryLength()), budget, st, ref rng);
            if (unit is not null)
                Place(unit, list, ref s, ref budget);
        }
        foreach (var (_, id) in queue)
            Warn($"{id} didn't fit its stretch at km {Km(st.S0):0.0}");
        if (lastUnit is not null)
        {
            double at = st.S1 - lastLen;
            if (list.Count > 0 && list[^1].S1 < at)
                list.Add(Connector(list[^1].S1, at, st, ref rng));
            Shift(lastUnit, at - lastUnit[0].S0);
            Place(lastUnit, list, ref at, ref budget);
            s = at;
        }
        // Close any gap left at the end.
        double tail = list.Count > 0 ? list[^1].S1 : st.S0;
        if (st.S1 - tail > 1e-6)
        {
            if (list.Count > 0 && list[^1].Type is "recovery" or "connector" && list[^1].H == HShape.Free)
                list[^1].S1 = st.S1;
            else
                list.Add(Connector(tail, st.S1, st, ref rng));
        }
        _budgetSpent += st.Budget - budget;
        return list;
    }


    IEnumerable<Item> Split(Item i, double most)
    {
        if (i.Type != "recovery" || i.H is not (HShape.Free or HShape.Straight) || i.Length <= most * 1.5)
        {
            if (i.Type == "recovery" && i.H == HShape.Straight)
                i.H = HShape.Free;
            yield return i;
            yield break;
        }
        int n = (int)Math.Ceiling(i.Length / most);
        double step = i.Length / n;
        for (int k = 0; k < n; k++)
            yield return new Item
            {
                Id = $"c{_itemCounter++}",
                Type = i.Type,
                Kind = i.Kind,
                Def = i.Def,
                S0 = i.S0 + k * step,
                S1 = k == n - 1 ? i.S1 : i.S0 + (k + 1) * step,
                H = HShape.Free,
                Wander = i.Wander,
                MinRadius = i.MinRadius,
                Params = new(i.Params),
            };
    }

    double _nextSettlement = double.NaN;

    /// <summary>The least a must-place unit needs: its pieces' shortest (a long climb's 1.2 km and a bit).</summary>
    double MustLength(string id, string[] chain) => id == "climb_long" ? 1400 : chain.Sum(c => c.Split('+').Max(x => Def(x).LengthM[0])) + 100;

    /// <summary>The room still owed to queued must-place units, and the recovery between them.</summary>
    double Owed(List<(string[] Chain, string Id)> queue) => queue.Sum(q => MustLength(q.Id, q.Chain)) + queue.Count * Math.Min(RecoveryLength(), 400);

    double NextSettlement(double s, ref Pcg32 rng) => s + rng.Range(Def("deadSettlement").Range("everyKm")) * 1000;

    static void Shift(List<Item> unit, double by)
    {
        foreach (var i in unit.SelectMany(u => u.All()))
        {
            i.S0 += by;
            i.S1 += by;
        }
    }

    void Place(List<Item> unit, List<Item> into, ref double s, ref double budget)
    {
        foreach (var i in unit)
        {
            into.Add(i);
            _estimate.Add(i);
            s = i.S1;
            foreach (var x in i.All())
                budget -= x.Cost;
        }
    }

    /// <summary>§7.2 Dark Forest / Open Plain over part of a stretch: at least one somewhere on every line (§15.3 Local).</summary>
    void LayRegion(Stretch st, ref Pcg32 rng)
    {
        if (st.Length < 1200 || (_regions.Count > 0 && !rng.Chance(0.4)))
            return;
        string biome = BiomeAt((st.S0 + st.S1) / 2);
        double forest = Bias(biome, "darkForest"), open = Bias(biome, "openPlain");
        string id = rng.Chance(forest / (forest + open)) ? "darkForest" : "openPlain";
        var def = Def(id);
        double len = Math.Min(st.Length, rng.Range(def.LengthM));
        double s0 = st.S0 + rng.Range(0, st.Length - len);
        _regions.Add(new Region(id, s0, s0 + len, id == "openPlain"));
        _regionItems.Add(new Item { Id = $"r{_itemCounter++}", Type = id, Kind = "region", Def = def, S0 = s0, S1 = s0 + len, Tags = [.. def.Tags], Cost = def.Cost });
    }

    double Bias(string biome, string piece) =>
        _c.Config.Biomes.Biomes.TryGetValue(biome, out var b) && b.Pieces.TryGetValue(piece, out var w) ? w : 1;

    /// <summary>A connector (§7.1): straight or with low wander, curvier in dark forest and dead straight on open plain.</summary>
    Item Connector(double s0, double s1, Stretch st, ref Pcg32 rng)
    {
        var region = _regions.FirstOrDefault(r => s0 < r.S1 && s1 > r.S0);
        var item = new Item
        {
            Id = $"c{_itemCounter++}",
            Type = "recovery",
            Kind = "straight",
            S0 = s0,
            S1 = s1,
            Def = Def("straight"),
            H = region?.Straight == true ? HShape.Straight : HShape.Free,
            Wander = rng.Range(_t.Alignment.WanderDeg),
            MinRadius = _t.Alignment.WanderMinRadius,
        };
        // In the black forest the connectors curve more: a sweep where there's room (§7.5). So do they where the biome
        // keeps to the coast or the contours (maritime-rules.md §7: the South Shore line heading every cove, the Island
        // railway going round every hill).
        double curve = region is { Straight: false } ? Def(region.Id).Param("curveBias")
            : region is null && _c.Config.Biomes.Biomes.TryGetValue(BiomeAt((s0 + s1) / 2), out var bd) ? bd.SweepChance : 0;
        if (s1 - s0 > 500 && curve > 0 && rng.Chance(curve))
        {
            var sweep = Def("sweep");
            item.Kind = "sweep";
            item.Def = sweep;
            item.H = HShape.Turn;
            item.Radius = Math.Max(_l.MinRadius * 2, rng.Range(sweep.Range("radius")));
            item.Deflection = rng.Range(sweep.Range("deflectionDeg")) * Math.PI / 180;
            item.Tags.Add("curve_gentle");
        }
        if (st.Window is { } w)
        {
            item.Params["window"] = w.Index;
            item.DriftCap = 0.15;
        }
        return item;
    }

    Item? Settlement(double s, double room, ref Pcg32 rng)
    {
        var def = Def("deadSettlement");
        bool town = rng.Chance(def.Param("townChance"));
        double len = town ? rng.Range(def.Range("townM")) : def.Param("haltM");
        // A town that won't fit is a halt.
        if (town && len + 200 > room)
            (town, len) = (false, def.Param("haltM"));
        if (len + 200 > room)
            return null;
        var item = Make(def, s, len);
        item.Params["town"] = town ? 1 : 0;
        item.H = HShape.Straight;
        item.Grades = [(0, 1, 0)];
        return item;
    }

    Item Make(PieceDef def, double s, double len) => new()
    {
        Id = $"p{_itemCounter++}",
        Type = def.Id,
        Kind = def.Kind,
        Def = def,
        S0 = s,
        S1 = s + Math.Round(len),
        Tags = [.. def.Tags],
        Cost = def.Cost,
    };

    /// <summary>A random unit for what's left of the budget: a build-up piece and a crunch, or a stack where the tier allows.</summary>
    List<Item>? RandomUnit(double s, double room, double budget, Stretch st, ref Pcg32 rng)
    {
        string biome = BiomeAt(s);
        var options = _c.Config.SetPieces.Pieces
            .Where(p => p.Weight > 0 && p.Kind is not ("straight" or "sweep" or "region" or "settlement") && p.MinD <= _p.D && p.LengthM[0] + 100 <= room)
            .Select(p => (p, p.Weight * Bias(biome, p.Id) * Bias(_region, p.Id))).ToList();
        if (options.Count == 0)
            return null;
        // A window's main side keeps to its flavour (§6.2: every alternate differs in what it trades), and comes out
        // near the height it went in at, so its alternate can meet it again within its grades: summits and rollers
        // rather than climbs or descents.
        if (st.Window is { } w)
            options = [.. options.Where(o => o.p.Kind is not ("climb" or "descent" or "drop" or "momentum") && (w.TradeOff.Level || o.p.Kind != "tunnel"))];
        if (options.Count == 0)
            return null;
        var pick = rng.Weighted(options.Select(o => (o.p, o.Item2)).ToList())!;
        string[] chain = [pick.Id];
        // Stacks where allowed (§7.3): a Blind Throat or a brass field on a grade, a tunnel on a climb.
        if (StackCapAt(st) >= 2 && rng.Chance(0.35))
        {
            if (pick.Kind == "descent" && _p.D >= Def("blindThroat").MinD)
                chain = ["descent+blindThroat"];
            else if (pick.Kind == "climb" && _p.D >= Def("tunnel").MinD && rng.Chance(0.5))
                chain = ["climb+tunnel"];
        }
        // Build-up before a crunch: a climb or descent leading in.
        else if (pick.Crunch && room > pick.LengthM[0] + 1400 && budget > pick.Cost + 1 && rng.Chance(0.5))
            chain = [rng.Chance(0.5) ? "climb" : "descent", pick.Id];
        return Unit(chain, pick.Id, s, room, st, ref rng);
    }

    /// <summary>Lays a chain of pieces from <paramref name="s"/>: "a+b" puts b over a's range (a stack).</summary>
    List<Item>? Unit(string[] chain, string id, double s, double room, Stretch st, ref Pcg32 rng)
    {
        var unit = new List<Item>();
        double at = s;
        for (int i = 0; i < chain.Length; i++)
        {
            var parts = chain[i].Split('+');
            double left = s + room - at - (chain.Length - 1 - i) * 400;
            string? signature = _c.Config.SetPieces.Signatures.Any(g => g.Id == id) ? id : null;
            var baseItem = Piece(parts[0], at, left, st, ref rng, stackedOn: null, signature, purpose: id);
            if (baseItem is null)
                return unit.Count > 0 ? unit : null;
            foreach (var over in parts.Skip(1))
            {
                var top = Piece(over, baseItem.S0, baseItem.Length, st, ref rng, stackedOn: baseItem, signature, purpose: id);
                if (top is not null)
                {
                    baseItem.Overlays.Add(top);
                    top.Depth = baseItem.Depth = 2;
                    top.Cost *= 1 + 0.5 * (top.Depth - 1);
                }
            }
            unit.Add(baseItem);
            at = baseItem.S1;
            // One after another inside each other's demand windows: a short gap, and the next.
            if (i < chain.Length - 1)
            {
                double gap = id == "dropIntoHighIron" ? rng.Range(80, 280) : rng.Range(100, 300);
                if (at + gap > s + room)
                    break;
                unit.Add(Connector(at, at + gap, st, ref rng));
                at += gap;
            }
        }
        return unit;
    }

    /// <summary>
    /// A set piece's parameters (§7.2), fitted to <paramref name="room"/>. Grades are laid here, so the estimate of the
    /// line's height can choose climbs or descents to keep it near the regional drift.
    /// </summary>
    /// <param name="purpose">What it was asked for: a must-place's name (a quota, a weak bridge), a signature, or its own id.</param>
    Item? Piece(string id, double s, double room, Stretch? st, ref Pcg32 rng, Item? stackedOn, string? signature, string purpose)
    {
        var def = Def(id);
        double min = def.LengthM[0], max = Math.Min(def.LengthM[1], room);
        if (stackedOn is not null)
            max = Math.Min(max, stackedOn.Length - 100);
        if (max < min)
        {
            if (stackedOn is null || max < min * 0.6)
                return null;
            min = max;
        }
        double len = rng.Range(min, max);
        double g = _l.MainGrade;
        Item item;
        double zNow = EstimateHeight(s);
        bool preferDown = zNow - Drift(s) > 0;
        switch (def.Kind)
        {
            case "climb":
            case "descent":
                {
                    bool up = def.Kind == "climb";
                    if (purpose is "climb_long" or "houndsHill" or "theLongDark")
                        len = Math.Max(len, Math.Min(max, 1400));
                    item = Make(def, s, len);
                    double share = rng.Range(def.Range("gradeShare"));
                    double grade = (up ? g : Math.Min(g, _l.DescentGrade)) * share;
                    item.Grades = [(0, 1, up ? grade : -grade)];
                    item.Cost = def.Cost + def.CostScale * share * Math.Min(1, len / 4000);
                    if (up && len >= 1200)
                        item.Tags.Add("climb_long");
                    item.Params["grade"] = Math.Round(up ? grade : -grade, 3);
                    break;
                }
            case "summit":
                {
                    item = Make(def, s, len);
                    double share = rng.Range(def.Range("gradeShare"));
                    double level = rng.Range(def.Range("levelM"));
                    double half = (len - level) / 2 / len;
                    item.Grades = [(0, half, g * share), (half, 1 - half, 0), (1 - half, 1, -Math.Min(g, _l.DescentGrade) * share)];
                    item.Cost = 2 * (1 + 2 * share * Math.Min(1, len / 8000));
                    if (len * half >= 1200)
                        item.Tags.Add("climb_long");
                    break;
                }
            case "roller":
                {
                    item = Make(def, s, len);
                    double wave = rng.Range(def.Range("wavelengthM"));
                    int halves = Math.Clamp((int)(len / (wave / 2)), 3, 12);
                    item.Grades = new();
                    double sign = preferDown ? -1 : 1;
                    for (int k = 0; k < halves; k++)
                    {
                        double gr = Math.Min(rng.Range(def.Range("grade")), g) * sign;
                        item.Grades.Add(((double)k / halves, (double)(k + 1) / halves, Math.Round(gr, 3)));
                        sign = -sign;
                    }
                    break;
                }
            case "drop":
                {
                    item = Make(def, s, len);
                    double lineSpeed = _l.LineSpeed;
                    double limit = rng.Range(def.Range("limitMs"));
                    // Mild below full difficulty: the curve still takes at least 85% of line speed (§7.2).
                    if (_p.D < def.FullD)
                        limit = Math.Max(limit, def.Param("mildShareOfLineSpeed") * lineSpeed);
                    limit = Math.Floor(Math.Min(limit, lineSpeed - 1));
                    double radius = Math.Max(_l.MinRadius, Math.Ceiling((limit + 0.5) * (limit + 0.5) / _t.Curves.APost));
                    item.Radius = radius;
                    item.Deflection = rng.Range(def.Range("curveDeg")) * Math.PI / 180;
                    double curve = Geometry.TurnLength(_t.Curves, item.Deflection, radius, limit);
                    if (curve > len - def.Param("descentMinM"))
                    {
                        item.Deflection = Geometry.MaxDeflection(_t.Curves, len - def.Param("descentMinM"), radius, limit);
                        curve = Geometry.TurnLength(_t.Curves, item.Deflection, radius, limit);
                    }
                    item.H = HShape.EndCurve;
                    double share = rng.Range(def.Range("gradeShare"));
                    double fd = Math.Clamp((len - curve) / len, 0.3, 0.95);
                    double grade = Math.Min(g, _l.DescentGrade) * share;
                    item.Grades = [(0, fd, -grade), (fd, 1, -grade * 0.3)];
                    item.Params["limitMs"] = limit;
                    item.Params["radius"] = radius;
                    break;
                }
            case "blindThroat":
                {
                    item = Make(def, s, len);
                    item.H = HShape.Reverse;
                    item.Radius = Math.Round(_l.MinRadius * rng.Range(def.Range("radiusFactor")));
                    item.Deflection = rng.Range(def.Range("deflectionDeg")) * Math.PI / 180;
                    double two = 2 * Geometry.TurnLength(_t.Curves, item.Deflection, item.Radius, _l.LineSpeed);
                    if (two + 40 > len)
                        item.Deflection = Math.Max(0.1, Geometry.MaxDeflection(_t.Curves, (len - 40) / 2, item.Radius, _l.LineSpeed));
                    item.Params["wallM"] = Math.Round(rng.Range(def.Range("wallM")), 1);
                    if (stackedOn is null)
                        item.Grades = null;
                    break;
                }
            case "bend":
                {
                    if (BendShape(def, max, ref rng) is not { } bend)
                        return null;
                    item = Make(def, s, bend.Length);
                    Bend(item, bend);
                    break;
                }
            case "ledge":
                {
                    item = Make(def, s, len);
                    item.H = HShape.Turn;
                    item.Radius = Math.Round(_l.MinRadius * rng.Range(def.Range("radiusFactor")));
                    item.Deflection = rng.Range(def.Range("deflectionDeg")) * Math.PI / 180;
                    item.Params["upM"] = Math.Round(rng.Range(def.Range("upM")));
                    item.Params["dropM"] = Math.Round(rng.Range(def.Range("dropM")));
                    item.DriftCap = g * 0.6;
                    break;
                }
            case "trestle":
                {
                    double span = Math.Min(rng.Range(def.Range("spanM")), len - 150);
                    if (span < def.Range("spanM")[0] * 0.8)
                        return null;
                    item = Make(def, s, len);
                    item.H = HShape.Straight;
                    item.Grades = [(0, 1, 0)];
                    item.Params["spanM"] = Math.Round(span);
                    item.Params["heightM"] = Math.Round(Math.Min(rng.Range(def.Range("heightM")), span * 0.4 + 12));
                    // Weak where the graph put a weak bridge on the main line, and in The Drop into High Iron from D 2.5
                    // (§7.4). On the main line a weak bridge's limit always takes the departing train (§6.2 step 6).
                    bool weak = purpose == "weakTrestle" || purpose == "dropIntoHighIron" && _p.D >= 2.5;
                    if (weak)
                    {
                        var limit = purpose == "weakTrestle" && _weakBridgesUsed < _mainWeakBridges.Count ? _mainWeakBridges[_weakBridgesUsed++]
                            : new WeakLimit(_p.Cars + rng.RangeInclusive(0, 3), Math.Round(rng.Range(_t.Hazards.WeakBridgeSpeed)));
                        item.Params["weakCars"] = Math.Max(limit.MaxCars, _p.Cars);
                        item.Params["weakSpeed"] = limit.SpeedMs;
                        item.Cost += def.CostScale;
                    }
                    break;
                }
            case "river":
                {
                    item = Make(def, s, len);
                    item.H = HShape.Straight;
                    item.Grades = [(0, 1, 0)];
                    item.Params["spanM"] = Math.Round(Math.Min(rng.Range(def.Range("spanM")), len * 0.4));
                    item.Params["depthM"] = Math.Round(rng.Range(def.Range("depthM")), 1);
                    break;
                }
            case "causeway":
                {
                    item = Make(def, s, len);
                    item.H = HShape.Gentle;
                    item.MinRadius = Math.Max(1500, _l.MinRadius * 3);
                    item.Grades = [(0, 1, 0)];
                    if (_p.D >= def.Param("contaminatedFromD") || purpose == "contaminated")
                        item.Tags.Add("contaminated");
                    break;
                }
            case "tunnel":
                {
                    double shortest = def.Range("boreM")[0];
                    // The approach cuttings shorten to fit the room before the tunnel gives up.
                    double approach = Math.Min(rng.Range(def.Range("approachM")), Math.Max(def.Range("approachM")[0] * 0.6, (max - shortest) / 2));
                    len = Math.Max(len, Math.Min(max, 2 * approach + shortest + 100));
                    double longest = Math.Min(_l.MaxTunnel, len - 2 * approach);
                    if (longest < shortest)
                        return null;
                    double bore = rng.Range(shortest, longest);
                    // The Long Dark: at least a kilometre, on a climb.
                    if (purpose == "theLongDark")
                        bore = Math.Max(bore, Math.Min(longest, 1000));
                    len = bore + 2 * approach;
                    item = Make(def, s, len);
                    item.Params["boreM"] = Math.Round(bore);
                    item.Params["approachM"] = Math.Round(approach);
                    bool curved = _l.CurvedTunnels && _p.D >= def.Param("curvedFromD") && rng.Chance(def.Param("curvedChance"));
                    item.H = curved ? HShape.Turn : HShape.Straight;
                    if (curved)
                    {
                        item.Radius = Math.Round(_l.MinRadius * rng.Range(2.5, 5));
                        item.Deflection = Math.Min(Geometry.MaxDeflection(_t.Curves, bore * 0.8, item.Radius, _l.LineSpeed), rng.Range(10, 35) * Math.PI / 180);
                        item.Params["curved"] = 1;
                    }
                    double tg = stackedOn?.Grades is { Count: > 0 } under ? Math.Clamp(under[0].G, -_l.TunnelGrade, _l.TunnelGrade)
                        : rng.Chance(def.Param("gradeChance")) ? _l.TunnelGrade * rng.Range(0.4, 1) * (preferDown ? -1 : 1) : 0;
                    item.Grades = [(0, 1, Math.Round(tg, 3))];
                    item.Cost = def.Cost * Math.Ceiling(bore / def.CostScale) + (Math.Abs(tg) > 0.2 ? 1 : 0) + (curved ? 1 : 0);
                    if (stackedOn is not null)
                    {
                        // Laid over a climb: the climb holds to the tunnel's grade cap there.
                        stackedOn.Grades = [(0, 1, Math.Min(stackedOn.Grades![0].G, _l.TunnelGrade))];
                        item.S0 = stackedOn.S0 + (stackedOn.Length - len) / 2;
                        item.S1 = item.S0 + len;
                        item.Grades = null;
                    }
                    break;
                }
            case "brass":
                {
                    double field = rng.Range(_t.Hazards.BrassLengthM);
                    len = Math.Min(max, _t.Hazards.BrassRampM + field + 100);
                    item = Make(def, s, len);
                    item.Params["fieldM"] = Math.Round(field);
                    item.Params["rampM"] = _t.Hazards.BrassRampM;
                    break;
                }
            case "momentum":
                {
                    double runUp = Math.Max(rng.Range(def.Range("runUpM")), _l.ConsistLength + _t.Validation.StallRollbackExtraM);
                    double gap = _l.MomentumGrade - g;
                    if (gap <= 0.2)
                        return null;
                    double bankGrade = Math.Round(g + gap * rng.Range(def.Range("gradeOverMain")), 2);
                    double carry = CarryDistance(bankGrade);
                    double bank = Math.Min(def.Param("carryShare") * carry, len - runUp - 200);
                    if (bank < 150)
                        return null;
                    len = runUp + bank + 200;
                    item = Make(def, s, len);
                    item.H = HShape.Straight;
                    item.Grades = [(0, runUp / len, 0), (runUp / len, (runUp + bank) / len, bankGrade), ((runUp + bank) / len, 1, 0)];
                    item.Params["runUpM"] = Math.Round(runUp);
                    item.Params["bankM"] = Math.Round(bank);
                    item.Params["grade"] = bankGrade;
                    item.Params["carryM"] = Math.Round(carry);
                    break;
                }
            case "settlement":
                return Settlement(s, room, ref rng);
            default:
                item = Make(def, s, len);
                break;
        }
        if (stackedOn is not null && def.Kind is not "tunnel")
        {
            // Stacked on another piece: centred on it, on its grade.
            double l = Math.Min(item.Length, stackedOn.Length - 60);
            item.S0 = stackedOn.S0 + (stackedOn.Length - l) / 2;
            item.S1 = item.S0 + l;
            if (def.Kind != "brass")
                item.Grades = null;
        }
        item.Signature = signature;
        if (item.H == HShape.Free && item.Wander == 0)
        {
            item.Wander = rng.Range(_t.Alignment.WanderDeg);
            item.MinRadius = _t.Alignment.WanderMinRadius;
        }
        return item;
    }

    int _weakBridgesUsed;
    int _bendsWanted;

    /// <summary>A hard bend's shape (note 278): its radius, its turn, and the length it takes with its straights.</summary>
    readonly record struct BendFit(double Radius, double Deflection, double Length, double Spur, double Fall);

    /// <summary>
    /// A bend that derails the train under its top speed, at a radius from the tier's derailing speeds (never under its
    /// minimum radius), with straight track either side so it's seen coming, in no more than <paramref name="room"/>.
    /// Where room is short, shorter straights first, then a smaller turn, and no bend at all under 60% of the least.
    /// </summary>
    BendFit? BendShape(PieceDef def, double room, ref Pcg32 rng)
    {
        var c = _t.Curves;
        double v = rng.Range(_l.BendDerail);
        double radius = Math.Max(Math.Ceiling(_l.MinRadius), Math.Ceiling(v * v / c.ADerail));
        var degrees = def.Range("deflectionDeg");
        double deflection = rng.Range(degrees) * Math.PI / 180;
        double tangent = rng.Range(def.Range("tangentM"));
        double spur = Math.Round(rng.Range(def.Range("spurM")), 1), fall = Math.Round(rng.Range(def.Range("fallM")), 1);
        double turn = Geometry.TurnLength(c, deflection, radius, _l.LineSpeed);
        if (turn + 2 * tangent > room)
        {
            tangent = Math.Max(def.Range("tangentM")[0] * 0.5, Math.Min(tangent, (room - turn) / 2));
            deflection = Math.Min(deflection, Geometry.MaxDeflection(c, room - 2 * tangent, radius, _l.LineSpeed));
            if (deflection < degrees[0] * 0.6 * Math.PI / 180)
                return null;
            turn = Geometry.TurnLength(c, deflection, radius, _l.LineSpeed);
        }
        return new BendFit(radius, deflection, turn + 2 * tangent, spur, fall);
    }

    /// <summary>Gives <paramref name="item"/> a hard bend's turn: its own piece, or a climb or descent laid round a hill.</summary>
    void Bend(Item item, BendFit bend)
    {
        item.H = HShape.Turn;
        item.Radius = bend.Radius;
        item.Deflection = bend.Deflection;
        item.Params["hardBend"] = 1;
        item.Params["radius"] = bend.Radius;
        item.Params["derailMs"] = Math.Round(Math.Sqrt(_t.Curves.ADerail * bend.Radius), 1);
        item.Params["spurM"] = bend.Spur;
        item.Params["fallM"] = bend.Fall;
        if (AwayFromBranches(item.S0, item.S1) is var turn and not 0)
            item.Params["turn"] = turn;
    }

    /// <summary>How far the loaded consist carries up a grade from line speed before it stalls, on the real train sim.</summary>
    double CarryDistance(double gradePercent)
    {
        var train = new Train.TrainDynamics(Train.Consist.Uniform(_c.Train, _p.Cars, 1)) { Velocity = _l.LineSpeed };
        var controls = new Train.TrainControls { Throttle = 1, Reverser = 1 };
        var track = new Train.TrackConditions { GradePercent = gradePercent, Traction = 1 };
        for (int i = 0; i < SimConstants.TickRate * 600 && train.Velocity > 0.5; i++)
            train.Step(SimConstants.TickSeconds, controls, track);
        return train.Distance;
    }

    /// <summary>The line's height so far at <paramref name="s"/>, from the grades scripted up to it (drift connectors at their mean).</summary>
    double EstimateHeight(double s)
    {
        double z = 0;
        foreach (var i in _estimate)
        {
            if (i.S0 >= s)
                break;
            double to = Math.Min(s, i.S1);
            if (i.Grades is null)
                continue;
            foreach (var (f0, f1, gr) in i.Grades)
            {
                double a = i.S0 + f0 * i.Length, b = Math.Min(to, i.S0 + f1 * i.Length);
                if (b > a)
                    z += (b - a) * gr / 100;
            }
        }
        return z;
    }

    readonly List<Item> _estimate = new();
}
