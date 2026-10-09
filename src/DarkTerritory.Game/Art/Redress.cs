using System.Numerics;
using Ballast.Assets;

namespace DarkTerritory.Game.Art;

/// <summary>What a re-dressed figure wears where: each a region of the survivors' figure, found by the bone it moves with.</summary>
public enum Cloth
{
    /// <summary>The head (the scan's face and hair).</summary>
    Face,
    Hands,
    Forearms,
    /// <summary>The upper arms: a tee's short sleeves, a long sleeve's top half, or bare.</summary>
    Sleeves,
    /// <summary>The chest, back, shoulders and belly, down to the waist.</summary>
    Shirt,
    Trousers,
    Feet,
}

/// <summary>
/// A cloth's colour and what it's made of: a texture layer tinted by it ("" for the flat colour), or null for the figure's
/// own atlas tinted (a face fairer or more weathered, keeping its features).
/// </summary>
public sealed record Dye(string? Texture, Vector3 Colour, float Shine = 0.05f);

/// <summary>
/// Something that grows on the face or head (a beard, short hair): the head's own triangles where <see cref="On"/> holds
/// (their middle, in the bind pose), copied <see cref="Out"/> metres out along their normals and dyed, so it lies on the
/// face it grows from and moves with it.
/// </summary>
public sealed record Growth(string Name, Func<Vector3, bool> On, float Out, Dye Dye);

/// <summary>
/// The survivors' figure (tools/models/recipes/survivor_prisoner.py: the crew figure in penal greys) dressed as somebody
/// else, without a new model (this container builds none: Blender isn't here): the long coat, the scarf and the chest lamp
/// left off, and what's under them, the figure's frame, cut by the bone each triangle moves with into <see cref="Cloth"/>
/// regions, each in its own <see cref="Dye"/>. Same rig and clips, so it's posed, worn on (<c>CreatureArt.Wear</c>) and
/// drawn as the survivors are. The figures met out in the Territory (GDD §3.2: Dave, Nicki, Jacob) are dressed this way;
/// the director's photographs of the people they're for are what they're dressed after.
/// </summary>
public static class Redress
{
    /// <summary>Where the waist is (m): the pelvis's triangles above it are the shirt's, below it the trousers'.</summary>
    const float Waist = 0.94f;
    /// <summary>The satchel at the right hip (what's out past this below the arms, m): left off with the coat.</summary>
    const float Satchel = 0.2f;
    /// <summary>The middle of the survivors' head in its bind pose (m): what grows on it grows out from here.</summary>
    static readonly Vector3 HeadMiddle = new(0, 1.64f, -0.015f);

