using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The flare pistol (queue #598, ARCHITECTURE §8 note 871; docs/design/upgrades.md §4: "A flare lights a village for 60 s, and the
/// Fire Flies follow it away from the train"; the director, 13 Oct: "fantastic"): sold at the stores from the Frontier tier,
/// in the guard van loaded; Fire with it in hand sends a flare out that burns its minute where it comes down, as bright a light
/// as there is; and a swarm on a car's lamp goes off after it, and sets nothing alight.
/// </summary>
public class FlarePistolTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), CampaignTuning.File));
    static readonly Combat.FlarePistolTuning F = Tuning.Combat.FlarePistol;
    static PlayerIntent Fire => new() { Buttons = PlayerButtons.Fire };

    /// <summary>Steps the night as the host does, its bodies too (the crew, the world, the bodies).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.World.BeginTick();
            foreach (var id in n.Crew.Keys.ToList())
            {
                var s = n.Crew[id];
                n.World.CrewAct(ref s, intent?.Invoke(id) ?? default, id);
                n.Crew[id] = s;
            }
            n.World.Step(n.Controls);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    [Fact]
    public void ItsSoldFromTheFrontierTierAndComesAboardLoaded()
    {
        var local = new CampaignState { Scrip = 1000, Cars = 3 };
        var refused = Campaign.Campaign.BuyStores(C, local, StoreKind.Flare);
        Assert.False(refused.Ok);
        Assert.Contains("stocked for", refused.Refused);
        int frontier = C.Tiers.First(t => t.Tier == Route.RouteTier.Frontier).FromCars;
        var bought = Campaign.Campaign.BuyStores(C, local with { Cars = frontier }, StoreKind.Flare);
        Assert.True(bought.Ok, bought.Refused);
        Assert.Equal(1, bought.State.Stores.Flares);
        var flares = C.Stores!.Flares!;
        Assert.Equal(1000 - flares.Cost, bought.State.Scrip);
        // Aboard: in the guard van, loaded with its flares.
        var kit = Campaign.Campaign.WithStores(C, new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies), bought.State.Stores).Train;
        Assert.Equal(1, kit.Kit.FlarePistols);
        var n = new Night(5, 0, trainTuning: kit);
        n.World.Stock();
        var pistol = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.FlarePistol);
        Assert.Equal(flares.Each, pistol.Charge);
    }

    /// <summary>Crewmate 1 on the ground beside car 2, a loaded flare pistol in hand, looking out from the train and up a little.</summary>
    static (Night N, Body Pistol) Holding()
    {
        var n = new Night(4, 0);
        var at = n.Train.Frames[2].ToWorld(new Double3(3, 0, 0));
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        // Out to the right of the train (its +X), 20 degrees up.
        var right = n.Train.Frames[2].Right;
        n.Crew[1] = s with { Yaw = Math.Atan2(-right.X, -right.Z), Pitch = 20 * Math.PI / 180 };
        var pistol = n.World.Bodies.SpawnItem(PlayerMotor.WorldPosition(n.Crew[1], n.Train) + Double3.Up * 1.1, n.Train.Dynamics.Distance, BodyKind.FlarePistol);
        pistol.Charge = 3;
        pistol.Carrier = 1;
        return (n, pistol);
    }

    [Fact]
    public void FireSendsAFlareOutThatBurnsItsMinuteWhereItLandsAndIsGone()
    {
        var (n, pistol) = Holding();
        var from = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        // One press, held: one flare.
        Run(n, 0.5, _ => Fire);
        var flare = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Flare);
        Assert.Equal(2, pistol.Charge);
        // Let go and pressed again within flarePistol.every of it: nothing.
        Run(n, 0.1);
        Run(n, 0.1, _ => Fire);
        Assert.Equal(2, pistol.Charge);
        // Nobody picks a burning flare up.
        Assert.NotEqual(flare, n.World.Bodies.InReach(n.Crew[1], n.Train));
        // Out and down, well clear of the train, and burning: the brightest light about, and a village's worth of it.
        Run(n, 4);
        var landed = Bodies.WorldCentre(flare, n.Train);
        Assert.True(((landed - from) with { Y = 0 }).Length > 15, $"landed {((landed - from) with { Y = 0 }).Length:0.0} m out");
        var light = Assert.Single(n.World.AllEmitters(), e => e.Kind == EmitterKind.Flare && e.Sense == Sense.Light && e.Source == flare.Id);
        Assert.True(light.Radius >= 50);
        // Pressed again after it: the second.
        Run(n, 0.1, _ => Fire);
        Assert.Equal(2, n.World.Bodies.All.Count(b => b.Kind == BodyKind.Flare));
        Assert.Equal(1, pistol.Charge);
        // Its minute out, it's gone.
        Run(n, F.BurnSeconds);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Flare);
        Assert.DoesNotContain(n.World.AllEmitters(), e => e.Kind == EmitterKind.Flare);
    }

    [Fact]
    public void AnEmptyPistolFiresNothing()
    {
        var (n, pistol) = Holding();
        pistol.Charge = 0;
        Run(n, 0.5, _ => Fire);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Flare);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFireFliesFollowAFlareOffTheTrainAndSetNothingAlight(bool flare)
    {
        // A stopped train, car 3's lamp lit, the swarm on it; a flare burning 40 m out from it. Without one, they set the car
        // alight (App. A.5); with one, they go off after it, swarm round it, and are gone when it's out.
        var n = new Night(5, 0);
        n.Train.Vehicles[3].LampLit = true;
        var flies = n.World.AddEnemy(id => FireFlies.OnLamp(id, n.Train, 3));
        Body? lit = null;
        if (flare)
        {
            var at = n.Train.Frames[3].ToWorld(new Double3(40, 0, 0));
            double hint = n.Train.Dynamics.Distance;
            lit = n.World.Bodies.SpawnItem(at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) }, hint, BodyKind.Flare);
            lit.Burns = F.BurnSeconds;
            lit.Pbd.Sleep();
        }
        Run(n, Tuning.Enemies.FireFlies.IgniteSeconds + 2);
        if (!flare)
        {
            Assert.Contains(n.World.ActiveEnemies, e => e is CarFire { Gone: false, Attached: 3 });
            return;
        }
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
        Assert.False(flies.Gone);
        Assert.Equal(Enemy.Loose, flies.Attached);
        Assert.True((flies.WorldPosition(n.Train) - Bodies.WorldCentre(lit!, n.Train)).Length < 2, "they're not round the flare");
        // Out, and they're gone with it; the car never caught.
        Run(n, F.BurnSeconds);
        Assert.True(flies.Gone);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire { Gone: false });
    }
}
