using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Stage 0 (plan §5): what a line is generated for. The consist is the departing one, planned for fully loaded (§3.4);
/// crew size is for the director and harness only, never geometry.
/// </summary>
/// <param name="Severity">The route's place in its tier, [0, 1): route length and known grades on the route card.</param>
/// <param name="Cars">N_plan: the departing consist's car count.</param>
/// <param name="Contracts">Facility types the crew's contracts need (§11.1): each is guaranteed to appear.</param>
public sealed record RunParameters(RouteTier Tier, ulong Seed, double Severity, int Cars, IReadOnlyList<FacilityKind> Contracts, int Crew = 4)
{
    /// <summary>§3.1: D = tier index + severity, 0 to just under 4.</summary>
    public double D => (int)Tier + Severity;

    public string RouteId => $"{char.ToLowerInvariant(Tier.ToString()[0])}{Tier.ToString()[1..]}-{Seed}";

    /// <summary>
    /// Parses a route spec, "tier:seed" or "tier:seed:severity" (e.g. "frontier:7"). Without a severity, it's the seed's
    /// own (a route's place in its tier is part of the route, so the same spec is the same line everywhere).
    /// </summary>
    public static RunParameters Parse(string spec, int cars, int crew = 4)
    {
        var parts = spec.Split(':');
        var tier = Enum.Parse<RouteTier>(parts[0], ignoreCase: true);
        ulong seed = parts.Length > 1 ? ulong.Parse(parts[1]) : 1;
        double severity = parts.Length > 2 ? double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : SeverityOf(tier, seed);
        return new RunParameters(tier, seed, Math.Clamp(severity, 0, 0.999), Math.Max(1, cars), [], crew);
    }

    /// <summary>A seed's severity: fixed by the seed, spread evenly over the tier.</summary>
    public static double SeverityOf(RouteTier tier, ulong seed) => (Streams.Mix(seed, "severity", tier.ToString()) >> 11) * (1.0 / (1UL << 53));

    /// <summary>§5: the run seed, hash(campaignSeed, routeId, runIndex) — here the route's own seed and tier.</summary>
    public ulong RunSeed => Streams.Mix(Seed, "run", RouteId);
}

/// <summary>What the generator needs from content beyond its own files: the train it plans for, and the kit it lays.</summary>
public sealed record LineGenContent(LineGenConfig Config, TrainTuning Train, BoilerTuning Boiler, RouteTuning Route, EnemyTuning Enemies)
{
    public static LineGenContent Load(string content) => new(
        LineGenConfig.Load(content),
        DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File)),
        DataFile.Load<BoilerTuning>(Path.Combine(content, BoilerTuning.File)),
        RouteTuning.Load(content),
        DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)));

    static readonly Dictionary<string, (DateTime Stamp, LineGenContent Content)> Cache = new();

    /// <summary>Loaded once per content folder, and again when any of its files change (hot reload).</summary>
    public static LineGenContent Cached(string content)
    {
        var stamp = LineGenConfig.Files.Select(f => File.GetLastWriteTimeUtc(Path.Combine(content, LineGenConfig.Directory, f)))
            .Concat(new[] { TrainTuning.File, BoilerTuning.File, RouteTuning.File, EnemyTuning.File }.Select(f => File.GetLastWriteTimeUtc(Path.Combine(content, f)))).Max();
        lock (Cache)
        {
            if (Cache.TryGetValue(content, out var hit) && hit.Stamp == stamp)
                return hit.Content;
            var loaded = Load(content);
            Cache[content] = (stamp, loaded);
            return loaded;
        }
    }
}

