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
        // A walled town's square has none (queue #74, note 335): the town's wall is out past its streets, and the square
        // opens between its buildings onto the first of them (Town's walls are the same).
        if (plan.Bounds is null)
        {
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
        // A new town lets go of the last one's houses (see HousePiece).
        if (!ReferenceEquals(plan, _housesOf))
            (_housesOf, _housePieces) = (plan, []);
        _houseFrame++;
        // Near you each house is its own, built to its design (and an open one inside); further off, its far form; past a
        // block's reach, a block of a street as one mesh (queue #74: a town of thousands is hundreds of houses).
        const double near = 75, reach = 430;
        foreach (var block in BlocksOf(plan))
        {
            var centre = town.World(block.S, block.D);
            double far = (centre - eye).Length;
            if (far > reach)
                continue;
            if (far > near + Block * 0.75)
            {
                string key = $"town-block-{block.S:0}-{block.D:0}-{lit}";
                var piece = HousePiece(key, () => MaritimeKit.Street(_look, block.Houses.Select(h => (h, InBlock(h, block))), town.Looks, lit, key));
                mesh.Instances.Add(new MeshInstance(piece, Place(line, eye, centre, block.S, 1, 0)));
                continue;
            }
            foreach (var h in block.Houses)
                House(mesh, line, eye, town, h, lit, near);
        }
    }

    /// <summary>
    /// A town's house or block mesh, kept while it's drawn. Every house is its own design (note 281) and a walled town has
    /// hundreds (note 335), so unlike <see cref="Piece"/>'s kit pieces they aren't kept for good: past
    /// <see cref="HousePieceLimit"/> the longest unused go (the renderer lets a mesh's upload go with it), and a new town
    /// lets go of the last one's. Before this a walled town's every house stayed cached for as long as its Look lived.
    /// </summary>
    MeshAsset HousePiece(string key, Func<MeshAsset> make)
    {
        if (_housePieces.TryGetValue(key, out var hit))
        {
            _housePieces[key] = (hit.Mesh, _houseFrame);
            return hit.Mesh;
        }
        var made = make();
        _housePieces[key] = (made, _houseFrame);
        if (_housePieces.Count > HousePieceLimit)
            foreach (string old in _housePieces.Where(kv => kv.Value.Used < _houseFrame).OrderBy(kv => kv.Value.Used).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(_housePieces.Count - HousePieceLimit * 3 / 4).Select(kv => kv.Key).ToList())
                _housePieces.Remove(old);
        return made;
    }

    /// <summary>How many of a town's house and block meshes are kept (a frame of the biggest town draws ~300).</summary>
    const int HousePieceLimit = 600;

    TownPlan? _housesOf;
    Dictionary<string, (MeshAsset Mesh, long Used)> _housePieces = [];
    long _houseFrame;

    /// <summary>How long a block of houses is along the line (m), for drawing it as one mesh past the near houses.</summary>
    const double Block = 60;

    /// <summary>A block of houses: its middle (along the line, across it), and its houses.</summary>
    sealed record HouseBlock(double S, double D, List<TownHouse> Houses);

    TownPlan? _blocksOf;
    List<HouseBlock> _blocks = [];

    /// <summary>The town's houses in blocks: by <see cref="Block"/> along the line and their row across it.</summary>
    List<HouseBlock> BlocksOf(TownPlan plan)
    {
        if (ReferenceEquals(plan, _blocksOf))
            return _blocks;
        _blocksOf = plan;
        _blocks = [.. plan.Houses.GroupBy(h => (Math.Floor(h.S / Block), Math.Round(h.D / 10)))
            .Select(g => new HouseBlock(g.Average(h => h.S), g.Average(h => h.D), [.. g]))];
        return _blocks;
    }

    /// <summary>A house in its block's frame (X across the line, −Z along it, as <see cref="Place"/> sets the block), turned to face its street.</summary>
    static Matrix4x4 InBlock(TownHouse h, HouseBlock block) =>
        Matrix4x4.CreateRotationY((float)(h.Side * Math.PI / 2)) * Matrix4x4.CreateTranslation((float)(h.D - block.D), 0, (float)-(h.S - block.S));

    /// <summary>One house on its own: near, built to its design (an open one inside, its lamps lit); else its far form.</summary>
    void House(MeshBuilder mesh, RailLine line, Double3 eye, Town town, TownHouse h, bool lit, double near)
    {
        var plan = town.Plan;
        var at = town.World(h.S, h.D);
        double far = (at - eye).Length;
        var m = Place(line, eye, at, h.S, 0, -h.Side);
        if (far > near && h.Layout is null)
        {
            var distant = HousePiece($"maritime-far-{h.Id}-{lit}", () => MaritimeKit.Far(_look, h, town.Looks, lit));
            mesh.Instances.Add(new MeshInstance(distant, m));
            return;
        }
        // Built to its design once while it's near (each house is its own: HousePiece keeps it while it's drawn).
        string? thing = h.Layout is null ? null
            : plan.Fixtures.FirstOrDefault(f => f.House == h.Id && f.Kind is not ("stove" or "stairdoor" or "photo"))?.Kind;
        var piece = HousePiece($"maritime-{h.Id}-{h.Kind}-{thing}-{lit}", () => MaritimeKit.House(_look, h, town.Looks, thing, lit));
        mesh.Instances.Add(new MeshInstance(piece, m));
        // A lived-in house's lamp by its door, its light on the siding and the ground in front (a custom that keeps the
        // windows dark keeps it low).
        if (h.Kind == HouseKind.Lived)
            Porch(mesh, m, h, lit);
        if (h.Layout is null)
            return;
        // An open house's lamps inside, and the light out of its open door onto the step.
        float dz = (float)(-h.Depth / 2), dx = (float)(h.Side * h.Layout.DoorU);
        mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(dx, 1.2f, dz - 0.8f), m), Palette.LampAmber * 0.7f, 5));
        if (far < 40)
            foreach (var (p, colour, range) in MaritimeKit.Lights(h))
                mesh.PointLights.Add(new PointLight(Vector3.Transform(p, m), colour, range));
    }

    /// <summary>
    /// A walled town's wall (queue #74, note 335; the Sim's is Run.Fortresses.Round): ten-metre pieces down each side from
    /// the rear wall to the gate, the front wall out from the gatehouse to each corner, the rear wall across the line, a
    /// tower at each corner and down the sides, their lamps lit near you.
    /// </summary>
    void TownWall(MeshBuilder mesh, RailLine line, Double3 eye, TownBounds town, double from, double to, bool lit)
    {
        var start = line.Sample(0);
        var tangent = new Double3(start.Tangent.X, 0, start.Tangent.Z).Normalized;
        var right = Double3.Cross(tangent, Double3.Up).Normalized;
        // A point in the rail frame: the yard is straight, behind its start too.
        Double3 P(double s, double d) => start.Position + tangent * s + right * d;
        bool Near(double s, double d) => (P(s, d) - eye).Length < 460;
        const double bay = Sim.Run.Fortresses.WallBay, every = Sim.Run.Fortresses.TowerEvery, gateOuter = Sim.Run.Fortresses.GateOuter;
        void Run(int side, double s, double d, double length, float yaw)
        {
            if (!Near(s, d))
                return;
            var wall = Piece($"wall-{side}", () => StructureKit.Wall(_look, side));
            mesh.Instances.Add(new MeshInstance(wall, Matrix4x4.CreateScale(1, 1, (float)(length / bay)) * Basis(start.Tangent, P(s, d), eye, yaw)));
        }
        foreach (var (d, side) in new[] { (-town.Left, -1), (town.Right, 1) })
        {
            for (double s = town.Rear; s < town.Gate - 0.05; s += bay)
                Run(side, s, d, Math.Min(bay, town.Gate - s), 0);
            // The front wall from the gatehouse's tower out to the corner, and the rear wall's half on this side.
            double inner = side * gateOuter;
            for (double x = 0; x < Math.Abs(d) - gateOuter - 0.05; x += bay)
                Run(1, town.Gate, inner + side * x, Math.Min(bay, Math.Abs(d) - gateOuter - x), side > 0 ? -MathF.PI / 2 : MathF.PI / 2);
            for (double x = 0; x < Math.Abs(d) - 0.05; x += bay)
                Run(-1, town.Rear, side * x, Math.Min(bay, Math.Abs(d) - x), side > 0 ? -MathF.PI / 2 : MathF.PI / 2);
            var towers = new List<double> { town.Rear, town.Gate };
            for (double s = town.Rear + every; s < town.Gate - every / 2; s += every)
                towers.Add(s);
            foreach (double s in towers)
            {
                if (!Near(s, d))
                    continue;
                var tower = Piece($"tower-{side}", () => StructureKit.Tower(_look, side));
                mesh.Instances.Add(new MeshInstance(tower, Basis(start.Tangent, P(s, d), eye, 0)));
                if (!lit || (P(s, d) - eye).Length > 220)
                    continue;
                // Its lamp, facing into the town, a lit pool on the street below it.
                var lamp = (P(s, d) - right * (side * 2.2) + Double3.Up * 14.1).RelativeTo(eye);
                mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 1.4f, 16));
                mesh.Billboard(lamp, 2.2f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
            }
        }
    }

    /// <summary>A walled town's streets and lanes are laid in strips this long.</summary>
    const double TownChunk = 20;

    /// <summary>
    /// A walled town's streets and lanes (queue #74, note 335): their beaten surface along each street and across each lane,
    /// and lamp posts down them, lit near you, so you can find your way round in the dark.
    /// </summary>
    public void Streets(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to)
    {
        if (town.Plan.Bounds is not { } b)
            return;
        const double chunk = TownChunk;
        var post = Piece("square-lamppost", () => SquareKit.LampPost(_look));
        foreach (var st in b.Streets)
        {
            var piece = Piece($"town-street-{st.Width:0.0}", () => TownGround(_look, (float)chunk, (float)st.Width, "ballast"));
            for (double s = st.S0; s < st.S1; s += chunk)
            {
                double mid = Math.Min(s + chunk / 2, st.S1 - chunk / 2);
                var at = town.World(mid, st.D);
                if ((at - eye).Length > 320)
                    continue;
                mesh.Instances.Add(new MeshInstance(piece, Place(line, eye, at, mid, 1, 0)));
            }
            // A lamp post every 45 m, the street's side away from the line, alternating.
            int n = 0;
            for (double s = st.S0 + 12; s < st.S1; s += 45, n++)
            {
                double d = st.D + Math.Sign(st.D) * (n % 2 == 0 ? 1 : -1) * (st.Width / 2 + 0.6);
                var at = town.World(s, d);
                double far = (at - eye).Length;
                if (far > 260)
                    continue;
                var m = Place(line, eye, at, s, 0, -Math.Sign(d - st.D));
                mesh.Instances.Add(new MeshInstance(post, m));
                if (far > 120)
                    continue;
                var flame = Vector3.Transform(SquareKit.LampTop, m);
                mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 1.8f, 13));
                mesh.Billboard(flame, 0.9f, 0, new Vector4(Palette.LampAmber * 0.55f, 1), -1, FxBlend.Additive);
            }
        }
        foreach (var lane in b.Lanes)
        {
            var piece = Piece($"town-lane-{lane.Width:0.0}", () => TownGround(_look, (float)chunk, (float)lane.Width, "ground_mud"));
            foreach (double mid in LaneMids(lane, chunk))
            {
                var at = town.World(lane.S, mid);
                if ((at - eye).Length > 320)
                    continue;
                mesh.Instances.Add(new MeshInstance(piece, Place(line, eye, at, lane.S, 0, 1)));
            }
        }
    }

    /// <summary>
    /// The middles of a lane's strips of beaten ground, <paramref name="chunk"/> long across the line, leaving out the
    /// line and its bed (<see cref="Streets"/> draws them; what's underfoot reads them, <see cref="TownWay"/>).
    /// </summary>
    static IEnumerable<double> LaneMids(TownLane lane, double chunk)
    {
        for (double d = lane.D0; d < lane.D1; d += chunk)
        {
            double mid = Math.Min(d + chunk / 2, lane.D1 - chunk / 2);
            if (Math.Abs(mid) >= chunk / 2 + 4)
                yield return mid;
        }
    }

    /// <summary>A strip of beaten ground <paramref name="length"/> along its front (−Z) by <paramref name="width"/>, a hand over the ground.</summary>
    static MeshAsset TownGround(Look look, float length, float width, string texture)
    {
        var k = new Kit(look, 4100 + texture.Length);
        k.Use(texture, Palette.DeepBrown, 0.9f, 0.04f, tile: 2.5f);
        k.Shade(texture == "ballast" ? 0.85f : 1.0f);
        k.Box(new Vector3(-width / 2, -0.2f, -length / 2), new Vector3(width / 2, 0.035f, length / 2), Kit.Faces.PosY);
        return k.Build($"town-ground-{texture}-{length:0}x{width:0.0}");
    }

    /// <summary>A house's door lamp (<see cref="MaritimeKit.Porch"/>) lit, low where the custom keeps the windows dark.</summary>
    void Porch(MeshBuilder mesh, in Matrix4x4 m, TownHouse h, bool lit)
    {
        var lamp = Vector3.Transform(MaritimeKit.Porch(h, (float)_look.Doorway.Height), m);
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
