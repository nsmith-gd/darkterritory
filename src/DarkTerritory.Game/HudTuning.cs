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
    /// <summary>The window (s) the link's loss is counted over (note 534).</summary>
    public double LossWindowSeconds { get; init; } = 10;
    /// <summary>Loss (0..1) under which the link's fine.</summary>
    public double LossGood { get; init; } = 0.02;
    /// <summary>Loss (0..1) at which the link's bad: danger ink, and shown out on the line.</summary>
    public double LossWarn { get; init; } = 0.08;
    /// <summary>The loss window in sim ticks.</summary>
    public int LossWindowTicks => Math.Max(1, (int)Math.Round(LossWindowSeconds * Sim.SimConstants.TickRate));
    /// <summary>The crew's loudness, as a share of the Choir's threshold, at which the noise meter shows.</summary>
    public double NoiseShowAt { get; init; } = 0.5;
    /// <summary>The HUD's colours that mean something (note 348), as the game was drawn.</summary>
    public HudPalette Standard { get; init; } = new([0.55, 0.82, 0.45], [1.00, 0.70, 0.30], [0.95, 0.26, 0.18]);
    /// <summary>COLOURS: COLOURBLIND (note 348): the same meanings told apart under protanopia, deuteranopia and tritanopia.</summary>
    public HudPalette Colourblind { get; init; } = new([0.40, 0.75, 1.00], [0.98, 0.85, 0.25], [0.80, 0.25, 0.15]);
    /// <summary>TEXT BACKING (note 404): how dark the band behind the HUD's print in play is, 0 to 1.</summary>
    public double TextBacking { get; init; } = 0.6;
}

/// <summary>
/// The colours on the HUD that mean something (note 348), sRGB 0..1: good (heard, a good ping, a rescue), a warning (a
/// heading, a ping going bad, a bend to slow for) and danger (dead, a grab, an alarm).
/// </summary>
public sealed record HudPalette(double[] Good, double[] Warn, double[] Danger)
{
    public System.Numerics.Vector4 GoodColour => Colour(Good);
    public System.Numerics.Vector4 WarnColour => Colour(Warn);
    public System.Numerics.Vector4 DangerColour => Colour(Danger);

    static System.Numerics.Vector4 Colour(double[] c) => new((float)c[0], (float)c[1], (float)c[2], 1);

    /// <summary>Palettes are the same when their colours are (a record compares arrays by reference).</summary>
    public bool Equals(HudPalette? other) => other is not null && Good.SequenceEqual(other.Good) && Warn.SequenceEqual(other.Warn)
        && Danger.SequenceEqual(other.Danger);

    public override int GetHashCode() => HashCode.Combine(GoodColour, WarnColour, DangerColour);
}
