using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The world, the places and the radio (GameAudio.Outside; tools/audio/cues.py "world-*", "place-*", "voice-*"): heard on a
/// client from what it's sent, where it happens.
/// </summary>
public class WorldSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly BoilerTuning Boilers = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly RunTuning Runs = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    static readonly HoldoutTuning Holdouts = DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File));

    /// <summary>A client's night with the engine <paramref name="from"/> metres from the start of the first feature like this.</summary>
    static (World World, RouteFeature Feature) Night(Func<RouteFeature, bool> pick, double from)
    {
        var routes = RouteTuning.Load(Content);
        foreach (var tier in Enum.GetValues<RouteTier>())
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var route = RouteGenerator.Generate(routes, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Start > 900 && pick(f)) is not { } f)
                    continue;
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), f.Start + from, Boilers);
                var world = new World(train);
                world.EnableRun(Runs, route, 600, authority: false);
                world.EnableHoldouts(Holdouts, route);
                world.Run!.Resume(900, -1, train.Boiler.Tender, 0);
                return (world, f);
            }
        throw new InvalidOperationException("no generated route has a feature like that");
    }

    static void Stand(GameAudio audio, params string[] cues)
    {
        foreach (var cue in cues)
            audio.Bank.Add(cue, new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Duration: 0.2, MaxInstances: 64, MaxDistance: 600));
    }

    static void Held(GameAudio audio, params string[] cues)
    {
        foreach (var cue in cues)
            audio.Bank.Add(cue, new SoundDef(2, [new LayerDef(SourceKind.Sine, 0.2, Frequency: 440)], Loop: true, MaxInstances: 64, MaxDistance: 600));
    }

    static bool Playing(GameAudio audio, string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished);

    sealed class Ears(GameAudio audio, World world)
    {
        readonly HashSet<int> _seen = [];
        public readonly List<SoundInstance> Started = [];

        public void Tick(Double3 ear, int ticks = 1, double? speed = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                if (speed is { } v)
                {
                    world.BeginTick();
                    world.Step(new TrainControls { Reverser = 1 });
                    world.Train.Dynamics.Velocity = v;
                }
                audio.Update(world, new TrainControls { Reverser = 1 }, Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
                audio.Mixer.Render(new float[Audio.Block * 2]);
                Started.AddRange(audio.Mixer.Voices.Where(x => _seen.Add(x.Id)));
            }
        }
    }

    [Fact]
    public void EachPrisonerCallsOutInTheirOwnVoiceFromTheirHoldout()
    {
        // GDD App. D.7: a call is a shout or a bout of banging from that occupant's voice set, at the Holdout, once a call
        // (Holdout.Calls, replicated); a prisoner keeps one voice for the run, and no two share one.
        var (world, site) = Night(f => f.Stop is { Holdouts.Count: > 0 }, from: -200);
        var audio = new GameAudio(Content);
        string[] voices = [.. Enumerable.Range(1, 8).SelectMany(n => new[] { $"voice-prisoner-sets.call.set{n}", $"voice-prisoner-sets.shout.set{n}" })];
        Stand(audio, [.. voices, "voice-callout.bang"]);
        var h = world.Holdouts!.All.First(x => x.Site == site);
        var ears = new Ears(audio, world);
        var ear = h.Door + Double3.Up * 1.6;
        ears.Tick(ear);
        int calls = 0;
        var sets = new Dictionary<int, HashSet<string>>();
        foreach (int prisoner in new[] { 1, 2 })
            for (int i = 0; i < 12; i++)
            {
                world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, prisoner, 0, calls: ++calls);
                int before = ears.Started.Count;
                ears.Tick(ear, SimConstants.TickRate);
                var heard = ears.Started.Skip(before).Where(v => v.Name.StartsWith("voice-")).ToList();
                Assert.NotEmpty(heard);
                Assert.All(heard, v => Assert.True((v.Position - h.Inside).Length < 2, $"{v.Name} away from the Holdout"));
                foreach (var v in heard.Where(v => v.Name.StartsWith("voice-prisoner-sets.")))
                    (sets.TryGetValue(prisoner, out var s) ? s : sets[prisoner] = []).Add(v.Name[^4..]);
            }
        Assert.Contains(ears.Started, v => v.Name == "voice-callout.bang");
        Assert.Single(sets[1]);
        Assert.Single(sets[2]);
        Assert.NotEqual(sets[1].Single(), sets[2].Single());
        // Nothing more until it's called from again.
        int was = ears.Started.Count(v => v.Name.StartsWith("voice-"));
        ears.Tick(ear, SimConstants.TickRate * 2);
        Assert.Equal(was, ears.Started.Count(v => v.Name.StartsWith("voice-")));
    }

    [Fact]
    public void TheTrainIsHeardGoingIntoATunnelAndComingOutOfIt()
    {
        var (world, tunnel) = Night(f => f.Kind == FeatureKind.Tunnel && f.Length < 500, from: -30);
        var audio = new GameAudio(Content);
        Stand(audio, "world-tunnels.enter", "world-tunnels.exit");
        Held(audio, "world-tunnels.inside");
        var ears = new Ears(audio, world);
        var line = world.Train.Line;
        bool inside = false;
        const double speed = 20;
        for (int i = 0; i < (tunnel.Length + 80) / speed * SimConstants.TickRate; i++)
        {
            var e = world.Train.Frames[0];
            ears.Tick(e.ToWorld(new Double3(0, 3, 0)), speed: speed);
            inside |= Playing(audio, "world-tunnels.inside");
        }
        var enter = Assert.Single(ears.Started, v => v.Name == "world-tunnels.enter");
        var exit = Assert.Single(ears.Started, v => v.Name == "world-tunnels.exit");
        Assert.True((enter.Position - line.Sample(tunnel.Start).Position).Length < 5);
        Assert.True((exit.Position - line.Sample(tunnel.End).Position).Length < 5);
        Assert.True(inside);
        Assert.False(Playing(audio, "world-tunnels.inside"));
    }

    [Fact]
    public void TheRadioClicksWhenItsKeyedAndHissesWhereItsDead()
    {
        var (world, tunnel) = Night(f => f.Kind == FeatureKind.Tunnel && f.Length > 150, from: 120);
        var audio = new GameAudio(Content) { Voice = null };
        Stand(audio, "voice-radio-sfx.key-down", "voice-radio-sfx.key-up", "voice-radio-sfx.squelch");
        Held(audio, "voice-radio-sfx.static");
        var radio = new VoiceChat(audio.Mixer);
        audio.Voice = radio;
        var ears = new Ears(audio, world);
        var outside = world.Train.Line.Sample(tunnel.Start - 300).Position + Double3.Up * 2;
        var under = world.Train.Line.Sample(tunnel.Start + 60).Position + Double3.Up * 2;
        ears.Tick(outside);
        radio.RadioHeld = true;
        ears.Tick(outside, 5);
        Assert.Single(ears.Started, v => v.Name == "voice-radio-sfx.key-down");
        Assert.False(Playing(audio, "voice-radio-sfx.static"));
        radio.RadioHeld = false;
        ears.Tick(outside, 5);
        Assert.Single(ears.Started, v => v.Name == "voice-radio-sfx.key-up");
        // Keyed under the hill, it's dead: static.
        radio.RadioHeld = true;
        ears.Tick(under, 5);
        Assert.True(Playing(audio, "voice-radio-sfx.static"));
        radio.RadioHeld = false;
        ears.Tick(under, 2);
        Assert.False(Playing(audio, "voice-radio-sfx.static"));
    }

    [Fact]
    public void TheCoalingChuteOpensPoursAndShuts()
    {
        var (world, tower) = Night(f => f.Facility == FacilityKind.CoalingTower, from: 20);
        var audio = new GameAudio(Content);
        Stand(audio, "place-coaling.chute-open", "place-coaling.chute-shut", "place-coaling.coal-settle");
        Held(audio, "place-coaling.coal-pour");
        var run = world.Run!;
        var facilities = run.Route.Of(FeatureKind.Facility).ToList();
        int index = facilities.IndexOf(tower);
        double[] left = [.. facilities.Select(_ => Runs.Chute.Capacity)];
        var ears = new Ears(audio, world);
        var lever = run.ChuteAt(tower, world.Train.Line).Lever;
        ears.Tick(lever);
        run.Mirror(RunPhase.AtFacility, RunEnd.None, 900, index, chuteOpen: true, left);
        ears.Tick(lever, 10);
        Assert.Single(ears.Started, v => v.Name == "place-coaling.chute-open");
        Assert.True(Playing(audio, "place-coaling.coal-pour"));
        world.Train.Boiler.Tender += 10;
        run.Mirror(RunPhase.AtFacility, RunEnd.None, 901, index, chuteOpen: false, left);
        ears.Tick(lever, SimConstants.TickRate * 2);
        Assert.Single(ears.Started, v => v.Name == "place-coaling.chute-shut");
        Assert.False(Playing(audio, "place-coaling.coal-pour"));
        Assert.Equal(2, ears.Started.Count(v => v.Name == "place-coaling.coal-settle"));
    }

    [Fact]
    public void TheGrainSpoutSwingsPoursAndStops()
    {
        // GDD §18's grain elevator (its spout, note 185): its own sounds while someone holds its lever, and none of the
        // coaling tower's.
        var (world, elevator) = Night(f => f.Facility == FacilityKind.GrainElevator, from: 20);
        var audio = new GameAudio(Content);
        Stand(audio, "place-grain.spout-swing", "place-grain.spout-stop", "place-coaling.chute-open", "place-coaling.chute-shut");
        Held(audio, "place-grain.grain-pour", "place-coaling.coal-pour");
        var run = world.Run!;
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), world.Train.Line);
        int index = run.Route.Of(FeatureKind.Facility).ToList().IndexOf(elevator);
        var site = run.Sites[index]!;
        Assert.True(site.Has(ModuleKind.Spout));
        double[] left = new double[run.FacilityCount];
        SiteState[] States(bool pouring) => [.. run.Sites.Select(x => new SiteState(true, 0, x?.SledsLeft ?? 0, false, false, 0) { Bin = x?.Bin ?? 0, Pouring = pouring && x == site })];
        var ears = new Ears(audio, world);
        ears.Tick(site.Spout);
        run.Mirror(RunPhase.AtFacility, RunEnd.None, 900, index, false, left, States(pouring: true));
        ears.Tick(site.Spout, 10);
        Assert.Single(ears.Started, v => v.Name == "place-grain.spout-swing");
        Assert.True(Playing(audio, "place-grain.grain-pour"));
        // Let go, or run dry: cut off.
        run.Mirror(RunPhase.AtFacility, RunEnd.None, 901, index, false, left, States(pouring: false));
        ears.Tick(site.Spout, SimConstants.TickRate * 2);
        Assert.Single(ears.Started, v => v.Name == "place-grain.spout-stop");
        Assert.False(Playing(audio, "place-grain.grain-pour"));
        Assert.DoesNotContain(ears.Started, v => v.Name.StartsWith("place-coaling", StringComparison.Ordinal));
        // Pouring again and the train moves off (it isn't at the elevator any more): cut off where it was.
        run.Mirror(RunPhase.AtFacility, RunEnd.None, 902, index, false, left, States(pouring: true));
        ears.Tick(site.Spout, 10);
        run.Mirror(RunPhase.Underway, RunEnd.None, 903, -1, false, left, States(pouring: false));
        ears.Tick(site.Spout, SimConstants.TickRate * 2);
        Assert.Equal(2, ears.Started.Count(v => v.Name == "place-grain.spout-stop"));
        Assert.False(Playing(audio, "place-grain.grain-pour"));
    }

    [Fact]
    public void AWreckYardHeapGroansForItsWholeWarningThenShifts()
    {
        // GDD §18's wreck yard (note 187): a heap about to shift onto whoever's beside it groans for the 3 s it gives them
        // (heap-groan, the tell, note 198) with the recorded creaks over it, and comes down with place-wreck's shift.
        var (world, yard) = Night(f => f.Facility == FacilityKind.WreckYard, from: 20);
        var audio = new GameAudio(Content);
        Stand(audio, "place-wreck.creak", "place-wreck.shift");
        var run = world.Run!;
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), world.Train.Line);
        var site = run.Sites[run.Route.Of(FeatureKind.Facility).ToList().IndexOf(yard)]!;
        var heap = site.Heaps[0];
        var ear = heap.Centre + new Double3(4, 1.7, 0);
        var ears = new Ears(audio, world);
        ears.Tick(ear, 5);
        Assert.False(Playing(audio, "heap-groan"));
        var was = heap.State;
        heap.Mirror(was with { Stability = 0, Groan = 3 });
        ears.Tick(ear, SimConstants.TickRate);
        Assert.True(Playing(audio, "heap-groan"));
        Assert.Equal(1, audio.Mixer.Voices.Single(v => v.Name == "heap-groan").Def.Tier);
        Assert.Contains(ears.Started, v => v.Name == "place-wreck.creak");
        heap.Mirror(was with { Stability = 0, Groan = 0, Shifts = was.Shifts + 1 });
        ears.Tick(ear, SimConstants.TickRate / 2);
        Assert.False(Playing(audio, "heap-groan"));
        Assert.Single(ears.Started, v => v.Name == "place-wreck.shift");
    }
}
