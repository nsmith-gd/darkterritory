namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The big forests (note 1006; the director, 23 Oct 2026: "We should really try and have, like, big forests in this game
/// ... stopping in the wild feels like it should feel scary"): long stretches of the main line where the woods close in,
/// dense, tall and right up to the ballast, dark under the canopy. Where a biome grows them (biomes.json "deepWoods": the
/// share of its line, how long each runs, how close and thick), and wherever the plan laid a Dark Forest (§7.2), dealt
/// from the night's seed and the plan alone, so the sim's trees (LinesideProps), the art, the woods' sound and anything
/// that comes out of them (#709's heavies: <see cref="Sites"/>) all find the same woods on every machine.
/// </summary>
public sealed class DeepWoods
{
    /// <summary>One stretch of deep woods along the main line, and the biome rule it grows by.</summary>
    public sealed record Stretch(double S0, double S1, DeepWoodsRule Rule);

    /// <summary>Where something could come out of the trees at the line: <see cref="Along"/> the main line, <see cref="Lateral"/> at the woods' inner edge (right positive).</summary>
    public readonly record struct Site(double Along, double Lateral, double Depth);

    /// <summary>The stretches dealt along the line, in order and apart.</summary>
    public IReadOnlyList<Stretch> Stretches { get; }

    readonly LinePlan _plan;

    DeepWoods(LinePlan plan)
    {
        _plan = plan;
        ulong seed = ulong.TryParse(plan.Seed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? plan.Seed[2..] : plan.Seed,
            System.Globalization.NumberStyles.HexNumber, null, out var s) ? s : Streams.Mix(0, plan.Seed);
        var laid = new List<(double S0, double S1)>();
        // Down the line in runs as long as the woods' own: each run is woods by the share its middle's biome grows, or a gap.
        // A stream per run, so the rolls of one never move another's.
        double at = 0;
        for (long i = 0; at < plan.LengthM && i < 10_000; i++)
        {
            var rng = Streams.Rng(seed, "deepwoods", "main", i);
            var rule = RuleAt(at + 400);
            double len = rule is null ? 400 : rng.Range(rule.LengthM);
            if (rule is not null && rng.Chance(rule.Share))
                laid.Add((at, at + len));
            at += len;
        }
        // The plan's own Dark Forests (§7.2) are deep woods wherever the country grows any.
        foreach (var piece in plan.Pieces)
            if (piece.Type == "darkForest" && piece.Edge == "main" && RuleAt((piece.S0 + piece.S1) / 2) is not null)
                laid.Add((piece.S0, piece.S1));
        // Trimmed to the country that grows them (no deep woods on the dykelands past a forest's end), then joined.
        var stretches = new List<Stretch>();
        foreach (var (a, b) in laid.OrderBy(x => x.S0))
        {
            double s0 = Math.Max(a, 0), s1 = Math.Min(b, plan.LengthM);
            while (s0 < s1 && RuleAt(s0) is null)
                s0 += 25;
            while (s1 > s0 && RuleAt(s1) is null)
                s1 -= 25;
            if (s1 - s0 < 150)
                continue;
            if (stretches.Count > 0 && s0 <= stretches[^1].S1 + 100)
                stretches[^1] = stretches[^1] with { S1 = Math.Max(stretches[^1].S1, s1) };
            else
                stretches.Add(new Stretch(s0, s1, RuleAt((s0 + s1) / 2) ?? RuleAt(s0) ?? RuleAt(s1)!));
        }
        Stretches = stretches;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LinePlan, DeepWoods> Built = new();

    /// <summary>The night's deep woods: a pure function of its plan, dealt once.</summary>
    public static DeepWoods Of(LinePlan plan)
    {
        lock (Built)
            return Built.GetValue(plan, p => new DeepWoods(p));
    }

    /// <summary>The deep woods' rule of the main line's biome at <paramref name="s"/>, or null where it grows none.</summary>
    DeepWoodsRule? RuleAt(double s)
    {
        string name = _plan.Biomes.Count > 0 ? _plan.Biomes[^1].Biome : "farmland";
        foreach (var b in _plan.Biomes)
            if (b.Edge == "main" && s >= b.S0 && s < b.S1)
            {
                name = b.Biome;
                break;
            }
        return _plan.Rules.Biomes.GetValueOrDefault(name)?.DeepWoods is { Share: > 0 } rule ? rule : null;
    }

    /// <summary>The stretch of deep woods at <paramref name="s"/> on the main line, or null.</summary>
    public Stretch? At(double s)
    {
        foreach (var w in Stretches)
            if (s >= w.S0 - w.Rule.EdgeM && s <= w.S1 + w.Rule.EdgeM)
                return w;
        return null;
    }

    /// <summary>
    /// How deep in the woods <paramref name="s"/> is on the main line: 1 inside a stretch, 0 out of it, eased in over the
    /// rule's edgeM either side of its ends (the trees thicken as the line goes in). Plain arithmetic: the sim's trees stand by it.
    /// </summary>
    public double Depth(double s)
    {
        if (At(s) is not { } w)
            return 0;
        double e = Math.Max(w.Rule.EdgeM, 1);
        double t = Math.Clamp(Math.Min(s - (w.S0 - e), (w.S1 + e) - s) / (2 * e), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The share of the main line between <paramref name="from"/> and <paramref name="to"/> in deep woods.</summary>
    public double Share(double from, double to)
    {
        if (to <= from)
            return 0;
        double inside = Stretches.Sum(w => Math.Max(0, Math.Min(to, w.S1) - Math.Max(from, w.S0)));
        return inside / (to - from);
    }

    /// <summary>
    /// The hook for #709's forest heavies (note 1006): where something big could step out of the trees onto the line,
    /// every <paramref name="everyM"/> along each stretch, both sides, at the woods' inner edge (its rule's nearM, a stride in
    /// from the first trunks). Nothing comes out of them yet; a spawn asks its ground of the stop's clearing and the
    /// LinesideProps walls itself (a stop's yard is cleared of the woods).
    /// </summary>
    public IEnumerable<Site> Sites(double everyM = 40)
    {
        foreach (var w in Stretches)
            for (double s = w.S0 + everyM / 2; s < w.S1; s += everyM)
                foreach (int side in new[] { -1, 1 })
                    yield return new Site(s, side * (w.Rule.NearM + 1.5), Depth(s));
    }
}
