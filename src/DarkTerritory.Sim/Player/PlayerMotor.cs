using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

[Flags]
public enum PlayerButtons : byte
{
    None = 0,
    Run = 1,
    Jump = 2,
    /// <summary>Interact: grab or let go of a ladder, later levers, switches, cargo.</summary>
    Use = 4,
    /// <summary>Hold the train brake. Only honoured from the engine.</summary>
    Brake = 8,
    /// <summary>Flip the reverser. Only honoured from the engine, with the train stopped.</summary>
    Reverser = 16,
    /// <summary>Fire the mounted gun you're standing at.</summary>
    Fire = 32,
    /// <summary>Throw what you're carrying.</summary>
    Throw = 64,
    /// <summary>A reaching hand is reported in <see cref="PlayerIntent.HandX"/>..<see cref="PlayerIntent.HandZ"/> (T29, VR).</summary>
    Hand = 128,
}

/// <summary>
/// What else a player's hands do this tick (GDD v1.1): a second byte of buttons, the first being full. Everyone swings the
/// tool they carry (App. C.2), pulls the whistle cord in the cab (§12), and switches a car's lamp (App. A.5, Fire Flies).
/// </summary>
[Flags]
public enum PlayerActions : byte
{
    None = 0,
    /// <summary>Swing your tool (App. C.2 melee): held, it swings as often as the tool recovers.</summary>
    Swing = 1,
    /// <summary>The whistle cord, from the cab (GDD §12, the conductor's whistle; it feeds the loudness meter).</summary>
    Whistle = 2,
    /// <summary>The lamp in the car you're in, on or off (a toggle on the press).</summary>
    CarLamp = 4,
    /// <summary>
    /// Cut the coupling you're standing on (T91 playtest): its own key, held, looking down at the coupler. It used to be Use,
    /// which is also every door's and ladder's, so couplings came apart by accident.
    /// </summary>
    Uncouple = 8,
    /// <summary>
    /// The blow-off held open from anywhere in the cab (note 264, the director's notes on build 1121: one key, held). The
    /// same bit as <see cref="Uncouple"/>: that's only on a coupler plate, and the cab never is one.
    /// </summary>
    Vent = 8,
    /// <summary>Take hold of the nearest ladder in reach, whichever way you face (T94 playtest): its own key.</summary>
    Ladder = 16,
    /// <summary>On the wire only: this intent carries a hotbar choice (<see cref="PlayerIntent.Select"/>, <see cref="PlayerIntent.Cycle"/>).</summary>
    Tool = 32,
    /// <summary>Sit at the gun you're at (T112): sent on the press. Sitting only, so a repeated intent is harmless; Jump gets up.</summary>
    Seat = 64,
    /// <summary>
    /// The dead's key (the last free bit, so two names for one context each; note 176, note 177). <see cref="Bookmark"/>: while
    /// the run's under way, a bookmark of the view you're following (GDD v1.4 App. D.10's UI, D.12), sent on the press; the host
    /// takes one on the tick it first sees it held. <see cref="Skip"/>: once the night's over, a held vote to skip the derailment
    /// film to its cause card (App. E.5) or the Stranded outro (E.9); the host counts heads, and a majority, or the host, skips.
    /// With each player's own skip (wreck.json "skip", note 315) the client keeps it and never sends it.
    /// The two never overlap: a run under way has no film or outro, and a night that's over takes no bookmarks.
    /// </summary>
    Bookmark = 128,
    Skip = 128,
}

/// <summary>
/// A crewmate's emote (GDD §9: in the yard "the crew wait for friends, hang out, dance"; note 298), sent on the press. The
/// crew see it; nothing in the sim acts on it.
/// </summary>
public enum Emote : byte { None, Dance, Wave, Point }

/// <summary>An emote as the crew see it (note 298): unique for the night (its record's key), the host tick it began, whose, which.</summary>
public readonly record struct EmoteEvent(int Id, uint Tick, int By, Emote Kind);

/// <summary>The forward lamp's switch in the cab (T52): set it on or off (a setting, not a toggle, so a held key or a resent intent is harmless).</summary>
public enum LampSwitch : byte { None, On, Off }

/// <summary>
/// What a client sends each tick (GDD §33: intent only, never positions).
/// Look is a delta so it stays meaningful when the player changes reference frame.
/// </summary>
public struct PlayerIntent
{
    /// <summary>Strafe, −1..1, positive right.</summary>
    public float MoveX;
    /// <summary>Forward/back, −1..1, positive forward. On a ladder, positive climbs.</summary>
    public float MoveZ;
    public float LookYaw;
    public float LookPitch;
    public PlayerButtons Buttons;
    /// <summary>Throttle notches to move this tick (−4..4). Only honoured from the engine.</summary>
    public sbyte ThrottleNotch;
    /// <summary>The forward lamp switched on or off (T52, "lamps down" against the Lamplighters). Only honoured from the cab.</summary>
    public LampSwitch Lamp;
    /// <summary>The second byte of buttons (<see cref="PlayerActions"/>).</summary>
    public PlayerActions Actions;
    /// <summary>
    /// How loud this player's voice is this tick, 0..255 (App. C.7, the loudness meter; C.8, the Gaunt listens for silence).
    /// It's what their microphone is sending, measured on their machine, so it's intent like any key (a bot "talks" by
    /// setting it). It moves no player or train state, only what the host's enemies hear.
    /// </summary>
    public byte Voice;
    /// <summary>
    /// Dead or waiting to board: the living crewmate this player watches (GDD App. D.10), 0 for nobody. It moves no player
    /// or train state; the host centres what it sends them (enemies, loose bodies) and what they hear on that player.
    /// </summary>
    public byte Watch;
    /// <summary>T108: a hotbar slot picked this tick (a number key), 1..<see cref="Kit.Slots"/>; 0 for none.</summary>
    public byte Select;
    /// <summary>T108: the wheel, a step to the next (+1) or previous (−1) slot with a tool in it; 0 for none.</summary>
    public sbyte Cycle;
    /// <summary>Note 298: an emote picked this tick (its wheel let go), or none. Sent once, on the pick.</summary>
    public Emote Emote;
    /// <summary>
    /// With <see cref="PlayerButtons.Hand"/>: a VR player's reaching hand, in metres from their feet in the frame they face
    /// (x right, y up, z behind, so ahead is −Z as ever). Reach is tested from it instead of from the body (T29).
    /// </summary>
    public float HandX, HandY, HandZ;
    /// <summary>
    /// With <see cref="Other"/> (and a reaching hand): the VR player's other hand, in the same frame (T43). Nothing reaches
    /// from it; it's there for what takes both hands, like getting a grip on your end of a heavy crate.
    /// </summary>
    public float OtherX, OtherY, OtherZ;
    public bool Other;
    /// <summary>
    /// With a reaching hand: the headset's height over the feet (m), on the centimetre grid; 0 when it isn't reported (a
    /// keyboard, a bot). Nothing in the sim acts on it: it goes out with the hands so the rest of the crew see the body
    /// under the head lean and crouch (T82, roadmap M4 "VR body IK").
    /// </summary>
    public float Head;

    public readonly bool Has(PlayerButtons b) => (Buttons & b) != 0;
    public readonly bool Has(PlayerActions a) => (Actions & a) != 0;

