using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Crew memory on a machine (GDD §9, §11, §3.1; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): the crew photo at the
/// gate staged from the replicated world, kept in the album; the logbook on its shelf, its pages in the player's profile
/// after the campaign's gone, drawn as a page; the board of lost crews read in town.
/// </summary>
public class CrewMemoryTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly MemoryTuning Tuning = DataFile.Load<MemoryTuning>(Path.Combine(Content, MemoryTuning.File));

    static PrototypeSession Night() => new(Content, Sim.LineGen.Routes.Generate(Content, "frontier:7", 6), 6, enemies: false);

    static string Temp(string what) => Path.Combine(Path.GetTempPath(), $"dt-{what}-{Guid.NewGuid():N}");

    [Fact]
    public void ADeliveredNightsPhotoStandsTheLivingInALineAndPutsTheDeadsCapsOnTheCrate()
    {
        var s = Night();
        var world = s.World;
        world.Names[1] = "Dave";
        world.Names[2] = "Priya";
        world.Names[3] = "Okafor";
        world.Names[4] = "Dunmore";
        world.Emotes.Add(new EmoteEvent(1, (uint)world.Tick, 3, Emote.Wave));
        var living = new PlayerState { Parent = PlayerState.World };
        // Given out of order, as the snapshot's remotes come: the photo's order is the ids'.
        (int, PlayerState)[] crew =
        [
            (4, living with { Death = DeathCause.Mauled }),
            (3, living),
            (1, living),
            (2, living with { Flags = PlayerFlags.Sooted }),
        ];
        var sitters = CrewPhoto.Sitters(world, crew);
        Assert.Equal(["Dave", "Priya", "Okafor", "Dunmore"], sitters.Select(c => c.Name));
        Assert.Equal([true, true, true, false], sitters.Select(c => c.Alive));
        Assert.True(sitters[1].Sooted);
        Assert.Equal(Emote.Wave, sitters[2].Emote);

        var engine = s.Train.Frames[0];
        var chalk = CrewPhoto.Chalk(CrewPhoto.RouteName("frontier:7"), 3, 1450);
        Assert.Equal(["FRONTIER 7", "NIGHT 3", "NET 1450"], chalk);
        var photo = CrewPhoto.Stage(engine, 1, sitters, chalk, Tuning.Photo);
        // The living stood, the dead not: their caps on the crate, in their own colours.
        Assert.Equal([1, 2, 3], photo.Crew.Select(c => (int)c.Id));
        Assert.Equal([4], photo.Props.Caps);
        Assert.Equal(Emote.Wave, photo.Crew[2].Emote);
        Assert.True(photo.Crew[1].Sooted);
        // In a line beside the engine, the same distance out, a shoulder's width apart, left to right in the lens.
        var right = engine.Right with { Y = 0 };
        foreach (var c in photo.Crew)
            Assert.Equal(Tuning.Photo.Out, Double3.Dot(c.Feet - engine.Origin, right.Normalized), 3);
        var lens = photo.Camera;
        double[] across = [.. photo.Crew.Select(c => Across(lens, c.Feet))];
        Assert.True(across[0] < across[1] && across[1] < across[2]);
        Assert.Equal(Tuning.Photo.Spacing, (photo.Crew[1].Feet - photo.Crew[0].Feet).Length, 3);
        // The crate past the line's right-hand end, the slate on the left in front of nobody; everyone facing the lens.
        Assert.True(Across(lens, photo.Props.Crate) > across[2]);
        Assert.True(Across(lens, photo.Props.Slate) < across[0]);
        foreach (var c in photo.Crew)
        {
            var toLens = (lens.Position - c.Feet) with { Y = 0 };
            Assert.Equal(1, Double3.Dot(new Double3(-Math.Sin(c.Yaw), 0, -Math.Cos(c.Yaw)), toLens.Normalized), 3);
        }
        // Faces in frame: every head in the lens's view, ahead of it.
        foreach (var c in photo.Crew)
            Assert.True(Double3.Dot(c.Feet + Double3.Up * 1.6 - lens.Position, Ahead(lens)) > 3);
        // Deterministic: the same world gives the same photo on every machine.
        var again = CrewPhoto.Stage(engine, 1, CrewPhoto.Sitters(world, crew.Reverse()), chalk, Tuning.Photo);
        Assert.Equal(photo.Crew, again.Crew);
        Assert.Equal(photo.Props.Crate, again.Props.Crate);
        Assert.Equal(photo.Camera.Position, again.Camera.Position);
        // The yard's open side is the one away from the town's square.
        Assert.Equal(1, CrewPhoto.YardSide(-1));
        Assert.Equal(-1, CrewPhoto.YardSide(1));
    }

    static Double3 Ahead(Camera lens) => new(lens.Forward.X, lens.Forward.Y, lens.Forward.Z);

    /// <summary>How far right of the lens's middle a point is (along its right, +X as the image has it).</summary>
    static double Across(Camera lens, Double3 p) => Double3.Dot(p - lens.Position, Double3.Cross(Ahead(lens), Double3.Up).Normalized);

    [Fact]
    public void ThePhotoIsKeptInTheAlbumWithItsSidecarAndReadsBack()
    {
        string dir = Temp("album");
        try
        {
            var album = new BookmarkAlbum(dir, new DateTime(2026, 10, 14, 21, 30, 0), "frontier-7");
            var rgba = new byte[8 * 4 * 4];
            for (int i = 0; i < rgba.Length; i++)
                rgba[i] = (byte)(i * 7);
            var still = new Still(8, 4, rgba);
            string sidecar = CrewPhoto.Sidecar(Staging.PhotoCrew(), ["FRONTIER 7", "NIGHT 3", "NET 1450"], "The Night Mail", "14 October 2026");
            string path = album.KeepPhoto(still, sidecar);
            Assert.Equal(Path.Combine(album.Night, BookmarkAlbum.PhotoName), path);
            Assert.Equal(path, album.Photo);
            var text = File.ReadAllText(Path.ChangeExtension(path, ".txt"));
            Assert.Contains("STANDING, LEFT TO RIGHT: Dave (wave), Priya (sooty), Okafor (point)", text, StringComparison.Ordinal);
            Assert.Contains("CAPS ON THE CRATE: Dunmore", text, StringComparison.Ordinal);
            Assert.Contains("FRONTIER 7 / NIGHT 3 / NET 1450", text, StringComparison.Ordinal);
            var back = BookmarkAlbum.ReadPhoto(path)!;
            Assert.Equal((8, 4), (back.Width, back.Height));
            Assert.Equal(still.Rgba, back.Rgba);
            Assert.Null(BookmarkAlbum.ReadPhoto(Path.Combine(dir, "gone.png")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EveryMachineKeepsTheCrewsLogbookInItsProfileAfterTheCampaignsOver()
    {
        string path = Temp("profile") + ".json";
        try
        {
            var s = Night();
            s.World.Names[1] = "Dave";
            s.World.Names[2] = "Priya";
            s.World.Commendations.Add((2, 1, 2));
            var profile = new PlayerProfile(path);
            var memory = new CrewMemory(profile, Tuning, "The Night Mail", "The Night Mail#63", night: 3);
            // Nothing till the report's in.
            Assert.Null(memory.Remember(s, new DateTime(2026, 10, 14)));
            Assert.Empty(memory.Pages());
            var report = new RunReport(RunEnd.DawnMissed, 1500, 7, 0, 5, 0, 0, 100, 20, 46, -900, 0, 2)
            {
                Lines = [new ReportLine(IncidentKind.Death, "Priya", "Taken at dawn by a Waker on the line at km 21. At the controls at dawn: Dave.")],
            };
            s.World.Run!.MirrorReport(report);
            s.World.DawnEndsCampaign = true;
            var page = memory.Remember(s, new DateTime(2026, 10, 14))!;
            Assert.Equal(3, page.Night);
            Assert.Equal("14 October 2026", page.Date);
            Assert.StartsWith("Frontier 7", page.Route, StringComparison.Ordinal);
            Assert.Equal(["DAVE: Kept the Fire, from Priya"], page.Commendations);
            Assert.StartsWith("PRIYA: Taken at dawn", Assert.Single(page.Losses), StringComparison.Ordinal);
            // Written again as the night's left: still one page.
            memory.Remember(s, new DateTime(2026, 10, 14));
            Assert.Single(memory.Pages());
            // Read from disk by another night (the host's save long gone): the crew, its page, and the board's card.
            var kept = new PlayerProfile(path).Load();
            var crew = Assert.Single(kept.Crews);
            Assert.True(crew.Over);
            Assert.Equal("The Night Mail", crew.Name);
            var (title, text) = Assert.Single(CrewMemory.LostCrews(kept));
            Assert.Equal("The Night Mail", title);
            Assert.StartsWith("Ran 3 nights. TAKEN AT DAWN. PRIYA:", text, StringComparison.Ordinal);
            // A quick night keeps nothing.
            Assert.Null(new CrewMemory(profile, Tuning, "", "", 0).Remember(s, DateTime.Now));
            // The setup carries the crew to every machine, and leaves it out of a quick night's.
            var setup = new SessionSetup(Route: "frontier:7") { Crew = "The Night Mail", CrewKey = "The Night Mail#63", Night = 3 };
            var sent = SessionSetup.Decode(setup.Encode());
            Assert.Equal(("The Night Mail", "The Night Mail#63", 3), (sent.Crew, sent.CrewKey, sent.Night));
            Assert.DoesNotContain("crewKey", new SessionSetup(Route: "frontier:7").Encode(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheHostsSaveKeepsTheLogbookThroughSaveSlots()
    {
        string dir = Temp("saves");
        try
        {
            var saves = new SaveSlots(dir);
            var state = new CampaignState { Slot = 2, Name = "Cold Feet", Logbook = Staging.LogPages("photo.png") };
            saves.Save(state);
            var back = saves.Load(2)!;
            Assert.Equal(state.Logbook.Select(p => (p.Night, p.Route, p.End, p.Net, p.Photo)), back.Logbook.Select(p => (p.Night, p.Route, p.End, p.Net, p.Photo)));
            Assert.Equal(state.Logbook[2].Losses, back.Logbook[2].Losses);
            Assert.Equal(state.Logbook[2].Commendations, back.Logbook[2].Commendations);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TheLogbookOnItsShelfOpensAtTheLastNightAndTurnsWithTheWalkingKeys()
    {
        var s = Night();
        var shelf = LogbookReader.Shelf(s.Train, Tuning.Logbook)!.Value;
        var bay = Sim.World.KitLocker(s.Train)!.Value.Bay;
        // Stood in the aisle at the kit locker, looking across at the shelf: its prompt.
        var feet = new Double3(bay.Front.X + bay.Facing * 0.75, bay.Front.Y, bay.Front.Z);
        var to = shelf.Local - (feet + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight);
        s.Player = s.Player with
        {
            Parent = shelf.Car,
            Surface = Surface.Deck,
            Position = feet,
            Yaw = Math.Atan2(-to.X, -to.Z),
            Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)),
            Velocity = default,
        };
        Assert.Equal(LogbookReader.Prompt, Hud.Prompt(s));
        // Looking away from it, no.
        s.Player = s.Player with { Yaw = s.Player.Yaw + Math.PI };
        Assert.NotEqual(LogbookReader.Prompt, Hud.Prompt(s));
        // The shelf's in its car, against the far wall from the lockers, off the floor.
        var room = s.Train.Frames[shelf.Car].Shape.Interior!.Value;
        Assert.InRange(shelf.Local.X, room.Min.X, room.Max.X);
        Assert.Equal(bay.Facing, Math.Sign(shelf.Local.X));
        Assert.InRange(shelf.Local.Y - room.Min.Y, 1, 1.6);

        var reader = new LogbookReader();
        reader.Use(pages: 3);
        Assert.True(reader.Open);
        Assert.Equal(2, reader.Page);
        Assert.False(reader.Turn(+1, 3));
        Assert.True(reader.Turn(-1, 3));
        Assert.True(reader.Turn(-1, 3));
        Assert.False(reader.Turn(-1, 3));
        Assert.Equal(0, reader.Page);
        reader.Use(pages: 3);
        Assert.False(reader.Open);
    }

    [Fact]
    public void ALogbookPageIsASheetWithEverythingOnItAndItsPhotoPastedIn()
    {
        var pages = Staging.LogPages();
        var photo = new Still(16, 9, Enumerable.Repeat((byte)200, 16 * 9 * 4).ToArray());
        var o = new Overlay();
        Hud.LogbookPage(o, 480, 270, "The Night Mail", pages, 2, photo);
        Assert.NotEmpty(o.Vertices);
        // All of it on the book (its board 5 px round the sheet), none off the screen.
        float w = Math.Min(480 - 24, 420), h = Math.Min(270 - 20, 250), x = MathF.Round((480 - w) / 2), y = MathF.Round((270 - h) / 2);
        foreach (var v in o.Vertices)
        {
            Assert.InRange(v.Position.X, x - 5.01f, x + w + 5.01f);
            Assert.InRange(v.Position.Y, y - 5.01f, y + h + 5.01f);
        }
        // With the photo there's more drawn than without; an empty book is less still, and says so.
        var bare = new Overlay();
        Hud.LogbookPage(bare, 480, 270, "The Night Mail", pages, 2, null);
        Assert.True(o.Count > bare.Count);
        var empty = new Overlay();
        Hud.LogbookPage(empty, 480, 270, "", [], 0, null);
        Assert.True(empty.Count < bare.Count);
        // A small screen keeps it on the screen too.
        var small = new Overlay();
        Hud.LogbookPage(small, 320, 180, "The Night Mail", pages, 2, photo);
        Assert.All(small.Vertices, v => Assert.InRange(v.Position.X, 0, 320));
    }

    [Fact]
    public void TheBoardOfLostCrewsIsReadACardAtATime()
    {
        var s = Night();
        var town = s.World.Town!;
        var (feet, look) = Staging.TownStand(town, "lostcrews", 0);
        var to = look - (feet + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight);
        s.Player = s.Player with
        {
            Parent = PlayerState.World,
            Surface = Surface.Ground,
            Position = feet,
            Yaw = Math.Atan2(-to.X, -to.Z),
            Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)),
            Velocity = default,
        };
        Assert.Equal("THE BOARD OF LOST CREWS   READ : [E]", Hud.Prompt(s));
        var target = Hud.TownTarget(s);
        Assert.True(target is { } t && TownTalk.IsLostCrews(town, t));
        // Nothing posted: the board's own words.
        var bare = new TownTalk();
        bare.Use(town, target, 0);
        Assert.Equal(TownGenerator.LostCrewsText, bare.Card(town, 0)!.Text);
        // Two crews: each card in turn, then done.
        var talk = new TownTalk { LostCrews = Staging.LostCrews() };
        talk.Use(town, target, 0);
        var first = talk.Card(town, 0)!;
        Assert.Equal("The Night Mail", first.Heading);
        Assert.Contains("TAKEN AT DAWN", first.Text, StringComparison.Ordinal);
        Assert.Contains("(1/2)", first.Footer, StringComparison.Ordinal);
        talk.Use(town, target, 1);
        Assert.Equal("Cold Feet", talk.Card(town, 1)!.Heading);
        talk.Use(town, target, 2);
        Assert.Null(talk.Card(town, 2));
    }
}
