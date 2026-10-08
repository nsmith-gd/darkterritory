using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Main's features since 7 Oct heard (queue #61, ARCHITECTURE §8 note 322): a healing find used, the crew's emotes and
/// outfits, the shovel's rack, an engine short of steam, a derelict shunted out, and a ball landing as what it struck. Each
/// from replicated state, at the moment it happens, once; the sounds are stand-ins, so what's installed doesn't matter.
/// </summary>
public class FeatureSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly BoilerTuning B = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
    const double Dt = SimConstants.TickSeconds;

    static void StandIn(GameAudio audio, params string[] cues)
    {
        foreach (var cue in cues)
            audio.Bank.Add(cue.TrimEnd('~'), new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Loop: cue.EndsWith('~'),
                Duration: 0.1, MaxInstances: 64));
    }

    /// <summary>The voices started since the last look, by name.</summary>
    sealed class Ears(GameAudio audio)
    {
        readonly HashSet<int> _seen = [];
        public List<string> New() => [.. audio.Mixer.Voices.Where(v => _seen.Add(v.Id)).Select(v => v.Name)];
        public bool Playing(string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped && !v.Finished);
    }

    [Fact]
    public void AHealingFindIsHeardWhileItsUsedAndOnceWhenItsUsedUp()
    {
        // Note 272: a find that heals, used with Use held standing, is gone when the dose is done. Its own sound while the
        // hands are at it (by what it is: a bandage, medicine, morphine), and the hurt eased once it's gone.
        World? world = null;
        Body? find = null;
        for (ulong seed = 1; find is null; seed++)
        {
            Assert.True(seed <= 40, "no healing find on 40 nights' stops");
            var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, seed);
            world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 0)), route.Build(), 3_000, B), C);
            world.EnableBodies();
            world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, route.GateOr(600), authority: true, loot: L);
            // Searched too: a village's finds are in its open houses' cupboards and cellars (note 326).
            for (int k = 0; k < world.Run!.Stops.Count; k++)
                world.Run.Stock(world.Bodies, k, searched: true);
            find = world.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Loot && world.Run.HealOf(b) > 0);
        }
        string item = world!.Run!.FindOf(find!)!.Value.Item;
        Assert.Contains(item, L.Healing!.Heals.Keys);
        var line = world.Train.Line.Sample(2_950);
        var at = line.Position + Double3.Cross(line.Tangent, Double3.Up).Normalized * 8;
        double hint = 2_950;
        var me = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, world.Train.Line, ref hint) }, world.Train.Line, 2_950, P) with { Health = 40 };
        find!.Carrier = 1;
        find.Claimed = true;

        var audio = new GameAudio(Content);
        StandIn(audio, $"crew-heal.apply.{item}~", "crew-heal.done");
        var ears = new Ears(audio);
        var heard = new List<string>();
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        bool appliedWhileHeld = false;
        for (int i = 0; i < Math.Round((L.Healing.UseSeconds + 0.5) * SimConstants.TickRate); i++)
        {
            world.BeginTick();
            world.CrewAct(ref me, use, 1);
            audio.CrewStates = [(1, me)];
            audio.Update(world, world.Controls, Listener.At(PlayerMotor.WorldPosition(me, world.Train), 0), exposed: true, Dt);
            heard.AddRange(ears.New());
            if (world.Bodies.All.Contains(find))
                appliedWhileHeld |= ears.Playing($"crew-heal.apply.{item}");
        }
        Assert.DoesNotContain(find, world.Bodies.All);
        Assert.True(appliedWhileHeld, string.Join(", ", heard));
        Assert.Equal(1, heard.Count(h => h == $"crew-heal.apply.{item}"));
        Assert.Equal(1, heard.Count(h => h == "crew-heal.done"));
        Assert.False(ears.Playing($"crew-heal.apply.{item}"));
    }

    /// <summary>A world on a straight line with a crewmate on car 1's roof, heard from beside them.</summary>
    sealed class Roof
    {
        public readonly World World = new(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)),
            new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000, B), C);
        public readonly GameAudio Audio = new(Content);
        public readonly Ears Ears;
        public readonly PlayerState Me;
        readonly HashSet<string> _cues;

        public Roof(params string[] cues)
        {
            _cues = [.. cues.Select(c => c.TrimEnd('~'))];
            StandIn(Audio, cues);
            Ears = new Ears(Audio);
            var frame = World.Train.Frames[1];
            Me = new PlayerState { Parent = 1, Position = new Double3(0, frame.Shape.RoofHeight, 0), Health = P.Health };
        }

        public List<string> Step()
        {
            World.BeginTick();
            Audio.CrewStates = [(2, Me)];
            Audio.Update(World, World.Controls, Listener.At(World.Train.Frames[1].Origin + Double3.Up * 4, 0), exposed: true, Dt);
            // Only what's under test: the train's own bed and the night go on underneath.
            return Ears.New().Where(_cues.Contains).ToList();
        }
    }

    [Fact]
    public void AnEmoteIsHeardOnceWhereItsPlayerStandsAndSoIsAnOutfitTriedOn()
    {
        // Note 298: emotes and outfits are the crew's to see; note 322 gives them their sounds. What's already there when
        // the client first looks is old news.
        var r = new Roof("crew-emotes.dance", "crew-emotes.wave", "crew-emotes.point", "crew-emotes.outfit");
        r.World.Emotes.Add(new EmoteEvent(1, (uint)r.World.Tick, 2, Emote.Wave));
        r.World.Outfits[2] = 1;
        Assert.Empty(r.Step());
        r.World.Emotes.Add(new EmoteEvent(2, (uint)r.World.Tick, 2, Emote.Dance));
        Assert.Equal(["crew-emotes.dance"], r.Step());
        var dance = r.Audio.Mixer.Voices.First(v => v.Name == "crew-emotes.dance");
        Assert.True((dance.Position - PlayerMotor.WorldPosition(r.Me, r.World.Train)).Length < 1.5);
        // Still on the wire: not again.
        Assert.Empty(r.Step());
        r.World.Emotes.Add(new EmoteEvent(3, (uint)r.World.Tick, 2, Emote.Point));
        Assert.Equal(["crew-emotes.point"], r.Step());
        // An outfit tried on: heard as it changes, not while it stays.
        r.World.Outfits[2] = 3;
        Assert.Equal(["crew-emotes.outfit"], r.Step());
        Assert.Empty(r.Step());
    }

    [Fact]
    public void TheShovelIsHeardComingOffItsRackAndGoingBackOn()
    {
        // Note 275: the fireman's shovel lives on the cab's rack; ShovelOut is the boiler's, replicated.
        var r = new Roof("crew-melee.shovel-rack-off", "crew-melee.shovel-rack-on");
        Assert.Empty(r.Step());
        r.World.Train.Boiler.ShovelOut = true;
        Assert.Equal(["crew-melee.shovel-rack-off"], r.Step());
        Assert.Empty(r.Step());
        r.World.Train.Boiler.ShovelOut = false;
        Assert.Equal(["crew-melee.shovel-rack-on"], r.Step());
        Assert.Empty(r.Step());
    }

    [Fact]
    public void AnEngineShortOfSteamIsHeardLabouringAsHardAsItsHeldBack()
    {
        // Note 319: past the speed its steam makes, a boiler under its working band holds the train back. The struggle is
        // heard while it does, louder the nearer the drag is to its worst, and not in the working band.
        var r = new Roof("state-starved.labour~");
        var train = r.World.Train;
        train.Dynamics.Velocity = 15;
        train.Boiler.Pressure = B.WorkingBandMin + 5;
        r.Step();
        Assert.Equal(0, B.StarvedDrag(train.Boiler, train.Dynamics.Speed, train.Dynamics.Tuning.MaxSpeed));
        Assert.False(r.Ears.Playing("state-starved.labour"));
        train.Boiler.Pressure = B.PowerFloor + (B.WorkingBandMin - B.PowerFloor) * 0.5;
        r.Step();
        Assert.True(r.Ears.Playing("state-starved.labour"));
        float some = r.Audio.Mixer.Voices.First(v => v.Name == "state-starved.labour").Volume;
        train.Boiler.Pressure = B.PowerFloor;
        r.Step();
        Assert.True(r.Audio.Mixer.Voices.First(v => v.Name == "state-starved.labour").Volume > some);
        train.Boiler.Pressure = B.WorkingBandMin + 5;
        r.Step();
        r.Step();
        Assert.False(r.Ears.Playing("state-starved.labour"));
    }

    [Fact]
    public void ADerelictIsHeardGrindingWhileItsShuntedAndNotStanding()
    {
        // Note 294: a blocked siding's derelicts, shunted out on their seized axles. The train's own cars roll as before.
        var r = new Roof("place-derelict.roll~");
        var train = r.World.Train;
        var derelict = train.StandDerelicts(RailLine.MainPath, train.Dynamics.RearDistance - 30, 1, 0.4, 0.5)[0];
        var rake = train.Rakes.First(k => k.Consist.Vehicles.Contains(derelict));
        var ear = Listener.At(train.Frames[derelict.Id].Origin + Double3.Up * 3, 0);
        void Step()
        {
            r.World.BeginTick();
            r.Audio.Update(r.World, r.World.Controls, ear, exposed: true, Dt);
        }
        Step();
        Assert.False(r.Ears.Playing("place-derelict.roll"));
        rake.Velocity = 1.5;
        Step();
        Assert.True(r.Ears.Playing("place-derelict.roll"));
        Assert.All(r.Audio.Mixer.Voices.Where(v => v.Name == "place-derelict.roll"), v => Assert.True((v.Position - train.Frames[derelict.Id].Origin).Length < 3));
        rake.Velocity = 0;
        Step();
        Step();
        Assert.False(r.Ears.Playing("place-derelict.roll"));
    }

    [Fact]
    public void ABallLandsAsWhatItStruckAndTheBoomWhereTheresNoTakeOfItsOwn()
    {
        // Note 290: a ball meets creatures, walls and the train's own body. Each lands as what it struck (note 322); the
        // doll's porcelain is still the doll's, and a surface with nothing installed of its own is the boom it was.
        string root = Directory.CreateTempSubdirectory("dt-landed-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "audio", "sounds"));
            File.Copy(Path.Combine(Content, MixDef.File), Path.Combine(root, MixDef.File));
            File.Copy(Path.Combine(Content, "audio", "spaces.json"), Path.Combine(root, "audio", "spaces.json"));
            var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1200);
            var world = new World(train);
            var audio = new GameAudio(root);
            StandIn(audio, "cannon-impact", "doll-shatter", "crew-cannon-impact.flesh", "crew-cannon-impact.structure");
            var ears = new Ears(audio);
            var ear = Listener.At(train.Frames[0].Origin, train.Frames[0].Heading);
            void Update() => audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, Dt);
            Update();
            ears.New();
            var ahead = train.Frames[0].ToWorld(new Double3(0, 0, -60));
            var ball = new CannonImpact(1, 0, ahead, Double3.Up, ImpactSurface.Creature, 1) { Struck = Sim.Enemies.EnemyKind.Ribbit };
            world.Impacts.Add(ball);
            world.Impacts.Add(ball with { Id = 2, Surface = ImpactSurface.Structure, Struck = 0 });
            world.Impacts.Add(ball with { Id = 3, Surface = ImpactSurface.Train, Struck = 0 });
            world.Impacts.Add(ball with { Id = 4, Struck = Sim.Enemies.EnemyKind.TrackDoll });
            Update();
            var heard = ears.New();
            Assert.Equal(1, heard.Count(h => h == "crew-cannon-impact.flesh"));
            Assert.Equal(1, heard.Count(h => h == "crew-cannon-impact.structure"));
            // The train's iron has no take here, and the doll's is the boom and her porcelain.
            Assert.Equal(2, heard.Count(h => h == "cannon-impact"));
            Assert.Equal(1, heard.Count(h => h == "doll-shatter"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
