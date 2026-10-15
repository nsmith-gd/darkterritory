using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Painted masks (GDD §29; note 731): PAINT YOUR MASK in the settings and the yard's menu, kept in the settings; the crew's
/// sigil set on the fortress board, kept in the save and taken into the night; and every stencil look.json lists drawn.
/// </summary>
public sealed class MaskPaintGameTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly LookTuning L = DataFile.Load<LookTuning>(Path.Combine(Content, LookTuning.File));
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-masks-{Guid.NewGuid():N}");

    FrontEnd Menu() => new(C, R, new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots), Path.Combine(_dir, "settings.json"), () => 42);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static void Choose(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => !x.Heading && x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, $"no {label} in {string.Join(" | ", m.Items.Select(x => x.Label))}");
        while (m.Selected != i)
            m.Down();
    }

    [Fact]
    public void TheMaskIsPaintedOnItsOwnPageAndKeptInTheSettings()
    {
        var m = Menu();
        m.Show(Screen.Settings);
        Choose(m, "PAINT YOUR MASK");
        Assert.Equal("PAINT YOUR MASK: UNPAINTED", m.Items[m.Selected].Label);
        m.Select();
        Assert.Equal(Screen.Paint, m.Screen);
        // The coat round its paints, the stencil round its stencils; the stencil's colour only once there's a stencil.
        Choose(m, "COAT");
        m.Right();
        m.Right();
        m.Right();
        Assert.Equal("COAT: SIGNAL RED", m.Items[m.Selected].Label);
        Assert.DoesNotContain(m.Items, x => x.Label.StartsWith("STENCIL COLOUR", StringComparison.Ordinal));
        Choose(m, "STENCIL");
        m.Left();
        Assert.Equal("STENCIL: ANCHOR", m.Items[m.Selected].Label);
        Choose(m, "STENCIL COLOUR");
        Assert.Equal("STENCIL COLOUR: BONE", m.Items[m.Selected].Label);
        m.Right();
        Assert.Equal("STENCIL COLOUR: SOOT", m.Items[m.Selected].Label);
        var saved = Settings.Load(Path.Combine(_dir, "settings.json"));
        Assert.Equal(new MaskPaint(3, 12, 2), saved.MaskPaint(m.MaskPaintNames.Count, m.StencilNames.Count));
        // DONE goes back where it was chosen, which says what's on it now.
        Choose(m, "DONE");
        m.Select();
        Assert.Equal(Screen.Settings, m.Screen);
        Assert.Contains(m.Items, x => x.Label == "PAINT YOUR MASK: SIGNAL RED, ANCHOR");
        // Off the end of what's on offer, a part's none.
        Assert.Equal(new MaskPaint(0, 0, 0), new Settings { MaskColour = 11, MaskStencil = 13, MaskInk = -1 }.MaskPaint(10, 12));
    }

    [Fact]
    public void TheYardsMenuPaintsTheMaskAndEscapeGoesBackToIt()
    {
        var m = Menu();
        m.OpenNight(new NightMenu(Yard: true));
        Choose(m, "PAINT YOUR MASK");
        m.Select();
        Assert.Equal(Screen.Paint, m.Screen);
        m.Back();
        Assert.Equal(Screen.Night, m.Screen);
        m.CloseNight();
        // Past the gate it's not on the night's menu (the settings still have it, for the next night).
        m.OpenNight(new NightMenu());
        Assert.DoesNotContain(m.Items, x => x.Label.StartsWith("PAINT YOUR MASK", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCrewsSigilIsSetOnTheFortressBoardAndGoesIntoTheNight()
    {
        var saves = new SaveSlots(Path.Combine(_dir, "saves"), C.SaveSlots);
        var m = new FrontEnd(C, R, saves, Path.Combine(_dir, "settings.json"), () => 42);
        saves.Save(new CampaignState { Slot = 1, Name = "Owls", Seed = 7, Cars = C.StartingCars, Scrip = C.StartingScrip });
        m.ShowFortress(1);
        Choose(m, "CREW SIGIL");
        Assert.Equal("CREW SIGIL: NONE", m.Items[m.Selected].Label);
        m.Left();
        Assert.Equal("CREW SIGIL: ANCHOR", m.Items[m.Selected].Label);
        var s = saves.Load(1)!;
        Assert.Equal(12, s.Sigil);
        // The night's setup carries it to every machine, and every world is built with it.
        var setup = HostedAsApp.Campaign(s, Campaign.Offers(C, R, s)[0], enemies: false);
        Assert.Equal(12, setup.Sigil);
        var decoded = SessionSetup.Decode(setup.Encode());
        Assert.Equal(12, decoded.Sigil);
        var (world, _) = (decoded with { Route = null }).Build(Content);
        Assert.Equal(12, world.CrewSigil);
    }

    [Fact]
    public void EveryStencilTheLookListsIsDrawnAndEveryPaintNamed()
    {
        Assert.Equal(L.MaskPaints.Length, L.MaskPaintNames.Length);
        Assert.InRange(L.MaskPaints.Length, 8, MaskPaint.Max - 1);
        Assert.InRange(L.MaskStencils.Length, 10, MaskPaint.Max - 1);
        foreach (var key in L.MaskStencils)
        {
            Assert.Contains(key, MaskKit.Known);
            Assert.NotEmpty(MaskKit.Design(key, 3).Triangles);
            Assert.True(MaskKit.OnTheMask(null, key, 3).Vertices.Length > 0, key);
        }
        // Each stencil's design keeps to its square.
        foreach (var key in MaskKit.Known)
            Assert.All(MaskKit.Design(key, 8).Triangles, t =>
                Assert.All(new[] { t.A, t.B, t.C }, v => Assert.True(MathF.Abs(v.X) <= 1.05f && MathF.Abs(v.Y) <= 1.05f, $"{key} {v}")));
        // The number is the wearer's: each one a different design.
        Assert.NotEqual(MaskKit.Design("number", 1).Triangles.Count, MaskKit.Design("number", 8).Triangles.Count);
        Assert.InRange(L.MaskInk, 1, L.MaskPaints.Length);
        Assert.InRange(L.SigilInk, 1, L.MaskPaints.Length);
    }

    [Fact]
    public void ACrewmateWearsTheirPaintAndAPassengerWearsTheFacesPaint()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 600);
        var world = new World(train);
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Assert.Equal(MaskPaint.None, CrewActs.Crewmate(3, s, world, train.Frames).Paint);
        world.Paints[3] = new MaskPaint(5, 8, 2);
        Assert.Equal(new MaskPaint(5, 8, 2), CrewActs.Crewmate(3, s, world, train.Frames).Paint);
        var passenger = new Sim.Enemies.Passenger(1) { Extra = 3, Attached = 1, Local = new Double3(0, 1.2, 0) };
        Assert.Equal(new MaskPaint(5, 8, 2), GreyboxScene.AsCrewmate(passenger, train.Frames, world.Outfits, world.Paints)!.Value.Paint);
    }
}
