using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The town types underfoot and their water, heard (queue #526, note 799; T1's #1003, note 692): a deck's planks, a way's
/// strip and a terrace's top sound as the art draws them; feet under the town's water wade; and the water laps where it
/// is, near the ear. On a dykeland (Fenfolk: its stages on piles, its marsh waded) and a quarry (Pitborn: its benches).
/// </summary>
public class TownFormSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static World Night(string form)
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trains, 6, 1)), route.Build(), gate - 8);
        var world = new World(train);
        world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, gate, authority: true);
        world.EnableTown(Towns with { Tuning = Towns.Tuning with { Population = [140, 140], Forms = Towns.Tuning.Forms with { Force = form } } },
            route, gate, []);
        Assert.Equal(form, world.Town!.Plan.Form);
        return world;
    }

    /// <summary>The first cell inside the town's wall, off the line's bed, that's like this; and where it is in the world (on
    /// what's stood on there).</summary>
    static (double S, double D, Double3 At) Find(Town town, Func<TownGround, double, double, bool> like)
    {
        var g = town.Ground;
        var o = town.Anchor();
        var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var right = Double3.Cross(along, Double3.Up).Normalized;
        for (int i = 0; i < g.Ns; i++)
            for (int j = 0; j < g.Nd; j++)
            {
                double s = g.S0 + i + 0.5, d = g.D0 + j + 0.5;
                if (Math.Abs(d) > 6 && g.Inside(s, d) && like(g, s, d))
                    return (s, d, o.Position + along * s + right * d + Double3.Up * g.Level(s, d));
            }
        throw new InvalidOperationException("no such cell");
    }

    static bool OnAWay(Town town, double s, double d) =>
        town.Plan.Ways.Any(w => Enumerable.Range(1, Math.Max(0, w.Points.Count - 1))
            .Any(i => TownDistrict.SegDist(s, d, w.Points[i - 1], w.Points[i]) <= w.Width / 2 + 1));

    [Fact]
    public void AFenTownsStagesAreBoardsItsMarshIsWadedAndItsWaterLapsNearTheEar()
    {
        var world = Night("dykeland");
        var town = world.Town!;
        double hint = 0;
        var deck = Find(town, (g, s, d) => g.Deck(s, d));
        Assert.Equal("wood", Footing.Ground(world, deck.At, ref hint));
        var marsh = Find(town, (g, s, d) => !g.Deck(s, d) && g.Water(s, d) is { } w && w - g.Bed(s, d) is > 0.2 and <= TownGround.Wade
            && !OnAWay(town, s, d));
        Assert.Equal("water", Footing.Ground(world, marsh.At, ref hint));
        // Stood on the deck over it, it's the boards still: the water's under the planks, not over the feet.
        Assert.Equal("wood", Footing.Ground(world, deck.At + Double3.Up * 0.02, ref hint));

        // The marsh laps where it is, the nearest of it to the ear, at its surface.
        var audio = new GameAudio(Content);
        audio.Bank.Add("world-water.lake", new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Loop: true, MaxInstances: 4, MaxDistance: 600));
        var ear = marsh.At + Double3.Up * 1.6;
        for (int i = 0; i < 10; i++)
        {
            audio.Update(world, new TrainControls { Reverser = 1 }, Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
            audio.Mixer.Render(new float[Audio.Block * 2]);
        }
        var lap = audio.Mixer.Voices.Single(v => v.Name == "world-water.lake" && !v.Finished);
        Assert.True((lap.Position - marsh.At).Length < 2, $"the lap {(lap.Position - marsh.At).Length:0.0} m off the marsh under the ear");
    }

    [Fact]
    public void AQuarryTownsBenchesAreItsGraniteAndItsWaysTheirBeatenStrips()
    {
        var world = Night("quarry");
        var town = world.Town!;
        double hint = 0;
        // A bench below the yard (the quarry's floor), off the ways: the granite the art lays its tops with.
        var bench = Find(town, (g, s, d) => g.Bed(s, d) < -1 && g.Water(s, d) is null && !g.Deck(s, d) && !OnAWay(town, s, d));
        Assert.Equal("granite_lichen", WorldArt.FormGround(town, bench.At, world.Train.Line));
        Assert.Equal("cobbles", Footing.Ground(world, bench.At, ref hint));
        // A way on the ground: the beaten strip over it.
        var way = town.Plan.Ways.First(w => !w.Deck && w.Points.Count > 1 && Math.Abs(w.Points[0].D) > 8 && town.Ground.Inside(w.Points[0].S, w.Points[0].D));
        var o = town.Anchor();
        var along = new Double3(o.Tangent.X, 0, o.Tangent.Z).Normalized;
        var at = o.Position + along * way.Points[0].S + Double3.Cross(along, Double3.Up).Normalized * way.Points[0].D;
        Assert.Equal("ballast", WorldArt.FormGround(town, at, world.Train.Line));
    }
}
