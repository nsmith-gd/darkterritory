using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 970 (the director, 21 Oct 2026, on n1-B3-car-hugger-low-ground: "We need more situations the Hugger can pop up, not
/// just marshes"): behind carHugger.lowGround, the line plan's places of its kind (by the director's tags) and the far side of
/// a train stood still are spots too; and the free Climbers' "lower third of the speed limit".
/// </summary>
public class CarHuggerPlacesTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly EnemyTuning E = Tuning.Enemies;

    static World WorldAt(Route.Route route, RailLine line, double at, double speed = 0)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, at);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(E, route, 1, 2, authority: true);
        return world;
    }

    [Fact]
    public void TheLinePlansTaggedPlacesAreSpotsAheadTooOnlyWhenListed()
    {
        var low = E.CarHugger.LowGround with { On = true, Biomes = [], Stood = new() };
        var none = E.CarHugger with { LowGround = low with { Tags = [] } };
        var tagged = E.CarHugger with { LowGround = low };
        Assert.NotEmpty(tagged.LowGround.Tags);
        var route = Enumerable.Range(1, 12).Select(s => Routes.Generate(Content, $"frontier:{s}", 4))
            .First(r => r.Plan?.Director.Tags.Any(g => g.Edge == "main" && tagged.LowGround.Tags.Contains(g.Tag)) == true);
        var line = route.Build();
        int without = 0, with = 0;
        for (double at = 300; at < route.RunLength - 1000; at += 150)
        {
            var world = WorldAt(route, line, at, speed: 10);
            var was = CarHugger.Spot(world, none);
            var now = CarHugger.Spot(world, tagged);
            without += was is null ? 0 : 1;
            with += now is null ? 0 : 1;
            if (was is not null)
                Assert.NotNull(now);
            if (now is { } spot)
            {
                Assert.InRange(spot, at + tagged.LurkAheadMin, at + tagged.LurkAheadFar);
                Assert.True(Math.Abs(line.Sample(world.Train.Dynamics.Path, spot).GradePercent) <= tagged.MaxGrade);
            }
        }
        Assert.True(with > without, $"{with} spots with the tags, {without} without");
    }

    [Fact]
    public void AStoodTrainsRearIsASpotOnlyWithNobodyByItAndItComesUpOnTheFarSide()
    {
        var on = E.CarHugger with { LowGround = E.CarHugger.LowGround with { On = true, Biomes = [], Tags = [], Stood = new() { On = true } } };
        var route = Routes.Generate(Content, "frontier:7", 4);
        var line = route.Build();
        // Stood, nobody about: beside its rear car.
        var world = WorldAt(route, line, 2_000);
        Assert.Equal(world.Train.Dynamics.RearDistance + 1, CarHugger.StoodSpot(world, on));
        Assert.Equal(world.Train.Dynamics.RearDistance + 1, CarHugger.Spot(world, on));
        // Moving: none (the spots ahead are the ones for a moving train).
        Assert.Null(CarHugger.StoodSpot(WorldAt(route, line, 2_000, speed: 5), on));
        // Off: none.
        Assert.Null(CarHugger.StoodSpot(world, E.CarHugger));

        // A crewmate on the ground beside the rear car: watched, none. 30 m off on its right: a spot, on its left.
        var n = new Night(4, speed: 0, enemies: E with { CarHugger = on });
        var rear = n.Train.Frames[n.Train.Dynamics.Consist.Vehicles[^1].Id];
        PlayerState Beside(double right, double back)
        {
            var at = rear.ToWorld(new Double3(right, 0, back));
            double hint = n.Train.Dynamics.Distance;
            return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) }, n.Train.Line, hint, Tuning.Player);
        }
        n.Crew[1] = Beside(rear.Shape.HalfWidth + 1.5, 0);
        n.Run(0.1);
        Assert.Null(CarHugger.StoodSpot(n.World, on));
        n.Crew[1] = Beside(30, 0);
        n.Run(0.1);
        Assert.NotNull(CarHugger.StoodSpot(n.World, on));
        Assert.Equal(-1, CarHugger.FarSide(n.World));
    }

    [Fact]
    public void TheStretchChanceKeepsThatShareOfTheLowStretchesTheSameEveryTime()
    {
        // Note 981: the marsh stretches thinned by a chance per stretch, by the line's seed and where it starts.
        var all = E.CarHugger.LowGround with { On = true, StretchChance = 1 };
        var plans = Enumerable.Range(1, 24).Select(s => Routes.Generate(Content, $"frontier:{s}", 4).Plan!).ToList();
        var stretches = plans.SelectMany(p => p.Biomes.Where(b => b.Edge == "main" && all.Biomes.Contains(b.Biome)).Select(b => (p.Seed, b.S0))).ToList();
        Assert.True(stretches.Count >= 20, $"{stretches.Count} low stretches");
        Assert.All(stretches, s => Assert.True(all.Keeps(s.Seed, s.S0)));
        var third = all with { StretchChance = 1.0 / 3 };
        int kept = stretches.Count(s => third.Keeps(s.Seed, s.S0));
        Assert.InRange(kept, stretches.Count * 0.15, stretches.Count * 0.5);
        // The same stretch, the same answer; nothing at 0.
        Assert.All(stretches, s => Assert.Equal(third.Keeps(s.Seed, s.S0), third.Keeps(s.Seed, s.S0)));
        Assert.DoesNotContain(stretches, s => (all with { StretchChance = 0 }).Keeps(s.Seed, s.S0));
        // A thinner chance's stretches are among a thicker one's.
        var half = all with { StretchChance = 0.5 };
        Assert.All(stretches.Where(s => third.Keeps(s.Seed, s.S0)), s => Assert.True(half.Keeps(s.Seed, s.S0)));
    }

    [Fact]
    public void TheShippingHuggerIsOnAtTheMeasuredSetting()
    {
        // The director's "on" (21 Oct 2026, n1-B3-car-hugger-low-ground), at exactly what note 981 measured at 30 seeds:
        // its low ground, the plan's places and a stood train's far side, a third of the marsh stretches, one a night.
        var low = E.CarHugger.LowGround;
        Assert.True(low.On);
        Assert.NotEmpty(low.Tags);
        Assert.True(low.Stood.On);
        Assert.Equal(0.3, low.StretchChance);
        Assert.Equal(1, E.Director.PerNight["carHugger"]);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.6, true)]
    [InlineData(5.9, true)]
    [InlineData(6.1, false)]
    [InlineData(14.0, false)]
    public void TheFreeClimbersComeInTheLowerThirdOfTheLineSpeed(double speed, bool comes)
    {
        // The Frontier's line speed 18 m/s (linegen tiers.json): its third, 6.
        Assert.Equal(comes, (E.Climbers.FreeSpeed with { On = true }).Allows(speed, RouteTier.Frontier));
    }
}
