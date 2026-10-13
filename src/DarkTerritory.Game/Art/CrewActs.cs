using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What a crewmate is doing with their hands (X1), read off what the snapshot already carries about them: their flags,
/// their surface, their timed action and what's nearest it, what they're carrying, the gun they're at. Presentation only:
/// nothing here is sent or fed back to the sim, so it's free to guess (a frame's lag in an act doesn't matter), and the
/// crew's clips for it are tools/blender/crew_clips.py's (note 145).
/// </summary>
public static class CrewActs
{
    /// <summary>Falling faster than this (m/s down), someone in the air is falling, not at the top of a jump.</summary>
    const double Falling = 3;

    /// <summary>Rising faster than this (m/s up), someone in the air has jumped (a step off a ledge never rises).</summary>
    const double Rising = 0.5;

    /// <summary>
    /// The crewmate <paramref name="id"/> as they're drawn this frame: where they are and what they're doing. At a gun they're
    /// sat on its seat, facing its way (note 137's cannon: the seat 0.75 m behind the pivot, on the roof under it).
    /// </summary>
    public static Crewmate Crewmate(byte id, in PlayerState s, World world, IReadOnlyList<CarFrame> frames, IReadOnlyList<PlayerState>? others = null)
    {
        var act = Of(s, id, world, others);
        var placed = act is CrewPose.Gunner or CrewPose.Reload && Guns.Mount(world.Train, s.Parent) is { } gun ? Seated(s, gun) : s;
        var (feet, yaw) = Eyes.World(placed, frames);
        var (emote, emoteSeconds) = act is null ? EmoteOf(id, s, world) : (Emote.None, 0);
        int outfit = world.OutfitOf(id);
        return new Crewmate(id, feet, yaw, s.Alive, s.Hand, s.OtherHand, Looks: outfit != id ? outfit : null, Act: act, Holding: Sim.Player.Kit.Held(s),
            Reach: act is CrewPose.Drive or CrewPose.Whistle ? AtTheControls(world, frames, feet, yaw, act == CrewPose.Whistle) : null,
            Lamp: world.Bodies.CarriedBy(id) is { Kind: BodyKind.Lamp }, Survivor: SurvivorOf(id, world),
            Stressed: s.Alive && Stressed(world, PlayerMotor.WorldPosition(s, world.Train)), Health: s.Health,
            Phase: act == CrewPose.Reload ? ReloadPhase(s, world) : 0, Death: s.Death,
            Headset: s.Head > 0 ? new HeadsetBody(s.Head, s.Pitch, s.Parent, s.Position, s.Yaw) : null,
            Car: placed.Parent != PlayerState.World && placed.Parent < frames.Count ? placed.Parent : PlayerState.World, Local: placed.Position,
            Emote: emote, EmoteSeconds: emoteSeconds);
    }

    /// <summary>Walking faster than this (m/s) ends an emote, as a step away from it does (note 298).</summary>
    const double EmoteStill = 0.5;

    /// <summary>
    /// The emote <paramref name="id"/> is at (note 298): their latest, while it lasts (player.json emotes) and while they
    /// stand, and how far into it they are. Anything they're doing (an act) comes first, so this is asked only when idle.
    /// </summary>
    public static (Emote Kind, double Seconds) EmoteOf(int id, in PlayerState s, World world)
    {
        if (!s.Alive || s.Velocity.X * s.Velocity.X + s.Velocity.Z * s.Velocity.Z > EmoteStill * EmoteStill)
            return (Emote.None, 0);
        EmoteEvent? latest = null;
        foreach (var e in world.Emotes)
            if (e.By == id && (latest is null || e.Tick > latest.Value.Tick))
                latest = e;
        if (latest is not { } at)
            return (Emote.None, 0);
        double age = ((double)world.Tick - at.Tick) * SimConstants.TickSeconds;
        return age >= 0 && age < world.EmoteTuning.Seconds(at.Kind) ? (at.Kind, age) : (Emote.None, 0);
    }

