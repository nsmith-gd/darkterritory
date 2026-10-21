using Ballast;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Player-relevant selection and placement (queue #609, note 895; the brief §3 "Encounter selection" 6, §5): the next threat
/// weighs down in a zone that already has one, and along a long train what comes is put away from the threats already about
/// it and not on top of a crewmate; with nothing about, exactly where it was put before.
/// </summary>
public class EncounterPlacementTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlacementTuning Place = E.Director.Encounters.Placement;

    /// <summary>Twelve cars' middles, 15 m apart, front first.</summary>
    static readonly double[] Cars = [.. Enumerable.Range(0, 12).Select(i => 2_000 - 7.5 - 15.0 * i)];

    /// <summary>The pick at each of <paramref name="n"/> evenly spread draws, as the rules draw them.</summary>
    static int[] Picks(double[] threats, double[] crew, int n = 1200, PlacementTuning? t = null) =>
        [.. Enumerable.Range(0, n).Select(i => EncounterPlacement.Pick(Cars, threats, crew, (i + 0.5) / n * (Cars.Length - 1e-9), t ?? Place))];

    [Fact]
    public void TheTuningIsTheGamesAndOn()
    {
        Assert.True(E.Director.Encounters.Selection.On);
        Assert.InRange(E.Director.Encounters.Selection.ZoneRepeatWeight, 0.01, 0.99);
        Assert.True(Place.On);
        Assert.True(Place.ThreatSpreadM > 0 && Place.OnTopM > 0);
        Assert.InRange(Place.NearThreatWeight, 0.01, 0.99);
        Assert.InRange(Place.OnTopWeight, 0.01, 0.99);
    }

    [Fact]
    public void WithNothingAboutAndNobodyNearItPicksWhatTheUniformPickDid()
    {
        // The night's dice stay as they were: the same draw, the same place.
        for (int i = 0; i < 1000; i++)
        {
            double draw = i / 1000.0 * (Cars.Length - 1e-9);
            Assert.Equal((int)draw, EncounterPlacement.Pick(Cars, [], [], draw, Place));
        }
        // Off, the same whatever's about.
        var off = Place with { On = false };
        for (int i = 0; i < 1000; i++)
        {
            double draw = i / 1000.0 * (Cars.Length - 1e-9);
            Assert.Equal((int)draw, EncounterPlacement.Pick(Cars, [Cars[0], Cars[1]], [Cars[5]], draw, off));
        }
    }

    [Fact]
    public void AlongALongTrainWhatComesIsSpreadAwayFromTheThreatsAlreadyOnIt()
    {
        // Three threats on the front three cars: the next is put there far less than a uniform pick would (3 in 12), and
        // spread over the rest of the train.
        var picks = Picks([Cars[0], Cars[1], Cars[2]], []);
        double front = picks.Count(p => p <= 2) / (double)picks.Length;
        Assert.True(front < 0.25 * 0.5, $"{front:P0} at the front");
        Assert.True(picks.Count(p => p >= 6) > picks.Length / 2, "not spread down the train");
        // Every car is still possible: it's a weighing, not a ban.
        Assert.Equal(12, picks.Distinct().Count());
        // The nearer a car to a threat, the less it's picked.
        var one = Picks([Cars[0]], []);
        Assert.True(one.Count(p => p == 0) < one.Count(p => p == 1));
        Assert.True(one.Count(p => p == 1) < one.Count(p => p == 4));
    }

    [Fact]
    public void ItIsntPutOnTopOfACrewmate()
    {
        var picks = Picks([], [Cars[4]]);
        int uniform = picks.Length / Cars.Length;
        Assert.True(picks.Count(p => p == 4) < uniform * 0.5, $"car 4 picked {picks.Count(p => p == 4)} of {picks.Length}");
        Assert.All(new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11 }, c => Assert.True(picks.Count(p => p == c) >= uniform));
    }

    [Fact]
    public void AZoneWithThreatsInItWeighsTheNextOneThereDown()
    {
        var t = E.Director.Encounters.Selection;
        Assert.Equal(1, EncounterPlacement.ZoneSpread(0, t));
        Assert.Equal(t.ZoneRepeatWeight, EncounterPlacement.ZoneSpread(1, t), 12);
        Assert.Equal(t.ZoneRepeatWeight * t.ZoneRepeatWeight, EncounterPlacement.ZoneSpread(2, t), 12);
        Assert.Equal(1, EncounterPlacement.ZoneSpread(3, t with { On = false }));
    }

    [Fact]
    public void ARulesPlaceOnARealTrainIsSpreadFromTheThreatsAboutIt()
    {
        // A twelve-car train with three Draggers under its first three cars: the Hotbox's car (any car of the rake) is picked
        // away from them.
        var n = new GuardNight(DirectorGuardTests.Pressed(), null, front: 3_000, speed: 14, cars: 12);
        n.Aboard(1);
        n.Run(1);
        var rake = n.Train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0).Select(v => v.Id).ToList();
        foreach (int car in rake.Take(3))
        {
            var shape = n.Train.Frames[car].Shape;
            n.World.AddEnemy(id => new Dragger(id) { Attached = car, Local = new Double3(shape.HalfWidth + 0.1, -0.3, 0), Extra = -1 });
        }
        int Front(PlacementTuning t)
        {
            var ctx = new SpawnContext(n.World, E with { Director = E.Director with { Encounters = E.Director.Encounters with { Placement = t } } }, n.Director);
            return Enumerable.Range(0, 400).Count(_ => rake.Take(3).Contains(ctx.Spread(rake)));
        }
        int spread = Front(Place), uniform = Front(Place with { On = false });
        Assert.True(spread < uniform / 2, $"{spread} of 400 at the front spread, {uniform} uniform");
    }
}
