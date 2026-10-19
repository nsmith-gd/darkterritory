using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Wakers heard (queue #502, note 772; wakers.md §2-§5): the stir's thuds on the tremor's beat and its calls, the land
/// getting up, its footfalls and roars as it runs a standing train down, the last car seized, and each car torn up and eaten.
/// </summary>
public class WakerSoundTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly EnemyTuning E = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));
    static readonly WakersTuning W = E.Wakers;

    readonly string _root = Path.Combine(Path.GetTempPath(), "dt-wakers-" + Guid.NewGuid().ToString("N"));
    readonly HashSet<int> _heard = [];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    GameAudio Audio(params string[] sounds)
    {
        Directory.CreateDirectory(Path.Combine(_root, "audio", "sounds"));
        File.Copy(Path.Combine(Content, MixDef.File), Path.Combine(_root, MixDef.File));
        File.Copy(Path.Combine(Content, "audio", "spaces.json"), Path.Combine(_root, "audio", "spaces.json"));
        var audio = new GameAudio(_root);
        foreach (var s in sounds)
            audio.Bank.Add(s.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 64));
        return audio;
    }

    [Fact]
    public void AStandingTrainHearsTheStirTheRiseTheRunAndEachCarTakenAndEaten()
    {
        Assert.True(W.Enabled && W.StirSeconds > 0);
        var routeTuning = RouteTuning.Load(Content);
        var route = RouteGenerator.Generate(routeTuning, RouteTier.Frontier, 7) with { DawnSeconds = W.StirSeconds + 30 };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 9_000);
        var world = new World(train, C);
        world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, 600, authority: true);
        world.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        var player = PlayerMotor.SpawnInCab(train, P);
        var audio = Audio("tell-wakers.thud", "tell-wakers.call", "tell-wakers.rise", "tell-wakers.stride", "tell-wakers.roar",
            "cs-wakers.grab", "cs-wakers.drag~", "cs-wakers.lift", "cs-wakers.eat");
        var heard = new List<(double At, string Name)>();
        double t = 0;
        while (world.Run?.Over != true && t < 600)
        {
            world.BeginTick();
            world.CrewAct(ref player, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepRun([player]);
            var ear = train.Frames[0].ToWorld(new Double3(0, 2.5, 0));
            audio.Update(world, world.Controls, Listener.At(ear, train.Frames[0].Heading), exposed: true, SimConstants.TickSeconds);
            t += SimConstants.TickSeconds;
            heard.AddRange(audio.Mixer.Voices.Where(v => _heard.Add(v.Id)).Select(v => (t, v.Name)));
        }
        int Count(string name, double from = 0, double to = double.MaxValue) => heard.Count(h => h.Name == name && h.At >= from && h.At < to);
        // Before the stir, nothing; through it, its thuds closer together as dawn comes, and a call or more.
        Assert.Equal(0, Count("tell-wakers.thud", 0, 29));
        int early = Count("tell-wakers.thud", 30, 30 + W.StirSeconds / 2), late = Count("tell-wakers.thud", 30 + W.StirSeconds / 2, 30 + W.StirSeconds);
        Assert.True(early >= 5 && late > early, $"the stir's thuds: {early} in its first half, {late} in its second");
        Assert.InRange(Count("tell-wakers.call", 30, 30 + W.StirSeconds), 2, 20);
        // Up at dawn (one on a Frontier night); then its footfalls, a roar or more, and no stir thuds while it runs.
        int rising = W.For(RouteTier.Frontier);
        Assert.Equal(rising, Count("tell-wakers.rise"));
        double up = heard.First(h => h.Name == "tell-wakers.rise").At;
        double grab = heard.Single(h => h.Name == "cs-wakers.grab").At;
        Assert.True(Count("tell-wakers.stride", up, grab) >= 10, $"{Count("tell-wakers.stride", up, grab)} footfalls in the chase");
        Assert.True(Count("tell-wakers.roar", up, grab) >= 1);
        Assert.Equal(0, Count("tell-wakers.thud", up + W.RiseSeconds + 1, grab));
        // The catch: the grab takes the last car up with it, and each car after is torn up on its own; then every one eaten,
        // the engine last, none before eatAfter.
        int vehicles = train.Dynamics.Consist.Vehicles.Count;
        Assert.Equal(vehicles - 1, Count("cs-wakers.lift", grab));
        Assert.Equal(vehicles, Count("cs-wakers.eat", grab));
        Assert.True(heard.Where(h => h.Name == "cs-wakers.eat").All(h => h.At >= grab + W.EatAfter - 0.1));
    }

    [Fact]
    public void StoodAtTheWallsTheTownsGunsAreHeardFiringOnItAndEachBallLanding()
    {
        // Pulled in just inside the gate at dawn (WakersTests.UnderTheTerminusGunsTheyStopShortAndTheCrewIsHome): the lead
        // stands stopShort out, and each of the gatehouse's and towers' guns fires on it on its own beat (Effects.WallGunBeat,
        // as the scene draws them), every ball landing on its back its flight later. Nothing fires before it's stood there.
        var runTuning = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7) with { DawnSeconds = 3 };
        double gate = route.Length - runTuning.TerminusZone;
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), gate - 20);
        var world = new World(train, C);
        world.EnableRun(runTuning, route, 600, authority: true);
        world.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        var player = PlayerMotor.SpawnInCab(train, P);
        var audio = Audio("town-guns.fire", "town-guns.hit", "gunshot", "tell-wakers.rise", "tell-wakers.stride", "tell-wakers.roar");
        double wall = GreyboxScene.WakerWall(train.Line, route, world.Run, W), t = 0, stood = -1;
        var heard = new List<(double At, string Name)>();
        while (t < W.RiseSeconds + 150 && world.Run?.Over != true)
        {
            world.BeginTick();
            world.CrewAct(ref player, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepRun([player]);
            var ear = train.Frames[0].ToWorld(new Double3(0, 2.5, 0));
            audio.Update(world, world.Controls, Listener.At(ear, train.Frames[0].Heading), exposed: true, SimConstants.TickSeconds);
            t += SimConstants.TickSeconds;
            if (stood < 0 && world.ActiveEnemies.OfType<Waker>().Any(w => w.Lead && w.Phase == SpinePhase.Commit && w.LineDistance >= wall - 1))
                stood = t;
            heard.AddRange(audio.Mixer.Voices.Where(v => _heard.Add(v.Id)).Select(v => (t, v.Name)));
        }
        Assert.False(world.Run!.Over);
        Assert.True(stood > 0, "the lead never stood at the walls");
        var lead = world.ActiveEnemies.OfType<Waker>().Single(w => w.Lead);
        Assert.Equal(wall, lead.LineDistance, 1.0);
        int guns = (world.Arrival is { Plan.Bounds: not null } walled ? GreyboxScene.WallGuns(walled, Double3.Zero)
            : GreyboxScene.WallGuns(train.Line, wall + W.StopShort, Double3.Zero)).Count;
        Assert.True(guns >= 2);
        int fired = heard.Count(h => h.Name == "town-guns.fire"), landed = heard.Count(h => h.Name == "town-guns.hit");
        Assert.DoesNotContain(heard, h => h.Name.StartsWith("town-guns.") && h.At < stood);
        // Each gun has its turn every WallGunEvery and fires in about three in five of them, more in a burst and fewer in a
        // lull (note 834: never on a beat), give or take its first and last; each ball landing (but those still in the air).
        double standing = t - stood, turns = standing / Art.Effects.WallGunEvery;
        Assert.True(standing > 60, $"stood under the guns {standing:0} s");
        Assert.InRange(fired, guns * (turns * 0.35 - 1), guns * (turns * 0.85 + 1));
        Assert.InRange(landed, fired - guns, fired);
        Assert.DoesNotContain(heard, h => h.Name == "gunshot");
    }
}
