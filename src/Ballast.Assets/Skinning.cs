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
    Vertex[] _verts = [];

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
            if (_verts.Length < count)
                _verts = new Vertex[count];
            var look = looks[part.Material];
            float wear = look.Emissive >= 1 ? 0 : look.Wear;
            var joints = part.Joints;
            var weights = part.Weights;
            // Each vertex skinned once into a finished Vertex; the triangles then copy them by index. (Building the
            // vertex per corner instead costs three times as much: most vertices are shared by several triangles.)
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
                var nn = Vector3.TransformNormal(part.Normals[i], m);
                float len = nn.Length();
                _verts[i] = new Vertex(Vector3.Transform(part.Positions[i], m), len > 1e-12f ? nn / len : Vector3.UnitY, look.Colour, look.Emissive)
                {
                    Surface = (part.Positions[i] + settings.Seed) * settings.TexelsPerMetre,
                    Wear = wear,
                    Shine = look.Shine,
                    Uv = part.Uvs[i],
                    Layer = look.Layer,
                    Layer2 = -1,
                    Blend = 0,
                };
            }
            var idx = part.Indices;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                mesh.Add(in _verts[idx[t]]);
                mesh.Add(in _verts[idx[t + 1]]);
                mesh.Add(in _verts[idx[t + 2]]);
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

    // ----------------------------------------------------------------------------------------------------------------
    // Driving a pose from outside (a ragdoll's particles): move it whole, then aim bones one at a time, parents first.

    /// <summary>Applies <paramref name="transform"/> to every bone of the pose (after it: model space becomes its target).</summary>
    public static void Place(Model model, Pose pose, in Matrix4x4 transform)
    {
        for (int b = 0; b < model.Skeleton.Count; b++)
            pose.World[b] *= transform;
        Reskin(model, pose);
    }

    /// <summary>
    /// Turns <paramref name="bone"/> about its head, carrying everything below it, so the head of <paramref name="toward"/>
    /// (a bone further down its chain) lies in the direction of <paramref name="target"/>. A swing only: the bone keeps
    /// the twist it inherited. Aim parents before children, or a child's aim is undone by its parent's.
    /// </summary>
    public static void Aim(Model model, Pose pose, int bone, int toward, Vector3 target)
    {
        var sk = model.Skeleton;
        var head = pose.World[bone].Translation;
        var from = pose.World[toward].Translation - head;
        var to = target - head;
        if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f)
            return;
        var swing = Matrix4x4.CreateTranslation(-head) * Matrix4x4.CreateFromQuaternion(Between(Vector3.Normalize(from), Vector3.Normalize(to)))
            * Matrix4x4.CreateTranslation(head);
        for (int b = 0; b < sk.Count; b++)
            if (Below(sk, b, bone))
            {
                pose.World[b] *= swing;
                pose.Skin[b] = sk.InverseBind[b] * pose.World[b];
            }
    }

    /// <summary>The shortest rotation taking unit <paramref name="a"/> to unit <paramref name="b"/>.</summary>
    public static Quaternion Between(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.999999f)
            return Quaternion.Identity;
        if (d < -0.999999f)
        {
            // Opposite: half a turn about any axis square to them.
            var axis = Vector3.Cross(MathF.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c, 1 + d));
    }

    static bool Below(Skeleton sk, int bone, int ancestor)
    {
        for (int b = bone; b >= 0; b = sk.Parents[b])
            if (b == ancestor)
                return true;
        return false;
    }

    static void Reskin(Model model, Pose pose)
    {
        var sk = model.Skeleton;
        for (int b = 0; b < sk.Count; b++)
            pose.Skin[b] = sk.InverseBind[b] * pose.World[b];
    }
}
