using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The handcar as it's drawn (GDD §24.1; ARCHITECTURE §8 note 729): a plank deck on four small wheels, a post in the middle with
/// the see-saw handle on it, swinging as the sim's handle swings, and a lantern on the front end. Hung on the guard van's back
/// it's the same trolley stood on its end, its lantern dark, hung by two chains from the van's roof end. Modelled (note 816,
/// tools/models/recipes/handcar.py: its body, a wheel turned by the distance it's run, the handle, a length of chain); boxes
/// where the art isn't loaded.
/// </summary>
public sealed partial class GreyboxScene
{
    /// <summary>
    /// The handcar's vehicle id, its handle's swing, whether it's on the rails, and a lift under way (how far, and the frame
    /// it's being lifted to): note 729, set from the train each frame.
    /// </summary>
    public (int Id, double Phase, bool OnRails, double Lift, CarFrame? To)? HandcarNow { get; set; }

    /// <summary>How far the handcar's run on the rails (m, its rake's distance along the line): its wheels turned by it (note 816).</summary>
    public double HandcarRolled { get; set; }

    /// <summary>Where the hung handcar's chains are hooked on the van (world: its roof end, left and right), or null with it off them.</summary>
    public (Double3 Left, Double3 Right)? HandcarHooks { get; set; }

    /// <summary>
    /// The handcar part-way through a lift (note 729): from where it is toward where it's going, up over the van's platform
    /// rail in an arc, eased in and out. Presentation only: the sim moves it at the lift's end.
    /// </summary>
    public static CarFrame Lifted(CarFrame from, CarFrame to, double lift)
    {
        double k = Math.Clamp(lift, 0, 1);
        k = k * k * (3 - 2 * k);
        var right = Double3.Lerp(from.Right, to.Right, k).Normalized;
        var up = Double3.Lerp(from.Up, to.Up, k);
        up = (up - right * Double3.Dot(up, right)).Normalized;
        var origin = Double3.Lerp(from.Origin, to.Origin, k) + Double3.Up * (0.6 * Math.Sin(Math.PI * k));
        return new CarFrame(from.Index, origin, right, up, Double3.Cross(right, up), from.Velocity, from.Shape);
    }

    // The look's material families pick textures by colour (Look.Material): its wood (the sleepers') and its iron.
    static readonly Vector3 HandcarWood = Palette.DeepBrown * 1.15f;
    static readonly Vector3 HandcarIron = Palette.IronGrey * 0.8f;

    bool IsHandcar(int index) => HandcarNow is { } h && h.Id == index;

    /// <summary>The handcar's lantern (note 729), on the rails: a practical light, so it goes in before what it lights.</summary>
    void HandcarLamp(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        if (HandcarNow is not { OnRails: true } || !IsHandcar(frame.Index))
            return;
        var t = HandcarTuning;
        var lamp = frame.ToWorld(new Double3(t.Width / 2 - 0.15, t.DeckHeight + 0.75, -t.Length / 2 + 0.1));
        mesh.PointLights.Add(new PointLight(V(lamp, eye), Palette.LampAmber * 1.5f, 9f));
    }

    /// <summary>The handcar's tuning as the train has it (its sizes), or the default's.</summary>
    HandcarTuning HandcarTuning { get; set; } = new();

    /// <summary>The handcar as the train has it now (note 729), for a scene built from frames alone (the app's, each frame).</summary>
    public void HandcarFrom(TrainOnLine train)
    {
        HandcarTuning = train.Dynamics.Tuning.Handcar;
        HandcarNow = train.Handcar is { } h ? (h.Id, h.Phase, !h.Carried, h.Lift, h.Lift > 0 ? train.HandcarLiftedTo() : null) : null;
        HandcarRolled = train.Handcar is { Carried: false } && train.HandcarRake is { } rake ? rake.Distance : 0;
        HandcarHooks = null;
        if (train.Handcar is { Carried: true, Lift: <= 0 } && train.HandcarHost is var host && host >= 0 && host < train.Frames.Count)
        {
            var van = train.Frames[host];
            double wall = van.Shape.Platform is { } platform ? platform.Min.Z : van.Shape.HalfLength, top = van.Shape.RoofHeight - 0.25;
            HandcarHooks = (van.ToWorld(new Double3(-ChainApart, top, wall)), van.ToWorld(new Double3(ChainApart, top, wall)));
        }
    }

    /// <summary>How far either side of the middle the hung handcar's chains hang (m), from its top end's beam to the van's roof.</summary>
    const double ChainApart = 0.6;

