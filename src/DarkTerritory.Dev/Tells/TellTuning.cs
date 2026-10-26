namespace DarkTerritory.Dev.Tells;

/// <summary>
/// The visual tell audit's numbers (content/tuning/tells.json; systems-spec A.4a, ARCHITECTURE §8 note 1037). A developer
/// build's only: nothing on a real night reads them.
/// </summary>
public sealed record TellTuning
{
    public const string File = "tuning/tells.json";

    public int Width { get; init; } = 640;
    public int Height { get; init; } = 360;
    public double Fov { get; init; } = 65;
    public double Settle { get; init; } = 1;
    /// <summary>The lightness step (L*) a pixel must differ by to show the creature.</summary>
    public double Jnd { get; init; } = 3;
    /// <summary>The floor a tell must clear: pixels showing it (at 720p), and the brightest tenth's step (L*).</summary>
    public double MinPixels { get; init; } = 60;
    public double MinContrast { get; init; } = 6;
    /// <summary>A crewmate's eye height over the ground, by where they stand.</summary>
    public Dictionary<string, double> Viewpoints { get; init; } = [];
    public Dictionary<string, Condition> Conditions { get; init; } = [];
    public Dictionary<string, Entry> Tells { get; init; } = [];
    /// <summary>The test's few.</summary>
    public string[] Fast { get; init; } = [];

    /// <param name="FogDensity">The night's fog over the route's (0: the route's own).</param>
    public sealed record Condition(double FogDensity = 0, bool LampsOut = false, bool Wet = false);

    /// <param name="WarnM">How far off it has to be seen to be answered (m).</param>
    /// <param name="Aim">Where on it to look (m over where it stands).</param>
    /// <param name="Hidden">Why it's hidden on purpose until it acts: measured and shown, never a failure.</param>
    /// <param name="Settle">Its own seconds after the call before the shot (a fire taking hold), over the audit's.</param>
    public sealed record Entry(double WarnM = 30, string[]? From = null, double Aim = 1, string What = "", string? Hidden = null, double? Settle = null)
    {
        public string[] From { get; init; } = From ?? ["foot"];
    }

    public static TellTuning Load(string content) => Ballast.DataFile.Load<TellTuning>(Path.Combine(content, File));
}
