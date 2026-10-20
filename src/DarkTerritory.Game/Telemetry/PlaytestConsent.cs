using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// What the session recorder (#624, note 913) asks before it records anything (#625, note 914; the plan, note 907):
/// whether this install said yes, and the anonymous id its sessions carry. Read it on every write, so turning playtest data
/// off stops the recording at once.
/// </summary>
public interface ITelemetryGate
{
    /// <summary>Record. False: record nothing, and drop what's held.</summary>
    bool Recording { get; }
    /// <summary>The random id this install's sessions carry; null while not recording.</summary>
    string? InstallId { get; }
    /// <summary>Where the spool's session files go (#626): cleared by <see cref="PlaytestConsent.Delete"/>.</summary>
    string SessionsDirectory { get; }
}

/// <summary>Whether this install has been asked, and what it said.</summary>
public enum PlaytestChoice { Unasked, Yes, No }

/// <summary>
/// The player's answer about playtest data (note 914): opt-in, asked once on first run (and again when the notice's
/// <see cref="PlaytestNotice.Version"/> rises), changeable in Settings. Kept in <c>consent.json</c> in the telemetry folder
/// beside the settings, with the sessions under it, so DELETE clears both. The id is random, made on saying yes, and a new
/// one after DELETE: nothing about the player or the machine is in it.
/// </summary>
public sealed class PlaytestConsent : ITelemetryGate
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "telemetry");

    sealed record Stored(PlaytestChoice Choice, int Notice, string? InstallId);

    readonly string _directory;
    Stored _stored;

    PlaytestConsent(string directory, Stored stored, bool defaultOn)
    {
        _directory = directory;
        _stored = stored;
        DefaultOn = defaultOn;
    }

    /// <param name="defaultOn">A developer build (note 907): unasked counts as yes, and the question says so.</param>
    public static PlaytestConsent Load(string directory, bool defaultOn = false)
    {
        string path = Path.Combine(directory, "consent.json");
        Stored? stored = null;
        try
        {
            if (File.Exists(path))
                stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), DataFile.Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A file that can't be read is no answer: asked again, and nothing recorded meanwhile.
        }
        return new PlaytestConsent(directory, stored ?? new Stored(PlaytestChoice.Unasked, 0, null), defaultOn);
    }

    public bool DefaultOn { get; }
    public PlaytestChoice Choice => _stored.Choice;

    /// <summary>The first-run question is due: never answered, or answered to an older notice.</summary>
    public bool NeedsAsking(PlaytestNotice notice) => _stored.Choice == PlaytestChoice.Unasked || _stored.Notice < notice.Version;

    public bool Recording => _stored.Choice == PlaytestChoice.Yes || _stored.Choice == PlaytestChoice.Unasked && DefaultOn;

    public string? InstallId
    {
        get
        {
            if (!Recording)
                return null;
            // A developer build recording before it's asked still needs an id: made, and kept with no answer recorded.
            if (_stored.InstallId is null)
                Save(_stored with { InstallId = NewId() });
            return _stored.InstallId;
        }
    }

    public string SessionsDirectory => Path.Combine(_directory, "sessions");

    /// <summary>Raised when recording turns on or off, for a recorder to start or stop.</summary>
    public event Action? Changed;

    /// <summary>The player's answer, to this version of the notice.</summary>
    public void Choose(bool yes, PlaytestNotice notice)
    {
        bool was = Recording;
        Save(new Stored(yes ? PlaytestChoice.Yes : PlaytestChoice.No, notice.Version, yes ? _stored.InstallId ?? NewId() : _stored.InstallId));
        if (was != Recording)
            Changed?.Invoke();
    }

    /// <summary>
    /// DELETE MY PLAYTEST DATA: every session on this computer, and a new id, so nothing sent before can be tied to what's sent
    /// after. <paramref name="forgetRemote"/> asks the studio's side to delete the old id's sessions (#626, once it uploads).
    /// The answer stays as it was. Returns how many session files went.
    /// </summary>
    public int Delete(Action<string>? forgetRemote = null)
    {
        int deleted = 0;
        if (Directory.Exists(SessionsDirectory))
            foreach (var file in Directory.GetFiles(SessionsDirectory))
            {
                File.Delete(file);
                deleted++;
            }
        if (_stored.InstallId is { } old)
            forgetRemote?.Invoke(old);
        Save(_stored with { InstallId = Recording ? NewId() : null });
        Changed?.Invoke();
        return deleted;
    }

    static string NewId() => Guid.NewGuid().ToString("N");

    void Save(Stored stored)
    {
        _stored = stored;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "consent.json"), JsonSerializer.Serialize(stored, DataFile.Options));
    }
}

/// <summary>A group of fields the recorder writes, as the "what we collect" page says it.</summary>
public sealed record PlaytestField(string What, string Detail);

/// <summary>The first-run question and the "what we collect" page, from content/ui/playtest-data.json (note 914).</summary>
public sealed record PlaytestNotice
{
    public const string File = "ui/playtest-data.json";

    public int Version { get; init; } = 1;
    public int RetentionMonths { get; init; } = 12;
    public string Contact { get; init; } = "";
    public string Question { get; init; } = "";
    public string[] Ask { get; init; } = [];
    public string DevBuild { get; init; } = "";
    public PlaytestField[] Collect { get; init; } = [];
    public string Never { get; init; } = "";
    public string Where { get; init; } = "";

    /// <summary>Where it goes and for how long, with the months and the contact put in.</summary>
    public string WhereLine => Where.Replace("{months}", RetentionMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{contact}", Contact, StringComparison.Ordinal);

    public static PlaytestNotice Load(string content) => DataFile.Load<PlaytestNotice>(Path.Combine(content, File));
}
