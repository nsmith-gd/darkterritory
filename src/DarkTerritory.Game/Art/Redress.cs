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
    /// <summary>The figure's long coat, kept and dyed (left off when it isn't given a dye): a greatcoat, a cassock, oilskins.</summary>
    Coat,
    /// <summary>The figure's scarf, kept and dyed (left off when it isn't given a dye).</summary>
    Scarf,
    /// <summary>The throat and chest under the chin, the head part's (the face's skin when it isn't given a dye): a collar, a shirt front.</summary>
    Collar,
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
/// else, without a new model (this container builds none: Blender isn't here): the long coat and the scarf left off unless
/// they're dyed (<see cref="Cloth.Coat"/>, <see cref="Cloth.Scarf"/>), the chest lamp left off, and what's under them, the figure's frame, cut by the bone each triangle moves with into <see cref="Cloth"/>
/// regions, each in its own <see cref="Dye"/>. Same rig and clips, so it's posed, worn on (<c>CreatureArt.Wear</c>) and
/// drawn as the survivors are. The figures met out in the Territory (GDD §3.2: Dave, Nicki, Jacob) are dressed this way;
/// the director's photographs of the people they're for are what they're dressed after.
/// </summary>
public static class Redress
{
    /// <summary>Where the waist is (m): the pelvis's triangles above it are the shirt's, below it the trousers'.</summary>
    const float Waist = 0.94f;
    /// <summary>Below this (m) the head part's skin is the collar's (<see cref="Cloth.Collar"/>): under the chin.</summary>
    const float CollarLine = 1.5f;
    /// <summary>The most open edges a loop has and is still a hole to close (<see cref="Capped"/>), not an opening.</summary>
    const int HoleEdges = 16;
    /// <summary>Where the frame's breast is crumpled (m, bind pose; across the breast, round the number patch): its normals set right (<see cref="Capped"/>).</summary>
    static class Patch
    {
        public const float MinX = -0.17f, MaxX = 0.17f, MinY = 1.28f, MaxY = 1.48f, MaxZ = -0.08f;
    }
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
        foreach (var raw in figure.Parts)
        {
            // The frame was modelled to be worn under the coat and the chest lamp, and is open where they hid it.
            var part = raw.Name.Contains("frame", StringComparison.Ordinal) ? Capped(raw) : raw;
            string material = figure.Materials[part.Material].Name;
            if (material.Contains(".lamp", StringComparison.Ordinal) || part.Clip is not null)
                continue;
            // The coat and the scarf are kept whole in their own dye when a character wears them (the townsfolk's
            // greatcoats and cassocks, note 694), and left off otherwise.
            Cloth? whole = part.Name.Contains("coat", StringComparison.Ordinal) ? Cloth.Coat
                : part.Name.Contains("scarf", StringComparison.Ordinal) ? Cloth.Scarf : null;
            if (whole is { } w && !dyes.ContainsKey(w))
                continue;
            var positions = shape is null ? part.Positions : Reshaped(part, bones, shape);
            if (whole is { } kept)
            {
                parts.Add(new MeshPart
                {
                    Name = $"{part.Name}.{kept.ToString().ToLowerInvariant()}",
                    Material = MaterialOf(kept, part.Material),
                    Positions = positions,
                    Normals = part.Normals,
                    Uvs = part.Uvs,
                    Joints = part.Joints,
                    Weights = part.Weights,
                    Indices = part.Indices,
                    Variants = part.Variants,
                });
                continue;
            }
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
                // The head part's skin runs on down the throat to the chest (1.27 m): under a buttoned coat or a high
                // collar it's that cloth's, when the clothes name one (note 694).
                if (headOnly && dyes.ContainsKey(Cloth.Collar) && (positions[idx[t]].Y + positions[idx[t + 1]].Y + positions[idx[t + 2]].Y) / 3 < CollarLine)
                    c = Cloth.Collar;
                // The shirt's and the coat's own neckline moves with the neck, but it's cloth, not the face's skin: in the
                // atlas it's the penal shirt's pale collar, which the face's tint would leave showing (note 694).
                if (!headOnly && c == Cloth.Face && dyes.ContainsKey(Cloth.Collar))
                    c = Cloth.Collar;
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

    /// <summary>
    /// The part with its small holes closed (a loop of open edges up to <see cref="HoleEdges"/> long, welded by position: the
    /// frame's breast, under where the chest lamp's strap was), each fanned from a vertex at its middle that moves as its
    /// first corner does. Seen through, they read as pale flecks on a dressed chest (note 694).
    /// </summary>
    static MeshPart Capped(MeshPart part)
    {
        var at = new Dictionary<Vector3, int>();
        var weld = new int[part.Positions.Length];
        for (int v = 0; v < weld.Length; v++)
        {
            var key = new Vector3(MathF.Round(part.Positions[v].X, 4), MathF.Round(part.Positions[v].Y, 4), MathF.Round(part.Positions[v].Z, 4));
            weld[v] = at.TryGetValue(key, out int w) ? w : at[key] = v;
        }
        // A triangle wound against its own corners' normals is culled from the front and seen through: turned round.
        var idx = part.Indices.ToArray();
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            var p0 = part.Positions[idx[t]];
            var face = Vector3.Cross(part.Positions[idx[t + 1]] - p0, part.Positions[idx[t + 2]] - p0);
            if (Vector3.Dot(face, part.Normals[idx[t]] + part.Normals[idx[t + 1]] + part.Normals[idx[t + 2]]) < 0)
                (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
        }
        var uses = new Dictionary<(int, int), int>();
        for (int t = 0; t + 2 < idx.Length; t += 3)
            for (int c = 0; c < 3; c++)
            {
                int a = weld[idx[t + c]], b = weld[idx[t + (c + 1) % 3]];
                var e = a < b ? (a, b) : (b, a);
                uses[e] = uses.GetValueOrDefault(e) + 1;
            }
        // Each open edge as its triangle winds it, a to b; a hole is walked b to a.
        var next = new Dictionary<int, int>();
        for (int t = 0; t + 2 < idx.Length; t += 3)
            for (int c = 0; c < 3; c++)
            {
                int a = weld[idx[t + c]], b = weld[idx[t + (c + 1) % 3]];
                if (uses[a < b ? (a, b) : (b, a)] == 1)
                    next.TryAdd(b, a);
            }
        var positions = part.Positions.ToList();
        var normals = part.Normals.ToList();
        var uvs = part.Uvs.ToList();
        var joints = part.Joints.ToList();
        var weights = part.Weights.ToList();
        // A seam in its texture is two vertices in one place, and they aren't always skinned alike: posed, they part, and
        // the field shows through the crack. Each moves as the first of its place does.
        for (int v = 0; v < weld.Length; v++)
            if (weld[v] != v)
                for (int j = 0; j < 4; j++)
                {
                    joints[v * 4 + j] = part.Joints[weld[v] * 4 + j];
                    weights[v * 4 + j] = part.Weights[weld[v] * 4 + j];
                }
        var indices = idx.ToList();
        var seen = new HashSet<int>();
        foreach (int start in next.Keys.Order())
        {
            if (seen.Contains(start))
                continue;
            var loop = new List<int> { start };
            seen.Add(start);
            for (int v = next[start]; v != start && loop.Count <= HoleEdges && next.TryGetValue(v, out int after) && seen.Add(v); v = after)
                loop.Add(v);
            if (loop.Count < 3 || loop.Count > HoleEdges || !next.TryGetValue(loop[^1], out int closes) || closes != start)
                continue;
            var middle = Vector3.Zero;
            var normal = Vector3.Zero;
            var uv = Vector2.Zero;
            foreach (int v in loop)
            {
                middle += part.Positions[v];
                normal += part.Normals[v];
                uv += part.Uvs[v];
            }
            int m = positions.Count;
            positions.Add(middle / loop.Count);
            normals.Add(Vector3.Normalize(normal));
            uvs.Add(uv / loop.Count);
            joints.AddRange(part.Joints.AsSpan(loop[0] * 4, 4).ToArray());
            weights.AddRange(part.Weights.AsSpan(loop[0] * 4, 4).ToArray());
            for (int i = 0; i < loop.Count; i++)
                indices.AddRange([loop[i], loop[(i + 1) % loop.Count], m]);
        }
        // The breast where the number patch was (tools/models/recipes/survivor_prisoner.py's number()) is crumpled round its
        // edge, its creases' normals turned every way: up, they catch the sky, pale flecks on a dressed chest (in the atlas
        // the patch's own paint hid them). They're given the chest's: out, and a little up.
        for (int v = 0; v < part.Positions.Length; v++)
        {
            var p = part.Positions[v];
            if (p.X is >= Patch.MinX and <= Patch.MaxX && p.Y is >= Patch.MinY and <= Patch.MaxY && p.Z <= Patch.MaxZ)
                normals[v] = Vector3.Normalize(new Vector3(p.X * 2.2f, 0.15f + MathF.Max(0, p.Y - 1.4f) * 4, -1));
        }
        return new MeshPart
        {
            Name = part.Name,
            Material = part.Material,
            Positions = [.. positions],
            Normals = [.. normals],
            Uvs = [.. uvs],
            Joints = [.. joints],
            Weights = [.. weights],
            Indices = [.. indices],
            Variants = part.Variants,
            Clip = part.Clip,
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
