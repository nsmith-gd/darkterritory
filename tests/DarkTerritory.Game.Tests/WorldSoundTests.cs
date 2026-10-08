using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
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
    static (World World, RouteFeature Feature) Night(Func<RouteFeature, bool> pick, double from, TrainTuning? trains = null)
    {
        var routes = RouteTuning.Load(Content);
        foreach (var tier in Enum.GetValues<RouteTier>())
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var route = RouteGenerator.Generate(routes, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Start > 900 && pick(f)) is not { } f)
                    continue;
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trains ?? Trains, 4, 1)), route.Build(), f.Start + from, Boilers);
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
    public void WithTheRadioRangeUpgradeTheStaticStartsWhereTheVoicesStop()
    {
        // Spec F.3's radio range (note 196): the radio carries kit.radioReach in from a tunnel's mouth before it dies, so the
        // static does too (HostSession.ForwardVoice's test, heard where the voices stop).
        var upgraded = Trains with { Kit = Trains.Kit with { RadioReach = 50 } };
        var (world, tunnel) = Night(f => f.Kind == FeatureKind.Tunnel && f.Length > 150, from: 120, upgraded);
        var audio = new GameAudio(Content) { Voice = null };
        Stand(audio, "voice-radio-sfx.key-down", "voice-radio-sfx.key-up", "voice-radio-sfx.squelch");
        Held(audio, "voice-radio-sfx.static");
        var radio = new VoiceChat(audio.Mixer);
        audio.Voice = radio;
        var ears = new Ears(audio, world);
        var mouth = world.Train.Line.Sample(tunnel.Start + 30).Position + Double3.Up * 2;
        var deep = world.Train.Line.Sample(tunnel.Start + 75).Position + Double3.Up * 2;
        ears.Tick(mouth);
        radio.RadioHeld = true;
        ears.Tick(mouth, 5);
        Assert.False(Playing(audio, "voice-radio-sfx.static"));
        ears.Tick(deep, 5);
        Assert.True(Playing(audio, "voice-radio-sfx.static"));
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
    public void ALockWorkedOpenWithTheWrenchIsQuietAndOneSmashedIsSmashed()
    {
        // D.7 (note 301's slice 2; queue #122, note 385): the wrench at a lock opens it quietly (Holdout.Quiet, replicated), and
        // it was heard as the smash, "loud as a cannon".
        var (world, site) = Night(f => f.Stop is { } stop && stop.Holdouts.Any(x => x.Kind != Sim.Stops.HoldoutKind.Shelter), from: -200);
        var audio = new GameAudio(Content);
        Stand(audio, "place-breach.smash", "place-breach.pick-give");
        Held(audio, "place-breach.pick");
        var h = world.Holdouts!.All.First(x => x.Site == site && x.Lockable);
        var ears = new Ears(audio, world);
        var ear = h.Door + new Double3(1, 1.6, 0);
        ears.Tick(ear);
        double progress = 0;
        void Breach(bool quiet, double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                world.Holdouts.Mirror(h.Index, HoldoutState.Breaching, 1, progress += SimConstants.TickSeconds, quiet);
                ears.Tick(ear);
            }
        }
        Breach(quiet: true, 2);
        Assert.True(Playing(audio, "place-breach.pick"));
        Assert.DoesNotContain(ears.Started, v => v.Name == "place-breach.smash");
        // It gives (Free clears Quiet the tick it does): a click and the hasp, not a smash.
        world.Holdouts.Mirror(h.Index, HoldoutState.Freed, 1, 0);
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Single(ears.Started, v => v.Name == "place-breach.pick-give");
        Assert.DoesNotContain(ears.Started, v => v.Name == "place-breach.smash");
        Assert.False(Playing(audio, "place-breach.pick"));
        // Without the wrench, it's smashed, and smashed open.
        world.Holdouts.Mirror(h.Index, HoldoutState.Occupied, 1, progress = 0);
        Breach(quiet: false, 2);
        Assert.True(ears.Started.Count(v => v.Name == "place-breach.smash") >= 2);
        Assert.False(Playing(audio, "place-breach.pick"));
        world.Holdouts.Mirror(h.Index, HoldoutState.Freed, 1, 0);
        ears.Tick(ear, SimConstants.TickRate);
        Assert.Single(ears.Started, v => v.Name == "place-breach.pick-give");
    }

    [Fact]
    public void TheSteamLiftsWindingEngineRunsWhileItWindsAndEachSkipTipsDownTheChute()
    {
        // The mine head's steam lift (note 368; queue #122, note 385), off the site's replicated state: winding, the ore left.
        var (world, mine) = Night(f => f.Facility == FacilityKind.MineHead, from: 20);
        var audio = new GameAudio(Content);
        Stand(audio, "place-mine-lift.tip");
        Held(audio, "place-mine-lift.winding");
        var run = world.Run!;
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), world.Train.Line);
        var site = run.Sites[run.Route.Of(FeatureKind.Facility).ToList().IndexOf(mine)]!;
        Assert.True(site.Has(ModuleKind.Lift));
        var ear = site.LiftLever + Double3.Up * 1.6;
        var ears = new Ears(audio, world);
        ears.Tick(ear, 5);
        Assert.False(Playing(audio, "place-mine-lift.winding"));
        site.Mirror(site.State with { Winding = true, Wind = 0.5 });
        ears.Tick(ear, SimConstants.TickRate);
        Assert.True(Playing(audio, "place-mine-lift.winding"));
        Assert.DoesNotContain(ears.Started, v => v.Name == "place-mine-lift.tip");
        // A skip wound and tipped: its ore off the shaft's, down the chute, once.
        site.Mirror(site.State with { Ore = site.Ore - 0.25, Wind = 0 });
        ears.Tick(ear, SimConstants.TickRate);
        var tip = Assert.Single(ears.Started, v => v.Name == "place-mine-lift.tip");
        Assert.True((tip.Position - site.LiftChute).Length < 1);
        // The lever let go, or the steam stopped: the engine stops.
        site.Mirror(site.State with { Winding = false });
        ears.Tick(ear, SimConstants.TickRate);
        Assert.False(Playing(audio, "place-mine-lift.winding"));
    }

    [Fact]
    public void InsideAStopsBuildingTheListenerIsInItsRoomAndUnderfootIsTheFloorItsDrawnWith()
    {
        // Queue #129 (note 392): the yard's sheds and the Holdouts drawn walk-in (note 387) and the open houses (note 326) were
        // the open night inside, and every building's floor concrete.
        static bool Boarded(Sim.Stops.BuildingKind k) =>
            k is Sim.Stops.BuildingKind.SignalBox or Sim.Stops.BuildingKind.LampRoom or Sim.Stops.BuildingKind.WaterTower;
        var (world, f) = Night(f => f.Stop is { } st && st.Holdouts.Any(h => Boarded(st.Buildings[h.Building].Kind))
            && st.Buildings.Any(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero), from: -200);
        var stop = f.Stop!;
        var line = world.Train.Line;
        // A point in a building's first part (an open house's outline isn't its middle), in the stop's (S, D).
        Sim.Stops.Pt In(Sim.Stops.StopBuilding b)
        {
            var (x, y) = b.Parts.Count > 0 ? (b.Parts[0].X, b.Parts[0].Y) : (0, 0);
            double c = Math.Cos(b.Yaw), n = Math.Sin(b.Yaw);
            return new Sim.Stops.Pt(b.S + x * c - y * n, b.D + x * n + y * c);
        }
        string SpaceAt(Sim.Stops.Pt p)
        {
            double hint = double.NaN;
            return GameAudio.SpaceOf(world, Run.StopWorld(line, f, p, 1.6), ref hint);
        }
        string FloorAt(Sim.Stops.Pt p)
        {
            double hint = f.Start + p.S;
            return Footing.Ground(world, Run.StopWorld(line, f, p, 0.1), ref hint);
        }
        var shed = stop.Buildings.First(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero);
        Assert.Equal("shed", SpaceAt(In(shed)));
        Assert.Equal("concrete", FloorAt(In(shed)));
        var room = stop.Buildings[stop.Holdouts.First(h => Boarded(stop.Buildings[h.Building].Kind)).Building];
        Assert.Equal("room", SpaceAt(In(room)));
        Assert.Equal("wood", FloorAt(In(room)));
        // Out on the line beside them, the night as before.
        Assert.DoesNotContain(SpaceAt(new Sim.Stops.Pt(shed.S, 0)), new[] { "shed", "room" });
        // A village's open house: a room on its boards.
        static bool Open(Sim.Stops.StopLayout st, int i) => st.Buildings[i].Open && Sim.Run.StopWalls.Walled(st, i);
        (world, f) = Night(f => f.Stop is { } st && Enumerable.Range(0, st.Buildings.Count).Any(i => Open(st, i)), from: -200);
        (stop, line) = (f.Stop!, world.Train.Line);
        var house = stop.Buildings[Enumerable.Range(0, stop.Buildings.Count).First(i => Open(stop, i))];
        Assert.Equal("room", SpaceAt(In(house)));
        Assert.Equal("wood", FloorAt(In(house)));
    }

    [Fact]
    public void FromInsideAStopsBuildingWhatsOutsideComesThroughItsWalls()
    {
        // Note 396 (note 392's "not yet"): in a Holdout's room or a shed, a sound out on the line is behind its walls (walls.json:
        // a room's one door lets less through than a shed's open bays); one in the room with you isn't; out of the building,
        // the same sound is clear.
        var walls = DataFile.Load<WallsTuning>(Path.Combine(Content, WallsTuning.File));
        static bool Boarded(Sim.Stops.BuildingKind k) =>
            k is Sim.Stops.BuildingKind.SignalBox or Sim.Stops.BuildingKind.LampRoom or Sim.Stops.BuildingKind.WaterTower;
        var (world, f) = Night(f => f.Stop is { } st && st.Holdouts.Any(h => Boarded(st.Buildings[h.Building].Kind))
            && st.Buildings.Any(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero), from: -200);
        var stop = f.Stop!;
        var line = world.Train.Line;
        Double3 At(Sim.Stops.StopBuilding b) => Run.StopWorld(line, f, new Sim.Stops.Pt(b.S, b.D), 1.6);
        var room = stop.Buildings[stop.Holdouts.First(h => Boarded(stop.Buildings[h.Building].Kind)).Building];
        var shed = stop.Buildings.First(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero);
        var audio = new GameAudio(Content);
        Held(audio, "out-on-the-line", "in-the-room");
        var ears = new Ears(audio, world);
        var outside = audio.Mixer.Play("out-on-the-line", Run.StopWorld(line, f, new Sim.Stops.Pt(room.S, 0), 1.6))!;
        var inside = audio.Mixer.Play("in-the-room", At(room) + new Double3(0.3, 0, 0.3))!;
        ears.Tick(At(room));
        Assert.Equal("room", audio.Space);
        Assert.Equal((float)walls.RoomWall, outside.Walls, 3);
        Assert.Equal(0, inside.Walls);
        ears.Tick(At(shed));
        Assert.Equal("shed", audio.Space);
        Assert.True(walls.ShedWall < walls.RoomWall);
        Assert.Equal((float)walls.ShedWall, outside.Walls, 3);
        ears.Tick(Run.StopWorld(line, f, new Sim.Stops.Pt(room.S, 2), 1.6));
        Assert.Equal(0, outside.Walls);
    }

    [Theory]
    [InlineData("deadLines:2", "world-water.surf", "sea")]
    [InlineData("deadLines:2", "world-water.river", "river")]
    [InlineData("frontier:7", "world-water.tide", "fundy")]
    [InlineData("frontier:7", "world-water.river", "tidal")]
    [InlineData("frontier:3", "world-water.lake", "lake")]
    public void TheLinesWaterIsHeardWhereItsWaterIs(string id, string cue, string kind)
    {
        // Note 429: each kind of the plan's water, by an ear beside it, held on its water (the terrain has water there, of
        // that kind); and nothing of it a kilometre up.
        var route = Sim.LineGen.Routes.Generate(Content, id, 4);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), gate, Boilers));
        world.EnableRun(Runs, route, gate, authority: false);
        var plan = world.TrackPlan ?? route.Plan!;
        var line = world.Train.Line;
        var terrain = ((line.Conditions is Sim.Net.HazardConditions h ? h.Inner : line.Conditions) as Sim.LineGen.PlanConditions)!.Terrain;
        Double3 OnLine(double s) => line.Sample(s).Position + Double3.Up * 1.6;
        Double3 ear;
        if (kind == "lake")
        {
            // 15 m out from a lake's shore, on the land.
            var lake = plan.Lakes.First(l => !l.Crossed);
            double r = 0;
            while (Sim.LineGen.TerrainField.LakeMetric(lake, lake.X + r, lake.Z) < 1)
                r += 0.5;
            double x = lake.X + r + 15;
            ear = new Double3(x, terrain.Height(x, lake.Z) + 1.6, lake.Z);
        }
        else if (kind == "tidal")
        {
            var river = plan.Water.First(w => w.Type == "tidal");
            ear = OnLine((river.S0 + river.S1) / 2);
        }
        else
        {
            var shore = plan.Shores.First(s => s.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase));
            ear = OnLine((shore.S0 + shore.S1) / 2);
        }
        var audio = new GameAudio(Content);
        Held(audio, cue);
        var ears = new Ears(audio, world);
        ears.Tick(ear, SimConstants.TickRate / 2);
        var voice = audio.Mixer.Voices.Single(v => v.Name == cue && !v.Finished);
        var p = voice.Position;
        Assert.True(terrain.WaterAt(p.X, p.Z) is not null || terrain.WaterNear(p.X, p.Z, 3)?.Kind == kind,
            $"{cue} at ({p.X:0}, {p.Y:0}, {p.Z:0}), {(p - ear).Length:0} m from the ear, isn't on {kind} water");
        ears.Tick(ear + Double3.Up * 1000, SimConstants.TickRate / 2);
        Assert.False(Playing(audio, cue));
    }

    [Fact]
    public void AStopsBuildingsWallsAreBetweenAnEarOutsideItAndASoundInIt()
    {
        // Note 428 (note 396's not-yets): a sound in a Holdout's room comes out through its walls to an ear on the line, and to
        // one in the shed through the room's (more than the shed's own); one at the room's face on the ear's side is at its
        // door and heard out of it. The night's own air, played at the ear, is behind the room's walls once the ear's in it.
        var walls = DataFile.Load<WallsTuning>(Path.Combine(Content, WallsTuning.File));
        static bool Boarded(Sim.Stops.BuildingKind k) =>
            k is Sim.Stops.BuildingKind.SignalBox or Sim.Stops.BuildingKind.LampRoom or Sim.Stops.BuildingKind.WaterTower;
        var (world, f) = Night(f => f.Stop is { } st && st.Holdouts.Any(h => Boarded(st.Buildings[h.Building].Kind))
            && st.Buildings.Any(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero), from: -200);
        var stop = f.Stop!;
        var line = world.Train.Line;
        Double3 At(Sim.Stops.StopBuilding b) => Run.StopWorld(line, f, new Sim.Stops.Pt(b.S, b.D), 1.6);
        var room = stop.Buildings[stop.Holdouts.First(h => Boarded(stop.Buildings[h.Building].Kind)).Building];
        var shed = stop.Buildings.Where(b => b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero)
            .MinBy(b => (At(b) - At(room)).Length)!;
        Assert.True((At(shed) - At(room)).Length < 120, $"the shed's {(At(shed) - At(room)).Length:0} m from the room");
        var onTheLine = Run.StopWorld(line, f, new Sim.Stops.Pt(room.S, 0), 1.6);
        // The room's face toward the line, walked out to from its middle.
        var footprint = GameAudio.RoomAt(world.Run!.Route, line, At(room), f.Start + room.S)!.Value;
        var toLine = new Double3(onTheLine.X - At(room).X, 0, onTheLine.Z - At(room).Z).Normalized;
        var face = At(room);
        while (footprint.Holds(face + toLine * 0.05))
            face += toLine * 0.05;
        Assert.True((face - At(room)).Length > 1.5);
        var audio = new GameAudio(Content);
        Held(audio, "in-the-room", "at-its-door", "out-on-the-line", "world-night.night");
        var ears = new Ears(audio, world);
        var inside = audio.Mixer.Play("in-the-room", face - toLine * 1.2)!;
        var door = audio.Mixer.Play("at-its-door", face - toLine * 0.2)!;
        var outside = audio.Mixer.Play("out-on-the-line", onTheLine + new Double3(0, 0, 3))!;
        ears.Tick(onTheLine, SimConstants.TickRate / 2);
        Assert.DoesNotContain(audio.Space, new[] { "room", "shed" });
        Assert.Equal((float)walls.RoomWall, inside.Walls, 3);
        Assert.Equal(0, door.Walls);
        Assert.Equal(0, outside.Walls);
        var night = audio.Mixer.Voices.Single(v => v.Name == "world-night.night" && !v.Finished);
        Assert.Equal(0, Math.Max(night.Occlusion, night.Walls));
        // From the shed, the room's walls and the shed's: the room's, the more.
        ears.Tick(At(shed), SimConstants.TickRate / 2);
        Assert.Equal("shed", audio.Space);
        Assert.Equal((float)walls.RoomWall, inside.Walls, 3);
        Assert.Equal((float)walls.ShedWall, night.Occlusion, 3);
        // In the room with it: clear, and the night's air behind the room's walls.
        ears.Tick(At(room), SimConstants.TickRate / 2);
        Assert.Equal("room", audio.Space);
        Assert.Equal(0, inside.Walls);
        Assert.Equal((float)walls.RoomWall, night.Occlusion, 3);
    }

    [Fact]
    public void TheWalledTownIsHeardItsFiresItsRoomsAndItsPeople()
    {
        // Note 415: the departure town's fires held where they burn, a lived-in house's range clear inside it and through its
        // walls from the street, its people out of doors heard talking low where they are, and coughing now and then; and
        // nothing of it away from the town. A town of 3000, as World.EnableTown stands it.
        var towns = Sim.Towns.TownContent.Load(Content)!;
        towns = towns with { Tuning = towns.Tuning with { Population = [3000, 3000] } };
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 6, 1)), route.Build(), gate - 8, Boilers));
        world.EnableRun(Runs, route, gate, authority: false);
        world.EnableTown(towns, route, gate, []);
        var town = world.Town!;
        var audio = new GameAudio(Content);
        Held(audio, "place-town.fire", "place-town.range", "place-town.clock", "place-town.radio", "place-town.murmur");
        Stand(audio, "place-town.cough");
        bool Sounding(string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished && !v.Stopped);
        SoundInstance VoiceAt(string name, Double3 at) =>
            audio.Mixer.Voices.Where(v => v.Name == name && !v.Finished && !v.Stopped).MinBy(v => (v.Position - at).Length)!;
        var ears = new Ears(audio, world);

        // The town is looked round every quarter second (GameAudio.TownLook): half a second at each place.
        // Beside a fire barrel in the square: it burns there, nothing between.
        var barrel = town.Plan.Fixtures.First(f => f.Kind == "barrel");
        var fire = town.World(barrel.S, barrel.D, Sim.Towns.TownFixtures.Size("barrel").Height);
        var street = town.World(barrel.S + 2.5, barrel.D, 1.6);
        Assert.Equal(-1, GameAudio.TownHouseAt(town, street));
        ears.Tick(street, SimConstants.TickRate / 2);
        Assert.True(Sounding("place-town.fire"));
        Assert.Contains(audio.Mixer.Voices, v => v.Name == "place-town.fire" && !v.Stopped && (v.Position - fire).Length < 1e-6 && v.Occlusion == 0);

        // A lived-in house's range: clear in its kitchen, through its walls from out in front.
        var stove = town.Plan.Fixtures.First(f => f.Kind == "stove" && f.House >= 0);
        var house = town.Plan.Houses.First(h => h.Id == stove.House);
        var kitchen = town.World(stove.S, stove.D, 1.6) + (town.World(house.S, house.D) - town.World(stove.S, stove.D)) * 0.3;
        Assert.Equal(house.Id, GameAudio.TownHouseAt(town, kitchen));
        ears.Tick(kitchen, SimConstants.TickRate / 2);
        var hob = town.World(stove.S, stove.D, Math.Max(0.5, stove.Height));
        Assert.True(Sounding("place-town.range"));
        Assert.True((VoiceAt("place-town.range", hob).Position - hob).Length < 1e-6);
        Assert.Equal(0, VoiceAt("place-town.range", hob).Occlusion);
        var front = house.Rail(0, -2.5);
        var outside = town.World(front.S, front.D, 1.6);
        Assert.Equal(-1, GameAudio.TownHouseAt(town, outside));
        ears.Tick(outside, SimConstants.TickRate / 2);
        Assert.True(Sounding("place-town.range"));
        Assert.True(VoiceAt("place-town.range", hob).Occlusion > 0.5f);

        // Where the townsfolk stand: their murmur, and in a minute or so a cough.
        var person = town.Plan.People.First(p => p.House < 0);
        var among = town.Feet(person) + new Double3(1.5, 1.6, 0);
        int coughs = ears.Started.Count(v => v.Name == "place-town.cough");
        ears.Tick(among, SimConstants.TickRate * 90);
        Assert.True(Sounding("place-town.murmur"));
        Assert.True(ears.Started.Count(v => v.Name == "place-town.cough") > coughs);

        // Away from the town: none of it.
        ears.Tick(world.Train.Line.Sample(gate + 3_000).Position + Double3.Up * 1.6, SimConstants.TickRate / 2);
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name.StartsWith("place-town.") && !v.Finished && !v.Stopped);
    }

    [Fact]
    public void AnOpenHousesHidingSpotIsHeardWhileItsSearchedAndItsFindOnceWhenItsGoneThrough()
    {
        // Note 412 (note 326): each kind of hiding spot its own sound, held where it's kept while the search is under way as
        // the host has it (a client's view: Run.MirrorSearch), cut when the hands come off; the find once, as it's gone through.
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), 900, Boilers);
        var world = new World(train);
        world.EnableRun(Runs, route, 600, authority: false, loot: DataFile.Load<Sim.Stops.LootTuning>(Path.Combine(Content, Sim.Stops.LootTuning.File)));
        var run = world.Run!;
        Assert.NotEmpty(run.HidingSpots);
        var audio = new GameAudio(Content);
        static string CueOf(Sim.Stops.ContainerKind kind) => kind switch
        {
            Sim.Stops.ContainerKind.Cupboard => "crew-search.cupboard",
            Sim.Stops.ContainerKind.Cabinet => "crew-search.cabinet",
            Sim.Stops.ContainerKind.Cellar => "crew-search.cellar",
            // A barn's or a shed's (note 417): the hayloft's and the workbench's own.
            Sim.Stops.ContainerKind.Hayloft => "crew-search.hayloft",
            Sim.Stops.ContainerKind.Bench => "crew-search.bench",
            _ => "crew-search.boards",
        };
        Held(audio, "crew-search.cupboard", "crew-search.cabinet", "crew-search.cellar", "crew-search.boards", "crew-search.hayloft", "crew-search.bench");
        Stand(audio, "crew-search.found");
        bool Sounding(string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished && !v.Stopped);
        var ears = new Ears(audio, world);
        var kinds = run.HidingSpots.GroupBy(h => h.Container.Kind).Select(g => g.First()).ToList();
        Assert.True(kinds.Count >= 3, $"only {kinds.Count} kinds of hiding spot on the night");
        foreach (var spot in kinds)
        {
            string cue = CueOf(spot.Container.Kind);
            var ear = spot.Kept + spot.Facing * 1.5 + Double3.Up * 1.6;
            ears.Tick(ear);
            Assert.False(Sounding(cue));
            // Under way: held where it's kept.
            run.MirrorSearch(spot.Stop, [], [(spot.Container.Index, 0.3)]);
            ears.Tick(ear, 5);
            Assert.True(Sounding(cue), cue);
            Assert.True((audio.Mixer.Voices.First(v => v.Name == cue && !v.Stopped).Position - spot.Kept).Length < 1.5);
            // Hands off: cut.
            run.MirrorSearch(spot.Stop, [], []);
            ears.Tick(ear, 2);
            Assert.False(Sounding(cue));
            // Gone through: the find, once.
            int found = ears.Started.Count(v => v.Name == "crew-search.found");
            run.MirrorSearch(spot.Stop, [spot.Container.Index], []);
            ears.Tick(ear, SimConstants.TickRate);
            Assert.Equal(found + 1, ears.Started.Count(v => v.Name == "crew-search.found"));
            Assert.False(Sounding(cue));
        }
    }

    [Fact]
    public void AVillageHouseDoorIsHeardShutAndOpenedInTheDoorwayAndTheChoirBeatsOnItWhenItsShutUp()
    {
        // Note 409 (B4's note 401): a house door shut or opened, off the replicated state, once a change, in its doorway: in
        // the room with an ear inside the house (no walls, nothing muffled) and clear from the street. A house shut up with
        // someone in it is a space of its own, and the Choir's BESIEGE beats on its door as on a shut car's.
        var routes = RouteTuning.Load(Content);
        var combat = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
        World? world = null;
        HouseDoor door = default;
        for (ulong seed = 1; world is null; seed++)
        {
            Assert.True(seed <= 40, "no open house with one door on 40 nights");
            var route = RouteGenerator.Generate(routes, RouteTier.Frontier, seed);
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), route.Build(), 900, Boilers);
            train.Walls = StopWalls.Of(route, train.Line, tuning: Runs.Walls);
            var single = train.Walls.HouseDoors.Where(d => train.Walls.HouseDoors.Count(o => o.House == d.House) == 1).ToList();
            if (single.Count == 0)
                continue;
            door = single[0];
            world = new World(train, combat);
            world.EnableRun(Runs, route, 600, authority: false);
        }
        var walls = world.Train.Walls!;
        var audio = new GameAudio(Content);
        Stand(audio, "crew-house-door.shut", "crew-house-door.open", "cs-choir.bang-door.wood", "cs-choir.disperse");
        Held(audio, "cs-choir.arrive");
        var heard = new List<SoundInstance>();
        var seen = new HashSet<int>();
        void Tick(Double3 ear, int ticks = 1, params Enemy[] enemies)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.BeginTick();
                world.MirrorEnemies(enemies);
                var state = new PlayerState { Parent = PlayerState.World, Position = ear };
                audio.Update(world, world.Controls, Listener.At(ear + Double3.Up * 1.6, 0), exposed: true, SimConstants.TickSeconds,
                    PlayerMotor.Space(state, world.Train));
                audio.Mixer.Render(new float[Audio.Block * 2]);
                heard.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id) && v.Name.StartsWith("crew-house-door") | v.Name.StartsWith("cs-choir.bang")));
            }
        }
        var street = door.At + door.Out * 5;
        var inside = door.At - door.Out * 2.5;
        Tick(street, 3);
        Assert.Empty(heard);

        // Shut from the street (the host's word, as a client gets it): once, in the doorway, clear.
        walls.MirrorShut([door.Key]);
        Tick(street, SimConstants.TickRate);
        var shut = Assert.Single(heard);
        Assert.Equal("crew-house-door.shut", shut.Name);
        Assert.True((shut.Position - GameAudio.DoorSound(door)).Length < 1e-6);
        Assert.Equal(0, shut.Occlusion);
        Assert.Equal(0, shut.Walls);

        // Opened again and shut from inside: in the room with the ear, nothing between them.
        heard.Clear();
        walls.MirrorShut([]);
        Tick(inside, 2);
        walls.MirrorShut([door.Key]);
        Tick(inside, 2);
        Assert.Equal(["crew-house-door.open", "crew-house-door.shut"], heard.Select(v => v.Name));
        Assert.Equal(PlayerMotor.HouseSpace(door.House), PlayerMotor.Space(new PlayerState { Parent = PlayerState.World, Position = inside }, world.Train));
        Assert.Equal("room", audio.Space);
        Assert.All(heard, v => Assert.Equal(0, Math.Max(v.Occlusion, v.Walls)));

        // Shut up in it while the Choir besieges: they beat on its door, now and then.
        heard.Clear();
        audio.CrewStates = [(1, new PlayerState { Parent = PlayerState.World, Position = inside, Health = 100 })];
        world.Choir = new ChoirState { Present = true, Build = 1 };
        var ghost = new ChoirGhost(70);
        ghost.Restore(SpinePhase.Commit, 2, 6, Enemy.Loose, inside + Double3.Up * 6, 0, 0, 0, -1, 0, -1, 0);
        Tick(inside, 90, ghost);
        Assert.InRange(heard.Count, 2, 5);
        Assert.All(heard, v => Assert.Equal("cs-choir.bang-door.wood", v.Name));
        Assert.All(heard, v => Assert.True((v.Position - GameAudio.DoorSound(door)).Length < 1e-6));
        Assert.All(heard, v => Assert.Equal(0, Math.Max(v.Occlusion, v.Walls)));
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

    [Fact]
    public void TheFoundrysFurnaceBurnsInItsShedWithNobodyToTendIt()
    {
        // C1's casting shed (note 420), heard (queue #161, note 425): the cupola banked and roaring under its own draught,
        // held at the works while the ear's near them, gone when it's well away.
        var (world, foundry) = Night(f => f.Facility == FacilityKind.Foundry, from: 20);
        var audio = new GameAudio(Content);
        Held(audio, "place-foundry.furnace");
        Stand(audio, "place-foundry.slump");
        var line = world.Train.Line;
        // Where the sound places the works: the yard's sheds of a laid-out stop, or off the line's side mid-zone.
        Double3 works;
        if (foundry.Stop?.Buildings.FirstOrDefault(b => b.Zone == StopZone.Yard && b.Kind is BuildingKind.Shed or BuildingKind.Hero) is { } shed)
            works = DarkTerritory.Sim.Run.Run.StopWorld(line, foundry, shed.Centre);
        else
        {
            var t = line.Sample((foundry.Start + foundry.End) / 2);
            works = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * ((foundry.Side == 0 ? 1 : foundry.Side) * 25);
        }
        var ears = new Ears(audio, world);
        ears.Tick(works + new Double3(6, 1.6, 0), SimConstants.TickRate);
        var furnace = audio.Mixer.Voices.Single(v => v.Name == "place-foundry.furnace" && !v.Finished);
        Assert.True((furnace.Position - (works + Double3.Up * 6)).Length < 1);
        ears.Tick(works + new Double3(400, 1.6, 0), SimConstants.TickRate);
        Assert.False(Playing(audio, "place-foundry.furnace"));
    }
}