    /// <summary>How near a waking threat has to be for a crewmate's run to be a hurried one (m).</summary>
    public const double StressRange = 30;

    /// <summary>Anything past its dormant phase within <see cref="StressRange"/> of <paramref name="at"/> (GDD §31: "hurried under stress").</summary>
    static bool Stressed(World world, Double3 at) =>
        world.ActiveEnemies.Any(e => !e.Gone && e.Phase is not (SpinePhase.Dormant or SpinePhase.Gone) && !e.Hazard
            && (e.WorldPosition(world.Train) - at).Length < StressRange);

    /// <summary>Who has hold of them (App. A.1 GRAB), as the pose that says so from across the car.</summary>
    static CrewPose HeldBy(int id, World world) => HeldPose(world.ActiveEnemies.FirstOrDefault(e => e.Holding == id)?.Kind);

    /// <summary>The pose someone held by a <paramref name="kind"/> is drawn in (App. A.1 GRAB).</summary>
    public static CrewPose HeldPose(EnemyKind? kind) =>
        kind switch
        {
            EnemyKind.Dragger => CrewPose.HeldHang,
            EnemyKind.CarHugger => CrewPose.HeldMouth,
            EnemyKind.Whistler => CrewPose.HeldCarried,
            EnemyKind.TippyToesie => CrewPose.HeldCover,
            EnemyKind.Ribbit => CrewPose.HeldFrozen,
            // (Ground into the peat under the Moose's rack: on their back, pushing at it. Note 311. Flat on the roof under the
            // Gannet's foot, the same: note 340.)
            EnemyKind.SootChildren or EnemyKind.Moose or EnemyKind.Gannet => CrewPose.HeldPinned,
            EnemyKind.Choir => CrewPose.HeldSeized,
            EnemyKind.Passenger => CrewPose.HeldDragged,
            _ => CrewPose.Held,
        };

    /// <summary>The seconds of crew_clips' reload clip that match how far the gun's reload is: 1.5 s a step (its beats), the
    /// steps done plus this one's progress.</summary>
    static double ReloadPhase(in PlayerState s, World world)
    {
        if (world.Combat is not { } combat || Guns.MannedGun(s, world.Train, combat.Guns) is not { } g)
            return 0;
        var gun = world.Train.Vehicles[g].Gun;
        int steps = combat.Guns.ReloadSteps;
        double done = steps - gun.ReloadNeeded + Math.Clamp(gun.ReloadProgress / combat.Guns.ReloadStepSeconds, 0, 1);
        return Math.Clamp(done, 0, steps) * ReloadBeatSeconds;
    }

    /// <summary>One reload step's length in crew_clips.py's reload clip (45 frames at 30 fps).</summary>
    const double ReloadBeatSeconds = 1.5;

    /// <summary>
    /// Who they came back as (GDD App. D.8: "a freed player becomes this character from then on"): the occupant of a freed
    /// Holdout, by its kind (a shelter's wildlander, a prison car's or a lockup's prisoner); the last one freed, if more.
    /// Every machine mirrors the Holdouts, so every machine draws the same.
    /// </summary>
    public static Survivor SurvivorOf(int id, World world)
    {
        // Tonight's freeing, else the look carried from an earlier night (Sim.Run.Identity; note 181).
        return Sim.Run.Identity.Of(world, id) switch
        {
            Sim.Run.Identity.Prisoner => Survivor.Prisoner,
            Sim.Run.Identity.Wildlander => Survivor.Wildlander,
            _ => Survivor.None,
        };
    }

    /// <summary>
    /// A driver's hands where the levers are (GDD §12): one on the regulator, at its notch, and one on the brake valve; or,
    /// whistling, one up on the cord's handle (<see cref="TrainKit.WhistleCord"/>, hauled down). From the feet, facing frame.
    /// </summary>
    static (Double3, Double3)? AtTheControls(World world, IReadOnlyList<CarFrame> frames, Double3 feet, double yaw, bool whistling) =>
        AtTheControls(world.Controls, frames, feet, yaw, whistling);

