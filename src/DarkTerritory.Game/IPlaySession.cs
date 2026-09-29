using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>Someone else on the train, where they are this frame.</summary>
public readonly record struct Crewmate(byte Id, Double3 Feet, double Yaw, bool Alive);

/// <summary>What the app plays: the single-player prototype, or a networked session (host or client).</summary>
public interface IPlaySession
{
    TrainOnLine Train { get; }
    World World { get; }
    Sim.Route.Route? Route { get; }
    PlayerState Player { get; }
    TrainControls Controls { get; }
    long Tick { get; }
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
}

/// <summary>What the HUD shows about the connection (spec E: ping to host "shown prominently", non-optional).</summary>
/// <param name="PingMs">Round trip to the host; null for the host itself.</param>
public readonly record struct LinkInfo(string Role, double? PingMs, int Aboard, string? Waiting, bool Lost);

/// <summary>First-person eye from a player's state, interpolated in their own frame so riding a car at speed is smooth.</summary>
public static class Eyes
{
    /// <summary>How far over the feet the eyes are, alive. A headset's tracking space hangs from here (<see cref="VrLocomotion"/>).</summary>
    public const double Height = 1.65;

    public static Camera From(in PlayerState cur, in PlayerState prev, IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch)
    {
        var local = prev.Parent == cur.Parent ? Double3.Lerp(prev.Position, cur.Position, alpha) : cur.Position;
        var eyeLocal = local + Double3.Up * (cur.Alive ? Height : 0.3);
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