    /// <summary>The handle's post over the deck, each arm from it to a grip bar, and how far an end goes up or down (m).</summary>
    const double HandlePost = 0.95, HandleArm = 0.8, HandleLift = 0.28, GripHalf = 0.3;

    static float HandleTilt(double phase) => (float)Math.Atan2(Handcar.EndHeight(phase, -1) * HandleLift, HandleArm);

    /// <summary>
    /// Where a pumper's hand holds the handle (note 729), in the handcar's frame: on the grip bar at <paramref name="end"/>,
    /// <paramref name="side"/> −1 left or +1 right of the middle, as the handle stands at the swing.
    /// </summary>
    public static Double3 HandleGrip(HandcarTuning t, double phase, int end, int side)
    {
        double tilt = HandleTilt(phase);
        return new Double3(side * GripHalf, t.DeckHeight + HandlePost - end * HandleArm * Math.Sin(tilt), end * HandleArm * Math.Cos(tilt));
    }

    void HandcarModel(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        var t = HandcarTuning;
        if (HandcarNow is { Lift: > 0, To: { } to } lifting)
            frame = Lifted(frame, to, lifting.Lift);
        if (Look is { } look && Art.PropArt.Of(look) is var props && props.Get("handcar_body") is { } body)
        {
            HandcarModelled(mesh, frame, eye, props, body);
            return;
        }
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;
        void Box(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), right, up, back, new Vector3((float)hx, (float)hy, (float)hz), colour);
        mesh.Seed = frame.Index + 1;
        double w = t.Width / 2, l = t.Length / 2, h = t.DeckHeight;
        // The deck, its planks across, and the two sills under it.
        int planks = 9;
        for (int i = 0; i < planks; i++)
        {
            double z = -l + (i + 0.5) * t.Length / planks;
            Box(0, h - 0.04, z, w, 0.04, t.Length / planks / 2 - 0.015, HandcarWood * (0.92f + 0.06f * (i % 3)));
        }
        foreach (int side in (ReadOnlySpan<int>)[-1, 1])
        {
            Box(side * 0.6, h - 0.14, 0, 0.06, 0.06, l - 0.05, HandcarIron);
            // The side step, half way up.
            Box(side * (w + 0.15), t.StepHeight - 0.03, 0, 0.15, 0.03, 0.4, HandcarWood * 0.8f);
            Box(side * (w + 0.02), (t.StepHeight + h) / 2 - 0.05, 0, 0.02, (h - t.StepHeight) / 2, 0.03, HandcarIron);
        }
        // Four small wheels on the rails, each two crossed boxes (an eight-sided disc), and their axles.
        const double radius = 0.26, gauge = 0.72;
        foreach (double z in (ReadOnlySpan<double>)[-l + 0.55, l - 0.55])
        {
            Box(0, radius, z, gauge, 0.03, 0.03, HandcarIron);
            foreach (int side in (ReadOnlySpan<int>)[-1, 1])
                for (int k = 0; k < 2; k++)
                {
                    float a = k * MathF.PI / 4;
                    var wu = up * MathF.Cos(a) + back * MathF.Sin(a);
                    var wb = back * MathF.Cos(a) - up * MathF.Sin(a);
                    mesh.Box(L(side * gauge, radius, z), right, wu, wb, new Vector3(0.04f, (float)radius, (float)(radius * 0.42)), HandcarIron);
                }
        }
        // The post, and the see-saw handle on it: its front end as high as the sim's swing has it.
        double post = h + HandlePost, phase = HandcarNow is { } now ? now.Phase : 0;
        Box(0, (h + post) / 2, 0, 0.07, (post - h) / 2, 0.07, HandcarIron);
        float tilt = HandleTilt(phase);
        // Tilted about the car's right: its front (−Z) end up.
        var bu = up * MathF.Cos(tilt) + back * MathF.Sin(tilt);
        var bb = back * MathF.Cos(tilt) - up * MathF.Sin(tilt);
        mesh.Box(L(0, post, 0), right, bu, bb, new Vector3(0.05f, 0.05f, (float)HandleArm), HandcarWood * 1.1f);
        foreach (int end in (ReadOnlySpan<int>)[-1, 1])
            mesh.Box(L(0, post, 0) + bb * (float)(end * HandleArm), right, bu, bb, new Vector3((float)GripHalf + 0.12f, 0.03f, 0.03f), HandcarIron);
        // The lantern on the front end, lit on the rails (its light goes in with the practical lights: HandcarLamp).
        bool lit = HandcarNow is { OnRails: true };
        Box(w - 0.15, h + 0.35, -l + 0.1, 0.03, 0.35, 0.03, HandcarIron);
        Box(w - 0.15, h + 0.75, -l + 0.1, 0.09, 0.12, 0.09, lit ? Palette.LampAmber * 2.2f : HandcarIron);
        if (lit)
            mesh.Billboard(L(w - 0.15, h + 0.75, -l + 0.1), 0.5f, 0, new Vector4(Palette.LampAmber * 0.8f, 1), -1, FxBlend.Additive);
    }

    /// <summary>The wheels' radius and where they stand (m): across from the middle, and along it (tools/models/recipes/handcar.py).</summary>
    const double WheelRadius = 0.26, WheelAcross = 0.72, WheelAlong = 0.95;

    /// <summary>
    /// The handcar modelled (note 816): its body on the frame; each wheel on its axle end, its flange inboard, turned by the
    /// distance run (rolled forward, its top goes forward); the handle on its bearing, tilted as the sim's swing has it (as
    /// <see cref="HandleGrip"/>'s); the hand lamp hung off the bracket at the front, lit on the rails; and hung on the van, by
    /// its two chains from the van's roof end to its top end's beam.
    /// </summary>
    void HandcarModelled(MeshBuilder mesh, CarFrame frame, Double3 eye, Art.PropArt props, MeshAsset body)
    {
        var t = HandcarTuning;
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;
        static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z, Vector3 at) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);
        mesh.Seed = frame.Index + 1;
        mesh.Append(body, Basis(right, up, back, o));
        if (props.Get("handcar_wheel") is { } wheel)
        {
            float roll = (float)-(HandcarRolled % (2 * Math.PI * WheelRadius) / WheelRadius);
            foreach (int side in (ReadOnlySpan<int>)[-1, 1])
                foreach (double along in (ReadOnlySpan<double>)[-WheelAlong, WheelAlong])
                {
                    // (Its flange is on its own −X: the right's as it is, the left's turned round, which turns its roll round too.)
                    var spun = Matrix4x4.CreateRotationX(side > 0 ? roll : -roll) * (side > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI));
                    mesh.Append(wheel, spun * Basis(right, up, back, L(side * WheelAcross, WheelRadius, along)));
                }
        }
        double phase = HandcarNow is { } now ? now.Phase : 0;
        if (props.Get("handcar_handle") is { } handle)
        {
            float tilt = HandleTilt(phase);
            var bu = up * MathF.Cos(tilt) + back * MathF.Sin(tilt);
            var bb = back * MathF.Cos(tilt) - up * MathF.Sin(tilt);
            mesh.Append(handle, Basis(right, bu, bb, L(0, t.DeckHeight + HandlePost, 0)));
        }
        // The lamp off its bracket's arm at the front right corner, lit on the rails (its light: HandcarLamp).
        bool lit = HandcarNow is { OnRails: true };
        double w = t.Width / 2, l = t.Length / 2;
        if (props.Get("hand_lantern") is { } lantern)
            mesh.Append(lantern, Basis(right, up, back, L(w - 0.15, t.DeckHeight + 0.58 - Art.CreatureArt.LanternRing, -l - 0.02)));
        if (lit)
            mesh.Billboard(L(w - 0.15, t.DeckHeight + 0.58 - Art.CreatureArt.LanternRing + Art.CreatureArt.LanternFlame, -l - 0.02), 0.45f, 0,
                new Vector4(Palette.LampAmber * 0.8f, 1), -1, FxBlend.Additive);
        // Hung on the van: by two chains from its roof end to the top end's beam, each sagging a little at its middle.
        if (HandcarHooks is { } hooks && HandcarNow is { OnRails: false, Lift: <= 0 } && props.Get("handcar_chain") is { } chain)
            foreach (var (hook, side) in (ReadOnlySpan<(Double3, int)>)[(hooks.Left, -1), (hooks.Right, 1)])
            {
                var a = V(hook, eye);
                var b = L(side * ChainApart, t.DeckHeight - 0.1, -l + 0.05);
                var mid = (a + b) * 0.5f - Vector3.UnitY * 0.06f * Vector3.Distance(a, b);
                foreach (var (from, to) in (ReadOnlySpan<(Vector3, Vector3)>)[(a, mid), (mid, b)])
                {
                    var along = to - from;
                    float length = along.Length();
                    if (length < 1e-3f)
                        continue;
                    var x = along / length;
                    var y = Vector3.Normalize(Vector3.Cross(x, MathF.Abs(x.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                    mesh.Append(chain, Basis(x * length, y, Vector3.Cross(x, y), from));
                }
            }
    }
}
