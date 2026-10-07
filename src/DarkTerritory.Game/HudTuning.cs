namespace DarkTerritory.Game;

/// <summary>
/// The flat-screen HUD's timings and thresholds (content/tuning/hud.json; GDD §32 "The HUD: your hands and the dark",
/// note 285): what comes and goes, when, and for how long. The defaults are the file's.
/// </summary>
public sealed record HudTuning
{
    public const string File = "tuning/hud.json";
    /// <summary>How long anything that comes and goes takes to fade in or out (s).</summary>
    public double FadeSeconds { get; init; } = 0.35;
    /// <summary>How long the tool's name stays over the hotbar after a change of hands (s).</summary>
    public double ToolNameSeconds { get; init; } = 1.6;
    /// <summary>How close a place is (m) when its name comes up at the top of the screen.</summary>
    public double PlaceAheadMetres { get; init; } = 1200;
    /// <summary>How long a place's name stays up (s).</summary>
    public double PlaceSeconds { get; init; } = 4.5;
    /// <summary>How long a deeper step of the cold is said (s).</summary>
    public double ColdSeconds { get; init; } = 5;
    /// <summary>How long before dawn (s) its clock comes up.</summary>
    public double DawnClockSeconds { get; init; } = 600;
    /// <summary>The ping (ms) at which it's shown out on the line (spec E: always in the lobby).</summary>
    public double PingWarnMs { get; init; } = 150;
    /// <summary>The crew's loudness, as a share of the Choir's threshold, at which the noise meter shows.</summary>
    public double NoiseShowAt { get; init; } = 0.5;
}
