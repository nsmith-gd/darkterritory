using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The band (ARCHITECTURE §8 note 700; the director, 13 Oct 2026: "all play together should create some sort of public domain
/// song ... synced up by the first person who starts playing ... selected from a repertoire ... that should all stop once the
/// last person stops playing", and "make sure that they are like Celtic folk style songs ... the East Coast folk aesthetic").
/// </summary>
public class BandTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly BandTuning B = DataFile.Load<BandTuning>(Path.Combine(Content, BandTuning.File));
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void EveryTuneReadsIntoFullBarsWithAChordEach()
    {
        Assert.NotEmpty(B.Scores);
        foreach (var score in B.Scores)
        {
            string t = score.Tune.Title;
            Assert.All(score.BarLengths, (l, i) => Assert.True(Math.Abs(l - score.BarEighths) < 1e-9, $"{t}: bar {i + 1} is {l} eighths"));
            Assert.Equal(score.Bars, score.Chords.Count);
            Assert.NotEmpty(score.Melody);
            // In a fiddle's and a whistle's reach: G below middle C up to the B two octaves over it.
            Assert.All(score.Melody, n => Assert.InRange(n.Pitch, 55, 83));
            // A session's pace: a slow air to a fast reel.
            Assert.InRange(score.Seconds, 20, 120);
            foreach (var instrument in B.Instruments)
                Assert.NotEmpty(score.Part(instrument));
        }
        // Repeats played out: the shanty's verse and chorus, each twice.
        var sailor = B.Scores.Single(s => s.Tune.Title == "Drunken Sailor");
        Assert.Equal(32, sailor.Bars);
        Assert.Equal(B.Scores.Select(s => s.Tune.Title).Distinct().Count(), B.Scores.Count);
    }

    [Fact]
    public void TheKeysAreTheirModesAndAnAccidentalLastsTheBar()
    {
        // A B C D E F G.
        Assert.Equal([0, 0, 0, 0, 0, 0, 0], BandScore.Signature("Ddor"));
        Assert.Equal([0, 0, 1, 0, 0, 1, 0], BandScore.Signature("Edor"));
        Assert.Equal([0, 0, 1, 0, 0, 1, 0], BandScore.Signature("D"));
        Assert.Equal([0, 0, 0, 0, 0, 1, 0], BandScore.Signature("G"));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0], BandScore.Signature("Am"));
        Assert.Equal([0, 0, 1, 0, 0, 1, 0], BandScore.Signature("Amix"));
        var tune = new BandTune("t", "6/8", "D", 120, "F^GF Gc'C, | G3 F3 |", "D D");
        var notes = BandScore.Parse(tune).Melody;
        // F sharp in D; G sharpened for the rest of the bar and not after; C sharp in D, an octave up and down.
        Assert.Equal([66, 68, 66, 68, 85, 49, 67, 66], notes.Select(n => n.Pitch));
        // A jig at 120: a dotted crotchet a half second, so an eighth a sixth.
        Assert.Equal(1.0 / 6, BandScore.Parse(tune).EighthSeconds, 9);
    }

    [Fact]
    public void TheFirstToPlayStartsTheNextTuneAndTheLastToStopStopsIt()
    {
        int next = 0;
        var band = Band.Step(BandState.Silent, 0, B, ref next, 10);
        Assert.False(band.Playing);
        band = Band.Step(band, 1, B, ref next, 11);
        Assert.Equal(new BandState(0, 11), band);
        // Another comes in: the same tune, from the same tick.
        Assert.Equal(band, Band.Step(band, 2, B, ref next, 40));
        Assert.Equal(band, Band.Step(band, 1, B, ref next, 41));
        band = Band.Step(band, 0, B, ref next, 42);
        Assert.False(band.Playing);
        // The next to start begins the tune after.
        band = Band.Step(band, 1, B, ref next, 100);
        Assert.Equal(new BandState(1, 100), band);
        // Played out, the next of the set, on the tick its breath ran out.
        uint end = 100 + B.Ticks(1);
        Assert.Equal(band, Band.Step(band, 1, B, ref next, end - 1));
        Assert.Equal(new BandState(2, end), Band.Step(band, 1, B, ref next, end));
        // No band, no tune.
        int none = 0;
        Assert.False(Band.Step(BandState.Silent, 3, null, ref none, 5).Playing);
    }

    static World Night(params string[] upgrades) => Night(true, upgrades);

    static World Night(bool host, params string[] upgrades)
    {
        var t = Campaign.Campaign.Apply(C, upgrades, new Loadout(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies)).Train;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 1_000), Tuning.Combat);
        world.EnableBand(B);
        if (!host)
            return world;
        world.EnableBodies();
        world.Stock();
        return world;
    }

    [Fact]
    public void TheBlacksmithsInstrumentsRideInTheGuardVan()
    {
        Assert.DoesNotContain(Night().Bodies.All, b => b.Kind == BodyKind.Instrument);
        string[] all = [.. C.Upgrades.Where(u => u.Effect.Keys.Any(k => k.StartsWith("instrument:", StringComparison.Ordinal))).Select(u => u.Id)];
        // One upgrade an instrument, each of band.json's.
        Assert.Equal(B.Instruments.Length, all.Length);
        var w = Night(all);
        var guard = w.Train.Dynamics.Consist.Vehicles.Last(v => v.Kind == VehicleKind.Guard).Id;
        var stocked = w.Bodies.All.Where(b => b.Kind == BodyKind.Instrument).ToList();
        Assert.Equal(Enumerable.Range(0, B.Instruments.Length).Select(i => (byte)i), stocked.Select(b => b.Instrument).Order());
        Assert.All(stocked, b => Assert.Equal(guard, b.Parent));
        // Saved aboard and put back as the same instruments (note 500).
        var copy = Night();
        copy.Restock(w.Aboard());
        Assert.Equal(stocked.Select(b => b.Instrument).Order(), copy.Bodies.All.Where(b => b.Kind == BodyKind.Instrument).Select(b => b.Instrument).Order());
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    static void Tick(World w, (int Id, PlayerIntent Intent)[] intents, PlayerState[] crew)
    {
        w.BeginTick();
        for (int i = 0; i < crew.Length; i++)
            w.CrewAct(ref crew[i], intents[i].Intent, intents[i].Id);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        for (int i = 0; i < crew.Length; i++)
            PlayerMotor.Step(ref crew[i], intents[i].Intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
        w.StepBodies([.. crew.Select((s, i) => (intents[i].Id, s))]);
    }

    [Fact]
    public void HeldUseOnAnInstrumentPlaysInTheFirstPlayersTuneAndATapPutsItDown()
    {
        var w = Night("fiddle", "bodhran");
        var instruments = w.Bodies.All.Where(b => b.Kind == BodyKind.Instrument).OrderBy(b => b.Instrument).ToList();
        var crew = new[] { PlayerMotor.SpawnInCab(w.Train, P) with { HeldSlot = 0 }, PlayerMotor.SpawnInCab(w.Train, P) with { HeldSlot = 0 } };
        instruments[0].Carrier = 1;
        instruments[1].Carrier = 2;
        (int, PlayerIntent)[] Intents(bool one, bool two) => [(1, one ? Use : default), (2, two ? Use : default)];
        // The fiddler holds Use: not yet playing on the press, playing once it's held.
        Tick(w, Intents(true, false), crew);
        Assert.False(crew[0].Has(PlayerFlags.Playing));
        for (int i = 0; i < B.HoldSeconds * SimConstants.TickRate + 1; i++)
            Tick(w, Intents(true, false), crew);
        Assert.True(crew[0].Has(PlayerFlags.Playing));
        Assert.True(w.Band.Playing);
        var tune = w.Band;
        // The drummer comes in later, on the fiddler's tune.
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            Tick(w, Intents(true, true), crew);
        Assert.True(crew[1].Has(PlayerFlags.Playing));
        Assert.Equal(tune, w.Band);
        // The fiddler stops; the drum plays on, the same tune.
        for (int i = 0; i < 10; i++)
            Tick(w, Intents(false, true), crew);
        Assert.False(crew[0].Has(PlayerFlags.Playing));
        Assert.Equal(BodyKind.Instrument, w.Bodies.CarriedBy(1)?.Kind);
        Assert.Equal(tune, w.Band);
        // The last stops: it all stops.
        for (int i = 0; i < 3; i++)
            Tick(w, Intents(false, false), crew);
        Assert.False(w.Band.Playing);
        Assert.Equal(BodyKind.Instrument, w.Bodies.CarriedBy(2)?.Kind);
        // A tap, not a hold, puts it down.
        Tick(w, Intents(true, false), crew);
        Tick(w, Intents(false, false), crew);
        Assert.Null(w.Bodies.CarriedBy(1));
        Assert.False(w.Band.Playing);
        // Picked up again by whoever starts next, the band starts the next tune of the set.
        for (int i = 0; i < B.HoldSeconds * SimConstants.TickRate + 2; i++)
            Tick(w, Intents(false, true), crew);
        Assert.Equal((tune.Tune + 1) % B.Repertoire.Length, w.Band.Tune);
    }

    [Fact]
    public void AnIdleBotJoinsTheBandWithAnInstrumentNearItAndPutsItDownWhenItStops()
    {
        // The director (14 Oct): "We do want bots to participate."
        var w = Night("fiddle");
        var fiddle = Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.Instrument);
        var room = w.Train.Frames[fiddle.Parent].Shape.Interior!.Value;
        // Standing about in the guard van, four metres from it along the floor.
        var s = PlayerMotor.SpawnInCab(w.Train, P) with { Parent = fiddle.Parent, Position = new Double3(0.1, room.Min.Y + 0.05, fiddle.Centre.Z - 4), Surface = Surface.Deck, HeldSlot = 0 };
        const int me = 3;
        void Tick(bool band)
        {
            w.Band = band ? new BandState(0, 1) : BandState.Silent;
            w.BeginTick();
            var intent = Bots.Heed.Band(default, s, w, me);
            w.CrewAct(ref s, intent, me);
            w.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
            var me1 = s;
            w.Bodies.Step(w.Train, w.Train.Dynamics.Tuning, id => id == me ? me1 : null);
        }
        // Quiet: it leaves the fiddle be.
        for (int i = 0; i < 30; i++)
            Tick(band: false);
        Assert.Null(w.Bodies.CarriedBy(me));
        // The band strikes up: it goes to the fiddle, takes it up and plays.
        for (int i = 0; i < 8 * SimConstants.TickRate && !s.Has(PlayerFlags.Playing); i++)
            Tick(band: true);
        Assert.Equal(BodyKind.Instrument, w.Bodies.CarriedBy(me)?.Kind);
        Assert.True(s.Has(PlayerFlags.Playing));
        // It stops: the bot stops, and puts the fiddle down where it is.
        for (int i = 0; i < 10; i++)
            Tick(band: false);
        Assert.False(s.Has(PlayerFlags.Playing));
        Assert.Null(w.Bodies.CarriedBy(me));
    }

    [Fact]
    public void WhatTheBandsPlayingIsEveryClients()
    {
        // Protocol 59: the world record carries the tune and its first tick; the body record which instrument it is.
        var w = Night("accordion");
        w.Band = new BandState(3, 4567);
        var controls = new TrainControls();
        var records = Net.WorldRecords.Capture(w, controls, []);
        var client = Night(host: false);
        Net.WorldRecords.Apply(records, client, ref controls, []);
        Assert.Equal(new BandState(3, 4567), client.Band);
        Assert.Equal((byte)B.IndexOf("accordion"), Assert.Single(client.Bodies.All, b => b.Kind == BodyKind.Instrument).Instrument);
        w.Band = BandState.Silent;
        Net.WorldRecords.Apply(Net.WorldRecords.Capture(w, controls, []), client, ref controls, []);
        Assert.False(client.Band.Playing);
    }
}
