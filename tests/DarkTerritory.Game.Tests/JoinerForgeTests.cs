using System.Net;
using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A joiner buys at the forge (queue #752, ARCHITECTURE §8 note 1054; GDD §3.1, §11): a joiner's choice at the blacksmith goes
/// to the host as an order (intent, never state, REVIEW.md N1); the host buys it from the crew's scrip, once, or refuses, and
/// the joiner is shown the words the host's own menu would say; shut, the yard's hosted again fitted, on every machine.
/// </summary>
public class JoinerForgeTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    const string Tender = "tenderCapacity";

    /// <summary>A campaign night hosted as the app hosts one, and the host's own wallet, bought from as the app's forge buys.</summary>
    sealed class Crew(CampaignState state)
    {
        public CampaignState State = state;
        public int Bought;
        public Purchase Buy(string id)
        {
            var p = Campaign.BuyUpgrade(C, State, id);
            if (p.Ok)
            {
                State = p.State;
                Bought++;
            }
            return p;
        }
    }

    static void Run(NetPlaySession host, double seconds, params NetPlaySession[] joiners)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            host.Step(default);
            foreach (var j in joiners)
                j.Step(default);
        }
    }

    static (Crew Crew, SessionSetup Setup) Campaigning(double scrip)
    {
        var s = Campaign.New(C, 1, "Forge Crew", 9) with { Scrip = scrip };
        return (new Crew(s), HostedAsApp.Campaign(s, new Contract(RouteTier.Frontier, 7, 450), enemies: false));
    }

    static double Cost(string id) => Campaign.UpgradeCost(C, Campaign.New(C, 1, "", 9), C.Upgrades.First(u => u.Id == id));

    [Fact]
    public void AJoinersOrderIsBoughtByTheHostChargedOnceAndFittedOnBothMachines()
    {
        double cost = Cost(Tender);
        var (crew, setup) = Campaigning(cost * 1.5);
        var first = NetPlaySession.HostGame(Content, setup, port: 0);
        int port = first.Port;
        using var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, port), () => first.Step(default));
        Run(first, 0.5, joiner);

        // The joiner's shop, as the app opens it: the host's setup's scrip, and its choice an order.
        string dir = Path.Combine(Path.GetTempPath(), $"dt-forge-{Guid.NewGuid():N}");
        var m = new FrontEnd(C, DataFile.Load<Sim.Run.RunTuning>(Path.Combine(Content, Sim.Run.RunTuning.File)), new SaveSlots(Path.Combine(dir, "saves"), C.SaveSlots),
            Path.Combine(dir, "settings.json"), () => 42)
        {
            SmithyOrder = joiner.Order,
            SmithyDone = joiner.OrderDone,
        };
        var stock = Campaign.New(C, 0, "", 1) with { Cars = joiner.Setup.Cars, Scrip = joiner.Setup.Scrip, Upgrades = joiner.Setup.Upgrades };
        m.OpenSmithy(new NightMenu(Hosting: false, Yard: true), new SmithyShop("Archie MacNeil", stock, CanBuy: true, "the host"));
        string label = C.Upgrades.First(u => u.Id == Tender).Name.ToUpperInvariant();
        Choose(m, label);
        Assert.Contains("ordered", m.Message);

        // The host buys it, from the crew's scrip, however many frames it serves orders over.
        bool refit = false;
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            Run(first, 1.0 / SimConstants.TickRate, joiner);
            refit |= first.ServeOrders(C, () => crew.State, crew.Buy);
            while (joiner.TakeOrderAnswer() is { } a)
                m.SmithyAnswered(a);
        }
        Assert.False(refit);
        Assert.Equal(1, crew.Bought);
        Assert.Equal(cost * 1.5 - cost, crew.State.Scrip, 6);
        Assert.Contains(Tender, crew.State.Upgrades);
        // The joiner's shown the host's own words, and its shelves as the host now has them.
        Assert.Equal(FrontEnd.SmithyWords("Tender capacity", false, new Purchase(crew.State, null), Tender), m.Message);
        Assert.Equal(crew.State.Scrip, m.Smithy!.State.Scrip, 6);
        Assert.Contains(Tender, m.Smithy.State.Upgrades);

        // Shut, it tells the host it's done: the yard's hosted again, fitted (note 697's refit), and the joiner follows it.
        m.CloseNight();
        for (int t = 0; t < SimConstants.TickRate && !refit; t++)
        {
            Run(first, 1.0 / SimConstants.TickRate, joiner);
            refit |= first.ServeOrders(C, () => crew.State, crew.Buy);
        }
        Assert.True(refit);
        Assert.Equal(1, crew.Bought);
        first.CarryOn(TownAt.Split(first.Setup.Route ?? "").From ?? new TownAt("the fortress", "", 0));
        Run(first, 0.5, joiner);
        first.Dispose();
        for (int t = 0; t < SimConstants.TickRate * 30 && !joiner.Lost; t++)
        {
            joiner.Step(default);
            Thread.Sleep(1);
        }
        var going = Assert.IsType<NetPlaySession.Following>(joiner.GoingOn);
        Assert.True(going.Refit);
        using var refitted = NetPlaySession.HostGame(Content, setup with { Upgrades = crew.State.Upgrades, Scrip = crew.State.Scrip }, port: port);
        var quick = P with { Rejoin = P.Rejoin with { RetrySeconds = 0.1, FollowSeconds = 60 } };
        using var followed = NetPlaySession.Follow(Content, going, () => refitted.Step(default), quick);
        Run(refitted, 0.5, followed);
        Assert.Contains(Tender, followed.Setup.Upgrades);
        var boiler = DataFile.Load<Sim.Train.BoilerTuning>(Path.Combine(Content, Sim.Train.BoilerTuning.File));
        Assert.Equal(boiler.TenderCapacity * 1.25, refitted.Host!.Train.BoilerTuning!.TenderCapacity, 0);
        Assert.Equal(refitted.Host.Train.BoilerTuning.TenderCapacity, followed.Train.BoilerTuning!.TenderCapacity);
    }

    [Fact]
    public void ARefusedOrderCostsNothingAndSaysWhatTheHostWouldSee()
    {
        double cost = Cost(Tender);
        var (crew, setup) = Campaigning(cost / 2);
        using var host = NetPlaySession.HostGame(Content, setup, port: 0);
        using var joiner = NetPlaySession.Join(Content, new IPEndPoint(IPAddress.Loopback, host.Port), () => host.Step(default));
        Run(host, 0.5, joiner);
        double before = crew.State.Scrip;
        var hostWouldSee = FrontEnd.SmithyWords("Tender capacity", false, Campaign.BuyUpgrade(C, crew.State, Tender), Tender);

        joiner.Order(Tender);
        joiner.OrderDone();
        OrderAnswer? answer = null;
        bool refit = false;
        for (int t = 0; t < SimConstants.TickRate && answer is null; t++)
        {
            Run(host, 1.0 / SimConstants.TickRate, joiner);
            refit |= host.ServeOrders(C, () => crew.State, crew.Buy);
            answer = joiner.TakeOrderAnswer();
        }
        Run(host, 0.2, joiner);
        refit |= host.ServeOrders(C, () => crew.State, crew.Buy);
        var a = Assert.IsType<OrderAnswer>(answer);
        Assert.False(a.Ok);
        Assert.Equal(hostWouldSee, a.Words);
        Assert.Contains("costs", a.Words);
        Assert.Equal(before, a.Scrip);
        Assert.False(a.Owned);
        Assert.Equal(before, crew.State.Scrip);
        Assert.Equal(0, crew.Bought);
        // Nothing bought, nothing to fit: the yard isn't hosted again for it.
        Assert.False(refit);

        // Owned already: the same, in its own words.
        crew.State = crew.State with { Upgrades = [Tender], Scrip = cost * 3 };
        joiner.Order(Tender);
        answer = null;
        for (int t = 0; t < SimConstants.TickRate && answer is null; t++)
        {
            Run(host, 1.0 / SimConstants.TickRate, joiner);
            host.ServeOrders(C, () => crew.State, crew.Buy);
            answer = joiner.TakeOrderAnswer();
        }
        Assert.Equal(Campaign.BuyUpgrade(C, crew.State, Tender).Refused, answer!.Words);
        Assert.Equal(cost * 3, crew.State.Scrip);

        // A night that isn't a campaign's: there's no crew's scrip, and it's said.
        joiner.Order(Tender);
        answer = null;
        for (int t = 0; t < SimConstants.TickRate && answer is null; t++)
        {
            Run(host, 1.0 / SimConstants.TickRate, joiner);
            host.ServeOrders(C, () => null, null);
            answer = joiner.TakeOrderAnswer();
        }
        Assert.Equal(NetPlaySession.NoPurse, answer!.Words);
    }

    [Fact]
    public void TwoJoinersOrderingTheLastOfSomethingAtOnceOneGetsIt()
    {
        // Enough scrip for one: both order it in the same tick. The host buys in the order they came: one is fitted, the
        // other's told the crew has it already, and the crew's charged once.
        double cost = Cost(Tender);
        var (crew, setup) = Campaigning(cost * 1.2);
        using var host = NetPlaySession.HostGame(Content, setup, port: 0);
        var at = new IPEndPoint(IPAddress.Loopback, host.Port);
        using var a = NetPlaySession.Join(Content, at, () => host.Step(default));
        using var b = NetPlaySession.Join(Content, at, () => host.Step(default));
        Run(host, 0.5, a, b);
        a.Order(Tender);
        b.Order(Tender);
        OrderAnswer? toA = null, toB = null;
        for (int t = 0; t < SimConstants.TickRate * 2 && (toA is null || toB is null); t++)
        {
            Run(host, 1.0 / SimConstants.TickRate, a, b);
            host.ServeOrders(C, () => crew.State, crew.Buy);
            toA ??= a.TakeOrderAnswer();
            toB ??= b.TakeOrderAnswer();
        }
        Assert.NotNull(toA);
        Assert.NotNull(toB);
        Assert.Equal(1, new[] { toA!, toB! }.Count(x => x.Ok));
        var refused = toA!.Ok ? toB! : toA;
        Assert.Equal(Campaign.BuyUpgrade(C, crew.State, Tender).Refused, refused.Words);
        Assert.Equal(1, crew.Bought);
        Assert.Equal(cost * 1.2 - cost, crew.State.Scrip, 6);
        Assert.Equal(crew.State.Scrip, refused.Scrip, 6);
    }

    [Fact]
    public void TheOrderAndItsAnswerReadBackAsWritten()
    {
        var w = new Ballast.Net.NetWriter();
        Messages.WriteOrder(w, Tender, done: false);
        var r = new Ballast.Net.NetReader(w.Written.ToArray());
        Assert.Equal((byte)MessageType.Order, r.U8());
        Assert.Equal(new ForgeOrder(3, Tender, false), Messages.ReadOrder(ref r, 3));
        Messages.WriteOrder(w, Tender, done: true);
        r = new Ballast.Net.NetReader(w.Written.ToArray());
        r.U8();
        Assert.Equal(new ForgeOrder(4, "", true), Messages.ReadOrder(ref r, 4));
        var answer = new OrderAnswer(Tender, true, "Tender capacity: fitted before the gate.", 1234.5, true);
        Messages.WriteOrderAnswer(w, answer);
        r = new Ballast.Net.NetReader(w.Written.ToArray());
        Assert.Equal((byte)MessageType.OrderAnswer, r.U8());
        Assert.Equal(answer, Messages.ReadOrderAnswer(ref r));
        Assert.Equal(0, r.Remaining);
        // Protocol 94 (REVIEW.md N3): the order and its answer are new messages.
        Assert.Equal(94, Protocol.Version);
    }

    static Launch? Choose(FrontEnd m, string label)
    {
        int i = m.Items.ToList().FindIndex(x => !x.Heading && x.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.True(i >= 0, string.Join(" | ", m.Items.Select(x => x.Label)));
        Assert.True(m.Items[i].Enabled, $"'{label}' is greyed");
        while (m.Selected != i)
            m.Down();
        return m.Select();
    }
}
