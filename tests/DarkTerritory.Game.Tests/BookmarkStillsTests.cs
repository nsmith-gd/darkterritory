using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. D.12 on this machine (note 176): when each bookmark's still is due and from what camera, the still kept
/// small, and the report drawing it beside its line.
/// </summary>
public class BookmarkStillsTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static PrototypeSession Night() => new(Content, DarkTerritory.Sim.Route.RouteGenerator.Generate(
        DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.RouteTier.Frontier, 7), 4, enemies: false);

    [Fact]
    public void AStillIsTheFrameCroppedToItsShapeAndAveragedDown()
    {
        // 4:3, with a 16:9 middle that's red and the bands above and below it blue.
        int w = 64, h = 48;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool middle = y >= 6 && y < 42;
                rgba[i] = (byte)(middle ? 200 : 0);
                rgba[i + 2] = (byte)(middle ? 0 : 200);
                rgba[i + 3] = 255;
            }
        var still = BookmarkStills.Shrink(rgba, w, h, 32, 18);
        Assert.Equal((32, 18), (still.Width, still.Height));
        Assert.All(Enumerable.Range(0, 32 * 18), p => Assert.Equal((200, 0), (still.Rgba[p * 4], still.Rgba[p * 4 + 2])));
    }

    [Fact]
    public void APictureOnTheOverlayIsARunAColourNotAQuadACell()
    {
        var o = new Overlay();
        var flat = Enumerable.Repeat((byte)90, 16 * 9 * 4).ToArray();
        o.Image(0, 0, 32, 18, flat, 16, 9, 64, 36);
        // One colour across: one quad a row.
        Assert.Equal(36 * 6, o.Count);
        Assert.All(o.Vertices, v => Assert.Equal(new Vector4(90 / 255f, 90 / 255f, 90 / 255f, 1), v.Colour, new ToleranceComparer(0.02f)));
    }

    sealed class ToleranceComparer(float tolerance) : IEqualityComparer<Vector4>
    {
        public bool Equals(Vector4 a, Vector4 b) => Vector4.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector4 v) => 0;
    }

    [Fact]
    public void AGrabsStillIsDueAtOnceFromItsEyeInItsCarADerailmentsAMomentLater()
    {
        var s = Night();
        var frames = s.InterpolatedFrames(1);
        var eye = new Double3(0.2, 5.65, 1.5);
        s.World.Bookmarks.Mirror(new Bookmark(1, BookmarkKind.Grab, 10, 2, 3, 2, eye, new Double3(0, 0, -1)));
        s.World.Bookmarks.Mirror(new Bookmark(2, BookmarkKind.Derail, 11, 1, 1, 0, new Double3(0, 2.5, 0), new Double3(0, 0, -1)));
        var stills = new BookmarkStills();
        var due = stills.Due(s, frames, now: 100);
        var (mark, camera) = Assert.Single(due);
        Assert.Equal(1, mark.Id);
        Assert.True((camera.Position - frames[2].ToWorld(eye)).Length < 1e-9);
        Assert.True(Vector3.Dot(camera.Forward, frames[2].DirToWorld(new Double3(0, 0, -1)).RelativeTo(Double3.Zero)) > 0.999f);
        stills.Keep(mark, new byte[64 * 36 * 4], 64, 36);
        // The derailment's: from when this machine heard of it, the wreck's bookmark beat later.
        Assert.Empty(stills.Due(s, frames, now: 100 + s.World.WreckTuning.BookmarkSeconds - 0.1));
        Assert.Equal(2, Assert.Single(stills.Due(s, frames, now: 100 + s.World.WreckTuning.BookmarkSeconds)).Mark.Id);
    }

    [Fact]
    public void TheReportDrawsEachStillBesideItsLineAndTheDeadsOwnInTheirRow()
    {
        var marks = new[]
        {
            new Bookmark(1, BookmarkKind.Grab, 100, 0, 1, 0, default, new Double3(0, 0, -1)),
            new Bookmark(2, BookmarkKind.Manual, 200, -1, 1, 0, default, new Double3(0, 0, -1), Taker: 2, Name: "Dave"),
        };
        var report = new RunReport(RunEnd.CrewLost, 300, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3)
        {
            Lines = [new ReportLine(IncidentKind.Death, "Priya", "Swallowed by the Car Hugger on the roof of car 3 at km 4.") { Marks = [1] }],
            Bookmarks = marks,
        };
        var bare = new Overlay();
        Hud.IncidentReport(bare, 480, 270, 100, report, bare.Font.LineHeight);
        // Stills of noise: every cell its own colour, so each costs a quad a cell.
        var rng = new Random(1);
        var noise = new byte[BookmarkStills.Width * BookmarkStills.Height * 4];
        rng.NextBytes(noise);
        var stills = new Dictionary<int, Still> { [1] = new(BookmarkStills.Width, BookmarkStills.Height, noise), [2] = new(BookmarkStills.Width, BookmarkStills.Height, noise) };
        var drawn = new Overlay();
        Hud.IncidentReport(drawn, 480, 270, 100, report, drawn.Font.LineHeight, stills);
        int cells = Hud.ThumbWidth * 2 * Hud.ThumbHeight * 2 + Hud.ManualWidth * 2 * Hud.ManualHeight * 2;
        Assert.InRange(drawn.Count / 6 - bare.Count / 6, cells * 0.8, cells + 40);
    }
}
