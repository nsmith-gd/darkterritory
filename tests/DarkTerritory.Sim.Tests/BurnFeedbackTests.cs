using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 821 (the director's 14 Oct playtest, line 25: "I wasn't getting any feedback that I was being burned. And I was
/// standing at the edge of the fire"): how hot a car fire is on a crewmate, as every machine can reckon it from the
/// replicated fire, is the burn's own measure: it's on whoever the burn is burning, from the first moment, even at the edge
/// where no whole point has come off yet, and nowhere it isn't.
/// </summary>
public class BurnFeedbackTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning E = EveryKind.Enemies;
    const int Car = 2;

    static (Night N, CarFire Fire) Blaze()
    {
        var n = new Night(5, speed: 8);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, Car, 2, E.CarFire, FireCause.Set).Ablaze(1, 1.5));
        n.Train.Vehicles[Car].Cargo = CargoKind.None;
        n.Run(3); // alight (past its telegraph), with nobody in the car yet
        return (n, fire);
    }

    static PlayerState At(Night n, double z, int car = Car) =>
        new() { Parent = car, Position = new Double3(n.Train.Frames[car].Shape.Interior!.Value.Centre.X, n.Train.Frames[car].Shape.Interior!.Value.Min.Y, z), Surface = Surface.Deck, Health = P.Health };

    [Fact]
    public void TheHeatOnACrewmateIsWhereTheBurnBurnsThem()
    {
        var (n, fire) = Blaze();
        var grid = FireGrid.Of(n.Train, Car, E.CarFire.CellSize)!;
        // In it: hot, and burned by the second.
        var inIt = At(n, fire.Local.Z + 0.5);
        double hot = CarFire.HeatOn(inIt, n.World);
        Assert.InRange(hot, 0.3, 1);
        Assert.Equal(CarFire.HeatOn(inIt.Position, grid, fire.Heat, E.CarFire), hot);
        n.Crew[1] = inIt;
        n.Run(1);
        Assert.True(n.Crew[1].Health < P.Health, "burned standing in it");

        // At its edge (the last burning cell's patch and most of its reach away): a little heat, there from the first tick.
        var (e, edgeFire) = Blaze();
        var edgeGrid = FireGrid.Of(e.Train, Car, E.CarFire.CellSize)!;
        // Inside the car, along from the fire to its farther end: the last place the heat reaches.
        var room = e.Train.Frames[Car].Shape.Interior!.Value;
        double end = Math.Abs(room.Max.Z - edgeFire.Local.Z) > Math.Abs(room.Min.Z - edgeFire.Local.Z) ? room.Max.Z - 0.3 : room.Min.Z + 0.3;
        double step = Math.Sign(end - edgeFire.Local.Z) * 0.05, far = double.NaN;
        for (double z = edgeFire.Local.Z; (end - z) * Math.Sign(step) > 0; z += step)
            if (CarFire.HeatOn(At(e, z).Position, edgeGrid, edgeFire.Heat, E.CarFire) > 0)
                far = z;
        Assert.True(Math.Abs(end - far) > 0.6, $"the heat stops short of the car's end ({far:0.00} of {end:0.00})");
        var edge = At(e, far - 4 * step);
        double warm = CarFire.HeatOn(edge, e.World);
        Assert.InRange(warm, 1e-6, 0.3);
        // Out of reach down the car, in the next car, or dead: none.
        Assert.Equal(0, CarFire.HeatOn(At(e, far + 10 * step), e.World));
        Assert.Equal(0, CarFire.HeatOn(At(e, edgeFire.Local.Z + 0.5, Car + 1), e.World));
        Assert.Equal(0, CarFire.HeatOn(At(e, edgeFire.Local.Z + 0.5) with { Death = DeathCause.Burned }, e.World));
        // It's the burn's measure: stood there long enough it burns them too.
        e.Crew[1] = edge;
        e.Run(8);
        Assert.True(e.Crew[1].Health < P.Health, $"burned at the edge ({warm:0.000})");
    }
}
