using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// U1's upgrades heard on AU1's banks (#967): the whimsies, pets and new emotes (queue #534, note 806; notes 714, 715, 745), each
/// of #967's moments playing the installed cue in place of U1's made sound; the horns and the BBQ deck (queue #505, note 775;
/// notes 678, 698), which #967 left silent.
/// </summary>
public class WhimsySoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(
        DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)),
        DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File)),
        DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File)),
        DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)));
    static readonly BandTuning Band = DataFile.Load<BandTuning>(Path.Combine(Content, BandTuning.File));
    static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    /// <summary>A standing train dressed with <paramref name="ids"/>, its bodies and band on, and audio with only <paramref name="cues"/> installed (loops marked ~).</summary>
    static (World World, GameAudio Audio) Night(string[] ids, params string[] cues)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = Campaign.Apply(C, ids, Base).Train;
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(train, 5, 1)), line, 2_000), Base.Combat);
        world.EnableBodies();
        world.EnableBand(Band);
        world.Stock();
        var audio = new GameAudio(Content) { PlayerTuning = Player };
        foreach (var c in cues)
            audio.Bank.Add(c.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Loop: c.EndsWith('~'), Duration: 0.6,
                MaxInstances: 16, MaxDistance: 600));
        return (world, audio);
    }

    sealed class Ears(GameAudio audio, World world)
    {
        static readonly int Blocks = (int)Math.Ceiling(Audio.SampleRate * SimConstants.TickSeconds / Audio.Block);
        readonly HashSet<int> _seen = [];
        public readonly List<SoundInstance> Started = [];

        /// <summary>The state as the host's record has it, set after the sim's own step (which works the canary's mood out afresh).</summary>
        public Action<World>? Host;

        public void Tick(Double3 ear, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.BeginTick();
                world.Step(new TrainControls { Reverser = 1, Brake = 1 });
                world.Tick++;
                Host?.Invoke(world);
                audio.Update(world, new TrainControls { Reverser = 1, Brake = 1 }, Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
                // A whole tick's worth, so the voices' own time keeps up with the game's (a phrase ends when it should).
                for (int b = 0; b < Blocks; b++)
                    audio.Mixer.Render(new float[Audio.Block * 2]);
                Started.AddRange(audio.Mixer.Voices.Where(x => _seen.Add(x.Id)));
            }
        }

        public int Count(string name) => Started.Count(v => v.Name == name);
    }

    static Double3 Over(World w, int car) => w.Train.Frames[car].ToWorld(new Double3(0, 2.5, 0));

    [Fact]
    public void TheCatHissesAndTheDogBarksAtSomethingAboardOnAU1sBanks()
    {
        // #967's pets warn with their mood (note 745): the cat's hiss and the dog's bark, AU1's where installed, in place of
        // U1's made ones; asleep, the cat says nothing.
        var (world, audio) = Night(["yardCat", "dog"], "up-pets.hiss", "up-pets.meow", "up-pets.bark");
        var ears = new Ears(audio, world);
        var cat = world.Bodies.All.Single(b => b.Kind == BodyKind.Cat);
        var dog = world.Bodies.All.Single(b => b.Kind == BodyKind.Dog);
        cat.Tipped = (sbyte)PetMood.Sleep;
        dog.Tipped = (sbyte)PetMood.Idle;
        ears.Tick(Over(world, 2), SimConstants.TickRate);
        Assert.Equal(0, ears.Count("up-pets.hiss") + ears.Count("up-pets.bark") + ears.Count("up-pets.meow"));
        cat.Tipped = (sbyte)PetMood.Warn;
        dog.Tipped = (sbyte)PetMood.Warn;
        ears.Tick(Over(world, 2), SimConstants.TickRate);
        Assert.InRange(ears.Count("up-pets.hiss"), 1, 2);
        Assert.InRange(ears.Count("up-pets.bark"), 1, 3);
        Assert.Equal(0, ears.Started.Count(v => v.Name.StartsWith("whimsy-")));
    }

    [Fact]
    public void TheCanarySingsAPhraseAtATimeOnAU1sBankAndGoesQuietAsTheTell()
    {
        // Note 715: a phrase, a pause counted from its end, another; something aboard, it stops (the silence is the tell).
        var (world, audio) = Night(["canary"], "up-whimsy.song");
        var ears = new Ears(audio, world);
        ears.Host = w => w.Whimsy = w.Whimsy with { Canary = CanaryMood.Singing };
        ears.Tick(Over(world, 1), SimConstants.TickRate * 6);
        // A 0.6 s phrase, then 1.5-3.5 s: two or three in six seconds.
        Assert.InRange(ears.Count("up-whimsy.song"), 2, 3);
        Assert.Equal(0, ears.Count("whimsy-canary"));
        ears.Host = w => w.Whimsy = w.Whimsy with { Canary = CanaryMood.Silent };
        int sung = ears.Count("up-whimsy.song");
        ears.Tick(Over(world, 1), SimConstants.TickRate * 6);
        Assert.Equal(sung, ears.Count("up-whimsy.song"));
    }

    [Fact]
    public void TheCalliopesNoteIsAU1sPipePlayedAtTheTunesPitch()
    {
        // Note 715: one note a pull, the tune's next; AU1's takes are C5 (MIDI 72), played at the note's pitch an octave down
        // (in the whistle's band, spec A.4).
        var installed = new GameAudio(Content).Bank.Get("up-whimsy.calliope");
        Assert.Equal("pitch", installed!.Layers[0].Rate?.Param);
        var (world, audio) = Night(["calliope"], "up-whimsy.calliope", "train-whistle");
        var ears = new Ears(audio, world);
        var at = Over(world, 0);
        foreach (int note in new[] { 1, 2, 7 })
        {
            world.Whimsy = world.Whimsy with { CalliopeNote = note };
            world.WhistleSeconds = 0.5;
            ears.Tick(at, 3);
            var v = ears.Started.Last(s => s.Name == "up-whimsy.calliope");
            int pitch = BandSynth.CalliopeTune[(note - 1) % BandSynth.CalliopeTune.Length];
            Assert.Equal(Math.Pow(2, (pitch - 12 - 72) / 12.0), v.Params.Get("pitch"), 6);
            world.WhistleSeconds = 0;
            ears.Tick(at, 3);
            Assert.True(v.Stopped);
        }
        Assert.Equal(0, ears.Count("whimsy-calliope"));
    }

    [Fact]
    public void TheGramophoneIsHeardWoundEachTimeItsSetGoingNotWhenFirstHeard()
    {
        var (world, audio) = Night(["gramophone"], "up-whimsy.wind");
        var ears = new Ears(audio, world);
        var ear = Over(world, world.Train.Frames.Count - 1);
        world.Whimsy = world.Whimsy with { Playing = true, Record = 0, WoundAt = 5 };
        ears.Tick(ear, 3);
        Assert.Equal(0, ears.Count("up-whimsy.wind"));
        world.Whimsy = world.Whimsy with { Playing = false };
        ears.Tick(ear, 3);
        world.Whimsy = world.Whimsy with { Playing = true, Record = 1, WoundAt = world.Tick };
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-whimsy.wind"));
    }

    [Fact]
    public void TheBowAndTheSaluteAreHeardOnceAndTheCongaIsOneVoiceForTheLine()
    {
        // Note 714's "more emotes": a bow's coat, a salute's heels, once each; the conga held while anyone dances it, one
        // voice for the line however many are in it.
        var (world, audio) = Night(["moreEmotes"], "crew-emotes.bow", "crew-emotes.salute", "crew-emotes.conga~");
        var ears = new Ears(audio, world);
        var frame = world.Train.Frames[2];
        PlayerState On(double z)
        {
            var s = PlayerMotor.SpawnOnGround(frame.ToWorld(new Double3(0, 0, z)), world.Train.Line, world.Train.Dynamics.Distance, Player);
            return s with { Parent = 2, Position = new Double3(0, frame.Shape.RoofHeight, z), Surface = Surface.Roof };
        }
        audio.CrewStates = [(1, On(-3)), (2, On(0)), (3, On(3))];
        var ear = frame.ToWorld(new Double3(0, frame.Shape.RoofHeight + 1.6, 0));
        ears.Tick(ear);
        world.Emotes.Add(new EmoteEvent(101, world.Tick, 1, Emote.Bow));
        world.Emotes.Add(new EmoteEvent(102, world.Tick, 2, Emote.Salute));
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Equal(1, ears.Count("crew-emotes.bow"));
        Assert.Equal(1, ears.Count("crew-emotes.salute"));
        world.Emotes.Clear();
        foreach (int id in new[] { 1, 2, 3 })
            world.Emotes.Add(new EmoteEvent(110 + id, world.Tick, id, Emote.Conga));
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Single(audio.Mixer.Voices, v => v.Name == "crew-emotes.conga" && !v.Stopped);
        world.Emotes.Clear();
        ears.Tick(ear, SimConstants.TickRate);
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name == "crew-emotes.conga" && !v.Stopped);
    }

    [Fact]
    public void TheHonkHonksEachPullAndTheGoldWhistleSoundsHeldAndLetGo()
    {
        // Note 775: the honk on each pull (another while it's held, the last one let finish); the gold-plated whistle its own
        // sounding, held, and let go. Neither plays the plain whistle.
        var (world, audio) = Night(["theHonk"], "up-horns.honk", "train-whistle");
        var ears = new Ears(audio, world);
        var at = Over(world, 0);
        for (int pull = 0; pull < 2; pull++)
        {
            ears.Host = w => w.WhistleSeconds = 0.3;
            ears.Tick(at, 6);
            ears.Host = w => w.WhistleSeconds = 0;
            ears.Tick(at, SimConstants.TickRate);
        }
        Assert.Equal(2, ears.Count("up-horns.honk"));
        Assert.Equal(0, ears.Count("train-whistle"));

        (world, audio) = Night(["goldWhistle"], "up-horns.gold-start", "up-horns.gold~", "up-horns.gold-stop", "train-whistle");
        ears = new Ears(audio, world);
        ears.Host = w => w.WhistleSeconds = 1;
        ears.Tick(at, SimConstants.TickRate);
        Assert.Equal(1, ears.Count("up-horns.gold-start"));
        Assert.Single(audio.Mixer.Voices, v => v.Name == "up-horns.gold" && !v.Stopped);
        ears.Host = w => w.WhistleSeconds = 0;
        ears.Tick(at, 3);
        Assert.Equal(1, ears.Count("up-horns.gold-stop"));
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name == "up-horns.gold" && !v.Stopped);
        Assert.Equal(0, ears.Count("train-whistle"));
    }

    [Fact]
    public void TheBbqDeckIsHeardLitBurningOpenedSatInAndDrunkFrom()
    {
        // Note 775: off the deck's record and the crew's: the charcoal catching, the grill held while lit, the cooler's lid as a
        // beer comes out, a chair sat in, a can cracked as a beer in hand is drunk, cans clinked when two with beers meet.
        var (world, audio) = Night(["bbqDeck"], "up-bbq.light", "up-bbq.grill~", "up-bbq.turn", "up-bbq.cooler", "up-bbq.can", "up-bbq.cheers",
            "up-bbq.chair");
        int v = Enumerable.Range(0, world.Train.Frames.Count).First(i => world.Train.Frames[i].Shape.Bbq is not null);
        var frame = world.Train.Frames[v];
        var deck = frame.Shape.Bbq!;
        var car = world.Train.Vehicles[v];
        var ears = new Ears(audio, world);
        var ear = frame.ToWorld(deck.Grill + new Double3(1.5, 1.6, 0));
        PlayerState Stood(Double3 local, bool sat = false) => new()
        {
            Parent = v,
            Position = local,
            Health = 100,
            Flags = sat ? PlayerFlags.Lounging : PlayerFlags.None,
        };
        audio.CrewStates = [(1, Stood(deck.Chairs[0])), (2, Stood(deck.Cooler + new Double3(0.6, 0, 0)))];
        car.Beers = 6;
        ears.Tick(ear, 3);
        // Lit: the whump once, the grill held.
        ears.Host = w => w.Train.Vehicles[v].Grill = 60;
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Equal(1, ears.Count("up-bbq.light"));
        Assert.Single(audio.Mixer.Voices, x => x.Name == "up-bbq.grill" && !x.Stopped);
        // A beer out of the cooler; someone sat in a chair.
        ears.Host = w => { w.Train.Vehicles[v].Grill = 60; w.Train.Vehicles[v].Beers = 5; };
        audio.CrewStates = [(1, Stood(deck.Chairs[0], sat: true)), (2, Stood(deck.Cooler + new Double3(0.6, 0, 0)))];
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-bbq.cooler"));
        Assert.Equal(1, ears.Count("up-bbq.chair"));
        // Two with beers in hand, together: cheers; one drunk: the can.
        var one = world.Bodies.SpawnItem(frame.ToWorld(deck.Cooler), 0, BodyKind.Beer);
        var two = world.Bodies.SpawnItem(frame.ToWorld(deck.Cooler), 0, BodyKind.Beer);
        one.Carrier = 1;
        two.Carrier = 2;
        audio.CrewStates = [(1, Stood(deck.Cooler + new Double3(0.3, 0, 0))), (2, Stood(deck.Cooler + new Double3(0.9, 0, 0)))];
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-bbq.cheers"));
        world.Bodies.Remove(one);
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-bbq.can"));
        // Out: the grill falls quiet.
        ears.Host = w => w.Train.Vehicles[v].Grill = 0;
        ears.Tick(ear, 3);
        Assert.DoesNotContain(audio.Mixer.Voices, x => x.Name == "up-bbq.grill" && !x.Stopped);
    }

    [Fact]
    public void TheDiningCarsBarIsHeardPouredSippedAndClinkedAsGlassesNotCans()
    {
        // Note 983: the counter's bell and a pour on its top as the bar's drinks go down; a drink in hand drunk is a glass's
        // sip, not the beer's can; a drink meeting a drink, glasses clinked, not cans.
        var (world, audio) = Night(["diningCar"], "up-dining.pour", "up-dining.sip", "up-dining.clink", "up-bbq.can", "up-bbq.cheers");
        int v = Enumerable.Range(0, world.Train.Frames.Count).First(i => world.Train.Frames[i].Shape.Dining is not null);
        var frame = world.Train.Frames[v];
        var dining = frame.Shape.Dining!;
        var c = dining.Counter;
        var top = frame.ToWorld(new Double3((c.Min.X + c.Max.X) / 2, c.Max.Y, (c.Min.Z + c.Max.Z) / 2));
        var ears = new Ears(audio, world);
        var ear = frame.ToWorld(dining.Bar + new Double3(0, 1.6, 0));
        PlayerState Stood(Double3 local) => new() { Parent = v, Position = local, Health = 100 };
        audio.CrewStates = [(1, Stood(dining.Bar)), (2, Stood(dining.Bar + new Double3(0, 0, -0.6)))];
        world.Train.Vehicles[v].Beers = 6;
        ears.Tick(ear, 3);
        Assert.Equal(0, ears.Count("up-dining.pour"));
        // One poured: the bell and the pour, on the counter's top.
        ears.Host = w => w.Train.Vehicles[v].Beers = 5;
        ears.Tick(ear, 3);
        var pour = Assert.Single(ears.Started, x => x.Name == "up-dining.pour");
        Assert.True((pour.Position - top).Length < 0.01, $"the pour at {pour.Position}, the counter's top at {top}");
        // Two with drinks in hand, together: glasses, not cans; one drunk: a sip, not a can.
        var one = world.Bodies.SpawnItem(frame.ToWorld(dining.Bar), 0, BodyKind.Drink);
        var two = world.Bodies.SpawnItem(frame.ToWorld(dining.Bar), 0, BodyKind.Drink);
        one.Carrier = 1;
        two.Carrier = 2;
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-dining.clink"));
        world.Bodies.Remove(one);
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-dining.sip"));
        Assert.Equal(0, ears.Count("up-bbq.cheers"));
        Assert.Equal(0, ears.Count("up-bbq.can"));
    }

    [Fact]
    public void TheHammockAndTheHotTubAreHeardGotIntoLainInAndOutAndASausageIsHeardEaten()
    {
        // Note 831 (note 744): the hammock lain in and got out of, its ropes creaking while anyone lies in it; the hot tub
        // bubbling while the boiler feeds it, got into and out of; a sausage bitten as it's started and again while it's had.
        var (world, audio) = Night(["hammock", "hotTub"], "up-lounge.hammock", "up-lounge.sway~", "up-lounge.tub-in", "up-lounge.tub~",
            "up-bbq.eat");
        var train = world.Train;
        int van = Enumerable.Range(0, train.Frames.Count).First(i => train.Frames[i].Shape.Lounges.Any(l => l.Kind == LoungeKind.Hammock));
        int engine = Enumerable.Range(0, train.Frames.Count).First(i => train.Frames[i].Shape.Lounges.Any(l => l.Kind == LoungeKind.HotTub));
        var hammock = train.Frames[van].Shape.Lounges.First(l => l.Kind == LoungeKind.Hammock);
        var bench = train.Frames[engine].Shape.Lounges.First(l => l.Kind == LoungeKind.HotTub);
        PlayerState In(int v, Double3 at, bool lounging) => new()
        {
            Parent = v,
            Position = at,
            Health = 100,
            Flags = lounging ? PlayerFlags.Lounging : PlayerFlags.None,
        };
        var ears = new Ears(audio, world);
        var vanEar = train.Frames[van].ToWorld(hammock.At + new Double3(1, 1.6, 0));
        audio.CrewStates = [(1, In(van, hammock.At + new Double3(0, 0, -1), false))];
        ears.Tick(vanEar, 3);
        Assert.Equal(0, ears.Count("up-lounge.hammock"));
        Assert.DoesNotContain(audio.Mixer.Voices, x => x.Name == "up-lounge.sway" && !x.Stopped);
        // Lain in: the canvas and the ropes once, and their creak held while they lie there.
        audio.CrewStates = [(1, In(van, hammock.At, true))];
        ears.Tick(vanEar, SimConstants.TickRate);
        Assert.Equal(1, ears.Count("up-lounge.hammock"));
        Assert.Single(audio.Mixer.Voices, x => x.Name == "up-lounge.sway" && !x.Stopped);
        // Out: once more, and the swing's creak gone with them.
        audio.CrewStates = [(1, In(van, hammock.At + new Double3(0, 0, -1), false))];
        ears.Tick(vanEar, 3);
        Assert.Equal(2, ears.Count("up-lounge.hammock"));
        Assert.DoesNotContain(audio.Mixer.Voices, x => x.Name == "up-lounge.sway" && !x.Stopped);

        // The tub bubbling while it's fed (a train without a boiler's tuning always is), got into with an 'ahh'.
        var tubEar = train.Frames[engine].ToWorld(bench.At + new Double3(1.5, 1.6, 0));
        ears.Tick(tubEar, 3);
        Assert.Single(audio.Mixer.Voices, x => x.Name == "up-lounge.tub" && !x.Stopped);
        Assert.Equal(0, ears.Count("up-lounge.tub-in"));
        audio.CrewStates = [(1, In(engine, bench.At, true))];
        ears.Tick(tubEar, 3);
        Assert.Equal(1, ears.Count("up-lounge.tub-in"));
        Assert.Equal(2, ears.Count("up-lounge.hammock"));

        // A sausage had in the tub: bitten as it's started, and again a second or so on while they're at it; put down, no more.
        var sausage = world.Bodies.SpawnItem(train.Frames[engine].ToWorld(bench.At), 0, BodyKind.Sausage);
        sausage.Carrier = 1;
        ears.Host = w => sausage.MendTicks = 10;
        ears.Tick(tubEar, 2 * SimConstants.TickRate);
        Assert.InRange(ears.Count("up-bbq.eat"), 2, 3);
        int bites = ears.Count("up-bbq.eat");
        ears.Host = w => sausage.MendTicks = 0;
        ears.Tick(tubEar, 2 * SimConstants.TickRate);
        Assert.Equal(bites, ears.Count("up-bbq.eat"));
    }

    [Fact]
    public void TheAnglersOwnCastAndLineAreHeardOffTheirUseAndEveryoneHearsTheCatch()
    {
        // Note 831 (note 744): the cast as Use goes down with a line to put in the water, the line held while Use is; a fish
        // that turns up at an angler's feet, caught. A shore where Jacob would fish, as the sim's LoungeTests find one.
        var anywhere = Base.Enemies!.Jacob with { Chance = 1 };
        var train = Campaign.Apply(C, ["fishingRod"], Base).Train;
        World? world = null;
        (Double3 At, double Along, double Yaw)? site = null;
        for (int seed = 1; seed <= 24 && site is null; seed++)
        {
            var route = DarkTerritory.Sim.LineGen.Routes.Generate(Content, $"frontier:{seed}", 4);
            world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(train, 4, 1)), route.Build(), 400), Base.Combat);
            site = Sim.Enemies.Jacob.Site(world, route, anywhere);
        }
        Assert.NotNull(site);
        var (spot, along, yaw) = site.Value;
        world!.EnableBodies();
        world.Stock();
        var rod = Assert.Single(world.Bodies.All, b => b.Kind == BodyKind.FishingRod);
        rod.Carrier = 1;
        rod.Claimed = true;
        var angler = PlayerMotor.SpawnOnGround(spot, world.Train.Line, along, Player) with { Yaw = yaw, HeldSlot = 0 };
        Assert.True(world.Angling(angler, 1));
        var audio = new GameAudio(Content) { PlayerTuning = Player, OwnId = 1 };
        foreach (var c in new[] { "up-fishing.cast", "up-fishing.line~", "up-fishing.catch" })
            audio.Bank.Add(c.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Loop: c.EndsWith('~'),
                Duration: 0.6, MaxInstances: 16, MaxDistance: 600));
        audio.CrewStates = [(1, angler)];
        var ears = new Ears(audio, world);
        var ear = angler.Position + Double3.Up * 1.6;
        ears.Tick(ear, 3);
        Assert.Equal(0, ears.Count("up-fishing.cast"));
        // Use down: the cast once, the line held while it's down.
        audio.OwnIntent = new PlayerIntent { Buttons = PlayerButtons.Use };
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Equal(1, ears.Count("up-fishing.cast"));
        Assert.Single(audio.Mixer.Voices, x => x.Name == "up-fishing.line" && !x.Stopped);
        // A trout at their feet: caught.
        var ahead = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        world.Bodies.SpawnItem(angler.Position + ahead * 0.6 + Double3.Up * 0.3, along, BodyKind.Fish);
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-fishing.catch"));
        // Use up: the line's out of the water; no second cast till it goes down again.
        audio.OwnIntent = default;
        ears.Tick(ear, 3);
        Assert.DoesNotContain(audio.Mixer.Voices, x => x.Name == "up-fishing.line" && !x.Stopped);
        Assert.Equal(1, ears.Count("up-fishing.cast"));
        // Someone else's line: not heard here (the host alone keeps when it went in), but their catch is.
        audio.OwnId = 2;
        audio.OwnIntent = new PlayerIntent { Buttons = PlayerButtons.Use };
        ears.Tick(ear, 3);
        Assert.Equal(1, ears.Count("up-fishing.cast"));
        world.Bodies.SpawnItem(angler.Position + ahead * 0.6 + Double3.Up * 0.3, along, BodyKind.Fish);
        ears.Tick(ear, 3);
        Assert.Equal(2, ears.Count("up-fishing.catch"));
    }
}
