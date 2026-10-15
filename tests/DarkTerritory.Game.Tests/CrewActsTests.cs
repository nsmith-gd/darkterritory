using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// X1 (note 145): a crewmate's act read off their replicated state picks their clip, and every act has one: the crew's
/// actions are crew_clips.glb's, merged into crew.glb on load.
/// </summary>
public class CrewActsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    static World World(double at = 5_000)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, at), C);
    }

    [Fact]
    public void EveryActHasACrewClip()
    {
        var crew = new CreatureArt(Look.Load(Content), Content).Get("crew");
        Assert.NotNull(crew);
        foreach (var pose in Enum.GetValues<CrewPose>())
        {
            // The emotes (note 298) wait on their clips (the art checklist's crew-emotes row): until then EmoteArms poses
            // them over the walk and the idle, so a missing one is a stand-in, not a gap. A clip of theirs is used as it lands.
            // (Note 722's emotes for two have theirs: note 807; note 714's bow, salute and conga are by hand.)
            if (pose is CrewPose.Dance or CrewPose.Wave or CrewPose.Point
                or CrewPose.Bow or CrewPose.Salute or CrewPose.Conga && !crew!.Clips.ContainsKey(CreatureArt.ClipOf(pose)))
                continue;
            string clip = CreatureArt.ClipOf(pose);
            Assert.True(pose == CrewPose.Idle || clip != "idle", $"{pose} has no clip of its own");
            Assert.True(crew!.Clip(clip) is not null, $"{pose}: the crew has no '{clip}' clip");
        }
    }

    /// <summary>
    /// Note 812: a friend over the shoulder is drawn as one with whoever carries them: in crew_clips' carried, posed in the
    /// carrier's own space, so at the carrier's feet, facing their way, on their clock (their beat less this one's).
    /// </summary>
    [Fact]
    public void AFriendOverTheShoulderIsDrawnAtTheCarriersFeetOnTheirClock()
    {
        var w = World();
        var carrier = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P) with { Yaw = 0.4, Flags = PlayerFlags.Carrying | PlayerFlags.Heavy };
        var carried = carrier with { Position = Sim.Player.Rescue.Shoulder(carrier, P.Rescue), Flags = PlayerFlags.Carried, Yaw = 2.0 };
        List<(int, PlayerState)> crew = [(3, carrier), (6, carried)];
        Assert.Equal(CrewPose.Fireman, CrewActs.Of(carrier, 3, w));
        var drawn = CrewActs.Crewmate(6, carried, w, w.Train.Frames, [carrier, carried], crew);
        var (feet, yaw) = Eyes.World(carrier, w.Train.Frames);
        Assert.Equal(CrewPose.Carried, drawn.Act);
        Assert.True((drawn.Feet - feet).Length < 1e-9, $"{drawn.Feet} vs {feet}");
        Assert.Equal(yaw, drawn.Yaw, 9);
        Assert.Equal(CreatureArt.BeatOffset(w.OutfitOf(3)) - CreatureArt.BeatOffset(drawn.Variant), drawn.Phase, 9);
    }

    /// <summary>
    /// Note 812: drawn together, stood and walking, the friend's hips stay on the carrier's left shoulder all through the clip
    /// (its joint within a hand's breadth or so), their feet hang well clear of the floor, and their head hangs down behind.
    /// </summary>
    [Fact]
    public void TheCarriedStayOnTheCarriersShoulderThroughTheirSteps()
    {
        var art = new CreatureArt(Look.Load(Content), Content);
        var mesh = new Ballast.Render.MeshBuilder();
        var model = System.Numerics.Matrix4x4.Identity;
        foreach (var (carrying, carried) in new[] { (CrewPose.Fireman, CrewPose.Carried), (CrewPose.FiremanWalk, CrewPose.CarriedWalk) })
            for (double t = 0; t < 2.4; t += 0.15)
            {
                Assert.True(art.Crewmate(mesh, model, carrying, t, 3));
                var shoulder = art.Joint("upperarm_l", model);
                var back = art.Joint("spine_03", model);
                Assert.True(art.Crewmate(mesh, model, carried, t + CreatureArt.BeatOffset(3) - CreatureArt.BeatOffset(5), 5));
                var hips = art.Joint("pelvis", model);
                Assert.True(System.Numerics.Vector3.Distance(hips, shoulder) < 0.3f, $"{carried} {t:0.00}: hips {System.Numerics.Vector3.Distance(hips, shoulder):0.00} m off the shoulder");
                Assert.True(art.Joint("foot_l", model).Y > 0.3f && art.Joint("foot_r", model).Y > 0.3f, $"{carried} {t:0.00}: feet down");
                var head = art.Joint("head", model);
                Assert.True(head.Y < shoulder.Y && head.Z > back.Z, $"{carried} {t:0.00}: head at {head}, not down behind");
            }
    }

    /// <summary>
    /// Note 813: a friend on a crane's hook is drawn stood on its lip behind the cable in on_hook, the hands on the cable; and the
    /// loaded hook's sway, which the hook, its cable, its lamp and its riders all take on the scene's one clock, is a little one
    /// (a tenth of a metre or so), level, and the same every time for the same time.
    /// </summary>
    [Fact]
    public void AFriendOnTheHookStandsOnItAndSwaysWithIt()
    {
        var hook = new Double3(10, 3, -4);
        var rider = CrewActs.OnTheHook(new Crewmate(3, hook, 0.3, true), hook);
        Assert.Equal(CrewPose.OnHook, rider.Act);
        Assert.NotNull(rider.Reach);
        Assert.InRange(((rider.Feet - hook) with { Y = 0 }).Length, 0.15, 0.25);
        for (double t = 0; t < 10; t += 0.37)
        {
            var sway = CrewActs.HookSway(t);
            Assert.Equal(0, sway.Y);
            Assert.InRange(sway.Length, 0, 0.11);
            Assert.Equal(sway, CrewActs.HookSway(t));
        }
        Assert.True(CrewActs.HookSway(0.9).Length > 0.03, "it does swing");
    }

    /// <summary>
    /// Note 818: whoever's by the handcar while it's coming off the van (or going back on) is drawn heaving it: turned to the
    /// trolley where the scene draws it part-way through the lift, both hands under the edge of its deck, posed by how high
    /// that is. Not before it moves, and not someone stood on it.
    /// </summary>
    [Fact]
    public void BesideTheHandcarAsItsLiftedTheyHeaveItWithTheirHandsUnderIt()
    {
        var t = T with { Handcar = T.Handcar with { Always = true } };
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000), C);
        var h = Assert.IsType<Sim.Train.Handcar>(w.Train.Handcar);
        var hung = w.Train.Frames[h.Id];
        var stand = hung.Origin + hung.Right * 1.2;
        var lifter = new PlayerState { Position = stand with { Y = 0 }, Parent = PlayerState.World, Surface = Surface.Ground, Health = 100 };
        Assert.True(lifter.Alive);
        Assert.True(w.CanLiftHandcar(lifter));
        Assert.Null(CrewActs.Of(lifter, 1, w));
        foreach (double lift in (ReadOnlySpan<double>)[0.1, 0.5, 0.9])
        {
            h.Lift = lift;
            Assert.Equal(CrewPose.Heave, CrewActs.Of(lifter, 1, w));
            var drawn = CrewActs.Crewmate(1, lifter, w, w.Train.Frames);
            Assert.Equal(CrewPose.Heave, drawn.Act);
            Assert.InRange(drawn.Phase, 0, CrewActs.HeaveSeconds);
            var car = GreyboxScene.Lifted(hung, w.Train.HandcarLiftedTo()!.Value, lift);
            var reach = Assert.NotNull(drawn.Reach);
            var (a, b) = (World(drawn, reach.A), World(drawn, reach.B));
            foreach (var hand in (ReadOnlySpan<Double3>)[a, b])
            {
                // On the edge of its deck, half way up it: on a side or an end.
                var local = car.ToLocal(hand);
                Assert.Equal(t.Handcar.DeckHeight * 0.5, local.Y, 6);
                Assert.True(Math.Abs(Math.Abs(local.X) - t.Handcar.Width / 2) < 1e-6 || Math.Abs(Math.Abs(local.Z) - t.Handcar.Length / 2) < 1e-6,
                    $"at the edge ({local.X:0.00}, {local.Z:0.00})");
            }
            // Facing it, the hands either side of straight ahead.
            Assert.True(reach.A.Z < 0 && reach.B.Z < 0, "the hands are ahead of them");
            Assert.Equal(CrewActs.HeavePhase((a.Y + b.Y) / 2 - lifter.Position.Y), drawn.Phase, 6);
        }
        // Stood on it, they ride it; nobody heaves a trolley they're on.
        Assert.NotEqual(CrewPose.Heave, CrewActs.Of(lifter with { Parent = h.Id, Position = default, Surface = Surface.Deck }, 1, w));
        h.Lift = 0;
        Assert.Null(CrewActs.Of(lifter, 1, w));
        // The clip's squatted under it low and stood up under it high, and no further either way.
        Assert.Equal(0, CrewActs.HeavePhase(0.1));
        Assert.Equal(0, CrewActs.HeavePhase(CrewActs.HeaveLow));
        Assert.Equal(CrewActs.HeaveSeconds, CrewActs.HeavePhase(CrewActs.HeaveHigh));
        Assert.Equal(CrewActs.HeaveSeconds, CrewActs.HeavePhase(3));
        Assert.True(CrewActs.HeavePhase(1.0) < CrewActs.HeavePhase(1.5));
    }

    /// <summary>A crewmate's hand from their own frame (from the feet, as they face) into the world.</summary>
    static Double3 World(Crewmate c, Double3 local) =>
        c.Feet + new Double3(Math.Cos(c.Yaw), 0, -Math.Sin(c.Yaw)) * local.X + Double3.Up * local.Y + new Double3(Math.Sin(c.Yaw), 0, Math.Cos(c.Yaw)) * local.Z;

    /// <summary>
    /// Note 818: a hand-up's two hands meet: the helper's held down out of the car's door and the runner's raised to it, a
    /// hand's thickness apart, each within an arm's reach; the helper's near where hand_up has it anyway.
    /// </summary>
    [Fact]
    public void AHandUpsTwoHandsMeet()
    {
        var w = World();
        var staged = Staging.Rescue(w.Train, Content, "handup");
        var (helper, runner) = (staged[0], staged[1]);
        Assert.Equal(CrewPose.HandUp, helper.Act);
        var (down, up) = (World(helper, helper.Hand), World(runner, runner.Hand));
        Assert.InRange((down - up).Length, 0.05, 0.1);
        Assert.True(down.Y > up.Y, "the helper's hand over the runner's");
        // The runner's raised from their shoulder, within an arm's reach (and the hand to the helper's side of them).
        var side = Math.Sign(runner.Hand.X);
        var shoulder = World(runner, new Double3(side * Arms.ShoulderWidth, Arms.ShoulderHeight, 0));
        Assert.InRange((up - shoulder).Length, 0, Arms.Upper + Arms.Lower);
        Assert.True(((helper.Feet - runner.Feet) with { Y = 0 }).Length > ((helper.Feet - up) with { Y = 0 }).Length, "reached toward the helper");
        // The helper's within a little of the clip's own reach down.
        var clipHand = World(helper, new Double3(0.23, 0.75, -0.85));
        Assert.InRange((down - clipHand).Length, 0, 0.45);
    }

    /// <summary>
    /// Note 814: a pumper's pump is played by the handle's swing, not a clock: at their own end's top (the front end's at a swing
    /// of 0, the back end's half a swing on) the clip's at its start, at their end's bottom half way through, and once round it's
    /// back where it began; the two ends are always half a pump apart.
    /// </summary>
    [Fact]
    public void ThePumpIsPlayedByTheHandlesSwing()
    {
        Assert.Equal(0, CrewActs.PumpPhase(0, -1), 9);
        Assert.Equal(CrewActs.PumpSeconds / 2, CrewActs.PumpPhase(Math.PI, -1), 9);
        Assert.Equal(CrewActs.PumpSeconds / 2, CrewActs.PumpPhase(0, 1), 9);
        for (double swing = -7; swing < 14; swing += 0.7)
        {
            Assert.InRange(CrewActs.PumpPhase(swing, -1), 0, CrewActs.PumpSeconds);
            double apart = Math.Abs(CrewActs.PumpPhase(swing, -1) - CrewActs.PumpPhase(swing, 1));
            Assert.Equal(CrewActs.PumpSeconds / 2, apart, 6);
            // Its end at the top (EndHeight 1) at the clip's start, at the bottom half way: the clip's own swing.
            double turn = CrewActs.PumpPhase(swing, -1) / CrewActs.PumpSeconds * 2 * Math.PI;
            Assert.Equal(Sim.Train.Handcar.EndHeight(swing, -1), Math.Cos(turn), 6);
        }
    }

    [Fact]
    public void TheActIsReadOffTheirState()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        Assert.Null(CrewActs.Of(s, 1, w));
        Assert.Equal(CrewPose.Held, CrewActs.Of(s with { Flags = PlayerFlags.Held }, 1, w));
        Assert.Equal(CrewPose.Climb, CrewActs.Of(s with { Surface = Surface.Ladder }, 1, w));
        Assert.Equal(CrewPose.Push, CrewActs.Of(s with { Flags = PlayerFlags.Pushing }, 1, w));
        // Note 808: a crewmate who's shoved someone, from the host's knock for the shove's 0.45 s.
        w.Tick = 300;
        w.Horseplays.Add(new HorseplayEvent(1, 290, 1, KnockKind.Shove, 2));
        Assert.Equal(CrewPose.Shove, CrewActs.Of(s, 1, w));
        Assert.Null(CrewActs.Of(s, 2, w));
        w.Tick = 290 + (uint)Math.Ceiling(CrewActs.ShoveSeconds * SimConstants.TickRate);
        Assert.Null(CrewActs.Of(s, 1, w));
        w.Horseplays.Clear();
        // Note 818: a friend thrown off the shoulder is a throw of its own, as long as its drive up and the beat after.
        w.Tick = 300;
        w.Horseplays.Add(new HorseplayEvent(2, 290, 1, KnockKind.Throw, 2));
        Assert.Equal(CrewPose.ThrowOff, CrewActs.Of(s, 1, w));
        w.Tick = 290 + (uint)Math.Ceiling(CrewActs.ShoveSeconds * SimConstants.TickRate);
        Assert.Equal(CrewPose.ThrowOff, CrewActs.Of(s, 1, w));
        w.Tick = 290 + (uint)Math.Ceiling(CrewActs.ThrowSeconds * SimConstants.TickRate);
        Assert.Null(CrewActs.Of(s, 1, w));
        w.Horseplays.Clear();
        Assert.Equal(CrewPose.Lever, CrewActs.Of(s with { Flags = PlayerFlags.Operating }, 1, w));
        // Note 664: crouched on the key, down; a headset rider's body crouches with their head instead.
        Assert.Equal(CrewPose.Crouch, CrewActs.Of(s with { Flags = PlayerFlags.Crouched }, 1, w));
        Assert.Null(CrewActs.Of(s with { Flags = PlayerFlags.Crouched, Head = 1.0 }, 1, w));
        // In the air: going up off a jump, the leap (note 375); over the top, nothing (SceneArt holds the leap); coming down
        // hard, falling.
        Assert.Equal(CrewPose.Jump, CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, 3, 0) }, 1, w));
        Assert.Null(CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, -1, 0) }, 1, w));
        Assert.Equal(CrewPose.Fall, CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, -6, 0) }, 1, w));
        // Note 813: on a crane's hook, stood on it; off their feet by a shove or a throw, flailing all the way down.
        Assert.Equal(CrewPose.OnHook, CrewActs.Of(s with { Flags = PlayerFlags.Hoisted }, 1, w));
        // Note 814: at the handcar's handle, pumping it.
        Assert.Equal(CrewPose.Pump, CrewActs.Of(s with { Flags = PlayerFlags.Pumping }, 1, w));
        Assert.Equal(CrewPose.Flail, CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, -6, 0), Flags = PlayerFlags.Shoved }, 1, w));
        // Dead, they're drawn as their body.
        Assert.Null(CrewActs.Of(s with { Death = DeathCause.Struck, Flags = PlayerFlags.Held }, 1, w));
    }

    [Fact]
    public void TheLampTheExtinguisherTheGapAndAFriendHeld()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        // What's in their hands: the hand lamp held out, the extinguisher on the hip (L0a).
        var lamp = w.Bodies.SpawnCrate(w.Train, 2, new Double3(0, w.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Lamp);
        lamp.Carrier = 1;
        Assert.Equal(CrewPose.Lantern, CrewActs.Of(s, 1, w));
        lamp.Carrier = -1;
        var ext = w.Bodies.SpawnCrate(w.Train, 2, new Double3(0, w.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Extinguisher);
        ext.Carrier = 1;
        Assert.Equal(CrewPose.Extinguish, CrewActs.Of(s, 1, w));
        ext.Carrier = -1;
        // On the coupling plate, balancing over the gap; beside a friend something's holding, hauling at them.
        Assert.Equal(CrewPose.Gap, CrewActs.Of(s with { Surface = Surface.Coupler }, 1, w));
        var friend = s with { Position = s.Position + new Double3(0.8, 0, 0), Flags = PlayerFlags.Held };
        Assert.Equal(CrewPose.Haul, CrewActs.Of(s, 1, w, [s, friend]));
        Assert.Null(CrewActs.Of(s, 1, w, [s, friend with { Position = s.Position + new Double3(4, 0, 0) }]));
    }

    [Fact]
    public void AtTheControlsTheHandsAreOnTheLeversAndTheCordOnlyForTheRealWhistle()
    {
        // GDD §12: the whistle with no hand on it is the Whistler's tell (App. A.4), so the real one is seen pulled.
        var w = World();
        var levers = w.Train.Frames[0].Shape.Levers!.Value;
        var s = PlayerMotor.SpawnInCab(w.Train, P) with { Position = levers.Regulator with { X = levers.Regulator.X + 0.2, Y = 0, Z = levers.Regulator.Z + 0.5 } };
        s.Position = s.Position with { Y = PlayerMotor.SpawnInCab(w.Train, P).Position.Y };
        Assert.Equal(CrewPose.Drive, CrewActs.Of(s, 1, w));
        var c = CrewActs.Crewmate(1, s, w, w.Train.Frames);
        Assert.NotNull(c.Reach);
        // Each hand within an arm's reach of the shoulders: the levers are where they stand.
        foreach (var hand in new[] { c.Reach!.Value.A, c.Reach.Value.B })
            Assert.InRange((hand - new Double3(0, 1.45, 0)).Length, 0.1, 0.9);
        w.Whistled(1);
        Assert.Equal(CrewPose.Whistle, CrewActs.Of(s, 1, w));
        Assert.True(CrewActs.CrewWhistling(w));
    }

    [Fact]
    public void AFriendOverTheEdgeIsHauledUpAndALampHangsFromTheFist()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        var below = s with { Position = s.Position + new Double3(0.9, -0.8, 0), Flags = PlayerFlags.Held };
        Assert.Equal(CrewPose.HaulUp, CrewActs.Of(s, 1, w, [s, below]));
        var lamp = w.Bodies.SpawnCrate(w.Train, 2, new Double3(0, w.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Lamp);
        lamp.Carrier = 1;
        Assert.True(CrewActs.Crewmate(1, s, w, w.Train.Frames).Lamp);
        lamp.Carrier = -1;
        Assert.False(CrewActs.Crewmate(1, s, w, w.Train.Frames).Lamp);
    }

    [Fact]
    public void TheRescueIsMatchedToWhatHasThem()
    {
        // Note 378 (App. A.1's rescue; the checklist's crew-rescue): out of the Car Hugger's mouth, heaved; the Tippy
        // Toesie's fingers prised off; lifted by the Whistler or the Choir, hauled down; a Dragger's over the edge, hauled
        // up; the rest, down at the collar. (The clips are CreatureArtTests' crew's.)
        Assert.Equal(CrewPose.PullMouth, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.CarHugger), below: false));
        Assert.Equal(CrewPose.PryOff, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.TippyToesie), below: false));
        Assert.Equal(CrewPose.HaulDown, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.Whistler), below: false));
        Assert.Equal(CrewPose.HaulDown, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.Choir), below: false));
        Assert.Equal(CrewPose.HaulUp, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.Dragger), below: true));
        Assert.Equal(CrewPose.Haul, CrewActs.RescueOf(CrewActs.HeldPose(EnemyKind.Ribbit), below: false));
    }

    [Fact]
    public void AtTheGunTheyreSatOnItsSeat()
    {
        var w = World();
        int gun = Enumerable.Range(0, w.Train.Vehicles.Count).Last(i => w.Train.Vehicles[i].HasGun);
        var mount = Guns.Mount(w.Train, gun)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, gun, mount.Position.Z + 0.5, P);
        Assert.Equal(CrewPose.Gunner, CrewActs.Of(s, 1, w));
        var c = CrewActs.Crewmate(1, s, w, w.Train.Frames);
        Assert.Equal(CrewPose.Gunner, c.Act);
        // Feet on the roof under the seat, behind the pivot (the barrel's way the other way), facing the gun's way.
        var seat = CrewActs.Seated(s, mount);
        Assert.Equal(mount.Position.Y - 0.9, seat.Position.Y, 3);
        Assert.Equal(mount.Position.Z - mount.Facing.Z * TrainKit.CannonSeat.Z, seat.Position.Z, 3);
        Assert.Equal(PlayerMotor.WorldPosition(seat, w.Train).X, c.Feet.X, 3);
        Assert.Equal(mount.Facing.Z < 0 ? 0 : Math.PI, Math.Abs(seat.Yaw), 3);
        // The pan, the clip's sat height over those feet: the cannon's seat 0.42 under the pivot.
        Assert.Equal(mount.Position.Y + TrainKit.CannonSeat.Y, seat.Position.Y + CrewActs.GunnerPan, 3);
    }

    [Fact]
    public void TheExtinguisherIsSprayedBracedAndHungBackNotTakenDownAgain()
    {
        // App. C.5; SceneArt.Crewmate. Carried, it's on the hip; at work on a fire (GreyboxScene's Spraying), braced and
        // kicking. At the bracket the sim says TakeDown either way: come to it empty-handed it's lifted off; come to it
        // carrying, it's hung back, and that plays on through the drop rather than snapping to stood.
        var art = new SceneArt(Look.Load(Content));
        var stood = new Crewmate(1, new Double3(0, 0, -5_000), 0, true);
        void At(double t, CrewPose? act) => art.Crewmate(new Ballast.Render.MeshBuilder(), stood with { Act = act }, default, t);

        At(0.0, CrewPose.Extinguish);
        Assert.Equal(CrewPose.Extinguish, art.LastPose);
        art.Spraying = new HashSet<int> { 1 };
        At(0.1, CrewPose.Extinguish);
        Assert.Equal(CrewPose.Spray, art.LastPose);
        art.Spraying = null;

        // Back to its bracket carrying it: hung up, through the drop, then stood.
        At(0.2, CrewPose.Extinguish);
        At(0.3, CrewPose.TakeDown);
        Assert.Equal(CrewPose.HangUp, art.LastPose);
        At(0.8, CrewPose.TakeDown);
        Assert.Equal(CrewPose.HangUp, art.LastPose);
        At(1.0, null);
        Assert.Equal(CrewPose.HangUp, art.LastPose);
        At(1.8, null);
        Assert.Equal(CrewPose.Idle, art.LastPose);

        // Come to it with nothing: taken down.
        At(2.0, CrewPose.TakeDown);
        Assert.Equal(CrewPose.TakeDown, art.LastPose);
    }

    [Fact]
    public void TheLeapIsHeldOverTheTopAndACarStrainingOnABendHasThemStumbling()
    {
        // Note 375 (the checklist's crew-gap): a jump's leap is held over the top of it, where the sim says nothing, not
        // dropped for the walk; then they're stood. On a car straining past halfway to off (BendStrain), they stumble,
        // stood or crossing the gap, unless their hands are on something; on another car, or a car under the mark, they don't.
        var art = new SceneArt(Look.Load(Content));
        var stood = new Crewmate(1, new Double3(0, 0, -5_000), 0, true, Car: 1);
        void At(double t, CrewPose? act, Crewmate? c = null) => art.Crewmate(new Ballast.Render.MeshBuilder(), (c ?? stood) with { Act = act }, default, t);

        At(0.0, null);
        At(0.1, CrewPose.Jump);
        Assert.Equal(CrewPose.Jump, art.LastPose);
        At(0.5, null);
        Assert.Equal(CrewPose.Jump, art.LastPose);
        At(1.2, null);
        Assert.Equal(CrewPose.Idle, art.LastPose);

        art.BendStrain = [(0, 1), (0.8f, 1), (0.3f, 1)];
        At(2.0, null);
        Assert.Equal(CrewPose.Stumble, art.LastPose);
        At(2.1, CrewPose.Gap);
        Assert.Equal(CrewPose.Stumble, art.LastPose);
        At(2.2, CrewPose.Handbrake);
        Assert.Equal(CrewPose.Handbrake, art.LastPose);
        At(2.3, null, stood with { Car = 2 });
        Assert.Equal(CrewPose.Idle, art.LastPose);
        At(2.4, null, stood with { Car = 0 });
        Assert.Equal(CrewPose.Idle, art.LastPose);
    }

    [Fact]
    public void ACrewmateStoodOnAMovingCarIsIdleNotRunning()
    {
        // Note 206: their pace is how fast they move over the car they're on, not over the ground. The train at 15 m/s,
        // drawn at 15 fps: a metre along the line each frame, with them stood still on car 2's roof.
        const double Dt = 1.0 / 15, Speed = 15;
        var art = new SceneArt(Look.Load(Content));
        var stood = PlayerMotor.SpawnOnRoof(World().Train, 2, 0, P);
        for (int i = 0; i < 8; i++)
        {
            var w = World(5_000 + Speed * Dt * i);
            art.Crewmate(new Ballast.Render.MeshBuilder(), CrewActs.Crewmate(1, stood, w, w.Train.Frames), default, Dt * i);
        }
        Assert.Equal(CrewPose.Idle, art.LastPose);

        // Walking along the roof (1.5 m/s over the car) while it moves: a walk, not a run.
        for (int i = 0; i < 8; i++)
        {
            var w = World(5_000 + Speed * Dt * i);
            var walking = stood with { Position = stood.Position + new Double3(0, 0, -1.5 * Dt * i) };
            art.Crewmate(new Ballast.Render.MeshBuilder(), CrewActs.Crewmate(2, walking, w, w.Train.Frames), default, Dt * i);
        }
        Assert.Equal(CrewPose.Walk, art.LastPose);

        // On the ground they're in the world's frame: stood still is idle, and 1.5 m/s over it a walk.
        var ground = new PlayerState { Parent = PlayerState.World, Position = new Double3(4, 0, -5_000) };
        for (int i = 0; i < 8; i++)
            art.Crewmate(new Ballast.Render.MeshBuilder(), CrewActs.Crewmate(3, ground, World(), World().Train.Frames), default, Dt * i);
        Assert.Equal(CrewPose.Idle, art.LastPose);
        for (int i = 0; i < 8; i++)
        {
            var w = World();
            var on = ground with { Position = ground.Position + new Double3(1.5 * Dt * i, 0, 0) };
            art.Crewmate(new Ballast.Render.MeshBuilder(), CrewActs.Crewmate(4, on, w, w.Train.Frames), default, Dt * i);
        }
        Assert.Equal(CrewPose.Walk, art.LastPose);
    }

    [Fact]
    public void YourOwnArmsAreTheCrewsForearmsAndGlovesInView()
    {
        var look = Look.Load(Content);
        var art = new CreatureArt(look, Content);
        var bar = PropArt.Of(look).Get("tool_crowbar");
        Assert.NotNull(bar);
        // Empty hands at your sides: nothing of you is in view.
        var mesh = new Ballast.Render.MeshBuilder();
        Assert.True(art.OwnArms(mesh, 0, 0, null, false, -1, 0, 1));
        Assert.Empty(mesh.Instances);
        // A tool in hand: the arms (a cut of the crew mesh, only forearms and gloves) and the tool, in front of the eye.
        Assert.True(art.OwnArms(mesh, 0, 0, null, false, -1, 0, 1, bar));
        var arms = mesh.Instances.Single(i => i.Asset.Skin is not null);
        int whole = art.Get("crew")!.Parts.Sum(p => p.Indices.Length / 3);
        Assert.InRange(arms.Asset.Triangles, 100, whole / 3);
        // The tool's appended to the mesh: in front of the eye (the mesh's origin), up from the right fist and leaning in
        // across the view, so its middle is about the middle.
        var at = Middle(mesh);
        Assert.True(at.Z < -0.2f, $"the tool at {at}");
        Assert.InRange(at.Y, -0.45f, 0.2f);
        Assert.InRange(at.X, -0.1f, 0.4f);
        // Turned to look behind you, it turns with you.
        mesh.Clear();
        art.OwnArms(mesh, MathF.PI, 0, null, false, -1, 0, 1, bar);
        Assert.True(Middle(mesh).Z > 0.2f);
    }

    static System.Numerics.Vector3 Middle(Ballast.Render.MeshBuilder mesh)
    {
        Assert.True(mesh.Count > 0);
        var sum = System.Numerics.Vector3.Zero;
        foreach (var v in mesh.Vertices)
            sum += v.Position;
        return sum / mesh.Count;
    }
}
