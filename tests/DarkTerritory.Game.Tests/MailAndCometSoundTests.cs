using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// K1's merged asks heard (queue #504, note 774): the mail catch at a lineside crane, the bag snatched or (hooked over the
/// posted speed) torn off, each heard once and not again for an ear that joins after; a comet fragment's hum near it.
/// </summary>
public class MailAndCometSoundTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly RunTuning R = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
    // The mail cranes up (cut for now, note 803): the catch is still heard for when it comes back.
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(Content, SightTuning.File)) with { MailCranes = true };

    readonly string _root = Path.Combine(Path.GetTempPath(), "dt-mail-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    GameAudio Audio()
    {
        Directory.CreateDirectory(Path.Combine(_root, "audio", "sounds"));
        if (!File.Exists(Path.Combine(_root, MixDef.File)))
        {
            File.Copy(Path.Combine(Content, MixDef.File), Path.Combine(_root, MixDef.File));
            File.Copy(Path.Combine(Content, "audio", "spaces.json"), Path.Combine(_root, "audio", "spaces.json"));
        }
        var audio = new GameAudio(_root);
        foreach (var s in new[] { "crew-mail.catch", "crew-mail.tear", "world-comet.hum~" })
            audio.Bank.Add(s.TrimEnd('~'), new SoundDef(3, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 64));
        return audio;
    }

    /// <summary>A crewmate at car 2's open side door with the hook out, the train going by the first crane at <paramref name="speed"/>.</summary>
    (World World, List<string> Heard) Past(double speed)
    {
        var route = new Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(3000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 600);
        var world = new World(train, C);
        world.EnableBodies();
        world.EnableRun(R, route, RouteTuning.Load(Content).YardLength, authority: true);
        world.EnableLineside(S, route);
        var drop = world.Lineside!.AllDrops.First();
        var shape = train.Frames[2].Shape;
        int door = StopHand.SideDoor(shape, drop.Side)!.Value;
        train.Vehicles[2].ToggleDoor(door);
        var (at, yaw) = WarmUp.Inside(shape, door);
        var s = new PlayerState { Parent = 2, Position = at with { Y = T.Geometry.Interior!.FloorHeight }, Yaw = yaw, Surface = Surface.Deck, Health = P.Health, LineHint = train.Cars[2].FrontDistance };
        var intent = new PlayerIntent { Buttons = PlayerButtons.Fire };
        var audio = Audio();
        var heard = new List<string>();
        var seen = new HashSet<int>();
        while (train.Dynamics.RearDistance < drop.At + 5)
        {
            train.Dynamics.Velocity = speed;
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            var ear = PlayerMotor.WorldPosition(s, train) + Double3.Up * 1.6;
            audio.Update(world, world.Controls, Listener.At(ear, 0), exposed: true, SimConstants.TickSeconds);
            heard.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id)).Select(v => v.Name));
        }
        return (world, heard);
    }

    [Fact]
    public void ABagCaughtIsHeardSnatchedAndOneHookedTooFastTornOnceEach()
    {
        var (caughtWorld, caught) = Past(13);
        Assert.True(caughtWorld.Lineside!.Caught(caughtWorld.Lineside.AllDrops.First().Id));
        Assert.Equal(["crew-mail.catch"], caught.Where(h => h.StartsWith("crew-mail.")));
        var (tornWorld, torn) = Past(S.CatchMaxSpeed + 2);
        Assert.True(tornWorld.Lineside!.Torn(tornWorld.Lineside.AllDrops.First().Id));
        Assert.Equal(["crew-mail.tear"], torn.Where(h => h.StartsWith("crew-mail.")));
        // An ear that joins after (a fresh GameAudio) doesn't hear the old catch.
        var late = Audio();
        late.Update(tornWorld, tornWorld.Controls, Listener.At(tornWorld.Train.Frames[2].Origin, 0), exposed: true, SimConstants.TickSeconds);
        late.Update(tornWorld, tornWorld.Controls, Listener.At(tornWorld.Train.Frames[2].Origin, 0), exposed: true, SimConstants.TickSeconds);
        Assert.DoesNotContain(late.Mixer.Voices, v => v.Name.StartsWith("crew-mail."));
    }

    [Fact]
    public void ACometFragmentHumsNearItAndNotFromAcrossTheYard()
    {
        var route = new Route("t", RouteTier.Frontier, 1, new LineDefinition("t", [new TrackSegment(3000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0.5)), route.Build(), 600);
        var world = new World(train, C);
        world.EnableBodies();
        var at = train.Line.Sample(800).Position + new Double3(8, 0.3, 0);
        var fragment = world.Bodies.SpawnLoot(at, 800, (1 << 12) | Run.CometContainer, 0.3);
        Assert.True(Run.IsComet(fragment));
        var audio = Audio();
        bool Humming() => audio.Mixer.Voices.Any(v => v.Name == "world-comet.hum" && !v.Stopped && !v.Finished);
        for (int i = 0; i < 3; i++)
            audio.Update(world, world.Controls, Listener.At(at + new Double3(4, 1.3, 0), 0), exposed: true, SimConstants.TickSeconds);
        Assert.True(Humming());
        for (int i = 0; i < SimConstants.TickRate; i++)
            audio.Update(world, world.Controls, Listener.At(at + new Double3(80, 1.3, 0), 0), exposed: true, SimConstants.TickSeconds);
        Assert.False(Humming());
    }
}
