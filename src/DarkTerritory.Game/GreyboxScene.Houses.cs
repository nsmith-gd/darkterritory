using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using HouseForm = DarkTerritory.Game.Art.HouseForm;
using HouseSite = DarkTerritory.Sim.Run.StopWalls.OpenHouse;

namespace DarkTerritory.Game;

/// <summary>
/// G1's house creatures, drawn in their houses (the director, 9 Oct 2026; notes 584–586), from the sim's replicated state
/// (each one's <see cref="Enemy.Extra2"/> its house, an index into the stop walls' <c>OpenHouses</c>):
/// <list type="bullet">
/// <item>the Hollow House is its house: lifted out of its cell (Art.WorldArt.Houses) and drawn shaking while it rumbles, its
/// doors slamming, sunk by its <see cref="Enemy.Extra"/> and tilting with the earth heaved up round it, then come down;</item>
/// <item>the Hanger hangs up under the ceiling it's given (joists at <see cref="Hanger.Ceiling"/>, the roof space dark over
/// them), its strands down into the room from it, and one taut to whoever it has;</item>
/// <item>the Householder's house is kept: its chair and its table set for one, a stove lit against a wall, the room warm.</item>
/// </list>
/// What's drawn of a house moved this way (its room, its light, its doors, its searched spots) moves with it.
/// </summary>
public sealed partial class GreyboxScene
{
    /// <summary>
    /// Each house a creature's moved this frame, by its stop's feature and its building: how (camera-relative, a point of the
    /// house as its cell drew it to where it's drawn now), whether it's come down, and whether it's the Householder's.
    /// </summary>
    readonly Dictionary<(int Feature, int Building), (Matrix4x4 Moved, bool Down, bool Kept)> _movedHouses = [];
    (Sim.Run.StopWalls Walls, List<HouseSite> Houses)? _openHouseList;
    readonly Dictionary<int, (double From, double To, double Since)> _hangerGrip = [];
    readonly Dictionary<int, (Double3 Seat, double Yaw)> _householderSeats = [];
    readonly Dictionary<int, (bool Shut, double Since)> _doorMoved = [];

    /// <summary>Where a Householder's chair is (as if it had been seen sitting there): for a staged one that's up and gone.</summary>
    public void Seat(int householder, Double3 seat, double yaw) => _householderSeats[householder] = (seat, yaw);

    /// <summary>How far a Hollow House tilts as it sinks (radians a metre, and at most), and how fast it comes down.</summary>
    const float HollowTilt = 0.065f, HollowTiltMost = 0.13f, HollowFallSeconds = 0.8f;

    /// <summary>The open house a house creature's <see cref="Enemy.Extra2"/> names, or null.</summary>
    HouseSite? HouseOf(Enemy e)
    {
        if (Walls is not { } walls)
            return null;
        if (_openHouseList is not { } cached || !ReferenceEquals(cached.Walls, walls))
            _openHouseList = cached = (walls, [.. walls.OpenHouses]);
        int i = (int)e.Extra2;
        return i >= 0 && i < cached.Houses.Count && cached.Houses[i].Feature >= 0 ? cached.Houses[i] : null;
    }

    /// <summary>
    /// The house creatures' houses, before the cells are drawn (a lifted house mustn't be in its cell this frame): the Hollow
    /// Houses and the Householders' houses drawn by their creatures' state, the Hangers in theirs.
    /// </summary>
    void HouseCreatures(MeshBuilder mesh, RailLine line, Double3 eye)
    {
        _movedHouses.Clear();
        if (Look is null || Route is null || Enemies is null || Walls is null)
            return;
        foreach (var e in Enemies)
        {
            if (e.Gone || e.Kind is not (EnemyKind.HollowHouse or EnemyKind.Householder or EnemyKind.Hanger) || HouseOf(e) is not { } house)
                continue;
            if (house.Origin.RelativeTo(eye).Length() > DrawDistance)
                continue;
            var f = Route.Features[house.Feature];
            if (f.Stop is null || house.Building >= f.Stop.Buildings.Count)
                continue;
            var frame = Art.WorldArt.HouseFrame(line, Route, f, house.Building, eye, (float)ValleyDepth);
            switch (e)
            {
                case HollowHouse hollow:
                    Hollow(mesh, hollow, house, frame, eye);
                    break;
                case Householder keeper:
                    Kept(mesh, keeper, house, frame, eye);
                    break;
            }
        }
        // The Hangers after: in a house that's moving, they move with it.
        foreach (var e in Enemies)
            if (!e.Gone && e is Hanger hanger && HouseOf(e) is { } house && house.Origin.RelativeTo(eye).Length() < 80)
                HangerIn(mesh, line, hanger, house, eye);
    }

