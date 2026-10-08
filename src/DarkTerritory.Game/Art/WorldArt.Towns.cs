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
    /// <summary>
    /// A walled town's green and what its people have put up (note 353): the grass and paths and their lamps, the statue's
    /// plinth, the wall of names, the bandstand, the lamp gardens, the trees, the flag, the laws' board, the day painted on
    /// the walls; each drawn where it is, near enough the view (the green's across the street, the murals at the back wall).
    /// </summary>
    public void Civic(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to)
    {
        var plan = town.Plan;
        int side = plan.Square.Side;
        foreach (var f in plan.Fixtures)
        {
            if (f.House >= 0 || !CivicKit.Draws(f.Kind) || f.S < from - 20 || f.S > to + 20)
                continue;
            var m = Place(line, eye, town.World(f.S, f.D), f.S, f.FaceS, f.FaceD);
            int variant = CivicKit.Variant(f.Kind, f.Name);
            mesh.Instances.Add(new MeshInstance(Piece($"civic-{f.Kind}-{variant}", () => CivicKit.Piece(_look, f.Kind, variant)), m));
            if (f.Kind == "garden")
                foreach (var lamp in CivicKit.GardenLamps())
                    mesh.PointLights.Add(new PointLight(Vector3.Transform(lamp, m), Palette.LampAmber * 1.4f, 6));
            if (f.Kind == "tree" && variant == 1)
                mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 3.0f, 0), m), Palette.LampAmber * 0.9f, 6));
            if (f.Kind is "mural" or "memorial")
                mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 3.2f, -3.5f), m), Palette.LampAmber * 1.3f, 11));
        }
        // A walled town's green across the street (note 353): its grass and paths, lamps at its corners and its middle.
        if (plan.Green is { } green && green.S1 > from - 20 && green.S0 < to + 20)
        {
            double gs = (green.S0 + green.S1) / 2, gd = side * (green.Near + green.Far) / 2;
            float length = (float)(green.S1 - green.S0), depth = (float)(green.Far - green.Near);
            var ground = Piece($"civic-green-{length:0}x{depth:0}", () => CivicKit.Green(_look, length, depth));
            mesh.Instances.Add(new MeshInstance(ground, Place(line, eye, town.World(gs, gd), gs, 0, -side)));
            var lamp = Piece("square-lamppost", () => SquareKit.LampPost(_look));
            foreach (var (ls, ld) in new[] { (green.S0 + 1.5, green.Near + 1.2), (green.S1 - 1.5, green.Near + 1.2), (gs, (green.Near + green.Far) / 2 + 1.5),
                (green.S0 + 1.5, green.Far - 1.2), (green.S1 - 1.5, green.Far - 1.2) })
            {
                var lm = Place(line, eye, town.World(ls, side * ld), ls, 0, -side);
                mesh.Instances.Add(new MeshInstance(lamp, lm));
                var flame = Vector3.Transform(SquareKit.LampTop, lm);
                mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 2.0f, 14));
                mesh.Billboard(flame, 0.9f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
            }
        }
    }

    /// <summary>
    /// A walled town's works (queue #90, note 353): each piece where the plan stands it, a facility's modelled piece
    /// (<see cref="WorksKit.Prop"/>) on its fixture's footprint, else the kit's; their lights; the works' cinder ground.
    /// </summary>
    public void Works(MeshBuilder mesh, RailLine line, Double3 eye, Town town, double from, double to)
    {
        var plan = town.Plan;
        if (plan.Works is not { } works || works.S1 < from - 60 || works.S0 > to + 60)
            return;
        foreach (var f in plan.Fixtures)
        {
            if (f.House >= 0 || !WorksKit.Draws(f.Kind) || !works.Holds(f.S, f.D, 1))
                continue;
            var at = town.World(f.S, f.D);
            if ((at - eye).Length > 420)
                continue;
            var front = Place(line, eye, at, f.S, f.FaceS, f.FaceD);
            if (WorksKit.Prop(f.Kind) is { } prop && _props.Get(prop.Name) is { } model)
            {
                // Its front along the line (the pit's pair, the ropes between them) or to the line; centred by its footprint.
                var m = prop.Along ? Place(line, eye, at, f.S, 1, 0) : front;
                var (middle, _) = prop.Centred ? WorksKit.Footprint(model) : (Vector3.Zero, Vector3.Zero);
                mesh.Instances.Add(new MeshInstance(model, Matrix4x4.CreateTranslation(-middle) * Matrix4x4.CreateScale(prop.Scale)
                    * Matrix4x4.CreateTranslation(0, -0.3f, 0) * m));
            }
            else
                mesh.Instances.Add(new MeshInstance(Piece($"works-{f.Kind}", () => WorksKit.Piece(_look, f.Kind)), front));
            foreach (var (lamp, colour, range) in WorksKit.Lights(f.Kind))
                mesh.PointLights.Add(new PointLight(Vector3.Transform(lamp, front), colour, range));
        }
    }

    /// <summary>Where the works' stacks and chimneys smoke (note 353): the foundry's stack, the winding house's chimney.</summary>
    public IEnumerable<Vector3> Stacks(RailLine line, Double3 eye, Town town, double reach)
    {
        if (town.Plan.Works is not { } works)
            yield break;
        foreach (var f in town.Plan.Fixtures)
        {
            if (!works.Holds(f.S, f.D, 1) || f.Kind is not ("casting" or "winding") || WorksKit.Prop(f.Kind) is not { } prop
                || _props.Get(prop.Name) is not { } model)
                continue;
            var at = town.World(f.S, f.D);
            if ((at - eye).Length > reach)
                continue;
            var m = prop.Along ? Place(line, eye, at, f.S, 1, 0) : Place(line, eye, at, f.S, f.FaceS, f.FaceD);
            var (middle, _) = WorksKit.Footprint(model);
            var top = WorksKit.StackTop(model);
            yield return Vector3.Transform(Vector3.Transform(top - middle, Matrix4x4.CreateScale(prop.Scale)) + new Vector3(0, -0.3f, 0), m);
        }
    }

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
            // Its front to the line, from whichever side it stands (the quiet house is on the far side from the square).
            var m = Place(line, eye, town.World(b.S, b.D), b.S, 0, -Math.Sign(b.D));
            mesh.Instances.Add(new MeshInstance(piece, m));
            if (b.Kind == "quiet")
                continue;
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
            // The green's and the walls' pieces are drawn with the green (Civic), wherever they are.
            if (CivicKit.Draws(f.Kind) || WorksKit.Draws(f.Kind))
                continue;
            var m = Place(line, eye, town.World(f.S, f.D), f.S, f.FaceS, f.FaceD);
            int count = f.Kind switch { "board" => notices, "line" => (int)(sq.S1 - sq.S0 - 8), _ => 0 };
            var piece = Piece($"square-{f.Kind}-{count}", () => SquareKit.Fixture(_look, f.Kind, count));
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

    /// <summary>
    /// Where wood smoke rises in a town (App. F.3, the director: the fortresses feel static): the chimneys of the houses
    /// lived in (and stood open) within <paramref name="reach"/> of you, relative to the eye. Nobody's fire in a house
    /// nobody lives in.
    /// </summary>
    public IEnumerable<Vector3> Chimneys(RailLine line, Double3 eye, Town town, double reach)
    {
        foreach (var h in town.Plan.Houses)
        {
            if (h.Kind is not (HouseKind.Lived or HouseKind.Open))
                continue;
            var at = town.World(h.S, h.D);
            if ((at - eye).Length > reach)
                continue;
            var m = Place(line, eye, at, h.S, 0, -h.Side);
            foreach (var top in MaritimeKit.ChimneyTops(h))
                yield return Vector3.Transform(top, m);
        }
    }

    /// <summary>
    /// The watch on a town's wall (App. F.3: the fortresses feel static): a guard with a lantern walking each stretch of
    /// wall between two towers, or most of them, up and back at a walk with a pause at each end, out of reach on the
    /// wall's walk. A walled town's side walls (note 335), or the yard's two (T124). Where each is now, by the clock: the
    /// feet, the way they face, and which of the crew's looks.
    /// </summary>
    public static IEnumerable<(Double3 Feet, Double3 Facing, int Variant)> Watch(Town town, double gate, double time)
    {
        var plan = town.Plan;
        var walls = plan.Bounds is { } b
            ? new[] { (-b.Left, b.Rear), (b.Right, b.Rear) }.Select(w => (D: w.Item1, From: w.Item2, To: b.Gate))
            : new[] { (-Sim.Run.Fortresses.WallOut, 0.0), (Sim.Run.Fortresses.WallOut, 0.0) }.Select(w => (D: w.Item1, From: w.Item2, To: gate));
        int n = 0;
        foreach (var (d, from, to) in walls)
            for (double s0 = from + 6; s0 + 30 < to - 6; s0 += Sim.Run.Fortresses.TowerEvery)
            {
                n++;
                double s1 = Math.Min(s0 + Sim.Run.Fortresses.TowerEvery - 12, to - 6);
                // Not every stretch has its man (one in four's empty), and not on a yard wall the square steps back from.
                if (n % 4 == 3 || plan.Bounds is null && Math.Sign(d) == plan.Square.Side && s1 > plan.Square.S0 && s0 < plan.Square.S1)
                    continue;
                double len = s1 - s0, speed = 0.9 + n % 3 * 0.12, pause = 4 + n % 5;
                // Up the stretch, a pause, back, a pause: a round of 2 (len / speed + pause) seconds, each on its own phase.
                double round = 2 * (len / speed + pause), t = (time + n * 37.3) % round;
                double along, dir;
                if (t < len / speed)
                    (along, dir) = (t * speed, 1);
                else if (t < len / speed + pause)
                    (along, dir) = (len, 1);
                else if (t < 2 * len / speed + pause)
                    (along, dir) = (len - (t - len / speed - pause) * speed, -1);
                else
                    (along, dir) = (0, -1);
                // On the walk's inner half, behind the parapet.
                double s = s0 + along;
                yield return (town.World(s, d - Math.Sign(d) * Sim.Run.Fortresses.WallHalf / 2, Sim.Run.Fortresses.WallWalk), town.Direction(s, dir, 0), n % 7 + 1);
            }
    }

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
                var at = town.World(mid, st.At(mid));
                if ((at - eye).Length > 320)
                    continue;
                // Turned along the street where it bends (note 353), a little longer so the pieces meet.
                double slope = (st.At(mid + 1) - st.At(mid - 1)) / 2;
                mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateScale(1, 1, (float)Math.Sqrt(1 + slope * slope) * 1.04f) * Place(line, eye, at, mid, 1, slope)));
            }
            // A lamp post every 45 m, the street's side away from the line, alternating.
            int n = 0;
            for (double s = st.S0 + 12; s < st.S1; s += 45, n++)
            {
                double d = st.At(s) + Math.Sign(st.D) * (n % 2 == 0 ? 1 : -1) * (st.Width / 2 + 0.6);
                var at = town.World(s, d);
                double far = (at - eye).Length;
                if (far > 260)
                    continue;
                var m = Place(line, eye, at, s, 0, -Math.Sign(d - st.At(s)));
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
            foreach (var (d, length, slope) in LaneStrips(lane, chunk))
            {
                double s = lane.At(d);
                var at = town.World(s, d);
                if ((at - eye).Length > 320)
                    continue;
                // Turned along the lane where it runs at an angle (note 353), cut to its stretch, a little long so they meet.
                mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateScale(1, 1, (float)(length / chunk) * 1.04f) * Place(line, eye, at, s, slope, 1)));
            }
            // Where it turns, a square of it, so the corner isn't a notch.
            foreach (var (kd, _) in lane.Kinks ?? [])
                if (Math.Abs(kd) >= LaneBed + lane.Width / 2 && kd > lane.D0 && kd < lane.D1)
                {
                    double ks = lane.At(kd), slope = (lane.At(kd + 1) - lane.At(kd - 1)) / 2;
                    var at = town.World(ks, kd);
                    if ((at - eye).Length <= 320)
                        mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateScale(1, 1, (float)(lane.Width / chunk)) * Place(line, eye, at, ks, slope, 1)));
                }
        }
    }

    /// <summary>How far either side of the line a lane leaves the line and its bed to the line's own ground (m).</summary>
    const double LaneBed = 4;

    /// <summary>
    /// A lane's strips of beaten ground, each at most <paramref name="chunk"/> long: where its middle is across the line
    /// (it's at <see cref="TownLane.At"/> along it), how long it is along the lane, and the lane's slope there (along the line
    /// per metre across it). Each straight stretch between its turns is cut into equal strips, leaving out the line and
    /// its bed (<see cref="Streets"/> draws them; what's underfoot reads them, <see cref="TownWay"/>).
    /// </summary>
    static IEnumerable<(double D, double Length, double Slope)> LaneStrips(TownLane lane, double chunk)
    {
        var cuts = new List<double> { lane.D0, -LaneBed, LaneBed, lane.D1 };
        foreach (var (kd, _) in lane.Kinks ?? [])
            if (kd > lane.D0 && kd < lane.D1 && Math.Abs(kd) > LaneBed)
                cuts.Add(kd);
        cuts.Sort();
        for (int i = 1; i < cuts.Count; i++)
        {
            double a = cuts[i - 1], b = cuts[i];
            if (b - a < 1e-6 || a >= -LaneBed && b <= LaneBed)
                continue;
            double slope = (lane.At(b) - lane.At(a)) / (b - a), along = (b - a) * Math.Sqrt(1 + slope * slope);
            int n = Math.Max(1, (int)Math.Ceiling(along / chunk - 1e-6));
            for (int j = 0; j < n; j++)
                yield return (a + (j + 0.5) * (b - a) / n, along / n, slope);
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
