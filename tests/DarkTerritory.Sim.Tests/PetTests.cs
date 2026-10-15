using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The whimsies' third round (ARCHITECTURE §8 note 745; docs/design/upgrades.md §4, §7.4): the yard cat (wanders, sleeps on
/// the warm backhead, hisses at what's aboard near it), the dog (barks at what's aboard or coming, fetches a toy to whoever's
/// near) and the tennis-ball launcher (a ball off the back, and the Cinder Hounds chasing the train go for it instead).
/// </summary>
public class PetTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(Tuning.Train, Tuning.Boiler, Tuning.Combat, Tuning.Enemies);
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    static TrainTuning Dressed(params string[] ids) => Campaign.Campaign.Apply(C, ids, Base).Train;

    static World World(TrainTuning t)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 2_000, Tuning.Boiler), Tuning.Combat);
        world.EnableBodies();
        world.EnableEnemies(E, null, 1, crew: 2, authority: true);
        world.Stock();
        return world;
    }

    static void Settle(World w, double seconds, params (int Id, PlayerState State)[] crew)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.Tick++;
            w.StepBodies(crew);
        }
    }

    static Body Pet(World w, BodyKind kind) => Assert.Single(w.Bodies.All, b => b.Kind == kind);

    static PetMood Mood(Body b) => (PetMood)b.Tipped;

    [Fact]
    public void EachIsOnTheTrainOnceBought()
    {
        foreach (var id in new[] { "yardCat", "dog", "tennisBalls" })
        {
            var u = Assert.Single(C.Upgrades, u => u.Id == id);
            Assert.Equal(UpgradeKind.Whimsy, u.Kind);
            Assert.NotNull(u.Line);
            Assert.NotNull(u.Tally);
            Assert.Contains(id, Dressed(id).Composition.Dress);
        }
        var plain = World(Tuning.Train);
        Assert.DoesNotContain(plain.Bodies.All, b => b.Kind is BodyKind.Cat or BodyKind.Dog);
        Assert.DoesNotContain(plain.Train.Frames[^1].Shape.Interactables, i => i.Kind == InteractableKind.Launcher);
        var w = World(Dressed("yardCat", "dog", "tennisBalls"));
        Assert.Equal(0, Pet(w, BodyKind.Cat).Parent);
        Assert.Equal(w.Train.Frames.Count - 1, Pet(w, BodyKind.Dog).Parent);
        Assert.Single(w.Train.Frames[^1].Shape.Interactables, i => i.Kind == InteractableKind.Launcher);
    }

    [Fact]
    public void TheCatSleepsOnTheWarmBackheadAndWandersWhenItsCold()
    {
        var w = World(Dressed("yardCat"));
        var cat = Pet(w, BodyKind.Cat);
        w.Train.Boiler.Firebox = 5;
        Settle(w, 10);
        Assert.Equal(PetMood.Sleep, Mood(cat));
        Assert.Equal(0, cat.Parent);
        // The fire out, it gets up and wanders the cab.
        w.Train.Boiler.Firebox = 0;
        var asleep = cat.Centre;
        Settle(w, 15);
        Assert.NotEqual(PetMood.Sleep, Mood(cat));
        Assert.True((cat.Centre - asleep).Length > 0.2, "it's moved");
        Assert.True(w.Train.Frames[0].Shape.Cab!.Value.Contains(cat.Centre), "and stayed in the cab");
    }

    [Fact]
    public void TheCatHissesAtWhatsAboardNearItAndNotFurther()
    {
        foreach (int car in new[] { 1, 3 })
        {
            var w = World(Dressed("yardCat"));
            var cat = Pet(w, BodyKind.Cat);
            var hound = w.AddEnemy(id => new CinderHound(id, 1) { Health = E.CinderHounds.Health });
            hound.Restore(SpinePhase.Commit, 1, E.CinderHounds.Health, car, new Double3(0, w.Train.Frames[car].Shape.RoofHeight, 0), 0, 0, 0, 1, 0);
            Settle(w, 0.5);
            if (car == 1)
                Assert.Equal(PetMood.Warn, Mood(cat));
            else
                Assert.NotEqual(PetMood.Warn, Mood(cat));
        }
    }

    [Fact]
    public void TheDogBarksAtWhatsComingAndFetchesAToyToWhoeversNear()
    {
        var w = World(Dressed("dog"));
        var dog = Pet(w, BodyKind.Dog);
        int van = dog.Parent;
        // A hound running the train down behind, in the open and closing: it barks.
        var hound = w.AddEnemy(id => new CinderHound(id, 1) { Health = E.CinderHounds.Health });
        hound.Restore(SpinePhase.Telegraph, 1, E.CinderHounds.Health, -1, default, w.Train.Dynamics.RearDistance - 20, 3, 0, 1, 0);
        Settle(w, 0.3);
        Assert.Equal(PetMood.Warn, Mood(dog));
        // Far back, it doesn't.
        hound.Restore(SpinePhase.Telegraph, 1, E.CinderHounds.Health, -1, default, w.Train.Dynamics.RearDistance - 400, 3, 0, 1, 0);
        Settle(w, 0.3);
        Assert.NotEqual(PetMood.Warn, Mood(dog));
        hound.Restore(SpinePhase.Gone, 0, 0, -1, default, 0, 0, 0, 1, 0);
        // A toy on the van's floor and a crewmate at the far end: it's fetched to them.
        var room = w.Train.Frames[van].Shape.Interior!.Value;
        var toy = w.Bodies.SpawnCrate(w.Train, van, new Double3(0.5, room.Min.Y + 0.1, room.Min.Z + 2), BodyKind.Toy);
        var me = PlayerMotor.SpawnInCab(w.Train, P) with { Parent = van, Position = new Double3(-0.3, room.Min.Y + 0.1, room.Max.Z - 1.0) };
        foreach (var other in w.Bodies.All.Where(b => b.Kind == BodyKind.Toy && b != toy).ToList())
            w.Bodies.Remove(other);
        bool fetching = false;
        for (int i = 0; i < 25 * SimConstants.TickRate; i++)
        {
            w.Tick++;
            w.StepBodies([(1, me)]);
            fetching |= Mood(dog) == PetMood.Fetch;
        }
        Assert.True(fetching);
        Assert.Equal(van, toy.Parent);
        Assert.True(((toy.Centre - me.Position) with { Y = 0 }).Length <= w.Train.Dynamics.Tuning.Composition.Whimsy.Drop + 0.6,
            $"the toy at {toy.Centre}, me at {me.Position}");
    }

    [Fact]
    public void TheLauncherFiresABallOffTheBackNoFasterThanItsSpring()
    {
        var w = World(Dressed("tennisBalls"));
        int van = w.Train.Frames.Count - 1;
        var spot = Assert.Single(w.Train.Frames[van].Shape.Interactables, i => i.Kind == InteractableKind.Launcher);
        var s = PlayerMotor.SpawnInCab(w.Train, P) with { Parent = van, Position = spot.Position, Yaw = Math.PI, HeldSlot = 0 };
        void Tick(PlayerIntent intent)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.StepBodies([(1, s)]);
            w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        Tick(use);
        var ball = Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.TennisBall);
        // Off the back, up the line behind.
        var van0 = w.Train.Frames[van];
        Tick(default);
        Tick(default);
        Assert.True(van0.ToLocal(Bodies.WorldCentre(ball, w.Train)).Z > van0.Shape.HalfLength, "it's gone off the back");
        // Again at once: nothing; after its spring's reset, another.
        Tick(use);
        Assert.Single(w.Bodies.All, b => b.Kind == BodyKind.TennisBall);
        for (int i = 0; i < w.Train.Dynamics.Tuning.Composition.Whimsy.LaunchEvery * SimConstants.TickRate; i++)
            Tick(default);
        Tick(use);
        Assert.Equal(2, w.Bodies.All.Count(b => b.Kind == BodyKind.TennisBall));
    }

    [Fact]
    public void AHoundGoesForTheBallAndFallsBehind()
    {
        double Gap(bool ball)
        {
            var n = new Night(4, speed: 12, trainTuning: Dressed("tennisBalls"));
            var hound = n.World.AddEnemy(id => new CinderHound(id, 1)
            {
                LineDistance = n.Train.Dynamics.RearDistance - 60,
                Lateral = 3,
                Height = 0.6,
                Health = E.CinderHounds.Health,
            });
            Body? thrown = null;
            if (ball)
            {
                var sample = n.Train.Line.Sample(n.Train.Dynamics.RearDistance - 30);
                thrown = n.World.Bodies.SpawnItem(sample.Position, n.Train.Dynamics.RearDistance - 30, BodyKind.TennisBall);
            }
            n.Run(6);
            if (ball)
                Assert.Equal(hound.Id, thrown!.TakenBy);
            return n.Train.Dynamics.RearDistance - hound.LineDistance;
        }
        Assert.True(Gap(true) > Gap(false) + 10, "the ball cost it ground");
    }
}
