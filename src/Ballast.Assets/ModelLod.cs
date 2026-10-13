using System.Numerics;

namespace Ballast.Assets;

/// <summary>
/// A distance copy of a skinned model made at load, for a model that has none of its own (an authored &lt;name&gt;.lod1.glb
/// needs Blender's decimate): its vertices clustered on a grid in the bind pose, each part on its own, and never across
/// bones (a cluster is a cell and the bone that leads its vertices), so an arm never fuses to the hip it swings past.
/// Each cluster keeps the vertex nearest its middle, with that vertex's normal, UV and weights; a triangle two of whose
/// corners land in one cluster goes. Coarse up close; a few pixels tall, a figure reads the same (ARCHITECTURE §8 note 479).
/// </summary>
public static class ModelLod
{
    /// <param name="cell">The grid's spacing (m): what's smaller than this is gone.</param>
    public static Model Clustered(Model full, float cell)
    {
        var parts = new MeshPart[full.Parts.Length];
        for (int p = 0; p < parts.Length; p++)
        {
            // A small part (a face, a hand: bare skin in patches smaller than a cell on each bone) would go whole: finer for it.
            var part = full.Parts[p];
            float c = cell;
            do
                parts[p] = Cluster(part, c);
            while (parts[p].Triangles < Math.Min(part.Triangles, 24) && (c /= 2) > 0.005f);
        }
        return new Model
        {
            Name = full.Name + ".clustered",
            Parts = parts,
            Materials = full.Materials,
            Skeleton = full.Skeleton,
            Clips = full.Clips,
            ClipNames = full.ClipNames,
            VariantCount = full.VariantCount,
            Min = full.Min,
            Max = full.Max,
        };
    }

    static MeshPart Cluster(MeshPart part, float cell)
    {
        int n = part.Positions.Length;
        // Each vertex's cluster: its cell and its leading bone.
        var keys = new Dictionary<(int, int, int, int), int>();
        var clusterOf = new int[n];
        var sums = new List<Vector3>();
        var counts = new List<int>();
        for (int v = 0; v < n; v++)
        {
            var q = part.Positions[v] / cell;
            var key = ((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y), (int)MathF.Floor(q.Z), Leading(part, v));
            if (!keys.TryGetValue(key, out int c))
            {
                keys[key] = c = sums.Count;
                sums.Add(Vector3.Zero);
                counts.Add(0);
            }
            clusterOf[v] = c;
            sums[c] += part.Positions[v];
            counts[c]++;
        }
        // The vertex nearest each cluster's middle stands for it (the first, on a tie: the same every load).
        var keep = new int[sums.Count];
        var best = new float[sums.Count];
        Array.Fill(keep, -1);
        for (int v = 0; v < n; v++)
        {
            int c = clusterOf[v];
            float d = Vector3.DistanceSquared(part.Positions[v], sums[c] / counts[c]);
            if (keep[c] < 0 || d < best[c])
                (keep[c], best[c]) = (v, d);
        }
        var indices = new List<int>(part.Indices.Length / 4);
        var seen = new HashSet<(int, int, int)>();
        for (int t = 0; t + 2 < part.Indices.Length; t += 3)
        {
            int a = clusterOf[part.Indices[t]], b = clusterOf[part.Indices[t + 1]], c = clusterOf[part.Indices[t + 2]];
            if (a == b || b == c || a == c)
                continue;
            // The same three clusters twice (two sides of a thin fold, collapsed onto each other): once. Its winding kept.
            var turn = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
            if (!seen.Add(turn))
                continue;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
        int m = keep.Length;
        var positions = new Vector3[m];
        var normals = new Vector3[m];
        var uvs = new Vector2[m];
        var joints = new int[m * 4];
        var weights = new float[m * 4];
        for (int c = 0; c < m; c++)
        {
            int v = keep[c];
            positions[c] = part.Positions[v];
            normals[c] = part.Normals[v];
            uvs[c] = part.Uvs[v];
            Array.Copy(part.Joints, v * 4, joints, c * 4, 4);
            Array.Copy(part.Weights, v * 4, weights, c * 4, 4);
        }
        return new MeshPart
        {
            Name = part.Name,
            Material = part.Material,
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            Joints = joints,
            Weights = weights,
            Indices = [.. indices],
            Variants = part.Variants,
            Clip = part.Clip,
        };
    }

    /// <summary>The bone that weighs most on a vertex.</summary>
    static int Leading(MeshPart part, int v)
    {
        int at = v * 4, lead = 0;
        for (int i = 1; i < 4; i++)
            if (part.Weights[at + i] > part.Weights[at + lead])
                lead = i;
        return part.Joints[at + lead];
    }
}
