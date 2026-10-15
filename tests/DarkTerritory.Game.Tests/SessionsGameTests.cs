using Ballast;
using Ballast.Net;
using DarkTerritory.Game;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Sessions on this machine (GDD §33.1, §11, §23.4; ARCHITECTURE §8 note 724): the crew's save kept among the crews ridden
/// with and hosted from the front end, the roster's mute, and the lobby's CREW MISCHIEF switch.
/// </summary>
[Collection("Hud.Keys")]
public sealed class SessionsGameTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-sessions-{Guid.NewGuid():N}");

    SaveSlots Saves(string who) => new(Path.Combine(_dir, who, "saves"), C.SaveSlots);
    FrontEnd Menu(string who) => new(C, R, Saves(who), Path.Combine(_dir, who, "settings.json"), () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static Launch? Choose(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => !x.Heading && x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no '{label}' on {m.Screen}: {string.Join(" | ", m.Items.Select(x => x.Label))}");
        Assert.True(m.Items[i].Enabled, $"'{label}' is greyed on {m.Screen}");
        while (m.Selected != i)
            m.Down();
        return m.Select();
    }

    /// <summary>The host's slot 2 campaign after its seventh night, as the app settles it.</summary>
    static CampaignState Settled(int runs = 7, string host = "Dave") =>
        Campaign.New(C, 2, "Owls", 4242) with { Runs = runs, Scrip = 900, LastHost = host, SettledAt = 1_760_000_000_000 + runs };

    [Fact]
    public void TheSaveGoesHostToClientAndTheClientHostsFromIt()
    {
        // Over the wire as a night settles: the host's machine's bytes to a joiner, kept among the crews it's ridden with.
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1200);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var joiner = new ClientSession(net.CreateClient(), Train(), T, P) { Name = "priya" };
        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                joiner.Step(default);
            }
        }
        Run(20);
        var hosts = Saves("dave");
        hosts.Save(Settled());
        host.ShareSave(SaveSlots.Bytes(hosts.Load(2)!));
        Run(10);
        var mine = Saves("priya");
        var kept = mine.Keep(joiner.TakeCrewSave()!);
        Assert.NotNull(kept);
        // Under the crew's id, never in one of priya's own slots.
        Assert.All(mine.List(), x => Assert.Null(x.State));
        Assert.Equal((0, "Owls", 7, 900.0), (kept!.Slot, kept.Name, kept.Runs, kept.Scrip));
        Assert.True(File.Exists(mine.CrewPath(kept.CrewId)));

        // Her front end lists it under the slots, and she can host its next night from it.
        var m = Menu("priya");
        Choose(m, "CAMPAIGN");
        Assert.Contains(m.Items, i => i.Heading && i.Label == "CREWS YOU'VE RIDDEN WITH");
        Assert.Null(Choose(m, "CREW OWLS · NIGHT 8 · LAST HOSTED BY DAVE"));
        Assert.Equal(Screen.Fortress, m.Screen);
        Assert.Equal(kept.CrewId, m.Open!.CrewId);
        Choose(m, "PLAY: ALONE");
        Assert.Contains(m.Items, i => i.Label.StartsWith("CREW MISCHIEF", StringComparison.Ordinal));
        var night = Assert.IsType<Launch.CampaignNight>(Choose(m, "TONIGHT"));
        Assert.Equal((0, kept.CrewId, true), (night.Slot, night.Crew, night.Host));
        // As the app takes the launch: the crew's copy, and saving it goes back to the crew's file.
        var hosting = mine.Load(night.Slot, night.Crew)!;
        Assert.Equal(7, hosting.Runs);
        mine.Save(hosting with { Runs = 8, LastHost = "Priya" });
        Assert.Equal(8, mine.LoadCrew(kept.CrewId)!.Runs);
        Assert.All(mine.List(), x => Assert.Null(x.State));
        // Neither renamed nor deleted from here: it's the crew's.
        m.ShowCrew(kept.CrewId);
        Assert.DoesNotContain(m.Items, i => i.Label.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCopyWithMoreNightsStaysAndAClosedCrewStaysListedAsEnded()
    {
        var mine = Saves("priya");
        mine.Keep(Settled(7));
        // An older copy (another crewmate's, from before the last night) doesn't take its place.
        Assert.Equal(7, mine.Keep(Settled(6, "Sam")).Runs);
        Assert.Equal("Dave", mine.Crews().Single().LastHost);
        // A newer one does.
        Assert.Equal(8, mine.Keep(Settled(8, "Sam")).Runs);
        // Taken at dawn on another fork: every copy's closed, and it stays listed, ended.
        var ended = mine.Keep(Settled(6, "Sam") with { Over = true });
        Assert.True(ended.Over);
        Assert.Equal(8, ended.Runs);
        var m = Menu("priya");
        Choose(m, "CAMPAIGN");
        Assert.Contains(m.Items, i => i.Label == "CREW OWLS · TAKEN AT DAWN · LAST HOSTED BY SAM");
        // Bytes that aren't a save are let go.
        Assert.Null(mine.Keep("not json"u8.ToArray()));
        // A crew in one of my own slots with as many nights isn't listed twice.
        var own = Saves("dave");
        own.Save(Settled(7));
        own.Keep(Settled(7));
        var d = Menu("dave");
        Choose(d, "CAMPAIGN");
        Assert.DoesNotContain(d.Items, i => i.Heading && i.Label == "CREWS YOU'VE RIDDEN WITH");
    }

    [Fact]
    public void AMutedCrewmateIsntHeardOnAnyPath()
    {
        // Near, on the radio and down the speaking tube: frames arrive, nothing of them is played.
        foreach (var (car, z, radio, tube) in new[] { (3, 4.0, false, false), (9, 0.0, true, false), (0, 0.0, false, true) })
        {
            var heard = VoiceBench.Run(Content, speakerCar: car, speakerZ: z, radio: radio, tube: tube, seconds: 1);
            var muted = VoiceBench.Run(Content, speakerCar: car, speakerZ: z, radio: radio, tube: tube, seconds: 1, muted: true);
            double Loudest(VoiceBenchReport r) => Math.Max(r.NearDb, Math.Max(r.RadioDb, r.TubeDb));
            Assert.True(Loudest(heard) > -60, $"{Loudest(heard)} dB unmuted");
            Assert.True(muted.FramesHeard > 0, "no frames came");
            Assert.Equal(-180, Loudest(muted));
        }
    }

    [Fact]
    public void TheRosterSaysWhosMutedAndNumbersTheCrewForItsKeys()
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 900));
        world.Names[1] = "Dave";
        world.Names[2] = "Priya";
        world.Names[3] = "Sam";
        var lines = NetPlaySession.RosterOf(2, default, [(1, default), (3, default)], world, id => id == 3);
        Assert.Equal([false, false, true], lines.Select(l => l.Muted));
        Assert.Equal(["1  DAVE", "   PRIYA", "2  SAM"], Hud.RosterNames(lines, numbered: true));
        Assert.Equal("MUTE : [1-9]   PUT OFF : [SHIFT] [1-9]", Hud.RosterKeys(host: true, vote: false));
        Assert.Equal("MUTE : [1-9]", Hud.RosterKeys(host: false, vote: false));
        Assert.Contains("VOTE TO PUT OFF", Hud.RosterKeys(host: false, vote: true));
        // The vote's line across the top, and the one voted on told.
        var vote = new KickVote(3, 1, 2, 900);
        Assert.Equal("PUT SAM OFF AT THE NEXT STOP? 1 OF 2 · 24 S   YES : [Q] [SHIFT] [2]", Hud.KickVoteLine(vote, "SAM", onYou: false, 23.2, 2));
        Assert.Equal("THE CREW IS VOTING TO PUT YOU OFF · 1 OF 2 · 24 S", Hud.KickVoteLine(vote, "SAM", onYou: true, 23.2, 0));
        Assert.Null(Hud.KickVoteLine(KickVote.None, "", false, 0, 0));
        // It draws, keys and all.
        var o = new Ballast.Render.Overlay();
        Hud.Roster(o, 480, 270, lines, null, Hud.RosterKeys(true, false));
        Hud.KickVoteBanner(o, 480, 270, Hud.KickVoteLine(vote, "SAM", false, 23.2, 2)!);
    }

    [Fact]
    public void CrewMischiefStartsByMoodAndTheHostSetsIt()
    {
        var h = P.Horseplay;
        Assert.True(Moods.MischiefByDefault(RunMood.Laughs, isPrivate: false, h));
        Assert.True(Moods.MischiefByDefault(RunMood.Either, isPrivate: false, h));
        Assert.False(Moods.MischiefByDefault(RunMood.Competitive, isPrivate: false, h));
        Assert.True(Moods.MischiefByDefault(RunMood.Competitive, isPrivate: true, h));
        Assert.True(Moods.MischiefByDefault(RunMood.Competitive, isPrivate: false, h with { CompetitiveOff = false }));
        // The host screen: on by default, chosen off, and the lobby opened with it off.
        var m = Menu("nick");
        Choose(m, "HOST");
        Assert.Contains("CREW MISCHIEF: ON", m.Items.Select(i => i.Label));
        Assert.Null(Choose(m, "CREW MISCHIEF"));
        Assert.Contains("CREW MISCHIEF: OFF", m.Items.Select(i => i.Label));
        Assert.False(Assert.IsType<Launch.Night>(Choose(m, "OPEN THE LOBBY")).Mischief);
        // In the yard, the host's night menu has it.
        m.OpenNight(new NightMenu(Yard: true) { Mischief = true });
        Assert.Equal(new Launch.SetMischief(false), Choose(m, "CREW MISCHIEF: ON"));
        m.CloseNight();
        m.OpenNight(new NightMenu(Hosting: false, Yard: true));
        Assert.DoesNotContain(m.Items, i => i.Label.StartsWith("CREW MISCHIEF", StringComparison.Ordinal));
        // Carried in the night's setup to every machine's world.
        var setup = SessionSetup.Decode(new SessionSetup(Line: "test-loop", Cars: 3, Enemies: false) { Mischief = false }.Encode());
        Assert.False(setup.Mischief);
        Assert.False(setup.Build(Content).World.CrewMischief);
        Assert.True(SessionSetup.Decode(new SessionSetup(Line: "test-loop").Encode()).Mischief);
    }
}
