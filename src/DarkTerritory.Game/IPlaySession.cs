using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>Someone else on the train, where they are this frame.</summary>
/// <param name="Hand">A VR crewmate's reaching hand (T47), from the feet in the frame they face (x right, y up, z behind); zero for none.</param>
/// <param name="Other">Their other hand, the same way.</param>
/// <param name="Looks">Whose look they have (cap, scarf, tint, gait's beat), if not their own id's: the Passenger wears a crewmate's (T61).</param>
/// <param name="Act">What they're doing with their hands (<see cref="CrewActs"/>), or null: standing, walking or running, by how they move.</param>
/// <param name="Holding">The tool in their hand (T108's hotbar, <see cref="Kit.Held"/>).</param>
/// <param name="Reach">Where the act puts both hands (from the feet, in the facing frame like <paramref name="Hand"/>): the levers
/// a driver works, the whistle cord. Null to leave the hands to the clip (or a headset's).</param>
/// <param name="Lamp">They carry the hand lamp: it hangs from their fist and swings with it, its light with it (GDD §31).</param>
/// <param name="Survivor">Freed from a Holdout, whose figure they play as from then on (App. D.8).</param>
/// <param name="Stressed">Something's after the train close by them (an enemy past its dormant phase within
/// <see cref="CrewActs.StressRange"/>): their run is a hurried one (GDD §31).</param>
/// <param name="Health">Their health, for the stagger when it drops (App. C.2).</param>
/// <param name="Phase">How far through a timed act they are, in its clip's seconds (the cannon's reload: steps done plus this one's progress).</param>
/// <param name="Death">How they died, if they have: a burned body is drawn charred (spec C.1).</param>
/// <param name="Headset">A headset player's head and where they stand (T82): their body leans, crouches, twists and steps
/// under it. Null for a keyboard or a bot.</param>
/// <param name="Car">The car whose frame they stand in (their replicated <see cref="PlayerState.Parent"/>), or
/// <see cref="PlayerState.World"/> on the ground: their gait is paced over it, not over the ground it carries them across (note 211).</param>
/// <param name="Local">Their feet in that car's frame (<see cref="PlayerState.Position"/>); unused on the ground.</param>
public readonly record struct Crewmate(byte Id, Double3 Feet, double Yaw, bool Alive, Double3 Hand = default, Double3 Other = default, int? Looks = null,
    CrewPose? Act = null, Tool Holding = Tool.None, (Double3 A, Double3 B)? Reach = null, bool Lamp = false, Survivor Survivor = Survivor.None,
    bool Stressed = false, int Health = 0, double Phase = 0, DeathCause Death = DeathCause.None, int Car = PlayerState.World,
    Double3 Local = default, HeadsetBody? Headset = null)
{
    public int Variant => Looks ?? Id;
}

/// <summary>
/// What the snapshot says of a headset player's head (T82): its height over their feet (<see cref="PlayerState.Head"/>),
/// the look's pitch, and where they stand and face in the frame they're in (<paramref name="Parent"/>, a car or the
/// world), which the feet are planted in so the train moving under them moves nothing.
/// </summary>
/// <param name="Staged">A stride to draw instead of the one this machine has kept (a staged screenshot is one frame, so
/// a step under way is given, not lived).</param>
public readonly record struct HeadsetBody(double Head, double Pitch, int Parent, Double3 Local, double Yaw, VrStride? Staged = null);

/// <summary>You, for your own arms in view (X3, <see cref="CreatureArt.OwnArms"/>): which way you face and look, what you're
/// doing (<see cref="CrewActs.Of"/>), whether you're walking, how far into a swing you are (negative: not swinging), whose
/// look is yours, the tool in your hand.</summary>
public readonly record struct OwnView(float Yaw, float Pitch, CrewPose? Act, bool Moving, double Swing, int Variant, Tool Holding);

/// <summary>
/// A line of the crew roster (T69): who, where, and whether they're alive. <paramref name="Id"/> is the player id the line
/// is for; a Passenger's line has the id of the face it wears (App. A.7 "appears on the roster"), so it sits beside theirs,
/// and it is not <paramref name="Voiced"/>: the voice heard under that id is the real crewmate's, never the thing's.
/// </summary>
public readonly record struct RosterLine(byte Id, string Name, string Where, bool Alive, bool You = false, bool Voiced = true);

