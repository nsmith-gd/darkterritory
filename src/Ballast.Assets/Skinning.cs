using System.Numerics;
using Ballast.Render;

namespace Ballast.Assets;

/// <summary>A skeleton's pose: each bone's model-space transform and its skinning matrix (inverse bind × world).</summary>
public sealed class Pose
{
    public Pose(int bones)
    {
        World = new Matrix4x4[bones];
        Skin = new Matrix4x4[bones];
    }

    public Matrix4x4[] World { get; }
    public Matrix4x4[] Skin { get; }
}

/// <summary>How one of a model's materials is drawn this time: its texture layer, tint and light.</summary>
/// <param name="Layer">Texture layer, or −1 for flat colour.</param>
/// <param name="Colour">Multiplies the texture (one), or is the colour when untextured.</param>
/// <param name="Emissive">0 lit, 1 a light source (see <see cref="Vertex.Emissive"/>).</param>
public readonly record struct MaterialLook(int Layer, Vector3 Colour, float Emissive, float Shine, float Wear);

/// <summary>Per-draw settings for <see cref="Skinner.Emit"/>.</summary>
/// <param name="Variant">Which variant's parts to draw (hats, scarves).</param>
/// <param name="Clip">The clip playing, for clip-only props.</param>
/// <param name="TexelsPerMetre">The look's grain density: <see cref="Vertex.Surface"/> is bind position × this.</param>
/// <param name="Seed">Shifts the grime pattern so two of the same thing don't wear identically (metres).</param>
public readonly record struct EmitSettings(int Variant = 0, string? Clip = null, float TexelsPerMetre = 128, Vector3 Seed = default);

/// <summary>
/// CPU skinning (linear blend, ≤ 4 bones a vertex) into the renderer's triangle soup. One per drawing thread: it keeps
/// its scratch buffers between calls, so a frame of a dozen creatures allocates nothing. Deterministic: plain float math
/// in a fixed order, nothing from the clock.
/// </summary>
public sealed class Skinner
{
    Matrix4x4[] _local = [];
    Matrix4x4[] _combined = [];
    Vector3[] _pos = [];
    Vector3[] _nor = [];

    /// <summary>Allocating convenience: the skinning matrices for <paramref name="clip"/> at <paramref name="time"/>.</summary>
    public static Matrix4x4[] Skin(Model model, string clip, double time, bool loop)
    {
        var pose = new Pose(model.Skeleton.Count);
        new Skinner().Evaluate(model, model.Clip(clip), time, loop, pose);
        return pose.Skin;
    }

    /// <summary>The frame of <paramref name="clip"/> shown at <paramref name="time"/> seconds, as a fraction of frames.</summary>
    public static double FrameAt(AnimationClip clip, double time, bool loop)
    {
        double f = time * clip.Fps;
        int span = clip.Frames - 1;
        if (span <= 0)
            return 0;
        if (loop)
        {
            f %= span;
            if (f < 0)
                f += span;
        }
        return Math.Clamp(f, 0, span);
    }

