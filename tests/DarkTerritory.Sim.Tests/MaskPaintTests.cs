using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Painted masks (GDD §29; note 731): a player's mask paint is said in the Hello and tried on in the yard, in bytes of its
/// own beside the outfit's, and everyone hears of it; the crew's sigil is kept in the campaign save.
/// </summary>
public class MaskPaintTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600);

    static void Run(LoopbackNetwork net, HostSession host, ClientSession[] clients, double seconds)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
    }

    [Fact]
    public void ThePaintRidesTheHelloAndItsOwnMessagesApartFromTheOutfit()
    {
        var w = new NetWriter();
        var paint = new MaskPaint(3, 11, 1);
        // In the Hello, after the outfit and the flags, and before a private run's key.
        var key = Messages.PasswordKey("lantern");
        Messages.WriteHello(w, "Dave", 42, outfit: 5, key: key, paint: paint);
        var r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Hello, r.U8());
        var hello = Messages.ReadHello(ref r);
        Assert.Equal((byte)5, hello.Outfit);
        Assert.Equal(paint, hello.Paint);
        Assert.Equal(key, hello.Key);
        // None, without a key: unpainted.
        Messages.WriteHello(w, "Priya");
        r = new NetReader(w.Written);
        r.U8();
        Assert.Equal(MaskPaint.None, Messages.ReadHello(ref r).Paint);
        // A client's Paint: its type and three bytes (the outfit's Wear stays two).
        Messages.WritePaint(w, paint);
        Assert.Equal(4, w.Written.Length);
        r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Paint, r.U8());
        Assert.Equal(paint, Messages.ReadPaint(ref r));
        Messages.WriteWear(w, 5);
        Assert.Equal(2, w.Written.Length);
        // The host's Paints, by id.
        Messages.WritePaints(w, new Dictionary<int, MaskPaint> { [2] = paint, [0] = new(1, 0, 0) });
        r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Paints, r.U8());
        var into = new Dictionary<int, MaskPaint>();
        Messages.ReadPaints(ref r, into);
        Assert.Equal(paint, into[2]);
        Assert.Equal(new MaskPaint(1, 0, 0), into[0]);
        Assert.Equal(83, Protocol.Version);
    }

    [Fact]
    public void APaintComesInTheHelloAndEveryoneHearsOfIt()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        var clients = new[]
        {
            new ClientSession(net.CreateClient(), Train(), T, P) { Name = "Dave", Outfit = 5, Paint = new MaskPaint(3, 4, 1) },
            new ClientSession(net.CreateClient(), Train(), T, P) { Name = "Priya" },
        };
        Run(net, host, clients, 1);
        int dave = clients[0].PlayerId!.Value, priya = clients[1].PlayerId!.Value;
        Assert.Equal(new MaskPaint(3, 4, 1), host.World.PaintOf(dave));
        Assert.Equal(new MaskPaint(3, 4, 1), clients[1].World.PaintOf(dave));
        // The outfit's untouched by it, and the paint by the outfit.
        Assert.Equal(5, clients[1].World.OutfitOf(dave));
        // Unpainted: none, and not in the map at all.
        Assert.Equal(MaskPaint.None, clients[0].World.PaintOf(priya));
        Assert.False(host.World.Paints.ContainsKey(priya));
    }

    [Fact]
    public void APaintIsTriedOnInTheYardAndHeldPastTheGate()
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 300, Tuning.Boiler), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), world, T, P);
        var client = new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 300, Tuning.Boiler), T, P);
        Run(net, host, [client], 1);
        int me = client.PlayerId!.Value;
        Assert.Equal(Sim.Run.RunPhase.Yard, world.Run!.Phase);
        client.PaintMask(new MaskPaint(7, 5, 1));
        Run(net, host, [client], 0.5);
        Assert.Equal(new MaskPaint(7, 5, 1), host.World.PaintOf(me));
        Assert.Equal(new MaskPaint(7, 5, 1), client.World.PaintOf(me));
        // A part past what the host takes is none.
        client.PaintMask(new MaskPaint(7, 200, 1));
        Run(net, host, [client], 0.5);
        Assert.Equal(new MaskPaint(7, 0, 1), client.World.PaintOf(me));
        client.PaintMask(new MaskPaint(7, 5, 1));
        Run(net, host, [client], 0.5);
        // Past the gate the crew go on seeing the paint put on in the yard; the new one's kept for the next Hello.
        world.Run.Resume(900, -1, world.Train.Boiler.Tender, 0);
        Assert.NotEqual(Sim.Run.RunPhase.Yard, world.Run.Phase);
        client.PaintMask(new MaskPaint(2, 11, 1));
        Run(net, host, [client], 0.5);
        Assert.Equal(new MaskPaint(7, 5, 1), host.World.PaintOf(me));
        Assert.Equal(new MaskPaint(7, 5, 1), client.World.PaintOf(me));
        Assert.Equal(new MaskPaint(2, 11, 1), client.Paint);
    }

    [Fact]
    public void TheCrewsSigilIsKeptInTheCampaignSave()
    {
        var s = new CampaignState { Name = "Owls", Sigil = 12 };
        var back = System.Text.Json.JsonSerializer.Deserialize<CampaignState>(System.Text.Json.JsonSerializer.Serialize(s, DataFile.Options), DataFile.Options)!;
        Assert.Equal(12, back.Sigil);
        // An older save, written before it: none.
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<CampaignState>("{\"name\":\"Owls\"}", DataFile.Options)!.Sigil);
    }
}