/// <summary>What the app plays: the single-player prototype, or a networked session (host or client).</summary>
public interface IPlaySession
{
    TrainOnLine Train { get; }
    World World { get; }
    /// <summary>The derailment's cinematic is playing (T117): the HUD holds the run's end back.</summary>
    bool WreckCinematic => false;
    /// <summary>Seconds since the train came off, as this client saw it.</summary>
    double WreckSeconds => 0;
    /// <summary>The derailment film (GDD v1.4 App. E), once this machine has shot it from the host's start; null till then.</summary>
    WreckFilm? Film => null;
    /// <summary>A vote to skip counts now (E.5: after the first player's shot; E.9: three seconds into the outro).</summary>
    bool Skippable => false;
    /// <summary>What derailed it, in the boards' km/h (T121): the host's own, or the incident report's line on a client.</summary>
    string? DerailCause => World.DerailCause is { Length: > 0 } c ? c
        : World.Run?.Report?.Lines.LastOrDefault(l => l.Kind == Sim.Run.IncidentKind.Derailed)?.Text;
    /// <summary>GDD v1.4 App. E.9: the Stranded outro is playing (the run's end screen waits for it).</summary>
    bool StrandedOutro => false;
    /// <summary>
    /// The fortress on the radio (GDD §9; note 178): the dispatcher's manifest as the train leaves the yard, or the clerk's
    /// tally at the terminus; null when nobody's on the air. <see cref="RadioSeconds"/> is how far into it.
    /// </summary>
    IReadOnlyList<string>? RadioReading => null;
    double RadioSeconds => 0;
    /// <summary>Each of <see cref="RadioReading"/>'s lines' turn when it's spoken (note 240); null, a line every lineSeconds.</summary>
    IReadOnlyList<double>? RadioTimes => null;
    /// <summary>The clerk's still reading the tally: the run's end screen waits for it.</summary>
    bool ClerkTally => false;
    /// <summary>This dead player's creature vote (GDD v1.4 App. D.11; note 180): the ballot offered and what they cast; null if none.</summary>
    (IReadOnlyList<Sim.Enemies.EnemyKind> Options, Sim.Enemies.EnemyKind? Cast)? Ballot => null;
    /// <summary>The ballot's picking on this machine (note 202): what's picked, and whether it's cast and on its way to the host.</summary>
    BallotPicker? Picker => null;
    /// <summary>The dead's cue showing now (D.11): "THE DEAD CALLED THE CAR HUGGER: PRIYA, SAM"; null when none is.</summary>
    string? VoteCue => null;
    /// <summary>The run-end commendation picker (D.12): who and which is picked, and whether it's given; null when there's none to give.</summary>
    (string To, string What, bool Given)? CommendPick => null;
    double OutroSeconds => 0;
    Sim.Route.Route? Route { get; }
    PlayerState Player { get; }
    TrainControls Controls { get; }
    long Tick { get; }
    /// <summary>
    /// The host's tick as this machine last heard it: what the sim's own timed records (a gun's last shot, World.Hits and
    /// Impacts, T121) are stamped with. Playing alone, the session's own.
    /// </summary>
    long HostTick => Tick;
    string Status();
    void Step(in PlayerIntent intent);
    IReadOnlyList<CarFrame> InterpolatedFrames(double alpha);
    Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch);
    /// <summary>Everyone else aboard, for drawing.</summary>
    IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha);
    PlayerTuning PlayerTuning { get; }
    /// <summary>This machine's player id in the world (bodies record who carries them).</summary>
    int PlayerId => 1;
    /// <summary>The network, for the HUD; null playing alone.</summary>
    LinkInfo? Link => null;
    /// <summary>The crew roster (T69), in player-id order: everyone aboard by the figures, a Passenger among them.</summary>
    IReadOnlyList<RosterLine> Roster() => [new RosterLine((byte)PlayerId, "YOU", PrototypeSession.Where(Player, Train), Player.Alive, You: true)];
    /// <summary>The living crewmate a dead or waiting player is watching (GDD App. D.10), or −1: nobody's left, or you're alive.</summary>
    int Watching => -1;
    /// <summary>Whose eyes and ears this machine has: the player's own or, watching, the crewmate's (their space, their shelter).</summary>
    PlayerState Viewpoint => Player;
    /// <summary>
    /// Everyone aboard as their states (your own as predicted, the rest as drawn, <paramref name="alpha"/> into the tick),
    /// for what's heard of them: footsteps, hands at work (GameAudio.CrewStates).
    /// </summary>
    IReadOnlyList<(int Id, PlayerState State)> CrewStates(double alpha) => [(PlayerId, Player)];
}