    /// <summary>The same, for the controls as given (a staged driver: Staging.Driver).</summary>
    public static (Double3, Double3)? AtTheControls(TrainControls c, IReadOnlyList<CarFrame> frames, Double3 feet, double yaw, bool whistling)
    {
        if (frames.Count == 0 || frames[0].Shape.Levers is not { } levers)
            return null;
        Double3 Local(Double3 inCab)
        {
            var d = frames[0].ToWorld(inCab) - feet;
            return new Double3(d.X * Math.Cos(yaw) - d.Z * Math.Sin(yaw), d.Y, d.X * Math.Sin(yaw) + d.Z * Math.Cos(yaw));
        }
        var brake = Local(levers.BrakeAt(c.Brake));
        var other = whistling ? Local(TrainKit.WhistleCordHandle(frames[0].Shape, pulled: true)) : Local(levers.RegulatorAt(c.Throttle));
        return (other, brake);
    }

    /// <summary>The real whistle is blowing, a crewmate's (GDD §12): the Whistler's blast is from nobody's hand on the cord (App. A.4).</summary>
    public static bool CrewWhistling(World world) =>
        world.WhistleSeconds > 0 && !world.ActiveEnemies.OfType<Sim.Enemies.Whistler>().Any(w => w.Phase == Sim.Enemies.SpinePhase.Telegraph && w.Extra > 0.5);

    /// <summary>How near the regulator (across the floor, m) a crewmate in the cab is at the controls.</summary>
    const double AtControls = 0.9;

    /// <summary><paramref name="s"/> moved onto <paramref name="gun"/>'s seat (their feet on the roof under it), facing the gun's way.</summary>
    public static PlayerState Seated(in PlayerState s, GunMount gun)
    {
        var seat = gun.Position - gun.Facing * TrainKit.CannonSeat.Z;
        return s with
        {
            Position = seat with { Y = gun.Position.Y + TrainKit.CannonSeat.Y - GunnerPan },
            Yaw = Math.Atan2(-gun.Facing.X, -gun.Facing.Z),
        };
    }

    /// <summary>How close a crewmate who's held has to be for someone to be hauling at them (m).</summary>
    const double HaulReach = 1.6;

    /// <summary>One of <paramref name="others"/>, alive and held, within <see cref="HaulReach"/> of <paramref name="s"/> in its frame, or null.</summary>
    static PlayerState? HeldNear(in PlayerState s, IReadOnlyList<PlayerState> others)
    {
        foreach (var o in others)
            if (o.Alive && o.Has(PlayerFlags.Held) && o.Parent == s.Parent && (o.Position - s.Position).Length is > 0.05 and < HaulReach)
                return o;
        return null;
    }

    /// <summary>
    /// How a friend held by something is hauled at, by what has them (note 378; App. A.1's rescue): out of the Car Hugger's
    /// mouth, heaved back hand over hand; the Tippy Toesie's fingers prised off their face; lifted away by the Whistler or
    /// the Choir, hauled back down by the legs; over an edge below (a Dragger's), hauled up; otherwise down on a knee at
    /// their collar. What has them is the holding creature nearest them (every machine mirrors the enemies).
    /// </summary>
    static CrewPose Rescue(in PlayerState friend, in PlayerState s, World world)
    {
        var train = world.Train;
        var at = PlayerMotor.WorldPosition(friend, train);
        Enemy? holder = null;
        double best = double.MaxValue;
        foreach (var e in world.ActiveEnemies)
            if (e.Holding >= 0 && (e.WorldPosition(train) - at).Length is var d && d < best)
                (holder, best) = (e, d);
        return RescueOf(HeldPose(holder?.Kind), below: friend.Position.Y < s.Position.Y - 0.5);
    }

    /// <summary>The rescuer's act for a friend held in <paramref name="held"/> (<see cref="HeldPose"/>), and whether they're below the edge.</summary>
    public static CrewPose RescueOf(CrewPose held, bool below) => held switch
    {
        CrewPose.HeldMouth => CrewPose.PullMouth,
        CrewPose.HeldCover => CrewPose.PryOff,
        CrewPose.HeldCarried or CrewPose.HeldSeized => CrewPose.HaulDown,
        _ => below ? CrewPose.HaulUp : CrewPose.Haul,
    };