/// <summary>
/// The tier table at one route's difficulty (§3.1–3.2, lerped by severity), coupled to the departing consist (§3.4):
/// the numbers every stage works to.
/// </summary>
public sealed class Limits
{
    public Limits(LineGenContent content, RunParameters p)
    {
        var cfg = content.Config.Tiers;
        int t = (int)p.Tier;
        var a = cfg.Columns[t];
        var b = cfg.Columns[t + 1];
        double f = p.Severity;
        double L(Func<TierColumn, double> get) => get(a) + (get(b) - get(a)) * f;
        double[] R(Func<TierColumn, double[]> get) => [get(a)[0] + (get(b)[0] - get(a)[0]) * f, get(a)[^1] + (get(b)[^1] - get(a)[^1]) * f];
        Column = a;
        D = p.D;
        Tier = p.Tier;
        LengthKm = L(c => c.LengthKm);
        Facilities = L(c => c.Facilities);
        MomentumGrade = a.MomentumGrade <= 0 ? 0 : L(c => c.MomentumGrade);
        BranchGrade = L(c => c.BranchGrade);
        MinRadius = L(c => c.MinRadius);
        MinVerticalRadius = L(c => c.MinVerticalRadius);
        MaxTunnel = L(c => c.MaxTunnel);
        CurvedTunnels = a.CurvedTunnels;
        StackCap = (int)Math.Round(a.StackCap);
        StackOnceDeeper = a.StackOnceDeeper;
        RecoveryMin = L(c => c.RecoveryMin);
        TellMargin = L(c => c.TellMargin);
        LineSpeed = Math.Floor(L(c => c.LineSpeed));
        Redundancy = (int)Math.Round(a.Redundancy);
        SignageSurvival = L(c => c.SignageSurvival);
        Junctions = R(c => c.Junctions);
        Alternates = R(c => c.Alternates);
        DeadLines = R(c => c.DeadLines);
        Washouts = R(c => c.Washouts);
        WeakBridges = R(c => c.WeakBridges);
        MomentumBanks = R(c => c.MomentumBanks);
        BrassFields = R(c => c.BrassFields);
        if (cfg.Demo.Enabled)
        {
            if (cfg.Demo.NoWashouts)
                Washouts = [0, 0];
            if (cfg.Demo.NoWeakBridges)
                WeakBridges = [0, 0];
            if (cfg.Demo.NoMomentumBanks)
                MomentumBanks = [0, 0];
        }
        BudgetPerKm = L(c => c.BudgetPerKm);
        Corruption = L(c => c.Corruption);
        Fog = R(c => c.Fog);
        RainChance = L(c => c.RainChance);
        Cold = R(c => c.Cold);
        Wind = R(c => c.Wind);
        SleeperDensity = L(c => c.SleeperDensity);

        // §3.4: the worst case is the departing consist with every car loaded.
        Cars = p.Cars;
        var consist = Consist.Uniform(content.Train, p.Cars, 1);
        var dynamics = new TrainDynamics(consist);
        ConsistLength = consist.LengthMetres;
        MassTonnes = consist.MassTonnes;
        ClimbMax = dynamics.MaxClimbableGradePercent();
        Brake = dynamics.MaxBrakeForce / consist.MassTonnes;
        Accel = dynamics.MaxTractiveForce / consist.MassTonnes;
        TierMainGrade = L(c => c.MainGrade);
        MainGrade = Math.Min(TierMainGrade, cfg.Consist.MainGradeShareOfClimbMax * ClimbMax);
        TunnelGrade = a.TunnelGrade <= 0 ? MainGrade : Math.Min(MainGrade, b.TunnelGrade <= 0 ? a.TunnelGrade : L(c => c.TunnelGrade));
        // The steepest sustained descent a loaded train holds its speed on without its brakes fading away (§16.3: never
        // below 60%). Spec B.5's brakes fade while applied and recover while released, so braking in pulses holds
        // steady at a duty of recover / (fade + recover): the grade that duty of full braking holds, on wet rail.
        double wet = content.Config.Tiers.Weather.WetBiasAdhesion;
        var fade = content.Train.BrakeFade;
        double duty = fade.RecoverPerSecond / (fade.FadePerSecond + fade.RecoverPerSecond);
        DescentGrade = Math.Min(MainGrade, Math.Tan(Math.Asin(Math.Min(1, Brake * duty * wet / content.Train.Gravity))) * 100);
        var longest = Consist.Uniform(content.Train, cfg.Consist.MaxCars, 1);
        LongestConsist = longest.LengthMetres;
        TenderEnduranceS = BoilerScenarios.TenderEnduranceMinutes(content.Boiler, p.Cars) * 60;
    }

    public TierColumn Column { get; }
    public double D { get; }
    public RouteTier Tier { get; }
    public double LengthKm { get; }
    public double Facilities { get; }
    public double TierMainGrade { get; }
    /// <summary>g_main (§3.4): the tier's cap, and no more than 85% of what the loaded consist can climb.</summary>
    public double MainGrade { get; }
    public double DescentGrade { get; }
    public double MomentumGrade { get; }
    public double BranchGrade { get; }
    public double MinRadius { get; }
    public double MinVerticalRadius { get; }
    public double MaxTunnel { get; }
    public double TunnelGrade { get; }
    public bool CurvedTunnels { get; }
    public int StackCap { get; }
    public bool StackOnceDeeper { get; }
    public double RecoveryMin { get; }
    public double TellMargin { get; }
    public double LineSpeed { get; }
    public int Redundancy { get; }
    public double SignageSurvival { get; }
    public double[] Junctions { get; }
    public double[] Alternates { get; }
    public double[] DeadLines { get; }
    public double[] Washouts { get; }
    public double[] WeakBridges { get; }
    public double[] MomentumBanks { get; }
    public double[] BrassFields { get; }
    public double BudgetPerKm { get; }
    public double Corruption { get; }
    public double[] Fog { get; }
    public double RainChance { get; }
    public double[] Cold { get; }
    public double[] Wind { get; }
    public double SleeperDensity { get; }

    public int Cars { get; }
    public double ConsistLength { get; }
    public double LongestConsist { get; }
    public double MassTonnes { get; }
    /// <summary>climbMax(N_plan), percent, from the train sim (spec B.5).</summary>
    public double ClimbMax { get; }
    /// <summary>Full-service braking and full-throttle acceleration of the loaded consist, m/s², from the train sim.</summary>
    public double Brake { get; }
    public double Accel { get; }
    public double TenderEnduranceS { get; }
}
