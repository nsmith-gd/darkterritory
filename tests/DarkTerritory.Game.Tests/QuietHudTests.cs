using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The HUD's rule since note 285 (GDD §32 "The HUD: your hands and the dark"; the director, 7 Oct: "too much UI on screen
/// ... I like the way Repo and Lethal Company do their UI/UX designs"): only the crosshair and your hands are always there,
/// and everything else comes up when it matters and goes when it doesn't.
/// </summary>
// Hud.Keys is the HUD's settings, static: the tests that set it don't run beside the one that compares two builds (note 390).
[Collection("Hud.Keys")]
public class QuietHudTests
{
    static readonly string Content = DataFile.FindContentRoot();
    const int W = 480, H = 270;

    /// <summary>A session as another seat at a networked night sees it: a solo session with a link to show.</summary>
    sealed class Linked(PrototypeSession inner, LinkInfo link) : IPlaySession
    {
        public TrainOnLine Train => inner.Train;
        public World World => inner.World;
        public Route? Route => inner.Route;
        public PlayerState Player => inner.Player;
        public TrainControls Controls => inner.Controls;
        public long Tick => inner.Tick;
        public PlayerTuning PlayerTuning => inner.PlayerTuning;
        public LinkInfo? Link => link;
        public string Status() => inner.Status();
        public void Step(in PlayerIntent intent) => inner.Step(intent);
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => inner.InterpolatedFrames(alpha);
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) =>
            inner.EyeCamera(frames, alpha, pendingYaw, pendingPitch);
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => inner.Crew(frames, alpha);
    }

    static Overlay Drawn(IPlaySession s)
    {
        var o = new Overlay();
        Hud.Build(o, W, H, s);
        return o;
    }

    /// <summary>How much is drawn in a box of the canvas.</summary>
    static int In(Overlay o, float x0, float y0, float x1, float y1) =>
        o.Vertices.Count(v => v.Position.X >= x0 && v.Position.X <= x1 && v.Position.Y >= y0 && v.Position.Y <= y1);

    /// <summary>A generated night with the train out past its yard, standing <paramref name="at"/> m along the line.</summary>
    static PrototypeSession OutOnTheLine(double at)
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var s = new PrototypeSession(Content, route, 4, enemies: false, at: at);
        s.Step(default);
        Assert.Equal(Sim.Run.RunPhase.Underway, s.World.Run!.Phase);
        return s;
    }

    [Fact]
    public void OnARoofWithEmptyHandsTheHudIsTheCrosshairAndTheHotbar()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        var o = Drawn(s);
        Assert.NotEmpty(o.Vertices);
        // The crosshair, a dot in the middle; the hotbar, a slot or two at the bottom centre. Nothing else anywhere.
        Assert.All(o.Vertices, v => Assert.True(
            (Math.Abs(v.Position.X - W / 2f) <= 5 && Math.Abs(v.Position.Y - H / 2f) <= 5)
            || (Math.Abs(v.Position.X - W / 2f) <= 40 && v.Position.Y >= H - Hud.SlotSize - 8),
            $"something's drawn at {v.Position}"));
        Assert.True(In(o, W / 2f - 40, H - Hud.SlotSize - 8, W / 2f + 40, H) > 0, "no hotbar");
    }

    [Fact]
    public void ChangingHandsNamesTheToolForAMomentThenItsGone()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        // Just over the hotbar, where the name goes.
        float y = H - Hud.SlotSize - 6;
        Assert.Equal(0, In(Drawn(s), W / 2f - 40, y - 10, W / 2f + 40, y - 1));
        s.Player = s.Player with { HeldSlot = (byte)((s.Player.HeldSlot + 1) % Kit.Slots) };
        Assert.True(In(Drawn(s), W / 2f - 40, y - 10, W / 2f + 40, y - 1) > 0, "the new slot isn't named");
        for (int i = 0; i < (Hud.Tuning.ToolNameSeconds + 0.1) * SimConstants.TickRate; i++)
            s.Step(default);
        Assert.Equal(0, In(Drawn(s), W / 2f - 40, y - 10, W / 2f + 40, y - 1));
    }

    [Fact]
    public void ThePingIsBigInTheLobbyAndOutOnTheLineOnlyWhenItsBad()
    {
        // The top right, where the ping is.
        static int TopRight(Overlay o) => In(o, W * 0.7f, 0, W, 30);
        // Spec E: in the lobby, prominently, however good it is.
        var yard = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4, enemies: false);
        Assert.Equal(Sim.Run.RunPhase.Yard, yard.World.Run!.Phase);
        Assert.True(TopRight(Drawn(new Linked(yard, new LinkInfo("JOINED", 40, 2, null, false)))) > 0);
        // Out on the line, a good link says nothing; a bad one, or a lost one, does.
        var line = OutOnTheLine(4000);
        Assert.Equal(0, TopRight(Drawn(new Linked(line, new LinkInfo("JOINED", 40, 2, null, false)))));
        Assert.True(TopRight(Drawn(new Linked(line, new LinkInfo("JOINED", Hud.Tuning.PingWarnMs + 20, 2, null, false)))) > 0);
        Assert.True(TopRight(Drawn(new Linked(line, new LinkInfo("JOINED", null, 2, null, true) { CanReconnect = true }))) > 0);
        // The host has no ping to show.
        Assert.Equal(0, TopRight(Drawn(new Linked(line, new LinkInfo("HOST", null, 2, null, false)))));
    }

    [Fact]
    public void ALinkLosingTooMuchIsSaidOutOnTheLineAndAGoodOneIsnt()
    {
        // Note 534 (netcode-audit.md gap 3): loss is shown as a bad ping is, only once it's bad.
        static int TopRight(Overlay o) => In(o, W * 0.7f, 0, W, 30);
        var line = OutOnTheLine(4000);
        var good = new LinkInfo("JOINED", 40, 2, null, false) { Loss = Hud.Tuning.LossGood / 2 };
        Assert.Equal(0, TopRight(Drawn(new Linked(line, good))));
        Assert.True(TopRight(Drawn(new Linked(line, good with { Loss = Hud.Tuning.LossWarn + 0.02 }))) > 0);
    }

    [Fact]
    public void TheLinksQualityIsSaidAsWhatsLostAndHowItsReached()
    {
        var relayed = new Ballast.Net.CarrierLink("Steam", Relayed: true, 42, 0.99f);
        var direct = new Ballast.Net.CarrierLink("", Relayed: false);
        var link = new LinkInfo("JOINED", 40, 2, null, false);
        Assert.Null(Hud.QualityLine(link));
        Assert.Equal("2% LOST, VIA STEAM RELAY", Hud.QualityLine(link with { Loss = 0.02, Via = relayed }));
        Assert.Equal("0% LOST, DIRECT", Hud.QualityLine(link with { Loss = 0, Via = direct }));
        Assert.Equal("<1% LOST", Hud.QualityLine(link with { Loss = 0.003 }));
        // The host's panel: each crewmate's round trip, loss and route, inked by the worst of them.
        var (fine, fineInk) = Hud.CrewLinkLine(new CrewLink(2, 30, 0, direct));
        Assert.Equal("30 MS, 0% LOST, DIRECT", fine);
        var (poor, poorInk) = Hud.CrewLinkLine(new CrewLink(3, 30, Hud.Tuning.LossWarn + 0.05, relayed));
        Assert.Equal("30 MS, 13% LOST, STEAM RELAY", poor);
        Assert.NotEqual(fineInk, poorInk);
        Assert.Equal(poorInk, Hud.CrewLinkLine(new CrewLink(4, Hud.Tuning.PingWarnMs + 50, 0, direct)).Ink);
    }

    [Fact]
    public void AHostOutOnTheLineIsToldWhoseLinkIsBad()
    {
        // Note 540: hosting, the corner names whoever's link has gone bad, and says nothing while everyone's is fine.
        static int TopRight(Overlay o) => In(o, W * 0.7f, 0, W, 30);
        var line = OutOnTheLine(4000);
        var fine = new LinkInfo("HOST", null, 3, null, false) { Crew = [new CrewLink(2, 40, 0.01, null), new CrewLink(3, 60, 0, null)] };
        Assert.Equal(0, TopRight(Drawn(new Linked(line, fine))));
        var struggling = fine with { Crew = [new CrewLink(2, 40, 0.01, null), new CrewLink(3, 60, Hud.Tuning.LossWarn + 0.05, null)] };
        Assert.True(TopRight(Drawn(new Linked(line, struggling))) > 0);
    }

    [Fact]
    public void TheBadLinksAreNamedWorstFirstAndTheRestCounted()
    {
        RosterLine Named(byte id, string name) => new(id, name, "", true);
        var roster = new[] { Named(1, "YOU"), Named(2, "PRIYA"), Named(3, "SAM"), Named(4, "ALEX"), Named(5, "JO"), Named(6, "LEE") };
        double warn = Hud.Tuning.PingWarnMs, lossy = Hud.Tuning.LossWarn;
        var link = new LinkInfo("HOST", null, 6, null, false)
        {
            Crew =
            [
                new CrewLink(2, 40, lossy + 0.05, null), // far over on loss
                new CrewLink(3, warn * 2, 0, null),      // twice the ping warning: the worst
                new CrewLink(4, 40, 0, null),            // fine: not named
                new CrewLink(5, warn + 1, 0, null),
                new CrewLink(6, warn + 2, 0, null),
            ],
        };
        var lines = Hud.BadLinks(link, roster).Select(l => l.Text).ToList();
        Assert.Equal([$"SAM: PING {warn * 2:0} MS", $"PRIYA: {Hud.Percent(lossy + 0.05)} LOST", $"LEE: PING {warn + 2:0} MS", "AND 1 MORE"], lines);
        Assert.Empty(Hud.BadLinks(link with { Crew = [new CrewLink(4, 40, 0, null)] }, roster));
    }

    [Fact]
    public void TheLinksCornerSaysWhatToDoAsTheAlarmDoes()
    {
        // Note 476: under NO LINK, the action and its key (note 285's form), as the alarm in the middle says it, never key first.
        var lost = new LinkInfo("JOINED", null, 2, null, true);
        Assert.Null(Hud.LinkLine(lost with { Lost = false }));
        Assert.Equal("RECONNECTING: TRY 2 OF 5", Hud.LinkLine(lost with { Attempt = 2, Attempts = 5 }));
        Assert.Equal("RECONNECT : [F5]", Hud.LinkLine(lost with { CanReconnect = true }));
        Assert.Equal("CREW FULL (8/8)   TRY AGAIN : [F5]", Hud.LinkLine(lost with { Refused = "CREW FULL (8/8)" }));
        Assert.Null(Hud.LinkLine(lost));
        foreach (var line in new[] { lost with { CanReconnect = true }, lost with { Refused = "WRONG PASSWORD" } }.Select(Hud.LinkLine))
            Assert.DoesNotMatch(@"^\[|\]\s+[A-Z]", line!);
    }

    [Fact]
    public void APlaceComingUpIsNamedForAWhileAndTheRouteCardHasTheRest()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var s = OutOnTheLine(4000);
        var next = Hud.Ahead(route, s.Train.Dynamics.Distance)!.Value;
        // The name's at the HUD's own size, a canvas pixel to the font's (the fine print under it, and the cold's line, are
        // half that): quads a pixel or more high at the top centre.
        static int TopCentre(Overlay o) => Enumerable.Range(0, o.Count / 6).Select(q => (A: o.Vertices[6 * q], C: o.Vertices[6 * q + 2]))
            .Count(q => q.C.Position.Y - q.A.Position.Y >= 1 && q.A.Position.Y < 30 && Math.Abs(q.A.Position.X - W / 2f) < W / 4f);
        // Further off than the tuning's distance, nothing; once it's that near, its name, and after its time, nothing again.
        if (next.At - s.Train.Dynamics.Distance > Hud.Tuning.PlaceAheadMetres)
            Assert.Equal(0, TopCentre(Drawn(s)));
        var near = OutOnTheLine(next.At - Hud.Tuning.PlaceAheadMetres / 2);
        Assert.True(TopCentre(Drawn(near)) > 0, $"{next.Name} isn't named coming up to it");
        for (int i = 0; i < (Hud.Tuning.PlaceSeconds + 0.1) * SimConstants.TickRate; i++)
            near.Step(default);
        Assert.Equal(0, TopCentre(Drawn(near)));
    }

    [Fact]
    public void TheNoiseMeterOnlyShowsOnceTheCrewsLoud()
    {
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 4), 4);
        Assert.NotNull(s.World.Combat);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        float threshold = (float)s.World.Combat!.Choir.Threshold;
        // Over the hotbar, under the middle of the screen.
        static int Meter(Overlay o) => In(o, W / 2f - 40, H * 0.7f, W / 2f + 40, H - Hud.SlotSize - 12);
        s.World.Choir.Loudness = 0;
        Assert.Equal(0, Meter(Drawn(s)));
        s.World.Choir.Loudness = threshold * (Hud.Tuning.NoiseShowAt + 0.1);
        Assert.True(Meter(Drawn(s)) > 0, "loud, and no meter");
    }

    [Fact]
    public void WithTheControlHintsOffTheCornerKeepsOnlyWhatItsAbout()
    {
        // The director (note 285): "a setting to hide corner controls". In the cab, the corner's the speed over the keys;
        // with the hints off, the speed alone.
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        static int Corner(Overlay o) => In(o, W * 0.6f, H * 0.6f, W, H);
        var keys = Hud.Keys;
        try
        {
            int on = Corner(Drawn(s));
            Hud.Keys = keys with { ControlHints = false };
            int off = Corner(Drawn(s));
            Assert.True(off > 0, "the speed went with the hints");
            Assert.True(off < on / 2, $"the hints are still drawn ({off} of {on} vertices)");
        }
        finally
        {
            Hud.Keys = keys;
        }
    }

    [Fact]
    public void TheTuningFileIsTheDefaults()
    {
        // The record's defaults are the file's, so a session that never loads it (a test, a film frame) looks the same.
        Assert.Equal(new HudTuning(), DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File)));
    }

    [Fact]
    public void ThePanelsYouOpenAreFinePrintWithNoRivets()
    {
        // Note 316 (note 285's "not yet"): the roster (Q) and the supplies (I) in the ballot's form: fine print on a dark
        // backing, no riveted plate and no brass trim, and narrower than a plate in the HUD's full-size text was.
        var s = new PrototypeSession(Content, "test-loop", 4);
        Drawn(s); // the fine print's scale, as a frame sets it
        static bool Riveted(Overlay o) => o.Vertices.Any(v => v.Colour == UiStyle.Rivet || v.Colour == UiStyle.Brass || v.Colour == UiStyle.Bevel);
        static float Wide(Overlay o) => o.Vertices.Max(v => v.Position.X) - o.Vertices.Min(v => v.Position.X);
        var supplies = new Overlay();
        Hud.Supplies(supplies, W, H, s);
        Assert.True(supplies.Count > 0);
        Assert.False(Riveted(supplies));
        Assert.True(Wide(supplies) < W * 0.6f, $"{Wide(supplies)} wide");
        var (lines, heard) = Staging.Roster(s.Train, Content);
        var roster = new Overlay();
        Hud.Roster(roster, W, H, lines, heard);
        Assert.True(roster.Count > 0);
        Assert.False(Riveted(roster));
        Assert.True(Wide(roster) < 200, $"{Wide(roster)} wide (the plate was 260)");
        // The roster still says who's speaking, in green.
        Assert.Contains(roster.Vertices, v => v.Colour.Y > 0.8f && v.Colour.X < 0.6f);
    }
}
