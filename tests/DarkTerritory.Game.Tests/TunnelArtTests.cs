using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A tunnel isn't seen through (ARCHITECTURE §8 note 433; the director, 8 Oct, GDD App. F.4: "see through or missing"):
/// above a portal's face the ground closes from its coping up onto the hill, and the bore's lining is whole at every
/// joint on a curve. `dt holes` found both, the sky showing through them.
/// </summary>
public class TunnelArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    /// <summary>Everything a mesh draws (its own triangles and its instances'), relative to the eye it was built for.</summary>
    static List<(Vector3, Vector3, Vector3)> Triangles(MeshBuilder mesh)
    {
        var tris = new List<(Vector3, Vector3, Vector3)>();
        var v = mesh.Vertices.ToArray();
        for (int t = 0; t + 2 < v.Length; t += 3)
            tris.Add((v[t].Position, v[t + 1].Position, v[t + 2].Position));
        foreach (var i in mesh.Instances)
        {
            var w = i.Asset.Vertices;
            for (int t = 0; t + 2 < w.Length; t += 3)
                tris.Add((Vector3.Transform(w[t].Position, i.Model), Vector3.Transform(w[t + 1].Position, i.Model), Vector3.Transform(w[t + 2].Position, i.Model)));
        }
        return tris;
    }

    /// <summary>Whether a segment passes through any of the triangles (Möller–Trumbore, either face).</summary>
    static bool Hits(List<(Vector3 A, Vector3 B, Vector3 C)> tris, Vector3 from, Vector3 to)
    {
        var d = to - from;
        foreach (var (a, b, c) in tris)
        {
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f)
                continue;
            var s = from - a;
            float u = Vector3.Dot(s, p) / det;
            if (u < 0 || u > 1)
                continue;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) / det;
            if (w < 0 || u + w > 1)
                continue;
            float t = Vector3.Dot(e2, q) / det;
            if (t is >= 0 and <= 1)
                return true;
        }
        return false;
    }

    static Vector3 At(RailLine line, double s, double lateral, double up, Double3 eye)
    {
        var t = line.Sample(s);
        var p = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral + Double3.Up * up;
        return p.RelativeTo(eye);
    }

    [Theory]
    [InlineData("deadLines:3")]
    [InlineData("deepTerritory:2")]
    public void AboveAPortalsFaceTheGroundClosesOntoTheHill(string spec)
    {
        // From up in front of each portal, either side of the line (where `dt holes` stood its high camera), every sight
        // line through the ground round the face lands on something: the face, the cap from its coping onto the hill, the
        // hill. Before note 433 the land's step across the face was left out with the hill's edge 5-7 m over the coping, and
        // under it there was nothing beside the lining: a tenth of these sight lines went on into the sky.
        var route = Sim.LineGen.Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var art = new WorldArt(Look);
        int portals = 0;
        foreach (var f in route.Features.Where(f => f.Kind == FeatureKind.Tunnel))
            foreach (var (face, way) in new[] { (f.Start, 1), (f.End, -1) })
                foreach (int side in new[] { -1, 1 })
                {
                    var eye = line.Sample(face).Position;
                    var mesh = new MeshBuilder();
                    art.Track(mesh, line, route, eye, face - 150, face + 150, 18);
                    art.Tunnel(mesh, line, f, eye, face - 150, face + 150);
                    var tris = Triangles(mesh);
                    var from = At(line, face - way * 82, side * 18, 30, eye);
                    int rays = 0, open = 0;
                    for (double lateral = -10; lateral <= 10; lateral += 2.5)
                        for (double up = 4; up <= 22; up += 3)
                            for (double into = 0; into <= 6; into += 3)
                            {
                                var through = At(line, face + way * into, lateral, up, eye);
                                rays++;
                                if (!Hits(tris, from, from + (through - from) * 12))
                                    open++;
                            }
                    // A ray or two down a seam between two triangles is the test's, not the land's.
                    Assert.True(open <= 2, $"{spec} tunnel at {f.Start:0}: {open} of {rays} sight lines open from {(side > 0 ? "right" : "left")} of the {(way > 0 ? "near" : "far")} portal");
                    portals++;
                }
        Assert.True(portals >= 4, $"{spec}: {portals} portal views");
    }

    [Fact]
    public void ABoresLiningIsWholeAtEveryJointRoundACurve()
    {
        // Out from the middle of the bore, either side, at and either side of every joint between its 10 m lengths: the
        // lining's wall. Laid each on its own tangent, a length's end stood off the curve by its sag, and a wedge opened at
        // the joint on the outer wall: deepTerritory:2's Holt Brook tunnel showed the sky through every one.
        var route = Sim.LineGen.Routes.Generate(Content, "deepTerritory:2", 6);
        var line = route.Build();
        var art = new WorldArt(Look);
        var f = route.Features.Where(f => f.Kind == FeatureKind.Tunnel).MaxBy(f => f.End - f.Start)!;
        int joints = 0;
        for (double joint = f.Start + 10; joint < f.End - 10; joint += 10)
        {
            var eye = line.Sample(joint).Position;
            var mesh = new MeshBuilder();
            art.Tunnel(mesh, line, f, eye, joint - 30, joint + 30);
            var tris = Triangles(mesh);
            for (double ds = -0.12; ds <= 0.12; ds += 0.02)
                foreach (int side in new[] { -1, 1 })
                    foreach (double up in new[] { 0.6, 2.0, 3.5 })
                        Assert.True(Hits(tris, At(line, joint + ds, 0, up, eye), At(line, joint + ds, side * (StructureKit.TunnelHalf + 1.5), up, eye)),
                            $"the tunnel at {f.Start:0}: the lining's open {ds:0.00} m from its joint at {joint - f.Start:0} m in, on the {(side > 0 ? "right" : "left")}, {up} m up");
            joints++;
        }
        Assert.True(joints > 10, $"only {joints} joints");
    }
}
