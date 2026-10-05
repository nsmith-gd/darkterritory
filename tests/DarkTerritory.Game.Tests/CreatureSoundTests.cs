using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The creature sounds' hooks (GameAudio.Creatures; tools/audio/cues.py cs-*, out/audio/hooks-map.md): each plays at the
/// moment its creature does the thing, read off enemy records mirrored tick by tick as a client gets them (records are
/// rebuilt every snapshot; deaths are records gone). The sounds are stand-ins in a bank of their own, so what the
/// checklist has installed (or not) doesn't change what's tested: which cue fires, when, and how often.
/// </summary>
public class CreatureSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    /// <summary>A stationary train with the ears on one car, and only the named sounds installed (loops marked with a trailing ~).</summary>
    sealed class Scene : IDisposable
    {
        readonly string _root = Directory.CreateTempSubdirectory("dt-creatures-").FullName;
        readonly HashSet<int> _heard = new();
        public readonly TrainOnLine Train;
        public readonly World World;
        public readonly GameAudio Audio;
        public int Rear => Train.Dynamics.Consist.Vehicles[^1].Id;

        public Scene(int earCar, params string[] sounds)
        {
            Directory.CreateDirectory(Path.Combine(_root, "audio", "sounds"));
            File.Copy(Path.Combine(Content, MixDef.File), Path.Combine(_root, MixDef.File));
            // GameAudio's spaces (the reverb per space, GameAudio.Mix) load with it.
            File.Copy(Path.Combine(Content, "audio", "spaces.json"), Path.Combine(_root, "audio", "spaces.json"));
            var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 6, 1)), line, 1200);
            World = new World(Train, DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File)));
            Audio = new GameAudio(_root);
            // The bed's wheels are taken as read (GameAudio.Bed).
            foreach (var s in sounds.Append("wheel-rail~"))
                Audio.Bank.Add(s.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 64));
            var frame = Train.Frames[earCar];
            Ear = Listener.At(frame.ToWorld(new Double3(0, frame.Shape.RoofHeight + 1.65, 0)), frame.Heading);
        }

        public Listener Ear;

        /// <summary>One tick with these records mirrored; the sounds it started.</summary>
        public List<string> Tick(params Enemy[] enemies)
        {
            World.BeginTick();
            World.MirrorEnemies(enemies);
            World.Step(new TrainControls { Reverser = 1 });
            Audio.Update(World, World.Controls, Ear, exposed: true, SimConstants.TickSeconds);
            return [.. Audio.Mixer.Voices.Where(v => _heard.Add(v.Id) && v.Name != "wheel-rail").Select(v => v.Name)];
        }

        public bool Playing(string name) => Audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    static T Record<T>(T e, SpinePhase phase, double seconds, double health, int attached, Double3 local, double s = 0, double lateral = 0,
        double height = 0, double extra = 0, double extra2 = 0, int holding = -1, double window = 0) where T : Enemy
    {
        e.Restore(phase, seconds, health, attached, local, s, lateral, height, extra, extra2, holding, window);
        return e;
    }

    [Fact]
    public void TheCarHuggerSwallowsIsHitAndBreaksAwayWithItsCarOnce()
    {
        using var scene = new Scene(5, "cs-car-hugger.swallow", "cs-car-hugger.hit", "cs-car-hugger.break-away");
        int rear = scene.Rear;
        var mouth = new Double3(0, 1, scene.Train.Frames[rear].Shape.HalfLength + 0.4);
        CarHugger Hugger(SpinePhase phase, double seconds, double health) =>
            Record(new CarHugger(46), phase, seconds, health, rear, mouth, holding: phase == SpinePhase.Grab ? 1 : -1, window: 10);

        Assert.Empty(scene.Tick(Hugger(SpinePhase.Commit, 4, 12)));
        Assert.Empty(scene.Tick(Hugger(SpinePhase.Commit, 4.03, 12.005)));
        Assert.Equal(["cs-car-hugger.swallow"], scene.Tick(Hugger(SpinePhase.Grab, 0, 12.01)));
        Assert.Empty(scene.Tick(Hugger(SpinePhase.Grab, 0.03, 12.015)));
        // A blow from the rear platform (it heals a little every tick in between: that's no blow).
        Assert.Equal(["cs-car-hugger.hit"], scene.Tick(Hugger(SpinePhase.Telegraph, 0, 11.015)));
        // Cut loose: once as the car goes, and not again as its record does.
        scene.Train.Uncouple(scene.Train.VehicleAhead(rear));
        Assert.Equal(["cs-car-hugger.break-away"], scene.Tick(Hugger(SpinePhase.Telegraph, 0.03, 11.02)));
        Assert.Empty(scene.Tick(Hugger(SpinePhase.Telegraph, 0.06, 11.025)));
        Assert.Empty(scene.Tick());
    }

    [Fact]
    public void TheWhistlerRunsWithItsVictimThenNestsAndIsHeardDying()
    {
        using var scene = new Scene(3, "cs-whistler.snatch", "cs-whistler.run~", "cs-whistler.nest~", "cs-whistler.hit", "cs-whistler.death");
        int gap = 3;
        var at = scene.Train.Frames[gap].ToWorld(CrewSense.GapLocal(scene.Train, gap));
        var victim = PlayerMotor.SpawnOnGround(at, scene.Train.Line, 1200, Player);
        var friend = victim with { Position = victim.Position + new Double3(1.2, 0, 0) };
        scene.Audio.CrewStates = [(1, victim), (2, friend)];
        Assert.Empty(scene.Tick(Record(new Whistler(22), SpinePhase.Commit, 2, 3, gap, CrewSense.GapLocal(scene.Train, gap))));

        // Off at a run (4.5 m/s) for a second: the snatch once, the run held.
        var away = new Double3(-0.15, 0, 0);
        var heard = new List<string>();
        for (int i = 0; i < 30; i++)
            heard.AddRange(scene.Tick(Record(new Whistler(22), SpinePhase.Grab, i / 30.0, 3, Enemy.Loose, at + away * i, holding: 1, window: 33)));
        Assert.Equal(1, heard.Count(h => h == "cs-whistler.snatch"));
        Assert.True(scene.Playing("cs-whistler.run"));
        Assert.False(scene.Playing("cs-whistler.nest"));

        // At the nest: the run stops, the nest's held.
        var nest = at + away * 29;
        for (int i = 30; i < 50; i++)
            scene.Tick(Record(new Whistler(22), SpinePhase.Grab, i / 30.0, 3, Enemy.Loose, nest, holding: 1, window: 33));
        Assert.False(scene.Playing("cs-whistler.run"));
        Assert.True(scene.Playing("cs-whistler.nest"));

        // Beaten to its last, then the last blow: gone in the tick it lands, with a friend at it.
        scene.Audio.CrewStates = [(1, victim), (2, friend with { Position = nest + new Double3(1, 0, 0) })];
        Assert.Equal(["cs-whistler.hit"], scene.Tick(Record(new Whistler(22), SpinePhase.Grab, 2, 1, Enemy.Loose, nest, holding: 1, window: 33)));
        var gone = scene.Tick();
        Assert.Contains("cs-whistler.hit", gone);
        Assert.Contains("cs-whistler.death", gone);
        Assert.False(scene.Playing("cs-whistler.nest"));
    }

    [Theory]
    [InlineData(3.0, false, "cs-draggers.haul-up")]
    [InlineData(7.95, false, "cs-draggers.under")]
    [InlineData(3.0, true, "cs-draggers.under")]
    public void ADraggersGrabEndsHauledUpOrDraggedUnder(double heldFor, bool victimDragged, string ending)
    {
        using var scene = new Scene(2, "cs-draggers.grab", "cs-draggers.scrabble~", "cs-draggers.haul-up", "cs-draggers.under");
        var shape = scene.Train.Frames[2].Shape;
        var edge = new Double3(-(shape.HalfWidth + 0.1), shape.RoofHeight - 0.35, 1);
        if (victimDragged)
            scene.Audio.CrewStates = [(1, PlayerMotor.SpawnOnRoof(scene.Train, 2, 1, Player))];
        scene.Tick(Record(new Dragger(21), SpinePhase.Telegraph, 0.9, 1, 2, edge, extra: 1));
        Assert.Equal(["cs-draggers.grab", "cs-draggers.scrabble"], scene.Tick(Record(new Dragger(21), SpinePhase.Grab, 0, 1, 2, edge, extra: 1, holding: 1, window: 8)));
        scene.Tick(Record(new Dragger(21), SpinePhase.Grab, heldFor, 1, 2, edge, extra: 1, holding: 1, window: 8));
        Assert.True(scene.Playing("cs-draggers.scrabble"));
        if (victimDragged)
            scene.Audio.CrewStates = [(1, PlayerMotor.SpawnOnRoof(scene.Train, 2, 1, Player) with { Health = 0, Death = DeathCause.Dragged })];
        Assert.Equal([ending], scene.Tick(Record(new Dragger(21), SpinePhase.Dormant, 0, 1, 2, edge, extra: -1)));
        Assert.False(scene.Playing("cs-draggers.scrabble"));
    }

    [Fact]
    public void HoundsGallopAtTheirSpeedLeapAboardAndHowlNearOnlyWhenClose()
    {
        using var scene = new Scene(6, "cs-hounds.paw.ground", "cs-hounds.paw.roof", "cs-hounds.leap", "cs-hounds.snarl", "hound-howl", "tell-hounds.howl-near");
        double rear = scene.Train.Dynamics.RearDistance;
        CinderHound Running(double s) => Record(new CinderHound(10, 10), SpinePhase.Commit, 2, 3, -1, default, s, 2.5, 0.6, extra: 10);

        // 60 m behind: the near howl, not the far one. A second's gallop at 19 m/s: a four-beat gallop of ~5.7 m strides.
        var heard = new List<string>();
        for (int i = 0; i < 30; i++)
            heard.AddRange(scene.Tick(Running(rear - 60 + 19.0 * i / 30)));
        Assert.Contains("tell-hounds.howl-near", heard);
        Assert.DoesNotContain("hound-howl", heard);
        Assert.InRange(heard.Count(h => h == "cs-hounds.paw.ground"), 10, 16);

        // Onto the rear car: the leap, and its landing on the roof just after.
        var shape = scene.Train.Frames[scene.Rear].Shape;
        var aboard = Record(new CinderHound(10, 10), SpinePhase.Commit, 2, 3, scene.Rear, new Double3(0.6, shape.RoofHeight, shape.HalfLength - 2.5), extra: 10);
        heard = scene.Tick(aboard);
        Assert.Contains("cs-hounds.leap", heard);
        for (int i = 0; i < 15; i++)
            heard.AddRange(scene.Tick(aboard));
        Assert.Equal(2, heard.Count(h => h == "cs-hounds.paw.roof"));
        Assert.Equal(1, heard.Count(h => h == "cs-hounds.leap"));

        // Far behind, the tell's own howl.
        using var far = new Scene(6, "hound-howl", "tell-hounds.howl-near");
        Assert.Contains("hound-howl", far.Tick(Record(new CinderHound(10, 10), SpinePhase.Telegraph, 1, 3, -1, default, far.Train.Dynamics.RearDistance - 150, 2.5, 0.6, extra: 10)));
    }

    [Fact]
    public void TheTrackDollWorksTheCabLeversWithTheirOwnSoundsUntilItHasOne()
    {
        static List<string> Tamper(Scene scene)
        {
            var cab = scene.Train.Frames[0].Shape.Cab!.Value;
            var heard = new List<string>();
            // Its beats (TrackDoll.Tamper): throttle open, then shut, then the brake on, every three seconds.
            for (double t = 2.5; t < 9.5; t += SimConstants.TickSeconds)
                heard.AddRange(scene.Tick(Record(new TrackDoll(26), SpinePhase.Punish, t, 2, 0, cab.Centre with { Y = cab.Min.Y }, extra2: 1)));
            return heard;
        }
        using (var crews = new Scene(0, "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle"))
            Assert.Equal(["crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle", "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle"],
                Tamper(crews));
        using var own = new Scene(0, "cs-track-doll.tamper", "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle");
        Assert.Equal(Enumerable.Repeat("cs-track-doll.tamper", 4), Tamper(own));
    }

    [Fact]
    public void RibbitsLandOnTheirHopClockAndTheLeadersTongueFeedsUntilLetGo()
    {
        using var scene = new Scene(2, "cs-ribbits.hop-land.ground", "cs-ribbits.tongue", "cs-ribbits.feed~", "cs-ribbits.hit");
        var start = scene.Train.Frames[2].ToWorld(new Double3(-12, 0, 0));
        var heard = new List<string>();
        var at = start;
        // Two seconds hopping in (Ribbit.Hop: half a second's leap at twice the speed, half a second sat).
        for (int i = 0; i < 60; i++)
        {
            double t = i / 30.0;
            if ((int)(t * 2 + 60) % 2 == 0)
                at += new Double3(8.0 / 30, 0, 0);
            heard.AddRange(scene.Tick(Record(new Ribbit(60, 0), SpinePhase.Dormant, t, 3, Enemy.Loose, at, extra: 1)));
        }
        Assert.Equal(2, heard.Count(h => h == "cs-ribbits.hop-land.ground"));

        Assert.Equal(["cs-ribbits.tongue", "cs-ribbits.feed"], scene.Tick(Record(new Ribbit(60, 0), SpinePhase.Grab, 0, 3, Enemy.Loose, at, extra: 1, holding: 1, window: 8)));
        Assert.Empty(scene.Tick(Record(new Ribbit(60, 0), SpinePhase.Grab, 0.03, 3, Enemy.Loose, at, extra: 1, holding: 1, window: 8)));
        Assert.True(scene.Playing("cs-ribbits.feed"));
        // Clubbed off its meal.
        Assert.Equal(["cs-ribbits.hit"], scene.Tick(Record(new Ribbit(60, 0), SpinePhase.Dormant, 0, 2, Enemy.Loose, at, extra: 1)));
        Assert.False(scene.Playing("cs-ribbits.feed"));
    }

    [Fact]
    public void ABlowFromEmptyHandsIsALimpSlap()
    {
        // A blow landing is the host's hit record (T121, note 171), heard once: the tool in the hitter's hand on flesh, or a
        // slap from empty hands, whoever else is near.
        using var scene = new Scene(2, "crew-mishaps.bare-slap", "crew-melee.crowbar-hit-flesh");
        var at = scene.Train.Frames[2].ToWorld(new Double3(-4, 0, 0));
        var near = PlayerMotor.SpawnOnGround(at + new Double3(0.6, 0, 0), scene.Train.Line, 1200, Player);
        var toad = Record(new Ribbit(61, 0), SpinePhase.Dormant, 0, 3, Enemy.Loose, at, extra: 1);
        scene.Audio.CrewStates = [(1, near with { Kit = 0 }), (2, near with { Kit = Kit.Of([Tool.Crowbar]), HeldSlot = 0, Position = near.Position + new Double3(0.3, 0, 0) })];
        scene.Tick(toad);
        HitConfirm Blow(int id, int by) => new(id, scene.World.Tick, 61, EnemyKind.Ribbit, by, HitSource.Melee, at + Double3.Up * 0.8, Double3.Up, false);
        scene.World.Hits.Add(Blow(1, by: 1));
        Assert.Contains("crew-mishaps.bare-slap", scene.Tick(toad));
        scene.World.Hits.Add(Blow(2, by: 2));
        var heard = scene.Tick(toad);
        Assert.Contains("crew-melee.crowbar-hit-flesh", heard);
        Assert.DoesNotContain("crew-mishaps.bare-slap", heard);
    }

    [Fact]
    public void ThePassengerWalksInTheCrewsOwnBootsAndDragsItsVictim()
    {
        using var scene = new Scene(5, "crew-footsteps.walk.wood", "cs-passenger.drag.wood~");
        var room = scene.Train.Frames[5].Shape.Interior!.Value;
        var heard = new List<string>();
        // Three seconds at walking pace (1.3 m/s): a step every 0.75 m.
        for (int i = 0; i < 90; i++)
            heard.AddRange(scene.Tick(Record(new Passenger(48), SpinePhase.Telegraph, 6 + i / 30.0, 5, 5,
                room.Centre with { Y = room.Min.Y, Z = room.Max.Z - 1 - 1.3 * i / 30 }, extra: 2)));
        Assert.InRange(heard.Count(h => h == "crew-footsteps.walk.wood"), 4, 6);
        scene.Tick(Record(new Passenger(48), SpinePhase.Grab, 0, 5, 5, room.Centre with { Y = room.Min.Y }, extra: 2, holding: 1, window: 600));
        Assert.True(scene.Playing("cs-passenger.drag.wood"));
    }

    [Fact]
    public void TheChoirIsHeardArrivingBangingOnTheDoorAndLeaving()
    {
        using var scene = new Scene(3, "cs-choir.arrive~", "cs-choir.disperse", "cs-choir.bang-door.wood", "cs-choir.seize");
        var over = scene.Train.Frames[3].ToWorld(new Double3(0, 6, 0));
        ChoirGhost Ghost(SpinePhase phase, double target) => Record(new ChoirGhost(70), phase, 2, 6, Enemy.Loose, over, extra: target);
        // Inside car 3 with its doors shut while they besiege it: banging, now and then.
        var inside = scene.Train.Frames[3].Shape.Interior!.Value;
        scene.Audio.CrewStates = [(1, new PlayerState { Parent = 3, Position = inside.Centre with { Y = inside.Min.Y }, Surface = Surface.Deck, Health = 100 })];
        scene.World.Choir = new ChoirState { Present = true, Build = 1 };
        var heard = new List<string>();
        for (int i = 0; i < 90; i++)
            heard.AddRange(scene.Tick(Ghost(SpinePhase.Commit, -1)));
        Assert.True(scene.Playing("cs-choir.arrive"));
        Assert.InRange(heard.Count(h => h == "cs-choir.bang-door.wood"), 2, 5);

        Assert.Equal(["cs-choir.seize"], scene.Tick(Ghost(SpinePhase.Grab, 1)));
        scene.World.Choir = ChoirState.Quiet;
        Assert.Equal(["cs-choir.disperse"], scene.Tick());
        Assert.False(scene.Playing("cs-choir.arrive"));
        Assert.Empty(scene.Tick());
    }
}
