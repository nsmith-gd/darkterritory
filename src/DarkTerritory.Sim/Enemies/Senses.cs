using Ballast;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What gives a sense off (the sense table's items, content/tuning/senses.json; note 728).</summary>
public enum EmitterKind : byte { HandLamp, CarLamp, Squeaker, MusicBox, Drummer, Spray, Knock, BurningLamp, Food, Body, Flare }

/// <summary>
/// Something in the world giving off (or, <see cref="Strength"/> negative, masking) a sense a creature answers (GDD App. A.1;
/// ARCHITECTURE §8 note 728): a lamp, a toy playing, the extinguisher's spray, a knock on the rail, a burning lamp, food, a
/// body. <see cref="By"/> is the crewmate it's in the name of (−1 nobody's), <see cref="Source"/> the body or car it comes
/// from (−1 none), <see cref="Car"/> the car it's in or on (−1 the world's). A spray reaches only along <see cref="Dir"/>,
/// within <see cref="Cone"/> (the cosine of its half-angle); everything else reaches all round.
/// </summary>
public readonly record struct Emitter(EmitterKind Kind, Sense Sense, Double3 At, double Radius, double Strength, int By, int Source, int Car,
    Double3 Dir = default, double Cone = -1)
{
    /// <summary>Whether it reaches <paramref name="point"/> (world): within its radius, and inside its cone if it has one.</summary>
    public bool Reaches(Double3 point)
    {
        var d = point - At;
        double length = d.Length;
        if (length > Radius)
            return false;
        if (Cone < -0.5 || length < 1e-6)
            return true;
        return Double3.Dot(d, Dir) >= Cone * length * Dir.Length;
    }

    public bool Masks => Strength < 0;
}

/// <summary>One sense an item gives off (senses.json <c>items</c>): how far it carries and how strong it is.</summary>
public sealed record SenseGiven(Sense Sense = Sense.Sound, double Radius = 0, double Strength = 0);

/// <summary>Mirror of content/tuning/senses.json (GDD App. A.1, App. C, §21; note 728). Field docs live in that file.</summary>
public sealed record SenseTuning
{
    public const string File = "tuning/senses.json";

    public Dictionary<string, SenseGiven[]> Items { get; init; } = new()
    {
        ["handLamp"] = [new(Sense.Light, 12, 1)],
        ["carLamp"] = [new(Sense.Light, 10, 1)],
        ["squeaker"] = [new(Sense.Sound, 15, 0.4)],
        ["musicBox"] = [new(Sense.Sound, 30, 0.6)],
        ["drummer"] = [new(Sense.Sound, 35, 0.8)],
        ["extinguisherSpray"] = [new(Sense.Heat, 3.5, -1)],
        ["knock"] = [new(Sense.Vibration, 40, 1)],
        ["burningLamp"] = [new(Sense.Light, 10, 0.8), new(Sense.Heat, 3, 0.6)],
        ["food"] = [new(Sense.Scent, 15, 0.5)],
        ["body"] = [new(Sense.Absence, 20, 1)],
    };
    public SenseToys Toys { get; init; } = new();
    public SenseSpray Spray { get; init; } = new();
    public SenseKnock Knock { get; init; } = new();
    public SenseThrown Thrown { get; init; } = new();
    public string[] FoodFinds { get; init; } = ["treats", "tinnedFood", "preserves"];
    public SenseAnswers Answers { get; init; } = new();

    /// <summary>What an item gives off, by its key in <c>items</c>; nothing for one the table doesn't list.</summary>
    public SenseGiven[] Of(string item) => Items.TryGetValue(item, out var given) ? given : [];

    /// <summary>What it gives off of <paramref name="sense"/>, or null.</summary>
    public SenseGiven? Of(string item, Sense sense) => Of(item).FirstOrDefault(g => g.Sense == sense);

    /// <summary>The table's key for a toy's noise ("" for a quiet one).</summary>
    public static string Key(Physics.ToyNoise noise) => noise switch
    {
        Physics.ToyNoise.Squeaker => "squeaker",
        Physics.ToyNoise.MusicBox => "musicBox",
        Physics.ToyNoise.Drummer => "drummer",
        _ => "",
    };

    static readonly SenseTuning Fallback = new();
    static readonly Dictionary<string, SenseTuning> Loaded = [];

    /// <summary>
    /// The table from <paramref name="content"/> (cached per content root); the repo's own when that's null and it can be found
    /// (tests, the CLI); the built-in mirror of it otherwise.
    /// </summary>
    public static SenseTuning Load(string? content = null)
    {
        try
        {
            content ??= DataFile.FindContentRoot();
        }
        catch (DirectoryNotFoundException)
        {
            return Fallback;
        }
        lock (Loaded)
        {
            if (Loaded.TryGetValue(content, out var t))
                return t;
            string path = Path.Combine(content, File);
            t = System.IO.File.Exists(path) ? DataFile.Load<SenseTuning>(path) : Fallback;
            Loaded[content] = t;
            return t;
        }
    }
}

public sealed record SenseToys
{
    public Dictionary<string, double> PlaysSeconds { get; init; } = new() { ["squeaker"] = 0, ["musicBox"] = 90, ["drummer"] = 30 };

    public double Plays(Physics.ToyNoise noise) => PlaysSeconds.GetValueOrDefault(SenseTuning.Key(noise));
}

public sealed record SenseSpray(double ConeDeg = 25);

public sealed record SenseKnock(double RailWithin = 1.1, double SpeedFrom = 3, double Seconds = 2);

public sealed record SenseThrown(double LampBreakSpeed = 6, double LampFireHeat = 0.35, double GroundBurnSeconds = 25);

public sealed record SenseAnswers
{
    public MooseSenses Moose { get; init; } = new();
    public ChoirSenses Choir { get; init; } = new();
    public GauntSenses Gaunt { get; init; } = new();
    public HoundSenses CinderHounds { get; init; } = new();
    public FireFlySenses FireFlies { get; init; } = new();
    public HuggerSenses CarHugger { get; init; } = new();
    public TowerJawSenses TowerJaw { get; init; } = new();
}

public sealed record MooseSenses(bool ToysLure = true, double StandOff = 3);

public sealed record ChoirSenses(bool LyingToysCount = true);

public sealed record GauntSenses(bool ToyHoldsSilence = true, bool ToyTalksDown = false);

public sealed record HoundSenses(bool SprayRepels = true, double RepelSeconds = 3, double DriveOffSeconds = 4, double DriveOffWindow = 10, double BackOffSpeed = 2.5);

public sealed record FireFlySenses(bool LampsAboardCount = true, double LureRadius = 30, double LureSpeed = 3, double FlareLureRadius = 80);

public sealed record HuggerSenses(bool KnocksDraw = true, double DrawnSeconds = 15, double CrawlSpeed = 2, double Lateral = 6);

public sealed record TowerJawSenses(bool KnocksTurn = true, double ListenSeconds = 4);
