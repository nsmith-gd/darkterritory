using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Follower's nest built up over its 60 s (GDD App. A.6; ARCHITECTURE §8 note 460), from the follower_nest model's own
/// pieces (tools/models/recipes/follower_nest.py: each blob, box and strand a piece of its own, welded at none of its
/// edges to another): the loot it's built on there from the start, the pale strands tying it to the floor first, the
/// crust's lobes swelling up out of the heap one after another from the floor up, and the wet hollow on its top last.
/// Each piece grows from its own foot over its own share of the build, so it comes up out of what's under it.
/// </summary>
public sealed class NestBuild
{
    /// <summary>One piece of the model: its triangles, the point it grows from, and the share of the build it grows over.</summary>
    public readonly record struct Piece(int[] Triangles, Vector3 Foot, float From, float To, NestPart Part);

    /// <summary>What a piece is, by its shape (the recipe's parts).</summary>
    public enum NestPart { Loot, Strand, Lobe, Hollow }

    // How finely the build is drawn (a stage a step, each cooked once).
    const int Steps = 48;
    // The strands go down over the first fifth; the lobes from a fifth to most of the way, each over a quarter of it; the
    // hollow over the last sixth.
    const float StrandsBy = 0.2f, StrandEach = 0.1f, LobesFrom = 0.15f, LobesTo = 0.85f, LobeEach = 0.25f, HollowFrom = 0.82f;

    MeshAsset? _model;
    Piece[] _pieces = [];
    float _fullTop;
    readonly Dictionary<int, (MeshAsset Mesh, float Top)> _stages = new();

    /// <summary>The model's pieces, as <see cref="At"/> grows them (for the tests).</summary>
    public IReadOnlyList<Piece> Pieces(MeshAsset model)
    {
        Prepare(model);
        return _pieces;
    }

    /// <summary>
    /// The nest <paramref name="built"/> of the way built (0..1): the stage's mesh, and how high it stands as a share of the
    /// whole nest's height (where the Follower rides on it).
    /// </summary>
    public (MeshAsset Mesh, float Top) At(MeshAsset model, float built)
    {
        Prepare(model);
        int step = (int)MathF.Round(Math.Clamp(built, 0, 1) * Steps);
        if (_stages.TryGetValue(step, out var stage))
            return stage;
        float b = step / (float)Steps;
        var from = model.Vertices;
        var into = new List<Vertex>(from.Length);
        float top = 0;
        foreach (var p in _pieces)
        {
            float s = Grown(p, b);
            if (s < 0.02f)
                continue;
            foreach (int t in p.Triangles)
                for (int k = 0; k < 3; k++)
                {
                    var v = from[t * 3 + k];
                    v.Position = p.Foot + (v.Position - p.Foot) * s;
                    top = MathF.Max(top, v.Position.Y);
                    into.Add(v);
                }
        }
        return _stages[step] = (new MeshAsset($"{model.Name}-built-{step}", [.. into]), _fullTop > 0 ? top / _fullTop : 1);
    }

    /// <summary>How far grown a piece is (0..1) at a build of <paramref name="built"/>, eased in.</summary>
    public static float Grown(in Piece p, float built)
    {
        if (p.To <= p.From)
            return built >= p.From ? 1 : 0;
        float x = Math.Clamp((built - p.From) / (p.To - p.From), 0, 1);
        return x * x * (3 - 2 * x);
    }

    void Prepare(MeshAsset model)
    {
        if (ReferenceEquals(model, _model))
            return;
        _model = model;
        _stages.Clear();
        var v = model.Vertices;
        int tris = v.Length / 3;
        // The pieces: triangles that share a corner (welded to a millimetre) are one piece.
        var parent = new int[tris];
        for (int i = 0; i < tris; i++)
            parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x)
                x = parent[x] = parent[parent[x]];
            return x;
        }
        var corner = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < v.Length; i++)
        {
            var p = v[i].Position;
            var key = ((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000), (int)MathF.Round(p.Z * 1000));
            if (corner.TryGetValue(key, out int other))
            {
                int a = Find(other), b = Find(i / 3);
                if (a != b)
                    parent[a] = b;
            }
            else
                corner[key] = i / 3;
        }
        var groups = Enumerable.Range(0, tris).GroupBy(Find).Select(g => g.ToArray()).ToList();
        _fullTop = v.Length == 0 ? 0 : v.Max(x => x.Position.Y);
        // What each is, by its shape: the loot it's built on low down (the spill and the sack) or boxy (a crate's twelve
        // triangles, wider than a strand); a strand, twelve triangles of thin cylinder; the hollow, the highest piece;
        // the rest the crust's lobes.
        var shaped = groups.Select(g =>
        {
            var ps = g.SelectMany(t => new[] { v[t * 3].Position, v[t * 3 + 1].Position, v[t * 3 + 2].Position }).ToList();
            var c = ps.Aggregate(Vector3.Zero, (a, b) => a + b) / ps.Count;
            float r = ps.Max(p => Vector3.Distance(p, c));
            var low = ps.MinBy(p => p.Y);
            return (Tris: g, Centre: c, Radius: r, Low: low, Bottom: low.Y);
        }).ToList();
        int hollow = shaped.Count == 0 ? -1 : shaped.IndexOf(shaped.MaxBy(x => x.Bottom));
        var parts = shaped.Select((x, i) =>
            i == hollow && x.Bottom > 0.3f ? NestPart.Hollow
            : x.Centre.Y < 0.15f || x.Tris.Length <= 12 && x.Radius > 0.3f ? NestPart.Loot
            : x.Tris.Length <= 12 ? NestPart.Strand
            : NestPart.Lobe).ToList();
        var strands = Enumerable.Range(0, shaped.Count).Where(i => parts[i] == NestPart.Strand)
            .OrderBy(i => MathF.Atan2(shaped[i].Centre.Z, shaped[i].Centre.X)).ToList();
        var lobes = Enumerable.Range(0, shaped.Count).Where(i => parts[i] == NestPart.Lobe)
            .OrderBy(i => shaped[i].Bottom).ThenBy(i => shaped[i].Centre.Y).ToList();
        var pieces = new Piece[shaped.Count];
        for (int i = 0; i < shaped.Count; i++)
        {
            var x = shaped[i];
            pieces[i] = parts[i] switch
            {
                NestPart.Loot => new Piece(x.Tris, x.Centre, 0, 0, NestPart.Loot),
                // Down to the floor from the heap, one after another round it: grown from its foot on the floor.
                NestPart.Strand => Window(x.Tris, x.Low, strands.IndexOf(i), strands.Count, 0, StrandsBy - StrandEach, StrandEach, NestPart.Strand),
                // Up out of the heap from its own foot, lowest first.
                NestPart.Lobe => Window(x.Tris, x.Centre with { Y = x.Bottom }, lobes.IndexOf(i), lobes.Count, LobesFrom, LobesTo - LobeEach, LobeEach, NestPart.Lobe),
                _ => new Piece(x.Tris, x.Centre with { Y = x.Bottom }, HollowFrom, 1, NestPart.Hollow),
            };
        }
        _pieces = pieces;
    }

    static Piece Window(int[] tris, Vector3 foot, int index, int count, float first, float last, float each, NestPart part)
    {
        float from = count <= 1 ? first : first + (last - first) * index / (count - 1);
        return new Piece(tris, foot, from, from + each, part);
    }
}
