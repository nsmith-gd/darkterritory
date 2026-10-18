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
    public void TheCarHuggerIsHeardSpittingOutACrewmatePulledFreeButNotOneEaten()
    {
        // Note 310: the crew pull a swallowed crewmate back out of its mouth. Eaten or pulled free, its grab ends the same
        // way on the wire (back to grinding on the car), so it's the one it held still alive that's heard getting out.
        using var scene = new Scene(5, "cs-car-hugger.swallow", "cs-car-hugger.hit", "cs-car-hugger.spit-out");
        int rear = scene.Rear;
        var mouth = new Double3(0, 1, scene.Train.Frames[rear].Shape.HalfLength + 0.4);
        CarHugger Hugger(SpinePhase phase, double seconds) =>
            Record(new CarHugger(46), phase, seconds, 12, rear, mouth, holding: phase == SpinePhase.Grab ? 1 : -1, window: 10);
        scene.Audio.CrewStates = [(1, new PlayerState { Parent = rear, Health = Player.Health })];
        scene.Tick(Hugger(SpinePhase.Commit, 4));
        Assert.Equal(["cs-car-hugger.swallow"], scene.Tick(Hugger(SpinePhase.Grab, 0)));
        Assert.Equal(["cs-car-hugger.spit-out"], scene.Tick(Hugger(SpinePhase.Telegraph, 0)));
        Assert.Empty(scene.Tick(Hugger(SpinePhase.Telegraph, 0.03)));
        // Swallowed again, and eaten this time: nobody comes out.
        Assert.Equal(["cs-car-hugger.swallow"], scene.Tick(Hugger(SpinePhase.Grab, 0)));
        scene.Audio.CrewStates = [(1, new PlayerState { Parent = rear, Health = 0, Death = DeathCause.Eaten })];
        Assert.Empty(scene.Tick(Hugger(SpinePhase.Telegraph, 0)));
    }

    [Fact]
    public void ABallOrABlowThatDoesntKillACreatureIsItsOwnHurt()
    {
        // Note 290: the gun hits what it's laid on, and per-creature pain is the audio chat's (note 322). Health dropping on
        // a record still there is a hurt (a killing blow takes the record, and that's its death: Vanished).
        using var scene = new Scene(1, "cs-gaunt.hit", "cs-soot-children.hit", "cs-followers.hit",
            "cs-grumbler.hit", "cs-grumbler.feral");
        double s = scene.Train.Dynamics.Distance - 40;
        var shape = scene.Train.Frames[2].Shape;
        var inside = new Double3(0, shape.Interior!.Value.Min.Y, 0);
        (Func<double, double, Enemy> Make, string Hurt)[] kinds =
        [
            ((h, x) => Record(new Gaunt(60), SpinePhase.Telegraph, 1, h, -1, default, s, 12), "cs-gaunt.hit"),
            ((h, x) => Record(new SootChildren(62), SpinePhase.Telegraph, 1, h, -1, default, s, 9), "cs-soot-children.hit"),
            ((h, x) => Record(new Follower(63), SpinePhase.Telegraph, 1, h, -1, default, s, 7), "cs-followers.hit"),
            ((h, x) => Record(new Grumbler(64), SpinePhase.Telegraph, 1, h, 2, inside, extra2: x), "cs-grumbler.hit"),
        ];
        foreach (var (make, hurt) in kinds)
        {
            Assert.DoesNotContain(hurt, scene.Tick(make(10, 0)));
            Assert.Equal([hurt], scene.Tick(make(7, 0)));
            Assert.Empty(scene.Tick(make(7, 0)));
            Assert.DoesNotContain(hurt, scene.Tick());
        }
        // The blow that turns the Grumbler feral is heard as it turning, not as a hurt too.
        scene.Tick(Record(new Grumbler(65), SpinePhase.Telegraph, 1, 10, 2, inside));
        Assert.Equal(["cs-grumbler.feral"], scene.Tick(Record(new Grumbler(65), SpinePhase.Telegraph, 1, 7, 2, inside, extra2: 1)));
    }

    [Fact]
    public void AGrumblerIsHeardHealingALoneBlowLouderTheFurtherDownItIs()
    {
        // App. A.8 ("heals if only one player has hit it"; note 494, E1's #224 shows it): its health climbing back after a
        // lone blow (enemies.json grumbler: 6, at 1.5 a second) is its healing, held, louder the further down it is; back
        // at full, it stops. A gang's blows, which it doesn't heal, are never heard healing.
        using var scene = new Scene(1, "cs-grumbler.hit", "cs-grumbler.heal~");
        var shape = scene.Train.Frames[2].Shape;
        var inside = new Double3(0, shape.Interior!.Value.Min.Y, 0);
        Grumbler At(double health) => Record(new Grumbler(66), SpinePhase.Telegraph, 1, health, 2, inside);
        float? Level() => scene.Audio.Mixer.Voices.FirstOrDefault(v => v.Name == "cs-grumbler.heal" && !v.Stopped)?.Volume;
        scene.Tick(At(6));
        Assert.Equal(["cs-grumbler.hit"], scene.Tick(At(2)));
        Assert.False(scene.Playing("cs-grumbler.heal"));
        double health = 2;
        var heard = new List<string>();
        float? deep = null, shallow = null;
        while (health < 6)
        {
            health = Math.Min(6, health + 1.5 * SimConstants.TickSeconds);
            heard.AddRange(scene.Tick(At(health)));
            Assert.True(scene.Playing("cs-grumbler.heal"));
            if (health < 2.5)
                deep ??= Level();
            if (health > 5.5)
                shallow ??= Level();
        }
        Assert.Equal(1, heard.Count(h => h == "cs-grumbler.heal"));
        Assert.True(deep > shallow, $"healing from 2 of 6 at {deep}, from 5.5 at {shallow}");
        for (int i = 0; i < 30; i++)
            scene.Tick(At(6));
        Assert.False(scene.Playing("cs-grumbler.heal"));
        // Ganged: knocked down and it stays down. Nothing heals.
        heard.Clear();
        for (double h = 6; h > 0.5; h -= 1.5)
            for (int i = 0; i < 15; i++)
                heard.AddRange(scene.Tick(At(h)));
        Assert.DoesNotContain("cs-grumbler.heal", heard);
        Assert.False(scene.Playing("cs-grumbler.heal"));
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
    public void ATrussDraggerLandsOnTheRoofBeforeItsGrabOrFallsOnTheBallastBehind()
    {
        // Note 444 (D1's #171, note 435): perched on a truss's chord (no car of its own, high over the line) in its tell, then
        // on the roof of the car passing under: its landing as it comes down, its grab a moment after. Perched still when it
        // goes (the train under it with nobody on the roofs): its fall onto the ballast, under the chord.
        using var scene = new Scene(2, "cs-draggers.drop", "cs-draggers.fall", "cs-draggers.grab", "cs-draggers.scrabble~", "dragger-scrape~");
        var shape = scene.Train.Frames[2].Shape;
        var edge = new Double3(shape.HalfWidth + 0.1, shape.RoofHeight - 0.35, 1);
        double chord = scene.Train.Dynamics.Distance - 25;
        Dragger Perched(int id) => Record(new Dragger(id), SpinePhase.Telegraph, 1.0, 1, -1, default, chord, 2.7, 7.2, extra: -1);

        scene.Tick(Perched(21));
        var landed = scene.Tick(Record(new Dragger(21), SpinePhase.Grab, 0, 1, 2, edge, extra: 1, holding: 1, window: 8));
        Assert.Contains("cs-draggers.drop", landed);
        Assert.DoesNotContain("cs-draggers.grab", landed);
        var after = new List<string>();
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            after.AddRange(scene.Tick(Record(new Dragger(21), SpinePhase.Grab, (i + 1) * SimConstants.TickSeconds, 1, 2, edge, extra: 1, holding: 1, window: 8)));
        Assert.Single(after, n => n == "cs-draggers.grab");
        Assert.DoesNotContain("cs-draggers.drop", after);
        Assert.DoesNotContain("cs-draggers.fall", landed.Concat(after));

        // Another on the chord, and the train gone under it with nobody up there: it drops onto the ballast, under the chord.
        scene.Tick(Perched(22));
        var high = scene.World.ActiveEnemies.Single(e => e.Id == 22).WorldPosition(scene.Train);
        var gone = scene.Tick();
        Assert.Contains("cs-draggers.fall", gone);
        Assert.DoesNotContain("cs-draggers.drop", gone);
        var fall = scene.Audio.Mixer.Voices.Single(v => v.Name == "cs-draggers.fall");
        Assert.Equal(high.Y - 7.2, fall.Position.Y, 1);
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
    public void AHoundAboardIsHeardOnItsFeetOnTheRoofOverAGapAndOnTheBoardsInside()
    {
        // Note 478 (D1's #208: hounds aboard patrol the roofs, jump the gaps they can make, and go in and out of cars whose
        // doors stand open): its paws at its walk on what it's on; standing, nothing. Its moves by its replicated mode
        // (note 489, CinderHound.Aboard, as mode × 4 + facing in Lateral, begun at LineDistance): over the gap, the spring
        // off the roof and its four feet landing on the next one's tin; down in at a door, the spring off the roof's edge
        // and its landing on the boards; out again, its climb; stopped, its sniffing, and nothing underfoot.
        using var scene = new Scene(5, "cs-hounds.paw.ground", "cs-hounds.paw.roof", "cs-hounds.paw.wood", "cs-hounds.leap", "cs-hounds.snarl",
            "cs-hounds.spring", "cs-hounds.climb", "cs-hounds.sniff~");
        int rear = scene.Rear, next = rear - 1;
        var shape = scene.Train.Frames[rear].Shape;
        double secs = 2;
        CinderHound On(int car, Double3 local, HoundMode mode = HoundMode.Still, double began = 0) =>
            Record(new CinderHound(10, 10), SpinePhase.Commit, secs, 3, car, local, s: began, lateral: (int)mode * 4, extra: 10);
        var roof = new Double3(0, shape.RoofHeight, shape.HalfLength - 2);
        var heard = new List<string>();
        int Count(string name) => heard.Count(h => h == name);
        List<string> Tick(CinderHound h)
        {
            secs += SimConstants.TickSeconds;
            return scene.Tick(h);
        }
        for (int i = 0; i < 10; i++)
            heard.AddRange(Tick(On(rear, roof)));
        heard.Clear();
        // Standing on the roof: nothing underfoot.
        for (int i = 0; i < 30; i++)
            heard.AddRange(Tick(On(rear, roof)));
        Assert.Equal(0, Count("cs-hounds.paw.roof"));
        // Two seconds' walk forward along it at 1.2 m/s: a walk's paws on the tin, four to a stride of about a metre.
        for (int i = 1; i <= 60; i++)
            heard.AddRange(Tick(On(rear, roof with { Z = roof.Z - 1.2 * i / 30 }, HoundMode.Patrol)));
        Assert.InRange(Count("cs-hounds.paw.roof"), 8, 16);
        Assert.Equal(0, Count("cs-hounds.spring"));
        // Over the gap onto the next car's roof (enemies.json's patrol: 0.9 s, carried across between a quarter and two
        // thirds of it, its car changing halfway): the spring as it drives off, nothing in the air, four paws as it lands.
        heard.Clear();
        var edge = roof with { Z = -shape.HalfLength + 0.8 };
        var over = new Double3(0, shape.RoofHeight, scene.Train.Frames[next].Shape.HalfLength - 0.8);
        double began = secs + SimConstants.TickSeconds;
        var times = new List<(string Name, double At)>();
        for (int i = 0; i < 27; i++)
        {
            double f = Math.Clamp((i / 30.0 / 0.9 - 0.25) / 0.4, 0, 1);
            foreach (var name in Tick(f < 0.5 ? On(rear, edge with { Z = edge.Z - 1.6 * f }, HoundMode.Leap, began)
                         : On(next, over with { Z = over.Z + 1.6 * (1 - f) }, HoundMode.Leap, began)))
                times.Add((name, secs - began));
        }
        for (int i = 0; i < 15; i++)
            foreach (var name in Tick(On(next, over, HoundMode.Patrol)))
                times.Add((name, secs - began));
        heard.AddRange(times.Select(t => t.Name));
        Assert.Equal(1, Count("cs-hounds.spring"));
        Assert.Equal(4, Count("cs-hounds.paw.roof"));
        Assert.Equal(0, Count("cs-hounds.leap"));
        Assert.InRange(times.Single(t => t.Name == "cs-hounds.spring").At, 0.2, 0.27);
        Assert.InRange(times.First(t => t.Name == "cs-hounds.paw.roof").At, 0.57, 0.64);
        // Down in at its open door (the record on the floor from the first tick): the spring off the roof's edge, then its
        // landing on the boards as the clip has it, and walking there, the boards.
        heard.Clear();
        var room = scene.Train.Frames[next].Shape.Interior!.Value;
        var floor = new Double3(room.Max.X - 0.4, room.Min.Y, room.Centre.Z);
        began = secs + SimConstants.TickSeconds;
        for (int i = 0; i < 30; i++)
            heard.AddRange(Tick(On(next, floor, HoundMode.Drop, began)));
        // (its hind feet come down just after the drop's second, as it stands)
        for (int i = 0; i < 5; i++)
            heard.AddRange(Tick(On(next, floor, HoundMode.Patrol)));
        Assert.Equal(1, Count("cs-hounds.spring"));
        Assert.Equal(4, Count("cs-hounds.paw.wood"));
        Assert.Equal(0, Count("cs-hounds.paw.roof"));
        for (int i = 1; i <= 30; i++)
            heard.AddRange(Tick(On(next, floor with { Z = floor.Z + 1.2 * i / 30 }, HoundMode.Patrol)));
        Assert.InRange(Count("cs-hounds.paw.wood"), 4 + 4, 4 + 9);
        // Up and out at the door (1.2 s at it, then put on the roof above): the climb, once, and no paws for either.
        heard.Clear();
        var door = floor with { Z = floor.Z + 1.2 };
        began = secs + SimConstants.TickSeconds;
        for (int i = 0; i < 36; i++)
            heard.AddRange(Tick(On(next, door, HoundMode.Climb, began)));
        var up = new Double3(0.6, shape.RoofHeight, door.Z);
        for (int i = 0; i < 5; i++)
            heard.AddRange(Tick(On(next, up, HoundMode.Patrol)));
        Assert.Equal(1, Count("cs-hounds.climb"));
        Assert.Equal(0, Count("cs-hounds.paw.roof") + Count("cs-hounds.paw.wood") + Count("cs-hounds.spring"));
        Assert.Equal(0, Count("cs-hounds.snarl"));
        // Stopped to sniff (3 s): its sniffing held, nothing underfoot; on its way again, the sniffing stops.
        began = secs + SimConstants.TickSeconds;
        for (int i = 0; i < 90; i++)
            heard.AddRange(Tick(On(next, up, HoundMode.Sniff, began)));
        Assert.True(scene.Playing("cs-hounds.sniff"));
        Assert.Equal(0, Count("cs-hounds.snarl"));
        for (int i = 1; i <= 60; i++)
            heard.AddRange(Tick(On(next, up with { Z = up.Z - 1.2 * i / 30 }, HoundMode.Patrol)));
        Assert.False(scene.Playing("cs-hounds.sniff"));
        Assert.Equal(1, Count("cs-hounds.sniff"));
        Assert.Equal(0, Count("cs-hounds.paw.ground"));
    }

    [Fact]
    public void TheTrackDollWorksTheCabLeversWithTheirOwnSoundsUntilItHasOne()
    {
        static List<string> Tamper(Scene scene)
        {
            var cab = scene.Train.Frames[0].Shape.Cab!.Value;
            var heard = new List<string>();
            // Its beats at her last stage (TrackDoll.Tamper, note 268): throttle open, then shut, then the brake on, every 3 s.
            for (double t = 2.5; t < 9.5; t += SimConstants.TickSeconds)
                heard.AddRange(scene.Tick(Record(new TrackDoll(26), SpinePhase.Punish, t, 2, 0, cab.Centre with { Y = cab.Min.Y }, extra2: 3)));
            return heard;
        }
        using (var crews = new Scene(0, "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle"))
            Assert.Equal(["crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle", "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle"],
                Tamper(crews));
        using var own = new Scene(0, "cs-track-doll.tamper", "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle");
        Assert.Equal(Enumerable.Repeat("cs-track-doll.tamper", 4), Tamper(own));
    }

    [Fact]
    public void RestlessAtTheRegulatorTheTrackDollRattlesTheBrakeHandleItHasntTakenYet()
    {
        // Note 268: at stage 2 she nudges the regulator up and lets it back; restless (extra2 2.5, her last stage coming)
        // she rattles the brake handle on the beat she'll take it, without moving it: the telegraph before the brake goes.
        using var scene = new Scene(0, "crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle");
        var cab = scene.Train.Frames[0].Shape.Cab!.Value;
        List<string> Heard(double escalation)
        {
            var heard = new List<string>();
            for (double t = 2.5; t < 9.5; t += SimConstants.TickSeconds)
                heard.AddRange(scene.Tick(Record(new TrackDoll(26), SpinePhase.Punish, t, 2, 0, cab.Centre with { Y = cab.Min.Y }, extra2: escalation)));
            return heard;
        }
        Assert.DoesNotContain("crew-cab-controls.brake-handle", Heard(2));
        // Let back at 3 s, the rattle at 6, nudged up again at 9.
        Assert.Equal(["crew-cab-controls.regulator-notch", "crew-cab-controls.brake-handle", "crew-cab-controls.regulator-notch"], Heard(2.5));
    }

    [Fact]
    public void RestlessTheTrackDollIsHeardOfHerOwnAndRattlesTheBrakeWithHerOwnHand()
    {
        // Note 499 (note 268's "not yet": "the doll has no recorded 'restless' sound of her own (the faster giggle and the
        // crew's brake handle stand in)"): restless (her escalation's half, the next stage coming), now and then her own,
        // in a car or at the controls; not restless, never. At stage 2 she rattles the brake with her own hand, not the
        // crew's lever, on the beat she'll take it.
        using var scene = new Scene(2, "cs-track-doll.restless", "cs-track-doll.rattle", "crew-cab-controls.regulator-notch",
            "crew-cab-controls.brake-handle");
        var floor = new Double3(0, scene.Train.Frames[2].Shape.Interior!.Value.Min.Y, 0);
        var cab = scene.Train.Frames[0].Shape.Cab!.Value;
        List<string> Heard(int car, Double3 local, double escalation, double from, double to)
        {
            var heard = new List<string>();
            for (double t = from; t < to; t += SimConstants.TickSeconds)
                heard.AddRange(scene.Tick(Record(new TrackDoll(27), SpinePhase.Punish, t, 2, car, local, extra2: escalation)));
            return heard;
        }
        // Haunting a car at stage 1, 20 s: not restless, nothing of hers; restless, 20 s: her own every 4-8 s.
        Assert.DoesNotContain("cs-track-doll.restless", Heard(2, floor, -1, 0, 20));
        Assert.InRange(Heard(2, floor, -1.5, 20, 40).Count(h => h == "cs-track-doll.restless"), 3, 5);
        // At the controls at stage 2, a while; then restless: let back at 3 s, her rattle at 6, nudged up again at 9; never
        // the crew's brake handle.
        Heard(0, cab.Centre with { Y = cab.Min.Y }, 2, 2.5, 9.5);
        var heard = Heard(0, cab.Centre with { Y = cab.Min.Y }, 2.5, 2.5, 9.5);
        Assert.Equal(["crew-cab-controls.regulator-notch", "cs-track-doll.rattle", "crew-cab-controls.regulator-notch"],
            heard.Where(h => h != "cs-track-doll.restless"));
        Assert.DoesNotContain("crew-cab-controls.brake-handle", heard);
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
    public void AKilledCreatureCrumblesAsItsSeenToAndOneThatGoesOtherwiseDoesnt()
    {
        // Note 244: killed is the host's word (HitConfirm.Killed), and a creature that stands on something goes over and
        // crumbles from halfway through Effects.DeathSeconds (note 208): the crumble's heard then, once.
        using var scene = new Scene(2, "creature-crumble", "cs-ribbits.hit");
        var at = scene.Train.Frames[2].ToWorld(new Double3(-4, 0, 0));
        Ribbit Toad(int id) => Record(new Ribbit(id, 0), SpinePhase.Dormant, 0, 3, Enemy.Loose, at, extra: 1);
        HitConfirm Blow(int id, int enemy, bool killed) =>
            new(id, scene.World.Tick, enemy, EnemyKind.Ribbit, 1, HitSource.Melee, at + Double3.Up * 0.5, Double3.Up, killed);
        scene.Tick(Toad(70), Toad(71));
        // One's killed, and its record goes with the blow.
        scene.World.Hits.Add(Blow(1, 70, killed: true));
        var heard = scene.Tick(Toad(71));
        Assert.Contains("cs-ribbits.hit", heard);
        Assert.DoesNotContain("creature-crumble", heard);
        var later = new List<string>();
        for (int i = 0; i < (int)(DarkTerritory.Game.Art.Effects.DeathSeconds * 0.5 * SimConstants.TickRate) + 2; i++)
            later.AddRange(scene.Tick(Toad(71)));
        Assert.Single(later, h => h == "creature-crumble");
        // The other's struck and goes, but not killed (it hopped off): no death, no crumble, whatever its health was.
        scene.World.Hits.Add(Blow(2, 71, killed: false));
        scene.Tick(Record(new Ribbit(71, 0), SpinePhase.Dormant, 0, 0.5, Enemy.Loose, at, extra: 1));
        var after = new List<string>();
        for (int i = 0; i < SimConstants.TickRate * 2; i++)
            after.AddRange(scene.Tick());
        Assert.DoesNotContain("creature-crumble", after);
        Assert.DoesNotContain("cs-ribbits.hit", after);
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

    [Fact]
    public void TheMooseIsHeardGrazingFallsSilentListeningWarnsAndChargesAndBoomsOnEachRam()
    {
        // Note 334 (queue #73), read off the record as G1 answered on #247: the mode in Height, the aggro in Extra2, the rams in
        // Health (1 + rams).
        string[] grazing = ["tell-moose-grazing.browse", "tell-moose-grazing.creak", "tell-moose-grazing.grunt"];
        string[] warning = ["tell-moose-warning.grunt", "tell-moose-warning.clack", "tell-moose-warning.hoof-drag"];
        using var scene = new Scene(1, [.. grazing, "tell-moose-grazing.chew~", .. warning, "tell-moose-square-up.stamp", "tell-moose-square-up.snort",
            "tell-moose-charge.hooves~", "tell-moose-charge.wheeze~", "tell-moose-charge.brush", "cs-moose-ram.boom", "cs-moose-ram.scrape",
            "cs-moose-train-pass.bellow", "cs-moose-train-pass.thrash"]);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Moose;
        var at = scene.Train.Frames[1].ToWorld(new Double3(40, 0, 0));
        Moose M(MooseMode mode, double aggro, int rams = 0) =>
            Record(new Moose(7), SpinePhase.Dormant, 1, 1 + rams, -1, at, height: (double)mode, extra: -1, extra2: aggro);
        var heard = new List<string>();
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MooseMode.Graze, 0)));
        Assert.True(scene.Playing("tell-moose-grazing.chew"));
        Assert.Contains(heard, grazing.Contains);
        Assert.DoesNotContain(heard, warning.Contains);
        // Listening: the chewing stops, and nothing plays.
        heard.Clear();
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MooseMode.Graze, (t.ListenAt + t.WarnAt) / 2)));
        Assert.False(scene.Playing("tell-moose-grazing.chew"));
        Assert.Empty(heard);
        // Warning: its grunts, clacks and pawing.
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MooseMode.Graze, t.WarnAt + 10)));
        Assert.NotEmpty(heard);
        Assert.All(heard, h => Assert.Contains(h, warning));
        // Squaring up: two stamps and a snort, once.
        heard.Clear();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MooseMode.SquareUp, 100)));
        Assert.Equal(["tell-moose-square-up.stamp", "tell-moose-square-up.stamp", "tell-moose-square-up.snort"], heard);
        // Charging: the hooves and the wheeze held.
        scene.Tick(M(MooseMode.Charge, 100));
        Assert.True(scene.Playing("tell-moose-charge.hooves") && scene.Playing("tell-moose-charge.wheeze"));
        // Ramming a car: a boom on each ram, not between them.
        heard.Clear();
        heard.AddRange(scene.Tick(M(MooseMode.Ram, 100)));
        heard.AddRange(scene.Tick(M(MooseMode.Ram, 100, rams: 1)));
        heard.AddRange(scene.Tick(M(MooseMode.Ram, 100, rams: 1)));
        heard.AddRange(scene.Tick(M(MooseMode.Ram, 100, rams: 2)));
        Assert.Equal(2, heard.Count(h => h == "cs-moose-ram.boom"));
    }

    [Fact]
    public void TheMooseGrazingIsHeardFromTheLineButQuietEnoughToCreepCloser()
    {
        // The director's 16 Oct playtest, line 19 (queue #562, note 834): "The munching is a bit loud ... We do need to be able
        // to hear it, but we don't want to hear it maybe so loud. We want to allow players to get a little bit closer." The
        // installed sounds, as heard at a distance: where its temper starts to rise (crowdAt) the chewing is well under its own
        // level, so it doesn't sound on top of you; it's still there from the train going by (trainPassAt); and it grows as
        // you creep in, so how close you are is in the sound.
        var audio = new GameAudio(Content);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Moose;
        double Db(string name, double d)
        {
            audio.Mixer.StopAll();
            audio.Mixer.Listener = Listener.At(Double3.Zero, 0);
            var v = audio.Mixer.Play(name, new Double3(0, 0, -d))!;
            audio.Mixer.Render(new float[Ballast.Audio.Audio.Block * 2]);
            return 20 * Math.Log10(v.AudibleGain);
        }
        foreach (var cue in new[] { "chew", "browse", "creak", "grunt" })
        {
            string name = $"tell-moose-grazing.{cue}";
            Assert.True(Db(name, t.CrowdAt) < -8, $"{name} at {t.CrowdAt} m: {Db(name, t.CrowdAt):0.0} dB");
            Assert.True(Db(name, t.TrainPassAt) > -16, $"{name} from the train going by: {Db(name, t.TrainPassAt):0.0} dB");
            Assert.True(Db(name, t.CloseAt / 2) > Db(name, t.CloseAt) + 4 && Db(name, t.CloseAt) > Db(name, t.CrowdAt) + 3);
        }
    }

    [Fact]
    public void TheGannetCallsOverheadFallsSilentOverAWalkerWhistlesDownBanksScreamingAndPecksOnItsBeat()
    {
        // Note 384 (queue #121), off its record (note 340): its GannetMode in Height, its pecks on the spine's grab clock.
        string[] calls = ["tell-gannet-calls.call", "tell-gannet-calls.wings"];
        using var scene = new Scene(1, [.. calls, "tell-gannet-fold.crack", "tell-gannet-fold.whistle", "tell-gannet-bank.scream",
            "tell-gannet-bank.wingbeats~", "cs-gannet-strike.stab", "cs-gannet-strike.thunk", "cs-gannet-strike.thrash~", "cs-gannet-strike.tear",
            "cs-gannet-strike.land", "cs-gannet-strike.windup", "cs-gannet-strike.peck", "cs-gannet-strike.driven", "cs-gannet-strike.hit",
            "cs-gannet-strike.death"]);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Gannet;
        var roof = new Double3(0, scene.Train.Frames[1].Shape.RoofHeight, 2);
        var up = roof + new Double3(8, 25, 0);
        Gannet G(GannetMode mode, Double3 local, SpinePhase phase = SpinePhase.Dormant, double seconds = 1, double health = 12, int holding = -1) =>
            Record(new Gannet(9), phase, seconds, health, 1, local, height: (double)mode, extra: -1, extra2: -1, holding: holding,
                window: holding >= 0 ? t.Pecks * t.PeckEvery : 0);
        int Ticks(double s) => (int)Math.Round(s * SimConstants.TickRate);
        const double dt = SimConstants.TickSeconds;
        var walker = new PlayerState { Parent = 1, Position = roof, Health = Player.Health };
        scene.Audio.CrewStates = [(1, walker)];
        var heard = new List<string>();
        void Run(double seconds, Func<double, Gannet> g)
        {
            for (int i = 0; i < Ticks(seconds); i++)
                heard.AddRange(scene.Tick(g(i * dt)));
        }

        // Soaring over the train: its calls and wingbeats, now and then.
        Run(20, _ => G(GannetMode.Soar, up));
        Assert.Contains("tell-gannet-calls.call", heard);
        Assert.All(heard, h => Assert.Contains(h, calls));
        // Hanging over a walker: nothing (the calls stop: the silence is the tell).
        heard.Clear();
        Run(t.HangSeconds, s => G(GannetMode.Hang, roof + new Double3(0, 20, 0), SpinePhase.Alert, s));
        Assert.Empty(heard);
        // The fold: the wings' crack and the whistle down its line, once.
        Run(t.FoldSeconds, s => G(GannetMode.Fold, roof + new Double3(0, 20 * (1 - s / t.FoldSeconds), 0), SpinePhase.Telegraph, s));
        Assert.Equal(["tell-gannet-fold.crack", "tell-gannet-fold.whistle"], heard);
        // A miss: the thunk, thrashing while it's stuck, and the tear free as the art's begins.
        heard.Clear();
        Run(t.StuckSeconds, s => G(GannetMode.Stuck, roof, SpinePhase.Commit, s));
        Assert.Equal(["cs-gannet-strike.thunk", "cs-gannet-strike.thrash", "cs-gannet-strike.tear"], heard);
        Run(3, _ => G(GannetMode.Climb, up));
        // Another dive, and this one lands: the stab, as it climbs out with the walker hurt under it.
        heard.Clear();
        Run(t.FoldSeconds, s => G(GannetMode.Fold, roof + new Double3(0, 20 * (1 - s / t.FoldSeconds), 0), SpinePhase.Telegraph, s));
        scene.Audio.CrewStates = [(1, walker with { Health = Player.Health - (int)t.StabDamage })];
        Run(dt, _ => G(GannetMode.Climb, roof + new Double3(0, 0.5, 0)));
        Assert.Equal(["tell-gannet-fold.crack", "tell-gannet-fold.whistle", "cs-gannet-strike.stab"], heard);
        // The bank for its mark: the scream, and the wingbeats held as it comes.
        heard.Clear();
        Run(t.BankSeconds, s => G(GannetMode.Bank, up, SpinePhase.Telegraph, s));
        Assert.Equal(["tell-gannet-bank.scream", "tell-gannet-bank.wingbeats"], heard);
        Assert.True(scene.Playing("tell-gannet-bank.wingbeats"));
        // The pin: its weight landing, then each peck's wind-up and blow on the beat, four of them.
        heard.Clear();
        Run(t.Pecks * t.PeckEvery - 0.05, s => G(GannetMode.Pin, roof, SpinePhase.Grab, s, holding: 1));
        Assert.False(scene.Playing("tell-gannet-bank.wingbeats"));
        Assert.Equal(["cs-gannet-strike.land", .. Enumerable.Repeat<string[]>(["cs-gannet-strike.windup", "cs-gannet-strike.peck"], t.Pecks).SelectMany(p => p)], heard);
        // Driven off with its victim alive: a screech as it lurches up. A blow on it after, a hurt.
        heard.Clear();
        Run(dt, _ => G(GannetMode.Climb, roof + new Double3(0, 2, 0)));
        Run(dt, _ => G(GannetMode.Climb, roof + new Double3(0, 3, 0), health: 11));
        Assert.Equal(["cs-gannet-strike.driven", "cs-gannet-strike.hit"], heard);
        // Shot dead in the air over the car: its crash when it's fallen to the roof, not before.
        scene.Audio.CrewStates = [];
        Run(dt, _ => G(GannetMode.Soar, up, health: 1));
        heard.Clear();
        heard.AddRange(scene.Tick());
        Assert.Empty(heard);
        for (int i = 0; i < Ticks(3); i++)
            heard.AddRange(scene.Tick());
        Assert.Equal(["cs-gannet-strike.death"], heard);
    }

    [Fact]
    public void TheGannetsFoldWhistlesUpItsOwnBandNotTheTrainWhistlesAndItsCallsSitInTheirs()
    {
        // Spec A.4's Gannet row (note 384; the design's §10: "dt audio render for the fold's whistle against the Whistler's"):
        // the fold is a whistle of air at 2-5 kHz rising to the strike, where the train's whistle (the Whistler's tell) is
        // 200-800 Hz and steady; its calls overhead are 1-3 kHz.
        var (report, mix) = AudioBench.RenderSound(Content, "tell-gannet-fold.whistle");
        Assert.Null(report.Error);
        double own = Meter.BandDb(mix, 2000, 5000), whistle = Meter.BandDb(mix, 200, 800);
        Assert.True(own > whistle + 20, $"{own:0.0} dB at 2-5 kHz, {whistle:0.0} dB in the train whistle's 200-800 Hz");
        int half = mix.Length / 4 * 2;
        var first = mix.AsSpan(0, half);
        var second = mix.AsSpan(half);
        static double Tilt(ReadOnlySpan<float> x) => Meter.BandDb(x, 3500, 5000) - Meter.BandDb(x, 2000, 3500);
        Assert.True(Tilt(second) > Tilt(first) + 3, $"not rising: {Tilt(first):0.0} dB then {Tilt(second):0.0} dB top over bottom");
        Assert.True(Meter.BandDb(second, 2000, 5000) > Meter.BandDb(first, 2000, 5000) + 6, "no crescendo to the strike");
        var (called, calls) = AudioBench.RenderSound(Content, "tell-gannet-calls.call");
        Assert.Null(called.Error);
        double band = Meter.BandDb(calls, 1000, 3000), below = Meter.BandDb(calls, 80, 1000), above = Meter.BandDb(calls, 3000, 12000);
        Assert.True(band > below + 6 && band > above + 6, $"its calls {band:0.0} dB at 1-3 kHz, {below:0.0} under, {above:0.0} over");
    }

    [Fact]
    public void ASignShownAfootIsHeardOnceAsItsOwnSoundAndTheGauntsIsItsEyesAlone()
    {
        // Note 342 (queue #79): a sign shown a crewmate afoot (note 327, World.Watcher) is heard once as it starts, from where
        // its eyes are: the checklist's sign.<kind> where there is one, the creature's own movement where not; the Gaunt
        // and the Followers make no sound of their own till they're on you.
        using var scene = new Scene(1, GameAudio.SignCue(EnemyKind.Ribbit), "cs-grumbler.scuttle", "cs-gaunt.step");
        var at = scene.Ear.Position + new Double3(14, -2, 0);
        void Show(EnemyKind kind) => scene.World.Watcher = new Watcher(3.5, kind, at, 1);
        Show(EnemyKind.Ribbit);
        Assert.Equal(["sign.ribbits"], scene.Tick());
        Assert.Empty(scene.Tick());
        scene.World.Watcher = default;
        Assert.Empty(scene.Tick());
        Show(EnemyKind.Grumbler);
        Assert.Equal(["cs-grumbler.scuttle"], scene.Tick());
        scene.World.Watcher = default;
        scene.Tick();
        Show(EnemyKind.Gaunt);
        Assert.Empty(scene.Tick());
        // The game's own: the four that move out there have theirs installed; the two that don't, none.
        var installed = new SoundBank(Path.Combine(Content, "audio", "sounds"));
        Assert.All(new[] { EnemyKind.Ribbit, EnemyKind.SootChildren, EnemyKind.Whistler, EnemyKind.Grumbler }, k => Assert.NotNull(installed.Get(GameAudio.SignCue(k))));
        Assert.All(new[] { EnemyKind.Gaunt, EnemyKind.Follower }, k => Assert.Null(installed.Get(GameAudio.SignCue(k))));
    }

    // ---- G1's six (queue #125, note 388) ---------------------------------------------------------------------------------

    [Fact]
    public void TheMournersKeenAsAGroupClickWhenStartledAndAreHeardDraggingTheBody()
    {
        // Note 362: one keening a group while it gathers, where those not hauling are; startled, each one's clicks a moment
        // apart; the two hauling hold the one drag between them, the body's.
        using var scene = new Scene(1, "tell-mourners.keening~", "tell-mourners.clicks", "cs-mourners-drag.drag~");
        var at = scene.Train.Frames[1].ToWorld(new Double3(30, 0, 0));
        Mourner M(int id, MournerMode mode, double x = 0) =>
            Record(new Mourner(id), SpinePhase.Telegraph, 1, 1, Enemy.Loose, at + new Double3(x, 0, 0), height: (double)mode, extra: 5, extra2: 20);
        var heard = new List<string>();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(20, MournerMode.Wait), M(21, MournerMode.Wait, 2), M(22, MournerMode.Wait, 4)));
        Assert.Equal(["tell-mourners.keening"], heard);
        // One of them started back from someone come close: its clicks, within the moment.
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            heard.AddRange(scene.Tick(M(20, MournerMode.Wait), M(21, MournerMode.Startle, 2), M(22, MournerMode.Wait, 4)));
        Assert.Equal(["tell-mourners.clicks"], heard);
        // Two haul the body off: one drag between them; the third still keening.
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(20, MournerMode.Drag), M(21, MournerMode.Drag, 1), M(22, MournerMode.Wait, 4)));
        Assert.Equal(["cs-mourners-drag.drag"], heard);
        Assert.Equal(1, scene.Audio.Mixer.Voices.Count(v => v.Name == "cs-mourners-drag.drag" && !v.Stopped));
        Assert.True(scene.Playing("tell-mourners.keening"));
        // All of them off into the dark with it: nothing held.
        for (int i = 0; i < 2; i++)
            scene.Tick(M(20, MournerMode.Leave), M(21, MournerMode.Leave, 1), M(22, MournerMode.Leave, 4));
        Assert.False(scene.Playing("tell-mourners.keening") || scene.Playing("cs-mourners-drag.drag"));
    }

    [Fact]
    public void TowerJawGnawsTheTowerCreaksFromHalfwayGroansItsLastSecondsAndCracksAndFalls()
    {
        // Note 363: the chiselling while it gnaws; the creak only from half gnawed; the groan in its last 5 s; come near, its
        // tail slapped and its lunge; gnawed through, the crack at the leg and the crash where it falls.
        using var scene = new Scene(1, "tell-tower-jaw.gnaw~", "tell-tower-jaw.creak", "tell-tower-jaw.groan~", "tell-tower-jaw.crack",
            "cs-tower-jaw.tail-slap", "cs-tower-jaw.lunge", "cs-tower-jaw.fall");
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).TowerJaw;
        var leg = scene.Train.Frames[1].ToWorld(new Double3(12, 0, 0));
        TowerJaw J(TowerJawMode mode, double gnawed, Double3? at = null) =>
            Record(new TowerJaw(30), SpinePhase.Alert, 1, t.Health, Enemy.Loose, at ?? leg, height: (double)mode, extra: gnawed);
        var heard = new List<string>();
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(J(TowerJawMode.Gnaw, 0.3)));
        Assert.Equal(["tell-tower-jaw.gnaw"], heard);
        heard.Clear();
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(J(TowerJawMode.Gnaw, t.LeanFrom + 0.1)));
        Assert.Contains("tell-tower-jaw.creak", heard);
        Assert.False(scene.Playing("tell-tower-jaw.groan"));
        // Its last seconds (on the frontier, through in its gnawSeconds): the groan.
        scene.Tick(J(TowerJawMode.Gnaw, 1 - (t.GroanSeconds - 1) / t.GnawFor(DarkTerritory.Sim.Route.RouteTier.Frontier)));
        Assert.True(scene.Playing("tell-tower-jaw.groan"));
        // Come near: its tail slapped at once and again, then the lunge.
        heard.Clear();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(J(TowerJawMode.Threat, 0.9)));
        Assert.Equal(2, heard.Count(h => h == "cs-tower-jaw.tail-slap"));
        Assert.Contains("cs-tower-jaw.lunge", scene.Tick(J(TowerJawMode.Lunge, 0.9)));
        scene.Tick(J(TowerJawMode.Gnaw, 0.99));
        // Through: the crack now (at the leg), the crash a moment after (where it falls, the record's from that tick).
        var falls = leg + new Double3(0, 0, -8);
        heard.Clear();
        heard.AddRange(scene.Tick(J(TowerJawMode.Wreck, 0.99, falls)));
        Assert.Equal(["tell-tower-jaw.crack"], heard);
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(J(TowerJawMode.Wreck, 0.99, falls)));
        Assert.Equal(["tell-tower-jaw.crack", "cs-tower-jaw.fall"], heard);
        Assert.False(scene.Playing("tell-tower-jaw.gnaw"));
    }

    [Fact]
    public void TheBrakemanLimpsAlongTheRoofsWindsTheBrakeAndCorneredLashesHisChain()
    {
        // Note 364 (and the routine's ask): his steps limp only if the good leg's takes and the bad leg's alternate on a
        // long-short beat, the bad one fired early by its toe's drag so its plant lands on the beat.
        using var scene = new Scene(1, "tell-brakeman.step.good", "tell-brakeman.step.bad", "tell-brakeman.chain~", "tell-brakeman.ratchet",
            "cs-brakeman.chain-up", "cs-brakeman.wheeze~", "cs-brakeman.lash", "tell-brakeman.squeal~");
        var roof = new Double3(0, scene.Train.Frames[2].Shape.RoofHeight, 0);
        Brakeman B(BrakemanMode mode, double seconds = 1, SpinePhase phase = SpinePhase.Dormant) =>
            Record(new Brakeman(40), phase, seconds, 4, 2, roof, height: (double)mode, extra: 20, extra2: 1);
        var steps = new List<(int Tick, string Leg)>();
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            foreach (var h in scene.Tick(B(BrakemanMode.Walk)))
                if (h.StartsWith("tell-brakeman.step.", StringComparison.Ordinal))
                    steps.Add((i, h["tell-brakeman.step.".Length..]));
        Assert.True(scene.Playing("tell-brakeman.chain"));
        Assert.True(steps.Count >= 6, string.Join(" ", steps));
        for (int i = 0; i < steps.Count; i++)
            Assert.Equal(i % 2 == 0 ? "good" : "bad", steps[i].Leg);
        // A long stance on the good leg, a quick one on the bad: fired good to bad, the long beat less the drag's lead.
        double Seconds(int a, int b) => (steps[b].Tick - steps[a].Tick) / (double)SimConstants.TickRate;
        for (int i = 0; i + 2 < steps.Count; i += 2)
        {
            Assert.InRange(Seconds(i, i + 1), 0.74 - 0.2 - 0.05, 0.74 - 0.2 + 0.05);
            Assert.True(Math.Abs(Seconds(i, i + 2) - 1.2) < 0.05, string.Join(" ", steps));
        }
        // At a brake wheel: its pawl ratcheting, run after run.
        var heard = new List<string>();
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(B(BrakemanMode.Wind)));
        Assert.InRange(heard.Count(h => h == "tell-brakeman.ratchet"), 3, 5);
        Assert.False(scene.Playing("tell-brakeman.chain"));
        // Cornered: the chain gathered up, his wheeze, and a lash each time it lands (its phase's clock starts again).
        heard.Clear();
        heard.AddRange(scene.Tick(B(BrakemanMode.Cornered, 0, SpinePhase.Telegraph)));
        Assert.Contains("cs-brakeman.chain-up", heard);
        Assert.True(scene.Playing("cs-brakeman.wheeze"));
        heard.Clear();
        heard.AddRange(scene.Tick(B(BrakemanMode.Cornered, 2.5, SpinePhase.Telegraph)));
        heard.AddRange(scene.Tick(B(BrakemanMode.Cornered, 0, SpinePhase.Telegraph)));
        Assert.Equal(["cs-brakeman.lash"], heard);
    }

    [Fact]
    public void AWoundBrakeSquealsAndASeizedAxleGrindsOnlyWithTheWheelsTurning()
    {
        // The cars' own, after whoever did it (the Brakeman's wound handbrake, Hotbox's seized axle): with the wheels
        // turning, steady; at a stand, nothing.
        using var scene = new Scene(1, "tell-brakeman.squeal~", "tell-hotbox.grind~");
        scene.Train.Vehicles[2].Wound = true;
        scene.Train.Vehicles[3].Seized = true;
        scene.Tick();
        Assert.False(scene.Playing("tell-brakeman.squeal") || scene.Playing("tell-hotbox.grind"));
        scene.Train.Dynamics.Velocity = 8;
        scene.Tick();
        Assert.True(scene.Playing("tell-brakeman.squeal") && scene.Playing("tell-hotbox.grind"));
        scene.Train.Vehicles[2].Wound = false;
        scene.Train.Dynamics.Velocity = 8;
        scene.Tick();
        Assert.False(scene.Playing("tell-brakeman.squeal"));
        Assert.True(scene.Playing("tell-hotbox.grind"));
    }

    [Fact]
    public void TheKnotterCreaksCreepingInStretchesTheCarsApartAndItsHawserCreaksTakingUp()
    {
        // Note 365: the creep held for its 4 s, the stretch once as it forces the cars apart, and taut its hawser creaking
        // as it takes up from slack, and as it coils round whoever slipped.
        using var scene = new Scene(1, "tell-knotter.creep~", "tell-knotter.stretch", "tell-knotter.hawser");
        var gap = new Double3(0, 1, scene.Train.Cars[2].Length / 2 + 1);
        Knotter K(KnotterMode mode) => Record(new Knotter(50), SpinePhase.Alert, 1, 8, 2, gap, height: (double)mode);
        var heard = new List<string>();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(K(KnotterMode.Creep)));
        Assert.Equal(["tell-knotter.creep"], heard);
        heard.Clear();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(K(KnotterMode.Force)));
        Assert.Equal(["tell-knotter.stretch"], heard);
        Assert.False(scene.Playing("tell-knotter.creep"));
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(K(KnotterMode.Taut)));
        heard.AddRange(scene.Tick(K(KnotterMode.Slack)));
        Assert.Empty(heard);
        heard.AddRange(scene.Tick(K(KnotterMode.Taut)));
        heard.AddRange(scene.Tick(K(KnotterMode.Coil)));
        Assert.Equal(["tell-knotter.hawser", "tell-knotter.hawser"], heard);
    }

    [Fact]
    public void TheFreightBeetleIsHeardShovingItsCrateOnWhatsUnderItAndStartled()
    {
        // Note 366: the crate's push held while it pushes, its surface's take; startled at its head, its plates and hiss.
        using var scene = new Scene(1, "cs-freight-beetle.push.ground~", "cs-freight-beetle.startle");
        var at = scene.Train.Frames[1].ToWorld(new Double3(15, 0, 0));
        FreightBeetle F(BeetleMode mode) => Record(new FreightBeetle(60), SpinePhase.Dormant, 1, 6, Enemy.Loose, at, height: (double)mode, extra: 3);
        var heard = new List<string>();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(F(BeetleMode.Walk)));
        Assert.Empty(heard);
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(F(BeetleMode.Push)));
        Assert.Equal(["cs-freight-beetle.push.ground"], heard);
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(F(BeetleMode.Startle)));
        Assert.Equal(["cs-freight-beetle.startle"], heard);
        Assert.False(scene.Playing("cs-freight-beetle.push.ground"));
    }

    [Fact]
    public void HotboxKnocksOnceAWheelTurnFasterTheFasterYouRunAndSizzlesGlowing()
    {
        // Note 367: a knock a turn of its wheel (Hotbox.KnocksPerSecond), none at a stand; glowing, the sizzle under it.
        using var scene = new Scene(1, "tell-hotbox.knock", "tell-hotbox.sizzle~");
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Hotbox;
        var truck = new Double3(0.7, 0.5, 4);
        Hotbox H(HotboxMode mode) => Record(new Hotbox(70), SpinePhase.Dormant, 1, 6, 2, truck, height: (double)mode, extra: 10, lateral: 1);
        int Knocks(double speed, HotboxMode mode = HotboxMode.Knock)
        {
            int n = 0;
            for (int i = 0; i < 4 * SimConstants.TickRate; i++)
            {
                scene.Train.Dynamics.Velocity = speed;
                n += scene.Tick(H(mode)).Count(h => h == "tell-hotbox.knock");
            }
            return n;
        }
        Assert.Equal(0, Knocks(0));
        int slow = Knocks(5), fast = Knocks(15);
        Assert.InRange(slow, (int)(4 * Hotbox.KnocksPerSecond(t, 5)) - 1, (int)(4 * Hotbox.KnocksPerSecond(t, 5)) + 1);
        Assert.InRange(fast, (int)(4 * Hotbox.KnocksPerSecond(t, 15)) - 1, (int)(4 * Hotbox.KnocksPerSecond(t, 15)) + 1);
        Assert.False(scene.Playing("tell-hotbox.sizzle"));
        Assert.True(Knocks(10, HotboxMode.Glow) > 0);
        Assert.True(scene.Playing("tell-hotbox.sizzle"));
    }

    // ---- The house creatures (queue #497, note 767) ------------------------------------------------------------------------

    [Fact]
    public void TheLodgerBreathesInItsCornerShrieksBeforeEachLungeAndSplintersADoor()
    {
        // Note 583: the breathing while it hides; its shriek each time it shrieks (before every lunge); the lunge's thump; a
        // door broken in under blows about every half second of its breakSeconds.
        using var scene = new Scene(1, "tell-lodger-breathing.breathing~", "tell-lodger-shriek.shriek", "cs-lodger.lunge", "cs-lodger.break");
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Lodger;
        var at = scene.Train.Frames[1].ToWorld(new Double3(20, 0, 0));
        Lodger L(LodgerMode mode) => Record(new Lodger(80), SpinePhase.Dormant, 1, t.Health, Enemy.Loose, at, height: (double)mode, extra: 1, extra2: 0);
        var heard = new List<string>();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(L(LodgerMode.Hidden)));
        Assert.Equal(["tell-lodger-breathing.breathing"], heard);
        heard.Clear();
        foreach (var mode in new[] { LodgerMode.Shriek, LodgerMode.Lunge, LodgerMode.Chase, LodgerMode.Shriek, LodgerMode.Lunge })
            for (int i = 0; i < SimConstants.TickRate / 2; i++)
                heard.AddRange(scene.Tick(L(mode)));
        Assert.Equal(["tell-lodger-shriek.shriek", "cs-lodger.lunge", "tell-lodger-shriek.shriek", "cs-lodger.lunge"], heard);
        Assert.False(scene.Playing("tell-lodger-breathing.breathing"));
        heard.Clear();
        for (int i = 0; i < (int)(t.BreakSeconds * SimConstants.TickRate); i++)
            heard.AddRange(scene.Tick(L(LodgerMode.Break)));
        Assert.InRange(heard.Count(h => h == "cs-lodger.break"), 3, 5);
    }

    [Fact]
    public void TheHouseholdersHouseIsHeardWhileItSitsItRisesHuntsAtAnEvenTreadAndSettlesHome()
    {
        // Note 584: the house's homely loop while it sits or watches; a creak now and then as it watches; the chair scraped
        // back as it rises; hunting, its tread a step a stride and never hurried, its breath; the grab; home, it settles.
        using var scene = new Scene(1, "tell-householder.house~", "cs-householder.creak", "tell-householder-rise.rise", "cs-householder.step",
            "cs-householder.breath~", "cs-householder.grab", "cs-householder.settle");
        var chair = scene.Train.Frames[1].ToWorld(new Double3(25, 0, 0));
        Householder H(HouseholderMode mode, Double3 at) =>
            Record(new Householder(81), SpinePhase.Dormant, 1, 6, Enemy.Loose, at, height: (double)mode, extra: 1, extra2: 0);
        var heard = new List<string>();
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(H(HouseholderMode.Watch, chair)));
        Assert.True(scene.Playing("tell-householder.house"));
        Assert.InRange(heard.Count(h => h == "cs-householder.creak"), 1, 3);
        heard.Clear();
        heard.AddRange(scene.Tick(H(HouseholderMode.Rise, chair)));
        Assert.Equal(["tell-householder-rise.rise"], heard);
        // Hunting at 3.4 m/s for 4 s: about one step a stride of it, the breath held, the house's loop gone.
        heard.Clear();
        var steps = new List<int>();
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            var at = chair + new Double3(3.4 * i / SimConstants.TickRate, 0, 0);
            foreach (var h in scene.Tick(H(HouseholderMode.Hunt, at)))
                if (h == "cs-householder.step")
                    steps.Add(i);
        }
        Assert.InRange(steps.Count, 8, 11);
        Assert.All(steps.Zip(steps.Skip(1)), p => Assert.True(p.Second - p.First >= (int)(0.45 * SimConstants.TickRate)));
        Assert.True(scene.Playing("cs-householder.breath"));
        Assert.False(scene.Playing("tell-householder.house"));
        Assert.Contains("cs-householder.grab", scene.Tick(H(HouseholderMode.Grab, chair)));
        scene.Tick(H(HouseholderMode.Return, chair));
        Assert.Contains("cs-householder.settle", scene.Tick(H(HouseholderMode.Sit, chair)));
    }

    [Fact]
    public void TheHollowHouseRumblesSinksLouderAsItGoesAndComesDownOnce()
    {
        // Note 585: the rumble held while it rumbles, the sink while it sinks (fuller the deeper), the collapse once.
        using var scene = new Scene(1, "tell-hollow-house.rumble~", "cs-hollow-house.sink~", "cs-hollow-house.collapse");
        var middle = scene.Train.Frames[1].ToWorld(new Double3(18, 0, 0));
        HollowHouse H(HollowMode mode, double sunk = 0) =>
            Record(new HollowHouse(82), SpinePhase.Dormant, 1, 1, Enemy.Loose, middle, height: (double)mode, extra: sunk, extra2: 0);
        var heard = new List<string>();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(H(HollowMode.Still)));
        Assert.Empty(heard);
        heard.AddRange(scene.Tick(H(HollowMode.Rumble)));
        Assert.Equal(["tell-hollow-house.rumble"], heard);
        scene.Tick(H(HollowMode.Sink, 0.1));
        var sink = scene.Audio.Mixer.Voices.First(v => v.Name == "cs-hollow-house.sink" && !v.Stopped);
        float shallow = sink.Volume;
        scene.Tick(H(HollowMode.Sink, 2.0));
        Assert.True(sink.Volume > shallow);
        Assert.False(scene.Playing("tell-hollow-house.rumble"));
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(H(HollowMode.Collapse, 2.4)));
        Assert.Equal(["cs-hollow-house.collapse"], heard);
    }

    [Fact]
    public void TheHangerSnapsOntoWhoeverItHasHaulsThemUpAndHissesBackIntoTheRoof()
    {
        // Note 586: the grab's snap once, the haul held while it hauls, the retreat's hiss once as it lets go.
        using var scene = new Scene(1, "cs-hanger.grab", "cs-hanger.haul~", "cs-hanger.retreat");
        var roof = scene.Train.Frames[1].ToWorld(new Double3(16, 4, 0));
        Hanger H(HangerMode mode) => Record(new Hanger(83), SpinePhase.Dormant, 1, 3, Enemy.Loose, roof, height: (double)mode, extra: 1, extra2: 0);
        var heard = new List<string>();
        foreach (var mode in new[] { HangerMode.Hidden, HangerMode.Grab, HangerMode.Haul, HangerMode.Haul, HangerMode.Hold, HangerMode.Retreat, HangerMode.Retreat })
            heard.AddRange(scene.Tick(H(mode)));
        Assert.Equal(["cs-hanger.grab", "cs-hanger.haul", "cs-hanger.retreat"], heard);
        Assert.False(scene.Playing("cs-hanger.haul"));
    }

    // ---- The Pickers (queue #501, note 771) -------------------------------------------------------------------------------

    [Fact]
    public void ThePickersComeUpScuttleCarryBiteAndGoAndTheGroupIsHeardOnceAtATime()
    {
        // Note 592: up out of its drain on its first tick, the grating and a chitter; its feet while it scuttles and a
        // chitter now and then; the crate and its pant while it carries; the bite; then a scatter's squeals and the drains'
        // rattle once for the two of them; a squeal cut short when a blow kills one.
        using var scene = new Scene(2, "tell-pickers.emerge", "tell-pickers.feet~", "tell-pickers.chitter", "cs-pickers.carry~",
            "cs-pickers.bite", "cs-pickers.scatter", "cs-pickers.down", "cs-pickers.hit", "creature-crumble");
        var drain = scene.Train.Frames[2].ToWorld(new Double3(-12, 0, 0));
        Picker P(int id, PickerMode mode, Double3 at) =>
            Record(new Picker(id), SpinePhase.Telegraph, 0, 1, Enemy.Loose, at, height: (double)mode, extra: -1);
        Assert.Equal(["tell-pickers.emerge"], scene.Tick(P(90, PickerMode.Emerge, drain)));
        var heard = new List<string>();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(P(90, PickerMode.Emerge, drain)));
        Assert.Empty(heard);
        // Ten seconds scuttling across the yard at 2 m/s.
        var at = drain;
        var step = new Double3(2.0 / SimConstants.TickRate, 0, 0);
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(P(90, PickerMode.Scuttle, at += step)));
        Assert.True(scene.Playing("tell-pickers.feet"));
        Assert.InRange(heard.Count(h => h == "tell-pickers.chitter"), 1, 5);
        Assert.DoesNotContain("tell-pickers.emerge", heard);
        // Carrying a crate home: the crate and the pant, not its feet.
        for (int i = 0; i < SimConstants.TickRate; i++)
            scene.Tick(P(90, PickerMode.Carry, at -= step));
        Assert.True(scene.Playing("cs-pickers.carry"));
        Assert.False(scene.Playing("tell-pickers.feet"));
        // A crewmate comes right up: it bites, drops the crate, and it and another near it bolt and go down together.
        var other = at + new Double3(1, 0, 0);
        Assert.Equal(["cs-pickers.bite"], scene.Tick(P(90, PickerMode.Bite, at), P(91, PickerMode.Scuttle, other)));
        Assert.Equal(["cs-pickers.scatter"], scene.Tick(P(90, PickerMode.Scatter, at), P(91, PickerMode.Scatter, other)));
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            scene.Tick(P(90, PickerMode.Scatter, at), P(91, PickerMode.Scatter, other));
        Assert.False(scene.Playing("cs-pickers.carry"));
        Assert.Equal(["cs-pickers.down"], scene.Tick(P(90, PickerMode.Down, drain), P(91, PickerMode.Down, drain)));
        // Another is clubbed: the blow kills it, and its record goes with it.
        for (int i = 0; i < SimConstants.TickRate; i++)
            scene.Tick(P(92, PickerMode.Scuttle, at));
        scene.World.Hits.Add(new HitConfirm(1, scene.World.Tick, 92, EnemyKind.Pickers, 1, HitSource.Melee, at, Double3.Up, true));
        Assert.Contains("cs-pickers.hit", scene.Tick());
    }

    // ---- The Mouse Moose (queue #537, note 809) ---------------------------------------------------------------------------

    [Fact]
    public void TheMouseMooseCreepsInSqueakingBitesDragsAndBoltsForItsHole()
    {
        // Note 790's pest, heard off its record: its claws while it creeps in and a squeak now and then (the tell); its teeth
        // in the casting; the drag, louder once it's out of the hook's reach and going faster; scared off, a squeal and a
        // scrabble, and the earth kicked back as it goes down its hole. Back out for the casting, a squeak soon after.
        using var scene = new Scene(2, "tell-mouse-moose.creep~", "tell-mouse-moose.squeak", "cs-mouse-moose.bite", "cs-mouse-moose.drag~",
            "cs-mouse-moose.bolt", "cs-mouse-moose.dig", "cs-mouse-moose.lost");
        var hole = scene.Train.Frames[2].ToWorld(new Double3(-20, 0, 0));
        MouseMoose M(MouseMooseMode mode, Double3 at) =>
            Record(new MouseMoose(97), SpinePhase.Telegraph, 0, 1, Enemy.Loose, at, height: (double)mode, extra: 0);
        var heard = new List<string>();
        var at = hole;
        // Twelve seconds creeping in at 1.2 m/s.
        var step = new Double3(1.2 / SimConstants.TickRate, 0, 0);
        for (int i = 0; i < 12 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MouseMooseMode.Creep, at += step)));
        Assert.True(scene.Playing("tell-mouse-moose.creep"));
        Assert.InRange(heard.Count(h => h == "tell-mouse-moose.squeak"), 1, 5);
        // Its teeth in it: the bite once, and its claws stop.
        heard.Clear();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MouseMooseMode.Bite, at)));
        Assert.Equal(["cs-mouse-moose.bite"], heard.Where(h => h != "tell-mouse-moose.squeak"));
        Assert.False(scene.Playing("tell-mouse-moose.creep"));
        // Dragging it off at 0.15 m/s, then at 0.5 once the hook can't reach it: louder.
        float Drag(double speed)
        {
            var back = new Double3(-speed / SimConstants.TickRate, 0, 0);
            for (int i = 0; i < SimConstants.TickRate; i++)
                scene.Tick(M(MouseMooseMode.Drag, at += back));
            return scene.Audio.Mixer.Voices.Single(v => v.Name == "cs-mouse-moose.drag" && !v.Stopped).Volume;
        }
        float slow = Drag(0.15), away = Drag(0.5);
        Assert.True(away > slow * 1.2, $"{away:0.00} out of reach, {slow:0.00} in it");
        // A crewmate comes up: it lets go and bolts, the drag stops; down its hole, the dig. No casting's lost.
        heard.Clear();
        heard.AddRange(scene.Tick(M(MouseMooseMode.Flee, at)));
        Assert.Equal(["cs-mouse-moose.bolt"], heard);
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            heard.AddRange(scene.Tick(M(MouseMooseMode.Flee, at = (at + hole) * 0.5)));
        Assert.False(scene.Playing("cs-mouse-moose.drag"));
        Assert.Equal(["cs-mouse-moose.dig"], scene.Tick(M(MouseMooseMode.Hide, hole)));
        heard.Clear();
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MouseMooseMode.Hide, hole)));
        Assert.Empty(heard);
        // Back out for it: a squeak within the first second, its claws as it comes.
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(M(MouseMooseMode.Creep, at = hole + step * (i + 1))));
        Assert.Equal(["tell-mouse-moose.creep", "tell-mouse-moose.squeak"], heard.Order());
        Assert.True(scene.Playing("tell-mouse-moose.creep"));
        Assert.DoesNotContain("cs-mouse-moose.lost", heard);
    }

    // ---- The Wakers (queue #502, note 772) --------------------------------------------------------------------------------

    [Fact]
    public void AWakerRisenBesideOneWhoRanStridesOverTakesThemAndSwallows()
    {
        // Note 710: up out of the land beside them (its first tick), its walk's footfalls as it strides over, its grip on
        // them, the swallow at its mouth.
        using var scene = new Scene(2, "tell-wakers.rise", "tell-wakers.stride", "cs-wakers.take", "cs-wakers.swallow");
        var feet = scene.Train.Frames[2].ToWorld(new Double3(-60, 0, 0));
        Waker K(SpinePhase phase, Double3 at) =>
            Record(new Waker(95), phase, 0, 1, Enemy.Loose, at, height: 0, extra: 1, extra2: Waker.PickerMark, holding: phase is SpinePhase.Grab ? 1 : -1);
        Assert.Equal(["tell-wakers.rise"], scene.Tick(K(SpinePhase.Telegraph, feet)));
        var heard = new List<string>();
        var step = new Double3(12.0 / SimConstants.TickRate, 0, 0);
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(K(SpinePhase.Commit, feet += step)));
        Assert.Equal(2, heard.Count(h => h == "tell-wakers.stride"));
        Assert.Equal(["cs-wakers.take"], scene.Tick(K(SpinePhase.Grab, feet)));
        for (int i = 0; i < SimConstants.TickRate; i++)
            scene.Tick(K(SpinePhase.Grab, feet));
        Assert.Equal(["cs-wakers.swallow"], scene.Tick(K(SpinePhase.Punish, feet)));
    }

    // ---- The figures (queue #503, note 773) -------------------------------------------------------------------------------

    [Fact]
    public void DavePaintsLaughsOffEachBlowTurnsAtTheLastWarningAndTakesTheNext()
    {
        // Note 570: painting at his easel; a breath of a laugh at each blow before his last warning; at the last warning the
        // brush set down as he turns (no laugh: he isn't laughing now); the snap of his grab on the blow after it.
        using var scene = new Scene(2, "figures.dave-paint~", "figures.dave-laugh", "figures.dave-turn", "figures.dave-grab");
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Dave;
        var easel = scene.Train.Frames[2].ToWorld(new Double3(-20, 0, 0));
        Dave D(SpinePhase phase, int blows) =>
            Record(new Dave(97), phase, 0, 1, Enemy.Loose, easel, extra: blows, extra2: blows > 0 ? 1 : -1, holding: phase == SpinePhase.Grab ? 1 : -1);
        scene.Tick(D(SpinePhase.Dormant, 0));
        var heard = new List<string>();
        for (int i = 0; i < SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(D(SpinePhase.Dormant, 0)));
        Assert.Equal(["figures.dave-paint"], heard);
        heard.Clear();
        for (int n = 1; n < t.Blows - 1; n++)
            for (int i = 0; i < SimConstants.TickRate / 2; i++)
                heard.AddRange(scene.Tick(D(SpinePhase.Dormant, n)));
        Assert.Equal(Enumerable.Repeat("figures.dave-laugh", t.Blows - 2), heard);
        Assert.Equal(["figures.dave-turn"], scene.Tick(D(SpinePhase.Telegraph, t.Blows - 1)));
        Assert.False(scene.Playing("figures.dave-paint"));
        Assert.Equal(["figures.dave-grab"], scene.Tick(D(SpinePhase.Grab, t.Blows)));
    }

    [Fact]
    public void JacobFishesAndHisBlessingRunsDownEveryCarOnce()
    {
        // Note 489: his line at the water's edge, held; World.Bless mends the train, heard down it from the engine, once.
        using var scene = new Scene(2, "figures.jacob-fish~", "figures.jacob-bless");
        var shore = scene.Train.Frames[0].ToWorld(new Double3(-25, 0, 0));
        Jacob J(double blessed) => Record(new Jacob(98), SpinePhase.Dormant, 0, 1, Enemy.Loose, shore, extra: blessed);
        scene.Tick(J(0));
        Assert.Equal(["figures.jacob-fish"], scene.Tick(J(0)));
        var heard = new List<string>();
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            heard.AddRange(scene.Tick(J(1)));
        Assert.Equal(scene.Train.Dynamics.Consist.Vehicles.Count, heard.Count(h => h == "figures.jacob-bless"));
        Assert.True(scene.Playing("figures.jacob-fish"));
    }

    [Fact]
    public void ASootChildStopsCallingDeadAsItRisesToSomeoneComingNearAndARealChildGoesOnCalling()
    {
        // Note 829 (note 823's telegraph): from 2.5 m outside its lunge, as someone comes on, a Soot Child's call stops dead
        // as its head snaps round to them, and it calls again when they back off; a real child goes on calling.
        double lunge = new SootChildrenTuning().LungeWithin; // the scene's world has no tuning: GameAudio's, the content's default
        foreach (bool soot in new[] { true, false })
        {
            using var scene = new Scene(0, "child-call");
            // A call long enough to be cut off in.
            scene.Audio.Bank.Add("child-call", new SoundDef(3, [new LayerDef(SourceKind.Noise, 0.1)], Duration: 1.5, MaxInstances: 4));
            var frame = scene.Train.Frames[0];
            var at = frame.ToWorld(new Double3(14, 0, 0));
            var child = Record(new SootChildren(41), SpinePhase.Telegraph, 1, 4, Enemy.Loose, at, extra: 1, extra2: soot ? 1 : 0);
            void Stand(double away) =>
                scene.Audio.CrewStates = [(1, new PlayerState { Parent = PlayerState.World, Position = at + new Double3(away, 0, 0), Health = Player.Health })];
            // A tick's worth of sound played, so a call ends when it should.
            int Step()
            {
                int n = scene.Tick(child).Count(h => h == "child-call");
                for (int b = 0; b < Audio.SampleRate / SimConstants.TickRate / Audio.Block; b++)
                    scene.Audio.Mixer.Render(new float[Audio.Block * 2]);
                return n;
            }
            int Calls(double seconds)
            {
                int n = 0;
                for (int i = 0; i < seconds * SimConstants.TickRate; i++)
                    n += Step();
                return n;
            }

            // Out past its telegraph: calling, call after call.
            Stand(lunge + Art.CreatureArt.SootRiseFrom + 1);
            Assert.True(Calls(6) >= 2, "it should call for help, call after call");
            while (!scene.Playing("child-call"))
                Step();

            // Coming on into it, mid-call.
            Stand(lunge + 1);
            Step();
            Step();
            if (soot)
            {
                Assert.False(scene.Playing("child-call"), "a Soot Child rising for its lunge should stop calling dead");
                Assert.Equal(0, Calls(6));
                // Backed off: it settles and calls again.
                Stand(lunge + Art.CreatureArt.SootRiseFrom + 1);
                Assert.True(Calls(6) >= 1, "backed off, it should call again");
            }
            else
            {
                Assert.True(scene.Playing("child-call"), "a real child should go on calling");
                Assert.True(Calls(6) >= 2, "a real child should go on calling");
            }
        }
    }
}
