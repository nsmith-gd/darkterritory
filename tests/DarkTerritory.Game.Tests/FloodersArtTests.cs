using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A Flooder's houseboat drawn (queue #549, note 822): a hull under the house up on its stilts, the house on its deck, its
/// chimney's smoke out of its lifted chimney; near, far and in a street's block alike.
/// </summary>
public class FloodersArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static Town Flooders()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "local:1", 6);
        var line = route.Build();
        var loaded = TownContent.Load(Content)!;
        var towns = loaded with { Tuning = loaded.Tuning with { Forms = loaded.Tuning.Forms with { Force = "dykeland" } } };
        double gate = route.GateOr(Sim.Route.RouteTuning.Load(Content).YardLength);
        var roster = DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)).Director.Roster;
        var town = new Town(TownGenerator.Generate(towns, TownSite.Of(route, gate, roster, towns)), towns.Tuning, line, towns.Looks);
        town.Lay(line);
        return town;
    }

    static (Vector3 Lo, Vector3 Hi) Bounds(MeshAsset m)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in m.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        return (lo, hi);
    }

    [Fact]
    public void AHouseboatStandsOnItsHullOnItsStilts()
    {
        var town = Flooders();
        Assert.Equal("flooders", town.Plan.Character);
        var boat = town.Plan.Houses.First(h => h.Design.Hull && h.Kind == HouseKind.Lived);
        var plain = boat with { Design = boat.Design with { Hull = false } };
        var (blo, bhi) = Bounds(MaritimeKit.House(Look, boat, town.Looks, null, true));
        var (plo, phi) = Bounds(MaritimeKit.House(Look, plain, town.Looks, null, true));
        // The house lifted onto the hull's deck: its roof the hull's height higher.
        Assert.InRange(bhi.Y - phi.Y, MaritimeKit.HullLift - 0.05f, MaritimeKit.HullLift + 0.05f);
        // The hull out past its walls on both sides (its yard is the same either way: only the hull reaches past it).
        Assert.True(bhi.X >= phi.X + MaritimeKit.HullBeam - 0.05f || blo.X <= plo.X - MaritimeKit.HullBeam + 0.05f, "no hull out past the house's sides");
        // Far and in a block, the same lift.
        var (_, fhi) = Bounds(MaritimeKit.Far(Look, boat, town.Looks, true));
        var (_, fphi) = Bounds(MaritimeKit.Far(Look, plain, town.Looks, true));
        Assert.InRange(fhi.Y - fphi.Y, MaritimeKit.HullLift - 0.05f, MaritimeKit.HullLift + 0.05f);
        // Its smoke out of its chimney where the chimney is now.
        var tops = MaritimeKit.ChimneyTops(boat).ToList();
        var plainTops = MaritimeKit.ChimneyTops(plain).ToList();
        Assert.Equal(plainTops.Count, tops.Count);
        Assert.All(tops.Zip(plainTops), p => Assert.Equal(MaritimeKit.HullLift, p.First.Y - p.Second.Y, 0.01f));
    }
}