/// <summary>What the HUD shows about the connection (spec E: ping to host "shown prominently", non-optional).</summary>
/// <param name="PingMs">Round trip to the host; null for the host itself.</param>
/// <param name="JoinAt">Hosting for friends on the network: the address they type to join (T114 playtest: "how is she supposed to join if we're on the same wifi?").</param>
/// <param name="Listed">Hosting a public lobby: it's in the join screen's list (a private one is joined by invite or address).</param>
public readonly record struct LinkInfo(string Role, double? PingMs, int Aboard, string? Waiting, bool Lost, string? JoinAt = null, bool Listed = false)
{
    /// <summary>Lost, and trying to get back (note 253): this try of <see cref="Attempts"/>; 0 when not trying.</summary>
    public int Attempt { get; init; }
    public int Attempts { get; init; }
    /// <summary>Lost, the tries run out: RECONNECT (F5) tries again.</summary>
    public bool CanReconnect { get; init; }
    /// <summary>The crew cap (player.json crew.cap, note 254); 0 when there's none to speak of.</summary>
    public int Cap { get; init; }
    /// <summary>Hosting: the places taken against <see cref="Cap"/> (the crew, the waiting, the held); 0 for a joiner.</summary>
    public int Places { get; init; }
    /// <summary>Hosting, with no room: the lobby's shut and joiners are turned away.</summary>
    public bool Full => Cap > 0 && Places >= Cap;
    /// <summary>Lost, and turned away on the way back (note 254): what the host said, "CREW FULL (8/8)".</summary>
    public string? Refused { get; init; }
}

/// <summary>First-person eye from a player's state, interpolated in their own frame so riding a car at speed is smooth.</summary>
public static class Eyes
{
    /// <summary>How far over the feet the eyes are, alive. A headset's tracking space hangs from here (<see cref="VrLocomotion"/>).</summary>
    public const double Height = 1.65;

    /// <summary>In the gun's seat (T112): the eyes at the shield's aiming slot (the seat 0.48 m over the roof, note 137).</summary>
    public const double Seated = 1.26;

    /// <summary>
    /// Up in the crane's cab while at its controls (T48): looking along the gantry at the bridge and trolley, not down at the
    /// hook. Spec D.2: "the crane operator cannot see the ground crew", who have to call the position.
    /// </summary>
    public static Camera? Operator(in PlayerState s, Sim.World world)
    {
        if (!s.Has(PlayerFlags.Operating) || world.Run?.CurrentSite?.CraneNear(Sim.Player.PlayerMotor.WorldPosition(s, world.Train)) is not { } crane)
            return null;
        var trolley = crane.HookAt with { Y = crane.BridgeEnd(0).Y - 0.6 };
        return Camera.LookAt(crane.Cab + Double3.Up * 0.3, trolley, 55);
    }

    public static Camera From(in PlayerState cur, in PlayerState prev, IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch)
    {
        var local = prev.Parent == cur.Parent ? Double3.Lerp(prev.Position, cur.Position, alpha) : cur.Position;
        var eyeLocal = local + Double3.Up * (!cur.Alive ? 0.3 : cur.Has(PlayerFlags.Seated) ? Seated : Height);
        bool onCar = cur.Parent != PlayerState.World && cur.Parent < frames.Count;
        var eye = onCar ? frames[cur.Parent].ToWorld(eyeLocal) : eyeLocal;
        double heading = onCar ? frames[cur.Parent].Heading : 0;
        return new Camera
        {
            Position = eye,
            Yaw = cur.Yaw + pendingYaw + heading,
            Pitch = Math.Clamp(cur.Pitch + pendingPitch, -1.5, 1.5),
            FovYDegrees = 75,
            Near = 0.05f,
            Far = 2000,
        };
    }

    /// <summary>The world heading of the frame a player's state is in (0 for the ground).</summary>
    public static double Heading(in PlayerState s, IReadOnlyList<CarFrame> frames) =>
        s.Parent != PlayerState.World && s.Parent < frames.Count ? frames[s.Parent].Heading : 0;

    /// <summary>A player's feet and facing in world space.</summary>
    public static (Double3 Feet, double Yaw) World(in PlayerState s, IReadOnlyList<CarFrame> frames)
    {
        bool onCar = s.Parent != PlayerState.World && s.Parent < frames.Count;
        return onCar ? (frames[s.Parent].ToWorld(s.Position), s.Yaw + frames[s.Parent].Heading) : (s.Position, s.Yaw);
    }
}