    /// <param name="figure">The survivors' figure as loaded.</param>
    /// <param name="name">What to call the dressed one.</param>
    /// <param name="dyes">A dye for each cloth to colour; one not given keeps the figure's own atlas there (its face, its hands).</param>
    /// <param name="shape">Moves the bind pose's vertices before they're cut (a slimmer waist, narrower shoulders), or null.</param>
    /// <param name="growths">Beards and hair grown from the head's own surface, or null.</param>
    public static Model Of(Model figure, string name, IReadOnlyDictionary<Cloth, Dye> dyes, Func<Vector3, string, Vector3>? shape = null,
        IReadOnlyList<Growth>? growths = null)
    {
        var bones = figure.Skeleton.Names;
        var materials = figure.Materials.ToList();
        var parts = new List<MeshPart>();
        var dyed = new Dictionary<Cloth, int>();
        int MaterialOf(Cloth cloth, int own)
        {
            if (!dyes.TryGetValue(cloth, out var dye))
                return own;
            if (dyed.TryGetValue(cloth, out int m) && materials[m].Texture == (dye.Texture ?? figure.Materials[own].Texture))
                return m;
            var source = figure.Materials[own];
            materials.Add(new ModelMaterial($"{name}.{cloth.ToString().ToLowerInvariant()}", dye.Texture ?? source.Texture,
                dye.Texture is null ? source.BaseColour * dye.Colour : dye.Colour, dye.Shine, 0, 0, dye.Texture is null ? source.Tint * dye.Colour : dye.Colour));
            return dyed[cloth] = materials.Count - 1;
        }
        foreach (var part in figure.Parts)
        {
            string material = figure.Materials[part.Material].Name;
            if (part.Name.Contains("coat", StringComparison.Ordinal) || part.Name.Contains("scarf", StringComparison.Ordinal)
                || material.Contains(".lamp", StringComparison.Ordinal) || part.Clip is not null)
                continue;
            var positions = shape is null ? part.Positions : Reshaped(part, bones, shape);
            // Each triangle to the cloth of the bone that moves it most (its three corners' weights summed).
            var cut = new Dictionary<Cloth, List<int>>();
            var idx = part.Indices;
            // The figure's "body" is its head, and the chest lamp's housing below it: only the head's kept.
            bool headOnly = part.Name == "body" || part.Name.EndsWith("_body", StringComparison.Ordinal);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var cloth = ClothOf(part, idx, t, bones, positions);
                if (cloth is not { } c || headOnly && c != Cloth.Face)
                    continue;
                if (!cut.TryGetValue(c, out var list))
                    cut[c] = list = [];
                list.Add(idx[t]);
                list.Add(idx[t + 1]);
                list.Add(idx[t + 2]);
            }
            if (headOnly && growths is not null && cut.TryGetValue(Cloth.Face, out var face))
                foreach (var g in growths)
                {
                    var grown = new List<int>();
                    for (int t = 0; t + 2 < face.Count; t += 3)
                        if (g.On((positions[face[t]] + positions[face[t + 1]] + positions[face[t + 2]]) / 3))
                            grown.AddRange([face[t], face[t + 1], face[t + 2]]);
                    if (grown.Count == 0)
                        continue;
                    // Out from the head's middle, not along the scan's normals (they don't all face out).
                    var outward = new Vector3[positions.Length];
                    var normals = new Vector3[positions.Length];
                    for (int v = 0; v < outward.Length; v++)
                    {
                        normals[v] = Vector3.Normalize(positions[v] - HeadMiddle);
                        outward[v] = positions[v] + normals[v] * g.Out;
                    }
                    materials.Add(new ModelMaterial($"{name}.{g.Name}", g.Dye.Texture ?? figure.Materials[part.Material].Texture, g.Dye.Colour, g.Dye.Shine, 0, 0, g.Dye.Colour));
                    parts.Add(new MeshPart
                    {
                        Name = $"{part.Name}.{g.Name}",
                        Material = materials.Count - 1,
                        Positions = outward,
                        Normals = normals,
                        Uvs = part.Uvs,
                        Joints = part.Joints,
                        Weights = part.Weights,
                        Indices = [.. grown],
                        Variants = part.Variants,
                    });
                }
            foreach (var (cloth, indices) in cut.OrderBy(kv => kv.Key))
                parts.Add(new MeshPart
                {
                    Name = $"{part.Name}.{cloth.ToString().ToLowerInvariant()}",
                    Material = MaterialOf(cloth, part.Material),
                    Positions = positions,
                    Normals = part.Normals,
                    Uvs = part.Uvs,
                    Joints = part.Joints,
                    Weights = part.Weights,
                    Indices = [.. indices],
                    Variants = part.Variants,
                });
        }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in parts)
            foreach (var v in p.Positions)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        return new Model
        {
            Name = name,
            Parts = [.. parts],
            Materials = [.. materials],
            Skeleton = figure.Skeleton,
            Clips = figure.Clips,
            ClipNames = figure.ClipNames,
            VariantCount = figure.VariantCount,
            Min = min,
            Max = max,
        };
    }

    static Vector3[] Reshaped(MeshPart part, string[] bones, Func<Vector3, string, Vector3> shape)
    {
        var moved = new Vector3[part.Positions.Length];
        for (int i = 0; i < moved.Length; i++)
            moved[i] = shape(part.Positions[i], bones[Dominant(part, i)]);
        return moved;
    }

    static int Dominant(MeshPart part, int v)
    {
        int k = v * 4, best = 0;
        for (int j = 1; j < 4; j++)
            if (part.Weights[k + j] > part.Weights[k + best])
                best = j;
        return part.Joints[k + best];
    }

    /// <summary>The cloth a triangle's in, or null to leave it off (the satchel).</summary>
    static Cloth? ClothOf(MeshPart part, int[] idx, int t, string[] bones, Vector3[] positions)
    {
        Span<float> weight = stackalloc float[bones.Length];
        for (int c = 0; c < 3; c++)
        {
            int k = idx[t + c] * 4;
            for (int j = 0; j < 4; j++)
                weight[part.Joints[k + j]] += part.Weights[k + j];
        }
        int bone = 0;
        for (int b = 1; b < bones.Length; b++)
            if (weight[b] > weight[bone])
                bone = b;
        var centre = (positions[idx[t]] + positions[idx[t + 1]] + positions[idx[t + 2]]) / 3;
        string n = bones[bone];
        // The satchel at the hip, whatever bone it hangs from.
        if (centre.X > Satchel && centre.Y is > 0.72f and < 1.0f)
            return null;
        if (n.StartsWith("head", StringComparison.Ordinal) || n == "neck")
            return Cloth.Face;
        if (n.StartsWith("hand", StringComparison.Ordinal) || n.StartsWith("finger", StringComparison.Ordinal) || n.StartsWith("thumb", StringComparison.Ordinal))
            return Cloth.Hands;
        if (n.StartsWith("lowerarm", StringComparison.Ordinal))
            return Cloth.Forearms;
        if (n.StartsWith("upperarm", StringComparison.Ordinal))
            return Cloth.Sleeves;
        if (n.StartsWith("foot", StringComparison.Ordinal) || n.StartsWith("ball", StringComparison.Ordinal))
            return Cloth.Feet;
        if (n.StartsWith("thigh", StringComparison.Ordinal) || n.StartsWith("calf", StringComparison.Ordinal))
            return Cloth.Trousers;
        if (n == "pelvis")
            return centre.Y > Waist ? Cloth.Shirt : Cloth.Trousers;
        return Cloth.Shirt;
    }
}
