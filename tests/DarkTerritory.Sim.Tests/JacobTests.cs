using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Jacob, the fisherman (GDD §3.2; the director, 8 Oct 2026; ARCHITECTURE §8 note 572). Very rarely he's at a water's
/// edge beside the line; a word with him (Use held by him, the host's) mends the whole train as new, once a night, and
/// leaves what it's carrying, its coal and its powder alone.
/// </summary>
public class JacobTests
{
    static readonly JacobTuning J = Tuning.Enemies.Jacob;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();

    static (Night Night, Jacob Jacob) ByTheWater()
    {
        var n = new Night(4, speed: 0, boiler: true);
        var at = n.Train.Frames[2].ToWorld(new Double3(25, 0, 0));
        double hint = n.Train.Dynamics.Distance;
        at = at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
        var jacob = n.World.AddEnemy(id => Jacob.At(id, at, hint, 0));
        return (n, jacob);
    }

    static void Stand(Night n, int id, Double3 at) => n.Crew[id] = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);

    /// <summary>The train knocked about: every kind of damage and wear it can carry, and a load, coal and powder to keep.</summary>
    static void Batter(Night n)
    {
        foreach (var v in n.Train.Dynamics.Consist.Vehicles)
        {
            v.Integrity = 0.35;
            v.Eaten = 0.2;
            v.Char = [9, 15, 4];
            v.Breached = true;
            v.HotBox = 40;
            v.Gutter = 20;
            v.Loose = 12;
            v.LampLit = false;
            v.Wound = true;
            v.Seized = true;
            v.Load = 0.7;
            v.CargoIntegrity = 0.8;
            v.Gun = new GunState { Mounted = true, Ammo = 9, Rack = 3, Jammed = true, Cooldown = 4 };
        }
        n.Train.Boiler.Ruptured = true;
        n.Train.Boiler.SafetyValveJammed = true;
        n.Train.Boiler.Tender = 123;
        n.World.SmashLamp(60);
    }

    [Fact]
    public void AWordWithHimMendsTheTrainAsNewAndLeavesItsLoadCoalAndPowder()
    {
        var (n, jacob) = ByTheWater();
        Batter(n);
        Stand(n, 1, jacob.Local + new Double3(1.2, 0, 0));
        n.Run(J.HoldSeconds + 0.3, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(jacob.Blessed);
        foreach (var v in n.Train.Dynamics.Consist.Vehicles)
        {
            Assert.Equal(1, v.Integrity);
            Assert.Equal(0, v.Eaten);
            Assert.Empty(v.Char);
            Assert.False(v.Breached);
            Assert.True(v.HotBox < 1 && v.Gutter < 1 && v.Loose < 1, $"car {v.Id}: hot box {v.HotBox:0.0}, gutter {v.Gutter:0.0}, loose {v.Loose:0.0}");
            Assert.True(v.LampLit);
            Assert.False(v.Wound || v.Seized, $"car {v.Id}: its handbrake unwound and its axle freed");
            Assert.False(v.Gun.Jammed);
            // What it carries and its powder, untouched.
            Assert.Equal(0.7, v.Load);
            Assert.Equal(0.8, v.CargoIntegrity);
            Assert.Equal(9, v.Gun.Ammo);
            Assert.Equal(3, v.Gun.Rack);
        }
        var b = n.Train.Boiler;
        Assert.False(b.Ruptured);
        Assert.False(b.SafetyValveJammed);
        Assert.True(b.Pressure >= Tuning.Boiler.WorkingBandMin, $"pressure {b.Pressure:0.0}: in steam again");
        Assert.True(b.Tender <= 123, "the tender's coal is the crew's, not the blessing's");
        Assert.Equal(0, n.World.LampOutSeconds);
    }

    [Fact]
    public void ItTakesAWordInReachAndOnlyOnceANight()
    {
        var (n, jacob) = ByTheWater();
        // Too far off: nothing.
        Stand(n, 1, jacob.Local + new Double3(J.Reach + 2, 0, 0));
        n.Run(2, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.False(jacob.Blessed);
        // In reach but only a tap: nothing.
        Stand(n, 1, jacob.Local + new Double3(1, 0, 0));
        n.Run(J.HoldSeconds / 2, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        n.Run(0.3);
        Assert.False(jacob.Blessed);
        // Held: the train's made new. Then knocked about again, and another word does nothing more.
        n.Run(J.HoldSeconds + 0.3, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(jacob.Blessed);
        n.Train.Dynamics.Consist.Vehicles[1].Integrity = 0.4;
        n.Run(J.HoldSeconds + 0.3, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.Equal(0.4, n.Train.Dynamics.Consist.Vehicles[1].Integrity);
        Assert.False(jacob.Hazard is false || jacob.Exposed || jacob.MeleeRadius > 0);
    }

    [Fact]
    public void WhereHesFoundHesOnDryGroundAtAWatersEdgeTheSameEveryTime()
    {
        var always = J with { Chance = 1 };
        int found = 0;
        for (int seed = 1; seed <= 16; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 6);
            var n = new Night(6, speed: 0, route: route);
            var a = Jacob.Site(n.World, route, always);
            Assert.Equal(a, Jacob.Site(n.World, route, always));
            if (a is not { } site)
                continue;
            found++;
            var train = n.Train;
            double hint = site.Along;
            double ground = PlayerMotor.GroundAt(site.At, train.Line, ref hint);
            Assert.False(Jacob.WaterAt(train, site.At) is { } wet && wet > ground, $"seed {seed}: he's stood in the water");
            var ahead = site.At + new Double3(-Math.Sin(site.Yaw), 0, -Math.Cos(site.Yaw)) * always.Edge;
            double h2 = site.Along;
            Assert.True(Jacob.WaterAt(train, ahead) is { } water && water > PlayerMotor.GroundAt(ahead, train.Line, ref h2), $"seed {seed}: no water in front of him");
            Assert.True(Moose.TrackOff(train, site.At, site.Along) >= always.TrackClearance - 1e-6);
        }
        Assert.True(found > 0, "no line of 16 had a water's edge for him");
        // And he's rare: off the director's rolls, most nights have none.
        Assert.True(J.Chance <= 0.1);
    }
}