    /// <summary>
    /// Reports a hand, on the centimetre grid the wire carries (<see cref="Net.Messages"/>), so a predicting client
    /// uses the hand the host will. The other hand too, when it's tracked; and the head's height over the feet (T82), when
    /// there's a headset to say (0: none).
    /// </summary>
    public void Reach(Double3 hand, Double3? other = null, double head = 0)
    {
        Head = head > 0 ? Centimetres(head) : 0;
        HandX = Centimetres(hand.X);
        HandY = Centimetres(hand.Y);
        HandZ = Centimetres(hand.Z);
        Buttons |= PlayerButtons.Hand;
        Other = other is not null;
        if (other is { } o)
        {
            OtherX = Centimetres(o.X);
            OtherY = Centimetres(o.Y);
            OtherZ = Centimetres(o.Z);
        }
    }

    public static float Centimetres(double metres) => (float)(Math.Round(Math.Clamp(metres, -300, 300) * 100) / 100);
}

/// <summary>What the player is on. Roof is exposed (roof speeds, Draggers); Deck is footing on the train that isn't.</summary>
public enum Surface : byte { Air, Ground, Roof, Coupler, Ladder, Deck }

/// <summary><see cref="Taken"/>: by the Soot Children, answering a voice from outside (T40).</summary>
/// <summary><see cref="Dragged"/>: pulled off the train at speed by the Draggers (T46).</summary>
/// <summary><see cref="Crushed"/>: under a casting let go of by the crane (T48).</summary>
/// <summary><see cref="PulledUnder"/>: into a coupling gap while the Rattle rattled (T51).</summary>
/// <summary><see cref="Lamplighter"/>: nearest when a Lamplighter reached the lamp (T52).</summary>
/// <summary><see cref="Deadman"/>, <see cref="Stoker"/>: fighting one out of the cab, or out of the firebox (T53).</summary>
/// <summary><see cref="Burned"/>, <see cref="Gnawed"/>: in a car with a fire, or a nest of Gnawers (the in-car incidents).</summary>
/// <summary><see cref="Waiting"/> isn't a death: a player who joined mid-run, spectating in the respawn queue until a Holdout frees them (GDD App. D.3).</summary>
/// <summary><see cref="Struck"/>: stood on a roof into a tunnel's mouth; <see cref="Thrown"/>: off a roof on a curve taken over its board.</summary>
public enum DeathCause : byte
{
    None, JumpedAtSpeed, Derailed, Mauled, Hollow, Choir, Cold, Taken, Dragged, Crushed, PulledUnder, Lamplighter, Deadman, Stoker, Ferryman, Climbed, TornOff, Gaunt, Struck, Thrown, Burned, Gnawed, Replaced, Nested, Drift,
    // GDD v1.1: swallowed by the Car Hugger; smothered by Tippy Toesie; eaten by Ribbits; drained by a Soot Child; carried off
    // to the Whistler's nest; seized by the Choir; taken with the caboose by the Passenger.
    Eaten, Suffocated, Devoured, Drained, Carried, Seized, Uncoupled,
    // GDD v1.2 App. D.5: not dead, waiting in the queue for a Holdout (joined after the gate opened).
    Waiting,
    // GDD §19, App. B.9 (note 182): a powder car going up; a cannon fired by the chemicals.
    Exploded, Poisoned,
    // GDD §18 (WP15, note 185): a powder keg at the depot going up; a chemical works' hose leaking.
    Keg, Leak,
    // GDD §18 (WP15b, note 187): a wreck yard's heap shifting on whoever was by it.
    Wreckage,
    // GDD §21, App. A.6 (note 339): pinned under the Moose's rack and ground into the peat.
    Trampled,
    // GDD §21, App. A.4 (note 340): the Gannet's fourth peck, pinned under its foot.
    Pecked
}

/// <summary>Conditions a player carries.</summary>
[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    /// <summary>
    /// Carrying a body, the last of the crew alive (GDD v1.4 App. D.9 "solo remainer"): <see cref="Heavy"/>'s pace on the
    /// ground, but a ladder may still be climbed, slowly (<see cref="PlayerTuning.SoloBodyClimb"/>; note 181).
    /// </summary>
    SoloCarry = 1,
    /// <summary>
    /// Carrying something that needs both arms (spec B.2 "carrying heavy cargo: 2.8 m/s, no climbing"; GDD v1.4 App. C.4, D.9:
    /// freight, a toy, a find, the child, a body).
    /// </summary>
    Heavy = 2,
    /// <summary>A hand has coal on the shovel from the tender, on its way to the firebox (T29).</summary>
    Shovelful = 4,
    /// <summary>At a crane's controls (T48): the stick and Jump drive the crane, not you.</summary>
    Operating = 8,
    /// <summary>
    /// Held by something (GDD v1.1 App. A.1 GRAB): you can look, talk and struggle, and nothing else, until a friend frees
    /// you or the window runs out. Set by the host each tick from what's holding whom.
    /// </summary>
    Held = 16,
    /// <summary>Pushing the gun they're at along its roof rail (T93): walking pace at most, and it goes where they go.</summary>
    Pushing = 32,
    /// <summary>
    /// In the gun's seat (T112): the mouse lays the gun (it follows at its own pace), the left button fires it, Use held
    /// loads it, Jump gets up. Your feet are the seat's, on the carriage as it turns.
    /// </summary>
    Seated = 64,
    /// <summary>
    /// The repair kit in their hands (GDD §12): what mends a ruptured boiler (T109). Set by the host each tick from what
    /// they carry, so a predicting client mends as the host does.
    /// </summary>
    RepairKit = 128,
}

/// <summary>
/// Authoritative player movement state. Position and velocity are in the parent frame:
/// world when <see cref="Parent"/> is <see cref="World"/>, otherwise that car's <see cref="CarFrame"/>.
/// </summary>
public struct PlayerState
{
    public const int World = -1;

    public int Parent;
    /// <summary>Feet position in the parent frame.</summary>
    public Double3 Position;
    /// <summary>Velocity relative to the parent frame.</summary>
    public Double3 Velocity;
    /// <summary>Radians, relative to the parent frame; 0 faces its −Z.</summary>
    public double Yaw;
    public double Pitch;
    public Surface Surface;
    public int Health;
    public DeathCause Death;
    /// <summary>Last known distance along the line, used to find the ground under a player off the train.</summary>
    public double LineHint;
    /// <summary>Seconds into a timed action (shovelling). Resets when the action stops.</summary>
    public double ActionProgress;
    /// <summary>Seconds of cold exposure (spec B.2): climbs outside, falls near heat, kills at the death mark.</summary>
    public double Cold;
    public PlayerFlags Flags;
    /// <summary>
    /// A VR player's reaching hand this tick, from the feet in the frame they face (<see cref="PlayerIntent.HandX"/>);
    /// zero for a player without one. Taken from each tick's intent by <see cref="PlayerMotor.TakeHand"/>, so it isn't
    /// replicated: the host and a predicting client both have it from the same intent.
    /// </summary>
    public Double3 Hand;
    /// <summary>The VR player's other hand, the same way (T43); zero when it isn't reported.</summary>
    public Double3 OtherHand;
    /// <summary>
    /// The VR player's head height over their feet (T82, <see cref="PlayerIntent.Head"/>), taken with the hands; zero when
    /// it isn't reported. Presentation only: what the crew see their body do under it.
    /// </summary>
    public double Head;
    /// <summary>
    /// Counts the host's authoritative moves (respawns, revivals, a harness shift change). A client that sees it change
    /// adopts the new state as a placement, not as a misprediction to correct.
    /// </summary>
    public byte Placed;
    /// <summary>T108: the tools carried, a byte a slot (<see cref="Player.Kit"/>).</summary>
    public ulong Kit;
    /// <summary>The hotbar slot in hand, 0..<see cref="Player.Kit.Slots"/> − 1.</summary>
    public byte HeldSlot;

