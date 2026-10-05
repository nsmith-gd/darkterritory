using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
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
            : this(new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000), C), cues)
        {
        }

        public Bench(World world, params string[] cues)
        {
            World = world;
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
            World.StepBodies(crew);
            Audio.CrewStates = crew;
            Audio.Update(World, World.Controls, Listener.At(Train.Frames[1].Origin + Double3.Up * 2, 0), exposed: true, Dt);
            // The crew's (and the old gunshot), not the train's bed going on underneath.
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && (v.Name.StartsWith("crew-") || v.Name == "gunshot"))
                    Heard.Add((v.Name, v.Position, Tick));
            Tick++;
        }

        public int Count(string name) => Heard.Count(h => h.Name == name);

        /// <summary>One tick with the train driven by these controls, heard from car 2's roof.</summary>
        public void Drive(TrainControls controls)
        {
            World.BeginTick();
            World.Step(controls);
            Audio.Update(World, controls, Listener.At(Train.Frames[2].Origin + Double3.Up * 3, 0), exposed: true, Dt);
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && v.Name.StartsWith("crew-"))
                    Heard.Add((v.Name, v.Position, Tick));
            Tick++;
        }
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
    public void ACrateSlidingAlongTheFloorScrapesUntilItStops()
    {
        var b = new Bench();
        b.World.EnableBodies();   // the host's world: it moves loose things
        var room = b.Train.Frames[1].Shape.Interior!.Value;
        var crate = b.World.Bodies.SpawnCrate(b.Train, 1, new Double3(0, room.Min.Y, 0));
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Step();
        Assert.Equal(0, b.Count("crew-carry.crate-drag.wood"));

        // Shoved along the boards for half a second (as a hard brake wakes it and throws it), then left to slide to a stop.
        crate.Pbd.Wake();
        var before = crate.Centre;
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
        {
            for (int p = 0; p < crate.Pbd.Particles.Length; p++)
                crate.Pbd.Particles[p].SetVelocity(new Double3(0, 0, 1.5), Dt);
            b.Step();
        }
        Assert.True(crate.Centre.Z - before.Z > 0.3);
        Assert.True(b.Count("crew-carry.crate-drag.wood") > 0);
        Assert.Contains(b.Audio.Mixer.Voices, v => v.Name == "crew-carry.crate-drag.wood" && !v.Stopped);
        for (int i = 0; i < SimConstants.TickRate * 2; i++)
            b.Step();
        Assert.DoesNotContain(b.Audio.Mixer.Voices, v => v.Name == "crew-carry.crate-drag.wood" && !v.Stopped);
    }

    [Fact]
    public void ACrewmateKilledWithAToolInHandDropsItABeatAfterTheBody()
    {
        var b = new Bench("crew-hurt.body-fall.roof", "crew-melee.wrench-drop.roof");
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P) with { Kit = Kit.Of([Tool.Wrench]), HeldSlot = 0 };
        b.Step((1, s));
        b.Step((1, s));
        var dead = s with { Health = 0, Death = DeathCause.Mauled };
        b.Step((1, dead));
        Assert.Equal(1, b.Count("crew-hurt.body-fall.roof"));
        Assert.Equal(0, b.Count("crew-melee.wrench-drop.roof"));
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            b.Step((1, dead));
        Assert.Equal(1, b.Count("crew-melee.wrench-drop.roof"));
        Assert.Equal(1, b.Count("crew-hurt.body-fall.roof"));
    }

    [Fact]
    public void ATunnelsMouthBonksWhoeverStoodOnTheRoofAndTheBodyTumblesOffAfter()
    {
        var b = new Bench("crew-mishaps.tunnel-bonk", "crew-mishaps.tunnel-tumble", "crew-hurt.body-fall.roof");
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        b.Step((1, s));
        b.Step((1, s with { Health = 0, Death = DeathCause.Struck }));
        Assert.Equal(1, b.Count("crew-mishaps.tunnel-bonk"));
        Assert.Equal(0, b.Count("crew-hurt.body-fall.roof"));   // the tumble is the body's fall
        var bonk = b.Heard.Single(h => h.Name == "crew-mishaps.tunnel-bonk");
        Assert.True(bonk.At.Y > PlayerMotor.WorldPosition(s, b.Train).Y + 1);   // at head height
        Assert.Equal(0, b.Count("crew-mishaps.tunnel-tumble"));
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            b.Step((1, s with { Health = 0, Death = DeathCause.Struck }));
        Assert.Equal(1, b.Count("crew-mishaps.tunnel-tumble"));
        Assert.Equal(1, b.Count("crew-mishaps.tunnel-bonk"));
    }

    [Fact]
    public void ACastingLetGoOnSomeoneBongsAndTheCranesChainRattlesAfter()
    {
        var b = new Bench("crew-mishaps.crushed", "crew-mishaps.crane-chain", "crew-hurt.body-fall.ground");
        var line = b.Train.Line.Sample(4_000);
        var s = new PlayerState { Parent = PlayerState.World, Position = line.Position, Surface = Surface.Ground, Health = P.Health };
        b.Step((1, s));
        b.Step((1, s with { Health = 0, Death = DeathCause.Crushed }));
        Assert.Equal(1, b.Count("crew-mishaps.crushed"));
        Assert.Equal(0, b.Count("crew-hurt.body-fall.ground"));
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Step((1, s with { Health = 0, Death = DeathCause.Crushed }));
        Assert.Equal(1, b.Count("crew-mishaps.crane-chain"));
    }

    [Fact]
    public void ThrownOffARoofTheyFlailAndAJumpIsNoThrow()
    {
        var b = new Bench("crew-mishaps.thrown-flail");
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        b.Step((1, s));
        // Over the side (Lineside's throw: PullOff, alive at this speed): the flailing, once.
        var thrown = s;
        PlayerMotor.PullOff(ref thrown, b.Train, b.Train.Frames[2].DirToWorld(new Double3(1, 0, 0)) * 3, T, DeathCause.Thrown);
        Assert.True(thrown.Alive);
        b.Step((1, thrown));
        b.Step((1, thrown));
        Assert.Equal(1, b.Count("crew-mishaps.thrown-flail"));
        // A jump up off a roof is nobody's throw.
        var c = new Bench("crew-mishaps.thrown-flail");
        var r = PlayerMotor.SpawnOnRoof(c.Train, 2, 0, P);
        c.Step((1, r));
        c.Step((1, r with { Surface = Surface.Air, Velocity = new Double3(0, 3, 0) }));
        Assert.Equal(0, c.Count("crew-mishaps.thrown-flail"));
    }

    [Fact]
    public void EmptyHandsSwingASleeve()
    {
        var b = new Bench("crew-mishaps.bare-swing", "crew-melee.crowbar-swing");
        b.Audio.OwnId = 1;
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P) with { Kit = Kit.Of([Tool.Crowbar]), HeldSlot = 3 };
        Assert.Equal(Tool.None, Kit.Held(s));
        b.Audio.OwnIntent = new PlayerIntent { Actions = PlayerActions.Swing };
        b.Step((1, s));
        b.Step((1, s));
        Assert.Equal(1, b.Count("crew-mishaps.bare-swing"));
        Assert.Equal(0, b.Count("crew-melee.crowbar-swing"));
        // With the crowbar in hand it's the crowbar's.
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Step((1, s with { HeldSlot = 0 }));
        Assert.True(b.Count("crew-melee.crowbar-swing") > 0);
        Assert.Equal(1, b.Count("crew-mishaps.bare-swing"));
    }

    [Fact]
    public void ACrewmatesSwingIsHeardInTheirHandsLandedOrNot()
    {
        // Note 197: the host logs every swing (World.Swings), so a crewmate's is heard on every machine, once, with the
        // tool in their hands; your own is heard from your intent, so the log doesn't play it twice.
        var b = new Bench("crew-mishaps.bare-swing", "crew-melee.crowbar-swing");
        b.Audio.OwnId = 1;
        var you = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        var mate = PlayerMotor.SpawnOnRoof(b.Train, 2, 2, P) with { Kit = Kit.Of([Tool.Crowbar]), HeldSlot = 0 };
        // Already in the log when this machine first looks: old news.
        b.World.Swings.Add(new SwingEvent(1, b.World.Tick, 2));
        b.Step((1, you), (2, mate));
        Assert.Empty(b.Heard);
        b.World.Swings.Add(new SwingEvent(2, b.World.Tick, 2));
        b.World.Swings.Add(new SwingEvent(3, b.World.Tick, 1));
        for (int i = 0; i < 5; i++)
            b.Step((1, you), (2, mate));
        var swing = Assert.Single(b.Heard, h => h.Name == "crew-melee.crowbar-swing");
        Assert.True((swing.At - b.Train.Frames[2].ToWorld(mate.Position)).Length < 2.5);
        Assert.Equal(0, b.Count("crew-mishaps.bare-swing"));
        // Empty-handed, it's a sleeve.
        b.World.Swings.Add(new SwingEvent(4, b.World.Tick, 2));
        b.Step((1, you), (2, mate with { HeldSlot = 3 }));
        Assert.Equal(1, b.Count("crew-mishaps.bare-swing"));
        Assert.Equal(1, b.Count("crew-melee.crowbar-swing"));
    }

    [Fact]
    public void ThePoweredThrowersLeverGoesOverInTheCabNotAtTheStand()
    {
        // Spec F.3's powered switch thrower (note 196): the driver throws the points ahead from a lever in the cab. Taking
        // hold of it and its going over are heard there, where they are; the points still move at the points.
        var fitted = T with { Composition = T.Composition with { SwitchThrower = true } };
        var junctions = RouteTuning.Load(Content).Junctions;
        const double Toe = 3_000;
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(8_000)]),
            [new BranchDefinition(BranchKind.DeadLine, Toe, +1,
                [new TrackSegment(junctions.DivergeLength, -junctions.DivergeRadius), new TrackSegment(junctions.DivergeLength, junctions.DivergeRadius), new TrackSegment(500)])]);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(fitted, 4, 1)), line, Toe - 150), C);
        world.EnableBodies();
        world.EnableSwitches(junctions);
        world.SetSwitch(0, true);
        var b = new Bench(world, "crew-switch.lever-unlatch", "crew-switch.lever-throw", "crew-switch.points-move", "crew-switch.lever-latch");
        b.Audio.OwnId = 1;
        var leverAt = b.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Points).Position;
        var s = PlayerMotor.SpawnInCab(b.Train, P);
        s.Position = s.Position with { X = Math.Clamp(leverAt.X, -1.05, 1.05), Z = leverAt.Z };
        Assert.True(SwitchStands.AtThrower(s, b.Train));
        var cab = b.Train.Frames[0].ToWorld(leverAt);
        b.Step((1, s));
        b.Audio.OwnIntent = new PlayerIntent { Buttons = PlayerButtons.Use };
        b.Step((1, s));
        var unlatch = Assert.Single(b.Heard, h => h.Name == "crew-switch.lever-unlatch");
        Assert.True((unlatch.At - cab).Length < 0.01);
        // The host throws it (replicated as the switch's setting): the cab's lever over and latched, the points at the toe.
        world.SetSwitch(0, false);
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Step((1, s));
        var thrown = Assert.Single(b.Heard, h => h.Name == "crew-switch.lever-throw");
        Assert.True((thrown.At - cab).Length < 0.01);
        Assert.True((Assert.Single(b.Heard, h => h.Name == "crew-switch.lever-latch").At - cab).Length < 0.01);
        Assert.True((Assert.Single(b.Heard, h => h.Name == "crew-switch.points-move").At - line.Sample(Toe).Position).Length < 0.01);
    }

    sealed class Windy(double wind) : ITrackConditions
    {
        public double Ground(Double3 world) => 0;
        public double Adhesion(int path, double distance) => 1;
        public double Drag(int path, double distance, double speed) => 0;
        public int ColdStep(int path, double distance) => 0;
        public double Wind(int path, double distance) => wind;
    }

    [Fact]
    public void OnARoofTheGustPushingYouIsHeardOnTheSideItBlowsFrom()
    {
        // Note 201's wind on a roof's footing (note 239): the gust that pushes you is a gale's roar off the side it comes
        // from, as loud as it pushes, so you hear it build before it walks you to the edge.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)])) { Conditions = new Windy(1) };
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
        world.Train.Dynamics.Velocity = T.MaxSpeed;
        var b = new Bench(world, "crew-footsteps.walk.roof");
        b.Audio.OwnId = 1;
        b.Audio.OwnIntent = new PlayerIntent { Buttons = PlayerButtons.Run };
        var roof = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        // Two places along the line with strong gusts from opposite sides.
        double From(double sign) => Enumerable.Range(0, 400).Select(i => 5_000.0 + i * 7).First(x => sign * PlayerMotor.Gust(x, P.Wind.GustMetres) > 0.8);
        SoundInstance? Gale() => b.Audio.Mixer.Voices.SingleOrDefault(v => v.Name == "world-wind.gale" && !v.Finished);
        foreach (double sign in new[] { 1.0, -1.0 })
        {
            var s = roof with { LineHint = From(sign) };
            for (int i = 0; i < 3; i++)
                b.Step((1, s));
            double push = PlayerMotor.WindPush(s, b.Audio.OwnIntent, b.Train, P, T);
            Assert.Equal(Math.Sign(sign), Math.Sign(push));
            var gale = Gale();
            Assert.NotNull(gale);
            // Off the car's left for a push to its right, and the other way round.
            var frame = b.Train.Frames[2];
            double side = Double3.Dot(gale!.Position - frame.ToWorld(s.Position), frame.Right);
            Assert.True(side * push < 0, $"gale {side:0.0} m across, push {push:0.00}");
            Assert.InRange(gale.Volume, 0.5f, 1f);
        }
        // Down off the roof, in the car: no push, no gale.
        var inside = PlayerMotor.SpawnInCab(b.Train, P);
        for (int i = 0; i < 3; i++)
            b.Step((1, inside));
        Assert.Null(Gale());
    }

    [Fact]
    public void ARadioMendedWithTheKitIsHeardComingBack()
    {
        // Note 201's mending (note 239): the kit opened as the hands go to work, its ratchet while they stay at it, and the
        // set's squelch with the kit shut once the radio's whole.
        var b = new Bench("crew-repair.kit-open", "crew-repair.ratchet", "crew-repair.done", "voice-radio-sfx.squelch");
        b.Audio.Bank.Add("crew-repair.ratchet", new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Loop: true, MaxInstances: 64));
        b.Audio.Bank.Add("voice-radio-sfx.squelch", new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
        b.World.EnableBodies();
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        var radio = b.World.Bodies.SpawnCrate(b.Train, 2, new Double3(0.5, b.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Radio);
        radio.Broken = true;
        b.Step((1, s));
        for (int i = 1; i <= 20; i++)
        {
            radio.MendTicks = i;
            b.Step((1, s));
        }
        Assert.Equal(1, b.Count("crew-repair.kit-open"));
        Assert.Contains(b.Audio.Mixer.Voices, v => v.Name == "crew-repair.ratchet" && !v.Finished);
        Assert.Equal(0, b.Count("crew-repair.done"));
        radio.Broken = false;
        radio.MendTicks = 0;
        for (int i = 0; i < 5; i++)
            b.Step((1, s));
        Assert.Equal(1, b.Count("crew-repair.done"));
        Assert.True(b.Heard.Any(h => h.Name == "voice-radio-sfx.squelch") || b.Audio.Mixer.Voices.Any(v => v.Name == "voice-radio-sfx.squelch"));
        Assert.DoesNotContain(b.Audio.Mixer.Voices, v => v.Name == "crew-repair.ratchet" && !v.Finished);
    }

    [Fact]
    public void AnExtinguisherRunDryGivesUpItsDregs()
    {
        var b = new Bench("crew-extinguisher.run-dry", "crew-extinguisher.spray", "crew-mishaps.extinguisher-dregs");
        b.World.EnableBodies();
        var s = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        var ext = b.World.Bodies.SpawnCrate(b.Train, 2, new Double3(0, b.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Extinguisher);
        ext.Carrier = 1;
        b.Step((1, s));
        // Spraying down to nothing: the run-dry, then a moment later the last dregs.
        for (int i = 0; i < 6; i++)
        {
            ext.Charge = Math.Max(0, 0.5 - 0.1 * i);
            b.Step((1, s));
        }
        ext.Charge = 0;
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Step((1, s));
        Assert.Equal(1, b.Count("crew-extinguisher.run-dry"));
        Assert.Equal(1, b.Count("crew-mishaps.extinguisher-dregs"));
    }

    [Fact]
    public void LivestockAreStartledByAHardJoltAndNotAgainStraightAway()
    {
        var b = new Bench("crew-mishaps.startle-cattle", "crew-mishaps.startle-pigs", "crew-mishaps.startle-sheep");
        b.Train.Vehicles[2].Cargo = CargoKind.Livestock;
        b.Train.Dynamics.Velocity = 15;
        var coast = new TrainControls { Reverser = 1 };
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Drive(coast);
        int Startled() => b.Heard.Count(h => h.Name.StartsWith("crew-mishaps.startle-"));
        Assert.Equal(0, Startled());
        // The brakes slammed on: one startle from the car, a moment after the jolt.
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Drive(coast with { Brake = 1 });
        Assert.Equal(1, Startled());
        // Off and on again straight away: they're still put out, not startled afresh.
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Drive(coast);
        for (int i = 0; i < SimConstants.TickRate; i++)
            b.Drive(coast with { Brake = 1 });
        Assert.Equal(1, Startled());
    }

    [Fact]
    public void OnLowSteamTheWhistleOnlyWheezes()
    {
        var boiler = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000, boiler), C);
        var audio = new GameAudio(Content);
        audio.Bank.Add("crew-mishaps.whistle-wheeze", new SoundDef(1, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 400)], Duration: 0.5));
        bool Blowing(string name) => audio.Mixer.Voices.Any(v => v.Name == name && !v.Finished);
        void Blow()
        {
            w.Whistled(1);
            for (int i = 0; i < 3; i++)
            {
                w.BeginTick();
                w.Step(w.Controls);
                audio.Update(w, w.Controls, Listener.At(w.Train.Frames[0].Origin, 0), exposed: true, Dt);
            }
        }
        // A full head of steam: the whistle.
        w.Train.Boiler.Pressure = boiler.WorkingBandMax;
        Blow();
        Assert.True(Blowing("train-whistle"));
        Assert.False(Blowing("crew-mishaps.whistle-wheeze"));
        // Run down below half the working band: it barely speaks.
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.Step(w.Controls);
            audio.Update(w, w.Controls, Listener.At(w.Train.Frames[0].Origin, 0), exposed: true, Dt);
        }
        w.Train.Boiler.Pressure = boiler.WorkingBandMin * 0.3;
        Blow();
        Assert.True(Blowing("crew-mishaps.whistle-wheeze"));
    }

    [Fact]
    public void AHardLandingOffTheTrainScattersWhatWasInHand()
    {
        var b = new Bench("crew-jump-off.impact.ground", "crew-mishaps.pocket-scatter");
        var at = b.Train.Line.Sample(4_000).Position;
        var ground = PlayerMotor.SpawnOnGround(at, b.Train.Line, 4_000, P);
        var flying = ground with { Surface = Surface.Air, Velocity = new Double3(8, -1, 0), Position = ground.Position + new Double3(0, 0.3, 0) };
        b.Step((1, flying));
        b.Step((1, flying));
        b.Step((1, ground));
        Assert.Equal(1, b.Count("crew-jump-off.impact.ground"));
        Assert.Equal(0, b.Count("crew-mishaps.pocket-scatter"));
        for (int i = 0; i < SimConstants.TickRate / 2; i++)
            b.Step((1, ground));
        Assert.Equal(1, b.Count("crew-mishaps.pocket-scatter"));
    }

    [Fact]
    public void ABodyCarriedThroughADoorKnocksTheFrameAndSetDownItsBootFollows()
    {
        var b = new Bench("crew-carry.body-lift", "crew-carry.body-set.wood", "crew-mishaps.body-boot.wood", "crew-mishaps.body-knock");
        b.World.EnableBodies();
        var room = b.Train.Frames[1].Shape.Interior!.Value;
        var roof = PlayerMotor.SpawnOnRoof(b.Train, 1, 0, P);
        var inside = new PlayerState
        {
            Parent = 1,
            Position = new Double3(T.Geometry.Interior!.DoorX, room.Min.Y, 0),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = b.Train.Cars[1].FrontDistance,
        };
        var child = b.World.Bodies.SpawnCrate(b.Train, 1, new Double3(0, room.Min.Y, 1), Sim.Physics.BodyKind.Child);
        for (int i = 0; i < 10; i++)
            b.Step((1, roof));
        // Picked up on the roof and carried down inside: its boots knock the frame on the way in, once.
        child.Carrier = 1;
        for (int i = 0; i < 5; i++)
            b.Step((1, roof));
        Assert.Equal(0, b.Count("crew-mishaps.body-knock"));
        for (int i = 0; i < 5; i++)
            b.Step((1, inside));
        Assert.Equal(1, b.Count("crew-mishaps.body-knock"));
        // Set down on the boards: the body, then a boot a beat after.
        child.Carrier = -1;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            b.Step((1, inside));
        Assert.Equal(1, b.Count("crew-carry.body-set.wood"));
        Assert.Equal(1, b.Count("crew-mishaps.body-boot.wood"));
        Assert.True(b.Heard.Single(h => h.Name == "crew-mishaps.body-boot.wood").Tick > b.Heard.Single(h => h.Name == "crew-carry.body-set.wood").Tick);
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
    public void LayingTheSeatedGunRunsItsSteamMotorWhileItTurnsAndClunksAsItStops()
    {
        // T112: the seated gunner lays it (Traverse and Elevation on the replicated gun); spec C.2: the turrets run on steam.
        var b = new Bench("crew-cannon-fire.traverse-stop");
        var gun = b.Train.Vehicles.Last(v => v.HasGun);
        bool Laying() => b.Audio.Mixer.Voices.Any(v => v.Name == "gun-lay" && !v.Finished);
        b.Step();
        Assert.False(Laying());
        for (int i = 0; i < 10; i++)
        {
            gun.Gun.Traverse += 0.02;
            b.Step();
        }
        Assert.True(Laying());
        // At its rate: faster turning, a faster gear.
        var lay = b.Audio.Mixer.Voices.First(v => v.Name == "gun-lay" && !v.Finished);
        Assert.InRange(lay.Params.Get("speed"), 0.3, 1);
        // Still: it stops, once, with the gear's clunk.
        for (int i = 0; i < 10; i++)
            b.Step();
        Assert.False(Laying());
        Assert.Equal(1, b.Count("crew-cannon-fire.traverse-stop"));
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
