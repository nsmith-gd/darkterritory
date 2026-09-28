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

    public readonly Vector3 Forward => new((float)(-Math.Sin(Yaw) * Math.Cos(Pitch)), (float)Math.Sin(Pitch), (float)(-Math.Cos(Yaw) * Math.Cos(Pitch)));

    /// <summary>View-projection for camera-relative positions, in Vulkan clip space (Y down, depth 0..1).</summary>
    public readonly Matrix4x4 ViewProjection(float aspect)
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, Forward, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FovYDegrees * MathF.PI / 180, aspect, Near, Far);
        proj.M22 *= -1;
        return view * proj;
    }
}

/// <summary>Per-frame lighting and atmosphere. Defaults are the cold, fogged night of the art sheet.</summary>
public struct FrameLighting
{
    public Vector3 FogColor;
    public float FogDensity;
    public Vector3 MoonDirection;
    public float Ambient;
    public Double3 LampPosition;
    public Vector3 LampDirection;
    public float LampRange;
    public float LampConeDegrees;

    public static FrameLighting Night => new()
    {
        FogColor = new Vector3(0.075f, 0.080f, 0.092f),
        FogDensity = 0.016f,
        MoonDirection = Vector3.Normalize(new Vector3(-0.3f, 0.6f, 0.4f)),
        Ambient = 0.09f,
        LampRange = 120,
        LampConeDegrees = 18,
        LampDirection = -Vector3.UnitZ,
    };
}