    /// <summary>
    /// Poses the skeleton. <paramref name="stepped"/> (the default) holds each 30 fps frame until the next, as the era's
    /// games did, rather than blending between them: GDD §31's "a bit stiff", and the monsters' pops stay pops.
    /// A null clip is the bind pose.
    /// </summary>
    public void Evaluate(Model model, AnimationClip? clip, double time, bool loop, Pose into, bool stepped = true)
    {
        var sk = model.Skeleton;
        int n = sk.Count;
        if (_local.Length < n)
            _local = new Matrix4x4[n];
        if (clip is null)
        {
            for (int b = 0; b < n; b++)
                _local[b] = ModelLoader.Compose(sk.RestTranslation[b], sk.RestRotation[b], sk.RestScale[b]);
        }
        else
        {
            double f = FrameAt(clip, time, loop);
            int f0 = (int)Math.Floor(f + 1e-6);
            if (f0 >= clip.Frames - 1)
                f0 = loop ? 0 : clip.Frames - 1;
            int f1 = Math.Min(f0 + 1, clip.Frames - 1);
            float u = stepped ? 0 : (float)Math.Clamp(f - f0, 0, 1);
            for (int b = 0; b < n; b++)
            {
                int a = f0 * n + b;
                if (u <= 0)
                    _local[b] = ModelLoader.Compose(clip.Translation[a], clip.Rotation[a], clip.Scale[a]);
                else
                {
                    int c = f1 * n + b;
                    _local[b] = ModelLoader.Compose(Vector3.Lerp(clip.Translation[a], clip.Translation[c], u),
                        Quaternion.Slerp(clip.Rotation[a], clip.Rotation[c], u), Vector3.Lerp(clip.Scale[a], clip.Scale[c], u));
                }
            }
        }
        for (int b = 0; b < n; b++)
        {
            int p = sk.Parents[b];
            into.World[b] = p < 0 ? _local[b] * sk.Outer[b] : _local[b] * sk.Outer[b] * into.World[p];
            into.Skin[b] = sk.InverseBind[b] * into.World[b];
        }
    }

    /// <summary>
    /// Appends the posed model's triangles to <paramref name="mesh"/>, placed by <paramref name="model"/> (object to
    /// camera-relative). <paramref name="looks"/> is per material (<see cref="Model.Materials"/>). Returns triangles added.
    /// </summary>
    public int Emit(MeshBuilder mesh, Model source, Pose pose, in Matrix4x4 model, ReadOnlySpan<MaterialLook> looks, in EmitSettings settings)
    {
        int bones = source.Skeleton.Count;
        if (_combined.Length < bones)
            _combined = new Matrix4x4[bones];
        for (int b = 0; b < bones; b++)
            _combined[b] = pose.Skin[b] * model;
        int added = 0;
        foreach (var part in source.Parts)
        {
            if (!part.DrawnFor(settings.Variant, settings.Clip))
                continue;
            int count = part.Positions.Length;
            if (_pos.Length < count)
            {
                _pos = new Vector3[count];
                _nor = new Vector3[count];
            }
            var joints = part.Joints;
            var weights = part.Weights;
            for (int i = 0; i < count; i++)
            {
                int k = i * 4;
                Matrix4x4 m;
                if (weights[k] >= 0.9999f)
                    m = _combined[joints[k]];
                else
                {
                    m = _combined[joints[k]] * weights[k];
                    for (int j = 1; j < 4; j++)
                        if (weights[k + j] > 0)
                            m += _combined[joints[k + j]] * weights[k + j];
                }
                _pos[i] = Vector3.Transform(part.Positions[i], m);
                var nn = Vector3.TransformNormal(part.Normals[i], m);
                float len = nn.Length();
                _nor[i] = len > 1e-12f ? nn / len : Vector3.UnitY;
            }
            var look = looks[part.Material];
            float wear = look.Emissive >= 1 ? 0 : look.Wear;
            var idx = part.Indices;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                for (int c = 0; c < 3; c++)
                {
                    int i = idx[t + c];
                    mesh.Add(new Vertex(_pos[i], _nor[i], look.Colour, look.Emissive)
                    {
                        Surface = (part.Positions[i] + settings.Seed) * settings.TexelsPerMetre,
                        Wear = wear,
                        Shine = look.Shine,
                        Uv = part.Uvs[i],
                        Layer = look.Layer,
                        Layer2 = -1,
                        Blend = 0,
                    });
                }
                added++;
            }
        }
        return added;
    }

    /// <summary>A bone's model-space transform in a pose (a socket: where the hand holds a thing), placed by <paramref name="model"/>.</summary>
    public static Matrix4x4 Socket(Model source, Pose pose, string bone, in Matrix4x4 model)
    {
        int b = source.Skeleton.IndexOf(bone);
        if (b < 0)
            throw new KeyNotFoundException($"{source.Name} has no bone '{bone}'");
        return pose.World[b] * model;
    }
}
