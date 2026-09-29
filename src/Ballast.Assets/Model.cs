using System.Numerics;

namespace Ballast.Assets;

/// <summary>
/// A material as the model names it. The texture is the name up to its first dot (<c>crew_atlas.coat</c> wears
/// <c>crew_atlas</c>; the rest only tells parts apart), resolved to a texture layer by whoever draws it.
/// </summary>
/// <param name="BaseColour">Linear flat colour, for when the texture isn't there.</param>
/// <param name="Emissive">A pure light (a lamp's glass, an eye): drawn at full colour, textured or not.</param>
/// <param name="Glow">Emissive only without its texture (an ember crack's texture carries its own emissive mask).</param>
/// <param name="Tint">Multiplies the texture when it's there (flesh darkened to a railway tie's brown).</param>
public sealed record ModelMaterial(string Name, string Texture, Vector3 BaseColour, float Shine, float Emissive, float Glow, Vector3 Tint);

/// <summary>
/// One primitive of the model: indexed triangles with one material, skinned to the skeleton by up to four bones a
/// vertex. Positions are in the bind pose, model space (+X right, +Y up, −Z forward, metres, the floor at y = 0).
/// </summary>
public sealed class MeshPart
{
    public required string Name { get; init; }
    public required int Material { get; init; }
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Vector2[] Uvs { get; init; }
    /// <summary>Four skeleton bone indices per vertex (0 where the weight is 0).</summary>
    public required int[] Joints { get; init; }
    /// <summary>Four weights per vertex, summing to one.</summary>
    public required float[] Weights { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Which variants draw it, one bit each (all bits: every variant).</summary>
    public ulong Variants { get; init; } = ulong.MaxValue;
    /// <summary>Drawn only while this clip plays (a prop: the fireman's shovel), or always when null.</summary>
    public string? Clip { get; init; }
    public int Triangles => Indices.Length / 3;

    public bool DrawnFor(int variant, string? clip) =>
        (Variants >> (variant & 63) & 1) != 0 && (Clip is null || Clip == clip);
}

/// <summary>The bones: hierarchy (parents before children), rest pose and inverse bind matrices.</summary>
public sealed class Skeleton
{
    public required string[] Names { get; init; }
    /// <summary>Each bone's parent's index, or −1. Parents always come before their children.</summary>
    public required int[] Parents { get; init; }
    public required Vector3[] RestTranslation { get; init; }
    public required Quaternion[] RestRotation { get; init; }
    public required Vector3[] RestScale { get; init; }
    /// <summary>Model space to each bone's space at bind.</summary>
    public required Matrix4x4[] InverseBind { get; init; }
    /// <summary>For a root bone, the transform of the nodes above it (the armature); identity otherwise.</summary>
    public required Matrix4x4[] Outer { get; init; }
    /// <summary>Bones no vertex is weighted to: attachment points (hand_r_weapon, head_hat, ...).</summary>
    public required bool[] Socket { get; init; }

    public int Count => Names.Length;

    public int IndexOf(string name) => System.Array.IndexOf(Names, name);
}

/// <summary>
/// A clip resampled to a fixed rate (30 fps, the era's: GDD §31's stiff keyframed motion): every bone's local
/// translation, rotation and scale at every frame. A loop's last frame repeats its first.
/// </summary>
public sealed class AnimationClip
{
    public required string Name { get; init; }
    public required int Frames { get; init; }
    public required float Fps { get; init; }
    public required int Bones { get; init; }
    /// <summary>[frame * Bones + bone].</summary>
    public required Vector3[] Translation { get; init; }
    public required Quaternion[] Rotation { get; init; }
    public required Vector3[] Scale { get; init; }

    /// <summary>Seconds from the first frame to the last.</summary>
    public double Duration => (Frames - 1) / (double)Fps;

    /// <summary>True when the last frame matches the first: the clip loops without a pop.</summary>
    public bool Loops
    {
        get
        {
            if (Frames < 2)
                return false;
            int last = (Frames - 1) * Bones;
            for (int b = 0; b < Bones; b++)
            {
                if (Vector3.Distance(Translation[b], Translation[last + b]) > 1e-3f || Vector3.Distance(Scale[b], Scale[last + b]) > 1e-3f)
                    return false;
                if (MathF.Abs(Quaternion.Dot(Rotation[b], Rotation[last + b])) < 0.99995f)
                    return false;
            }
            return true;
        }
    }
}

/// <summary>A skinned model: its parts, materials, skeleton and clips (glTF animations by name).</summary>
public sealed class Model
{
    public required string Name { get; init; }
    public required MeshPart[] Parts { get; init; }
    public required ModelMaterial[] Materials { get; init; }
    public required Skeleton Skeleton { get; init; }
    public required IReadOnlyDictionary<string, AnimationClip> Clips { get; init; }
    /// <summary>Clip names in file order.</summary>
    public required string[] ClipNames { get; init; }
    /// <summary>How many variants its parts name (1 when none do).</summary>
    public required int VariantCount { get; init; }
    /// <summary>Bind-pose bounds.</summary>
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }

    /// <summary>Triangles drawn for a variant (without clip-only props).</summary>
    public int Triangles(int variant = 0) => Parts.Where(p => p.DrawnFor(variant, null)).Sum(p => p.Triangles);

    /// <summary>Every triangle in the file, all variants and props.</summary>
    public int AllTriangles => Parts.Sum(p => p.Triangles);

    public AnimationClip Clip(string name) =>
        Clips.TryGetValue(name, out var c) ? c : throw new KeyNotFoundException($"{Name} has no clip '{name}' (has {string.Join(", ", ClipNames)})");
}
