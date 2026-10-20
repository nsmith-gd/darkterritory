namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// What the session recorder (#624, note 913) asks before it records anything (#625's consent, note 914; the plan, note 907):
/// whether this install said yes, and the anonymous id its sessions carry. The recorder reads it on every write, so turning
/// playtest data off stops the recording at once. #625's <c>PlaytestConsent</c> is the game's; <see cref="FixedTelemetryGate"/>
/// is a fixed answer (tests, <c>dt telemetry</c>, and the app's <c>--telemetry</c> until #625 lands).
/// </summary>
public interface ITelemetryGate
{
    /// <summary>Record. False: record nothing, and drop what's held.</summary>
    bool Recording { get; }
    /// <summary>The random id this install's sessions carry; null while not recording.</summary>
    string? InstallId { get; }
    /// <summary>Where the spool's session files go (#626).</summary>
    string SessionsDirectory { get; }
}

/// <summary>A gate whose answer is fixed when it's made (or set by hand, for a test switching it off mid-night).</summary>
public sealed class FixedTelemetryGate(bool recording, string sessionsDirectory, string? installId = null) : ITelemetryGate
{
    public bool Recording { get; set; } = recording;
    public string? InstallId => Recording ? installId : null;
    public string SessionsDirectory { get; } = sessionsDirectory;

    /// <summary>The sessions' folder in the player's app data, as #625's consent keeps it.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "telemetry", "sessions");
}
