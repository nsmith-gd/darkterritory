using Ballast;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The interface sounds (the audio checklist's ui-* lines): the menus ask for theirs as they're worked; in the night, the
/// hold you're working, the report coming up and the respawn queue moving are read off the replicated world each tick.
/// </summary>
public sealed class UiSoundTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning Campaign = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning RunT = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly BoilerTuning B = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly HoldoutTuning H = DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-uisound-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    FrontEnd Menu(List<string> heard, EditionTuning? edition = null) =>
        new(Campaign, RunT, new SaveSlots(Path.Combine(_dir, "saves"), Campaign.SaveSlots), Path.Combine(_dir, "settings.json"), () => 42, edition)
        {
            Cue = heard.Add,
        };

    static void Pick(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no '{label}' on {m.Screen}");
        while (m.Selected != i)
            m.Down();
    }

    [Fact]
    public void EveryInterfaceSoundIsFlatOnItsTier()
    {
        // tools/audio/install.py writes them so: heard without position, on the interface's tier.
        var audio = new GameAudio(Content);
        foreach (var name in typeof(UiCue).GetFields().Select(f => (string)f.GetValue(null)!))
            if (audio.Bank.Get(name) is { } def)
                Assert.True(def.Flat && def.Tier == 4, $"{name}: flat {def.Flat}, tier {def.Tier}");
        Assert.True(audio.Bank.Get(UiCue.Hold)!.Loop && audio.Bank.Get(UiCue.Title)!.Loop);
    }

    [Fact]
    public void TheMenusAskForAMoveAChoiceAndBackingOut()
    {
        var heard = new List<string>();
        var m = Menu(heard);
        // At the title, Back goes nowhere, so it says nothing.
        m.Back();
        Assert.Empty(heard);
        m.Down();
        m.Up();
        Assert.Equal([UiCue.Move, UiCue.Move], heard);

        Pick(m, "QUICK NIGHT");
        heard.Clear();
        Assert.Null(m.Select());
        Assert.Equal(Screen.QuickNight, m.Screen);
        Assert.Equal([UiCue.Select], heard);

        // A value stepped is a move; at its end, where it doesn't change, nothing.
        Pick(m, "CARS");
        heard.Clear();
        m.Right();
        Assert.Equal([UiCue.Move], heard);
        for (int i = 0; i < 40; i++)
            m.Right();
        heard.Clear();
        m.Right();
        Assert.Empty(heard);
        // Enter on a value with nothing to choose says nothing either.
        m.Select();
        Assert.Empty(heard);

        // Choosing BACK, and the Back key, both back out.
        Pick(m, "BACK");
        heard.Clear();
        m.Select();
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Equal([UiCue.Back], heard);
        Pick(m, "SETTINGS");
        m.Select();
        heard.Clear();
        m.Back();
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Equal([UiCue.Back], heard);

        // Starting a night is a choice like any other.
        Pick(m, "QUICK NIGHT");
        m.Select();
        Pick(m, "PLAY");
        heard.Clear();
        Assert.IsType<Launch.Night>(m.Select());
        Assert.Equal([UiCue.Select], heard);
    }

    [Fact]
    public void BindingAKeyIsAChoiceAndEscapeBacksOut()
    {
        var heard = new List<string>();
        var m = Menu(heard);
        Pick(m, "SETTINGS");
        m.Select();
        Pick(m, "CONTROLS");
        m.Select();
        m.Select();
        Assert.NotNull(m.Capturing);
        heard.Clear();
        m.Bind("K");
        Assert.Equal([UiCue.Select], heard);
        m.Select();
        heard.Clear();
        m.Back();
        Assert.Null(m.Capturing);
        Assert.Equal([UiCue.Back], heard);
    }

    [Fact]
    public void OnlyTheDemoEndsANightOnItsEndCard()
    {
        var heard = new List<string>();
        Menu(heard).NightOver();
        Assert.Empty(heard);
        var demo = Menu(heard, new EditionTuning { AfterNight = "Wishlist Dark Territory on Steam." });
        demo.NightOver();
        Assert.Equal([UiCue.EndCard], heard);
    }

    static int Played(GameAudio audio, string name) => audio.Mixer.Voices.Count(v => v.Name == name);

    static GameAudio Audio() => new(Content);

    [Fact]
    public void AReloadStepHoldsItsLoopHurryingThenCompletesAndLettingGoCancels()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
        int g = Enumerable.Range(0, w.Train.Vehicles.Count).Last(i => w.Train.Vehicles[i].HasGun);
        var s = PlayerMotor.SpawnOnRoof(w.Train, g, Guns.Mount(w.Train, g)!.Value.Position.Z + 0.5, P);
        Assert.Equal(g, Guns.MannedGun(s, w.Train, C.Guns));
        w.Train.Vehicles[g].Gun.ReloadNeeded = 2;
        var audio = Audio();
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        void Tick(in PlayerIntent intent)
        {
            Guns.Reload(s, intent, w.Train, C.Guns, SimConstants.TickSeconds);
            audio.Interface(w, s, 1);
        }

        audio.Interface(w, s, 1);
        Assert.Equal(0, Played(audio, UiCue.Hold));
        Tick(use);
        var hold = Assert.Single(audio.Mixer.Voices, v => v.Name == UiCue.Hold);
        int half = (int)(C.Guns.ReloadStepSeconds / 2 * SimConstants.TickRate);
        for (int i = 1; i < half; i++)
            Tick(use);
        Assert.InRange(hold.Params.Get("progress"), 0.45, 0.55);
        Assert.False(hold.Finished);
        // Held to the end of the step: done, once, and the loop stops.
        while (w.Train.Vehicles[g].Gun.ReloadNeeded == 2)
            Tick(use);
        Assert.Equal(1, Played(audio, UiCue.Complete));
        Assert.True(hold.Finished);
        Assert.Equal(0, Played(audio, UiCue.Cancel));

        // The next step's started, and let go halfway: cancelled, once, its loop stopped.
        for (int i = 0; i < half; i++)
            Tick(use);
        var next = audio.Mixer.Voices.Last(v => v.Name == UiCue.Hold);
        Assert.NotSame(hold, next);
        Assert.False(next.Finished);
        Tick(default);
        Tick(default);
        Assert.Equal(1, Played(audio, UiCue.Cancel));
        Assert.Equal(1, Played(audio, UiCue.Complete));
        Assert.True(next.Finished);
    }

    [Fact]
    public void ClearingAFoulAndBoardingUpABreachHoldTheLoopAndCompleteWhenDone()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
        var audio = Audio();

        // A fouled gun (GDD §23), cleared on the reload's count: the loop at its fraction, done once it's clear.
        int g = Enumerable.Range(0, w.Train.Vehicles.Count).Last(i => w.Train.Vehicles[i].HasGun);
        var gunner = PlayerMotor.SpawnOnRoof(w.Train, g, Guns.Mount(w.Train, g)!.Value.Position.Z + 0.5, P);
        w.Train.Vehicles[g].Gun.Jammed = true;
        w.Train.Vehicles[g].Gun.ReloadProgress = C.Guns.FoulClearSeconds / 2;
        audio.Interface(w, gunner, 1);
        var hold = Assert.Single(audio.Mixer.Voices, v => v.Name == UiCue.Hold);
        Assert.InRange(hold.Params.Get("progress"), 0.45, 0.55);
        w.Train.Vehicles[g].Gun.Jammed = false;
        w.Train.Vehicles[g].Gun.ReloadProgress = 0;
        audio.Interface(w, gunner, 1);
        Assert.Equal(1, Played(audio, UiCue.Complete));
        Assert.True(hold.Finished);

        // A breached car (decided 1 Oct), boarded up from inside at the hole on the boarder's own count.
        const int car = 2;
        w.Train.Vehicles[car].Breach(Breaches.EndWall(w.Train.Frames[car].Shape)!.Value);
        var boarder = new PlayerState
        {
            Parent = car, Position = Breaches.StandAt(w.Train, car), Surface = Surface.Deck, Health = P.Health,
            ActionProgress = T.Breach.BoardSeconds / 2,
        };
        audio.Interface(w, boarder, 1);
        var board = audio.Mixer.Voices.Last(v => v.Name == UiCue.Hold);
        Assert.NotSame(hold, board);
        Assert.InRange(board.Params.Get("progress"), 0.45, 0.55);
        audio.Interface(w, boarder with { ActionProgress = T.Breach.BoardSeconds }, 1);
        Assert.Equal(2, Played(audio, UiCue.Complete));
        Assert.True(board.Finished);
        Assert.Equal(0, Played(audio, UiCue.Cancel));
    }

    /// <summary>A generated night with a Holdout, and a client's view of it: the host's Holdouts come in through Mirror.</summary>
    static (World World, Holdout Holdout) Holdouts()
    {
        var routes = RouteTuning.Load(Content);
        for (ulong seed = 1; ; seed++)
        {
            var route = RouteGenerator.Generate(routes, RouteTier.Frontier, seed);
            if (route.Features.FirstOrDefault(f => f.Stop is { Holdouts.Count: > 0 }) is not { } site)
                continue;
            var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 0)), route.Build(), site.Start - 100, B), C);
            w.EnableHoldouts(H, route);
            return (w, w.Holdouts!.All.First(h => h.Site == site));
        }
    }

    [Fact]
    public void BreakingAHoldoutOpenHoldsItsLoopUntilItsFreedOrLeft()
    {
        var (w, h) = Holdouts();
        var me = new PlayerState { Parent = PlayerState.World, Position = h.Door, Surface = Surface.Ground, Health = P.Health };
        var audio = Audio();
        double seconds = h.Breach(H).Seconds;
        w.Holdouts!.Mirror(h.Index, HoldoutState.Occupied, 2, 0);
        audio.Interface(w, me, 1);
        w.Holdouts.Mirror(h.Index, HoldoutState.Breaching, 2, 0.1 * seconds);
        audio.Interface(w, me, 1);
        var hold = Assert.Single(audio.Mixer.Voices, v => v.Name == UiCue.Hold);
        w.Holdouts.Mirror(h.Index, HoldoutState.Breaching, 2, 0.8 * seconds);
        audio.Interface(w, me, 1);
        Assert.Equal(0.8, hold.Params.Get("progress"), 3);
        // Let go: the breach falls back to nothing.
        w.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 2, 0);
        audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Cancel));
        Assert.True(hold.Finished);
        // At it again, to the end: they're out.
        w.Holdouts.Mirror(h.Index, HoldoutState.Breaching, 2, 0.5 * seconds);
        audio.Interface(w, me, 1);
        w.Holdouts.Mirror(h.Index, HoldoutState.Freed, 2, seconds);
        audio.Interface(w, me, 1);
        audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Complete));
        Assert.Equal(1, Played(audio, UiCue.Cancel));
        Assert.All(audio.Mixer.Voices.Where(v => v.Name == UiCue.Hold), v => Assert.True(v.Finished));
    }

    [Fact]
    public void TheDeadHearTheQueueMoveWhenAHoldoutTakesSomeoneIn()
    {
        var (w, h) = Holdouts();
        var dead = new PlayerState { Parent = PlayerState.World, Position = h.Door + new Double3(200, 0, 0), Death = DeathCause.Mauled };
        var audio = Audio();
        audio.Interface(w, dead, 1);
        // The Holdout lights and takes the first in the queue: it's moved up.
        w.Holdouts!.Mirror(h.Index, HoldoutState.Occupied, 2, 0);
        audio.Interface(w, dead, 1);
        audio.Interface(w, dead, 1);
        Assert.Equal(1, Played(audio, UiCue.Queue));
        // Handed on to the next (2 deferred): it's moved again; you next.
        w.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 3, 0);
        audio.Interface(w, dead, 1);
        w.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 1, 0);
        audio.Interface(w, dead, 1);
        Assert.Equal(3, Played(audio, UiCue.Queue));
        // The living don't hear the queue.
        var alive = Audio();
        var (w2, h2) = Holdouts();
        var living = dead with { Death = DeathCause.None };
        alive.Interface(w2, living, 1);
        w2.Holdouts!.Mirror(h2.Index, HoldoutState.Occupied, 2, 0);
        alive.Interface(w2, living, 1);
        Assert.Equal(0, Played(alive, UiCue.Queue));
    }

    [Fact]
    public void TheReportStampsEachDeathOfTheNightAndTypesOutTheOnesTheCrewDidToThemselves()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), route.Build(), 5_000, B), C);
        w.EnableRun(RunT, route, 600, authority: true);
        var me = PlayerMotor.SpawnInCab(w.Train, P);
        var mate = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        var other = PlayerMotor.SpawnOnRoof(w.Train, 3, 0, P);
        var audio = StampingAudio();
        audio.Interface(w, me, 1);
        // One into a tunnel's mouth (their own doing), one taken by something (not): the host's world gives each a body, and
        // the report lists them.
        w.StepBodies([(1, me), (2, mate with { Health = 0, Death = DeathCause.Struck }), (3, other with { Health = 0, Death = DeathCause.Mauled })]);
        w.Derail();
        w.StepRun([me]);
        Assert.Equal(2, w.Run!.Report!.Fatalities.Count);
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
            audio.Interface(w, me, 1);
        Assert.Equal(Hud.DeathLines(w.Run.Report).Count, Played(audio, UiCue.DeathStamp));
        Assert.Equal(2, Played(audio, UiCue.DeathStamp));
        Assert.Equal(1, Played(audio, UiCue.OwnGoal));
        // After the report's own lines.
        var first = audio.Mixer.Voices.First(v => v.Name == UiCue.DeathStamp);
        Assert.All(audio.Mixer.Voices.Where(v => v.Name == UiCue.Tally), t => Assert.True(t.Id < first.Id));
    }

    /// <summary>The interface sounds with the report's stamp and typewriter, whether or not they're installed yet.</summary>
    GameAudio StampingAudio()
    {
        var audio = Audio();
        foreach (var cue in new[] { UiCue.DeathStamp, UiCue.OwnGoal })
            audio.Bank.Add(cue, new Ballast.Audio.SoundDef(4, [new Ballast.Audio.LayerDef(Ballast.Audio.SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, Flat: true));
        return audio;
    }

    [Fact]
    public void OnAClientTheReportTalliesInWhenItArrivesAndALongListStampsItsLines()
    {
        // A client mirrors the run: the phase can come a snapshot before the report it's sent (WorldRecords' report record).
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), route.Build(), 5_000, B), C);
        w.EnableRun(RunT, route, 600, authority: false);
        var me = PlayerMotor.SpawnInCab(w.Train, P);
        var audio = StampingAudio();
        audio.Interface(w, me, 1);
        w.Run!.Mirror(RunPhase.Failed, RunEnd.Derailed, 300, -1, false, []);
        for (int i = 0; i < SimConstants.TickRate; i++)
            audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Report));
        Assert.Equal(0, Played(audio, UiCue.Tally));
        // A massacre: the HUD lists the first few and a line for the rest, and each line it shows is stamped.
        var dead = Enumerable.Range(1, 9).Select(i => new Fatality(i, i == 1 ? DeathCause.Thrown : DeathCause.Derailed, 2, DeathSpot.Roof, 5));
        var report = new RunReport(RunEnd.Derailed, 300, 5, 0, 3, 0, 0, 0, 0, 0, 0, 0, 9, Fatalities: new DeathRoll(dead));
        w.Run.MirrorReport(report);
        for (int i = 0; i < 12 * SimConstants.TickRate; i++)
            audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Report));
        Assert.Equal(Hud.ReportLines(report).Count - 1, Played(audio, UiCue.Tally));
        Assert.True(Hud.DeathLines(report).Count < 9);
        Assert.Equal(Hud.DeathLines(report).Count, Played(audio, UiCue.DeathStamp));
        Assert.Equal(1, Played(audio, UiCue.OwnGoal));
    }

    [Fact]
    public void TheNightsReportComesUpOnceAndItsLinesTallyIn()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), route.Build(), 5_000, B), C);
        w.EnableRun(RunT, route, 600, authority: true);
        var me = PlayerMotor.SpawnInCab(w.Train, P);
        var audio = Audio();
        for (int i = 0; i < 5; i++)
        {
            w.StepRun([me]);
            audio.Interface(w, me, 1);
        }
        Assert.Equal(0, Played(audio, UiCue.Report));
        w.Derail();
        w.StepRun([me]);
        Assert.True(w.Run!.Over);
        audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Report));
        Assert.Equal(0, Played(audio, UiCue.Tally));
        // A line under the headline for each tally, a moment after it comes up.
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Report));
        Assert.Equal(Hud.ReportLines(w.Run.Report!).Count - 1, Played(audio, UiCue.Tally));
        Assert.True(Played(audio, UiCue.Tally) > 0);
        // Left and come back to (or joined) already over: not news.
        audio.InterfaceEnd();
        audio.Interface(w, me, 1);
        Assert.Equal(1, Played(audio, UiCue.Report));
    }
}