    public readonly bool Alive => Death == DeathCause.None;
    public readonly bool Has(PlayerFlags flag) => (Flags & flag) != 0;
    public readonly bool Grounded => Surface is Surface.Ground or Surface.Roof or Surface.Coupler or Surface.Deck;
}

/// <summary>
/// Kinematic first-person movement on and around a moving train. Deterministic and allocation-free,
/// so the host and a predicting client produce identical results from identical intent.
/// Greybox collision only: car bodies, coupler plates, ladders and the ground. Interiors and props
/// move to Jolt when it lands (ARCHITECTURE §4).
/// </summary>
public static class PlayerMotor
{
    const double MaxPitch = 1.5;
    const double GroundSnap = 0.05;
    const double NearbyCar = 40;

    /// <summary>Stands a player on whatever is highest at (x, z) on a car: a roof, or the engine's boiler.</summary>
    public static PlayerState SpawnOnRoof(TrainOnLine train, int car, double localZ, PlayerTuning p, double localX = 0)
    {
        var top = train.Frames[car].Shape.TopAt(localX, localZ) ?? (train.Frames[car].Shape.RoofHeight, SurfaceKind.Roof);
        return new PlayerState
        {
            Parent = car,
            Position = new Double3(localX, top.Top, localZ),
            Surface = ToSurface(top.Kind),
            Health = p.Health,
            LineHint = train.Cars[car].FrontDistance,
            Kit = p.StartingKit,
        };
    }

    /// <summary>Stands a player on the cab floor, facing forward: where the conductor and fireman work.</summary>
    public static PlayerState SpawnInCab(TrainOnLine train, PlayerTuning p, double localX = 0)
    {
        var shape = train.Frames[0].Shape;
        var cab = shape.Cab ?? throw new InvalidOperationException("engine has no cab");
        return new PlayerState
        {
            Parent = 0,
            Position = new Double3(localX, cab.Min.Y + 0.1, CabFloorZ(shape)),
            Surface = Surface.Deck,
            Health = p.Health,
            LineHint = train.Cars[0].FrontDistance,
            Kit = p.StartingKit,
        };
    }

    /// <summary>
    /// Where along the cab a crewmate's put in it: on the footplate's open floor, clear across the cab's width, so a spawn
    /// to either side lands on the boards. Cab forward (note 276), that's the strip between the driver's console and the
    /// coal bunker, which stands along the left wall at the cab's middle; a cab without one, its middle.
    /// </summary>
    public static double CabFloorZ(CarShape shape)
    {
        var cab = shape.Cab!.Value;
        foreach (var solid in shape.Solids)
            if (solid.Part == PartKind.Tender && cab.ContainsXZ(solid.Box.Centre))
                return (cab.Min.Z + ConsoleDepth + solid.Box.Min.Z) / 2;
        return cab.Centre.Z;
    }

    /// <summary>How far back from the cab's front the driver's console stands (m): the floor starts behind it.</summary>
    const double ConsoleDepth = 0.6;

    /// <summary>True when standing inside the engine's cab.</summary>
    public static bool InCab(in PlayerState s, TrainOnLine train) =>
        s.Parent == 0 && s.Surface == Surface.Deck && train.Frames[0].Shape.Cab is { } cab && cab.Contains(s.Position);

    /// <summary>
    /// The enclosed space a player is in: <see cref="Outside"/>, the engine cab (vehicle 0), or a car's
    /// interior with every door shut (that car's id). A car with a door open is part of the outside: sound,
    /// voice and the Choir come in through it (GDD §26: protected versus exposed). So is a breached car until it's boarded
    /// up (decided 1 Oct: "a breached car no longer counts as behind a closed door"; <see cref="Vehicle.Breached"/>).
    /// </summary>
    public static int Space(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return Outside;
        if (InCab(s, train))
            return 0;
        var shape = train.Frames[s.Parent].Shape;
        if (shape.Interior is { } room && room.Contains(s.Position) && s.Surface == Surface.Deck && train.Vehicles[s.Parent] is { DoorsOpen: 0, Breached: false })
            return s.Parent;
        return Outside;
    }

    public const int Outside = -1;

    /// <summary>Inside a car's walls, doors open or not.</summary>
    public static bool Indoors(in PlayerState s, TrainOnLine train) =>
        InCab(s, train) || s.Parent != PlayerState.World && s.Parent < train.Frames.Count
            && train.Frames[s.Parent].Shape.Interior is { } room && room.Contains(s.Position) && s.Surface == Surface.Deck;

    static Surface ToSurface(SurfaceKind k) => k switch
    {
        SurfaceKind.Roof => Surface.Roof,
        SurfaceKind.Coupler => Surface.Coupler,
        _ => Surface.Deck,
    };

    public static PlayerState SpawnOnGround(Double3 world, RailLine line, double lineHint, PlayerTuning p)
    {
        var s = new PlayerState { Parent = PlayerState.World, Position = world, Surface = Surface.Ground, Health = p.Health, LineHint = lineHint, Kit = p.StartingKit };
        s.Position = world with { Y = GroundHeight(ref s, line) };
        return s;
    }

    /// <summary>
    /// Stands a player up where something of theirs lies (a returning player's body, note 253): in a car's frame, on the
    /// highest walkable surface under it within a step (a floor, a roof, a coupler plate); off the train, or over nothing on
    /// it (between the cars), on the ground below.
    /// </summary>
    public static PlayerState StandUp(TrainOnLine train, int parent, Double3 at, double lineHint, PlayerTuning p)
    {
        if (parent != PlayerState.World && parent < train.Frames.Count)
        {
            var frame = train.Frames[parent];
            if (frame.Shape.TopAt(at.X, at.Z, at.Y + p.StepUp) is { } top)
                return new PlayerState
                {
                    Parent = parent,
                    Position = new Double3(at.X, top.Top, at.Z),
                    Surface = ToSurface(top.Kind),
                    Health = p.Health,
                    LineHint = train.Cars[parent].FrontDistance,
                    Kit = p.StartingKit,
                };
            at = frame.ToWorld(at);
            lineHint = train.Cars[parent].FrontDistance;
        }
        return SpawnOnGround(at, train.Line, lineHint, p);
    }

