using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game.Telemetry;

/// <summary>Whether this install has been asked, and what it said.</summary>
public enum PlaytestChoice { Unasked, Yes, No }

/// <summary>
/// The player's answer about playtest data (note 914): opt-in, asked once on first run (and again when the notice's
/// <see cref="PlaytestNotice.Version"/> rises), changeable in Settings. Kept in <c>consent.json</c> in the telemetry folder
/// beside the settings, with the sessions under it, so DELETE clears both. The id is random, made on saying yes, and a new
/// one after DELETE: nothing about the player or the machine is in it.
/// <para>
/// In a playtest build (note 946; the director, the inbox, 20 Oct: "On by default in playtest builds, with an off switch") it's
/// on from the start: the first run shows a notice (<see cref="OnByNotice"/>) with the off switch beside OK, not the question.
/// A yes that was the notice's counts only in a playtest: the same machine on an ordinary build is asked properly.
/// </para>
/// </summary>
public sealed class PlaytestConsent : ITelemetryGate
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "telemetry");

    /// <param name="ByNotice">The yes was a playtest build's notice put away, not an answer to the question (note 946).</param>
    sealed record Stored(PlaytestChoice Choice, int Notice, string? InstallId, bool ByNotice = false);

    readonly string _directory;
    Stored _stored;

    PlaytestConsent(string directory, Stored stored, bool defaultOn, bool onByNotice)
    {
        _directory = directory;
        _stored = stored;
        OnByNotice = onByNotice;
        DefaultOn = defaultOn || onByNotice;
    }

    /// <param name="defaultOn">A developer build (note 907): unasked counts as yes, and the question says so.</param>
    /// <param name="onByNotice">A playtest build (note 946): on unless the player turns it off, and a notice in place of the question.</param>
    public static PlaytestConsent Load(string directory, bool defaultOn = false, bool onByNotice = false)
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
        return new PlaytestConsent(directory, stored ?? new Stored(PlaytestChoice.Unasked, 0, null), defaultOn, onByNotice);
    }

    /// <summary>Unasked counts as yes: a developer build, or a playtest build.</summary>
    public bool DefaultOn { get; }
    /// <summary>A playtest build: the first run shows the notice, OK or TURN IT OFF, rather than the question (note 946).</summary>
    public bool OnByNotice { get; }
    public PlaytestChoice Choice => _stored.Choice;

    // A notice's yes, outside a playtest build, is no answer.
    bool Answered => _stored.Choice != PlaytestChoice.Unasked && !(_stored.ByNotice && !OnByNotice);

    /// <summary>The first-run question (or a playtest's notice) is due: never answered, or answered to an older notice.</summary>
    public bool NeedsAsking(PlaytestNotice notice) => !Answered || _stored.Notice < notice.Version;

    public bool Recording => Answered ? _stored.Choice == PlaytestChoice.Yes : DefaultOn;

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
    public void Choose(bool yes, PlaytestNotice notice) => Keep(yes, notice, byNotice: false);

    /// <summary>A playtest build's notice put away with OK: on, as it was, and not shown again in a playtest (note 946).</summary>
    public void Acknowledge(PlaytestNotice notice) => Keep(true, notice, byNotice: true);

    void Keep(bool yes, PlaytestNotice notice, bool byNotice)
    {
        bool was = Recording;
        Save(new Stored(yes ? PlaytestChoice.Yes : PlaytestChoice.No, notice.Version, yes ? _stored.InstallId ?? NewId() : _stored.InstallId, byNotice));
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
/// <param name="Fields">The schema's fields it covers (note 913's list, "type.field, field; ..."), for the test that holds the page to the schema.</param>
public sealed record PlaytestField(string What, string Detail, string Fields = "");

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
    /// <summary>Under the question in a playtest build (#628, note 916), "{code}" its cohort's: which test it is, and that no is fine.</summary>
    public string PlaytestBuild { get; init; } = "";
    /// <summary>A playtest build's first run (note 946): the notice's title and lines, "{code}" its cohort's.</summary>
    public string OnByDefaultTitle { get; init; } = "";
    public string[] OnByDefault { get; init; } = [];
    /// <summary>Who keeps it for the studio (note 946, with #637's PostHog), on WHAT WE COLLECT.</summary>
    public string ThirdParty { get; init; } = "";
    public PlaytestField[] Collect { get; init; } = [];
    public string Never { get; init; } = "";
    public string Where { get; init; } = "";

    /// <summary>Where it goes and for how long, with the months and the contact put in.</summary>
    public string WhereLine => Where.Replace("{months}", RetentionMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{contact}", Contact, StringComparison.Ordinal);

    public static PlaytestNotice Load(string content) => DataFile.Load<PlaytestNotice>(Path.Combine(content, File));
}
