using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 551 (queue #293, D1's ask on #286): on main's 4-bot nights every mauling of a crewmate too hurt to fight the pack
/// (under <see cref="Heed.PackFightHealth"/>) was one out on the roofs with the pack about. Hurt, it gets in off the roofs, in
/// a car off the pack's ground, behind shut doors, where no hound reaches it.
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
    public void TooHurtToFightThePackItGetsInOffItsGround()
    {
        var (n, bot, ground, steps) = Walk(19, 40);
        var s = n.Crew[1];
        string where = $"{string.Join(" > ", steps)}; {s.Surface} on {s.Parent}, health {s.Health}, pack's ground from consist {ground}";
        Assert.True(bot.Hunted, where);
        Assert.True(PlayerMotor.Indoors(s, n.Train), where);
        Assert.True(n.Train.Dynamics.Consist.IndexOf(s.Parent) < ground, where);
        Assert.Equal("Warm/shelter", bot.WarmUpStep);
        Assert.Equal(19, s.Health);
        Assert.Equal(0, n.Train.Vehicles[s.Parent].DoorsOpen & ~(1 << CarShape.HatchBit));
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
