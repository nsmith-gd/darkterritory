using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T108 (playtest: "I don't have a bludgeoning weapon in my inventory"): a hotbar of the tools on the train (GDD §1322:
/// shovel, wrench, crowbar), picked by number key or wheel, and everyone starts the night, and every respawn, with one.
/// </summary>
public class KitTests
{
    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);

    [Fact]
    public void EveryoneStartsWithACrowbarInHand()
    {
        var train = Train();
        var p = Tuning.Player;
        foreach (var s in new[] { PlayerMotor.SpawnInCab(train, p), PlayerMotor.SpawnOnRoof(train, 2, 0, p), PlayerMotor.SpawnOnGround(train.Line.Sample(RailLine.MainPath, 480).Position + new Double3(3, 0, 0), train.Line, 480, p) })
        {
            Assert.Equal(Tool.Crowbar, Kit.Held(s));
            Assert.Equal(Kit.Of([Tool.Crowbar]), s.Kit);
        }
    }

    [Fact]
    public void ANumberKeyPicksItsSlotAndTheWheelStepsThroughTheTools()
    {
        var s = new PlayerState { Kit = Kit.Of([Tool.Crowbar, Tool.Shovel]) };
        Kit.TryAdd(ref s.Kit, Tool.Wrench);
        Assert.Equal(Tool.Wrench, Kit.At(s.Kit, 2));
        Kit.Select(ref s, new PlayerIntent { Select = 2 });
        Assert.Equal(Tool.Shovel, Kit.Held(s));
        // An empty slot by its key: hands free.
        Kit.Select(ref s, new PlayerIntent { Select = 6 });
        Assert.Equal(Tool.None, Kit.Held(s));
        // The wheel skips empty slots, both ways, round the end.
        Kit.Select(ref s, new PlayerIntent { Cycle = 1 });
        Assert.Equal(Tool.Crowbar, Kit.Held(s));
        Kit.Select(ref s, new PlayerIntent { Cycle = -1 });
        Assert.Equal(Tool.Wrench, Kit.Held(s));
        Kit.Select(ref s, new PlayerIntent { Cycle = 1 });
        Assert.Equal(Tool.Crowbar, Kit.Held(s));
    }

    [Fact]
    public void AToolIsAWholeBlowAndAFistAFractionOfOne()
    {
        // Note 275: each tool its own blow (MeleeTests); every one of them more than a fist.
        var melee = Tuning.Enemies.Melee;
        foreach (var tool in new[] { Tool.Crowbar, Tool.Shovel, Tool.Wrench })
            Assert.True(melee.Blow(Tool.None) < melee.Blow(tool));
    }

    [Fact]
    public void AHotbarChoiceCrossesTheWireOnlyOnTheTickItsMade()
    {
        var w = new NetWriter();
        var plain = new PlayerIntent { MoveZ = 1, Actions = PlayerActions.Swing };
        Messages.WriteInput(w, [new InputFrame(7, plain)], 3);
        int bare = w.Length;
        foreach (var chosen in new[] { plain with { Select = 3 }, plain with { Cycle = -1 }, plain with { Cycle = 1, Select = 8 } })
        {
            Messages.WriteInput(w, [new InputFrame(7, chosen)], 3);
            Assert.Equal(bare + 1, w.Length);
            var r = new NetReader(w.Written);
            r.U8();
            var read = new List<InputFrame>();
            Messages.ReadInput(ref r, read, out _);
            Assert.Equal(chosen, read[0].Intent);
        }
    }

    [Fact]
    public void TheKitAndTheToolInHandReplicate()
    {
        var train = Train();
        var world = new World(train);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player);
        Kit.TryAdd(ref s.Kit, Tool.Wrench);
        s.HeldSlot = 1;
        var controls = new TrainControls();
        var players = new List<PlayerSnapshot>();
        WorldRecords.Apply(WorldRecords.Capture(world, controls, [new PlayerSnapshot(1, s)]), new World(Train()), ref controls, players);
        Assert.Equal(s.Kit, players[0].State.Kit);
        Assert.Equal(Tool.Wrench, Kit.Held(players[0].State));
    }
}