    /// <summary>
    /// Stood at the door of a Holdout that's being breached (App. D.7): the act by its kind: a barricade pried, a lock picked
    /// with the repair kit (Holdout.Quiet), or smashed.
    /// </summary>
    static CrewPose? Breaching(in PlayerState s, World world)
    {
        if (world.Holdouts is not { } holdouts || s.Parent != PlayerState.World)
            return null;
        foreach (var h in holdouts.All)
            if (h.State == Sim.Run.HoldoutState.Breaching && ((h.Door - s.Position) with { Y = 0 }).Length <= holdouts.Tuning.BreachReach)
                return h.Layout.Kind == Sim.Stops.HoldoutKind.Shelter ? CrewPose.Pry : h.Quiet ? CrewPose.Pick : CrewPose.Smash;
        return null;
    }

    /// <summary>How near its mount (across the floor, m) an extinguisher in hand is being lifted off it or hung back.</summary>
    const double MountReach = 0.7;

    static bool AtMount(Body extinguisher, in PlayerState s, TrainOnLine train) =>
        s.Parent == extinguisher.Home && s.Parent > 0 && s.Parent < train.Frames.Count && train.Frames[s.Parent].Shape.Interior is { } room
        && ((World.ExtinguisherMount(train.Frames[s.Parent].Shape, room) - s.Position) with { Y = 0 }).Length < MountReach + 0.6;

    /// <summary>The seat pan's height over the gunner's feet in crew_clips.py's gunner clip (m).</summary>
    public const double GunnerPan = 0.48;