    /// <summary>Turns the view by this tick's look input. <see cref="World.CrewAct"/> does this first, so shots go where you look.</summary>
    public static void Look(ref PlayerState s, in PlayerIntent intent)
    {
        if (!s.Alive)
            return;
        s.Yaw += intent.LookYaw;
        s.Pitch = Math.Clamp(s.Pitch + intent.LookPitch, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Takes this tick's reaching hand from the intent (after <see cref="Look"/>, so it's in the frame the player now
    /// faces), held within an arm's length across of the body and between the feet and overhead: an intent can say
    /// where a hand is, not put it through a wall at the far end of the car. It can be as low as the feet because the
    /// headset player crouches for real (the sim's body doesn't). Without hand tuning, or without a hand, there's none.
    /// </summary>
    public static void TakeHand(ref PlayerState s, in PlayerIntent intent, HandTuning? hand)
    {
        s.Hand = s.OtherHand = default;
        s.Head = 0;
        if (hand is null || !s.Alive || !intent.Has(PlayerButtons.Hand))
            return;
        s.Hand = Held(intent.HandX, intent.HandY, intent.HandZ, hand);
        if (s.Hand != default && intent.Other)
            s.OtherHand = Held(intent.OtherX, intent.OtherY, intent.OtherZ, hand);
        // The head (T82): somewhere between a deep crouch and on tiptoe, as far as the hands can go overhead.
        if (s.Hand != default && float.IsFinite(intent.Head) && intent.Head > 0)
            s.Head = Math.Round(Math.Clamp(intent.Head, MinHead, hand.Overhead) * 100) / 100;
    }

    /// <summary>The lowest a reported head is taken to be over the feet (m): kneeling, near enough.</summary>
    public const double MinHead = 0.5;

    static Double3 Held(float hx, float hy, float hz, HandTuning hand)
    {
        if (!float.IsFinite(hx) || !float.IsFinite(hy) || !float.IsFinite(hz))
            return default;
        double x = hx, z = hz, across = Math.Sqrt(x * x + z * z);
        if (across > hand.Arm)
            (x, z) = (x * hand.Arm / across, z * hand.Arm / across);
        static double Cm(double v) => Math.Round(v * 100) / 100;
        return new Double3(Cm(x), Cm(Math.Clamp(hy, 0.01, hand.Overhead)), Cm(z));
    }

    /// <summary>A player's reaching hand (or, with <paramref name="other"/>, their other hand) in their parent frame, or null.</summary>
    public static Double3? HandAt(in PlayerState s, bool other = false)
    {
        var hand = other ? s.OtherHand : s.Hand;
        if (hand == default)
            return null;
        double c = DMath.Cos(s.Yaw), n = DMath.Sin(s.Yaw);
        return s.Position + new Double3(hand.X * c + hand.Z * n, hand.Y, -hand.X * n + hand.Z * c);
    }

    /// <summary>A player's reaching hand (or their other hand) in the world, or null for a player without one.</summary>
    public static Double3? HandWorld(in PlayerState s, TrainOnLine train, bool other = false) => HandAt(s, other) is { } h ? ToWorld(s, train, h) : null;

    /// <summary>
    /// Whether a player's hands are on a grip (in the world): a reported hand within grab of it, or, for everyone else,
    /// whatever the thing's own reach rule says (<paramref name="body"/>).
    /// </summary>
    public static bool Grips(in PlayerState s, TrainOnLine train, HandTuning? hand, Double3 grip, bool body) =>
        hand is not null && HandWorld(s, train) is { } h ? (h - grip).Length <= hand.Grab : body;

    /// <summary>Advances one tick. Call after the train has stepped this tick.</summary>
    /// <param name="applyLook">False when look was already applied this tick (the world's crew step does it).</param>
    public static void Step(ref PlayerState s, in PlayerIntent input, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt, bool applyLook = true)
    {
        var intent = input;
        if (!s.Alive)
            return;
        Player.Kit.Select(ref s, intent);

        if (applyLook)
            Look(ref s, intent);

        StepCold(ref s, train, p, dt);
        if (!s.Alive)
            return;

        // Held (App. A.1 GRAB): your feet are the thing's now. What moves you is what holds you.
        if (s.Has(PlayerFlags.Held))
        {
            intent.MoveX = intent.MoveZ = 0;
            intent.Buttons &= ~(PlayerButtons.Jump | PlayerButtons.Run);
        }

        if (s.Surface == Surface.Ladder)
        {
            StepLadder(ref s, intent, train, p, t, dt);
            return;
        }

        if (s.Grounded)
        {
            double speed = s.Surface == Surface.Roof
                ? (intent.Has(PlayerButtons.Run) ? p.RoofRun : p.RoofWalkSafe)
                : (intent.Has(PlayerButtons.Run) ? p.Run : p.Walk);
            if (Chilled(s, p))
                speed *= p.Cold.OnsetSpeedScale;
            if (s.Has(PlayerFlags.Heavy))
                speed = Math.Min(speed, p.CarryHeavy);
            if (s.Has(PlayerFlags.Pushing))
                speed = Math.Min(speed, p.PushGun);
            if (s.Has(PlayerFlags.Operating) || s.Has(PlayerFlags.Seated))
                speed = 0;
            var wish = WishDirection(s.Yaw, intent) * speed;
            // GDD §22 wind on a roof (note 201): a sideways push across the car, on top of where you're going.
            double wind = WindPush(s, intent, train, p, t);
            s.Velocity = new Double3(wish.X + wind, 0, wish.Z);
            // Jump in the gun's seat is getting up out of it (T112), not a leap off the carriage.
            if (s.Has(PlayerFlags.Seated))
            {
                if (intent.Has(PlayerButtons.Jump))
                    s.Flags &= ~PlayerFlags.Seated;
            }
            else if (intent.Has(PlayerButtons.Jump) && !s.Has(PlayerFlags.Heavy) && !s.Has(PlayerFlags.Operating))
            {
                // Take off in the car's frame and integrate this tick there. The car has already moved
                // this tick; switching to world first would count its motion twice (0.73 m at 22 m/s).
                // Support resolution below moves us into the world frame once we're clear of the roof.
                s.Velocity = s.Velocity with { Y = p.JumpVelocity };
                s.Surface = Surface.Air;
            }
            // T128 (note 273): walking, the edge holds you unless you mean to go over it. A jump goes where it goes.
            if (s.Surface != Surface.Air)
                HoldAtEdge(ref s, wish, wind, train, p, dt);
        }
        else if (s.Parent == PlayerState.World)
        {
            s.Velocity -= Double3.Up * (p.Gravity * dt);
        }

        // Integrate in the parent frame: a car parent carries the player with it for free.
        var prevWorld = ToWorld(s, train, s.Position);
        int pushedOn = s.Has(PlayerFlags.Pushing) && s.Grounded ? s.Parent : PlayerState.World;
        double pushedFrom = s.Position.Z;
        s.Position += s.Velocity * dt;
        var world = ToWorld(s, train, s.Position);

        world = Collide(world, train, p, out bool ceiling);
        // The world is solid (note 279): down in a tunnel you stay inside its lining; on the land you don't walk up a cliff.
        if (s.Parent == PlayerState.World && train.Line.Conditions is { } land)
        {
            world = land.Confine(world, p.Radius);
            if (s.Grounded && s.Surface == Surface.Ground)
                world = Walkable(world, prevWorld, land, p);
        }
        if (ceiling && s.Velocity.Y > 0)
            s.Velocity = s.Velocity with { Y = 0 };
        UpdateSupport(ref s, world, prevWorld, train, p, t);
        // The gun you're pushing goes along its rail as far as you went along the car (T93).
        if (pushedOn != PlayerState.World && s.Parent == pushedOn)
            Combat.Guns.Slide(train, pushedOn, s.Position.Z - pushedFrom);

        // Use while pushing towards it grabs a ladder; Use standing still is for working things (CrewActions). A hand on
        // the ladder takes hold of it without pushing (T29). Walking straight into the foot of one takes hold of it too
        // (T90 playtest: nobody found Use + forward). Not while pushing a gun along (Use and walking is that too, T103: the
        // guard van's hatch ladder comes up through the roof on the gun's way, and took whoever pushed it down inside); the
        // ladder key still does.
        if (s.Surface != Surface.Ladder && (!s.Has(PlayerFlags.Heavy) || s.Has(PlayerFlags.SoloCarry)) && !s.Has(PlayerFlags.Seated))
        {
            bool pushing = s.Has(PlayerFlags.Pushing);
            if (intent.Has(PlayerActions.Ladder))
                TryGrabLadder(ref s, train, p, byHand: false);
            else if (!pushing && intent.Has(PlayerButtons.Use) && (intent.MoveZ > 0.5 || s.Hand != default))
                TryGrabLadder(ref s, train, p, byHand: intent.MoveZ <= 0.5);
            else if (!pushing && intent.MoveZ > 0.5 && s.Grounded)
                TryGrabLadder(ref s, train, p, byHand: false, walkIn: true);
        }
    }

    /// <summary>
    /// T128 (build 1121: "far too easy to fall off the train"; note 273): on a roof or a coupler plate, a step that would take
    /// your feet within <see cref="EdgeTuning.Lip"/> of where the footing ends is held there, across the car and along it
    /// separately (so you slide along the edge). Over the side only if you mean it: walking at it within
    /// <see cref="EdgeTuning.StepOffDegrees"/> of straight out. Off an end only by a jump or a ladder, unless there's more of
    /// the train under it to step down onto (a coupler plate, a platform, the tender). The wind (unless
    /// <see cref="EdgeTuning.WindOverLip"/>) brings you to the lip and no further. A pull, a throw or a jump isn't walking: they
    /// put you in the air and this never sees them.
    /// </summary>
    static void HoldAtEdge(ref PlayerState s, Double3 wish, double wind, TrainOnLine train, PlayerTuning p, double dt)
    {
        var e = p.Edge;
        if (e.Lip <= 0 || s.Parent < 0 || s.Parent >= train.Frames.Count || s.Surface is not (Surface.Roof or Surface.Coupler))
            return;
        var v = s.Velocity;
        if (v.X == 0 && v.Z == 0)
            return;
        var frame = train.Frames[s.Parent];
        var at = s.Position;
        if (v.X != 0 && !Footing(train, frame, at + new Double3(v.X * dt + Math.Sign(v.X) * e.Lip, 0, 0), e.CatchDrop))
        {
            double length = Math.Sqrt(wish.X * wish.X + wish.Z * wish.Z);
            bool meant = length > 1e-6 && wish.X * Math.Sign(v.X) >= DMath.Cos(e.StepOffDegrees * Math.PI / 180) * length;
            if (!meant)
                v = v with { X = e.WindOverLip && Math.Sign(wind) == Math.Sign(v.X) ? wind : 0 };
        }
        if (v.Z != 0 && !Footing(train, frame, at + new Double3(0, 0, v.Z * dt + Math.Sign(v.Z) * e.Lip), e.CatchDrop))
            v = v with { Z = 0 };
        s.Velocity = v;
    }

    /// <summary>
    /// Whether there's footing under a point in a car's frame, given at the height of the feet: this car's or a neighbour's,
    /// no higher than a step up, and a roof or a plate no further down than <paramref name="drop"/> (beyond that it's the
    /// ballast).
    /// </summary>
    static bool Footing(TrainOnLine train, CarFrame frame, Double3 local, double drop)
    {
        if (Catches(frame.Shape.TopAt(local.X, local.Z, local.Y + 0.05), local.Y, drop))
            return true;
        var world = frame.ToWorld(local);
        foreach (var other in train.Frames)
        {
            if (other.Index == frame.Index || (other.Origin - world).Length > NearbyCar)
                continue;
            var there = other.ToLocal(world);
            if (Catches(other.Shape.TopAt(there.X, there.Z, there.Y + 0.05), there.Y, drop))
                return true;
        }
        return false;
    }

    /// <summary>How far down a step off a roof's footing is still just a step (anything of the train's at all).</summary>
    const double EdgeStepDown = 0.5;

    /// <summary>
    /// Footing at a step's height, or a roof or a plate further down to step onto. Not a door's steps, a running board or the
    /// chassis a long way below: dropped onto from a roof at speed, they're no catch.
    /// </summary>
    static bool Catches((double Top, SurfaceKind Kind)? top, double feet, double drop) =>
        top is { } t && (t.Top >= feet - EdgeStepDown || t.Kind != SurfaceKind.Deck && t.Top >= feet - drop);

    /// <summary>Whether the ladder key would take hold of a ladder from here (T94: the HUD says so).</summary>
    public static bool LadderInReach(in PlayerState s, TrainOnLine train, PlayerTuning p)
    {
        if (!s.Alive || s.Surface is Surface.Ladder or Surface.Air || s.Has(PlayerFlags.Heavy) && !s.Has(PlayerFlags.SoloCarry))
            return false;
        var probe = s;
        TryGrabLadder(ref probe, train, p, byHand: false);
        return probe.Surface == Surface.Ladder;
    }

    /// <summary>How close to a ladder's foot walking into it takes hold (tighter than Use's reach, so passing one doesn't).</summary>
    const double WalkInReach = 0.45;

    /// <summary>
    /// Spec B.2 cold: exposure climbs outside and kills at the death mark; near heat it falls fast enough that even the
    /// nearly frozen are recovered within the reset time. Heat is the cab while the fire's lit, a shut car while the
    /// boiler has steam to heat it, or a crew car's stove (note 184).
    /// </summary>
    static void StepCold(ref PlayerState s, TrainOnLine train, PlayerTuning p, double dt)
    {
        // The fortress yard before the run begins is a safe space (note 263): the cold doesn't bite there.
        if (train.HeldInYard)
            return;
        var c = p.Cold;
        if (NearHeat(s, train))
        {
            s.Cold = Math.Max(0, s.Cold - dt * c.DeathSeconds / c.RecoverSecondsNearHeat);
            return;
        }
        // Out of the wind inside a car with a door open: it comes on, but slower (spec B.2), and slower still in insulated cars
        // (spec F.3 car insulation, note 184: a car with the steam gone cold as well). GDD §22 deep cold (note 183): faster
        // the colder it is where they are (the night's cold, high ground, exposed track).
        double deep = 1 + c.PerColdStep * ColdStep(s, train);
        s.Cold += (Indoors(s, train) ? dt * c.IndoorsRate * train.Dynamics.Tuning.Composition.Insulation : dt) * deep;
        if (s.Cold >= c.DeathSeconds)
        {
            s.Health = 0;
            s.Death = DeathCause.Cold;
        }
    }

    /// <summary>GDD §22 deep cold where a player is (note 183): the route's cold step there, 0 on a line without conditions.</summary>
    public static int ColdStep(in PlayerState s, TrainOnLine train) =>
        Math.Max(0, train.Line.Conditions?.ColdStep(RailLine.MainPath, s.LineHint) ?? 0);

    /// <summary>
    /// GDD §22 wind, spec B.2 "roof run: wind and balance penalty" (note 201): how hard the wind pushes someone on a roof
    /// across their car (m/s along the car's +X, its right), or 0 off one. The route's wind there (the night's, ×1.5 on
    /// exposed track), harder the faster the train goes and at a run, in gusts from either side along the line; a hand
    /// on the roof handrails takes most of it, and a gun's seat is behind its shield. The same on every machine: the
    /// conditions are built from the night's seed, and the gusts are a hash of where along the line you are.
    /// </summary>
    public static double WindPush(in PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t)
    {
        var w = p.Wind;
        if (s.Surface != Surface.Roof || s.Parent == PlayerState.World || s.Has(PlayerFlags.Seated) || s.Has(PlayerFlags.Held) || w.Drift <= 0)
            return 0;
        double wind = train.Line.Conditions?.Wind(RailLine.MainPath, s.LineHint) ?? 0;
        if (wind <= 0)
            return 0;
        double speed = t.MaxSpeed > 0 ? Math.Min(1, Math.Abs(train.RakeOf(s.Parent).Speed) / t.MaxSpeed) : 1;
        double push = wind * w.Drift * (w.Still + (1 - w.Still) * speed) * Gust(s.LineHint, w.GustMetres);
        if (!intent.Has(PlayerButtons.Run))
            push *= w.Walking;
        if (t.Composition is { Handrails: true } fit)
            push *= fit.Rails.Wind;
        return push;
    }

    /// <summary>
    /// The wind's gusts along the line (note 201): −1 (from the right) to +1 (from the left), a hash of each
    /// <paramref name="metres"/> of line, eased from one to the next. No trig and no dice, so a predicting client agrees.
    /// </summary>
    public static double Gust(double along, double metres)
    {
        if (metres <= 0)
            return 1;
        double x = along / metres, k = Math.Floor(x), f = x - k;
        f = f * f * (3 - 2 * f);
        double a = GustAt((long)k), b = GustAt((long)k + 1);
        return a + (b - a) * f;
    }

    static double GustAt(long k)
    {
        ulong h = unchecked((ulong)k * 0x9E3779B97F4A7C15UL);
        h ^= h >> 31;
        h = unchecked(h * 0xBF58476D1CE4E5B9UL);
        h ^= h >> 29;
        return (h >> 11) * (2.0 / (1UL << 53)) - 1;
    }

    /// <summary>Warm enough to recover: see <see cref="StepCold"/>.</summary>
    public static bool NearHeat(in PlayerState s, TrainOnLine train)
    {
        if (BesideStove(s, train))
            return true;
        int space = Space(s, train);
        if (space == Outside)
            return false;
        if (train.BoilerTuning is null)
            return true;
        return space == 0 ? train.Boiler.Firebox > 0 || train.Boiler.Pressure > 0 : train.Boiler.Pressure > 0;
    }

    /// <summary>
    /// In a crew car's walls with its stove (note 184): its own heat, steam or none. Shut in, the whole car's warm; with a door
    /// open, only near the stove (train.json <c>composition.stoveReach</c>), like standing at the cab's firebox.
    /// </summary>
    public static bool BesideStove(in PlayerState s, TrainOnLine train)
    {
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count || train.Frames[s.Parent].Shape.Stove is not { } stove || !Indoors(s, train))
            return false;
        // A breached car lets the cold in as an open door does (decided 1 Oct).
        if (train.Vehicles[s.Parent] is { DoorsOpen: 0, Breached: false })
            return true;
        double dx = s.Position.X - Math.Clamp(s.Position.X, stove.Min.X, stove.Max.X), dz = s.Position.Z - Math.Clamp(s.Position.Z, stove.Min.Z, stove.Max.Z);
        double reach = train.Dynamics.Tuning.Composition.StoveReach;
        return dx * dx + dz * dz <= reach * reach;
    }

    /// <summary>Past the onset of cold: slower, and the HUD says so. The revived reach it sooner (spec C.2).</summary>
    public static bool Chilled(in PlayerState s, PlayerTuning p) =>
        s.Cold >= p.Cold.OnsetSeconds;

    static Double3 WishDirection(double yaw, in PlayerIntent intent)
    {
        double x = Math.Clamp(intent.MoveX, -1, 1), z = Math.Clamp(intent.MoveZ, -1, 1);
        double len = Math.Sqrt(x * x + z * z);
        if (len > 1) { x /= len; z /= len; }
        var forward = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        var right = new Double3(DMath.Cos(yaw), 0, -DMath.Sin(yaw));
        return right * x + forward * z;
    }

    /// <summary>
    /// Pushes the player's cylinder out of every car body, wall, shut door and coupler plate nearby. A head
    /// that rises into something overhead (a car's ceiling) stops there instead of being shoved sideways.
    /// </summary>
    static Double3 Collide(Double3 world, TrainOnLine train, PlayerTuning p, out bool ceiling)
    {
        ceiling = false;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - world).Length > NearbyCar)
                continue;
            var local = frame.ToLocal(world);
            var vehicle = train.Vehicles[frame.Index];
            // The engine's running boards are only ever footing: a thin plate at deck height that whoever's down on the
            // ballast beside the engine passes under (up to the cab steps, along to the points), and nobody bumps into.
            foreach (var solid in frame.Shape.Solids)
                if (solid.Part != PartKind.RunningBoard && solid.Present(vehicle))
                    local = Ceiling(local, solid.Box, p, ref ceiling);
            foreach (var solid in frame.Shape.Solids)
                if (solid.Part != PartKind.RunningBoard && solid.Present(vehicle))
                    local = PushOut(local, solid.Box, p);
            foreach (var door in frame.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index))
                    local = PushOut(local, door.Box, p);
            world = frame.ToWorld(local);
        }
        // The stops' buildings (T114) and the fortresses' (T124): pushed out of each wall in its own frame. Out of one can
        // be into the next, in an inside corner or a doorway's jamb (note 326's open houses), so round again until nothing
        // moves (a few passes; the walls in their fixed order, so every machine settles the same).
        if (train.Walls is { } walls)
        {
            var near = walls.Near(world).ToList();
            for (int pass = 0; pass < 4; pass++)
            {
                var was = world;
                foreach (var w in near)
                    world = w.ToWorld(PushOut(w.ToLocal(world), w.Box, p));
                if ((world - was).Length < 1e-9)
                    break;
            }
        }
        return world;
    }

    /// <summary>
    /// Note 279: a step that would take someone up land steeper than <see cref="PlayerTuning.ClimbSlope"/> isn't climbed.
    /// What's left of it goes across the slope (along its contour) while that stays walkable, else nowhere. Down is free.
    /// </summary>
    static Double3 Walkable(Double3 to, Double3 from, ITrackConditions land, PlayerTuning p)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z, run = Math.Sqrt(dx * dx + dz * dz);
        if (run < 1e-6)
            return to;
        double here = land.Ground(from);
        if (land.Ground(to) - here <= p.ClimbSlope * run + 1e-4)
            return to;
        const double H = 0.25;
        double gx = (land.Ground(to with { X = to.X + H }) - land.Ground(to with { X = to.X - H })) / (2 * H);
        double gz = (land.Ground(to with { Z = to.Z + H }) - land.Ground(to with { Z = to.Z - H })) / (2 * H);
        double g = Math.Sqrt(gx * gx + gz * gz);
        if (g > 1e-9)
        {
            double ux = gx / g, uz = gz / g, up = dx * ux + dz * uz;
            if (up > 0)
                (dx, dz) = (dx - up * ux, dz - up * uz);
            var across = new Double3(from.X + dx, to.Y, from.Z + dz);
            double left = Math.Sqrt(dx * dx + dz * dz);
            if (left > 1e-6 && land.Ground(across) - here <= p.ClimbSlope * left + 1e-4)
                return across;
        }
        return new Double3(from.X, to.Y, from.Z);
    }

    /// <summary>Head up into the underside of a box whose footprint we're under: stop at it.</summary>
    static Double3 Ceiling(Double3 feet, Box box, PlayerTuning p, ref bool hit)
    {
        double head = feet.Y + p.Height;
        if (head <= box.Min.Y || feet.Y >= box.Min.Y || head - box.Min.Y > 0.5 || !box.ContainsXZ(feet))
            return feet;
        hit = true;
        return feet with { Y = box.Min.Y - p.Height };
    }

    static Double3 PushOut(Double3 feet, Box box, PlayerTuning p)
    {
        if (feet.Y >= box.Max.Y || feet.Y + p.Height <= box.Min.Y)
            return feet;
        // Close enough to the top to step (or land) onto it: support resolution handles that.
        if (box.Max.Y - feet.Y <= p.StepUp)
            return feet;

        double cx = Math.Clamp(feet.X, box.Min.X, box.Max.X), cz = Math.Clamp(feet.Z, box.Min.Z, box.Max.Z);
        double dx = feet.X - cx, dz = feet.Z - cz;
        double d2 = dx * dx + dz * dz;
        double r = p.Radius;
        if (d2 >= r * r)
            return feet;
        if (d2 > 1e-12)
        {
            double d = Math.Sqrt(d2);
            return feet with { X = cx + dx / d * r, Z = cz + dz / d * r };
        }
        // Centre inside the footprint: leave by the nearest face.
        double left = feet.X - box.Min.X, right = box.Max.X - feet.X, front = feet.Z - box.Min.Z, back = box.Max.Z - feet.Z;
        double min = Math.Min(Math.Min(left, right), Math.Min(front, back));
        if (min == left) return feet with { X = box.Min.X - r };
        if (min == right) return feet with { X = box.Max.X + r };
        if (min == front) return feet with { Z = box.Min.Z - r };
        return feet with { Z = box.Max.Z + r };
    }

    /// <summary>Finds what the player is standing on (if anything), re-parents, and applies landing rules.</summary>
    /// <summary>How far above someone the ground can be and still be what they step up onto (beyond a step or a fall).</summary>
    const double GroundLiftMargin = 0.5;

    static void UpdateSupport(ref PlayerState s, Double3 world, Double3 prevWorld, TrainOnLine train, PlayerTuning p, TrainTuning t)
    {
        bool wasGrounded = s.Grounded;
        var worldVelocity = WorldVelocity(s, train);
        double fall = Math.Max(0, prevWorld.Y - world.Y);
        double below = Math.Max(p.StepUp, fall + 0.01);
        double snap = wasGrounded ? GroundSnap : 0;

        // Ground is always a candidate if we're at or below it: nobody falls through the earth.
        var probe = s with { Position = world };
        double groundY = GroundHeight(ref probe, train.Line);
        s.LineHint = probe.LineHint;
        bool underGround = world.Y <= groundY + (wasGrounded ? p.StepUp : 0);
        // Ground a long way overhead is a bore or a cutting the terrain doesn't know about, not something to be lifted up
        // onto: under a train's surface it loses, and only catches whoever has nothing else underfoot (T66: a crew on an
        // alternate line's cutting stood up on the hill and was left behind).
        bool groundFar = groundY - world.Y > below + GroundLiftMargin;
        // T107: and riding the train, it's the train underfoot while any of it is: the land beside an alternate's climb read
        // a hand's breadth over the cab floor on deepTerritory:1, and the driver and fireman were set down on it at 13 m/s.
        // Off every surface of the train (stepped off its side), the earth catches them as before.
        bool riding = s.Parent != PlayerState.World;
        double bestTop = underGround && !groundFar && !riding ? groundY : double.NegativeInfinity;
        int bestParent = PlayerState.World;
        var bestSurface = Surface.Ground;
        Double3 bestLocal = default;

        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - world).Length > NearbyCar)
                continue;
            var local = frame.ToLocal(world);
            var vehicle = train.Vehicles[frame.Index];
            foreach (var solid in frame.Shape.Solids)
            {
                if (!solid.Present(vehicle))
                    continue;
                var box = solid.Box;
                double top = box.Max.Y;
                if (!box.ContainsXZ(local) || local.Y > top + snap || local.Y < top - below)
                    continue;
                double worldTop = frame.ToWorld(local with { Y = top }).Y;
                if (worldTop <= bestTop)
                    continue;
                bestTop = worldTop;
                bestParent = frame.Index;
                bestSurface = ToSurface(solid.Top);
                bestLocal = local with { Y = top };
            }
        }

        if (double.IsNegativeInfinity(bestTop) && underGround)
            bestTop = groundY; // nothing of the train underfoot either: the earth, however far up it is
        if (double.IsNegativeInfinity(bestTop))
        {
            // Nothing underfoot: fall, carrying whatever velocity the car gave us.
            s.Surface = Surface.Air;
            SetWorld(ref s, train, world, worldVelocity);
            return;
        }

        if (bestParent == PlayerState.World)
        {
            if (!wasGrounded)
                Land(ref s, worldVelocity, p, t);
            s.Parent = PlayerState.World;
            s.Surface = Surface.Ground;
            s.Position = world with { Y = groundY };
            s.Velocity = default;
            return;
        }

        var carFrame = train.Frames[bestParent];
        if (s.Parent != bestParent)
            s.Yaw += (s.Parent == PlayerState.World ? 0 : train.Frames[s.Parent].Heading) - carFrame.Heading;
        s.Parent = bestParent;
        s.Surface = bestSurface;
        s.Position = bestLocal;
        s.Velocity = default;
    }

    /// <summary>
    /// Spec B.3 landings: faster over the ground than <see cref="LandingTuning.LethalAbove"/> kills; slower, it's a knock that
    /// grows with the speed (T90: at the old 4 m/s threshold every step off a moving train was a death).
    /// </summary>
    static void Land(ref PlayerState s, Double3 worldVelocity, PlayerTuning p, TrainTuning t)
    {
        double horizontal = Math.Sqrt(worldVelocity.X * worldVelocity.X + worldVelocity.Z * worldVelocity.Z);
        var l = p.Landing;
        double lethal = l.LethalAbove > 0 ? l.LethalAbove : t.SpeedBands.JumpOffLethal;
        if (horizontal > lethal)
        {
            s.Health = 0;
            s.Death = DeathCause.JumpedAtSpeed;
        }
        else if (horizontal > l.RollAbove)
        {
            double k = Math.Clamp((horizontal - l.RollAbove) / Math.Max(1e-6, lethal - l.RollAbove), 0, 1);
            int damage = (int)Math.Round(l.RollDamage + (Math.Max(l.DamageAtLethal, l.RollDamage) - l.RollDamage) * k);
            s.Health = Math.Max(1, s.Health - damage);
        }
    }

    /// <param name="byHand">The reaching hand has to be on the ladder: from its foot to a grab iron over the top rung.</param>
    /// <param name="walkIn">Walking into it: close to its foot, facing up it, and not already at its top.</param>
    static void TryGrabLadder(ref PlayerState s, TrainOnLine train, PlayerTuning p, bool byHand, bool walkIn = false)
    {
        var world = ToWorld(s, train, s.Position);
        var worldVelocity = WorldVelocity(s, train);
        var hand = byHand ? HandWorld(s, train) : null;
        if (byHand && hand is null)
            return;
        foreach (var frame in train.Frames)
        {
            var local = frame.ToLocal(world);
            foreach (var ladder in frame.Shape.Ladders)
            {
                double dx = local.X - ladder.Foot.X, dz = local.Z - ladder.Foot.Z;
                double reach = walkIn ? Math.Min(WalkInReach, p.Ladder.GrabRange) : p.Ladder.GrabRange;
                if (dx * dx + dz * dz > reach * reach)
                    continue;
                if (local.Y < ladder.Foot.Y - 0.5 || local.Y > ladder.Top + 0.1)
                    continue;
                if (walkIn)
                {
                    // Not a hatch ladder indoors (the cab's, a car's): in there you walk past them all the time, working.
                    if (local.Y > ladder.Top - 0.5 || frame.Shape.Cab is { } cab && cab.Contains(ladder.Foot + new Double3(0, 0.2, 0))
                        || frame.Shape.Interior is { } room && room.Contains(ladder.Foot + new Double3(0, 0.2, 0)))
                        continue;
                    double yaw = WorldYaw(s, train);
                    var facing = frame.DirToLocal(new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw)));
                    if (facing.X * ladder.Inward.X + facing.Z * ladder.Inward.Z < 0.7)
                        continue;
                }
                if (hand is { } h && !OnLadder(frame.ToLocal(h), ladder, p.Hand.Grab))
                    continue;
                var relative = frame.VelocityToLocal(worldVelocity);
                if (Math.Sqrt(relative.X * relative.X + relative.Z * relative.Z) >= p.Ladder.GrabMaxRelativeSpeed)
                    continue;
                if (s.Parent != frame.Index)
                    s.Yaw += (s.Parent == PlayerState.World ? 0 : train.Frames[s.Parent].Heading) - frame.Heading;
                s.Parent = frame.Index;
                s.Surface = Surface.Ladder;
                s.Position = new Double3(ladder.Foot.X, Math.Clamp(local.Y, ladder.Foot.Y, ladder.Top - 0.05), ladder.Foot.Z);
                s.Velocity = default;
                return;
            }
        }
    }

    static void StepLadder(ref PlayerState s, in PlayerIntent intent, TrainOnLine train, PlayerTuning p, TrainTuning t, double dt)
    {
        var frame = train.Frames[s.Parent];
        var ladder = NearestLadder(frame.Shape, s.Position);
        var inward = ladder.Inward;
        if (intent.Has(PlayerButtons.Jump) || intent.Has(PlayerButtons.Use) && intent.MoveZ < -0.5)
        {
            // Let go: push off the car and fall.
            s.Velocity = inward * -1.5;
            s.Surface = Surface.Air;
            Reparent(ref s, train, PlayerState.World);
            return;
        }

        double top = ladder.Top;
        // D.9: the last one standing hauls a body up a ladder at a quarter of the pace.
        double climb = s.Has(PlayerFlags.SoloCarry) ? p.SoloBodyClimb : p.LadderClimb;
        double y = s.Position.Y + Math.Clamp(intent.MoveZ, -1, 1) * climb * dt;
        if (y >= top)
        {
            // Over the top onto whatever the ladder serves, just inside the edge: the highest footing at the top rung, not
            // whatever's overhead (the cab steps come up under the cab roof).
            var onto = s.Position + inward * (p.Radius + 0.45);
            var surface = frame.Shape.TopAt(onto.X, onto.Z, top + 0.05);
            s.Position = onto with { Y = surface?.Top ?? top };
            s.Surface = ToSurface(surface?.Kind ?? SurfaceKind.Roof);
            s.Velocity = default;
            return;
        }
        s.Position = s.Position with { Y = Math.Max(ladder.Foot.Y, y) };
        s.Velocity = default;
        if (y <= ladder.Foot.Y && ladder.Foot.Y > 0)
        {
            // A hatch ladder: step off onto the floor it stands on.
            var floor = frame.Shape.TopAt(s.Position.X, s.Position.Z, ladder.Foot.Y);
            s.Position = s.Position with { Y = ladder.Foot.Y };
            s.Surface = ToSurface(floor?.Kind ?? SurfaceKind.Deck);
            return;
        }
        if (y <= 0)
        {
            // Stepping off the bottom rung onto the ballast at whatever speed the train is doing.
            s.Surface = Surface.Air;
            var world = frame.ToWorld(s.Position);
            SetWorld(ref s, train, world, frame.Velocity);
            UpdateSupport(ref s, world, world, train, p, t);
        }
    }

    static bool OnLadder(Double3 hand, Ladder ladder, double grab)
    {
        double dx = hand.X - ladder.Foot.X, dz = hand.Z - ladder.Foot.Z;
        return dx * dx + dz * dz <= grab * grab && hand.Y >= ladder.Foot.Y && hand.Y <= ladder.Top + 0.4;
    }

    static Ladder NearestLadder(CarShape shape, Double3 p)
    {
        Ladder best = shape.Ladders[0];
        double bestD = double.MaxValue;
        foreach (var l in shape.Ladders)
        {
            double d = (l.Foot.X - p.X) * (l.Foot.X - p.X) + (l.Foot.Z - p.Z) * (l.Foot.Z - p.Z);
            if (d < bestD) { bestD = d; best = l; }
        }
        return best;
    }

    static Double3 ToWorld(in PlayerState s, TrainOnLine train, Double3 position) =>
        s.Parent == PlayerState.World ? position : train.Frames[s.Parent].ToWorld(position);

    public static Double3 WorldPosition(in PlayerState s, TrainOnLine train) => ToWorld(s, train, s.Position);

    public static Double3 WorldVelocity(in PlayerState s, TrainOnLine train) =>
        s.Parent == PlayerState.World ? s.Velocity : train.Frames[s.Parent].VelocityToWorld(s.Velocity);

    /// <summary>World-space yaw of the player's view, for cameras and audio.</summary>
    public static double WorldYaw(in PlayerState s, TrainOnLine train) =>
        s.Parent == PlayerState.World ? s.Yaw : s.Yaw + train.Frames[s.Parent].Heading;

    static void Reparent(ref PlayerState s, TrainOnLine train, int parent)
    {
        if (parent == s.Parent)
            return;
        var world = ToWorld(s, train, s.Position);
        var velocity = WorldVelocity(s, train);
        double yaw = WorldYaw(s, train);
        if (parent == PlayerState.World)
        {
            s.Parent = parent;
            s.Position = world;
            s.Velocity = velocity;
            s.Yaw = yaw;
            return;
        }
        var frame = train.Frames[parent];
        s.Parent = parent;
        s.Position = frame.ToLocal(world);
        s.Velocity = frame.VelocityToLocal(velocity);
        s.Yaw = yaw - frame.Heading;
    }

    /// <summary>
    /// Pulled off a car over its side (the Draggers, App. A.4): out past its edge and falling, with the train's speed and
    /// <paramref name="outward"/> on top. Spec B.3's one threshold decides it: faster than a survivable jump, that's death
    /// (the body goes over the side); slower, you land on the ballast and the train goes on without you. An authoritative
    /// move, so a predicting client adopts it (<see cref="PlayerState.Placed"/>).
    /// </summary>
    public static void PullOff(ref PlayerState s, TrainOnLine train, Double3 outward, TrainTuning t, DeathCause cause = DeathCause.Dragged)
    {
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return;
        var frame = train.Frames[s.Parent];
        var across = frame.DirToLocal(outward);
        var local = s.Position with { X = Math.Sign(across.X == 0 ? 1 : across.X) * (frame.Shape.HalfWidth + 0.4), Y = s.Position.Y - 0.3 };
        var carVelocity = frame.VelocityToWorld(Double3.Zero);
        SetWorld(ref s, train, frame.ToWorld(local), carVelocity + outward);
        s.Surface = Surface.Air;
        s.Placed++;
        if (SpeedBands.JumpOffIsLethal(t, Math.Sqrt(carVelocity.X * carVelocity.X + carVelocity.Z * carVelocity.Z)))
        {
            s.Health = 0;
            s.Death = cause;
        }
    }

    static void SetWorld(ref PlayerState s, TrainOnLine train, Double3 world, Double3 worldVelocity)
    {
        s.Yaw = WorldYaw(s, train);
        s.Parent = PlayerState.World;
        s.Position = world;
        s.Velocity = worldVelocity;
    }

    /// <summary>Height of the ground near a world point, refining a hint along the line (bodies use this too).</summary>
    /// <remarks>
    /// Beside the track the ground is at the height of the nearest track: the main line, or a branch off it. On a
    /// generated line the land beyond rises and falls with its terrain (linegen plan §12), which keeps the formation at
    /// that same rail height.
    /// </remarks>
    public static double GroundAt(Double3 world, RailLine line, ref double hint)
    {
        var (path, along) = line.Nearest(world, ref hint);
        return line.Conditions is { } c ? c.Ground(world) : line.Sample(path, along).Position.Y;
    }

    static double GroundHeight(ref PlayerState s, RailLine line) => GroundAt(s.Position, line, ref s.LineHint);
}
