using System.Numerics;

namespace Ballast.Render;

/// <summary>
/// A first-person camera. The renderer draws everything relative to <see cref="Position"/>
/// (floating origin at the eye), so the view matrix only ever rotates.
/// </summary>
public struct Camera
{
    public Double3 Position;
    /// <summary>Radians; 0 looks along world −Z, positive turns left.</summary>
    public double Yaw;
    /// <summary>Radians; positive looks up.</summary>
    public double Pitch;
    public float FovYDegrees;
    public float Near;
    public float Far;
    /// <summary>A headset eye's whole orientation, used instead of <see cref="Yaw"/> and <see cref="Pitch"/> when set.</summary>
    public Quaternion? Orientation;
    /// <summary>A headset eye's asymmetric field of view, used instead of <see cref="FovYDegrees"/> when set.</summary>
    public EyeFov? Fov;
    /// <summary>
    /// Where a headset eye is from <see cref="Position"/>, which stays the scene's origin (the mesh is built around
    /// it), so both eyes share one mesh and only the view moves by the few centimetres between them.
    /// </summary>
    public Vector3 EyeOffset;

    public static Camera LookAt(Double3 eye, Double3 target, float fovY = 70)
    {
        var d = (target - eye).Normalized;
        return new Camera
        {
            Position = eye,
            Yaw = Math.Atan2(-d.X, -d.Z),
            Pitch = Math.Asin(Math.Clamp(d.Y, -1, 1)),
            FovYDegrees = fovY,
            Near = 0.1f,
            Far = 2000f,
        };
    }

    public readonly Vector3 Forward => Orientation is { } q
        ? Vector3.Transform(-Vector3.UnitZ, q)
        : new((float)(-Math.Sin(Yaw) * Math.Cos(Pitch)), (float)Math.Sin(Pitch), (float)(-Math.Cos(Yaw) * Math.Cos(Pitch)));

    /// <summary>View-projection for camera-relative positions, in Vulkan clip space (Y down, depth 0..1).</summary>
    public readonly Matrix4x4 ViewProjection(float aspect)
    {
        var view = Orientation is { } q
            ? Matrix4x4.CreateTranslation(-EyeOffset) * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(q))
            : Matrix4x4.CreateLookAt(EyeOffset, EyeOffset + Forward, Vector3.UnitY);
        return view * Projection(aspect);
    }

    /// <summary>The projection alone, in Vulkan's clip space (the screen-space passes rebuild view positions from depth by it).</summary>
    public readonly Matrix4x4 Projection(float aspect)
    {
        var proj = Fov is { } f
            ? Matrix4x4.CreatePerspectiveOffCenter(Near * MathF.Tan(f.Left), Near * MathF.Tan(f.Right), Near * MathF.Tan(f.Down), Near * MathF.Tan(f.Up), Near, Far)
            : Matrix4x4.CreatePerspectiveFieldOfView(FovYDegrees * MathF.PI / 180, aspect, Near, Far);
        // Vulkan's clip-space Y points down: flip every term that feeds it (only M22 when the frustum is centred).
        proj.M22 *= -1;
        proj.M32 *= -1;
        return proj;
    }
}

/// <summary>
/// An asymmetric frustum as OpenXR gives it: the angles (radians) of each edge from straight ahead,
/// left and down negative. A headset eye's view is off-centre towards the nose.
/// </summary>
public readonly record struct EyeFov(float Left, float Right, float Up, float Down);

/// <summary>Per-frame lighting and atmosphere. Defaults are the cold, fogged night of the art sheet.</summary>
public struct FrameLighting
{
    public Vector3 FogColor;
    public float FogDensity;
    /// <summary>World height the fog is thickest at and below (the ground under the eye when NaN).</summary>
    public double FogBase;
    /// <summary>How fast the fog thins going up, per metre (0: the same everywhere).</summary>
    public float FogHeightFalloff;
    /// <summary>How much of the fog is left however high you go.</summary>
    public float FogFloor;
    /// <summary>
    /// The fog's shape: 1 is plain exponential; above 1 it's clearer near and thicker far, crossing plain exponential at
    /// the 1/e distance (1 / <see cref="FogDensity"/>), so the weather's visibility stays where it was.
    /// </summary>
    public float FogCurve;
    public Vector3 MoonDirection;
    public Vector3 MoonColour;
    public float MoonStrength;
    public float Ambient;
    public Double3 LampPosition;
    public Vector3 LampDirection;
    public float LampRange;
    public float LampConeDegrees;
    public Vector3 LampColour;
    public float LampIntensity;
    /// <summary>How wet everything is, 0..1: rain darkens surfaces and puts a sheen on what faces the sky.</summary>
    public float Wetness;
    /// <summary>How hard the frost is, 0..1: a pale rime on what's outdoors, heaviest on what faces the sky.</summary>
    public float Frost;
    /// <summary>Seconds, for what drifts (clouds, grain). Screenshots keep it fixed so they're repeatable.</summary>
    public double Time;

    public static FrameLighting Night => new()
    {
        FogColor = new Vector3(0.075f, 0.080f, 0.092f),
        FogDensity = 0.016f,
        FogBase = double.NaN,
        FogFloor = 1,
        FogCurve = 1,
        MoonDirection = Vector3.Normalize(new Vector3(-0.3f, 0.6f, 0.4f)),
        MoonColour = new Vector3(0.55f, 0.62f, 0.78f),
        MoonStrength = 0.6f,
        Ambient = 0.09f,
        LampRange = 120,
        LampConeDegrees = 18,
        LampDirection = -Vector3.UnitZ,
        LampColour = new Vector3(1.0f, 0.72f, 0.38f),
        LampIntensity = 3,
    };
}