    /// <summary>
    /// What <paramref name="s"/> (player <paramref name="id"/>) is doing, or null for nothing but standing or walking.
    /// <paramref name="others"/>: the rest of the crew as they stand (a friend held close by is being hauled at).
    /// </summary>
    public static CrewPose? Of(in PlayerState s, int id, World world, IReadOnlyList<PlayerState>? others = null)
    {
        if (!s.Alive)
            return null;
        var train = world.Train;
        if (s.Has(PlayerFlags.Held))
            return HeldBy(id, world);
        // At a Holdout's door while it's breached (App. D.7): smashing its lock, prying its barricade, or picking the lock
        // with the repair kit. (Who's breaching isn't sent; whoever stands at the door is at it.)
        if (Breaching(s, world) is { } breach)
            return breach;
        // Freed, still where they came back inside it (App. D.8): up off the floor.
        var feet = s.Position;
        if (world.Holdouts is { } holdouts && s.Parent == PlayerState.World
            && holdouts.All.Any(h => h.State == Sim.Run.HoldoutState.Freed && h.Occupant == id && ((h.Inside - feet) with { Y = 0 }).Length < 0.3))
            return CrewPose.GetUp;
        if (s.Surface == Surface.Ladder)
            return s.Has(PlayerFlags.SoloCarry) ? CrewPose.ClimbCarry : CrewPose.Climb;
        // In the air: going up off a jump, the leap (note 375; SceneArt holds it over the top); coming down hard, falling.
        if (s.Surface == Surface.Air)
            return s.Velocity.Y < -Falling ? CrewPose.Fall : s.Velocity.Y > Rising ? CrewPose.Jump : null;
        if (s.Has(PlayerFlags.Pushing))
            return CrewPose.Push;
        if (s.Has(PlayerFlags.Operating))
            return CrewPose.Lever;
        if (world.Bodies.CarriedBy(id) is { } carried)
            return carried.Kind switch
            {
                // Over the shoulder (App. C.4), the arm round its legs: where the sim holds it (Bodies.Shoulder).
                BodyKind.Ragdoll => CrewPose.Shoulder,
                // The child in the arms, clinging to them (App. C.4; Bodies.ChildAt).
                BodyKind.Child => CrewPose.Cradle,
                // The hand lamp out low in one hand; the extinguisher on the hip, aimed (App. C.5).
                BodyKind.Lamp => CrewPose.Lantern,
                // Just off its bracket (or about to go back on it), by the mount, it's being lifted.
                BodyKind.Extinguisher when AtMount(carried, s, train) => CrewPose.TakeDown,
                BodyKind.Extinguisher => CrewPose.Extinguish,
                // The repair kit at work on a burst boiler (T109): down at the firebox mending it.
                BodyKind.RepairKit when s.ActionProgress > 0 && CrewActions.AtTheRupture(s, train) => CrewPose.Mend,
                // Otherwise the kit by its handle at the side, a toolbox (note 513), not held out in front like a crate.
                BodyKind.RepairKit => CrewPose.Toolbox,
                _ => CrewPose.Carry,
            };
        // The wrench at work on a break (note 301): down at it, mending.
        if (s.ActionProgress > 0 && Repairs.WrenchInHand(s) && Repairs.ByWrench(train) && Repairs.At(s, train) != BreakKind.None)
            return CrewPose.Mend;
        if (s.Has(PlayerFlags.Shovelful))
            return CrewPose.Shovel;
        if (world.Combat is { } combat && Guns.MannedGun(s, train, combat.Guns) is { } manned)
            return train.Vehicles[manned].Gun.ReloadNeeded > 0 ? CrewPose.Reload : CrewPose.Gunner;
        // On the ground at the line's own levers: a switch stand's (spec C.6), a coaling chute's or a spout's (spec D.3).
        if (s.Parent == PlayerState.World)
        {
            if (world.Switches?.InReach(s, train) is not null)
                return CrewPose.Throw;
            if (world.Run is { } run && run.LeverInReach(s, train))
                return CrewPose.Chute;
        }
        if (world.Run is { } r && (r.SpoutLeverInReach(s, train) is not null || r.LiftLeverInReach(s, train) is not null))
            return CrewPose.Spout;
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return null;
        // Beside a friend something's got hold of (App. A.1's rescue), down hauling them free; one hanging over the edge
        // below (a Dragger's, App. A.4), stood leaning back hauling them up.
        if (others is not null && HeldNear(s, others) is { } friend)
            return Rescue(friend, s, world);
        var near = CrewActions.Nearest(s, train);
        // Something timed under way (CrewActions: the progress resets the moment it stops).
        if (s.ActionProgress > 0)
            return s.Surface == Surface.Coupler && near != InteractableKind.Door ? CrewPose.Uncouple : near switch
            {
                InteractableKind.Firebox or InteractableKind.Coal => CrewPose.Shovel,
                InteractableKind.Door => CrewPose.Door,
                InteractableKind.Handbrake => CrewPose.Handbrake,
                InteractableKind.Hatch => CrewPose.Hatch,
                _ => (CrewPose?)null,
            };
        // Stood on the coupling plate between two cars, balancing over the gap (GDD §32).
        if (s.Surface == Surface.Coupler)
            return CrewPose.Gap;
        // The held valves keep no progress: the boiler venting, the rail being sanded, by whoever's at it.
        if (s.Parent == 0 && s.Surface == Surface.Deck)
        {
            var valve = near switch
            {
                InteractableKind.Vent when train.Boiler.Venting => CrewPose.Vent,
                InteractableKind.Sandbox when train.Sanding => CrewPose.Lever,
                _ => (CrewPose?)null,
            };
            if (valve is not null)
                return valve;
            // At the controls in the cab (GDD §12): hands on the regulator and the brake, or up on the whistle cord while it's
            // blowing. The Whistler's blast has no hand on the cord (App. A.4): that's its tell.
            if (PlayerMotor.InCab(s, train) && train.Frames[0].Shape.Levers is { } levers
                && ((levers.Regulator - s.Position) with { Y = 0 }).Length < AtControls)
                return CrewWhistling(world) ? CrewPose.Whistle : CrewPose.Drive;
        }
        return null;
    }
}