    /// <summary>
    /// A Hollow House by its mode (HollowMode, the sim's Height): still, as the village has it; rumbling, shaking a little
    /// and the grit coming down; its doors slamming (GreyboxScene.HouseDoors swings them shut hard, dust off each); sinking,
    /// lowered into the ground by its Extra and tilting, the earth heaved up round it; coming down, crushed onto its heap; then
    /// the heap, sunk.
    /// </summary>
    void Hollow(MeshBuilder mesh, HollowHouse e, HouseSite house, in Matrix4x4 frame, Double3 eye)
    {
        var mode = e.Mode;
        double since = ModeSince(e, out _);
        float sink = (float)Math.Max(0, e.Extra);
        var key = (house.Feature, house.Building);
        var b = house.B;
        float halfX = (float)b.Width / 2, halfZ = (float)b.Length / 2;
        Look!.Art.World.Lift(Route!, house.Feature, house.Building);
        // Which way it goes over as it sinks (its own, from its id): about a level axis through its middle.
        float turn = (float)(e.Id * 2.399 % MathF.Tau);
        var axis = new Vector3(MathF.Cos(turn), 0, MathF.Sin(turn));
        float tilt = MathF.Min(HollowTiltMost, sink * HollowTilt);
        // Rumbling (and through the slams and the sinking) it shudders: a few millimetres, fast, and a hair of a rock.
        float shudder = mode switch { HollowMode.Rumble => 0.6f, HollowMode.Shut => 0.8f, HollowMode.Sink => 1f, HollowMode.Collapse => 1.2f, _ => 0 };
        double t = Time;
        var shake = shudder * new Vector3((float)(0.006 * Math.Sin(t * 41.3) + 0.003 * Math.Sin(t * 67.1)), (float)(0.004 * Math.Sin(t * 53.7)),
            (float)(0.006 * Math.Sin(t * 47.9 + 1.1)));
        float rock = shudder * (float)(0.0025 * Math.Sin(t * 23.1));
        var moved = Matrix4x4.CreateFromAxisAngle(axis, tilt + rock) * Matrix4x4.CreateTranslation(shake + new Vector3(0, -sink, 0));
        bool down = mode is HollowMode.Collapse && since >= HollowFallSeconds || mode is HollowMode.Settled;
        if (mode is HollowMode.Collapse && since < HollowFallSeconds)
        {
            // Coming down: the walls folding as the roof drives them into the ground.
            float k = (float)(since / HollowFallSeconds);
            moved = Matrix4x4.CreateScale(1 + 0.04f * k, 1 - 0.65f * k * k, 1 + 0.04f * k) * moved;
        }
        var drawn = moved * frame;
        Look.Art.World.House(mesh, Route!, house.Feature, house.Building, down ? HouseForm.Collapsed : HouseForm.Standing, drawn);
        Matrix4x4.Invert(frame, out var back);
        _movedHouses[key] = (back * drawn, down, false);
        // The earth heaved up round its walls as it goes down into it (in the ground's frame, not the house's).
        if (sink > 0.02f || down)
            Heave(mesh, b, frame, MathF.Min(0.75f, 0.2f + sink * 0.45f), e.Id);
        var fx = Look.Art.Effects;
        float shaking = mode switch { HollowMode.Rumble => 0.5f, HollowMode.Shut => 0.7f, HollowMode.Sink => 1f, _ => 0 };
        if (shaking > 0)
            fx.HouseRumble(mesh, drawn, halfX, halfZ, shaking, Time, e.Id);
        if (mode is HollowMode.Sink)
            fx.HouseSinking(mesh, frame, halfX, halfZ, Time, e.Id);
        // Its doors slamming, one after another (the sim shuts them; GreyboxScene.HouseDoors swings each shut hard): the dust
        // off each frame, in turn.
        if (mode is HollowMode.Shut && Walls is { } walls)
        {
            int n = 0;
            foreach (var d in walls.HouseDoors.Where(d => house.Doors.Contains(d.Key)))
            {
                var outward = Vector3.Normalize(ToF(d.Out));
                var local = Vector3.Transform(d.At.RelativeTo(eye), back) with { Y = 2.35f };
                var top = Vector3.Transform(local, drawn);
                fx.DoorSlam(mesh, top, outward, Vector3.Cross(Vector3.UnitY, outward), since - n++ * 0.35, d.Key);
            }
        }
        if (mode is HollowMode.Collapse or HollowMode.Settled)
            fx.HouseDown(mesh, frame, halfX + 0.5f, halfZ + 0.5f, mode is HollowMode.Settled ? 30 + since : since, e.Id);
    }

