using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The procedural line generator (docs/design/linegen-plan.md): run parameters in, a validated Line Plan out.
/// Deterministic, engine-free, and fast enough to run while the crew is still in the fortress (§5: under 5 s).
/// <para>
/// Each attempt runs every stage on streams sub-seeded from the run seed and the attempt number. A stage that can't
/// lay its part (an alternate that won't close, a dead line with no room) retries its own edge first; what fails
/// validation retries the whole attempt. After ten, a curated fallback seed for the tier and consist band (§16.4).
/// </para>
/// </summary>
public static class LineGenerator
{
    public static LinePlan Generate(LineGenContent content, RunParameters p)
    {
        var t = content.Config.Tiers;
        var failures = new List<string>();
        for (int attempt = 0; attempt < t.Validation.Attempts; attempt++)
        {
            var (plan, why) = Attempt(content, p, attempt);
            if (plan is not null && plan.Validation.Passed)
                return plan with { Validation = plan.Validation with { Attempts = attempt + 1, Warnings = [.. plan.Validation.Warnings, .. failures.Take(5)] } };
            failures.Add($"attempt {attempt + 1}: {why ?? string.Join("; ", plan!.Validation.Checks.Where(c => !c.Pass).Select(c => $"{c.Name}: {c.Detail}"))}");
        }
        // §16.4: a seed proven to pass for this tier and consist band. Every use is a generator bug; it says so.
        ulong fallback = FallbackSeed(content, p);
        var fp = p with { Seed = fallback };
        for (int attempt = 0; attempt < t.Validation.Attempts; attempt++)
        {
            var (plan, _) = Attempt(content, fp, attempt);
            if (plan is not null && plan.Validation.Passed)
                return plan with
                {
                    Validation = plan.Validation with
                    {
                        Attempts = t.Validation.Attempts + attempt + 1,
                        Fallback = true,
                        Warnings = [.. plan.Validation.Warnings, $"fell back to curated seed {fallback} for {p.RouteId} at {p.Cars} cars", .. failures.Take(5)],
                    },
                };
        }
        // Nothing passed: the first attempt that built at all, its failed checks on it, so a caller can still play and
        // see why (and a sweep counts it). It never comes to nothing: a night is always generated.
        for (int attempt = 0; attempt < t.Validation.Attempts * 2; attempt++)
        {
            var (plan, _) = Attempt(content, p, attempt);
            if (plan is not null)
                return plan with { Validation = plan.Validation with { Attempts = 2 * t.Validation.Attempts, Warnings = [.. plan.Validation.Warnings, .. failures.Take(8)] } };
        }
        throw new InvalidOperationException($"line generation failed for {p.RouteId}: {string.Join("; ", failures.Take(3))}");
    }

    /// <summary>One attempt: every stage, then validation. Null plan with a reason when a stage couldn't lay its part.</summary>
    public static (LinePlan? Plan, string? Why) Attempt(LineGenContent content, RunParameters p, int attempt)
    {
        ulong seed = attempt == 0 ? p.RunSeed : Streams.Mix(p.RunSeed, "attempt", "", attempt);
        var b = new LineBuilder(content, p, seed);
        return b.Run();
    }

    /// <summary>`dt linegen debug`: one attempt's working, for looking at what the stages did.</summary>
    public static IEnumerable<string> Debug(LineGenContent content, RunParameters p, int attempt)
    {
        ulong seed = attempt == 0 ? p.RunSeed : Streams.Mix(p.RunSeed, "attempt", "", attempt);
        var b = new LineBuilder(content, p, seed);
        var (_, why) = b.Run();
        return [.. b.Debug(), $"result: {why ?? "ok"}", .. b.Warnings];
    }

    static ulong FallbackSeed(LineGenContent content, RunParameters p)
    {
        var bands = content.Config.FallbackSeeds.Seeds.GetValueOrDefault(LineBuilder.Key(p.Tier));
        if (bands is null)
            return p.Seed + 1;
        foreach (var (band, seeds) in bands)
        {
            var parts = band.Split('-');
            if (seeds.Length > 0 && p.Cars >= int.Parse(parts[0]) && p.Cars <= int.Parse(parts[^1]))
                return seeds[(int)(p.Seed % (ulong)seeds.Length)];
        }
        return p.Seed + 1;
    }
}
