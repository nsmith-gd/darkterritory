using Ballast;
using DarkTerritory.Game.Telemetry;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Playtest data's consent and privacy (#625, note 914): nothing recorded before a yes, an anonymous id, the question asked
/// once (and again when the notice changes), the switch and the "what we collect" page in Settings, and DELETE.
/// </summary>
public sealed class PlaytestConsentTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-playtest-{Guid.NewGuid():N}");
    static readonly PlaytestNotice Notice = PlaytestNotice.Load(DataFile.FindContentRoot());

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    PlaytestConsent Consent(bool dev = false) => PlaytestConsent.Load(Path.Combine(_dir, "telemetry"), defaultOn: dev);

    FrontEnd Menu(PlaytestConsent consent)
    {
        var content = DataFile.FindContentRoot();
        return new(DataFile.Load<CampaignTuning>(Path.Combine(content, CampaignTuning.File)), DataFile.Load<RunTuning>(Path.Combine(content, RunTuning.File)),
            new SaveSlots(Path.Combine(_dir, "saves"), 3), Path.Combine(_dir, "settings.json"), () => 42)
        {
            Playtest = consent,
            PlaytestNotice = Notice,
        };
    }

    [Fact]
    public void NothingIsRecordedBeforeAYesAndTheIdIsRandom()
    {
        var c = Consent();
        Assert.True(c.NeedsAsking(Notice));
        Assert.False(c.Recording);
        Assert.Null(c.InstallId);
        c.Choose(yes: true, Notice);
        Assert.True(c.Recording);
        Assert.Matches("^[0-9a-f]{32}$", c.InstallId!);
        // Kept: the next launch neither asks nor makes a new id.
        var again = Consent();
        Assert.False(again.NeedsAsking(Notice));
        Assert.Equal(c.InstallId, again.InstallId);
        // Another install is someone else.
        var other = PlaytestConsent.Load(Path.Combine(_dir, "other"));
        other.Choose(yes: true, Notice);
        Assert.NotEqual(c.InstallId, other.InstallId);
    }

    [Fact]
    public void NoMeansNothingAndTurningItOffStopsIt()
    {
        var c = Consent();
        int changes = 0;
        c.Changed += () => changes++;
        c.Choose(yes: false, Notice);
        Assert.False(c.Recording);
        Assert.Null(c.InstallId);
        Assert.False(c.NeedsAsking(Notice));
        c.Choose(yes: true, Notice);
        c.Choose(yes: false, Notice);
        Assert.Equal(2, changes);
        Assert.Null(c.InstallId);
    }

    [Fact]
    public void ANewNoticeIsAskedAgain()
    {
        var c = Consent();
        c.Choose(yes: true, Notice);
        Assert.True(c.NeedsAsking(Notice with { Version = Notice.Version + 1 }));
    }

    [Fact]
    public void DeleteClearsTheSessionsAndStartsANewId()
    {
        var c = Consent();
        c.Choose(yes: true, Notice);
        string old = c.InstallId!;
        Directory.CreateDirectory(c.SessionsDirectory);
        File.WriteAllText(Path.Combine(c.SessionsDirectory, "a.jsonl"), "{}");
        File.WriteAllText(Path.Combine(c.SessionsDirectory, "b.jsonl"), "{}");
        string? forgotten = null;
        Assert.Equal(2, c.Delete(id => forgotten = id));
        Assert.Empty(Directory.GetFiles(c.SessionsDirectory));
        Assert.Equal(old, forgotten);
        Assert.NotEqual(old, c.InstallId);
        Assert.True(c.Recording);
    }

    [Fact]
    public void ADeveloperBuildIsOnUnlessTheDirectorSaysNoAndTheQuestionSaysSo()
    {
        var c = Consent(dev: true);
        Assert.True(c.Recording);
        Assert.NotNull(c.InstallId);
        var m = Menu(c);
        m.Show(Screen.Consent);
        Assert.Equal("YES, RECORD MY NIGHTS", m.Items[0].Label);
        Assert.Contains(Notice.DevBuild, m.Items[0].Detail);
        m.Down();
        m.Select();
        Assert.False(c.Recording);
    }

    [Fact]
    public void TheFirstRunQuestionPutsNoAnswerFirstAndEscapeAnswersNothing()
    {
        var c = Consent();
        var m = Menu(c);
        m.Show(Screen.Consent);
        Assert.Equal(Screen.Consent, m.Screen);
        Assert.Equal(["NO THANKS", "YES, RECORD MY NIGHTS", "WHAT WE COLLECT"], m.Items.Select(i => i.Label));
        Assert.DoesNotContain(Notice.DevBuild, m.Items[1].Detail);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Equal(PlaytestChoice.Unasked, c.Choice);
        Assert.False(c.Recording);

        m.Show(Screen.Consent);
        m.Down();
        m.Select();
        Assert.Equal(Screen.Title, m.Screen);
        Assert.True(c.Recording);
    }

    [Fact]
    public void WhatWeCollectListsEveryKindAndBackGoesWhereItCameFrom()
    {
        var c = Consent();
        var m = Menu(c);
        m.Show(Screen.Consent);
        m.Down();
        m.Down();
        m.Select();
        Assert.Equal(Screen.PlaytestData, m.Screen);
        var labels = m.Items.Select(i => i.Label).ToList();
        Assert.Equal("PLAYTEST DATA: OFF", labels[0]);
        Assert.All(Notice.Collect, f => Assert.Contains(f.What, labels));
        Assert.Contains("WHAT WE NEVER COLLECT", labels);
        Assert.Contains($"KEPT FOR {Notice.RetentionMonths} MONTHS", labels);
        Assert.Contains("DELETE MY PLAYTEST DATA", labels);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.Equal(Screen.Consent, m.Screen);
    }

    [Fact]
    public void SettingsHasTheSwitchAndThePage()
    {
        var c = Consent();
        c.Choose(yes: false, Notice);
        var m = Menu(c);
        m.Show(Screen.Settings);
        int at = m.Items.Select(i => i.Label).ToList().IndexOf("PLAYTEST DATA: OFF");
        Assert.True(at > 0);
        while (m.Selected != at)
            m.Down();
        m.Select();
        Assert.True(c.Recording);
        Assert.Equal("PLAYTEST DATA: ON", m.Items[at].Label);
        m.Left();
        Assert.False(c.Recording);
        m.Down();
        Assert.Equal("WHAT WE COLLECT", m.Items[m.Selected].Label);
        m.Select();
        Assert.Equal(Screen.PlaytestData, m.Screen);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.Equal(Screen.Settings, m.Screen);
    }

    [Fact]
    public void TheNoticeSaysHowLongAndWhoToAsk()
    {
        // Law 25 and the GDPR: what, why, how long, and who answers for it.
        Assert.True(Notice.RetentionMonths > 0);
        Assert.Contains("@", Notice.Contact);
        Assert.Contains($"{Notice.RetentionMonths} months", Notice.WhereLine);
        Assert.Contains(Notice.Contact, Notice.WhereLine);
        Assert.NotEmpty(Notice.Collect);
        Assert.Contains("IP address", Notice.Never);
        Assert.All(Notice.Collect, f => Assert.False(string.IsNullOrWhiteSpace(f.Detail)));
    }

    [Fact]
    public void WithoutTheNoticeNothingAsks()
    {
        var m = Menu(Consent());
        m.PlaytestNotice = null;
        m.Show(Screen.Consent);
        Assert.Equal(Screen.Title, m.Screen);
    }
}
