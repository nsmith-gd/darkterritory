using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A walker out of the armoured engine (note 338's hood: the cab at the front, the boiler cased behind it and plated across
/// the cab's back). A 45-minute bot night on 8 Oct had a walker stood against that plating from the cab all night, with car
/// fires down the train it was meant to answer: its way out is the corridor beside the boiler, the footplate onto the coupler
/// plate, the engine's end ladder onto the hood's roof, and the gap jumped to car 1.
/// </summary>
public class EngineWayOutTests
{
    [Fact]
    public void AWalkerInTheCabGetsOutOntoTheCarRoofs()
    {
        var n = new Night(4, 12);
        n.Crew[3] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        var walker = new RoofWalkerBot(1, Tuning.Player.Cold) { Me = 3 };
        double? out_ = null;
        for (int s = 0; s < 30 && out_ is null; s++)
        {
            n.Run(1, id => walker.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            if (n.Crew[3] is { Parent: >= 1, Surface: Surface.Roof })
                out_ = s + 1;
        }
        var c = n.Crew[3];
        Assert.True(out_ is < 20, $"still on {c.Parent}/{c.Surface} at {c.Position}");
        Assert.True(c.Alive);
    }
}
