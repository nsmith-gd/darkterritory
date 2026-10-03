using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Art;

/// <summary>The line's set pieces as the art pass places them: bridges, tunnels, fortresses, facilities, branches.</summary>
public sealed partial class WorldArt
{
    /// <summary>How high the hill stands over a tunnel, above rail height.</summary>
    const float RidgeHeight = 11.4f;

    /// <summary>How much hill there is at <paramref name="s"/>: 1 over a tunnel's bore, easing down its approaches.</summary>
    public static float Ridge(Route? route, double s, out bool inside)
    {
        inside = false;
        if (route is null)
            return 0;
        float best = 0;
        foreach (var f in route.Features)
        {
            if (f.Kind != FeatureKind.Tunnel || s < f.Start - 45 || s > f.End + 45)
                continue;
            if (s >= f.Start + 1.8 && s <= f.End - 1.8)
            {
                inside = true;
                return 1;
            }
            double out_ = s < f.Start ? f.Start - s : s > f.End ? s - f.End : 0;
            float g = (float)Math.Clamp(1 - out_ / 45, 0, 1);
            best = MathF.Max(best, g * g * (3 - 2 * g));
        }
        return best;
    }

    /// <summary>The hill's height at a point: over the bore everywhere across, and on the approaches only off to the sides (a cutting).</summary>
    static float Hill(Route? route, double s, float lateral)
    {
        float r = Ridge(route, s, out bool inside);
        if (r <= 0)
            return 0;
        float a = MathF.Abs(lateral);
        float across = inside ? 1 : Math.Clamp((a - 5) / 14, 0, 1);
        // Rounded off towards its flanks, far out.
        float flank = 1 - Math.Clamp((a - 45) / 55, 0, 1) * 0.7f;
        return RidgeHeight * r * across * flank;
    }

    /// <summary>Along-line distances the ground's rows must fall on: the ends of bores and bridges, so the hill meets its portal.</summary>
    static IEnumerable<double> Breaks(Route? route, double from, double to)
    {
        if (route is null)
            yield break;
        foreach (var f in route.Features)
        {
            if (f.Kind == FeatureKind.Tunnel)
                foreach (double s in new[] { f.Start - 0.05, f.Start + 1.8, f.End - 1.8, f.End + 0.05 })
                    if (s > from && s < to)
                        yield return s;
            if (f.Kind == FeatureKind.Bridge)
                foreach (double s in new[] { f.Start, f.End })
                    if (s > from && s < to)
                        yield return s;
        }
    }

    /// <summary>
    /// A bridge (GDD §17): a masonry viaduct of round arches over the gorge (the art sheet's), an iron girder viaduct on
    /// steel towers, or an iron through truss; a weak one, one that takes only so many cars, or the plan's timber trestle,
    /// on raked bents, and you can see why.
    /// </summary>
    /// <param name="type">What a generated line's plan built it as (linegen plan §12.3): a girder viaduct or a truss in iron,
    /// a timber trestle, or stone. Unset (a hand-laid line), the masonry viaduct, or the trestle where it's weak.</param>
    public void Bridge(MeshBuilder mesh, RailLine line, RouteFeature f, Double3 eye, double from, double to, float depth,
        Sim.LineGen.StructureType? type = null)
    {
        bool weak = f.MaxCars > 0 || type == Sim.LineGen.StructureType.Trestle;
        float step = weak ? StructureKit.Bent : StructureKit.Bay;
        int count = (int)Math.Ceiling((f.End - f.Start) / step);
        for (int i = 0; i < count; i++)
        {
            double s = f.Start + i * step;
            if (s + step < from - 30 || s > to + 30)
                continue;
            var t = line.Sample(s);
            if ((t.Position - eye).Length > 450)
                continue;
            bool last = i == count - 1;
            var piece = weak
                ? Piece($"trestle-{depth:0}", () => StructureKit.TrestleBent(_look, depth))
                : type == Sim.LineGen.StructureType.Girder ? Piece($"girder-{depth:0}-{last}", () => StructureKit.GirderBay(_look, depth, last))
                : type == Sim.LineGen.StructureType.Truss ? Piece($"truss-{depth:0}-{last}", () => StructureKit.TrussSpan(_look, depth, last))
                : Piece($"viaduct-{depth:0}-{last}", () => StructureKit.ViaductBay(_look, depth, last));
            mesh.Instances.Add(new MeshInstance(piece, Basis(t.Tangent, t.Position, eye, 0)));
        }
    }

