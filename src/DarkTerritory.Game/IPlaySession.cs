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
public readonly record struct Crewmate(byte Id, Double3 Feet, double Yaw, bool Alive, Double3 Hand = default, Double3 Other = default, int? Looks = null,
    CrewPose? Act = null, Tool Holding = Tool.None)
{
    public int Variant => Looks ?? Id;
}

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
    /// <summary>GDD v1.4 App. E.9: the Stranded outro is playing (the run's end screen waits for it).</summary>
    bool StrandedOutro => false;
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
}

/// <summary>What the HUD shows about the connection (spec E: ping to host "shown prominently", non-optional).</summary>
/// <param name="PingMs">Round trip to the host; null for the host itself.</param>
/// <param name="JoinAt">Hosting for friends on the network: the address they type to join (T114 playtest: "how is she supposed to join if we're on the same wifi?").</param>
/// <param name="Listed">Hosting a public lobby: it's in the join screen's list (a private one is joined by invite or address).</param>
public readonly record struct LinkInfo(string Role, double? PingMs, int Aboard, string? Waiting, bool Lost, string? JoinAt = null, bool Listed = false);

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
