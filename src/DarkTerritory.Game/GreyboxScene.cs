using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Builds greybox geometry for the line and the train around a camera. Positions are emitted
/// relative to the camera (floating origin), so a train 40 km down the line renders as precisely
/// as one at the yard.
/// </summary>
public sealed class GreyboxScene
{
    public float DrawDistance { get; init; } = 400;
    public int Seed { get; init; } = 7;
    /// <summary>How hot the firebox is, 0..1: the glow in the cab is how the Boiler reads the fire.</summary>
    public float FireGlow { get; set; } = 0.7f;
    /// <summary>Spec C.2 "lights drop to emergency only": the cars' lamps go to a dim red during a Vigil.</summary>
    public bool Emergency { get; set; }
    static readonly System.Numerics.Vector3 EmergencyRed = new(0.5f, 0.06f, 0.04f);
    /// <summary>Tunnels, bridges, facilities and hazards to draw along the line, when it's a generated route.</summary>
    public Route? Route { get; set; }
    /// <summary>Live enemies to draw. When set, the route's Sleepers come from here rather than its features.</summary>
    public IReadOnlyList<Enemy>? Enemies { get; set; }

    /// <summary>
    /// The firebox's light: the fire's orange, or with a Stoker in it (T53) "wrong-coloured firebox glow" (App. A.5), a sick
    /// green that burns brighter, not dimmer, as the pressure climbs.
    /// </summary>
    Vector3 FireColour(float scale) => Enemies?.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone) == true
        ? Palette.SignalGreen * (0.5f + 0.8f * scale) : Palette.FurnaceOrange * scale;
    /// <summary>Tonight's run, for facility machinery (the coaling chute pouring).</summary>
    public Sim.Run.Run? Run { get; set; }
    /// <summary>Seconds, for animating things that move on their own.</summary>
    public double Time { get; set; }
    /// <summary>Vehicle state for doors (open or shut). Without it every door is drawn shut.</summary>
    public IReadOnlyList<Vehicle>? Vehicles { get; set; }
    /// <summary>Loose bodies: crates, lamps, the dead.</summary>
    public IReadOnlyList<Sim.Physics.Body>? Bodies { get; set; }
    /// <summary>Other players, drawn as greybox figures.</summary>
    public IReadOnlyList<Crewmate>? Crew { get; set; }
    /// <summary>How each branch's switch is set (true: for the branch), for its stand's lamp. Unset, all read main.</summary>
    public Func<int, bool>? Diverging { get; set; }
    /// <summary>The switch stands, for where their levers are. Unset, they stand where the default tuning puts them.</summary>
    public SwitchStands? Stands { get; set; }
    /// <summary>The cab's controls, for where the levers' handles are.</summary>
    public TrainControls Controls { get; set; } = new() { Reverser = 1 };
    /// <summary>The sim's tick now, for effects timed from the sim (a gun's muzzle flash); unset, none are shown.</summary>
    public long Tick { get; set; } = -1;
    /// <summary>The boiler's pressure as a fraction of its maximum, for the cab's gauge (the sim's; unset, a working pressure).</summary>
    public float Pressure { get; set; } = 0.78f;
    /// <summary>The art pass's surfaces (T39, look.json). Unset, the greybox is flat colour.</summary>
    public Look? Look { get; set; }

    /// <summary>Depth of the valley under a bridge.</summary>
    const double ValleyDepth = 18;

    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye)
    {
        Vehicles ??= train.Vehicles;
        Build(mesh, train.Line, train.Frames, train.Dynamics.Distance, eye);
    }

    /// <param name="frames">Car frames to draw, e.g. interpolated between ticks.</param>
    /// <param name="hint">Any distance along the line near the eye, to start the nearest-point search.</param>
    public void Build(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, double hint, Double3 eye)
    {
        mesh.Clear();
        mesh.Style = Look?.Style;
        mesh.Seed = 0;
        // Bare triangles (the ground, the ballast, the trees) take world texels, wrapped every few km so they fit a
        // float; the pattern jumps at a wrap, rarely and far off in the fog.
        const double Wrap = 4096;
        static float W(double v) => (float)(v - Math.Floor(v / Wrap) * Wrap);
        mesh.SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z));
        double centre = NearestDistance(line, eye, hint);
        double from = Math.Max(0, centre - DrawDistance), to = Math.Min(line.Length, centre + DrawDistance);

        Track(mesh, line, eye, from, to, centre);
        foreach (var branch in line.Branches)
            if (branch.Toe < to && branch.End > from)
                Branch(mesh, line, branch, eye);
        Lineside(mesh, line, eye, from, to);
        if (Route is not null)
        {
            Features(mesh, line, eye, from, to);
            // A generated line's own land, boards, hazards, water and places (Art/PlanArt, linegen plan §12-13).
            if (Look is not null && Route.Plan is not null)
                Look.Art.World.Plan(mesh, line, Route, eye, centre, DrawDistance, Time);
            if (Run is not null)
                foreach (var site in Run.Sites)
                {
                    if (site is not null && (site.Capstan - eye).Length < DrawDistance)
                        Winch(mesh, site, eye);
                    if (site?.Crane is { } crane && (crane.HookAt - eye).Length < DrawDistance)
                        Crane(mesh, crane, frames, eye);
                }
            // GDD §9: the fortress yard behind the gates, and the terminus: "lights, then walls, then gun towers".
            double yard = Run?.YardLength ?? 600, terminus = Run?.Tuning.TerminusZone ?? 400;
            Fortress(mesh, line, eye, from, to, 0, yard, gateAt: yard);
            double home = Route.Plan?.Terminus.GateM ?? line.Length - terminus - 200;
            Fortress(mesh, line, eye, from, to, home, line.Length, gateAt: home, lit: Route.Plan?.Terminus.Silent != true);
        }
        // Practical lights first, so everything built after is lit by them: each car's lamps, the firebox,
        // and any hand lamp lying about or being carried.
        if (Bodies is not null)
            foreach (var b in Bodies.Where(b => b.Kind == Sim.Physics.BodyKind.Lamp))
                if (BodyWorld(b, frames, b.Centre) is { } at && (at - eye).Length < 60)
                    mesh.PointLights.Add(new PointLight(V(at, eye), Palette.LampAmber * 1.8f, 7f));
        foreach (var frame in frames)
        {
            if ((frame.Origin - eye).Length > 60)
                continue;
            // Its interior as an enclosed space: the night stays outside it (Room).
            if (Look is not null && frame.Shape.Interior is { } inside)
                mesh.Rooms.Add(new Room(V(frame.ToWorld(inside.Centre), eye), ToF(frame.Right), ToF(frame.Up), ToF(frame.Back), ToF(inside.HalfSize)));
            if (frame.Shape.Interior is { } room)
                foreach (double z in new[] { -room.HalfSize.Z * 0.5, room.HalfSize.Z * 0.5 })
                {
                    // With the art pass the lamps are flames, and flicker (Art.SceneArt.Flicker); the greybox's are steady.
                    float flicker = Look is null ? 1 : Art.SceneArt.Flicker(Time, frame.Index * 2 + (z < 0 ? 0 : 1));
                    mesh.PointLights.Add(Emergency
                        ? new PointLight(V(frame.ToWorld(new Double3(0, room.Max.Y - 0.2, room.Centre.Z + z)), eye), EmergencyRed, 4f)
                        : new PointLight(V(frame.ToWorld(new Double3(0, room.Max.Y - 0.2, room.Centre.Z + z)), eye), Palette.LampAmber * 1.6f * flicker, 7.5f));
                }
            foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox))
                mesh.PointLights.Add(new PointLight(V(frame.ToWorld(i.Position + new Double3(0, 0.7, 0.3)), eye), FireColour(0.6f + 1.6f * FireGlow), 5f));
        }
        foreach (var frame in frames)
            Car(mesh, frame, eye);
        if (Look is not null)
        {
            // The art pass's effects (Art/Effects): smoke, steam, sparks, the lamp's beam, and fog banks along the line.
            Look.Art.Effects.Train(mesh, frames, eye, Time, Controls, FireGlow, Emergency);
            var fog = Look.Apply(FrameLighting.Night).FogColor;
            Look.Art.Effects.Fog(mesh, line, eye, centre, Time, fog, (float)(Route?.Weather.FogDensity ?? 0.016));
            if (Route?.Weather is { Wet: true } weather)
                Look.Art.Effects.Rain(mesh, eye, Time, (float)weather.Wind, fog);
        }
        mesh.Seed = 0;
        if (Enemies is not null)
            foreach (var e in Enemies)
                if (!e.Gone)
                    DrawEnemy(mesh, line, frames, e, eye, from, to, Look?.Art.Creatures);
        if (Bodies is not null)
        {
            // Heavy crates only come from a facility's site, so its size is there (facilities.json "heavy").
            double heavyHalf = Run?.Sites.FirstOrDefault(x => x is not null)?.HeavyRadius ?? 0.5;
            foreach (var b in Bodies)
                if (Look?.Art.Body(mesh, frames, b, eye, heavyHalf, Time) != true)
                    DrawBody(mesh, frames, b, eye, heavyHalf);
        }
        if (Crew is not null)
            foreach (var c in Crew)
                if (c.Alive && Look?.Art.Crewmate(mesh, c, eye, Time) != true) // the dead are drawn as their bodies
                    DrawCrewmate(mesh, c, eye);
    }

    static Double3? BodyWorld(Sim.Physics.Body b, IReadOnlyList<CarFrame> frames, Double3 local) =>
        b.Parent == Sim.Player.PlayerState.World ? local : b.Parent < frames.Count ? frames[b.Parent].ToWorld(local) : null;

    /// <summary>Crates and lamps as boxes turned by their yaw; a body as bones between its joints.</summary>
    /// <param name="heavyHalf">A heavy crate's half-size, its body's radius (a client's stand-in bodies don't carry their radius).</param>
    static void DrawBody(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, double heavyHalf)
    {
        var up = b.Parent == Sim.Player.PlayerState.World || b.Parent >= frames.Count ? Double3.Up : frames[b.Parent].Up;
        double heading = b.Parent == Sim.Player.PlayerState.World || b.Parent >= frames.Count ? 0 : frames[b.Parent].Heading;
        var ps = b.Pbd.Particles;
        if (b.Kind != Sim.Physics.BodyKind.Ragdoll)
        {
            if (BodyWorld(b, frames, ps[0].Position) is not { } at)
                return;
            double yaw = b.Yaw + heading;
            var right = new Vector3((float)Math.Cos(yaw), 0, (float)-Math.Sin(yaw));
            var back = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
            if (b.Kind == Sim.Physics.BodyKind.Cargo)
            {
                // Freight: bigger than the train's own stores, stencilled, strapped.
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.44f, 0.44f, 0.44f), Palette.BlueGrey);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.45f, 0.05f, 0.45f), Palette.SootBlack);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.05f, 0.45f, 0.45f), Palette.SootBlack);
            }
            else if (b.Kind == Sim.Physics.BodyKind.Heavy)
            {
                // Two-man freight (T43): a long iron-banded case with a rope grip at each end, so it reads as one for two.
                float h = (float)heavyHalf;
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(h * 1.3f, h * 0.8f, h * 0.8f), Palette.RustRed * 0.8f);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(h * 1.31f, 0.05f, h * 0.81f), Palette.IronGrey);
                foreach (float end in new[] { -1f, 1f })
                {
                    mesh.Box(V(at, eye) + right * (end * h * 1.1f), right, ToF(up), back, new Vector3(0.05f, h * 0.81f, h * 0.81f), Palette.IronGrey);
                    mesh.Box(V(at, eye) + right * (end * (h * 1.3f + 0.06f)) + ToF(up) * (h * 0.3f), right, ToF(up), back, new Vector3(0.05f, 0.03f, 0.18f), Palette.DeepBrown);
                }
            }
            else if (b.Kind == Sim.Physics.BodyKind.Crate)
            {
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.34f, 0.34f, 0.34f), Palette.TarnishedBrass * 0.8f);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.35f, 0.06f, 0.35f), Palette.DeepBrown);
            }
            else if (b.Kind == Sim.Physics.BodyKind.Radio)
            {
                // A walkie-talkie (T41): an iron brick with its aerial up and a pinprick of a lamp, so a dropped one's found.
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.07f, 0.12f, 0.04f), Palette.IronGrey);
                mesh.Box(V(at, eye) + ToF(up) * 0.2f + right * 0.04f, right, ToF(up), back, new Vector3(0.008f, 0.1f, 0.008f), Palette.SootBlack);
                mesh.Emissive = 1;
                mesh.Box(V(at, eye) + ToF(up) * 0.09f - back * 0.042f, right, ToF(up), back, new Vector3(0.012f, 0.012f, 0.004f), Palette.SignalRed);
                mesh.Emissive = 0;
            }
            else
            {
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.1f, 0.14f, 0.1f), Palette.IronGrey);
                mesh.Emissive = 1;
                mesh.Box(V(at, eye) + ToF(up) * 0.02f, right, ToF(up), back, new Vector3(0.07f, 0.07f, 0.11f), Palette.LampAmber);
                mesh.Emissive = 0;
            }
            return;
        }
        // Bones: head-chest, chest-pelvis, arms, legs.
        (int, int, float)[] bones = [(0, 1, 0.12f), (1, 2, 0.17f), (1, 3, 0.07f), (3, 4, 0.06f), (1, 5, 0.07f), (5, 6, 0.06f), (2, 7, 0.09f), (7, 8, 0.08f), (2, 9, 0.09f), (9, 10, 0.08f)];
        foreach (var (a, c, r) in bones)
        {
            if (a >= ps.Length || c >= ps.Length || BodyWorld(b, frames, ps[a].Position) is not { } pa || BodyWorld(b, frames, ps[c].Position) is not { } pc)
                continue;
            var axis = pc - pa;
            double len = axis.Length;
            if (len < 1e-4)
                continue;
            var dir = axis * (1 / len);
            var side = Double3.Cross(dir, Math.Abs(dir.Y) > 0.9 ? new Double3(1, 0, 0) : Double3.Up).Normalized;
            var third = Double3.Cross(side, dir);
            var colour = a == 0 ? Palette.Corrupted : Palette.DeepBrown;
            mesh.Box(V((pa + pc) * 0.5, eye), ToF(side), ToF(dir), ToF(third), new Vector3(r, (float)len / 2 + r * 0.5f, r), colour);
        }
    }

    /// <summary>A crewmate: coat, head and a lamp at the chest so you can find each other in the dark. Dead ones lie down.</summary>
    static void DrawCrewmate(MeshBuilder mesh, Crewmate c, Double3 eye)
    {
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var o = V(c.Feet, eye);
        if (!c.Alive)
        {
            mesh.Box(o + Vector3.UnitY * 0.15f, right, Vector3.UnitY, back, new Vector3(0.25f, 0.15f, 0.9f), Palette.DeepBrown);
            return;
        }
        mesh.Box(o + Vector3.UnitY * 0.45f, right, Vector3.UnitY, back, new Vector3(0.16f, 0.45f, 0.12f), Palette.Charcoal);
        mesh.Box(o + Vector3.UnitY * 1.2f, right, Vector3.UnitY, back, new Vector3(0.24f, 0.33f, 0.15f), Palette.DeepBrown);
        mesh.Box(o + Vector3.UnitY * 1.68f, right, Vector3.UnitY, back, new Vector3(0.12f, 0.13f, 0.12f), Palette.Corrupted);
        // Arms (T47): to a headset player's hands where they are, hanging for everyone else.
        Vector3 At(Double3 local) => o + right * (float)local.X + Vector3.UnitY * (float)local.Y + back * (float)local.Z;
        void Segment(Double3 a, Double3 b, float r, Vector3 colour)
        {
            var axis = At(b) - At(a);
            float len = axis.Length();
            if (len < 1e-4f)
                return;
            var dir = axis / len;
            var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            mesh.Box((At(a) + At(b)) * 0.5f, side, dir, Vector3.Cross(side, dir), new Vector3(r, len * 0.5f + r * 0.5f, r), colour);
        }
        var (left, rightHand) = Arms.Hands(c.Hand, c.Other);
        foreach (var (armSide, target) in new[] { (-1, left), (1, rightHand) })
        {
            var shoulder = Arms.Shoulder(armSide);
            var (elbow, hand) = Arms.Solve(shoulder, target, Arms.Pole(armSide));
            Segment(shoulder, elbow, 0.06f, Palette.DeepBrown);
            Segment(elbow, hand, 0.05f, Palette.DeepBrown);
            // A glove, a shade lighter: the hand is what the others watch.
            mesh.Box(At(hand), right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.05f), Palette.Corrupted * 0.8f);
        }
        mesh.Emissive = 1;
        mesh.Box(o + Vector3.UnitY * 1.3f - back * 0.16f, right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.02f), Palette.LampAmber);
        mesh.Emissive = 0;
    }

    /// <summary>
    /// Greybox stand-ins, each readable by silhouette and by its telegraph (App. A.1: the tell must be
    /// perceivable). The real creatures come with the art pass; these exist to make pacing watchable.
    /// </summary>
    static void DrawEnemy(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Enemy e, Double3 eye, double from, double to, Art.CreatureArt? creatures = null)
    {
        // A basis for the enemy: its car's, or the line's at its distance.
        Double3 origin, right, up = Double3.Up, back;
        if (e.Attached >= 0)
        {
            if (e.Attached >= frames.Count)
                return;
            var f = frames[e.Attached];
            origin = f.ToWorld(e.Local);
            (right, up, back) = (f.Right, f.Up, f.Back);
        }
        else
        {
            if (e.LineDistance < from || e.LineDistance > to)
                return;
            var t = line.Sample(e.LineDistance);
            right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            back = t.Tangent * -1;
            origin = t.Position + right * e.Lateral + Double3.Up * e.Height;
        }
        var o = V(origin, eye);
        var (r, u, b) = (ToF(right), ToF(up), ToF(back));
        // The art pass's creature, where it has one (Art/CreatureArt): the same place, the thing itself.
        if (creatures is not null && creatures.Enemy(mesh, Art.CreatureArt.Basis(o, r, u, b), e))
            return;
        Vector3 L(double x, double y, double z) => o + r * (float)x + u * (float)y + b * (float)z;
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        float pulse = (float)(0.5 + 0.5 * Math.Sin(e.PhaseSeconds * 9));

        switch (e.Kind)
        {
            case EnemyKind.Sleepers:
                // Ties that aren't: across the rail, and braced (lifting, writhing) once they've telegraphed.
                bool braced = e.Phase >= SpinePhase.Telegraph;
                for (int i = 0; i < 6; i++)
                {
                    double lift = braced ? 0.12 + 0.08 * Math.Sin(e.PhaseSeconds * 6 + i) : 0;
                    Draw(0, lift, -i * 2.6, 1.5, 0.14, 0.45, Palette.Corrupted);
                }
                break;
            case EnemyKind.CinderHound:
                // Low, long, and lit from inside: the heat they hunt by is what you see of them at night.
                bool aboard = e.Phase == SpinePhase.Punish;
                double y = e.Attached >= 0 ? 0.35 : 0; // boarded: standing on the roof, not centred in it
                Draw(0, y, 0, 0.22, 0.28, 0.65, Palette.SootBlack);
                Draw(0, y + 0.2, -0.75, 0.16, 0.16, 0.25, Palette.SootBlack);
                mesh.Emissive = 1;
                // Cracks of ember along the flanks and back, proud of the body so they read from any side.
                Draw(0, y + 0.02, 0, 0.24, 0.1, 0.45, Palette.FurnaceOrange * (0.5f + 0.5f * (aboard ? pulse : 0.7f)));
                Draw(0, y + 0.29, 0.1, 0.08, 0.02, 0.4, Palette.FurnaceOrange * 0.8f);
                Draw(0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                Draw(-0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                mesh.Emissive = 0;
                break;
            case EnemyKind.Clinger:
                // A bulge on the hull; the drill point brightens as it works through (Extra = drill progress).
                double outward = Math.Sign(e.Local.X);
                Draw(outward * 0.2, 0, 0, 0.2, 0.55, 0.8, Palette.Corrupted);
                mesh.Emissive = 1;
                float drilled = (float)Math.Clamp(e.Extra, 0, 1);
                var bite = e.Phase == SpinePhase.Punish ? Palette.FurnaceOrange * (0.6f + 0.4f * pulse) : Palette.FurnaceOrange * (0.1f + 0.5f * drilled);
                // The seam where it's working through shows on its back, away from the hull.
                Draw(outward * 0.41, 0.1, 0, 0.02, 0.06, 0.5, bite);
                mesh.Emissive = 0;
                break;
            case EnemyKind.Dragger when e.Phase is SpinePhase.Telegraph or SpinePhase.Punish:
                {
                    // Out of sight under the edge until it reaches (App. A.4): then a long limb comes up just outside the eave
                    // and hooks in over the roof, rising through the telegraph's second; grabbing, two of them, further in.
                    // Corrupted flesh, pale: the one light-coloured thing at a car's dark edge, so the reach reads in time.
                    double inward = -Math.Sign(e.Local.X);
                    double rise = e.Phase == SpinePhase.Punish ? 1 : Math.Clamp(e.PhaseSeconds / 1.0, 0.2, 1);
                    int limbs = e.Phase == SpinePhase.Punish ? 2 : 1;
                    var flesh = Palette.Corrupted * 1.7f;
                    double outside = -inward * 0.14;
                    for (int i = 0; i < limbs; i++)
                    {
                        double z = (i - (limbs - 1) * 0.5) * 0.4;
                        double top = -0.1 + 0.8 * rise;
                        Draw(outside, (top - 0.4) * 0.5, z, 0.08, (top + 0.4) * 0.5, 0.08, flesh);
                        double reach = 0.3 + (e.Phase == SpinePhase.Punish ? 0.3 : 0.12) * rise;
                        Draw(outside + inward * reach * 0.5, top, z, reach * 0.5 + 0.04, 0.06, 0.07, flesh);
                        // Fingers splayed on the roof sheet.
                        Draw(outside + inward * (reach + 0.08), top - 0.04, z, 0.09, 0.03, 0.12, Palette.Corrupted * 1.3f);
                    }
                    break;
                }
            case EnemyKind.Switchman:
                {
                    // A railwayman, stooped wrong, standing at the switch with a lantern held out: the lantern's the only
                    // light on it, and it swings while it waits for the train.
                    Draw(0, 0.45, 0, 0.12, 0.45, 0.12, Palette.SootBlack);
                    Draw(0, 1.2, 0.05, 0.22, 0.35, 0.14, Palette.SootBlack);
                    Draw(0, 1.62, -0.12, 0.13, 0.13, 0.13, Palette.Corrupted);
                    double swing = e.Phase == SpinePhase.Telegraph ? 0.15 * Math.Sin(e.PhaseSeconds * 2.2) : 0;
                    Draw(0.32, 1.0, -0.1 + swing, 0.05, 0.28, 0.05, Palette.SootBlack);
                    mesh.Emissive = 1;
                    Draw(0.32, 0.7, -0.1 + swing, 0.08, 0.1, 0.08, Palette.LampAmber);
                    mesh.Emissive = 0;
                    break;
                }
            case EnemyKind.Lamplighter:
                {
                    // Tall, thin and stooped, soot-dark against the dark (App. A.6): nothing of it shows but the eyes, and
                    // those only when they've caught the lamp (the tell). Facing in at the track.
                    double inward = -Math.Sign(e.Lateral + 1e-9);
                    Draw(0, 0.6, 0, 0.1, 0.6, 0.1, Palette.SootBlack);
                    Draw(inward * 0.08, 1.55, 0, 0.14, 0.38, 0.12, Palette.SootBlack);
                    Draw(inward * 0.2, 2.02, 0, 0.1, 0.12, 0.1, Palette.SootBlack);
                    if (((Sim.Enemies.Lamplighter)e).Eyeshine)
                    {
                        mesh.Emissive = 1;
                        var shine = Palette.SignalGreen * 1.6f;
                        // Big enough to read at a low resolution across the dark: two points, set close.
                        Draw(inward * 0.31, 2.05, -0.06, 0.02, 0.035, 0.04, shine);
                        Draw(inward * 0.31, 2.05, 0.06, 0.02, 0.035, 0.04, shine);
                        mesh.Emissive = 0;
                    }
                    break;
                }
            case EnemyKind.SootChildren:
                {
                    // Small, crouched, huddled in the dark out from the car: three of them, soot on waxy skin, heads
                    // turned up at the doors. Only ever seen at the edge of the lamplight, and never moving while looked at.
                    for (int i = 0; i < 3; i++)
                    {
                        double x = (i - 1) * 0.45, z = (i % 2) * 0.35;
                        Draw(x, 0.28, z, 0.16, 0.28, 0.14, Palette.SootBlack);
                        Draw(x, 0.66, z - 0.05, 0.1, 0.1, 0.1, Palette.Corrupted * 0.7f);
                    }
                    break;
                }
            case EnemyKind.Hollow:
                if (e.Phase == SpinePhase.Telegraph)
                {
                    // Soot falling into the cab from the stack.
                    for (int i = 0; i < 4; i++)
                        Draw(0.3 * Math.Sin(i * 1.7), 1.0 - (e.PhaseSeconds * 2 + i * 0.4) % 1.6, 0.3 * Math.Cos(i * 2.3), 0.04, 0.2, 0.04, Palette.SootBlack);
                }
                else
                {
                    // Tall and thin, blacker than the cab.
                    Draw(0, -0.1, 0, 0.22, 0.95, 0.16, Palette.SootBlack);
                    mesh.Emissive = 1;
                    Draw(0.07, 0.7, -0.17, 0.025, 0.02, 0.01, Palette.Corrupted * 1.6f);
                    Draw(-0.07, 0.7, -0.17, 0.025, 0.02, 0.01, Palette.Corrupted * 1.6f);
                    mesh.Emissive = 0;
                }
                break;
        }
    }

    /// <summary>Finds the along-line distance nearest a point, starting from a guess.</summary>
    public static double NearestDistance(RailLine line, Double3 p, double guess)
    {
        double s = guess;
        for (int i = 0; i < 8; i++)
        {
            var sample = line.Sample(s);
            s = Math.Clamp(sample.Distance + Double3.Dot(p - sample.Position, sample.Tangent), 0, line.Length);
        }
        return s;
    }

    static Vector3 V(Double3 p, Double3 eye) => p.RelativeTo(eye);

    void Track(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double centre)
    {
        if (Look is not null)
        {
            // The art pass's line (WorldArt): the ground, the track and the lineside, cooked in cells.
            Look.Art.World.Cells(mesh, line, Route, eye, from, to, Seed, (float)ValleyDepth);
            return;
        }
        const double step = 5, gauge = 0.72, sleeperPitch = 0.75;
        const double groundHalfWidth = 90, bedHalfWidth = 1.8;
        for (double s = from; s < to; s += step)
        {
            var a = line.Sample(s);
            var b = line.Sample(Math.Min(s + step, to));
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            var down = new Double3(0, -0.02, 0);
            bool bridge = Route?.BridgeAt(s + step / 2) is not null;

            // Ground either side, then the raised ballast bed, then rails. Under a bridge the ground
            // drops into a valley and there's no ballast: the deck is drawn with the features.
            var valley = bridge ? new Double3(0, -ValleyDepth, 0) : default;
            double inner = bridge ? 0 : bedHalfWidth;
            mesh.Quad(V(a.Position - ra * groundHalfWidth + down * 10 + valley, eye), V(a.Position - ra * inner + down + valley, eye),
                      V(b.Position - rb * inner + down + valley, eye), V(b.Position - rb * groundHalfWidth + down * 10 + valley, eye), Palette.MuddyOlive);
            mesh.Quad(V(a.Position + ra * inner + down + valley, eye), V(a.Position + ra * groundHalfWidth + down * 10 + valley, eye),
                      V(b.Position + rb * groundHalfWidth + down * 10 + valley, eye), V(b.Position + rb * inner + down + valley, eye), Palette.MuddyOlive);
            if (!bridge)
                mesh.Quad(V(a.Position - ra * bedHalfWidth, eye), V(a.Position + ra * bedHalfWidth, eye),
                          V(b.Position + rb * bedHalfWidth, eye), V(b.Position - rb * bedHalfWidth, eye), Palette.Ballast);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var p0 = V(a.Position + ra * (side * gauge), eye);
                var p1 = V(b.Position + rb * (side * gauge), eye);
                var mid = (p0 + p1) / 2;
                var fwd = Vector3.Normalize(p1 - p0);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                mesh.Box(mid + new Vector3(0, 0.12f, 0), right, Vector3.UnitY, -fwd, new Vector3(0.04f, 0.07f, (p1 - p0).Length() / 2), Palette.IronGrey);
            }
        }
        // Sleepers only near the eye: past ~150 m the fog has eaten them anyway.
        double sleeperFrom = Math.Max(from, centre - 150), sleeperTo = Math.Min(to, centre + 150);
        for (double s = Math.Ceiling(sleeperFrom / sleeperPitch) * sleeperPitch; s < sleeperTo; s += sleeperPitch)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            mesh.Box(V(t.Position, eye) + new Vector3(0, 0.03f, 0), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(1.25f, 0.05f, 0.12f), Palette.DeepBrown);
        }
    }

    void Lineside(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        if (Look is not null)
        {
            return; // cooked with the track (WorldArt.Cells)
        }
        // Telegraph poles every 50 m and a scatter of pines: depth cues for fog and speed.
        for (double s = Math.Ceiling(from / 50) * 50; s < to; s += 50)
        {
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var foot = V(t.Position + right * 4.5, eye);
            mesh.AxisBox(foot + new Vector3(-0.12f, 0, -0.12f), foot + new Vector3(0.12f, 7.5f, 0.12f), Palette.DeepBrown);
            mesh.AxisBox(foot + new Vector3(-0.9f, 6.8f, -0.06f), foot + new Vector3(0.9f, 6.95f, 0.06f), Palette.DeepBrown);
        }
        for (double s = Math.Floor(from / 12) * 12; s < to; s += 12)
        {
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
            // A fixed hash, not HashCode.Combine: that one's seeded afresh every process, and the pines would move.
            var rng = new Random(unchecked(Seed * 73856093 ^ (int)(s / 12) * 19349663));
            mesh.Seed = (float)(s / 12 % 997);
            for (int k = 0; k < 3; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (9 + rng.NextDouble() * 70);
                double along = s + rng.NextDouble() * 12;
                // Not on a branch: clear of a dead line's track, and out of a facility's yard beside its spur.
                if (line.Branches.Any(b => along > b.Toe - 20 && along < b.End + 20 && Math.Sign(offset) == b.Side
                        && Math.Abs(offset) < (b.Kind == BranchKind.Spur ? 60 : 16)))
                    continue;
                var t = line.Sample(along);
                var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var foot = V(t.Position + right * offset + new Double3(0, -0.2, 0), eye);
                float h = 7 + (float)rng.NextDouble() * 9;
                mesh.AxisBox(foot + new Vector3(-0.2f, 0, -0.2f), foot + new Vector3(0.2f, h * 0.35f, 0.2f), Palette.DeepBrown);
                mesh.Pyramid(foot + new Vector3(0, h * 0.25f, 0), h * 0.22f, h * 0.8f, Palette.PineDark);
            }
        }
    }

    /// <summary>
    /// A branch off the main line (GDD §17, App. A.7): its own ballast, rails and sleepers from the points, the
    /// switch stand beside them with its target lamp (green set for the main line, red for the branch: from the cab,
    /// the lamp is how you read a switch before you're on it), and a buffer stop at the far end.
    /// </summary>
    void Branch(MeshBuilder mesh, RailLine main, Branch branch, Double3 eye)
    {
        if (Look is not null)
        {
            var at = (Stands ?? DefaultStands).LeverAt(main, branch.Index);
            Look.Art.World.Branch(mesh, branch, eye, DrawDistance, at, main.Sample(branch.Toe).Tangent, Diverging?.Invoke(branch.Index) ?? false);
            return;
        }
        const double step = 5, gauge = 0.72, sleeperPitch = 0.75, bedHalfWidth = 1.8, start = 4;
        var local = branch.Local;
        for (double s = start; s < local.Length; s += step)
        {
            var a = local.Sample(s);
            var b = local.Sample(Math.Min(s + step, local.Length));
            if ((a.Position - eye).Length > DrawDistance)
                continue;
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            var up = new Double3(0, 0.01, 0); // over the main line's bed where the two still overlap
            mesh.Quad(V(a.Position - ra * bedHalfWidth + up, eye), V(a.Position + ra * bedHalfWidth + up, eye),
                      V(b.Position + rb * bedHalfWidth + up, eye), V(b.Position - rb * bedHalfWidth + up, eye), Palette.Ballast);
            // Its shoulders run down into the ground: the main line's ground falls away from it, so out here the
            // branch sits on a low embankment of its own.
            var shoulder = new Double3(0, -0.35, 0);
            mesh.Quad(V(a.Position + ra * bedHalfWidth + up, eye), V(a.Position + ra * (bedHalfWidth + 0.8) + shoulder, eye),
                      V(b.Position + rb * (bedHalfWidth + 0.8) + shoulder, eye), V(b.Position + rb * bedHalfWidth + up, eye), Palette.Ballast);
            mesh.Quad(V(a.Position - ra * (bedHalfWidth + 0.8) + shoulder, eye), V(a.Position - ra * bedHalfWidth + up, eye),
                      V(b.Position - rb * bedHalfWidth + up, eye), V(b.Position - rb * (bedHalfWidth + 0.8) + shoulder, eye), Palette.Ballast);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var p0 = V(a.Position + ra * (side * gauge), eye);
                var p1 = V(b.Position + rb * (side * gauge), eye);
                var fwd = Vector3.Normalize(p1 - p0);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                mesh.Box((p0 + p1) / 2 + new Vector3(0, 0.12f, 0), right, Vector3.UnitY, -fwd, new Vector3(0.04f, 0.07f, (p1 - p0).Length() / 2), Palette.IronGrey);
            }
        }
        for (double s = start; s < local.Length; s += sleeperPitch)
        {
            var t = local.Sample(s);
            if ((t.Position - eye).Length > 150)
                continue;
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            mesh.Box(V(t.Position, eye) + new Vector3(0, 0.035f, 0), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(1.25f, 0.05f, 0.12f), Palette.DeepBrown);
        }

        // The buffer stop: a timber-and-iron block across the rails, with a red lamp on it.
        var end = local.Sample(local.Length);
        // An alternate has no end of its own: it runs back into the main line (linegen plan §6.2).
        if (!branch.Rejoins && (end.Position - eye).Length < DrawDistance)
        {
            var right = Double3.Cross(end.Tangent, Double3.Up).Normalized;
            mesh.Box(V(end.Position + Double3.Up * 0.6, eye), ToF(right), Vector3.UnitY, ToF(end.Tangent * -1), new Vector3(1.3f, 0.6f, 0.4f), Palette.RustRed);
            mesh.Emissive = 1;
            mesh.Box(V(end.Position + Double3.Up * 1.35, eye), ToF(right), Vector3.UnitY, ToF(end.Tangent * -1), new Vector3(0.12f, 0.12f, 0.12f), Palette.SignalRed);
            mesh.Emissive = 0;
        }

        // The stand at the points: a post, the throw lever at hand height, and the target lamp on top.
        var lever = (Stands ?? DefaultStands).LeverAt(main, branch.Index);
        if ((lever - eye).Length > DrawDistance)
            return;
        var toe = main.Sample(branch.Toe);
        var across = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
        var foot = lever - Double3.Up * 0.9;
        mesh.Box(V(foot + Double3.Up * 0.8, eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.08f, 0.8f, 0.08f), Palette.IronGrey);
        mesh.Box(V(lever - across * (branch.Side * 0.35), eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.35f, 0.035f, 0.035f), Palette.TarnishedBrass);
        bool diverging = Diverging?.Invoke(branch.Index) ?? false;
        var lamp = diverging ? Palette.SignalRed : Palette.SignalGreen;
        var lampAt = foot + Double3.Up * 1.75;
        mesh.PointLights.Add(new PointLight(V(lampAt, eye), lamp * 0.6f, 5));
        mesh.Emissive = 1;
        mesh.Box(V(lampAt, eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.16f, 0.16f, 0.16f), lamp);
        mesh.Emissive = 0;
    }

    static readonly SwitchStands DefaultStands = new(new JunctionTuning());

    /// <summary>A box following the line: <paramref name="lateral"/> metres right of centre, base at <paramref name="y"/> above rail.</summary>
    static void Along(MeshBuilder mesh, RailLine line, Double3 eye, double s, double length, double lateral, double y, double halfWidth, double height, Vector3 color)
    {
        var t = line.Sample(s + length / 2);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var centre = t.Position + right * lateral + Double3.Up * (y + height / 2);
        mesh.Box(V(centre, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3((float)halfWidth, (float)height / 2, (float)length / 2), color);
    }

    void Features(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        foreach (var f in Route!.Features)
        {
            if (f.End < from || f.Start > to)
                continue;
            double a = Math.Max(f.Start, from), b = Math.Min(f.End, to);
            // The art pass's structures (Art/StructureKit): viaducts and trestles, portals and bores.
            if (Look is not null && f.Kind == FeatureKind.Bridge)
            {
                Look.Art.World.Bridge(mesh, line, f, eye, from, to, Art.WorldArt.SpanDepth(Route, f) ?? (float)ValleyDepth);
                continue;
            }
            if (Look is not null && f.Kind == FeatureKind.Tunnel)
            {
                Look.Art.World.Tunnel(mesh, line, f, eye, from, to);
                continue;
            }
            switch (f.Kind)
            {
                case FeatureKind.Tunnel:
                    // Walls and roof close round the track, a hill sits on top, and the portals are faced in stone.
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, -3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 6.6, 3.8, 1.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 7.8, 40, 14, Palette.MuddyOlive);
                    }
                    foreach (double portal in new[] { f.Start, f.End - 1.5 })
                        if (portal >= from && portal <= to)
                        {
                            Along(mesh, line, eye, portal, 1.5, -5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 0, 7.4, 7.7, 1.6, Palette.IronGrey);
                        }
                    break;
                case FeatureKind.Bridge:
                    // Weak bridges are timber trestles; sound ones are iron girders on stone piers.
                    var deck = f.MaxCars > 0 ? Palette.DeepBrown : Palette.IronGrey;
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, 0, -0.6, 2.2, 0.6, deck);
                        Along(mesh, line, eye, s, len, -2.3, -0.6, 0.12, 1.6, deck);
                        Along(mesh, line, eye, s, len, 2.3, -0.6, 0.12, 1.6, deck);
                    }
                    for (double s = Math.Ceiling(a / 25) * 25; s < b; s += 25)
                        Along(mesh, line, eye, s, 3, 0, -ValleyDepth, f.MaxCars > 0 ? 1.6 : 2.0, ValleyDepth - 0.6, f.MaxCars > 0 ? Palette.DeepBrown : Palette.Charcoal);
                    break;
                case FeatureKind.Sleepers when Enemies is null:
                    // Shaped like ties, lying across the rail: invisible until the lamp finds them (App. A.2).
                    for (double s = a; s < b; s += 2.6)
                        Along(mesh, line, eye, s, 0.9, 0, 0.12, 1.5, 0.28, Palette.Corrupted);
                    break;
                case FeatureKind.Grease:
                    mesh.Emissive = 0.15f;
                    foreach (double side in new[] { -0.72, 0.72 })
                        Along(mesh, line, eye, a, b - a, side, 0.19, 0.05, 0.02, Palette.GreaseSheen);
                    mesh.Emissive = 0;
                    break;
                case FeatureKind.Junction:
                    // The branch and its switch stand are drawn with the track.
                    break;
                case FeatureKind.Facility when line.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)) is { } spur:
                    {
                        // Down its spur (GDD §17): the buildings stand back beyond the machinery, behind the crate stack
                        // and short of the winch's haul, so the modules stay in the open where you work them.
                        var site = Run?.Sites.FirstOrDefault(s => s?.Spur == spur.Index);
                        double layout = site?.Mid ?? spur.Local.Length - 45;
                        FacilityBuildings(mesh, spur.Local, eye, f.Facility, layout - 25, spur.Side, push: 4);
                        break;
                    }
                case FeatureKind.Facility:
                    Facility(mesh, line, eye, f);
                    if (f.Facility == FacilityKind.CoalingTower && Run is { } run)
                        Chute(mesh, line, eye, f, run);
                    break;
            }
        }
    }

    /// <summary>Walls both sides, gun towers with lamps, and a gatehouse over the line.</summary>
    void Fortress(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double start, double end, double gateAt, bool lit = true)
    {
        if (Look is not null)
        {
            Look.Art.World.Fortress(mesh, line, eye, from, to, start, end, gateAt, platform: start == 0, lit);
            return;
        }
        double a = Math.Max(start, from), b = Math.Min(end, to);
        if (a >= b)
            return;
        for (double s = Math.Floor(a / 10) * 10; s < b; s += 10)
            foreach (int side in new[] { -1, 1 })
                Along(mesh, line, eye, s, 10, side * 14, 0, 0.8, 7, Palette.IronGrey);
        for (double s = Math.Ceiling(a / 120) * 120; s < b; s += 120)
            foreach (int side in new[] { -1, 1 })
            {
                Along(mesh, line, eye, s, 4, side * 14, 0, 2.2, 11, Palette.Charcoal);
                mesh.Emissive = 1;
                Along(mesh, line, eye, s + 1.5, 1, side * 11.6, 9.5, 0.2, 0.4, Palette.LampAmber);
                mesh.Emissive = 0;
            }
        if (gateAt >= from && gateAt <= to)
        {
            foreach (int side in new[] { -1, 1 })
                Along(mesh, line, eye, gateAt - 3, 6, side * 5, 0, 1.5, 10, Palette.Charcoal);
            Along(mesh, line, eye, gateAt - 3, 6, 0, 8.5, 6.5, 1.5, Palette.Charcoal);
            mesh.Emissive = 1;
            Along(mesh, line, eye, gateAt, 0.4, 0, 8.2, 0.5, 0.3, Palette.LampAmber);
            mesh.Emissive = 0;
        }
    }

    /// <summary>The coaling lever on the ground, and coal falling from the spout while the chute is open.</summary>
    void Chute(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f, Sim.Run.Run run)
    {
        var (spout, lever) = run.ChuteAt(f, line);
        var t = line.Sample(spout);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        mesh.Box(V(lever - Double3.Up * 0.45, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.06f, 0.45f, 0.06f), Palette.IronGrey);
        bool open = run.ChuteOpen && run.FacilityFeature == f;
        // The handle: down when pouring, up when shut.
        mesh.Box(V(lever + Double3.Up * (open ? -0.1 : 0.25) + right * (f.Side * 0.15), eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.2f, 0.04f, 0.04f), Palette.TarnishedBrass);
        if (!open)
            return;
        // A curtain of coal from the spout onto whatever's under it, and dust lit by the tower's lamp.
        var top = t.Position + right * (f.Side * 0.4) + Double3.Up * 8.8;
        for (int i = 0; i < 24; i++)
        {
            double fall = (Time * 7 + i * 0.37) % 5.4;
            var p = top - Double3.Up * fall + right * (0.3 * Math.Sin(i * 2.1)) + t.Tangent * (0.35 * Math.Cos(i * 1.3));
            mesh.Box(V(p, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.22f, 0.26f, 0.22f), i % 3 == 0 ? Palette.IronGrey : Palette.Charcoal);
        }
    }

    /// <summary>
    /// A capstan winch (spec D.2): the drum by the track with its two handles, the rope out across the ground, and the
    /// sled of freight on it, as far along as the crew have hauled it.
    /// </summary>
    static void Winch(MeshBuilder mesh, Sim.Run.Site site, Double3 eye)
    {
        if (!site.Has(Sim.Run.ModuleKind.Winch))
            return;
        // The drum on its trestle, lying along the track between the cranks, turned by the crank's angle (T43).
        var axis = ToF(site.Axis);
        var outward = ToF(site.Outward);
        var hub = site.Capstan + Double3.Up * 0.9;
        float half = (float)((site.Handles[1] - site.Handles[0]).Length * 0.5) - 0.15f;
        foreach (float end in new[] { -1f, 1f })
            mesh.Box(V(site.Capstan + site.Axis * (end * (half - 0.05f)) + Double3.Up * 0.45, eye), axis, Vector3.UnitY, outward, new Vector3(0.05f, 0.45f, 0.35f), Palette.DeepBrown);
        var turn = new Vector3(0, (float)Math.Sin(site.Crank), 0) + outward * (float)Math.Cos(site.Crank);
        var turnUp = Vector3.Cross(axis, turn);
        mesh.Box(V(hub, eye), axis, turn, turnUp, new Vector3(half - 0.1f, 0.22f, 0.22f), Palette.IronGrey);
        mesh.Box(V(hub, eye), axis, turn, turnUp, new Vector3(half - 0.3f, 0.235f, 0.06f), Palette.DeepBrown);
        for (int i = 0; i < site.Handles.Length; i++)
        {
            // Each crank: an arm from the hub out to its grip, and the grip sticking out from the drum's end.
            var h = site.Handles[i];
            var grip = site.Grip(i);
            var arm = grip - h;
            var dir = ToF(arm.Normalized);
            var side = Vector3.Cross(axis, dir);
            // Brass, worn bright by hands, so the crank reads in the dark as the thing to take hold of.
            mesh.Box(V(h + arm * 0.5, eye), dir, side, axis, new Vector3((float)arm.Length * 0.5f + 0.04f, 0.035f, 0.025f), Palette.TarnishedBrass);
            float outOf = i == 0 ? -1 : 1;
            mesh.Box(V(grip + site.Axis * (outOf * 0.12), eye), axis, dir, side, new Vector3(0.12f, 0.035f, 0.035f), Palette.TarnishedBrass * 1.3f);
        }
        var drum = site.Capstan;
        var sled = site.Sled;
        var rope = sled - drum;
        // The rope comes off the top of the drum and down to the sled.
        var from = hub + Double3.Up * 0.22;
        var line = sled + Double3.Up * 0.3 - from;
        if (line.Length > 0.5)
        {
            var dir = line.Normalized;
            var ropeSide = Double3.Cross(dir, Double3.Up).Normalized;
            mesh.Box(V(from + line * 0.5, eye), ToF(dir), ToF(Double3.Cross(ropeSide, dir)), ToF(ropeSide), new Vector3((float)line.Length * 0.5f, 0.02f, 0.02f), Palette.DeepBrown);
        }
        if (site.SledsLeft > 0)
        {
            var across = ToF(rope.Length > 0.1 ? rope.Normalized : Double3.Cross(Double3.Up, new Double3(1, 0, 0)));
            mesh.Box(V(sled + Double3.Up * 0.15, eye), across, Vector3.UnitY, Vector3.Cross(across, Vector3.UnitY), new Vector3(1.2f, 0.15f, 0.8f), Palette.RustRed);
            mesh.Box(V(sled + Double3.Up * 0.75, eye), across, Vector3.UnitY, Vector3.Cross(across, Vector3.UnitY), new Vector3(0.9f, 0.45f, 0.6f), Palette.BlueGrey);
        }
    }

    /// <summary>
    /// A gantry crane (spec D.2, T48): four legs astride the track, the rails along the top, the bridge across them where it
    /// is, the trolley on it, the hook on its cable, and the castings: stacked, on the hook, or lashed on a car's roof. The
    /// cab's up at the near end, a box on the leg with its window looking along the gantry.
    /// </summary>
    static void Crane(MeshBuilder mesh, Sim.Run.Crane crane, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        void Beam(Double3 a, Double3 b, float r, Vector3 colour)
        {
            var axis = b - a;
            double len = axis.Length;
            if (len < 1e-4)
                return;
            var dir = axis * (1 / len);
            var side = Double3.Cross(dir, Math.Abs(dir.Y) > 0.9 ? new Double3(1, 0, 0) : Double3.Up).Normalized;
            mesh.Box(V((a + b) * 0.5, eye), ToF(side), ToF(dir), ToF(Double3.Cross(side, dir)), new Vector3(r, (float)len * 0.5f + r, r), colour);
        }
        double top = crane.Top;
        for (int end = 0; end < 2; end++)
            for (int side = 0; side < 2; side++)
            {
                var foot = crane.Corner(end, side);
                Beam(foot, foot + Double3.Up * top, 0.18f, Palette.IronGrey);
            }
        // The rails along the top, one over each row of legs.
        for (int side = 0; side < 2; side++)
            Beam(crane.Corner(0, side) + Double3.Up * top, crane.Corner(1, side) + Double3.Up * top, 0.15f, Palette.RustRed);
        // The bridge where it is, and the trolley on it.
        Beam(crane.BridgeEnd(0), crane.BridgeEnd(1), 0.22f, Palette.RustRed * 1.1f);
        var hook = crane.HookAt;
        var trolley = hook with { Y = crane.BridgeEnd(0).Y };
        Beam(trolley - Double3.Up * 0.3, trolley + Double3.Up * 0.3, 0.35f, Palette.IronGrey);
        Beam(trolley, hook, 0.02f, Palette.SootBlack);
        Beam(hook, hook - Double3.Up * 0.3, 0.07f, Palette.TarnishedBrass);
        // The cab up at the near end, and its control stand at the foot of the leg.
        var cab = crane.Cab;
        mesh.Box(V(cab, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.9f, 0.8f, 0.9f), Palette.DeepBrown);
        mesh.Emissive = 1;
        mesh.Box(V(cab + Double3.Up * 0.3, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.92f, 0.12f, 0.6f), Palette.LampAmber * 0.5f);
        mesh.Emissive = 0;
        mesh.Box(V(crane.Controls + Double3.Up * 0.55, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.25f, 0.55f, 0.2f), Palette.IronGrey);
        var size = crane.Tuning.CastingSize;
        foreach (var c in crane.Castings)
            if (crane.Where(c, frames) is { } at)
            {
                var up = c.State == Sim.Run.CastingState.Loaded && c.Car >= 0 && c.Car < frames.Count ? frames[c.Car].Up : Double3.Up;
                var centre = at + up * (size[1] * 0.5);
                // A foundry casting: a dark iron block with a lifting eye on top, rust on its edges.
                mesh.Box(V(centre, eye), Vector3.UnitX, ToF(up), Vector3.UnitZ, new Vector3((float)size[0] * 0.5f, (float)size[1] * 0.5f, (float)size[2] * 0.5f), Palette.SootBlack * 1.4f);
                mesh.Box(V(centre + up * (size[1] * 0.5 + 0.1), eye), Vector3.UnitX, ToF(up), Vector3.UnitZ, new Vector3(0.08f, 0.1f, 0.03f), Palette.RustRed);
            }
    }

    /// <summary>Placeholder silhouettes until facility modules exist: oversized, dark, one working lamp (GDD §30).</summary>
    void Facility(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f) =>
        FacilityBuildings(mesh, line, eye, f.Facility, (f.Start + f.End) / 2, f.Side, push: 0);

    /// <summary>A facility's buildings beside a track (the main line, or its spur), centred along it at <paramref name="mid"/>.</summary>
    void FacilityBuildings(MeshBuilder mesh, RailLine line, Double3 eye, FacilityKind? kind, double mid, double side, double push)
    {
        if (Look is not null)
        {
            Look.Art.World.Facility(mesh, line, eye, kind, mid, side, push);
            return;
        }
        side = side == 0 ? 1 : side;
        double Out(double lateral) => side * (lateral + push);
        switch (kind)
        {
            case FacilityKind.CoalingTower:
                Along(mesh, line, eye, mid - 6, 12, Out(7), 0, 5, 22, Palette.Charcoal);
                Along(mesh, line, eye, mid - 2, 4, Out(2.6), 9, 1.6, 1.2, Palette.IronGrey);
                break;
            case FacilityKind.GrainElevator:
                for (int i = 0; i < 3; i++)
                    Along(mesh, line, eye, mid - 20 + i * 12, 10, Out(12), 0, 5, 26, Palette.BlueGrey);
                break;
            case FacilityKind.Foundry:
                Along(mesh, line, eye, mid - 40, 80, Out(22), 0, 14, 16, Palette.RustRed);
                Along(mesh, line, eye, mid + 10, 6, Out(26), 16, 2, 18, Palette.SootBlack);
                break;
            default:
                Along(mesh, line, eye, mid - 30, 60, Out(20), 0, 12, 10, Palette.DeepBrown);
                break;
        }
        mesh.Emissive = 1;
        Along(mesh, line, eye, mid, 0.4, Out(4), 5, 0.2, 0.3, Palette.LampAmber);
        mesh.Emissive = 0;
    }

    void Car(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;
        void Draw(Box box, Vector3 color) => mesh.Box(L(box.Centre.X, box.Centre.Y, box.Centre.Z), right, up, back, ToF(box.HalfSize), color);

        var shape = frame.Shape;
        bool engine = frame.Index == 0;
        // Each car wears its own way (the grime pattern is in the car's own frame, so it rides with it).
        mesh.Seed = frame.Index + 1;
        var vehicle = Vehicles is { } vs && frame.Index < vs.Count ? vs[frame.Index] : null;
        // The art pass's kit (TrainKit): the body, doors and gun as cooked pieces; what's left here is what glows and moves.
        if (Look is not null && Look.Art.Car(mesh, frame, eye, vehicle, Emergency, Tick))
        {
            CarWorkings(mesh, frame, eye, Draw);
            if (engine)
            {
                // The dials: pressure from the boiler, heat from the fire, the water glass (no water model yet: steady),
                // and speed against the line's 80 km/h top.
                float speed = (float)frame.Velocity.Length / 22.2f;
                Look.Art.Gauges(mesh, frame, eye, [Pressure, FireGlow, 0.72f, speed]);
            }
            return;
        }
        // What you see is what you collide with: every solid is drawn, coloured by what it is. Long interior
        // surfaces go down in 2 m slices so the per-vertex lamp light has vertices to land on.
        foreach (var solid in shape.Solids)
        {
            var colour = PartColour(solid.Part, frame.Index);
            var b = solid.Box;
            double length = b.Max.Z - b.Min.Z;
            if (shape.Interior is null || length <= 2.5 || solid.Part is not (PartKind.Wall or PartKind.Chassis or PartKind.Body or PartKind.Cargo))
            {
                Draw(b, colour);
                continue;
            }
            int slices = (int)Math.Ceiling(length / 2);
            for (int k = 0; k < slices; k++)
            {
                double z0 = b.Min.Z + length * k / slices, z1 = b.Min.Z + length * (k + 1) / slices;
                Draw(new Box(b.Min with { Z = z0 }, b.Max with { Z = z1 }), colour);
            }
        }

        double half = shape.HalfLength;
        if (engine)
        {
            // The headlamp: dark with no power in a Vigil.
            mesh.Emissive = 1;
            Draw(Box.FromCentre(new Double3(0, 2.8, -half - 0.05), new Double3(0.35, 0.35, 0.1)), Emergency ? Palette.LampAmber * 0.08f : Palette.LampAmber);
            mesh.Emissive = 0;
        }
        CarWorkings(mesh, frame, eye, Draw);
        if (!engine)
        {
            // Roof walkway plank down the safe centreline.
            Draw(new Box(new Double3(-0.35, shape.RoofHeight, -half + 0.2), new Double3(0.35, shape.RoofHeight + 0.04, half - 0.2)), Palette.TarnishedBrass);
        }
        // Doors: shut in the doorway, or slid aside when open: an end door along the end wall inside, a side door back
        // along the outside of the car (a boxcar's sliding door).
        foreach (var door in shape.DoorList)
        {
            bool open = vehicle?.DoorOpen(door.Index) ?? false;
            var box = door.Box;
            bool side = box.Max.Z - box.Min.Z > box.Max.X - box.Min.X;
            if (open)
            {
                var move = side
                    ? new Double3(box.Min.X < 0 ? -0.12 : 0.12, 0, box.Max.Z - box.Min.Z)
                    : new Double3(box.Max.X - box.Min.X, 0, box.Min.Z < 0 ? 0.12 : -0.12);
                box = new Box(box.Min + move, box.Max + move);
            }
            Draw(box, Palette.DeepBrown);
            var handle = side
                ? new Double3(box.Min.X < 0 ? box.Min.X - 0.03 : box.Max.X + 0.03, box.Min.Y + 1.0, open ? box.Min.Z + 0.1 : box.Max.Z - 0.12)
                : new Double3(open ? box.Min.X + 0.1 : box.Max.X - 0.12, box.Min.Y + 1.0, box.Centre.Z);
            Draw(Box.FromCentre(handle, side ? new Double3(0.08, 0.04, 0.04) : new Double3(0.04, 0.04, 0.08)), Palette.TarnishedBrass);
        }
        if (shape.Gun is { } gun)
        {
            // Barrel along the gun's facing: its arc is readable from its silhouette (GDD §26).
            Draw(Box.FromCentre(gun.Position + gun.Facing * 0.9, new Double3(0.08, 0.08, 0.9)), Palette.SootBlack);
            Draw(Box.FromCentre(gun.Position, new Double3(0.3, 0.25, 0.35)), Palette.IronGrey);
        }
        foreach (var ladder in shape.Ladders)
        {
            // Rails run up the face the ladder is fixed to: thin across it, a hand-width wide along it.
            bool side = Math.Abs(ladder.Inward.X) > 0;
            var h = new Double3(side ? 0.05 : 0.25, ladder.Top / 2, side ? 0.25 : 0.05);
            Draw(Box.FromCentre(ladder.Foot + new Double3(0, ladder.Top / 2, 0), h), Palette.IronGrey);
        }
        // Wheel sets under both ends.
        foreach (double z in new[] { -half * 0.6, half * 0.6 })
            Draw(Box.FromCentre(new Double3(0, 0.45, z), new Double3(shape.HalfWidth * 0.8, 0.4, 1.2)), Palette.SootBlack);
    }

    /// <summary>
    /// What glows and moves on a car, with or without the kit: the firebox's glow, the vent valve, the driver's levers
    /// where the controls have them, and the lamp in every car.
    /// </summary>
    void CarWorkings(MeshBuilder mesh, CarFrame frame, Double3 eye, Action<Box, Vector3> draw)
    {
        var shape = frame.Shape;
        if (frame.Index == 0)
        {
            mesh.Emissive = 1;
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox))
                draw(Box.FromCentre(i.Position + new Double3(0, 0.7, -0.17), new Double3(0.3, 0.2, 0.02)), FireColour(0.15f + 0.85f * FireGlow));
            mesh.Emissive = 0;
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Vent))
                draw(Box.FromCentre(i.Position + new Double3(0, 1.1, 0), new Double3(0.12, 0.12, 0.04)), Palette.TarnishedBrass);
            // The driver's levers, their handles where the controls have them (T29): a headset player takes hold of
            // these. The regulator comes back as it opens, the brake handle as it goes on, the reverser forward for ahead.
            if (shape.Levers is { } levers)
            {
                void Lever(Double3 handle, double rod)
                {
                    draw(Box.FromCentre(handle - new Double3(0, rod / 2, 0), new Double3(0.02, rod / 2, 0.02)), Palette.IronGrey);
                    draw(Box.FromCentre(handle, new Double3(0.07, 0.03, 0.03)), Palette.TarnishedBrass);
                }
                Lever(levers.RegulatorAt(Controls.Throttle), 0.3);
                Lever(levers.BrakeAt(Controls.Brake), 0.2);
                Lever(levers.ReverserAt(Controls.Reverser), 0.9);
            }
        }
        if (shape.Interior is not null && Look is not null)
        {
            // Lanterns hanging where the car's lights are (the art pass's).
            Look.Art.CarLamps(mesh, frame, eye, Emergency);
        }
        else if (shape.Interior is { } room)
        {
            // A lamp in every car: the warm interior against the hostile exterior (GDD §26).
            mesh.Emissive = 1;
            draw(Box.FromCentre(new Double3(0, room.Max.Y - 0.08, 0), new Double3(0.12, 0.06, 0.12)), Emergency ? EmergencyRed : Palette.LampAmber);
            mesh.Emissive = 0;
        }
    }

    static Vector3 PartColour(PartKind part, int car) => part switch
    {
        PartKind.Body => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Chassis => Palette.Charcoal,
        PartKind.Boiler or PartKind.Stack => Palette.SootBlack,
        PartKind.CabWall or PartKind.CabRoof or PartKind.Coupler => Palette.IronGrey,
        PartKind.Tender => Palette.Charcoal,
        PartKind.Wall => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Cargo => Palette.MuddyOlive,
        PartKind.Locker or PartKind.Steps => Palette.IronGrey,
        _ => Palette.IronGrey,
    };

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
}
