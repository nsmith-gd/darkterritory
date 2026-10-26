using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The big cab's photo rail (GDD §10 "The back wall"; ARCHITECTURE §8 note 1056): each delivered night's crew photo pinned in
/// the next frame, kept in the save (the logbook's pages carry it), the frames run out by memory.json's rail, the frames
/// themselves on the back wall over the ways out, and a pinned print drawn in its own colours.
/// </summary>
public class PhotoRailTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly MemoryTuning Tuning = DataFile.Load<MemoryTuning>(Path.Combine(Content, MemoryTuning.File));

    static string Temp(string what) => Path.Combine(Path.GetTempPath(), $"dt-{what}-{Guid.NewGuid():N}");

    static TrainOnLine Train()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200);
    }

    /// <summary>A crew photo of one colour (each night's its own), as the album keeps it.</summary>
    static Still Photo(byte r, byte g, byte b)
    {
        int w = Tuning.Photo.Width, h = Tuning.Photo.Height;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, 255);
        return new Still(w, h, rgba);
    }

    static string Keep(string dir, int night)
    {
        string file = Path.Combine(dir, $"night {night}", BookmarkAlbum.PhotoName);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var p = Photo((byte)(night * 20), 0, 0);
        PngWriter.Write(file, p.Rgba, p.Width, p.Height);
        return file;
    }

    [Fact]
    public void EachNightsPhotoGoesInTheNextFrameAndTheFramesRunOutByTheRailsRule()
    {
        Assert.Equal([1, 3, 4], PhotoRail.Pinned([4, 1, 3], 8));
        var eleven = Enumerable.Range(1, 11).ToList();
        // Latest: the oldest come down as new ones go up. First: the first eight stay.
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11], PhotoRail.Pinned(eleven, 8, RailTuning.Latest));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], PhotoRail.Pinned(eleven, 8, RailTuning.First));
        Assert.Equal(RailTuning.Latest, Tuning.Rail.Keep);
        // Only the nights with a photo (a lost night has none).
        LogPage[] book = [new(1, "Local 4", "", RunEnd.Delivered, 0) { Photo = "/a.png" }, new(2, "Frontier 3", "", RunEnd.Derailed, 0), new(3, "Frontier 7", "", RunEnd.Delivered, 0) { Photo = "/c.png" }];
        Assert.Equal([1, 3], PhotoRail.Photographed(book));
    }

    [Fact]
    public void TheRailIsKeptInTheSaveAndInEveryPlayersProfile()
    {
        string dir = Temp("rail");
        try
        {
            var ct = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
            // The host's save: three nights, the second lost (no photo), and the rail read back the same after a load.
            LogPage[] book =
            [
                new(1, "Local 4", "", RunEnd.Delivered, 860) { Photo = Keep(dir, 1) },
                new(2, "Frontier 3", "", RunEnd.Derailed, -420),
                new(3, "Frontier 7", "", RunEnd.Delivered, 1450) { Photo = Keep(dir, 3) },
            ];
            var saves = new SaveSlots(Path.Combine(dir, "saves"), ct.SaveSlots);
            saves.Save(Campaign.New(ct, 1, "The Night Mail", 63) with { Logbook = book });
            var loaded = saves.Load(1)!;
            Assert.Equal([book[0].Photo, "", book[2].Photo], loaded.Logbook.Select(p => p.Photo));
            Assert.Equal([1, 3], PhotoRail.Pinned(PhotoRail.Photographed(loaded.Logbook), TrainKit.PhotoFrames));

            // A player's profile: the nights written, tonight's photo taken, the rail in night order with tonight's last.
            string path = Path.Combine(dir, "profile.json");
            var profile = new PlayerProfile(path);
            profile.Log("The Night Mail#63", "The Night Mail", book[0], false);
            profile.Log("The Night Mail#63", "The Night Mail", book[1], false);
            var s = new PrototypeSession(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 6), 6, enemies: false);
            s.World.Run!.MirrorReport(Staging.Report(s.World, RunEnd.Delivered));
            var memory = new CrewMemory(profile, Tuning, "The Night Mail", "The Night Mail#63", night: 3);
            Assert.Equal([20], memory.RailPhotos().Select(p => (int)p.Rgba[0]));
            memory.Remember(s, new DateTime(2026, 10, 26));
            var album = new BookmarkAlbum(Path.Combine(dir, "album"), new DateTime(2026, 10, 26, 21, 30, 0), "frontier-7");
            var taken = Photo(200, 0, 0);
            memory.Took(s, taken.Rgba, taken.Width, taken.Height, album, 0, new DateTime(2026, 10, 26));
            Assert.Equal([20, 200], memory.RailPhotos().Select(p => (int)p.Rgba[0]));
            // Another night, read from the profile on disk: the same rail.
            var later = new CrewMemory(new PlayerProfile(path), Tuning, "The Night Mail", "The Night Mail#63", night: 4);
            Assert.Equal([20, 200], later.RailPhotos().Select(p => (int)p.Rgba[0]));
            // A photo gone from the disk leaves its frame to the next.
            File.Delete(book[0].Photo);
            Assert.Equal([200], new CrewMemory(new PlayerProfile(path), Tuning, "The Night Mail", "The Night Mail#63", night: 4).RailPhotos().Select(p => (int)p.Rgba[0]));
            // A quick night pins nothing.
            Assert.Empty(new CrewMemory(profile, Tuning, "", "", 0).RailPhotos());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TheFramesHangOnTheBackWallOverTheWaysOutClearOfEachOtherAndTheClock()
    {
        var shape = Train().Frames[0].Shape;
        var cab = shape.Cab!.Value;
        float door = (float)new DoorwayTuning().Height, deck = (float)cab.Min.Y + 0.1f;
        var frames = Enumerable.Range(0, TrainKit.PhotoFrames).Select(i => TrainKit.PhotoFrame(shape, i)).ToList();
        var half = TrainKit.PhotoFrameSize / 2;
        foreach (var f in frames)
        {
            Assert.True(cab.Contains(new Double3(f.X, f.Y, f.Z)), $"{f} is out of the cab");
            Assert.True(f.Z > TrainKit.GalleryFloor(shape)!.Value.Max.Z, $"{f} is up on the gallery");
            // Over the doorways' heads and under the roof.
            Assert.True(f.Y - half.Y > deck + door, $"{f} hangs into a doorway");
            Assert.True(f.Y + half.Y < cab.Max.Y, $"{f} is in the roof");
            // Clear of the cuckoo clock's house in the middle.
            Assert.True(MathF.Abs(f.X) - half.X > 0.23f, $"{f} is over the clock");
        }
        for (int i = 0; i < frames.Count; i++)
            for (int j = i + 1; j < frames.Count; j++)
                Assert.True(MathF.Abs(frames[i].X - frames[j].X) >= TrainKit.PhotoFrameSize.X || MathF.Abs(frames[i].Y - frames[j].Y) >= TrainKit.PhotoFrameSize.Y,
                    $"frames {i} and {j} overlap");
        // Pinned in reading order, facing the wall (your left is +X): along the top row, then the bottom.
        Assert.True(frames[0].X > frames[1].X && frames[1].X > frames[2].X && frames[2].X > frames[3].X);
        Assert.True(frames[0].Y > frames[4].Y);
        Assert.Equal(frames[0].X, frames[4].X);
        // The print fits its frame, the crew photo's 16:9.
        Assert.True(TrainKit.PhotoPrintSize.X < TrainKit.PhotoFrameSize.X && TrainKit.PhotoPrintSize.Y < TrainKit.PhotoFrameSize.Y);
        Assert.Equal(16 / 9f, TrainKit.PhotoPrintSize.X / TrainKit.PhotoPrintSize.Y, 2);
    }

    [Fact]
    public void APinnedPhotoIsDrawnInItsOwnColoursMatteAndNothingWithoutOne()
    {
        var train = Train();
        var eye = train.Frames[0].ToWorld(new Double3(0, 3, 0));
        int Drawn(IReadOnlyList<Still>? rail, out List<Vertex> vertices)
        {
            var mesh = new MeshBuilder();
            new GreyboxScene { Rail = rail, RailCells = Tuning.Rail.Cells, RailGlow = Tuning.Rail.Glow }.Build(mesh, train, eye);
            vertices = [.. mesh.Vertices];
            return vertices.Count;
        }
        int bare = Drawn(null, out _);
        Assert.Equal(bare, Drawn([], out _));
        // A green photo and a blue one: their colours on the wall, matte (no shine, a little glow to read by), more drawn for each.
        int one = Drawn([Photo(0, 255, 0)], out _);
        int two = Drawn([Photo(0, 255, 0), Photo(0, 0, 255)], out var drawn);
        Assert.True(one > bare && two > one);
        var green = drawn.Where(v => v.Color == new Vector3(0, 1, 0)).ToList();
        var blue = drawn.Where(v => v.Color == new Vector3(0, 0, 1)).ToList();
        Assert.NotEmpty(green);
        Assert.NotEmpty(blue);
        Assert.All(green.Concat(blue), v => Assert.True(v.Shine == 0 && v.Wear == 0 && v.Emissive == Tuning.Rail.Glow));
        Assert.InRange(Tuning.Rail.Glow, 0, 0.3f);
        // A photo of one colour is one run a row: a few quads, not a cell each.
        int rows = (int)MathF.Round(Tuning.Rail.Cells * TrainKit.PhotoPrintSize.Y / TrainKit.PhotoPrintSize.X);
        Assert.Equal(rows * 6, green.Count);
    }
}
