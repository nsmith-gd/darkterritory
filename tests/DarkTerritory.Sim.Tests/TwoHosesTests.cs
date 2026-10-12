using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using Xunit;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 634 (queue #364; the director, the inbox, 12 Oct: `D1-bots-two-extinguishers`, "a, two together"; built as two
/// before it's well alight, D1's choice of 12 Oct): a car fire is two hands' at most, the second bringing an extinguisher of
/// its own from the car beside it; a car well alight nobody goes into, and the cars either side are kept from catching.
/// </summary>
public class TwoHosesTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed record Result(bool Out, double Seconds, double Peak, double Cargo, int Least, bool InWellAlight, string Steps, List<int> Brought, string Fires);

    /// <summary>Walkers on the roofs of <paramref name="roofs"/>, car 3 alight <paramref name="span"/> m either side of its middle.</summary>
    static Result Run(int[] roofs, double heat, double span, double seconds = 200)
    {
        var n = new Night(6, speed: 10);
        n.World.MountExtinguishers();
        var calls = new Bots.CrewCalls();
        var bots = new List<Bots.RoofWalkerBot>();
        for (int i = 0; i < roofs.Length; i++)
        {
            bots.Add(new Bots.RoofWalkerBot(3 + i, P.Cold) { Me = i + 1, Calls = calls });
            n.Crew[i + 1] = PlayerMotor.SpawnOnRoof(n.Train, roofs[i], 0, P);
        }
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 0, Tuning.Enemies.CarFire).Ablaze(heat, span));
        uint t0 = n.World.Tick;
        double peak = 0;
        int least = P.Health;
        bool everIn = false;
        var brought = new List<int>();
        var steps = new List<string>();
        for (int t = 0; t < seconds * 4 && !fire.Gone; t++)
        {
            foreach (var b in bots)
                b.Crew = [.. n.Crew.Select(kv => (kv.Key, kv.Value))];
            PlayerIntent last = default;
            n.Run(0.25, id => { var i = bots[id - 1].Decide(n.Crew[id], n.World, n.World.Tick, out _); if (id == 2) last = i; return i; });
            peak = Math.Max(peak, fire.Extra);
            least = Math.Min(least, n.Crew.Values.Min(c => c.Health));
            everIn |= fire.Extra > Bots.RoofWalkerBot.WellAlight && n.Crew.Values.Any(c => c.Parent == 3 && c.Surface == Surface.Deck);
            foreach (var b in bots)
                if (n.World.Bodies.CarriedBy(b.Me) is { Kind: Physics.BodyKind.Extinguisher } ext && n.Crew[b.Me].Parent == 3 && !brought.Contains(ext.Home))
                    brought.Add(ext.Home);
            string w = string.Join(" ", bots.Select(b => $"{b.TendStep ?? b.WarmUpStep ?? "-"}:{n.Crew[b.Me].Surface}{n.Crew[b.Me].Parent}:{n.Crew[b.Me].Health / 10 * 10}@{n.Crew[b.Me].Position.X:0.0},{n.Crew[b.Me].Position.Z:0.0}"));
            if (steps.Count == 0 || steps[^1] != w)
                steps.Add($"{t / 4}s {w} f{fire.Extra:0.00} i2[{last.MoveX:0.0},{last.MoveZ:0.0},{last.LookYaw:0.00},{last.Buttons}] y{n.Crew[2].Yaw:0.00}");
        }
        string fires = string.Join(",", n.World.ActiveEnemies.Where(e => !e.Gone && e is CarFire).Select(e => $"{e.Attached}:{e.Extra:0.00}"));
        return new(fire.Gone, (n.World.Tick - t0) / (double)SimConstants.TickRate, peak, n.Train.Vehicles[3].CargoIntegrity, least, everIn,
            string.Join(" > ", steps.Where((_, k) => k % 3 == 0).TakeLast(40)), brought, fires);
    }

    [Fact]
    public void TheSecondHandBringsAnExtinguisherOfItsOwnFromTheCarBeside()
    {
        // A third of the car alight (3 m either side of its middle at full blaze): car 3's own extinguisher is the first
        // hand's; the second's is car 2's or car 4's. Out before it's well alight, and nobody hurt past the fit's margin.
        var r = Run([2, 4], 1.0, 3);
        Assert.True(r.Out, $"{r.Peak:0.00}: {r.Steps}");
        Assert.True(r.Brought.Any(home => home is 2 or 4), $"brought {string.Join(",", r.Brought)}: {r.Steps}");
        Assert.True(r.Peak < Bots.RoofWalkerBot.WellAlight, $"peaked at {r.Peak:0.00}: {r.Steps}");
        Assert.True(r.Least >= Bots.RoofWalkerBot.FireFightHealth - 15, $"burned to {r.Least}: {r.Steps}");
    }

    [Fact]
    public void ACarWellAlightIsLeftToBurnOutAndTheCarsEitherSideKeptFromIt()
    {
        // The whole car at full blaze: two hands can't put it out (note 634: 72 cells, 15 s of spray each, and it relights),
        // so nobody goes in while it is (once it's burning down, it's a fire like any). It burns itself out in about two minutes; what it sets going in the cars either side, they put out.
        var r = Run([2, 4], 0.97, 99, seconds: 240);
        Assert.True(r.Out, $"not out ({r.Fires}): {r.Steps}");
        Assert.False(r.InWellAlight, $"went in: {r.Steps}");
        Assert.True(r.Fires.Length == 0, $"still alight: {r.Fires}; {r.Steps}");
        Assert.True(r.Least >= Bots.RoofWalkerBot.FireFightHealth - 15, $"burned to {r.Least}: {r.Steps}");
    }

    [Fact]
    public void AFireIsTwoHandsAtMost()
    {
        // In the order they say so; a hand that stops saying so is off it a second later, and the next takes its place.
        var calls = new Bots.CrewCalls();
        Assert.Equal(1, calls.ClaimFire(3, 7, 100));
        Assert.Equal(2, calls.ClaimFire(3, 8, 100));
        Assert.Equal(0, calls.ClaimFire(3, 9, 100));
        Assert.Equal(1, calls.ClaimFire(3, 7, 101));
        Assert.Equal(1, calls.ClaimFire(4, 9, 101));
        Assert.Equal(1, calls.ClaimFire(3, 7, 102 + SimConstants.TickRate));
        Assert.Equal(2, calls.ClaimFire(3, 9, 102 + SimConstants.TickRate));
    }
}
