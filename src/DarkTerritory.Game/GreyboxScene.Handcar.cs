using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The handcar as it's drawn (GDD §24.1; ARCHITECTURE §8 note 729): a plank deck on four small wheels, a post in the middle with
/// the see-saw handle on it, swinging as the sim's handle swings, and a lantern on the front end. Hung on the guard van's back
/// it's the same trolley stood on its end, its lantern dark. Boxes for now: the art pass's model is a later job.
/// </summary>
public sealed partial class GreyboxScene
{
    /// <summary>
    /// The handcar's vehicle id, its handle's swing, whether it's on the rails, and a lift under way (how far, and the frame
    /// it's being lifted to): note 729, set from the train each frame.
    /// </summary>
    public (int Id, double Phase, bool OnRails, double Lift, CarFrame? To)? HandcarNow { get; set; }

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
    }

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
}