    /// <summary>
    /// The earth heaved up round a house's walls to <paramref name="height"/> (in its frame, unmoved: the ground stays where it
    /// is), a broken berm of it pushed up out of the ground and cracked, clods thrown out over the grass.
    /// </summary>
    void Heave(MeshBuilder mesh, Sim.Stops.StopBuilding b, in Matrix4x4 frame, float height, int seed)
    {
        var k = new Art.Kit(Look, mesh);
        k.Use("ground_mud", new Vector3(0.22f, 0.17f, 0.12f), 0.95f, 0.02f, tile: 2);
        float hx = (float)b.Width / 2 + 0.05f, hz = (float)b.Length / 2 + 0.05f;
        uint h = (uint)seed * 2654435761u | 1;
        float R()
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return (h & 0xFFFFFF) / 16777216f;
        }
        k.Push(frame);
        // Round the four sides: lumps of earth along the wall's foot, highest against it and sloping off outward.
        foreach (int side in new[] { 0, 1, 2, 3 })
        {
            bool alongZ = side < 2;
            float edge = alongZ ? hx : hz, run = alongZ ? hz : hx, sign = side % 2 == 0 ? 1 : -1;
            int n = Math.Max(2, (int)(run * 2 / 0.7f));
            for (int i = 0; i < n; i++)
            {
                float a0 = -run - 0.4f + (2 * run + 0.8f) * i / n, a1 = a0 + (2 * run + 0.8f) / n + 0.1f;
                float up = height * (0.6f + 0.6f * R()), out_ = 0.5f + height * (1.2f + 1.2f * R());
                Vector3 P(float along, float across, float y) => alongZ ? new(sign * across, y, along) : new(along, y, sign * across);
                // The berm's face: from out on the grass up to the wall, a lip over the gap it opened.
                var a = P(a0, edge + out_, 0.14f);
                var b2 = P(a1, edge + out_ * (0.8f + 0.4f * R()), 0.14f);
                var c = P(a1, edge - 0.02f, 0.15f + up);
                var d = P(a0, edge - 0.02f, 0.15f + up * (0.8f + 0.3f * R()));
                k.Quad(a, b2, c, d, twoSided: true);
                // A clod thrown out.
                if (R() < 0.6f)
                {
                    var clod = P(a0 + (a1 - a0) * R(), edge + out_ + 0.3f + 0.8f * R(), 0.15f);
                    float s = 0.08f + 0.12f * R();
                    k.Box(clod - new Vector3(s, 0.05f, s), clod + new Vector3(s, s * 0.8f, s));
                }
            }
        }
        // Inside, the boards it's gone down under: the earth come up through them, planks torn up out of it at angles.
        if (height > 0.3f)
        {
            k.Use("wood_grey", Palette.DeepBrown, 0.95f, 0.02f, tile: 1);
            int planks = 6 + (int)(b.Length * b.Width / 6);
            for (int i = 0; i < planks; i++)
            {
                var at = new Vector3((R() - 0.5f) * (hx * 2 - 1), 0.15f, (R() - 0.5f) * (hz * 2 - 1));
                float len = 0.5f + 0.9f * R();
                k.Push(Matrix4x4.CreateRotationX(0.15f + 0.6f * R()) * Matrix4x4.CreateRotationY(R() * MathF.Tau) * Matrix4x4.CreateTranslation(at));
                k.Box(new Vector3(-0.08f, -0.02f, -len / 2), new Vector3(0.08f, 0.02f, len / 2));
                k.Pop();
            }
        }
        k.Pop();
    }

    /// <summary>
    /// The Householder's house, kept (note 584): lifted and drawn with nothing underfoot (WorldArt.Houses), its chair at its
    /// seat facing the way it sits, its table set for one <see cref="Householder.TableOut"/> in front of it, and a stove lit
    /// against a wall clear of everything, the house warm with its light and the candle's.
    /// </summary>
    void Kept(MeshBuilder mesh, Householder e, HouseSite house, in Matrix4x4 frame, Double3 eye)
    {
        var key = (house.Feature, house.Building);
        Look!.Art.World.Lift(Route!, house.Feature, house.Building);
        Look.Art.World.House(mesh, Route!, house.Feature, house.Building, HouseForm.Kept, frame);
        _movedHouses[key] = (Matrix4x4.Identity, false, true);
        var household = Look.Art.Household;
        // Its seat: where it sat, as last seen sitting (risen and gone hunting, its chair's left where it was).
        if (e.Mode is HouseholderMode.Sit or HouseholderMode.Watch || !_householderSeats.ContainsKey(e.Id))
            _householderSeats[e.Id] = (e.Local, e.Lateral);
        var (seat, yaw) = _householderSeats[e.Id];
        var floor = Vector3.Transform(new Vector3(0, 0.17f, 0), frame);
        var facing = new Vector3((float)-Math.Sin(yaw), 0, (float)-Math.Cos(yaw));
        var at = seat.RelativeTo(eye) with { Y = floor.Y };
        Matrix4x4 Facing(Vector3 p, Vector3 forward) => Art.CreatureArt.Basis(p, Vector3.Cross(Vector3.UnitY, -forward), Vector3.UnitY, -forward);
        // Its chair, pushed back a little while it's up, the table square in front of it.
        var chair = at - (e.Mode is HouseholderMode.Sit or HouseholderMode.Watch ? Vector3.Zero : facing * 0.25f);
        mesh.Instances.Add(new MeshInstance(household.Chair, Facing(chair, facing)));
        var table = Facing(at + facing * (float)Householder.TableOut, facing);
        mesh.Instances.Add(new MeshInstance(household.SetTable, table));
        double t = Time * 9 + e.Id;
        float gutter = (float)(0.85 + 0.1 * Math.Sin(t) + 0.06 * Math.Sin(t * 2.7 + 0.4));
        var flame = Vector3.Transform(Art.HouseholdKit.TableFlame, table);
        mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 1.3f * gutter, 7));
        mesh.Billboard(flame, 0.2f * gutter, 0, new Vector4(Palette.LampAmber * 0.6f * gutter, 1), -1, FxBlend.Additive);
        // The stove against the wall clear of the table, the furniture and the doors, its fire's light low and red.
        if (StoveAt(house, Route!.Features[house.Feature].Stop!, e.Local, e.Lateral) is var (sx, sy, ox, oy))
        {
            var p = Vector3.Transform(new Vector3((float)sy, 0.17f, (float)-sx), frame);
            var inward = Vector3.Normalize(Vector3.TransformNormal(new Vector3((float)-oy, 0, (float)ox), frame));
            var stove = Facing(p, inward);
            mesh.Instances.Add(new MeshInstance(household.Stove, stove));
            double s = Time * 5.3 + e.Id * 0.7;
            float glow = (float)(0.85 + 0.1 * Math.Sin(s) + 0.07 * Math.Sin(s * 2.3 + 1) + 0.04 * Math.Sin(s * 7.1));
            var fire = Vector3.Transform(Art.HouseholdKit.StoveFire, stove);
            mesh.PointLights.Add(new PointLight(fire + inward * 0.35f, new Vector3(1.0f, 0.42f, 0.14f) * 3.2f * glow, 7.5f));
            mesh.Billboard(fire, 0.7f * glow, 0, new Vector4(new Vector3(1.0f, 0.4f, 0.1f) * 0.5f * glow, 1), -1, FxBlend.Additive);
            mesh.Billboard(fire - inward * 0.3f + Vector3.UnitY * 0.2f, 1.6f, 0, new Vector4(new Vector3(1.0f, 0.35f, 0.08f) * 0.12f * glow, 1), -1, FxBlend.Additive);
        }
        // The whole room warm: a lamp's worth of light up under the ceiling over the table, steady.
        mesh.PointLights.Add(new PointLight(Vector3.Transform(new Vector3(0, 0, 0), table) + new Vector3(0, 2.2f, 0), Palette.LampAmber * 0.55f, 9));
    }

    /// <summary>
    /// Where the Householder's stove stands (the house's plan: its back's middle against a wall's inside face, and the way out
    /// from that wall into the room): along the outside runs with no door, the spot furthest from the doors that's clear of the
    /// furniture, the solid clutter, the table and its chair. Null if none is.
    /// </summary>
    static (double X, double Y, double OutX, double OutY)? StoveAt(HouseSite house, Sim.Stops.StopLayout stop, Double3 seat, double yaw)
    {
        var b = house.B;
        double t = Sim.Run.StopWalls.WallThickness, depth = Art.HouseholdKit.StoveHalfDepth + 0.04, half = Art.HouseholdKit.StoveHalfWidth;
        var outline = Sim.Run.StopWalls.Outline(b);
        // The seat and the table in the plan.
        var d = seat - house.Origin;
        double sx = d.X * house.Ex.X + d.Z * house.Ex.Z, sy = d.X * house.Ey.X + d.Z * house.Ey.Z;
        var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        double fx = forward.X * house.Ex.X + forward.Z * house.Ex.Z, fy = forward.X * house.Ey.X + forward.Z * house.Ey.Z;
        var taken = new List<(double X, double Y, double R)>
        {
            (sx, sy, 0.7), (sx + fx * Householder.TableOut, sy + fy * Householder.TableOut, 0.8),
        };
        foreach (var (_, inside) in Sim.Run.StopWalls.Doorways(b))
            taken.Add((inside.X, inside.Y, 1.6));
        var solids = Sim.Run.StopWalls.Furniture(b, stop.Containers.Where(c => c.Building == house.Building)).Select(x => (x.X, x.Y, x.HalfX, x.HalfY))
            .Concat(Sim.Run.StopWalls.ClutterOf(stop, house.Building).Where(c => c.Solid).Select(c => (c.X, c.Y, c.Box.HalfX, c.Box.HalfY))).ToList();
        (double, double, double, double)? best = null;
        double score = double.MinValue;
        for (int i = 0; i < outline.Runs.Count; i++)
        {
            var r = outline.Runs[i];
            if (r.Inner || outline.Doors.Contains(i))
                continue;
            for (double a = Math.Min(r.A, r.B) + 0.7; a <= Math.Max(r.A, r.B) - 0.7; a += 0.25)
            {
                // (The run's inside face, the stove's back against it.)
                double across = r.At - r.Out * (t / 2 + depth);
                double x = r.AlongX ? a : across, y = r.AlongX ? across : a;
                double ox = r.AlongX ? 0 : -r.Out, oy = r.AlongX ? -r.Out : 0;
                if (taken.Any(p => Math.Abs(p.X - x) < p.R + half && Math.Abs(p.Y - y) < p.R + half))
                    continue;
                if (solids.Any(s => Math.Abs(s.X - x) < s.Item3 + half + 0.1 && Math.Abs(s.Y - y) < s.Item4 + half + 0.1))
                    continue;
                double far = taken.Skip(2).Select(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y)).DefaultIfEmpty(5).Min();
                if (far > score)
                    (best, score) = ((x, y, ox, oy), far);
            }
        }
        return best;
    }

    /// <summary>
    /// The Hanger in its house (note 586): its ceiling (Art.HouseholdKit.Ceiling: joists, a few boards, the rafters over them),
    /// its strands (<see cref="Hanger.Strands"/>, as many as its tuning hangs) hanging from the joists down to
    /// <see cref="Hanger.StrandBottom"/>, glistening, swaying a little, each with a bead at its end; and itself at its body
    /// (<see cref="Enemy.Local"/>): hidden or retreating, up in the roof space between the boards; on someone, down under the
    /// joists, one strand from its mouth taut to their chest.
    /// </summary>
    void HangerIn(MeshBuilder mesh, RailLine line, Hanger e, HouseSite house, Double3 eye)
    {
        var f = Route!.Features[house.Feature];
        var b = house.B;
        var frame = Art.WorldArt.HouseFrame(line, Route, f, house.Building, eye, (float)ValleyDepth);
        if (_movedHouses.TryGetValue((house.Feature, house.Building), out var moved))
        {
            if (moved.Down)
                return;
            frame *= moved.Moved;
        }
        const float Floor = 0.17f, Eaves = 3.0f;
        float ceiling = Floor + (float)Hanger.Ceiling;
        var household = Look!.Art.Household;
        var parts = b.Parts.Count > 0 && Sim.Run.StopWalls.Composite(b) ? b.Parts : null;
        if (parts is null)
            mesh.Instances.Add(new MeshInstance(household.Ceiling((float)b.Width, (float)b.Length, ceiling, Eaves, e.Id), frame));
        else
            foreach (var p in parts)
            {
                bool alongAxis = p.Length >= p.Width;
                var local = (alongAxis ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI / 2)) * Matrix4x4.CreateTranslation((float)p.Y, 0, (float)-p.X);
                mesh.Instances.Add(new MeshInstance(household.Ceiling((float)Math.Min(p.Length, p.Width), (float)Math.Max(p.Length, p.Width), ceiling, Eaves, e.Id), local * frame));
            }
        // Whoever it has: the strand to them is the one nearest them, gone taut from its mouth.
        var mode = e.Mode;
        Crewmate? held = null;
        if (e.Extra >= 0 && mode is HangerMode.Grab or HangerMode.Haul or HangerMode.Hold && Crew is { } crew)
            foreach (var c in crew)
                if (c.Id == (int)e.Extra && c.Alive)
                    held = c;
        Matrix4x4.Invert(frame, out var toHouse);
        var strands = Hanger.Strands(house, e.Id, Look.Art.Creatures.HangerTuning.Strands);
        int taut = -1;
        if (held is { } victim)
        {
            var v = Vector3.Transform(victim.Feet.RelativeTo(eye), toHouse);
            taut = Enumerable.Range(0, strands.Count).MinBy(i => Vector2.Distance(new((float)strands[i].Y, (float)-strands[i].X), new(v.X, v.Z)));
        }
        var k = new Art.Kit(Look, mesh);
        k.Push(frame);
        for (int i = 0; i < strands.Count; i++)
        {
            if (i == taut)
                continue;
            var (sx, sy) = strands[i];
            // Hung from the joists' undersides (the plan's x on −Z, its y on X), swaying a hand's breadth in the air that moves
            // through the house; the bead at its end heavier, so it swings most.
            double s = Time * (0.55 + 0.1 * (i % 3)) + i * 1.9 + e.Id;
            var swing = new Vector3((float)(0.035 * Math.Sin(s)), 0, (float)(0.028 * Math.Sin(s * 0.83 + 1.3)));
            var top = new Vector3((float)sy, ceiling, (float)-sx);
            var bottom = new Vector3((float)sy, Floor + (float)Hanger.StrandBottom, (float)-sx) + swing;
            Strand(k, top, bottom, 0.005f);
            Bead(k, bottom);
        }
        // Itself: up in the roof space while it's hidden or going back there, down under the joists while it's at someone.
        double grip = mode is HangerMode.Hidden or HangerMode.Retreat ? ceiling + 0.75 : ceiling;
        if (!_hangerGrip.TryGetValue(e.Id, out var g) || Time < g.Since)
            _hangerGrip[e.Id] = g = (grip, grip, Time);
        else if (g.To != grip)
            _hangerGrip[e.Id] = g = (g.From + (g.To - g.From) * Math.Clamp((Time - g.Since) / 0.35, 0, 1), grip, Time);
        double u = Math.Clamp((Time - g.Since) / 0.35, 0, 1);
        float y = (float)(g.From + (g.To - g.From) * u * u * (3 - 2 * u));
        var body = Vector3.Transform(e.Local.RelativeTo(eye), toHouse);
        var at = new Vector3(body.X, y, body.Z);
        // It faces whoever it has, else its own way (its id).
        var facing = new Vector3(MathF.Sin(e.Id * 1.7f), 0, MathF.Cos(e.Id * 1.7f));
        if (held is { } them)
        {
            var to = Vector3.Transform(them.Feet.RelativeTo(eye), toHouse) - at;
            if (to.X * to.X + to.Z * to.Z > 0.01f)
                facing = Vector3.Normalize(to with { Y = 0 });
        }
        var model = Art.CreatureArt.Basis(at, Vector3.Cross(Vector3.UnitY, -facing), Vector3.UnitY, -facing);
        Look.Art.Creatures.Hanger(mesh, model * frame, mode, ModeSince(e, out _), e.Id);
        if (held is { } caught)
        {
            var mouth = Vector3.Transform(Art.CreatureArt.HangerMouth, model);
            var chest = Vector3.Transform((caught.Feet + Double3.Up * 1.3).RelativeTo(eye), toHouse);
            Strand(k, mouth, chest, 0.008f);
        }
        k.Pop();
    }

    /// <summary>A strand, wet and pale, from <paramref name="a"/> to <paramref name="b"/>: a thin glassy thread, glinting in a lamp.</summary>
    static void Strand(Art.Kit k, Vector3 a, Vector3 b, float radius)
    {
        k.Use("cream", new Vector3(0.8f, 0.79f, 0.72f), 0.05f, 0.95f, tile: 0.2f);
        k.Tint = new Vector3(0.95f, 0.94f, 0.88f);
        k.Cylinder(a, b, radius, 4, caps: false);
    }

    /// <summary>The bead at a strand's end: a drop of something yellowish and sticky, glistening.</summary>
    static void Bead(Art.Kit k, Vector3 end)
    {
        k.Use("cream", new Vector3(0.75f, 0.64f, 0.36f), 0.05f, 0.98f, tile: 0.2f);
        k.Tint = new Vector3(0.85f, 0.72f, 0.42f);
        k.Lathe(end - new Vector3(0, 0.03f, 0), [new(0.002f, 0), new(0.012f, 0.008f), new(0.014f, 0.017f), new(0.008f, 0.028f), new(0.004f, 0.036f)], 8);
    }

    /// <summary>Which of the route's features <paramref name="f"/> is (its index), or −1.</summary>
    int FeatureIndex(Sim.Route.RouteFeature f)
    {
        for (int i = 0; i < (Route?.Features.Count ?? 0); i++)
            if (ReferenceEquals(Route!.Features[i], f))
                return i;
        return -1;
    }

    /// <summary>
    /// A door's swing toward where it's going (note 401's doors, drawn from the sim's state): it swings to over a moment, not
    /// at once, and slams (shut fast, a little bounce, dust off its frame). Returns how far open (1 open, 0 shut) and how long
    /// since it slammed shut (s, or −1).
    /// </summary>
    (float Open, double Slammed) DoorSwing(int key, bool shut)
    {
        if (!_doorMoved.TryGetValue(key, out var was) || Time < was.Since)
        {
            _doorMoved[key] = (shut, double.NegativeInfinity);
            return (shut ? 0 : 1, -1);
        }
        if (was.Shut != shut)
            _doorMoved[key] = was = (shut, Time);
        double age = Time - was.Since;
        if (!shut)
            return ((float)Math.Clamp(age / 0.5, 0, 1), -1);
        // Shut: slammed over a fifth of a second, bouncing off the frame once.
        float k = (float)Math.Clamp(age / 0.18, 0, 1);
        float open = (1 - k * k) + (age > 0.18 && age < 0.38 ? 0.06f * (float)Math.Sin((age - 0.18) / 0.2 * Math.PI) : 0);
        return (open, double.IsInfinity(was.Since) ? -1 : age);
    }
}
