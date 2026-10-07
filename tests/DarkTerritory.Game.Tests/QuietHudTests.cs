using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The HUD's rule since note 281 (GDD §32 "The HUD: your hands and the dark"; the director, 7 Oct: "too much UI on screen
/// ... I like the way Repo and Lethal Company do their UI/UX designs"): only the crosshair and your hands are always there,
/// and everything else comes up when it matters and goes when it doesn't.
/// </summary>
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
    public void TheTuningFileIsTheDefaults()
    {
        // The record's defaults are the file's, so a session that never loads it (a test, a film frame) looks the same.
        Assert.Equal(new HudTuning(), DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File)));
    }
}