    /// <summary>A tunnel: a portal at each end facing out, the bore lined in sooted brick between; the hill is the ground's.</summary>
    public void Tunnel(MeshBuilder mesh, RailLine line, RouteFeature f, Double3 eye, double from, double to)
    {
        const double step = 10;
        for (double s = f.Start; s < f.End; s += step)
        {
            if (s + step < from || s > to)
                continue;
            var t = line.Sample(s);
            float len = (float)Math.Min(step, f.End - s) + 0.05f;
            mesh.Instances.Add(new MeshInstance(Piece($"tunnel-{len:0.0}", () => StructureKit.TunnelLining(_look, len)), Basis(t.Tangent, t.Position, eye, 0)));
        }
        // Faces in the walls (tools/models wall_face): deep in the bore, one on each side, where the brick has bulged
        // round a face pushing out through it at a man's height. The headlamp finds one as the train goes by.
        if (_props.Get("wall_face") is { } faces)
            foreach (var (along, side) in new[] { (0.42, 1), (0.71, -1) })
            {
                double s = f.Start + (f.End - f.Start) * along;
                if (s < from || s > to || f.End - f.Start < 40)
                    continue;
                var t = line.Sample(s);
                var at = Matrix4x4.CreateRotationY(side > 0 ? MathF.PI / 2 : -MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(side * (StructureKit.TunnelHalf - 0.015f), 0.15f, 0) * Basis(t.Tangent, t.Position, eye, 0);
                mesh.Instances.Add(new MeshInstance(faces, at));
            }
        var portal = Piece("portal", () => StructureKit.Portal(_look));
        if (f.Start >= from - 50 && f.Start <= to + 50)
        {
            var t = line.Sample(f.Start);
            mesh.Instances.Add(new MeshInstance(portal, Basis(t.Tangent, t.Position, eye, 0)));
        }
        if (f.End >= from - 50 && f.End <= to + 50)
        {
            var t = line.Sample(f.End);
            mesh.Instances.Add(new MeshInstance(portal, Basis(t.Tangent, t.Position, eye, MathF.PI)));
        }
    }

    /// <summary>
    /// A fortress (GDD §9: "lights, then walls, then gun towers"): walls both sides, gun towers every 120 m with their
    /// lamps burning, the gatehouse over the line, and at the home fortress the station platform under its canopy,
    /// a lantern to every bay (the sheet's fortified station: warm pools, and the dark between them).
    /// </summary>
    /// <param name="lit">A town that has stopped answering (linegen plan §22.4) stands dark: its lamps are out.</param>
    /// <param name="time">Seconds, for the searchlights' sweep.</param>
    public void Fortress(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double start, double end, double gateAt, bool platform, bool lit = true,
        double time = 0)
    {
        double a = Math.Max(start, from), b = Math.Min(end, to);
        if (a >= b)
            return;
        foreach (int side in new[] { -1, 1 })
        {
            var wall = Piece($"wall-{side}", () => StructureKit.Wall(_look, side));
            for (double s = Math.Floor(a / 10) * 10; s < b; s += 10)
            {
                var t = line.Sample(s);
                var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                mesh.Instances.Add(new MeshInstance(wall, Basis(t.Tangent, t.Position + r * (side * 14.8), eye, 0)));
            }
            var tower = Piece($"tower-{side}", () => StructureKit.Tower(_look, side));
            for (double s = Math.Ceiling(a / 120) * 120; s < b; s += 120)
            {
                var t = line.Sample(s);
                var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var at = t.Position + r * (side * 14.8);
                mesh.Instances.Add(new MeshInstance(tower, Basis(t.Tangent, at, eye, 0)));
                // The tower's lamp over the line, a lit pool on the tracks below it.
                var lamp = (at + r * (-side * 2.2) + Double3.Up * 14.1).RelativeTo(eye);
                if (!lit)
                    continue;
                mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 1.4f, 16));
                mesh.Billboard(lamp, 2.2f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
                // Every other tower a searchlight on its outer face (the checklist's fortress: its searchlights): a hard
                // cold beam sweeping slowly over the ground outside the walls, each tower's on its own beat.
                if (((int)(s / 120) + (side > 0 ? 1 : 0)) % 2 == 0)
                {
                    var lens = (at + r * (side * 1.6) + Double3.Up * 15.2).RelativeTo(eye);
                    float phase = (float)(s * 0.013) + side;
                    float sweep = 0.9f * MathF.Sin((float)time * 0.22f + phase);
                    var outward = new Vector3((float)(r.X * side), 0, (float)(r.Z * side));
                    var along = Vector3.Normalize(new Vector3((float)t.Tangent.X, 0, (float)t.Tangent.Z));
                    var dir = Vector3.Normalize(outward * MathF.Cos(sweep) + along * MathF.Sin(sweep) - Vector3.UnitY * 0.2f);
                    Effects.Beam(mesh, lens, dir, 5.5f, 110, new Vector3(0.55f, 0.6f, 0.7f) * 0.2f);
                    mesh.Billboard(lens, 1.3f, 0, new Vector4(0.9f, 0.95f, 1.0f, 1), -1, FxBlend.Additive);
                    mesh.Billboard(lens, 4.5f, 0, new Vector4(0.3f, 0.33f, 0.4f, 1), -1, FxBlend.Additive);
                }
            }
        }
        if (lit)
            FortVillage(mesh, line, eye, a, b, start, end, gateAt, platform);
        if (gateAt >= from && gateAt <= to)
        {
            var t = line.Sample(gateAt);
            var gm = Basis(t.Tangent, t.Position, eye, 0);
            mesh.Instances.Add(new MeshInstance(Piece("gatehouse", () => StructureKit.Gatehouse(_look)), gm));
            var lamp = (t.Position + Double3.Up * 8.0 + t.Tangent * -3.8).RelativeTo(eye);
            if (lit)
                mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 1.6f, 14));
            if (lit)
                mesh.Billboard(lamp, 1.8f, 0, new Vector4(Palette.LampAmber * 0.7f, 1), -1, FxBlend.Additive);
            // Either side of the way in, a lamp post whose lantern holds a skull where the flame should be (tools/models
            // skull_lantern): the first thing on the line that says what this place has become.
            if (lit && _props.Get("skull_lantern") is { } skulls)
                foreach (int side in new[] { -1, 1 })
                {
                    var at = Matrix4x4.CreateRotationY(side > 0 ? MathF.PI / 2 : -MathF.PI / 2) * Matrix4x4.CreateTranslation(side * 4.6f, 0, 6f) * gm;
                    mesh.Instances.Add(new MeshInstance(skulls, at));
                    if (_props.Socket("skull_lantern", "lamp") is { } s)
                    {
                        // A candle guttering under the skull, so its brow and teeth catch the light from below and
                        // the sockets stay black; a small halo at the flame, not over the face.
                        var flame = Vector3.Transform(s + new Vector3(0, -0.2f, 0), at);
                        mesh.PointLights.Add(new PointLight(flame, new Vector3(1.0f, 0.55f, 0.25f) * 1.1f, 6));
                        mesh.Billboard(flame, 0.35f, 0, new Vector4(0.6f, 0.32f, 0.12f, 1), -1, FxBlend.Additive);
                    }
                }
        }
        if (!platform)
            return;
        // The platform: behind the gate, a few hundred metres of it, right of the line.
        double p0 = Math.Max(a, start + 60), p1 = Math.Min(b, gateAt - 30);
        for (double s = Math.Floor(p0 / 8) * 8; s < p1; s += 8)
        {
            if ((line.Sample(s).Position - eye).Length > 260)
                continue;
            int v = (int)(s / 8) % 3;
            var t = line.Sample(s);
            var m = Basis(t.Tangent, t.Position, eye, 0);
            var hand = _props.Get("hand_lantern");
            mesh.Instances.Add(new MeshInstance(Piece($"platform-{v}:{hand is not null}", () => StructureKit.PlatformBay(_look, v, lantern: hand is null)), m));
            if (hand is not null)
            {
                // The sourced lantern on the bay's bracket, a little bigger than a hand lamp: a station's.
                var flame = _props.Socket("hand_lantern", "lamp") ?? Vector3.Zero;
                mesh.Instances.Add(new MeshInstance(hand, Matrix4x4.CreateTranslation(-flame) * Matrix4x4.CreateScale(1.35f) * Matrix4x4.CreateTranslation(StructureKit.Lantern) * m));
            }
            var lantern = Vector3.Transform(StructureKit.Lantern, m);
            mesh.PointLights.Add(new PointLight(lantern, Palette.LampAmber * 1.5f, 9));
            mesh.Billboard(lantern, 0.9f, 0, new Vector4(Palette.LampAmber * 0.7f, 1), -1, FxBlend.Additive);
        }
    }

    /// <summary>How far apart the houses inside a fortress's walls stand along it, and how far out from the line.</summary>
    const double HouseEvery = 15, HouseOut = 10.9;

    /// <summary>Along a fortress's line, clear of its gate: where its houses stand.</summary>
    static bool InTheVillage(double s, double start, double end, double gateAt) =>
        s > start + 25 && s < end - 25 && Math.Abs(s - gateAt) > 30;

    /// <summary>The home fortress's platform, right of the line behind its gate (<see cref="Fortress"/>), which no house stands on.</summary>
    static bool OnThePlatform(double s, int side, double start, double gateAt, bool platform) =>
        platform && side > 0 && s > start + 50 && s < gateAt - 20;

    /// <summary>
    /// The village the walls keep (T100 playtest: "fort villages should look like protected villages btw with people
    /// around"): whole houses down both sides between the line and the walls, fronts to the line, a lamp in a window here
    /// and there and the odd one lighting the ground in front, gaps between them. Laid out from where they stand, so it
    /// doesn't change as you pass.
    /// </summary>
    void FortVillage(MeshBuilder mesh, RailLine line, Double3 eye, double a, double b, double start, double end, double gateAt, bool platform)
    {
        for (double s = Math.Ceiling(a / HouseEvery) * HouseEvery; s < b; s += HouseEvery)
        {
            if (!InTheVillage(s, start, end, gateAt))
                continue;
            var t = line.Sample(s);
            if ((t.Position - eye).Length > 320)
                continue;
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            foreach (int side in new[] { -1, 1 })
            {
                int h = (int)(s / HouseEvery) * 7 + (side > 0 ? 3 : 0);
                if (h % 5 == 0 || OnThePlatform(s, side, start, gateAt, platform))
                    continue;
                int v = h % 6;
                var at = t.Position + r * (side * HouseOut);
                mesh.Instances.Add(new MeshInstance(Piece($"lived-house-{v}", () => TownKit.LivedHouse(_look, v)), Basis(t.Tangent, at, eye, side * MathF.PI / 2)));
                if (h % 3 != 0)
                    continue;
                var glow = (at - r * (side * (TownKit.LivedDepth / 2 + 0.8)) + Double3.Up * 1.4).RelativeTo(eye);
                mesh.PointLights.Add(new PointLight(glow, Palette.LampAmber * 0.9f, 7));
            }
        }
    }

    /// <summary>
    /// The few about in a fortress at night (T100: "not a ton because its night"): a watchman either side of the gate, and
    /// here and there someone standing in front of their house facing the line, the train that's leaving or come in. Where
    /// each stands (feet, world) and which way they face (world, level), within <paramref name="reach"/> of the eye.
    /// </summary>
    public static IEnumerable<(Double3 Feet, Double3 Facing, int Variant)> FortFolk(RailLine line, Double3 eye, double start, double end, double gateAt, bool platform, double reach = 160)
    {
        if (gateAt >= 0 && gateAt <= line.Length)
        {
            var g = line.Sample(gateAt);
            var gr = Double3.Cross(g.Tangent, Double3.Up).Normalized;
            // Inside the gate, back from the skull lanterns, facing across the line at each other. (A sample's tangent
            // points back down the line, as the kit's frames have it: +Z is behind.)
            foreach (int side in new[] { -1, 1 })
            {
                var feet = g.Position + gr * (side * 5.4) + g.Tangent * (start == 0 ? 9 : -9);
                if ((feet - eye).Length < reach)
                    yield return (feet, gr * -side, side > 0 ? 1 : 2);
            }
        }
        for (double s = Math.Ceiling(start / HouseEvery) * HouseEvery; s < end; s += HouseEvery)
        {
            if (!InTheVillage(s, start, end, gateAt))
                continue;
            int n = (int)(s / HouseEvery);
            // One house in four or so has someone out in front of it.
            if (n * 37 % 11 > 2)
                continue;
            int side = n % 2 == 0 ? -1 : 1;
            if (OnThePlatform(s, side, start, gateAt, platform) || (n * 7 + (side > 0 ? 3 : 0)) % 5 == 0)
                continue;
            var t = line.Sample(s);
            var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var feet = t.Position + r * (side * (HouseOut - TownKit.LivedDepth / 2 - 1.3)) + t.Tangent * (n % 3 - 1);
            if ((feet - eye).Length < reach)
                yield return (feet, r * -side, 3 + n % 5);
        }
    }

    /// <summary>A facility's buildings beside a track (the main line or its spur) at <paramref name="mid"/>, and its one working lamp.</summary>
    public void Facility(MeshBuilder mesh, RailLine line, Double3 eye, FacilityKind? kind, double mid, double side, double push)
    {
        side = side == 0 ? 1 : side;
        var t = line.Sample(Math.Clamp(mid, 0, line.Length));
        if ((t.Position - eye).Length > 500)
            return;
        var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var at = t.Position + r * (side * push);
        mesh.Instances.Add(new MeshInstance(Piece($"facility-{kind}-{side}", () => StructureKit.Facility(_look, kind, (int)side)), Basis(t.Tangent, at, eye, 0)));
        var lamp = (at + r * (side * 4) + Double3.Up * 5.1).RelativeTo(eye);
        mesh.PointLights.Add(new PointLight(lamp, Palette.LampAmber * 1.3f, 12));
        mesh.Billboard(lamp, 1.4f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
    }

    /// <summary>
    /// A branch off the main line (GDD §17, App. A.7): its bed, sleepers and rails from the points, the buffer stop at its
    /// end with a red lamp, and the switch stand with its lever and target lamp (green for the main line, red for the
    /// branch: the lamp is how the cab reads a switch before it's on it).
    /// </summary>
    public void Branch(MeshBuilder mesh, Branch branch, Double3 eye, float drawDistance, Double3 lever, Double3 toeTangent, bool diverging,
        float flicker = 1)
    {
        var local = branch.Local;
        const double start = 4, step = 5;
        var k = new Kit(_look, mesh) { SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z)), Baked = 0 };
        var origin = k.SurfaceOrigin;
        int ballast = _look.Layer("ballast"), mud = _look.Layer("ground_mud");
        float tile = ballast >= 0 && _look.Textures[ballast].TileMetres is { } tm ? tm : 1.5f;
        var tint = ballast >= 0 ? Vector3.One : Palette.Ballast;
        // Its bed: level with the main line's where they overlap, on a low embankment of its own beyond.
        float[] lat = [-2.6f, -1.8f, 1.8f, 2.6f];
        float[] ht = [-0.35f, 0.01f, 0.01f, -0.35f];
        for (double s = start; s < local.Length; s += step)
        {
            var a = local.Sample(s);
            var b = local.Sample(Math.Min(s + step, local.Length));
            if ((a.Position - eye).Length > drawDistance)
                continue;
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            for (int c = 0; c < 3; c++)
            {
                Vector3 P(TrackSample t, Double3 r, int i) => (t.Position + r * lat[i] + Double3.Up * ht[i]).RelativeTo(eye);
                var corner = new Corner(tint * (c == 1 ? 1 : 0.8f), c == 1 ? 0 : 0.6f);
                Quad(mesh, P(a, ra, c), P(a, ra, c + 1), P(b, rb, c + 1), P(b, rb, c), corner, corner, corner, corner, origin, ballast, mud, tile);
            }
        }
        Rails(k, local, eye, start, local.Length, s => (local.Sample(s).Position - eye).Length < 150, s => (local.Sample(s).Position - eye).Length < drawDistance);

        var end = local.Sample(local.Length);
        // An alternate has no end of its own: it runs back into the main line (linegen plan §6.2).
        if (!branch.Rejoins && (end.Position - eye).Length < drawDistance)
        {
            mesh.Instances.Add(new MeshInstance(Piece("buffer-stop", () => StructureKit.BufferStop(_look)), Basis(end.Tangent, end.Position, eye, 0)));
            var lamp = (end.Position + Double3.Up * 1.4 - end.Tangent * 0.13).RelativeTo(eye);
            mesh.Billboard(lamp, 0.25f, 0, new Vector4(0.9f, 0.12f, 0.06f, 1), -1, FxBlend.Additive);
            mesh.Billboard(lamp, 1.2f, 0, new Vector4(0.5f, 0.06f, 0.03f, 1), -1, FxBlend.Additive);
            mesh.PointLights.Add(new PointLight(lamp, new Vector3(0.5f, 0.05f, 0.03f), 4));
        }
        if ((lever - eye).Length > drawDistance)
            return;
        var foot = lever - Double3.Up * 0.9;
        var standAt = Basis(toeTangent, foot, eye, 0);
        mesh.Instances.Add(new MeshInstance(Piece("switch-stand", () => StructureKit.SwitchStand(_look)), standAt));
        // The throw lever (GDD §17: thrown by hand at the points): down along the line for the main, thrown over the
        // other way for the branch, so the points read from the cab by the lever as well as by the lamp.
        float throwAngle = (diverging ? -1 : 1) * 0.85f * branch.Side;
        mesh.Instances.Add(new MeshInstance(Piece("switch-lever", () => StructureKit.SwitchLever(_look)),
            Matrix4x4.CreateRotationX(throwAngle) * Matrix4x4.CreateTranslation(0, 0.9f, 0) * standAt));
        // The lamp's glass: green set for the main line, red for the branch; flickering while the Switchman grips the
        // lever to throw it under the train (App. A.8, the derail's telegraph).
        var colour = (diverging ? Palette.SignalRed : Palette.SignalGreen) * flicker;
        var glass = (foot + Double3.Up * 1.75).RelativeTo(eye);
        mesh.Billboard(glass, 0.3f, 0, new Vector4(colour * 1.6f, 1), -1, FxBlend.Additive);
        mesh.Billboard(glass, 1.6f, 0, new Vector4(colour * 0.45f, 1), -1, FxBlend.Additive);
        mesh.PointLights.Add(new PointLight(glass, colour * 0.6f, 5));
    }

    /// <summary>Sleepers (where <paramref name="sleepers"/> says) and rails (where <paramref name="rails"/> says) along a line.</summary>
    void Rails(Kit k, RailLine line, Double3 eye, double from, double to, Func<double, bool> sleepers, Func<double, bool> rails)
    {
        const double pitch = 0.68, step = 5;
        k.Use("wood_sleeper", Palette.DeepBrown, 0.8f, 0, tile: 1.3f);
        for (double s = Math.Ceiling(from / pitch) * pitch; s < to; s += pitch)
        {
            if (!sleepers(s))
                continue;
            float j = Hash((float)(s * 1.37));
            k.Tint = Vector3.One * (0.75f + 0.35f * j);
            var t = line.Sample(s);
            k.With(Basis(t.Tangent, t.Position, eye, (j - 0.5f) * 0.05f), () => k.Box(new Vector3(-1.3f, -0.05f, -0.12f), new Vector3(1.3f, 0.07f, 0.12f), Kit.Faces.All & ~Kit.Faces.NegY));
        }
        k.Use("rail_steel", Palette.IronGrey, 0.4f, 0.6f, tile: 1);
        k.Tint = Vector3.One;
        for (double s = from; s < to; s += step)
        {
            if (!rails(s))
                continue;
            var a = line.Sample(s);
            var b = line.Sample(Math.Min(s + step, to));
            var dir = (b.Position - a.Position).Normalized;
            float half = (float)((b.Position - a.Position).Length / 2);
            k.With(Basis(dir, a.Position + (b.Position - a.Position) * 0.5, eye, 0), () =>
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * TrainKit.HalfGauge;
                    k.Box(new Vector3(x - 0.035f, 0.14f, -half), new Vector3(x + 0.035f, 0.19f, half), Kit.Faces.All & ~(Kit.Faces.PosZ | Kit.Faces.NegZ));
                    k.Box(new Vector3(x - 0.01f, 0.08f, -half), new Vector3(x + 0.01f, 0.14f, half), Kit.Faces.PosX | Kit.Faces.NegX);
                    k.Box(new Vector3(x - 0.07f, 0.07f, -half), new Vector3(x + 0.07f, 0.085f, half), Kit.Faces.PosY | Kit.Faces.PosX | Kit.Faces.NegX);
                }
            });
        }
    }
}
