using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

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
        var (mark, camera, peak) = Assert.Single(due);
        Assert.Null(peak);
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

    [Fact]
    public void EachPlayersPeakIsWhereTheirShotShowsItAtTheirShotsCamera()
    {
        // E.5: the moment of the cut that shows each player's peak (WreckFilm.Peaks), from their shot's camera.
        var (_, film) = FilmPlaybackTests.Shot();
        foreach (var p in film.Start.Players)
        {
            var peak = DerailSequence.PeakOf(film, p.Id);
            Assert.NotNull(peak);
            Assert.Equal((ShotKind.Player, p.Id), (peak.Shot.Kind, peak.Shot.Subject));
            Assert.InRange(peak.Into, 0, peak.Shot.Real);
            Assert.Equal(Math.Clamp(film.Peaks[p.Id].At, peak.Shot.From, peak.Shot.To), peak.Recorded, 4);
            Assert.True((peak.Camera.Position - DerailSequence.FilmCamera(peak.Shot, peak.Into).Position).Length < 1e-9);
        }
        Assert.Null(DerailSequence.PeakOf(film, 7));
    }

    [Fact]
    public void ADerailmentsStillIsTheCrewMembersPeakInTheFilmOnceItsShot()
    {
        var s = Night();
        var frames = s.InterpolatedFrames(1);
        var (_, film) = FilmPlaybackTests.Shot();
        // Crew 0 is in the film; crew 3 isn't (the film's world had two aboard).
        s.World.Bookmarks.Mirror(new Bookmark(1, BookmarkKind.Derail, 11, 0, 0, 0, new Double3(0, 2.5, 0), new Double3(0, 0, -1)));
        s.World.Bookmarks.Mirror(new Bookmark(2, BookmarkKind.Derail, 11, 3, 3, 1, new Double3(0, 2.5, 0), new Double3(0, 0, -1)));
        var stills = new BookmarkStills();
        var blank = new byte[64 * 36 * 4];
        // No film yet: the first-person beat's stand-in, the wreck's bookmark beat after it was heard of.
        Assert.Empty(stills.Due(s, frames, now: 100));
        var standIns = stills.Due(s, frames, now: 100 + s.World.WreckTuning.BookmarkSeconds);
        Assert.Equal([1, 2], standIns.Select(d => d.Mark.Id));
        Assert.All(standIns, d => Assert.Null(d.Peak));
        foreach (var d in standIns)
            stills.Keep(d.Mark, blank, 64, 36);
        Assert.Empty(stills.Due(s, frames, now: 110));
        // The film in: crew 0's is due again, now their peak, from their shot's camera; crew 3 keeps the stand-in.
        var due = Assert.Single(stills.Due(s, frames, now: 110, film));
        Assert.Equal(1, due.Mark.Id);
        var peak = Assert.IsType<FilmPeak>(due.Peak);
        Assert.Equal(DerailSequence.PeakOf(film, 0)!.Recorded, peak.Recorded);
        Assert.True((due.Camera.Position - peak.Camera.Position).Length < 1e-9);
        // Drawn as the film draws it: its cars, the ragdolls, nobody else.
        var scene = new GreyboxScene { Crew = s.Crew(frames, 1) };
        var at = DerailSequence.Stage(scene, peak, frames);
        Assert.Empty(scene.Crew!);
        Assert.Equal(film.Start.Players.Count, scene.Bodies!.Count);
        Assert.True(scene.Derailed);
        Assert.True((at[film.Start.Cars[0].Vehicle].Origin - DerailSequence.FilmFrames(film, peak.Recorded, frames)[film.Start.Cars[0].Vehicle].Origin).Length < 1e-9);
        stills.Keep(due.Mark, blank, 64, 36, peak: true);
        Assert.True(stills.IsPeak(1));
        Assert.False(stills.IsPeak(2));
        Assert.Empty(stills.Due(s, frames, now: 111, film));
    }

    [Fact]
    public void TheNightsStillsAreKeptOnDiskNamedByNightAndTimeAndRewrittenWhenThePeakComes()
    {
        var s = Night();
        s.World.Names[0] = "Priya";
        s.World.Names[1] = "Dave";
        s.World.Names[2] = "Sam";
        var marks = new[]
        {
            new Bookmark(1, BookmarkKind.Grab, 1104, 0, 1, 0, default, new Double3(0, 0, -1)),
            new Bookmark(2, BookmarkKind.Derail, 3725, 1, 1, 0, default, new Double3(0, 0, -1)),
            new Bookmark(3, BookmarkKind.Manual, 200, -1, 1, 0, default, new Double3(0, 0, -1), Taker: 2, Name: "Dave"),
            new Bookmark(4, BookmarkKind.Punish, 300, 0, 1, 0, default, new Double3(0, 0, -1)),
        };
        var report = new RunReport(RunEnd.Derailed, 3725, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3)
        {
            Lines = [new ReportLine(IncidentKind.Death, "Priya", "Grabbed by the Dragger on the roof of car 3 at km 4.") { Seconds = 1104, Marks = [1, 4] }],
            Bookmarks = marks,
        };
        Still Flat(byte v) => new(4, 2, Enumerable.Repeat(v, 4 * 2 * 4).ToArray());
        var stills = new Dictionary<int, Still> { [1] = Flat(10), [2] = Flat(20), [3] = Flat(30) };
        string dir = Path.Combine(Path.GetTempPath(), $"dt-album-{Guid.NewGuid():N}");
        try
        {
            var album = new BookmarkAlbum(dir, new DateTime(2026, 10, 5, 21, 30, 0), "frontier:7");
            Assert.Equal(Path.Combine(dir, "2026-10-05 2130 frontier-7"), album.Night);
            var first = album.Save(report, stills, s.World).Select(Path.GetFileName).ToList();
            // By number, time in the run, kind and whom; the punish has no still here, so no file.
            Assert.Equal(["01 18m24s grab priya.png", "02 1h02m05s derailment dave.png", "03 3m20s bookmark dave.png"], first);
            Assert.Equal(3, album.Kept);
            Assert.All(first, f => Assert.True(File.Exists(Path.Combine(album.Night, f!))));
            string index = File.ReadAllText(Path.Combine(album.Night, "night.txt"));
            Assert.Contains("PRIYA: Grabbed by the Dragger", index);
            Assert.Contains("[01 18m24s grab priya.png]", index);
            Assert.Contains("Sam, following Dave: [03 3m20s bookmark dave.png]", index);
            // Nothing new: nothing written. The film's peak replacing the derailment's stand-in: that one again.
            Assert.Empty(album.Save(report, stills, s.World));
            stills[2] = Flat(99);
            Assert.Equal(["02 1h02m05s derailment dave.png"], album.Save(report, stills, s.World).Select(Path.GetFileName));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
