using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Art;

/// <summary>The engine's headlamp as the boards see it this frame (note 560): where it is, the way it shines, and whether it's lit.</summary>
public readonly record struct BoardLamp(Double3 Position, Double3 Direction, bool Lit);

/// <summary>
/// The line's lettered boards drawn where they stand (note 560; <see cref="BoardSite"/>, the sim's): the face
/// (<see cref="SignKit.Board"/>), its posts to the land, and the shine its reflective paint sends back to the lamp.
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>The headlamp this frame (GreyboxScene sets it as it builds): the boards shine back at it.</summary>
    public BoardLamp Lamp { get; set; }

    /// <summary>The signage kit's rules (signage.json) for a line with no plan of its own: a hand-laid line's boards.</summary>
    public (IReadOnlyDictionary<string, BoardDef> Boards, BillboardRules Rules) HandLaidSignage =>
        (_look.Signage?.Boards ?? new Dictionary<string, BoardDef>(), _look.Signage?.Billboard ?? new BillboardRules());

    /// <summary>
    /// How bright a board's reflective paint is (the instance's glow) with the lamp where it is: <see cref="BillboardRules.DarkGlow"/>
    /// off the beam (lamps down, a board is nothing in the dark till it's close, as the sim reads it: sight.json
    /// darkSignRange), in it <see cref="BillboardRules.LitGlow"/> close to and <see cref="BillboardRules.FarGlow"/> out at the
    /// board's reading distance (<paramref name="visible"/>), fading over its last fifth. It has to face the lamp to send it back.
    /// </summary>
    public static float Glow(BillboardRules r, BoardLamp lamp, Double3 centre, Double3 toward, double visible)
    {
        if (!lamp.Lit)
            return (float)r.DarkGlow;
        var to = centre - lamp.Position;
        double d = to.Length;
        if (d < 1e-3)
            return (float)r.DarkGlow;
        var dir = to * (1 / d);
        var beam = lamp.Direction.Normalized;
        double cone = Math.Cos(r.GlowConeDeg * Math.PI / 180), edge = Math.Cos(r.GlowConeDeg * 1.4 * Math.PI / 180);
        double inBeam = SmoothStep(edge, cone, Double3.Dot(dir, beam));
        double facing = Math.Clamp(-Double3.Dot(dir, toward) * 3, 0, 1);
        double reach = Math.Clamp((visible - d) / (visible * 0.2), 0, 1);
        // Brighter the further off it is: the fog takes the more of it (the paint sends the lamp back along its own line).
        double lit = r.LitGlow + (r.FarGlow - r.LitGlow) * Math.Clamp(d / visible, 0, 1);
        return (float)(r.DarkGlow + (lit - r.DarkGlow) * inBeam * facing * reach);
    }

    static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A board where it stands (<paramref name="site"/>), lit as the lamp finds it; <paramref name="fallen"/>, down in the
    /// brush at its foot, face up, its posts snapped.
    /// </summary>
    public void Billboard(MeshBuilder mesh, BoardSite site, BillboardRules r, double visible, Double3 eye, bool fallen = false)
    {
        var face = Piece($"board-{site.Type}-{site.Text}-{site.Face.Pixel:0.000}-{site.Face.Width:0.00}",
            () => SignKit.Board(_look, site.Type, site.Face, (float)r.Emissive, (float)r.PlainShare));
        var x = F(site.Right);
        var z = F(site.Toward);
        var y = Vector3.UnitY;
        var at = site.Bottom.RelativeTo(eye);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, at.X, at.Y, at.Z, 1);
        if (fallen)
        {
            // Down on its back at its foot: tipped over its bottom edge, lying on the land below it.
            double ground = site.Posts.Count > 0 ? site.Posts.Min(p => p.Foot.Y) : site.Bottom.Y;
            m = Matrix4x4.CreateRotationX(-1.45f) * Matrix4x4.CreateTranslation(0, (float)(ground + 0.3 - site.Bottom.Y), 0) * m;
            mesh.Instances.Add(new MeshInstance(face, m, (float)r.DarkGlow));
            return;
        }
        mesh.Instances.Add(new MeshInstance(face, m, Glow(r, Lamp, site.Centre, site.Toward, visible)));
        var post = Piece("sign-post", () => SignKit.Post(_look));
        float thick = site.Face.Width > 3 ? 0.32f : 0.18f;
        foreach (var (foot, height) in site.Posts)
        {
            // Behind the face, up from the land to near its top.
            var p = (foot - site.Toward * 0.3).RelativeTo(eye) - new Vector3(0, 0.3f, 0);
            mesh.Instances.Add(new MeshInstance(post, Matrix4x4.CreateScale(thick, (float)height + 0.3f, thick)
                * new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1)));
        }
    }

    /// <summary>
    /// A board stood beside a hand-laid line (no plan: no land of its own, nothing cleared), as a generated line's stand: back
    /// from the rail, turned in, over the rail by its height, on <paramref name="side"/> as given (the billboard side is the
    /// caller's: <see cref="BillboardRules.Side"/>).
    /// </summary>
    public BoardSite HandLaidSite(RailLine line, double s, int side, string type, string text)
    {
        if (!ReferenceEquals(_handLaidFor, line))
        {
            _handLaid.Clear();
            _handLaidFor = line;
        }
        var key = (s, side, type, text);
        if (_handLaid.TryGetValue(key, out var site))
            return site;
        var (boards, rules) = HandLaidSignage;
        var def = boards.GetValueOrDefault(type);
        double ground = line.Sample(Math.Clamp(s, 0, line.Length)).Position.Y - 0.3;
        // (Stood where it's put: a hand-laid line has no land to hide it.)
        return _handLaid[key] = LinesideProps.Place(def, BoardLayout.Of(type, text, def, rules), rules with { MaxBackM = 0 }, "main", line, s, side, type, text,
            (_, _) => ground, (_, _, _, _) => true, _ => false, _ => true);
    }

    readonly Dictionary<(double, int, string, string), BoardSite> _handLaid = [];
    RailLine? _handLaidFor;

    /// <summary>The side a board stands on as the train coming sees it (<see cref="BillboardRules.Side"/>; 0: the right, the driver's).</summary>
    public static int BillboardSide(BillboardRules rules) => rules.Side != 0 ? rules.Side : 1;
}
