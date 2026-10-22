using System.Text.Json;
using Ballast;
using DarkTerritory.Game.Telemetry;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The playtest kit (queue #628, note 916): the end-of-night survey, asked only in a playtest and only with playtest data on;
/// its answers (or that it was skipped) in the session; the cohort code typed or baked into a playtest build, on the session
/// line; and the build's mark as <c>tools/package.sh --playtest</c> bakes it.
/// </summary>
public sealed class PlaytestKitTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-kit-{Guid.NewGuid():N}");
    static readonly string Content = DataFile.FindContentRoot();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    string Telemetry => Path.Combine(_dir, "telemetry");

    /// <summary>A playtest build's content folder: only its mark (the kit reads nothing else of it).</summary>
    string Baked(string? cohort)
    {
        string content = Path.Combine(_dir, "build");
        Directory.CreateDirectory(Path.Combine(content, "ui"));
        File.WriteAllText(Path.Combine(content, PlaytestBuild.File), JsonSerializer.Serialize(new PlaytestBuild(Cohort: cohort), DataFile.Options));
        return content;
    }

    FrontEnd Menu(PlaytestKit kit) => new(DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File)),
        DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), new SaveSlots(Path.Combine(_dir, "saves"), 3), Path.Combine(_dir, "settings.json"), () => 42)
    {
        Kit = kit,
    };

    [Fact]
    public void ACodeIsKeptAsSaidAloudAndATypedOneOutranksTheBuilds()
    {
        Assert.Equal("FALL-26-A", PlaytestKit.Clean(" fall-26-a! "));
        Assert.Null(PlaytestKit.Clean("  ?? "));
        Assert.Equal(PlaytestKit.CodeLength, PlaytestKit.Clean(new string('X', 40))!.Length);

        // An ordinary build: no mark, no code, nothing asked.
        var plain = PlaytestKit.Load(Content, Telemetry);
        Assert.False(plain.PlaytestBuildOn);
        Assert.Null(plain.Cohort);
        Assert.False(plain.AsksSurvey(recording: true));

        // A playtest build carries its cohort; a code typed here outranks it and is kept for the next session; blank, the build's.
        var kit = PlaytestKit.Load(Baked("wave-1"), Telemetry);
        Assert.True(kit.PlaytestBuildOn);
        Assert.Equal("WAVE-1", kit.Cohort);
        kit.Type("table 4");
        Assert.Equal("TABLE4", kit.Cohort);
        Assert.Equal("TABLE4", PlaytestKit.Load(Baked("wave-1"), Telemetry).Cohort);
        kit.Type("");
        Assert.Equal("WAVE-1", PlaytestKit.Load(Baked("wave-1"), Telemetry).Cohort);
    }

    [Fact]
    public void TheSurveyIsAskedOnlyInAPlaytestWithPlaytestDataOn()
    {
        // A playtest build: asked while recording, never without consent.
        var build = PlaytestKit.Load(Baked(null), Telemetry);
        Assert.True(build.AsksSurvey(recording: true));
        Assert.False(build.AsksSurvey(recording: false));
        // An ordinary build with a code typed is a playtest too.
        var typed = PlaytestKit.Load(Content, Telemetry);
        typed.Type("LAN-PARTY");
        Assert.True(typed.AsksSurvey(recording: true));

        // The front end: not asked, the menu stays where the night left it.
        var menu = Menu(PlaytestKit.Load(Content, Path.Combine(_dir, "elsewhere")));
        menu.NightOver();
        menu.AskSurvey(recording: true);
        Assert.Equal(Screen.Title, menu.Screen);
        menu = Menu(build);
        menu.NightOver();
        menu.AskSurvey(recording: false);
        Assert.Equal(Screen.Title, menu.Screen);
    }

    [Fact]
    public void AnsweredTheAnswersGoToTheSessionAndTheMenuGoesWhereTheNightLeftIt()
    {
        SurveyAnswers? got = null;
        var menu = Menu(PlaytestKit.Load(Baked("WAVE-1"), Telemetry));
        menu.Surveyed = a => got = a;
        menu.ShowFortress(1);
        var then = menu.Screen;
        menu.AskSurvey(recording: true);
        Assert.Equal(Screen.Survey, menu.Screen);
        // Nothing said, SEND isn't offered: SKIP is.
        Assert.False(menu.Items.Single(i => i.Label == "SEND").Enabled);
        // Fun 4 (Right from unsaid is 1, then up), fear 1 (Left from unsaid is 5, then down to its floor), play again unsaid.
        for (int i = 0; i < 4; i++)
            menu.Right();
        menu.Down();
        for (int i = 0; i < 6; i++)
            menu.Left();
        Assert.Contains(menu.Items, i => i.Label == "HOW FUN: 4 OF 5");
        Assert.Contains(menu.Items, i => i.Label == "HOW FRIGHTENING: 1 OF 5");
        Assert.Contains(menu.Items, i => i.Label == "ANOTHER NIGHT: -");
        // A word, typed: only what prints, capped.
        menu.Down();
        menu.Down();
        menu.Select();
        menu.Type("Great bridge.\u0007" + new string('!', 400));
        menu.Select();
        // The words go only with their own yes (note 916): off until it's ticked.
        menu.Down();
        Assert.Equal("SEND MY WORDS TOO: NO", menu.Items[menu.Selected].Label);
        menu.Select();
        Assert.Equal("SEND MY WORDS TOO: YES", menu.Items[menu.Selected].Label);
        menu.Down();
        Assert.Equal("SEND", menu.Items[menu.Selected].Label);
        menu.Select();
        Assert.Equal(new SurveyAnswers(4, 1, null, ("Great bridge." + new string('!', 400))[..SurveyAnswers.CommentLength], SendComment: true), got);
        Assert.Equal(then, menu.Screen);
        Assert.StartsWith("Thank you.", menu.Message);
    }

    [Fact]
    public void EscapeSkipsIt()
    {
        SurveyAnswers? got = null;
        var menu = Menu(PlaytestKit.Load(Baked(null), Telemetry));
        menu.Surveyed = a => got = a;
        menu.NightOver("Night's done.");
        menu.AskSurvey(recording: true);
        menu.Right();
        menu.Back();
        Assert.Equal(new SurveyAnswers(Skipped: true), got);
        Assert.Equal(Screen.Title, menu.Screen);
        Assert.Equal("Night's done.", menu.Message);
    }

    [Fact]
    public void TheSessionCarriesTheCohortAndTheSurveyIsItsOwnLine()
    {
        var sink = new MemoryTelemetrySink();
        var recorder = new SessionRecorder(sink, new FixedTelemetryGate(true, _dir, "install"), () => 1, "s1");
        recorder.Begin(new SessionFacts("full", false, "os", "gpu", false, "1280x720", 1, true, 75, Cohort: "WAVE-1"));
        // No night yet: nothing to say a survey about.
        recorder.Survey(new SurveyAnswers(5, 5, 5));
        Assert.Empty(sink.Lines);
        var night = new PrototypeSession(Content, "test-loop", 3);
        recorder.NightBegin(night.World, "solo", 1, 0, 0);
        recorder.NightEnd(night.World);
        recorder.Survey(new SurveyAnswers(4, 2, 7, "  Good.  ", SendComment: true));
        // Words written but not ticked to go: not kept.
        recorder.Survey(new SurveyAnswers(4, 2, 3, "left here"));
        recorder.Survey(new SurveyAnswers(Fun: 3, Comment: "never said", Skipped: true));
        var lines = sink.Lines.Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal("WAVE-1", lines.Single(l => l.GetProperty("type").GetString() == "session").GetProperty("cohort").GetString());
        var surveys = lines.Where(l => l.GetProperty("type").GetString() == "survey").ToList();
        Assert.Equal(3, surveys.Count);
        Assert.Equal(JsonValueKind.Null, surveys[1].GetProperty("comment").ValueKind);
        surveys.RemoveAt(1);
        Assert.Equal([.. TelemetrySchema.Common, .. TelemetrySchema.Events["survey"]], surveys[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, surveys[0].GetProperty("night").GetInt32());
        Assert.Equal(4, surveys[0].GetProperty("fun").GetInt32());
        Assert.Equal(2, surveys[0].GetProperty("fear").GetInt32());
        // Out of 1-5 is not said; the comment trimmed.
        Assert.Equal(JsonValueKind.Null, surveys[0].GetProperty("again").ValueKind);
        Assert.Equal("Good.", surveys[0].GetProperty("comment").GetString());
        Assert.False(surveys[0].GetProperty("skipped").GetBoolean());
        // Skipped: none of it is kept.
        Assert.True(surveys[1].GetProperty("skipped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, surveys[1].GetProperty("fun").ValueKind);
        Assert.Equal(JsonValueKind.Null, surveys[1].GetProperty("comment").ValueKind);
    }

    [Fact]
    public void WhatWeCollectSaysTheSurveyAndTheCode()
    {
        // #625's page covers the survey's fields and the cohort (its wording is the director's to change, so the fields are held).
        var notice = PlaytestNotice.Load(Content);
        var fields = string.Join("; ", notice.Collect.Select(f => f.Fields));
        foreach (string field in TelemetrySchema.Events["survey"])
            Assert.Contains(field, fields);
        Assert.Contains("session.cohort", fields);
        // And the question says a playtest build when it is one.
        Assert.NotEmpty(notice.PlaytestBuild);
    }
}
