using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Music;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>GDD v1.4 App. E.6 "Rotation" and E.11 "Rotation": the derailment's shuffle bag.</summary>
public class MusicRotationTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly MusicTuning Rules = DataFile.Load<WreckTuning>(Path.Combine(Content, WreckTuning.File)).Music;

    static MusicTrack Track(string id, MusicMood mood) =>
        new(id, id + ".wav", id, "", 1850, "", "", MusicManifest.Licence, new MusicEvidence("", ""), mood, 0, 5, 25, -16, 0);

    [Fact]
    public void AThousandDerailsOnOneSaveNeverRepeatInABagNorPlayATrackTwiceRunning()
    {
        // E.11: "Across 1,000 simulated derails on one save, no track repeats inside a bag and no track plays twice running."
        // The real manifest, and the bag carried from derail to derail through the save as text, as a campaign keeps it.
        var tracks = MusicManifest.Load(Content).Tracks;
        Assert.True(tracks.Length >= 4, "E.6: at least 4 tracks for the December demo");
        var save = new CampaignState { Slot = 1, Name = "test" };
        var speeds = new Pcg32(7);
        var played = new List<string>();
        for (int derail = 0; derail < 1000; derail++)
        {
            var rotation = new MusicRotation(tracks, Rules, save.Music);
            var drawn = rotation.Draw(speeds.Range(2, 30), (ulong)derail * 7919 + 13);
            Assert.NotNull(drawn);
            played.Add(drawn.Id);
            save = JsonSerializer.Deserialize<CampaignState>(JsonSerializer.Serialize(save with { Music = rotation.Bag }, DataFile.Options), DataFile.Options)!;
        }
        // Each bag is a full pass: every track once, in some order.
        for (int bag = 0; bag + tracks.Length <= played.Count; bag += tracks.Length)
            Assert.Equal(tracks.Select(t => t.Id).Order(), played.Skip(bag).Take(tracks.Length).Order());
        for (int i = 1; i < played.Count; i++)
            Assert.NotEqual(played[i - 1], played[i]);
        // And it does shuffle: more than one order of the bag turns up.
        Assert.True(Enumerable.Range(0, played.Count / tracks.Length).Select(b => string.Join(",", played.Skip(b * tracks.Length).Take(tracks.Length))).Distinct().Count() > 5);
    }

    [Fact]
    public void TheFirstDrawFromARefilledBagIsNeverTheLastTrackPlayed()
    {
        MusicTrack[] tracks = [Track("a", MusicMood.Lament), Track("b", MusicMood.Gallop), Track("c", MusicMood.Doom)];
        for (ulong seed = 0; seed < 200; seed++)
        {
            // An empty bag whose last track was the one the speed favours most: it still can't come first.
            var rotation = new MusicRotation(tracks, Rules, new MusicBag { Left = [], Last = "a" });
            Assert.NotEqual("a", rotation.Draw(2, seed)!.Id);
        }
    }

    [Fact]
    public void SpeedWeightsTheDrawWithinTheBag()
    {
        // A fresh bag, many seeds: slow derails reach for the Lament first, fast ones for Gallop and Doom; Swagger fits any.
        MusicTrack[] tracks = [Track("lament", MusicMood.Lament), Track("gallop", MusicMood.Gallop), Track("doom", MusicMood.Doom), Track("swagger", MusicMood.Swagger)];
        Dictionary<string, int> First(double speed)
        {
            var counts = tracks.ToDictionary(t => t.Id, _ => 0);
            for (ulong seed = 0; seed < 4000; seed++)
                counts[new MusicRotation(tracks, Rules).Draw(speed, seed)!.Id]++;
            return counts;
        }
        var slow = First(Rules.LamentBelow - 2);
        var fast = First(Rules.GallopAbove + 4);
        Assert.True(slow["lament"] > slow["gallop"] * 2 && slow["lament"] > slow["doom"] * 2, string.Join(", ", slow));
        Assert.True(fast["gallop"] > fast["lament"] * 2 && fast["doom"] > fast["lament"] * 2, string.Join(", ", fast));
        Assert.True(slow["swagger"] > slow["gallop"] && fast["swagger"] > fast["lament"]);
    }

    [Fact]
    public void TheDrawIsTheSameFromTheSameBagAndSeed()
    {
        var tracks = MusicManifest.Load(Content).Tracks;
        var bag = new MusicBag { Left = [.. tracks.Skip(1).Select(t => t.Id).Reverse()], Last = tracks[0].Id };
        var a = new MusicRotation(tracks, Rules, bag);
        var b = new MusicRotation(tracks, Rules, bag);
        for (ulong seed = 1; seed < 20; seed++)
            Assert.Equal(a.Draw(seed, seed)?.Id, b.Draw(seed, seed)?.Id);
        Assert.Equal(a.Bag.Left, b.Bag.Left);
    }

    [Fact]
    public void ADerailDrawsOnTheHostAndTheTrackGoesOutWithTheWorld()
    {
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 800);
        var host = new World(Train()) { Music = MusicRotation.Load(Content, Rules, null) };
        host.Train.Dynamics.Velocity = 5;
        host.Derail("test");
        Assert.NotEqual(0u, host.DerailMusic);
        var drawn = MusicManifest.Load(Content).ByKey(host.DerailMusic);
        Assert.NotNull(drawn);
        Assert.Equal(drawn.Id, host.Music.Bag.Last);
        var controls = new TrainControls();
        var client = new World(Train());
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        Assert.Equal(host.DerailMusic, client.DerailMusic);
        // A second derail of the same world draws nothing more: it's off the rails once.
        var bag = host.Music.Bag;
        host.Derail("again");
        Assert.Same(bag, host.Music.Bag);
    }
}
