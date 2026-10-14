using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 619 (D1.5's case for queue #350): at frontier:12's first stop (seed 1, a switchyard), the two walkers in car 1 put
/// its lamp out for the Fire Flies with the lamp key pressed every other tick, the key a toggle and their view of the lamp a
/// snapshot behind: the presses still in flight lit it again, the flies came back to it every 30 to 40 s, and the walkers
/// (sat at car 1's side door for a mail bag the whole while, the train standing) never went to their parts. The 240 s the
/// driver holds for them ran out and the stop was given up.
/// </summary>
public class LampKeyTests
{
    static PlayerState Inside(Night n, int car) => new()
    {
        Parent = car,
        Position = new Double3(0, Tuning.Train.Geometry.Interior!.FloorHeight, 0),
        Surface = Surface.Deck,
        Health = Tuning.Player.Health,
    };

    [Fact]
    public void AHandPressesAtOnceThenLeavesTheKeyToSeeItsPressComeBack()
    {
        var n = new Night(4, speed: 0);
        var key = new LampKey();
        var self = Inside(n, 2);
        var presses = Enumerable.Range(100, SimConstants.TickRate * 2).Where(t => key.Press(self, n.World, 3, [], (uint)t)).ToList();
        Assert.Equal(100, presses[0]);
        Assert.All(presses.Zip(presses.Skip(1)), p => Assert.True(p.Second - p.First >= LampKey.Settle, $"{p.First} and {p.Second}"));
    }

    [Fact]
    public void TwoHandsInOneCarTheLowerIdPressesFirstAndTheOtherOnlyIfItDoesnt()
    {
        var n = new Night(4, speed: 0);
        var (two, three) = (new LampKey(), new LampKey());
        var self = Inside(n, 2);
        Assert.True(two.Press(self, n.World, 2, [(3, self)], 100));
        Assert.False(three.Press(self, n.World, 3, [(2, self)], 100));
        // Still lit as it sees it a second on (the other's a person, say, not pressing): it presses.
        int? pressed = Enumerable.Range(101, SimConstants.TickRate * 2).FirstOrDefault(t => three.Press(self, n.World, 3, [(2, self)], (uint)t));
        Assert.Equal(100 + (int)LampKey.Defer, pressed);
    }

    [Fact]
    public void FrontierTwelvesFirstStopIsWorkedNotGivenUp()
    {
        var content = DataFile.FindContentRoot();
        const int cars = 10;
        var route = LineGen.Routes.Generate(content, "frontier:12", cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        double? spurAt = null;
        // As `dt harness --route frontier:12 --bots 4 --enemies --upkeep --seed 1` hosts it: the lossy link is the lag.
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 4,
            Cars = cars,
            Seconds = 420,
            // In at the switch (off the main onto the yard's spur, path 0 on): the stop's being worked.
            Until = world => (spurAt ??= world.Train.Dynamics.Path >= 0 ? world.Run?.Seconds : null) is not null,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Upkeep = DataFile.Load<Train.UpkeepTuning>(Path.Combine(content, Train.UpkeepTuning.File)),
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(content, FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        Assert.True(spurAt is not null, $"never in at the switch: {string.Join("; ", (report.Stops ?? []).Select(s => string.Join(", ", s.Legs.Select(l => $"{l.Key} {l.Value:0}"))))}");
    }
}
