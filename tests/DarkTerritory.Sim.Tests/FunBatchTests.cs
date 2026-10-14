using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The fun batch (ARCHITECTURE §8 note 714; docs/design/upgrades.md §4): the liveries (bought once, painted in again for
/// nothing), the nameplate, the hat rack, more emotes and the yard photographer. Each is on the train once bought, and the
/// rack's hats and the new emotes are the host's only when the train has them.
/// </summary>
public class FunBatchTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));

    static readonly string[] Fun = ["nameplate", "hatRack", "moreEmotes", "yardPhotographer"];

    static Loadout With(params string[] upgrades) => Campaign.Campaign.Apply(C, upgrades, Base);

    static TrainTuning Dressed(params string[] dress) => T with { Composition = T.Composition with { Dress = dress } };

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
    public void EachIsOnTheTrainOnceBoughtWithItsLineAndTally()
    {
        foreach (var id in Fun)
        {
            var u = Assert.Single(C.Upgrades, u => u.Id == id);
            Assert.Equal(UpgradeKind.Fun, u.Kind);
            Assert.NotNull(u.Line);
            Assert.NotNull(u.Tally);
            Assert.Equal([id], With(id).Train.Composition.Dress);
        }
        Assert.Contains("{engine}", C.Upgrades.Single(u => u.Id == "nameplate").Tally);
    }

    [Fact]
    public void ALiveryBoughtOnceIsPaintedInAgainForNothing()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 7) with { Scrip = 100_000 };
        Assert.Null(Campaign.Campaign.LiveryOf(C, s));
        Assert.Equal(0, With().Train.Composition.Livery);
        s = Campaign.Campaign.BuyUpgrade(C, s, "liveryCrimson").State;
        s = Campaign.Campaign.BuyUpgrade(C, s, "liveryForest").State;
        Assert.Equal("liveryForest", Campaign.Campaign.LiveryOf(C, s));
        Assert.Equal(2, Campaign.Campaign.Apply(C, s.Upgrades, Base).Train.Composition.Livery);
        // Crimson again: no scrip, the list reordered so Apply paints her in it.
        double scrip = s.Scrip;
        var again = Campaign.Campaign.BuyUpgrade(C, s, "liveryCrimson");
        Assert.Null(again.Refused);
        Assert.Equal(scrip, again.State.Scrip);
        Assert.Equal("liveryCrimson", Campaign.Campaign.LiveryOf(C, again.State));
        Assert.Equal(1, Campaign.Campaign.Apply(C, again.State.Upgrades, Base).Train.Composition.Livery);
        Assert.Equal(s.Upgrades.Count, again.State.Upgrades.Count);
        // And the one she's in, chosen again, is said so.
        Assert.NotNull(Campaign.Campaign.BuyUpgrade(C, again.State, "liveryCrimson").Refused);
    }

    [Fact]
    public void AnOutfitByteCarriesAColourAndAHat()
    {
        for (int hat = 0; hat <= 4; hat++)
            for (int colour = -1; colour < Outfit.Own; colour++)
            {
                byte b = Outfit.Encode(colour, hat);
                Assert.True(b < 64);
                Assert.Equal(hat, Outfit.HatOf(b));
                Assert.Equal(colour < 0 ? null : colour, Outfit.ColourOf(b));
            }
        // A colour alone is the byte it always was.
        Assert.Equal(5, Outfit.Encode(5, 0));
    }

    [Fact]
    public void AHatIsWornOnlyWhenTheTrainHasTheRack()
    {
        foreach (bool rack in new[] { false, true })
        {
            var tuning = rack ? Dressed("hatRack") : T;
            var net = new LoopbackNetwork();
            var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), TestLoop, 600), tuning, P);
            var client = new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), TestLoop, 600), tuning, P);
            Run(net, host, [client], 1);
            int me = client.PlayerId!.Value;
            client.Wear(Outfit.Encode(2, 3));
            Run(net, host, [client], 0.5);
            // The colour either way; the fez only off the rack.
            Assert.Equal(2, host.World.OutfitOf(me));
            Assert.Equal(rack ? 3 : 0, host.World.HatOf(me));
            Assert.Equal(rack ? 3 : 0, client.World.HatOf(me));
        }
    }

    [Fact]
    public void EveryEmoteRidesWithEveryHotbarSlot()
    {
        foreach (var e in Enum.GetValues<Emote>())
            for (byte select = 0; select < 8; select++)
            {
                var w = new NetWriter();
                Messages.WriteInput(w, [new InputFrame(7, new PlayerIntent { MoveZ = 1, Emote = e, Select = select })], 0);
                var r = new NetReader(w.Written);
                r.U8();
                var frames = new List<InputFrame>();
                Messages.ReadInput(ref r, frames, out _);
                Assert.Equal((e, select), (frames[0].Intent.Emote, frames[0].Intent.Select));
            }
    }

    [Fact]
    public void TheBowTheSaluteAndTheCongaAreTheUpgradesOnly()
    {
        foreach (bool more in new[] { false, true })
            foreach (var e in new[] { Emote.Bow, Emote.Salute, Emote.Conga })
            {
                var tuning = more ? Dressed("moreEmotes") : T;
                var net = new LoopbackNetwork();
                var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), TestLoop, 600), tuning, P);
                var clients = new[] { new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), TestLoop, 600), tuning, P) };
                Run(net, host, clients, 1);
                Run(net, host, clients, 0.3, (_, t) => new PlayerIntent { Emote = t == 0 ? e : Emote.None });
                if (more)
                    Assert.Equal(e, Assert.Single(host.World.Emotes).Kind);
                else
                    Assert.Empty(host.World.Emotes);
            }
    }
}
