using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The crew's cues on the audio checklist (GameAudio.Crew.cs) play at the moment the replicated state says the thing
/// happened, once, where it happened, and on what's underfoot. Each cue is installed here as a short tone (the real
/// takes come from tools/audio/install.py), so what plays can be counted off the mixer.
/// </summary>
public class CrewAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    const double Dt = SimConstants.TickSeconds;

    /// <summary>A world on a straight hand-laid line, the audio with these cues installed, and what's been heard.</summary>
    sealed class Bench
    {
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public readonly List<(string Name, Double3 At, int Tick)> Heard = [];
        readonly HashSet<int> _seen = [];
        public int Tick;

        public Bench(params string[] cues)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
            foreach (var cue in cues)
                Audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
            Audio.PlayerTuning = P;
        }

        public TrainOnLine Train => World.Train;

        /// <summary>One tick: the world, then what's heard with this crew aboard.</summary>
        public void Step(params (int Id, PlayerState State)[] crew)
        {
            World.BeginTick();
            World.Step(World.Controls);
            Audio.CrewStates = crew;
            Audio.Update(World, World.Controls, Listener.At(Train.Frames[1].Origin + Double3.Up * 2, 0), exposed: true, Dt);
            // The crew's (and the old gunshot), not the train's bed going on underneath.
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && (v.Name.StartsWith("crew-") || v.Name == "gunshot"))
                    Heard.Add((v.Name, v.Position, Tick));
            Tick++;
        }

        public int Count(string name) => Heard.Count(h => h.Name == name);
    }

    [Fact]
    public void ASlidingDoorOpensOnceRollsToItsStopAndLatchesWhenShut()
    {
        var b = new Bench("crew-doors.slide-start", "crew-doors.slide-roll", "crew-doors.slide-open-stop", "crew-doors.slide-shut",
            "crew-doors.slide-latch", "crew-doors.hatch-open", "crew-doors.end-open");
        var car = b.Train.Vehicles[1];
        var shape = b.Train.Frames[1].Shape;
        Assert.NotNull(shape.Interior);
        // Already open when first seen: that's how it is, not something happening.
        car.DoorsOpen = 1;
        b.Step();
        Assert.Empty(b.Heard);
        car.DoorsOpen = 0;
        b.Step();
        car.DoorsOpen = 1 << 2;
        for (int i = 0; i < 45; i++)
            b.Step();
        Assert.Equal(1, b.Count("crew-doors.slide-start"));
        Assert.Equal(1, b.Count("crew-doors.slide-roll"));
        Assert.Equal(1, b.Count("crew-doors.slide-open-stop"));
        var start = b.Heard.First(h => h.Name == "crew-doors.slide-start");
        var stop = b.Heard.First(h => h.Name == "crew-doors.slide-open-stop");
        Assert.InRange((stop.Tick - start.Tick) * Dt, 0.8, 1.0);
        // At the door, in the car's side.
        var door = shape.DoorList.First(d => d.Index == 2).Box.Centre;
        Assert.True((start.At - b.Train.Frames[1].ToWorld(door)).Length < 0.01);
        Assert.Equal(0, b.Count("crew-doors.end-open"));

        car.DoorsOpen = 0;
        for (int i = 0; i < 15; i++)
            b.Step();
        Assert.Equal(1, b.Count("crew-doors.slide-shut"));
        Assert.Equal(1, b.Count("crew-doors.slide-latch"));
        car.DoorsOpen = 1 << CarShape.HatchBit;
        b.Step();
        Assert.Equal(1, b.Count("crew-doors.hatch-open"));
    }

    [Fact]
    public void FootstepsFallOncePerStrideAtTheFeetOnWhatsUnderThem()
    {
        var b = new Bench("crew-footsteps.walk.roof", "crew-footsteps.walk.wood", "crew-footsteps.run.ballast", "crew-footsteps.run.roof",
            "crew-footsteps.scuff.ballast");
        // Walking the roof of car 2 at the roof's safe walk, 3 s: one step every ~0.8 m, each at the feet, all on the roof.
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 3, P);
        b.Step((1, s));
        var at = new List<Double3>();
        for (int i = 0; i < 90; i++)
        {
            s.Position -= new Double3(0, 0, P.RoofWalkSafe * Dt);
            b.Step((1, s));
            at.Add(PlayerMotor.WorldPosition(s, b.Train));
        }
        int steps = b.Count("crew-footsteps.walk.roof");
        Assert.InRange(steps, 5, 8);
        Assert.Equal(0, b.Count("crew-footsteps.run.roof"));
        Assert.All(b.Heard.Where(h => h.Name == "crew-footsteps.walk.roof"), h => Assert.True((h.At - at[h.Tick - 1]).Length < 0.01));

        // Standing still: nothing.
        int before = b.Heard.Count;
        for (int i = 0; i < 30; i++)
            b.Step((1, s));
        Assert.Equal(before, b.Heard.Count);

        // Off the train beside it, running on the ballast; stopping short scuffs.
        var line = b.Train.Line;
        double along = b.Train.Dynamics.Distance - 30;
        var g = PlayerMotor.SpawnOnGround(line.Sample(along).Position + Double3.Cross(line.Sample(along).Tangent, Double3.Up).Normalized * 1.0, line, along, P);
        b.Step((2, g));
        for (int i = 0; i < 60; i++)
        {
            g.Position += line.Sample(along).Tangent * (P.Run * Dt);
            b.Step((2, g));
        }
        for (int i = 0; i < 10; i++)
            b.Step((2, g));
        Assert.InRange(b.Count("crew-footsteps.run.ballast"), 5, 9);
        Assert.Equal(1, b.Count("crew-footsteps.scuff.ballast"));
    }

    [Fact]
    public void WhatsUnderfootIsWhatTheArtDrawsThere()
    {
        var b = new Bench();
        var train = b.Train;
        var engine = train.Frames[0].Shape;
        var cab = engine.Cab!.Value;
        Assert.Equal("wood", Footing.OnCar(train, 0, new Double3(0.6, cab.Min.Y + 0.1, cab.Centre.Z)));
        Assert.Equal("plate", Footing.OnCar(train, 0, new Double3(0, cab.Min.Y + 0.1, cab.Max.Z - 0.3)));
        var tender = engine.Solids.First(x => x.Part == PartKind.Tender).Box;
        Assert.Equal("coal", Footing.OnCar(train, 0, tender.Centre with { Y = tender.Max.Y }, Surface.Roof));
        var board = engine.Solids.First(x => x.Part == PartKind.RunningBoard).Box;
        Assert.Equal("grate", Footing.OnCar(train, 0, board.Centre with { Y = board.Max.Y }));
        var car = train.Frames[1].Shape;
        Assert.Equal("roof", Footing.OnCar(train, 1, new Double3(0, car.RoofHeight, -car.HalfLength + 1), Surface.Roof));
        Assert.Equal("wood", Footing.OnCar(train, 1, new Double3(0, car.Interior!.Value.Min.Y + 0.1, 0)));
        var plate = car.Solids.First(x => x.Part == PartKind.Coupler).Box;
        Assert.Equal("grate", Footing.OnCar(train, 1, plate.Centre with { Y = plate.Max.Y }, Surface.Coupler));

        // Off the train, across the hand-laid line's bands: the bed's ballast, the ditch's mud, then the grass.
        var line = train.Line;
        double s = 2_000;
        var sample = line.Sample(s);
        var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
        string At(double lateral)
        {
            double hint = s;
            return Footing.Ground(b.World, sample.Position + right * lateral, ref hint);
        }
        Assert.Equal("ballast", At(1));
        Assert.Equal("mud", At(-5));
        Assert.Equal("grass", At(9.5));
    }

    [Fact]
    public void OnAGeneratedLineTheGroundIsItsBiomesAndItsStopsAreBuiltOn()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var plan = route.Plan!;
        // A point's ground texture, from a line hint where it is (a player's, as it walks).
        string At(double s, double lateral)
        {
            var t = line.Sample(s);
            var p = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
            double hint = s;
            return Art.WorldArt.GroundTexture(line, route, p with { Y = line.Conditions!.Ground(p) }, ref hint);
        }
        // Out on the open line, away from anything built: the bed's ballast, and past it the biome's own ground.
        double open = Enumerable.Range(1, 400).Select(i => i * 25.0).First(s =>
            !route.Features.Any(f => s > f.Start - 300 && s < f.End + 300) && !plan.Crossings.Any(c => Math.Abs(c.S - s) < 30));
        Assert.Equal("ballast", At(open, 1));
        var biome = plan.Rules.Biomes[Art.WorldArt.BiomeAt(route, open)!];
        string[] land = [biome.Ground, .. biome.Materials.Take(1), "shore_shingle", "ground_red_clay", "ballast"];
        foreach (double lateral in new[] { -40.0, 25, 60 })
            Assert.Contains(At(open, lateral), land);
        // A stop: the floor of the building nearest the line.
        var stop = route.Features.First(f => f.Stop is { Buildings.Count: > 0 });
        var house = stop.Stop!.Buildings.MinBy(b => Math.Abs(b.D))!;
        Assert.Equal("concrete", At(stop.Start + house.S, house.D));
        Assert.All(new[] { "ballast", "concrete", "cobbles", biome.Ground }, t => Assert.NotEqual("ground", Footing.OfTexture(t)));
    }

    [Fact]
    public void ABotCrewIsHeardOverTheNetworkAsTheAppHearsIt()
    {
        // A night with a bot crew, each a client over loopback, heard as the app hears it: the client world, the crew from
        // the session (crewmates as drawn), every tick. Their feet on the roofs, the driver's regulator.
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: null, bots: 3);
        var audio = new GameAudio(Content);
        string[] feet = ["wood", "grate", "plate", "roof", "coal", "ballast", "dirt", "grass", "mud", "cobbles", "concrete"];
        foreach (var cue in feet.SelectMany(m => new[] { $"crew-footsteps.walk.{m}", $"crew-footsteps.run.{m}" })
            .Append("crew-cab-controls.regulator-notch").Append("crew-ladder.rung-up").Append("crew-ladder.grab"))
            audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
        var heard = new Dictionary<string, int>();
        var seen = new HashSet<int>();
        for (int t = 0; t < SimConstants.TickRate * 30; t++)
        {
            night.Step(default);
            audio.CrewStates = night.CrewStates(1);
            audio.OwnId = night.PlayerId;
            audio.Update(night.World, night.Controls, Listener.At(PlayerMotor.WorldPosition(night.Player, night.Train), 0), exposed: true, Dt);
            foreach (var v in audio.Mixer.Voices)
                if (seen.Add(v.Id) && v.Name.StartsWith("crew-"))
                    heard[v.Name] = heard.GetValueOrDefault(v.Name) + 1;
            Thread.Sleep(1);
        }
        Assert.Equal(4, night.CrewStates(1).Count);
        Assert.True(heard.Where(h => h.Key.StartsWith("crew-footsteps.")).Sum(h => h.Value) > 10, string.Join(", ", heard));
        Assert.True(heard.GetValueOrDefault("crew-cab-controls.regulator-notch") > 0, string.Join(", ", heard));
    }

    [Fact]
    public void EveryonesShotIsHeardOnceFromTheGunAndAGunPushedOntoAnotherCarIsNoShot()
    {
        var b = new Bench("crew-cannon-fire.shot-close", "crew-cannon-fire.shot-far", "crew-cannon-fire.recoil", "crew-cannon-fire.traverse",
            "crew-cannon-fire.traverse-stop", "crew-cannon-reload.powder", "crew-cannon-reload.powder-done", "crew-cannon-ball.ball-in",
            "crew-cannon-ball.ball-roll", "crew-cannon-ram.ram", "crew-cannon-ram.ram-home");
        var guard = b.Train.Vehicles.Last(v => v.HasGun);
        b.Step();
        // A crewmate's shot (it's in nobody's world.Shots but theirs): a round gone, the gun to reload.
        guard.Gun.Ammo--;
        guard.Gun.LastShotTick = 99;
        guard.Gun.ReloadNeeded = C.Guns.ReloadSteps;
        b.Step();
        b.Step();
        Assert.Equal(1, b.Count("crew-cannon-fire.shot-close") + b.Count("crew-cannon-fire.shot-far"));
        Assert.Equal(1, b.Count("crew-cannon-fire.recoil"));
        Assert.Equal(0, b.Count("gunshot"));
        var mount = Guns.Mount(b.Train, guard.Id)!.Value;
        var shot = b.Heard.First(h => h.Name.StartsWith("crew-cannon-fire.shot"));
        Assert.True((shot.At - b.Train.Frames[guard.Id].ToWorld(mount.Position + mount.Facing * 1.5)).Length < 0.01);

        // The reload, step by step: powder rammed while it's held, seated; the ball in and down; rammed home.
        foreach (int step in new[] { 3, 2, 1 })
        {
            for (int i = 0; i < 10; i++)
            {
                guard.Gun.ReloadProgress += Dt;
                b.Step();
            }
            guard.Gun.ReloadProgress = 0;
            guard.Gun.ReloadNeeded = step - 1;
            b.Step();
        }
        Assert.Equal(1, b.Count("crew-cannon-reload.powder"));
        Assert.Equal(1, b.Count("crew-cannon-reload.powder-done"));
        Assert.Equal(1, b.Count("crew-cannon-ball.ball-in"));
        Assert.Equal(1, b.Count("crew-cannon-ball.ball-roll"));
        Assert.Equal(1, b.Count("crew-cannon-ram.ram"));
        Assert.Equal(1, b.Count("crew-cannon-ram.ram-home"));

        // Pushed along the rail and over the coupling onto the car ahead: it traverses, and it's no shot.
        int ahead = b.Train.VehicleAhead(guard.Id);
        for (int i = 0; i < 10; i++)
        {
            Guns.Slide(b.Train, guard.Id, -0.04);
            b.Step();
        }
        var moved = b.Train.Vehicles[ahead];
        moved.Gun = guard.Gun;
        guard.Gun = default;
        for (int i = 0; i < 10; i++)
            b.Step();
        Assert.Equal(1, b.Count("crew-cannon-fire.shot-close") + b.Count("crew-cannon-fire.shot-far"));
        Assert.True(b.Count("crew-cannon-fire.traverse") >= 1);
        Assert.True(b.Count("crew-cannon-fire.traverse-stop") >= 1);
    }

    [Fact]
    public void JumpLandClimbAndCutACoupling()
    {
        var b = new Bench("crew-footsteps.jump.roof", "crew-footsteps.land.roof", "crew-ladder.grab", "crew-ladder.rung-up", "crew-ladder.let-go",
            "crew-coupling.knuckle-release", "crew-coupling.hose-part", "crew-coupling.knuckle-close", "crew-coupling.pin-drop", "crew-hurt.hit",
            "crew-hurt.body-fall.grate", "crew-hurt.body-fall.wood", "crew-melee.crowbar-stow", "crew-melee.wrench-equip");
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        b.Step((1, s));
        // Up, and down again on the roof: the push-off and the landing, once each, nothing in between.
        var air = s with { Surface = Surface.Air, Velocity = new Double3(0, 3, 0) };
        b.Step((1, air));
        b.Step((1, air with { Velocity = new Double3(0, -2, 0) }));
        b.Step((1, s));
        Assert.Equal(1, b.Count("crew-footsteps.jump.roof"));
        Assert.Equal(1, b.Count("crew-footsteps.land.roof"));
        Assert.Equal(0, b.Count("crew-hurt.hit"));

        // A ladder up the car's side: hands on, a rung each 0.3 m climbed, off at the top.
        var ladder = b.Train.Frames[2].Shape.Ladders[0];
        var climb = s with { Surface = Surface.Ladder, Position = ladder.Foot + new Double3(0, 0.05, 0) };
        for (int i = 0; i < 40; i++)
        {
            b.Step((1, climb));
            climb.Position += new Double3(0, P.LadderClimb * Dt, 0);
        }
        b.Step((1, s));
        Assert.Equal(1, b.Count("crew-ladder.grab"));
        Assert.InRange(b.Count("crew-ladder.rung-up"), (int)(39 * P.LadderClimb * Dt / 0.3) - 1, (int)(39 * P.LadderClimb * Dt / 0.3) + 1);
        Assert.Equal(1, b.Count("crew-ladder.let-go"));

        // Hit, then dead on the roof; a tool changed in hand.
        b.Step((1, s with { Health = s.Health - 20 }));
        Assert.Equal(1, b.Count("crew-hurt.hit"));
        var wrench = s with { Health = s.Health - 20, Kit = Kit.With(s.Kit, 1, Tool.Wrench), HeldSlot = 1 };
        b.Step((1, wrench));
        Assert.Equal(1, b.Count("crew-melee.crowbar-stow"));
        Assert.Equal(1, b.Count("crew-melee.wrench-equip"));

        // The coupling behind car 2 cut, then made again.
        Assert.True(b.Train.Uncouple(2));
        b.Step();
        Assert.Equal(1, b.Count("crew-coupling.knuckle-release"));
        Assert.Equal(1, b.Count("crew-coupling.hose-part"));
        Assert.Equal(0, b.Count("crew-coupling.knuckle-close"));
    }
}
