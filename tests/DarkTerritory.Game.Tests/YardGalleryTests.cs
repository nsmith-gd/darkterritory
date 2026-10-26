using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The yard photographer's gallery (docs/design/upgrades.md §4, "the campaign's end card shows every one"; ARCHITECTURE §8
/// note 1030): each night's print carried on the crew's logbook page, gathered back in night order with tonight's, drawn on
/// the night's end card and the campaign's end inside the frame at every TEXT SIZE, and laid out on a sheet in the night's folder.
/// </summary>
public class YardGalleryTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly MemoryTuning Tuning = DataFile.Load<MemoryTuning>(Path.Combine(Content, MemoryTuning.File));

    static string Temp(string what) => Path.Combine(Path.GetTempPath(), $"dt-{what}-{Guid.NewGuid():N}");

    /// <summary>A print of one shade (each night's its own, so they can be told apart), as the photographer keeps it.</summary>
    static Still Print(byte shade)
    {
        var rgba = new byte[YardPhotographer.Width * YardPhotographer.Height * 4];
        for (int i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (shade, shade, shade, 255);
        return new Still(YardPhotographer.Width, YardPhotographer.Height, rgba);
    }

    static string Keep(string dir, int night)
    {
        string file = Path.Combine(dir, $"night {night}", "00 yard photograph.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var p = Print((byte)(night * 20));
        PngWriter.Write(file, p.Rgba, p.Width, p.Height);
        return file;
    }

    static LogPage Page(int night, string yard = "") => new(night, "Frontier 7", "", RunEnd.Delivered, 0) { Yard = yard };

    [Fact]
    public void TheLogbooksPageCarriesTheYardsPrintAndAPageWrittenAgainKeepsIt()
    {
        var report = new RunReport(RunEnd.Delivered, 1500, 7, 5, 0, 0, 0, 100, 20, 46, 900, 0, 4);
        var page = Logbook.Page(report, 2, "Frontier 7", "", [], yard: "/kept/00 yard photograph.png");
        Assert.Equal("/kept/00 yard photograph.png", page.Yard);
        // The night written again as its commendations come in, the print not passed: the book keeps it.
        var book = Logbook.With(Logbook.With([], page), page with { Yard = "", Commendations = ["DAVE: Kept the Fire, from Priya"] });
        Assert.Equal("/kept/00 yard photograph.png", Assert.Single(book).Yard);
        // A page from before the gallery has none, and reads as none.
        Assert.Equal("", Logbook.Page(report, 1, "Local 4", "", []).Yard);
    }

    [Fact]
    public void TheGalleryIsTheLogbooksPrintsInNightOrderWithTonightsAndLeavesOutOnesGone()
    {
        string dir = Temp("gallery");
        try
        {
            // Night 2 had no photographer; night 4's print was deleted since.
            LogPage[] pages = [Page(3, Keep(dir, 3)), Page(1, Keep(dir, 1)), Page(2), Page(4, Path.Combine(dir, "gone.png"))];
            var gallery = new YardGallery();
            Assert.Equal([(1, pages[1].Yard), (3, pages[0].Yard), (4, pages[3].Yard)], YardGallery.Files(pages));
            var prints = gallery.Prints(pages, tonight: 5, Print(200));
            Assert.Equal([1, 3, 5], prints.Select(p => p.Night));
            Assert.Equal(20, prints[0].Print.Rgba[0]);
            Assert.Equal(60, prints[1].Print.Rgba[0]);
            Assert.Equal(200, prints[2].Print.Rgba[0]);
            // Tonight's as this machine took it, in place of a copy of tonight's already written to the book.
            Assert.Equal([1, 3], gallery.Prints(pages, tonight: 3, Print(250)).Select(p => p.Night));
            Assert.Equal(250, gallery.Prints(pages, tonight: 3, Print(250))[1].Print.Rgba[0]);
            // A first night: tonight's alone. A night with none: nothing.
            Assert.Equal([1], gallery.Prints([], tonight: 1, Print(9)).Select(p => p.Night));
            Assert.Empty(gallery.Prints([Page(1)]));
            // The card's thumbnails: the latest few, cut to size once and kept.
            var thumbs = gallery.Thumbs(prints, 96, 60, most: 2);
            Assert.Equal([3, 5], thumbs.Select(t => t.Night));
            Assert.All(thumbs, t => Assert.Equal((96, 60), (t.Print.Width, t.Print.Height)));
            Assert.Same(thumbs[0].Print, gallery.Thumbs(prints, 96, 60, most: 2)[0].Print);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TheLayoutFillsFromTheEndSoTonightsIsAtTheBottomRight()
    {
        Assert.Empty(YardGallery.Layout(0, 3, 10, 10, 2, 4).At);
        // One: alone, the block its size.
        var (one, w1, h1) = YardGallery.Layout(1, 3, 10, 6, 2, 4);
        Assert.Equal((0f, 0f), Assert.Single(one));
        Assert.Equal((10f, 10f), (w1, h1));
        // Five in rows of three: the first row short and set right, the second full; nothing overlaps, all in the block.
        var (five, w5, h5) = YardGallery.Layout(5, 3, 10, 6, 2, 4);
        Assert.Equal([(12f, 0f), (24f, 0f), (0f, 12f), (12f, 12f), (24f, 12f)], five);
        Assert.Equal((34f, 22f), (w5, h5));
        Assert.Equal(five.Count, five.Distinct().Count());
        Assert.All(five, a => Assert.True(a.X + 10 <= w5 && a.Y + 10 <= h5));
        Assert.Equal("LEAVING THE YARD", YardGallery.Heading(5, 5));
        Assert.Equal("LEAVING THE YARD: THE LAST 6 OF 9", YardGallery.Heading(6, 9));
    }

    [Fact]
    public void TheNightsTonightsPageCarriesThePrintAndTheEndCardHasTheCampaignsSoFar()
    {
        string dir = Temp("gallery-memory"), path = Temp("profile") + ".json";
        try
        {
            var profile = new PlayerProfile(path);
            var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 6), 6, enemies: false);
            s.World.Run!.MirrorReport(Staging.Report(s.World, RunEnd.Delivered));
            // Two nights before tonight's, kept in this player's profile with their prints.
            profile.Log("The Night Mail#63", "The Night Mail", Page(1, Keep(dir, 1)), false);
            profile.Log("The Night Mail#63", "The Night Mail", Page(2, Keep(dir, 2)), false);
            var memory = new CrewMemory(profile, Tuning, "The Night Mail", "The Night Mail#63", night: 3);
            // No photographer tonight: the gallery's the book's.
            Assert.Equal((2, 2), memory.EndGallery() is { } g0 ? (g0.Prints.Count, g0.Total) : default);
            string tonight = Keep(dir, 3);
            memory.Snapped(BookmarkAlbum.ReadPhoto(tonight)!, tonight);
            var page = memory.Remember(s, new DateTime(2026, 10, 25))!;
            Assert.Equal(tonight, page.Yard);
            var (prints, total) = memory.EndGallery()!.Value;
            Assert.Equal(3, total);
            Assert.Equal([1, 2, 3], prints.Select(p => p.Night));
            Assert.All(prints, p => Assert.Equal((Tuning.Gallery.CardWidth * 2, Tuning.Gallery.CardHeight * 2), (p.Print.Width, p.Print.Height)));
            // Read from disk by a later night: the page keeps where tonight's print is.
            Assert.Equal(tonight, new PlayerProfile(path).Load().Crews.Single().Pages.Single(p => p.Night == 3).Yard);
            // A crew with none: nothing on the card.
            Assert.Null(new CrewMemory(profile, Tuning, "Cold Feet", "Cold Feet#2", night: 1).EndGallery());
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void TheEndCardsGalleryDrawsInsideTheFrameAtEveryTextSize(int nights)
    {
        var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 6), 6, enemies: false);
        s.World.Run!.MirrorReport(Staging.Report(s.World, RunEnd.Delivered));
        var gallery = new YardGallery();
        var t = Tuning.Gallery;
        var prints = Enumerable.Range(1, nights).Select(n => new GalleryPrint(n, Print((byte)(n * 30)))).ToList();
        foreach (var size in Settings.TextSizes)
        {
            var (w, h) = new Settings { TextSize = size }.Canvas;
            var bare = new Overlay();
            Hud.Build(bare, w, h, s, pixels: 1080f / h);
            var o = new Overlay();
            Hud.Build(o, w, h, s, pixels: 1080f / h, gallery: (gallery.Thumbs(prints, t.CardWidth * 2, t.CardHeight * 2, t.CardMost), nights));
            Assert.True(o.Count > bare.Count, $"{nights} prints at {size:0%}: nothing more drawn");
            var outside = o.Vertices.Where(v => v.Position.X < -0.01 || v.Position.X > w + 0.01 || v.Position.Y < -0.01 || v.Position.Y > h + 0.01).ToList();
            Assert.True(outside.Count == 0, $"{nights} prints at {size:0%}: {outside.Count} vertices outside");
            // None (a first night with no photographer): the card as it was.
            var none = new Overlay();
            Hud.Build(none, w, h, s, pixels: 1080f / h, gallery: null);
            Assert.Equal(bare.Count, none.Count);
        }
    }

    [Fact]
    public void TheSheetInTheNightsFolderLaysOutEveryPrintFullSize()
    {
        string dir = Temp("gallery-sheet");
        try
        {
            var t = Tuning.Gallery;
            var prints = Enumerable.Range(1, 5).Select(n => new GalleryPrint(n, Print((byte)(n * 40)))).ToList();
            Assert.Null(YardGallery.Sheet([], "The Night Mail", t));
            var sheet = YardGallery.Sheet(prints, "The Night Mail", t)!;
            int columns = Math.Min(t.SheetColumns, prints.Count), rows = (prints.Count + columns - 1) / columns;
            Assert.True(sheet.Width >= columns * YardPhotographer.Width && sheet.Height >= rows * YardPhotographer.Height);
            string file = YardGallery.Save(dir, prints, "The Night Mail", t)!;
            Assert.Equal(YardGallery.SheetName, Path.GetFileName(file));
            var read = BookmarkAlbum.ReadPhoto(file)!;
            Assert.Equal((sheet.Width, sheet.Height), (read.Width, read.Height));
            // Each print's in it whole: night 5's shade at the middle of its place (row 2, column 1).
            int m = t.SheetMargin, line = BitmapFont.Default.LineHeight * 2;
            int x = m + YardPhotographer.Width / 2, y = BitmapFont.Default.LineHeight * 3 + m + m + (YardPhotographer.Height + line + m) + YardPhotographer.Height / 2;
            Assert.Equal(200, read.Rgba[(y * read.Width + x) * 4]);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TheCampaignsEndInTheMenuShowsItsPrintsInsideTheFrame()
    {
        string dir = Temp("gallery-menu");
        try
        {
            var ct = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
            var rt = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
            int Drawn(IReadOnlyList<LogPage> book, double size)
            {
                var saves = new SaveSlots(Path.Combine(dir, $"saves-{book.Count}-{size}"), ct.SaveSlots);
                saves.Save(Campaign.New(ct, 1, "The Night Shift", 7) with { Over = true, Runs = 7, Logbook = book });
                var menu = new FrontEnd(ct, rt, saves, Path.Combine(dir, "settings.json"));
                menu.ShowFortress(1);
                var (w, h) = new Settings { TextSize = size }.Canvas;
                var o = new Overlay();
                menu.Draw(o, w, h);
                var outside = o.Vertices.Where(v => v.Position.X < -0.01 || v.Position.X > w + 0.01 || v.Position.Y < -0.01 || v.Position.Y > h + 0.01).ToList();
                Assert.True(outside.Count == 0, $"{book.Count} prints at {size:0%}: {outside.Count} vertices outside");
                return o.Count;
            }
            LogPage[] book = [.. Enumerable.Range(1, 8).Select(n => Page(n, Keep(dir, n)))];
            Assert.True(Drawn(book, 1) > Drawn([], 1));
            // Pages with no print (no photographer): as with none.
            Assert.Equal(Drawn([], 1), Drawn([Page(1), Page(2)], 1));
            foreach (var size in Settings.TextSizes)
                Drawn(book, size);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
