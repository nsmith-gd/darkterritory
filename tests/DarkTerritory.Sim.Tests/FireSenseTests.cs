using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 539 (queue #281; note 495's not-yet, D1.3's frontier:7 4-bot seed 1): the winch pair went into car 1 to fight its
/// fire when it was already well alight, walked at the extinguisher through it for 10 s without reaching it, and burned from
/// 99 to 14 before going. A fire-fighter doesn't walk into a car well alight, fights a fire from outside its reach, and is
/// out before it's too hurt, not shut in with it.
/// </summary>
public class FireSenseTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A walker on car 2's roof, car 3 alight (<paramref name="span"/> m of it at <paramref name="heat"/>), for a minute.</summary>
    static (int Least, bool InAtTheEnd, bool EverIn, string Steps) Run(double heat, double span)
    {
        var n = new Night(5, speed: 10);
        n.World.MountExtinguishers();
        var bot = new Bots.RoofWalkerBot(3, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 2, Tuning.Enemies.CarFire).Ablaze(heat, span));
        int least = P.Health;
        bool everIn = false;
        var steps = new List<string>();
        for (int i = 0; i < 60 * 4 && !fire.Gone && n.Crew[1].Alive; i++)
        {
            n.Run(0.25, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            least = Math.Min(least, n.Crew[1].Health);
            everIn |= n.Crew[1].Parent == 3 && n.Crew[1].Surface == Surface.Deck;
            string w = $"{bot.TendStep ?? bot.WarmUpStep ?? "-"}:{n.Crew[1].Surface}{n.Crew[1].Parent}:{n.Crew[1].Health / 10 * 10}";
            if (steps.Count == 0 || steps[^1] != w) steps.Add(w);
        }
        bool inAtTheEnd = !fire.Gone && n.Crew[1].Parent == 3 && n.Crew[1].Surface == Surface.Deck;
        return (least, inAtTheEnd, everIn, string.Join(" > ", steps.TakeLast(16)));
    }

    [Fact]
    public void ACarWellAlightIsntWalkedInto()
    {
        // The whole car at full blaze from the next roof: never in (it used to go in, and died in there). Its fire spreads to
        // the cars either side (spreadFrom), and those, still small, it fights, from out of their reach, and lives.
        var (least, _, everIn, steps) = Run(0.97, 99);
        Assert.False(everIn, steps);
        Assert.True(least >= Bots.RoofWalkerBot.FireFightHealth - 15, $"burned to {least}: {steps}");
    }

    [Fact]
    public void AHotFireItCantBeatItsOutOfBeforeItsTooHurt()
    {
        // Three metres of it at full blaze, growing: in, the extinguisher, and at it from out of its reach; and out of the car,
        // not shut in with it, while there's health to go (it used to burn on to nothing in there).
        var (least, inAtTheEnd, everIn, steps) = Run(0.97, 3);
        Assert.True(everIn, steps);
        Assert.False(inAtTheEnd, steps);
        Assert.True(least >= Bots.RoofWalkerBot.FireFightHealth - 15, $"burned to {least}: {steps}");
    }
}
