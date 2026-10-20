using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// A playtest build's mark (note 916; <c>tools/package.sh --playtest</c> bakes it in as <c>content/ui/playtest.json</c>): the
/// channel it was built for and the cohort code its testers' sessions carry. A build without the file isn't a playtest's.
/// </summary>
public sealed record PlaytestBuild(string Channel = "playtest", string? Cohort = null)
{
    public const string File = "ui/playtest.json";
}

/// <summary>
/// The end-of-night survey's answers (note 916; the plan, note 907): fun, fear and would-play-again, each 1-5 or not said,
/// and a comment in the player's own words if they wrote one. Skipped: none of it, and that it was skipped.
/// </summary>
public sealed record SurveyAnswers(int? Fun = null, int? Fear = null, int? Again = null, string? Comment = null, bool Skipped = false)
{
    /// <summary>A comment's longest: a few sentences, not a letter.</summary>
    public const int CommentLength = 280;

    public bool Said => Fun is not null || Fear is not null || Again is not null || Comment is { Length: > 0 };
}

/// <summary>
/// The playtest kit (queue #628, note 916): what kind of build this is, the cohort code its sessions are grouped by (typed in
/// Settings, PLAYTEST DATA, over the one baked into a playtest build), and whether a night's end asks the survey. The typed
/// code is kept in <c>playtest.json</c> in the telemetry folder, beside #625's consent, so neither store is the other's.
/// </summary>
public sealed class PlaytestKit
{
    sealed record Stored(string? Code);

    readonly string? _directory;
    string? _typed;

    PlaytestKit(PlaytestBuild? build, string? directory, string? typed)
    {
        Build = build;
        _directory = directory;
        _typed = typed;
    }

    /// <summary>The build's mark (null: not a playtest build), and the code typed on this machine (in <paramref name="directory"/>).</summary>
    public static PlaytestKit Load(string content, string? directory)
    {
        PlaytestBuild? build = null;
        try
        {
            string baked = Path.Combine(content, PlaytestBuild.File);
            if (System.IO.File.Exists(baked))
                build = DataFile.Load<PlaytestBuild>(baked) is { } b ? b with { Cohort = Clean(b.Cohort) } : null;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException)
        {
            // A mark that can't be read is no mark: the build's an ordinary one.
        }
        string? typed = null;
        try
        {
            if (directory is not null && System.IO.File.Exists(Path.Combine(directory, "playtest.json")))
                typed = Clean(JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(Path.Combine(directory, "playtest.json")), DataFile.Options)?.Code);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return new PlaytestKit(build, directory, typed);
    }

    /// <summary>A kit with nothing baked or typed (tests, a session nobody's set up).</summary>
    public static PlaytestKit None => new(null, null, null);

    public PlaytestBuild? Build { get; }
    public bool PlaytestBuildOn => Build is not null;

    /// <summary>The code this machine's sessions carry: the one typed here, else the build's; null, none.</summary>
    public string? Cohort => _typed ?? Build?.Cohort;

    /// <summary>What's typed in Settings (what the PLAYTEST CODE row shows while it's edited).</summary>
    public string Typed => _typed ?? "";

    /// <summary>A code's longest, and what it may hold: letters, digits and dashes, upper case (said aloud at a playtest).</summary>
    public const int CodeLength = 16;

    /// <summary>A code as it's kept: upper case, only what a code may hold, no longer than <see cref="CodeLength"/>; blank, null.</summary>
    public static string? Clean(string? code)
    {
        if (code is null)
            return null;
        string kept = new([.. code.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-').Take(CodeLength)]);
        return kept.Length == 0 ? null : kept;
    }

    /// <summary>The code typed in Settings (blank: none, and the build's again), kept for the next session.</summary>
    public void Type(string code)
    {
        _typed = Clean(code);
        if (_directory is null)
            return;
        try
        {
            Directory.CreateDirectory(_directory);
            System.IO.File.WriteAllText(Path.Combine(_directory, "playtest.json"), JsonSerializer.Serialize(new Stored(_typed), DataFile.Options) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not kept past this session; this session still carries it.
        }
    }

    /// <summary>
    /// A night's end asks the survey: only while the player's said yes to playtest data, and only in a playtest (a playtest
    /// build, or a code typed): a player who said yes for the good of the game isn't asked questions after every night.
    /// </summary>
    public bool AsksSurvey(bool recording) => recording && (PlaytestBuildOn || Cohort is not null);
}
