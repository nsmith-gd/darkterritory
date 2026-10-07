using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The yard's fun (GDD §9: "the crew wait for friends, hang out, dance, try on outfits"; note 298): an emote is intent the
/// host turns into an event the crew see, and an outfit is said in the Hello and tried on in the yard. Neither moves any
/// player or train state.
/// </summary>
public class EmoteAndOutfitTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600);

    static void Run(LoopbackNetwork net, HostSession host, ClientSession[] clients, double seconds, Func<int, int, PlayerIntent>? intent = null)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            for (int i = 0; i < clients.Length; i++)
                clients[i].Step(intent?.Invoke(i, t) ?? default);
        }
    }

    [Fact]
    public void AnEmoteRidesTheIntentsHotbarByteBothWays()
    {
        foreach (var e in Enum.GetValues<Emote>())
        {
            var w = new NetWriter();
            Messages.WriteInput(w, [new InputFrame(7, new PlayerIntent { MoveZ = 1, Emote = e, Select = e == Emote.Wave ? (byte)3 : (byte)0 })], 0);
            var r = new NetReader(w.Written);
            r.U8();
            var frames = new List<InputFrame>();
            Messages.ReadInput(ref r, frames, out _);
            Assert.Equal(e, frames[0].Intent.Emote);
            Assert.Equal(e == Emote.Wave ? 3 : 0, frames[0].Intent.Select);
        }
    }

    [Fact]
    public void AnEmoteIsSeenByTheCrewForItsLengthAndThenGoes()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var clients = new[] { new ClientSession(net.CreateClient(), Train(), T, P), new ClientSession(net.CreateClient(), Train(), T, P) };
        Run(net, host, clients, 1);
        int dancer = clients[0].PlayerId!.Value;
        // One tick's dance, picked on the wheel.
        Run(net, host, clients, 0.5, (i, t) => new PlayerIntent { Emote = i == 0 && t == 0 ? Emote.Dance : Emote.None });
        var seen = Assert.Single(host.World.Emotes);
        Assert.Equal((dancer, Emote.Dance), (seen.By, seen.Kind));
        // The other client has it, from the snapshot.
        Assert.Equal(host.World.Emotes, clients[1].World.Emotes);
        // Kept for player.json's dance seconds, then gone everywhere.
        Run(net, host, clients, P.Emotes.Dance);
        Assert.Empty(host.World.Emotes);
        Assert.Empty(clients[1].World.Emotes);
    }

    [Fact]
    public void ANewEmoteTakesTheLastOnesPlaceButNotOnTopOfIt()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var clients = new[] { new ClientSession(net.CreateClient(), Train(), T, P) };
        Run(net, host, clients, 1);
        // A wave, then a point inside the cooldown (not taken), then one after it (taking the wave's place).
        int after = (int)Math.Ceiling(P.Emotes.Cooldown * SimConstants.TickRate) + 2;
        Run(net, host, clients, 1.5, (_, t) => new PlayerIntent { Emote = t == 0 ? Emote.Wave : t == 2 ? Emote.Point : t == after ? Emote.Point : Emote.None });
        var now = Assert.Single(host.World.Emotes);
        Assert.Equal(Emote.Point, now.Kind);
    }

    [Fact]
    public void AnEmoteMovesNothing()
    {
        // The same night with and without a dance: every player and the train end the same.
        PlayerState[] Night(bool dance)
        {
            var net = new LoopbackNetwork();
            var host = new HostSession(net.CreateHost(), Train(), T, P);
            var clients = new[] { new ClientSession(net.CreateClient(), Train(), T, P), new ClientSession(net.CreateClient(), Train(), T, P) };
            Run(net, host, clients, 3, (i, t) => new PlayerIntent { MoveZ = i == 1 ? 1 : 0, Emote = dance && t % 30 == 0 ? Emote.Dance : Emote.None });
            return [.. host.Players.Select(p => p.State)];
        }
        Assert.Equal(Night(false), Night(true));
    }

    [Fact]
    public void AnOutfitComesInTheHelloAndEveryoneHearsOfIt()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var clients = new[]
        {
            new ClientSession(net.CreateClient(), Train(), T, P) { Name = "Dave", Outfit = 5 },
            new ClientSession(net.CreateClient(), Train(), T, P) { Name = "Priya" },
        };
        Run(net, host, clients, 1);
        int dave = clients[0].PlayerId!.Value, priya = clients[1].PlayerId!.Value;
        Assert.Equal(5, host.World.OutfitOf(dave));
        // No outfit chosen: their id's look, as before.
        Assert.Equal(priya, host.World.OutfitOf(priya));
        Assert.Equal(5, clients[1].World.OutfitOf(dave));
    }

    [Fact]
    public void AnOutfitIsTriedOnInTheYardAndNotPastTheGate()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        // Stood in the fortress yard (the gates are run.json's yard length on), the brake on.
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 300, Tuning.Boiler), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), world, T, P);
        var client = new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 3_000, Tuning.Boiler), T, P);
        Run(net, host, [client], 1);
        int me = client.PlayerId!.Value;
        Assert.Equal(Sim.Run.RunPhase.Yard, world.Run!.Phase);
        client.Wear(2);
        Run(net, host, [client], 0.5);
        Assert.Equal(2, host.World.OutfitOf(me));
        Assert.Equal(2, client.World.OutfitOf(me));
        // Under way, it's kept to the next Hello, but the crew go on seeing the one put on in the yard.
        world.Run.Resume(900, -1, world.Train.Boiler.Tender, 0);
        Assert.NotEqual(Sim.Run.RunPhase.Yard, world.Run.Phase);
        client.Wear(6);
        Run(net, host, [client], 0.5);
        Assert.Equal(2, host.World.OutfitOf(me));
        Assert.Equal(6, client.Outfit);
    }
}
