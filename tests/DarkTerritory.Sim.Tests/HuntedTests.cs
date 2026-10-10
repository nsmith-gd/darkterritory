using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 551 (queue #293, D1's ask on #286): on main's 4-bot nights every mauling of a crewmate too hurt to fight the pack
/// (under <see cref="Heed.PackFightHealth"/>) was one out on the roofs with the pack about. Hurt, it goes forward into the cab,
/// where no hound reaches it; in a car already, it stays in with the doors shut.
/// </summary>
public class HuntedTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static List<CinderHound> PackOn(Night n, int car, int size)
    {
        var shape = n.Train.Frames[car].Shape;
        var pack = new List<CinderHound>();
        for (int i = 0; i < size; i++)
        {
            var h = n.World.AddEnemy(id => new CinderHound(id, id));
            h.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5 - i * 0.8), 0, 0, 0.6, h.Id, 0);
            pack.Add(h);
        }
        return pack;
    }

    static (Night N, RoofWalkerBot Bot, int Ground, List<string> Steps) Walk(int health, double seconds)
    {
        // A pack on car 4, a walker on car 2's roof, warm: its ground runs forward of car 4 (FrontCar), so car 2 may be on it.
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var cars = n.Train.Dynamics.Consist.Vehicles;
        PackOn(n, cars[4].Id, 3);
        n.Run(SimConstants.TickSeconds);
        var hound = n.World.ActiveEnemies.OfType<CinderHound>().First();
        int ground = n.Train.Dynamics.Consist.IndexOf(hound.FrontCar(n.Train, Tuning.Enemies.CinderHounds));
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, cars[2].Id, 0, P) with { Health = health };
        var steps = new List<string>();
        for (int i = 0; i < seconds * 4; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (bot.WarmUpStep is { } w && (steps.Count == 0 || steps[^1] != w)) steps.Add(w);
        }
        return (n, bot, ground, steps);
    }

    [Fact]
    public void TooHurtToFightThePackItGoesForwardIntoTheCab()
    {
        var (n, bot, ground, steps) = Walk(19, 60);
        var s = n.Crew[1];
        string where = $"{string.Join(" > ", steps)}; {s.Surface} on {s.Parent} at z {s.Position.Z:0.0}, health {s.Health}, pack's ground from consist {ground}";
        Assert.True(bot.Hunted, where);
        Assert.True(PlayerMotor.InCab(s, n.Train), where);
        Assert.Equal(19, s.Health);
    }

    [Fact]
    public void TooHurtAndInACarItStaysInWithTheDoorsShut()
    {
        // Warming in car 1 (ahead of the pack's ground) as the pack comes: in it stays, warm or not, its doors shut.
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var cars = n.Train.Dynamics.Consist.Vehicles;
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, cars[1].Id, 2, P) with { Cold = P.Cold.OnsetSeconds * 0.9, Health = 30 };
        for (int i = 0; i < 60 * 4 && bot.WarmUpStep is not "Warm/cold"; i++)
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
        Assert.Equal("Warm/cold", bot.WarmUpStep);
        int car = n.Crew[1].Parent;
        PackOn(n, cars[4].Id, 3);
        for (int i = 0; i < 30 * 4; i++)
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
        var s = n.Crew[1];
        Assert.True(bot.Hunted);
        Assert.True(PlayerMotor.Indoors(s, n.Train) && s.Parent == car, $"{bot.WarmUpStep}; {s.Surface} on {s.Parent}");
        Assert.Equal("Warm/shelter", bot.WarmUpStep);
        Assert.Equal(0, n.Train.Vehicles[car].DoorsOpen & ~(1 << CarShape.HatchBit));
        Assert.Equal(30, s.Health);
    }

    /// <summary>
    /// Note 602 (queue #334): a pack aboard car 5 and the pin behind car 2 loose, a walker on car 2's roof with the wrench. Hurt
    /// (30 hp, too hurt for the pack and for errands), it goes down into the gap and tightens the pin before the cab; on 8 hp
    /// it leaves it and goes in.
    /// </summary>
    [Theory]
    [InlineData(30, true)]
    [InlineData(8, false)]
    public void HurtAWalkerMendsALoosePinBesideItBeforeTakingShelter(int health, bool mends)
    {
        var n = new Night(6, 6, enemies: HoundRunTests.Quiet);
        var loose = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;
        n.World.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = loose with { FirstAfterMetres = 1e9 } };
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var cars = n.Train.Dynamics.Consist.Vehicles;
        PackOn(n, cars[5].Id, 3);
        n.Run(SimConstants.TickSeconds);
        int car = cars[2].Id;
        n.Train.Vehicles[car].Loose = SimConstants.TickSeconds;
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P) with { Health = health, Kit = Kit.Of([Tool.Shovel, Tool.Wrench]) };
        double? tight = null;
        for (int i = 0; i < 60 * 4 && tight is null; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (n.Train.Vehicles[car].Loose == 0)
                tight = i / 4.0;
        }
        var s = n.Crew[1];
        string where = $"{bot.WarmUpStep}; {s.Surface} on {s.Parent} at z {s.Position.Z:0.0}, health {s.Health}, hunted {bot.Hunted}";
        if (mends)
        {
            Assert.True(tight is not null, where);
            Assert.True(tight < loose.PartAfter, where);
        }
        else
        {
            Assert.Null(tight);
            Assert.True(PlayerMotor.InCab(s, n.Train), where);
        }
    }

    /// <summary>
    /// Note 602, D1's boundary on note 537: a pack aboard car 2, a fit walker on car 6's roof with the wrench. A pin with two
    /// whole cars between its gap and the pack (behind car 5: cars 3 and 4 between) it goes to and tightens; one on the
    /// pack's ground (behind car 3, next to it) it leaves.
    /// </summary>
    [Theory]
    [InlineData(5, true)]
    [InlineData(3, false)]
    public void AFitWalkerGoesToALoosePinOnlyClearOfThePack(int behind, bool goes)
    {
        var n = new Night(8, 6, enemies: HoundRunTests.Quiet);
        var loose = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;
        n.World.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = loose with { FirstAfterMetres = 1e9 } };
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        var cars = n.Train.Dynamics.Consist.Vehicles;
        var pack = PackOn(n, cars[2].Id, 2);
        n.Run(SimConstants.TickSeconds);
        int car = cars[behind].Id;
        n.Train.Vehicles[car].Loose = SimConstants.TickSeconds;
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, cars[6].Id, 0, P) with { Kit = Kit.Of([Tool.Shovel, Tool.Wrench]) };
        double? tight = null;
        for (int i = 0; i < 40 * 4 && tight is null; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (n.Train.Vehicles[car].Loose == 0)
                tight = i / 4.0;
        }
        var s = n.Crew[1];
        string where = $"{bot.WarmUpStep}; {s.Surface} on {s.Parent} at z {s.Position.Z:0.0}, health {s.Health}; pack on {string.Join(",", pack.Select(h => h.Attached))}";
        if (goes)
            Assert.True(tight is not null, where);
        else
        {
            Assert.Null(tight);
            Assert.NotEqual(car, s.Parent);
        }
    }

    [Fact]
    public void FitEnoughItStaysOutForThePack()
    {
        // Fit, it's the pack fight's (note 484): not sheltering from it.
        var (_, bot, _, steps) = Walk(P.Health, 5);
        Assert.False(bot.Hunted);
        Assert.DoesNotContain(steps, w => w.Contains("shelter"));
    }
}
