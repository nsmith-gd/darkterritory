using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The fortress town's square (GDD §3.1; ARCHITECTURE §8 note 281): the walls stepping back for it, its buildings, the
/// custom's centrepiece, the board and its papers, the lamps. Its people are the scene's (<see cref="GreyboxScene"/>),
/// drawn with the crew's model. Everything stands where the town's plan (Sim) says, so what's drawn is what's solid.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>Draws a town's square within [<paramref name="from"/>, <paramref name="to"/>] along the line.</summary>
    public void Square(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to)
    {
        var plan = town.Plan;
        var sq = plan.Square;
        if (sq.S1 < from - 40 || sq.S0 > to + 40)
            return;
        int side = sq.Side;
        // The walls round it: the far wall along the line, the two ends back to the yard's walls, a tower at each far corner.
        var wall = Piece($"wall-{side}", () => StructureKit.Wall(_look, side));
        WallRun(mesh, line, eye, wall, sq.S0, sq.S1, sq.WallD, from, to);
        double inner = side * 14.8;
        foreach (double s in new[] { sq.S0, sq.S1 })
        {
            var t = line.Sample(s);
            var at = t.Position + Right(t) * inner;
            float k = (float)(Math.Abs(sq.WallD - inner) / 10);
            mesh.Instances.Add(new MeshInstance(wall, Matrix4x4.CreateScale(1, 1, k) * Basis(t.Tangent, at, eye, -side * MathF.PI / 2)));
            var corner = t.Position + Right(t) * sq.WallD;
            mesh.Instances.Add(new MeshInstance(Piece($"tower-{side}", () => StructureKit.Tower(_look, side)), Basis(t.Tangent, corner, eye, 0)));
        }
        // The buildings, fronts to the line, a lamp burning by each door.
        foreach (var b in plan.Buildings)
        {
            var piece = Piece($"square-{b.Kind}-{b.Style}-{b.Length:0}x{b.Depth:0}", () => SquareKit.Building(_look, b.Kind, (float)b.Length, (float)b.Depth, b.Style));
            var m = Place(line, eye, town.World(b.S, b.D), b.S, 0, -side);
            mesh.Instances.Add(new MeshInstance(piece, m));
            var lamp = Vector3.Transform(SquareKit.Lamp((float)b.Depth, b.Kind == "hall"), m);
            mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 1.3f, 9));
            mesh.Billboard(lamp, 0.8f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
        }
        // The things in it (those inside the houses are the houses' own: Houses).
        int notices = plan.Papers.Count(p => p.OnBoard);
        foreach (var f in plan.Fixtures)
        {
            if (f.House >= 0)
                continue;
            int count = f.Kind switch { "board" => notices, "line" => (int)(sq.S1 - sq.S0 - 8), _ => 0 };
            var piece = Piece($"square-{f.Kind}-{count}", () => SquareKit.Fixture(_look, f.Kind, count));
            var m = Place(line, eye, town.World(f.S, f.D), f.S, f.FaceS, f.FaceD);
            mesh.Instances.Add(new MeshInstance(piece, m));
            // The banked fire glows; the shuttered lamps leak a little round their shutters.
            if (f.Kind == "brazier")
                mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 1.3f, 0), m), Palette.FurnaceOrange * 1.1f, 7));
            if (f.Kind == "barrel")
                mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 1.4f, 0), m), Palette.FurnaceOrange * 2.0f, 11));
            if (f.Kind == "lamps")
                mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 1.8f, -0.6f), m), Palette.LampAmber * 0.4f, 4));
        }
        // The papers left about: flat on a bench or a crate, or pinned up by the hall's door.
        foreach (var p in plan.Papers)
        {
            if (p.OnBoard)
                continue;
            bool standing = p.Height > 1.2;
            var paper = Piece($"square-paper-{standing}", () => SquareKit.Paper(_look, standing));
            mesh.Instances.Add(new MeshInstance(paper, Place(line, eye, town.World(p.S, p.D, p.Height), p.S, 0, -side)));
        }
        // Lamp posts down the square's edge by the line and across its middle: a town keeps its square lit (GDD §28: "inside
        // = warm, human, temporary safety"), pools of it with the dark between.
        var post = Piece("square-lamppost", () => SquareKit.LampPost(_look));
        double mid = (sq.S0 + sq.S1) / 2, across = (sq.WallD + side * 3.2) / 2;
        foreach (var (s, d) in new[] { (sq.S0 + 2, side * 3.2), (mid, side * 3.2), (sq.S1 - 2, side * 3.2), (sq.S0 + 12, across), (sq.S1 - 12, across) })
        {
            var m = Place(line, eye, town.World(s, d), s, 0, -side);
            mesh.Instances.Add(new MeshInstance(post, m));
            var flame = Vector3.Transform(SquareKit.LampTop, m);
            mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 2.2f, 16));
            mesh.Billboard(flame, 1.0f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
        }
    }

    /// <summary>
    /// A town's houses down its street (note 281; <see cref="MaritimeKit"/>), within [<paramref name="from"/>,
    /// <paramref name="to"/>]: fronts to the line, lamplight in the windows of those lived in (none where the custom keeps
    /// them dark), the open ones built inside to the layout the Sim walls them by, their lamps and range lit while you're
    /// near enough to see in.
    /// </summary>
    public void Houses(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to)
    {
        var plan = town.Plan;
        bool lit = plan.Culture != "shutters";
        foreach (var h in plan.Houses)
        {
            if (h.S < from - 20 || h.S > to + 20)
                continue;
            var at = town.World(h.S, h.D);
            double far = (at - eye).Length;
            if (far > 320)
                continue;
            var m = Place(line, eye, at, h.S, 0, -h.Side);
            if (h.Layout is null)
            {
                float w = (float)Math.Round(h.Width, 1), d = (float)Math.Round(h.Depth, 1);
                var piece = Piece($"maritime-{h.Kind}-{h.Style}-{h.Paint}-{w:0.0}x{d:0.0}-{lit}", () => MaritimeKit.House(_look, h.Kind, h.Style, h.Paint, w, d, lit));
                mesh.Instances.Add(new MeshInstance(piece, m));
                // A lived-in house's lamp by its door, its light on the clapboard and the ground in front (a custom that keeps
                // the windows dark keeps it low).
                if (h.Kind == HouseKind.Lived && far < 140)
                    Porch(mesh, m, 0, d, lit);
                continue;
            }
            string? thing = plan.Fixtures.FirstOrDefault(f => f.House == h.Id && f.Kind is not ("stove" or "stairdoor" or "photo"))?.Kind;
            var open = Piece($"maritime-open-{plan.Name}-{h.Id}-{h.S:0.0}-{h.Width:0.00}-{thing}-{lit}", () => MaritimeKit.Open(_look, h, thing, lit));
            mesh.Instances.Add(new MeshInstance(open, m));
            // Its lamps inside, its lamp by the door, and the light out of its open door onto the step.
            float dz = (float)(-h.Depth / 2), dx = (float)(h.Side * h.Layout.DoorU);
            Porch(mesh, m, dx, (float)h.Depth, lit);
            mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(dx, 1.2f, dz - 0.8f), m), Palette.LampAmber * 0.7f, 5));
            if (far < 40)
                foreach (var (p, colour, range) in MaritimeKit.Lights(h))
                    mesh.PointLights.Add(new PointLight(Vector3.Transform(p, m), colour, range));
        }
    }

    /// <summary>A house's door lamp (<see cref="MaritimeKit.Porch"/>) lit, low where the custom keeps the windows dark.</summary>
    void Porch(MeshBuilder mesh, in Matrix4x4 m, float doorX, float depth, bool lit)
    {
        var lamp = Vector3.Transform(MaritimeKit.Porch(doorX, depth, (float)_look.Doorway.Height), m);
        mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * (lit ? 1.0f : 0.3f), lit ? 7 : 3.5f));
        mesh.Billboard(lamp, lit ? 0.5f : 0.25f, 0, new Vector4(Palette.LampAmber * (lit ? 0.5f : 0.2f), 1), -1, FxBlend.Additive);
    }

    /// <summary>A thing at <paramref name="at"/>, its front (−Z) turned to face along the line by <paramref name="faceS"/>
    /// and across it by <paramref name="faceD"/>.</summary>
    static Matrix4x4 Place(RailLine line, Double3 eye, Double3 at, double s, double faceS, double faceD)
    {
        var t = line.Sample(s);
        float yaw = (float)Math.Atan2(-faceD, faceS);
        return Basis(t.Tangent, at, eye, yaw);
    }

    static Double3 Right(TrackSample t) => Double3.Cross(t.Tangent, Double3.Up).Normalized;

    /// <summary>
    /// Ten-metre wall pieces along the line from <paramref name="s0"/> to <paramref name="s1"/>, <paramref name="d"/> out,
    /// on the ten-metre grid the fortress's own walls keep (so nothing moves as the visible stretch does), the end pieces
    /// cut short to stop exactly where the run does.
    /// </summary>
    void WallRun(MeshBuilder mesh, RailLine line, Double3 eye, MeshAsset wall, double s0, double s1, double d, double from, double to)
    {
        for (double g = Math.Floor(s0 / 10) * 10; g < s1; g += 10)
        {
            double lo = Math.Max(g, s0), hi = Math.Min(g + 10, s1);
            if (hi - lo < 0.05 || hi < from - 10 || lo > to + 10)
                continue;
            var t = line.Sample(lo);
            mesh.Instances.Add(new MeshInstance(wall, Matrix4x4.CreateScale(1, 1, (float)((hi - lo) / 10)) * Basis(t.Tangent, t.Position + Right(t) * d, eye, 0)));
        }
    }
}
