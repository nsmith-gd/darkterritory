using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Dave, the wandering painter (the director, 8 Oct 2026: "a unique model so he's recognizable from afar ... Dave doesn't
/// have a beard, he's a bit tubby on the belly and has glasses. He's extremely fashionable and wears Birks sandals often. He
/// loves vests and cool hats. Players often find him wearing different cool hats and vests. He's a true artist"; GDD §3.2;
/// ARCHITECTURE §8 note 526). He's dressed after the director's photographs of the man he's named for: grey hair, clean-shaven,
/// black rectangular glasses, a small black hoop in his left ear and a silver chain, a black tee and light jeans under his
/// vest. The survivors' figure (<see cref="Redress"/>: no coat, no scarf, no lamp) in his own things, the only person out there
/// dressed for anything but the dark:
/// <list type="bullet">
/// <item>one of his hats (<see cref="Hats"/>: the wide straw, a beret, a fedora with a feather, his black felt, a panama, a
/// ball cap with a bright patch);</item>
/// <item>one of his waistcoats (<see cref="Vests"/>, his black leather one among them), over a belly he's fond of;</item>
/// <item>his glasses, and no mask: he breathes the air as it is, and nobody knows how;</item>
/// <item>his sandals, cork and buckled straps, on bare feet.</item>
/// </list>
/// Which hat and which vest are the night's (<see cref="Outfit"/>), so the crews who meet him more than once rarely see him
/// dressed the same. He stands at a tall easel with a lantern hung from its top, lit, so he's a warm point and a hat's
/// silhouette from across the ground. On the easel is the world as it was: <see cref="Landscape"/>, in the same hand as his
/// murals in the towns (<see cref="CivicKit"/>'s variant 3 and up), signed in the corner with a red D.
/// </summary>
public sealed class DaveKit(Look? look)
{
    readonly Dictionary<string, MeshAsset> _pieces = [];

    // The survivors' head in their bind pose (TownsfolkKit's measurements): the head's middle front to back and its
    // half-width, its crown; and where the eyes are.
    const float HeadZ = -0.026f, HeadHalfX = 0.1f, Crown = 1.794f, EyeY = 1.655f, EyeZ = -0.128f;
    // The feet's bind (foot_l and foot_r at ±0.105, the ankle 8.5 cm up; the ball 13 cm in front, at the floor).
    const float FootX = 0.105f;

    /// <summary>The figure he's drawn as (<see cref="Clothes"/> on the survivors', CreatureArt's dressed figures).</summary>
    public const string Figure = "dave";

    /// <summary>
    /// Under the vest and hat: a black tee, short-sleeved, his forearms bare; light, washed jeans; bare feet in his sandals.
    /// His face and hands are the figure's own, fairer.
    /// </summary>
    public static readonly IReadOnlyDictionary<Cloth, Dye> Clothes = new Dictionary<Cloth, Dye>
    {
        [Cloth.Face] = new(null, new Vector3(1.4f, 1.22f, 1.1f), 0.12f),
        [Cloth.Hands] = new(null, new Vector3(1.3f, 1.15f, 1.05f), 0.1f),
        [Cloth.Shirt] = new("cream", new Vector3(0.13f, 0.125f, 0.12f)),
        [Cloth.Sleeves] = new("cream", new Vector3(0.13f, 0.125f, 0.12f)),
        [Cloth.Forearms] = new("cream", new Vector3(0.78f, 0.56f, 0.45f), 0.1f),
        [Cloth.Trousers] = new("cream", new Vector3(0.42f, 0.52f, 0.66f)),
        [Cloth.Feet] = new("cream", new Vector3(0.78f, 0.56f, 0.45f), 0.1f),
    };

    /// <summary>How many landscapes he paints (his canvases and his murals).</summary>
    public const int Scenes = 3;

    /// <summary>His hats and his waistcoats: how many of each to draw from.</summary>
    public const int Hats = 6, Vests = 6;

    /// <summary>
    /// What he's wearing tonight (a hat, a vest), from where he's standing along the line: the same on every machine (it's
    /// replicated), and a different pairing most nights.
    /// </summary>
    public static (int Hat, int Vest) Outfit(double along)
    {
        if (Wearing is { } staged)
            return staged;
        ulong h = Sim.LineGen.Streams.Mix(0xDA7E, "outfit", "", (long)Math.Floor(along));
        return ((int)(h % Hats), (int)(h / Hats % Vests));
    }

    /// <summary>A hat and vest to stage him in, whatever the night's (<c>dt screenshot --dave … --outfit h,v</c>); null for the night's.</summary>
    public static (int Hat, int Vest)? Wearing { get; set; }

    /// <summary>The easel in front of him (his frame: he faces −Z), its lantern's flame there, and how far out it stands.</summary>
    public static readonly Vector3 Flame = new(0.42f, 1.86f, -0.78f);
    const float EaselOut = 0.78f;

    /// <summary>The clip he plays: at his canvas the brush arm out; turned on someone, still; with them by the neck, both hands out.</summary>
    public static string Clip(Sim.Enemies.SpinePhase phase) => phase switch
    {
        Sim.Enemies.SpinePhase.Telegraph => "idle",
        Sim.Enemies.SpinePhase.Commit or Sim.Enemies.SpinePhase.Grab or Sim.Enemies.SpinePhase.Punish => "push",
        _ => "point",
    };

    public MeshAsset Hat(int kind) => Get("hat." + kind % Hats, k => HatOf(k, kind % Hats));
    public MeshAsset Vest(int kind) => Get("vest." + kind % Vests, k => VestOf(k, kind % Vests));
    public MeshAsset Glasses => Get("glasses", BlackFrames);
    /// <summary>His grey hair, short and pushed about, round the back and sides (and the top, under a hat or not).</summary>
    public MeshAsset Hair => Get("hair", GreyHair);
    /// <summary>The small black hoop in his left ear, and the silver chain round his neck (worn on the head and the chest).</summary>
    public MeshAsset Earring => Get("earring", Hoop);
    public MeshAsset Chain => Get("chain", SilverChain);
    /// <summary>A sandal, on <paramref name="left"/>'s foot or the right (worn on that foot's bone).</summary>
    public MeshAsset Sandal(bool left) => Get(left ? "sandal.l" : "sandal.r", k => SandalOn(k, left ? -FootX : FootX));

    /// <summary>The easel with <paramref name="scene"/> on it and its lantern, in his frame (he faces −Z, his feet at the origin).</summary>
    public MeshAsset Easel(int scene) => Get("easel." + (scene % Scenes), k => EaselWith(k, scene % Scenes));

    /// <summary>Puts on him, as drawn last at <paramref name="model"/>: the hat and vest, his hair, glasses, hoop, chain and sandals.</summary>
    public void Dress(CreatureArt creatures, MeshBuilder mesh, in Matrix4x4 model, int hat, int vest)
    {
        creatures.Wear(mesh, Hair, "head", model, Figure);
        creatures.Wear(mesh, Hat(hat), "head", model, Figure);
        creatures.Wear(mesh, Glasses, "head", model, Figure);
        creatures.Wear(mesh, Earring, "head", model, Figure);
        creatures.Wear(mesh, Chain, "spine_03", model, Figure);
        creatures.Wear(mesh, Vest(vest), "spine_02", model, Figure);
        creatures.Wear(mesh, Sandal(true), "foot_l", model, Figure);
        creatures.Wear(mesh, Sandal(false), "foot_r", model, Figure);
    }

    MeshAsset Get(string key, Action<Kit> build)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look, 4700 + key.Length);
        build(k);
        return _pieces[key] = k.Build("dave." + key);
    }

    /// <summary>Paints what's drawn next in a flat colour (the cream ground, tinted): his clothes are colours, not stuff.</summary>
    static void Colour(Kit k, Vector3 colour, float wear = 0.5f, float shine = 0.05f)
    {
        k.Use("cream", colour, wear, shine, tile: 1);
        k.Tint = colour;
    }

    /// <summary>A ring round the head at <paramref name="y"/>, <paramref name="grow"/> out from it (a crown's or a brim's).</summary>
    static Vector3[] Ring(float y, float rx, float rz, int n = 18, float droop = 0, float tiltZ = 0)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n, sz = MathF.Cos(a);
            ring[i] = new Vector3(MathF.Sin(a) * rx, y - droop + tiltZ * sz, HeadZ + sz * rz);
        }
        return ring;
    }

    /// <summary>
    /// One of his hats: 0 the wide straw with a red band; 1 a beret, plum, pulled to one side; 2 a grey fedora with a black
    /// band and a pheasant's feather; 3 his black felt, a tall round crown and a short brim turned up; 4 a cream panama with a
    /// black band; 5 a faded navy ball cap with a bright round patch on its front.
    /// </summary>
    static void HatOf(Kit k, int kind)
    {
        float r = HeadHalfX + 0.03f, foot = 1.70f;
        switch (kind)
        {
            case 1:
                {
                    // The beret: a band snug round the head, then a wide soft disc, tipped down over his right ear.
                    Colour(k, new Vector3(0.45f, 0.12f, 0.3f), 0.7f);
                    var c = new Vector3(0, 1.735f, HeadZ);
                    var tilt = Matrix4x4.CreateRotationZ(-0.28f, c);
                    k.With(tilt, () =>
                    {
                        k.Loft([Ring(foot + 0.03f, r, r + 0.012f), Ring(foot + 0.06f, r + 0.05f, r + 0.06f), Ring(foot + 0.095f, r + 0.07f, r + 0.075f),
                            Ring(foot + 0.115f, r + 0.02f, r + 0.025f), Ring(foot + 0.12f, 0.01f, 0.01f)], closed: true, twoSided: true);
                        k.Cylinder(new Vector3(0, foot + 0.12f, HeadZ), new Vector3(0, foot + 0.15f, HeadZ), 0.008f, 5);
                    });
                    break;
                }
            case 2:
                {
                    // The fedora: a pinched crown, a black band, a brim snapped down at the front, a feather in the band.
                    Colour(k, new Vector3(0.42f, 0.42f, 0.44f), 0.55f);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot + 0.08f, r - 0.004f, r + 0.012f), Ring(foot + 0.125f, r - 0.02f, r - 0.005f),
                        Ring(foot + 0.115f, 0.03f, 0.05f)], closed: true, twoSided: true);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot - 0.005f, r + 0.06f, r + 0.07f), Ring(foot - 0.01f, r + 0.075f, r + 0.085f, droop: 0, tiltZ: 0.025f)],
                        closed: true, twoSided: true);
                    Colour(k, new Vector3(0.06f, 0.06f, 0.07f), 0.4f);
                    k.Loft([Ring(foot + 0.004f, r + 0.003f, r + 0.018f), Ring(foot + 0.035f, r + 0.002f, r + 0.017f)], closed: true, twoSided: true);
                    Colour(k, new Vector3(0.7f, 0.42f, 0.18f), 0.3f);
                    var f0 = new Vector3(r - 0.01f, foot + 0.025f, HeadZ + 0.03f);
                    k.Quad(f0, f0 + new Vector3(0.012f, 0, 0.012f), f0 + new Vector3(0.03f, 0.12f, 0.07f), f0 + new Vector3(0.02f, 0.12f, 0.06f), twoSided: true);
                    break;
                }
            case 3:
                {
                    // His black felt (the photograph at the alpacas'): a tall crown, a little narrower at its rounded top, and a
                    // short brim rolled up all round, a band of the same black a shade duller.
                    Colour(k, new Vector3(0.05f, 0.05f, 0.055f), 0.7f, 0.08f);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot + 0.1f, r - 0.006f, r + 0.006f), Ring(foot + 0.15f, r - 0.016f, r - 0.006f),
                        Ring(foot + 0.165f, r - 0.05f, r - 0.04f), Ring(foot + 0.17f, 0.01f, 0.01f)], closed: true, twoSided: true);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot - 0.004f, r + 0.04f, r + 0.05f), Ring(foot + 0.02f, r + 0.058f, r + 0.068f)],
                        closed: true, twoSided: true);
                    Colour(k, new Vector3(0.025f, 0.025f, 0.03f), 0.5f, 0.15f);
                    k.Loft([Ring(foot + 0.004f, r + 0.003f, r + 0.018f), Ring(foot + 0.03f, r + 0.002f, r + 0.017f)], closed: true, twoSided: true);
                    break;
                }
            case 5:
                {
                    // The ball cap (his photograph with the bottle): a soft six-panel crown in faded navy, a button on top, its
                    // peak curved out over the glasses, and on its front a round patch, a mandala in his own bright colours.
                    Colour(k, new Vector3(0.16f, 0.17f, 0.24f), 0.8f);
                    var c = new Vector3(0, foot - 0.01f, HeadZ);
                    float top = Crown - c.Y + 0.03f;
                    k.Lathe(c, [new(r + 0.004f, 0), new(r + 0.002f, top * 0.45f), new(HeadHalfX * 0.85f, top * 0.85f), new(0.04f, top), new(0, top + 0.004f)], 16);
                    k.Cylinder(c + new Vector3(0, top, 0), c + new Vector3(0, top + 0.012f, 0), 0.008f, 6);
                    // The peak: a curved slab out the front.
                    Vector3 Peak(float u, float out_) => new(MathF.Sin(u) * (r + out_) * 0.95f, foot + 0.005f - out_ * 0.12f, HeadZ - MathF.Cos(u) * (r + out_));
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = -0.95f + i * 0.2375f, a1 = a0 + 0.2375f;
                        k.Quad(Peak(a0, 0), Peak(a1, 0), Peak(a1, 0.085f), Peak(a0, 0.085f), twoSided: true);
                    }
                    var patch = new Vector3(0, foot + 0.05f, HeadZ - r - 0.006f);
                    foreach (var (radius, colour) in new[] { (0.026f, new Vector3(0.9f, 0.88f, 0.8f)), (0.02f, new Vector3(0.25f, 0.55f, 0.85f)),
                        (0.014f, new Vector3(0.9f, 0.35f, 0.25f)), (0.008f, new Vector3(0.95f, 0.8f, 0.25f)) })
                    {
                        Colour(k, colour, 0.3f);
                        patch.Z -= 0.0012f;
                        k.Disc(patch, -Vector3.UnitZ, radius, 12);
                    }
                    break;
                }
            case 4:
                {
                    // The panama: a cream crown with a crease, a black band, a brim turned up at the back.
                    Colour(k, new Vector3(0.92f, 0.88f, 0.76f), 0.45f);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot + 0.09f, r - 0.005f, r + 0.008f), Ring(foot + 0.11f, r - 0.04f, r - 0.03f),
                        Ring(foot + 0.1f, 0.02f, 0.06f)], closed: true, twoSided: true);
                    k.Loft([Ring(foot, r, r + 0.015f), Ring(foot - 0.01f, r + 0.07f, r + 0.08f, tiltZ: 0.03f)], closed: true, twoSided: true);
                    Colour(k, new Vector3(0.05f, 0.05f, 0.05f), 0.4f);
                    k.Loft([Ring(foot + 0.004f, r + 0.003f, r + 0.018f), Ring(foot + 0.03f, r + 0.002f, r + 0.017f)], closed: true, twoSided: true);
                    break;
                }
            default:
                {
                    // The wide straw: a low round crown and a red band, its brim the widest thing on any head out there.
                    Colour(k, new Vector3(1.05f, 0.85f, 0.5f), 0.5f);
                    var c = new Vector3(0, foot, HeadZ);
                    float top = Crown - c.Y + 0.025f;
                    k.Lathe(c, [new(r, 0), new(r + 0.002f, top * 0.55f), new(HeadHalfX + 0.012f, top * 0.9f), new(0.05f, top), new(0.0f, top - 0.012f)], 16);
                    k.Loft([Ring(foot, r, r, 20), Ring(foot - 0.012f, 0.24f, 0.24f, 20), Ring(foot - 0.045f, 0.36f, 0.36f, 20)], closed: true, twoSided: true);
                    Colour(k, new Vector3(0.85f, 0.18f, 0.12f), 0.4f);
                    k.Lathe(c + new Vector3(0, 0.005f, 0), [new(r + 0.004f, 0), new(r + 0.004f, 0.035f), new(r, 0.036f)], 16, capTop: false);
                    break;
                }
        }
        k.Tint = Vector3.One;
    }

    /// <summary>
    /// One of his waistcoats, over the belly he's fond of (the torso's rings, bulged at the front low down), buttoned with
    /// brass: 0 mustard corduroy; 1 plum velvet; 2 a green tartan, its checks in bands; 3 teal brocade with gold trim; 4 a
    /// patchwork of his own paints; 5 his black leather, silver snaps (the photograph outside the gallery).
    /// </summary>
    static void VestOf(Kit k, int kind)
    {
        (Vector3 Body, Vector3 Trim) = kind switch
        {
            1 => (new Vector3(0.42f, 0.12f, 0.3f), new Vector3(0.25f, 0.06f, 0.18f)),
            2 => (new Vector3(0.16f, 0.36f, 0.22f), new Vector3(0.65f, 0.15f, 0.12f)),
            3 => (new Vector3(0.1f, 0.42f, 0.45f), new Vector3(0.85f, 0.65f, 0.25f)),
            4 => (new Vector3(0.8f, 0.42f, 0.2f), new Vector3(0.2f, 0.4f, 0.75f)),
            5 => (new Vector3(0.05f, 0.045f, 0.045f), new Vector3(0.02f, 0.02f, 0.02f)),
            _ => (new Vector3(0.85f, 0.6f, 0.15f), new Vector3(0.45f, 0.3f, 0.1f)),
        };
        // Rings round the torso from the hips to the chest: (height, half-width, front, back). The belly's the front out at
        // 1.05-1.2 m; over the figure's frame everywhere (its front at z ≈ −0.18, its back +0.18 at the hips, +0.15 up top),
        // open at the armholes (the frame's 0.25 at 1.3 m).
        (float Y, float Half, float Front, float Back)[] torso =
            [(0.96f, 0.205f, 0.205f, 0.195f), (1.05f, 0.215f, 0.235f, 0.19f), (1.14f, 0.22f, 0.245f, 0.18f), (1.24f, 0.225f, 0.225f, 0.172f),
             (1.34f, 0.232f, 0.2f, 0.165f), (1.43f, 0.2f, 0.17f, 0.16f)];
        Vector3[] RingAt((float Y, float Half, float Front, float Back) t, float grow = 0)
        {
            const int n = 20;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n, c = MathF.Cos(a);
                ring[i] = new Vector3(MathF.Sin(a) * (t.Half + grow), t.Y, c < 0 ? c * (t.Front + grow) : c * (t.Back + grow));
            }
            return ring;
        }
        Colour(k, Body, kind == 1 ? 0.8f : 0.6f, kind is 3 or 5 ? 0.3f : 0.05f);
        if (kind == 2)
        {
            // Tartan: the body in bands of its two colours, a check of the trim between.
            for (int i = 0; i < torso.Length - 1; i++)
            {
                Colour(k, i % 2 == 0 ? Body : Vector3.Lerp(Body, Trim, 0.45f), 0.6f);
                k.Loft([RingAt(torso[i]), RingAt(torso[i + 1])], closed: true, twoSided: true);
            }
        }
        else if (kind == 4)
        {
            // Patchwork: each band another of his paints.
            Vector3[] paints = [new(0.8f, 0.42f, 0.2f), new(0.2f, 0.4f, 0.75f), new(0.85f, 0.75f, 0.2f), new(0.3f, 0.6f, 0.3f), new(0.75f, 0.2f, 0.2f)];
            for (int i = 0; i < torso.Length - 1; i++)
            {
                Colour(k, paints[i % paints.Length], 0.6f);
                k.Loft([RingAt(torso[i]), RingAt(torso[i + 1])], closed: true, twoSided: true);
            }
        }
        else
            k.Loft([.. torso.Select(t => RingAt(t))], closed: true, twoSided: true);
        // The trim: its hem, and the opening down the front where the tee shows, a V from the chest to the belly, laid on
        // the vest's own front (its rings' front, interpolated, and round by the width) just proud of it.
        Colour(k, Trim, 0.5f, kind == 3 ? 0.4f : 0.1f);
        k.Loft([RingAt(torso[0], 0.004f), RingAt(torso[0] with { Y = torso[0].Y + 0.03f }, 0.004f)], closed: true, twoSided: true);
        Vector3 On(float x, float y)
        {
            int i = 0;
            while (i < torso.Length - 2 && torso[i + 1].Y < y)
                i++;
            var (a, b) = (torso[i], torso[i + 1]);
            float f = Math.Clamp((y - a.Y) / (b.Y - a.Y), 0, 1);
            float half = a.Half + (b.Half - a.Half) * f, front = a.Front + (b.Front - a.Front) * f;
            float u = Math.Clamp(x / half, -0.95f, 0.95f);
            return new Vector3(x, y, -front * MathF.Sqrt(1 - u * u) - 0.006f);
        }
        const float bottom = 1.2f, top = 1.45f, open = 0.075f, edge = 0.014f, step = 0.025f;
        static float Wide(float y) => open * (y - bottom) / (top - bottom);
        Colour(k, Clothes[Cloth.Shirt].Colour, 0.6f);
        for (float y = bottom; y < top - 1e-4f; y += step)
        {
            float y1 = Math.Min(y + step, top);
            k.Quad(On(-Wide(y), y), On(Wide(y), y), On(Wide(y1), y1), On(-Wide(y1), y1), twoSided: true);
        }
        Colour(k, Trim, 0.5f, kind == 3 ? 0.4f : 0.1f);
        foreach (float side in (float[])[-1, 1])
            for (float y = bottom; y < top - 1e-4f; y += step)
            {
                float y1 = Math.Min(y + step, top);
                Vector3 Lapel(float yy, float out_) => On(side * (Wide(yy) + out_), yy) - new Vector3(0, 0, 0.001f);
                k.Quad(Lapel(y, 0), Lapel(y, edge), Lapel(y1, edge), Lapel(y1, 0), twoSided: true);
            }
        // Buttons down the belly, the way a waistcoat strains: brass, or the leather's silver snaps.
        Colour(k, kind == 5 ? new Vector3(0.8f, 0.8f, 0.82f) : Palette.TarnishedBrass * 1.6f, 0.3f, 0.7f);
        foreach (float y in (float[])[1.13f, 1.06f, 0.99f])
            k.Disc(On(0, y) - new Vector3(0, 0, 0.002f), -Vector3.UnitZ, 0.011f, 8);
        k.Tint = Vector3.One;
    }

    /// <summary>His glasses: black rectangular frames, thick at the top, the arms back to the ears (the glass clear: his eyes show).</summary>
    static void BlackFrames(Kit k)
    {
        Colour(k, new Vector3(0.03f, 0.03f, 0.03f), 0.3f, 0.6f);
        const float halfW = 0.025f, halfH = 0.015f, bar = 0.0028f, z = EyeZ - 0.004f;
        foreach (float side in (float[])[-1, 1])
        {
            var c = new Vector3(side * 0.036f, EyeY, z);
            k.BoxAt(c + new Vector3(0, halfH, 0), new Vector3(halfW + bar, bar * 1.6f, bar));
            k.BoxAt(c - new Vector3(0, halfH, 0), new Vector3(halfW + bar, bar, bar));
            k.BoxAt(c + new Vector3(halfW, 0, 0), new Vector3(bar, halfH, bar));
            k.BoxAt(c - new Vector3(halfW, 0, 0), new Vector3(bar, halfH, bar));
            // The arm, from the frame's outer top corner back over the ear.
            k.Cylinder(c + new Vector3(side * halfW, halfH * 0.6f, 0), new Vector3(side * 0.098f, EyeY + 0.008f, HeadZ + 0.02f), bar, 4);
        }
        k.BoxAt(new Vector3(0, EyeY + 0.006f, z), new Vector3(0.01f, bar, bar));
        k.Tint = Vector3.One;
    }

    /// <summary>
    /// His hair: grey, short and pushed about, a cap of it round the back and sides from the nape to the crown, its front edge
    /// up off the forehead, and a few tufts standing up on top (seen when the hat's a beret or a cap, and round its brim).
    /// </summary>
    static void GreyHair(Kit k)
    {
        Colour(k, new Vector3(0.66f, 0.65f, 0.63f), 0.35f);
        // Rings round the skull (measured off the figure's head: front −0.12, back +0.095, ±0.097 at the ears), each lower at
        // the back than the front: from the nape and over the ears up to the hairline, then the crown.
        static Vector3[] Round(float yBack, float yFront, float half, float front, float back)
        {
            const int n = 20;
            const float mid = -0.015f;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n, c = MathF.Cos(a);
                float z = c < 0 ? mid + c * (mid - front) : mid + c * (back - mid);
                ring[i] = new Vector3(MathF.Sin(a) * half, (yBack + yFront) / 2 - c * (yFront - yBack) / 2, z);
            }
            return ring;
        }
        k.Loft([Round(1.565f, 1.75f, 0.1f, -0.116f, 0.078f), Round(1.66f, 1.775f, 0.104f, -0.103f, 0.108f), Round(1.74f, 1.795f, 0.09f, -0.075f, 0.1f),
            Round(1.79f, 1.806f, 0.055f, -0.042f, 0.055f), Round(1.812f, 1.812f, 0.004f, -0.019f, -0.011f)], closed: true, twoSided: true);
        // Tufts: little wedges standing up and out on top and over the ears, as he's pushed his hand through it.
        foreach (var (x, y, z, lean) in new[] { (-0.03f, 1.8f, -0.06f, -0.4f), (0.025f, 1.805f, -0.03f, 0.3f), (-0.06f, 1.77f, 0.02f, -0.6f),
            (0.065f, 1.76f, 0.01f, 0.7f), (-0.104f, 1.66f, 0.0f, -1.1f), (0.106f, 1.66f, 0.005f, 1.1f), (0.0f, 1.79f, 0.05f, 0.1f) })
        {
            var root = new Vector3(x, y, HeadZ + z);
            var tip = root + new Vector3(MathF.Sin(lean) * 0.035f, MathF.Cos(lean) * 0.03f, 0.01f);
            k.Tri(root + new Vector3(-0.018f, 0, 0), root + new Vector3(0.018f, 0, 0), tip);
            k.Tri(root + new Vector3(0.018f, 0, 0), root + new Vector3(-0.018f, 0, 0), tip);
        }
        k.Tint = Vector3.One;
    }

    /// <summary>A small black hoop through his left ear lobe.</summary>
    static void Hoop(Kit k)
    {
        Colour(k, new Vector3(0.02f, 0.02f, 0.02f), 0.2f, 0.7f);
        var c = new Vector3(-HeadHalfX - 0.004f, 1.6f, HeadZ + 0.012f);
        const float r = 0.009f;
        for (int i = 0; i < 10; i++)
        {
            float a0 = i * MathF.Tau / 10, a1 = (i + 1) * MathF.Tau / 10;
            k.Cylinder(c + new Vector3(0, MathF.Cos(a0) * r, MathF.Sin(a0) * r), c + new Vector3(0, MathF.Cos(a1) * r, MathF.Sin(a1) * r), 0.0022f, 4, caps: false);
        }
    }

    /// <summary>A fine silver chain round his neck, down to a point on his chest (worn on spine_03: it sits on the tee).</summary>
    static void SilverChain(Kit k)
    {
        Colour(k, new Vector3(0.85f, 0.85f, 0.88f), 0.2f, 0.85f);
        Vector3[] half = [new(0.065f, 1.53f, 0.0f), new(0.075f, 1.49f, -0.08f), new(0.05f, 1.43f, -0.15f), new(0.02f, 1.39f, -0.17f), new(0, 1.375f, -0.172f)];
        foreach (float side in (float[])[-1, 1])
            for (int i = 0; i + 1 < half.Length; i++)
                k.Cylinder(half[i] with { X = half[i].X * side }, half[i + 1] with { X = half[i + 1].X * side }, 0.0025f, 4, caps: false);
        k.Cylinder(new Vector3(-0.065f, 1.53f, 0), new Vector3(-0.04f, 1.55f, 0.09f), 0.0025f, 4, caps: false);
        k.Cylinder(new Vector3(0.065f, 1.53f, 0), new Vector3(0.04f, 1.55f, 0.09f), 0.0025f, 4, caps: false);
        k.Cylinder(new Vector3(-0.04f, 1.55f, 0.09f), new Vector3(0.04f, 1.55f, 0.09f), 0.0025f, 4, caps: false);
    }

    /// <summary>A sandal on the foot at <paramref name="x"/>: a cork sole on a dark tread, and two broad buckled straps across.</summary>
    static void SandalOn(Kit k, float x)
    {
        // The tread and the cork footbed, heel to toe.
        Colour(k, new Vector3(0.1f, 0.09f, 0.08f), 0.6f);
        k.BoxAt(new Vector3(x, 0.006f, -0.06f), new Vector3(0.055f, 0.006f, 0.15f));
        Colour(k, new Vector3(0.72f, 0.55f, 0.36f), 0.7f);
        k.BoxAt(new Vector3(x, 0.02f, -0.06f), new Vector3(0.052f, 0.009f, 0.145f));
        // The two straps, brown leather, arched over the foot, each with its brass buckle on the outside.
        float outside = Math.Sign(x);
        foreach (var (z, height) in new[] { (-0.1f, 0.075f), (-0.02f, 0.105f) })
        {
            Colour(k, new Vector3(0.32f, 0.2f, 0.12f), 0.5f, 0.2f);
            var left = new Vector3(x - 0.058f, 0.025f, z);
            var right = new Vector3(x + 0.058f, 0.025f, z);
            var top = new Vector3(x, height, z);
            k.Quad(left, left + new Vector3(0, 0, -0.03f), top + new Vector3(0, 0, -0.03f), top, twoSided: true);
            k.Quad(top, top + new Vector3(0, 0, -0.03f), right + new Vector3(0, 0, -0.03f), right, twoSided: true);
            Colour(k, Palette.TarnishedBrass * 1.6f, 0.3f, 0.7f);
            k.BoxAt(new Vector3(x + outside * 0.045f, height * 0.6f, z - 0.015f), new Vector3(0.004f, 0.012f, 0.012f));
        }
        k.Tint = Vector3.One;
    }

    /// <summary>The easel: three legs, the ledge and the canvas on it, the lantern on its hook, a paint box at its foot.</summary>
    static void EaselWith(Kit k, int scene)
    {
        float z = -EaselOut;
        k.Use("wood_grey", Palette.DeepBrown, 0.6f, 0.05f, tile: 1);
        k.Tint = new Vector3(1.1f, 0.85f, 0.6f);
        var top = new Vector3(0, 1.95f, z - 0.05f);
        k.Cylinder(new Vector3(-0.32f, 0, z + 0.05f), top, 0.018f, 5);
        k.Cylinder(new Vector3(0.32f, 0, z + 0.05f), top, 0.018f, 5);
        k.Cylinder(new Vector3(0, 0, z - 0.55f), top + new Vector3(0, -0.05f, 0), 0.018f, 5);
        // The ledge the canvas stands on, and the arm out to the lantern's hook.
        k.BoxAt(new Vector3(0, 0.98f, z + 0.03f), new Vector3(0.36f, 0.015f, 0.04f));
        k.Cylinder(top + new Vector3(0, -0.03f, 0), Flame + new Vector3(0, 0.22f, 0), 0.012f, 4);
        // The canvas: its stretcher's edge, then the picture, facing him (+Z: he's behind it at the origin, looking −Z).
        k.Use("cream", Palette.BoardEnamel, 0.2f, 0, tile: 1);
        k.Tint = new Vector3(1.2f, 1.17f, 1.1f);
        const float w = 0.42f, h = 0.62f, y0 = 1.0f;
        k.Box(new Vector3(-w, y0, z), new Vector3(w, y0 + h, z + 0.025f));
        k.With(Matrix4x4.CreateRotationY(MathF.PI) * Kit.At(0, y0, z + 0.03f), () => Landscape(k, w - 0.02f, h - 0.02f, scene, back: true));
        // The paint box at the easel's foot, open.
        k.Use("wood_crate", Palette.DeepBrown, 0.7f, 0, tile: 1);
        k.BoxAt(new Vector3(-0.38f, 0.07f, z + 0.25f), new Vector3(0.17f, 0.07f, 0.11f));
        foreach (var (dx, colour) in new[] { (-0.48f, new Vector3(0.85f, 0.2f, 0.15f)), (-0.42f, new Vector3(0.2f, 0.45f, 0.85f)), (-0.36f, new Vector3(0.95f, 0.8f, 0.2f)), (-0.3f, new Vector3(0.25f, 0.65f, 0.3f)) })
        {
            k.Use("cream", colour, 0.2f, 0.1f, tile: 1);
            k.Tint = colour * 1.3f;
            k.BoxAt(new Vector3(dx, 0.145f, z + 0.25f), new Vector3(0.025f, 0.012f, 0.06f));
        }
        // The lantern on its hook: a frame and a glass, its flame the light (the scene adds it).
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.5f, tile: 1);
        k.Tint = Vector3.One;
        k.Cylinder(Flame + new Vector3(0, 0.2f, 0), Flame + new Vector3(0, 0.12f, 0), 0.005f, 4);
        k.BoxAt(Flame + new Vector3(0, 0.1f, 0), new Vector3(0.06f, 0.012f, 0.06f));
        k.BoxAt(Flame + new Vector3(0, -0.1f, 0), new Vector3(0.065f, 0.015f, 0.065f));
        k.Use("glass_dirty", Palette.LampAmber, 0.1f, 0.6f, tile: 1);
        k.Tint = new Vector3(1.6f, 1.2f, 0.7f);
        k.Emissive = 0.9f;
        k.BoxAt(Flame, new Vector3(0.05f, 0.09f, 0.05f));
        k.Emissive = 0;
    }

    /// <summary>
    /// One of his landscapes, the world as it was, centred on the origin's bottom edge in the XY plane, <paramref name="w"/>
    /// either side and <paramref name="h"/> tall, facing −Z (<paramref name="back"/>: also seen from +Z): a valley in June
    /// with its river, the sea and a lighthouse at noon, a lake under hills in October. Laid in bands and hills as a painter
    /// blocks them in, with the sun lit a little; signed bottom right with a red D.
    /// </summary>
    public static void Landscape(Kit k, float w, float h, int scene, bool back = false)
    {
        float layer = 0;
        void Paint(Vector3 colour, float emissive = 0)
        {
            k.Use("cream", colour, 0.25f, 0, tile: 1);
            k.Tint = colour * 1.2f;
            k.Emissive = emissive;
            layer -= 0.0015f;
        }
        Vector3 P(float u, float v) => new(-w + 2 * w * u, h * v, layer);
        void Rect(float u0, float u1, float v0, float v1) => k.Quad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), twoSided: back);
        // A hill: from u0 to u1 along the bottom at v0, its ridge the heights given, filled down to v0.
        void Hill(float v0, params float[] ridge)
        {
            int n = ridge.Length - 1;
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n, b = (float)(i + 1) / n;
                k.Quad(P(a, v0), P(b, v0), P(b, ridge[i + 1]), P(a, ridge[i]), twoSided: back);
            }
        }
        void Sun(float u, float v, float r, Vector3 colour)
        {
            Paint(colour, 0.35f);
            k.Disc(P(u, v), back ? Vector3.UnitZ : -Vector3.UnitZ, r * h, 14);
            k.Emissive = 0;
        }
        void Tree(float u, float v, float size, Vector3 colour)
        {
            Paint(new Vector3(0.3f, 0.2f, 0.12f));
            Rect(u - 0.004f, u + 0.004f, v, v + size * 0.3f);
            Paint(colour);
            k.Tri(P(u - size * 0.12f, v + size * 0.25f), P(u + size * 0.12f, v + size * 0.25f), P(u, v + size));
            if (back)
                k.Tri(P(u + size * 0.12f, v + size * 0.25f), P(u - size * 0.12f, v + size * 0.25f), P(u, v + size));
        }
        void Bird(float u, float v)
        {
            Paint(new Vector3(0.15f, 0.15f, 0.2f));
            Rect(u - 0.02f, u, v, v + 0.006f);
            Rect(u, u + 0.02f, v + 0.006f, v + 0.012f);
        }
        switch (scene % Scenes)
        {
            case 1:
                {
                    // The sea and a lighthouse at noon: the sky high and blue, the sea flecked, a headland of grass.
                    foreach (var (v0, v1, c) in new[] { (0.55f, 1f, new Vector3(0.38f, 0.62f, 0.9f)), (0.42f, 0.55f, new Vector3(0.62f, 0.8f, 0.95f)) })
                    {
                        Paint(c);
                        Rect(0, 1, v0, v1);
                    }
                    Paint(new Vector3(0.95f, 0.97f, 1.0f));
                    Rect(0.12f, 0.3f, 0.78f, 0.84f);
                    Rect(0.6f, 0.85f, 0.85f, 0.9f);
                    Sun(0.78f, 0.82f, 0.07f, new Vector3(1.0f, 0.95f, 0.7f));
                    Paint(new Vector3(0.12f, 0.42f, 0.62f));
                    Rect(0, 1, 0.12f, 0.42f);
                    Paint(new Vector3(0.85f, 0.95f, 1.0f));
                    for (int i = 0; i < 6; i++)
                        Rect(0.05f + i * 0.09f, 0.1f + i * 0.09f, 0.2f + i % 3 * 0.06f, 0.21f + i % 3 * 0.06f);
                    Paint(new Vector3(0.4f, 0.62f, 0.28f));
                    Hill(0, 0.35f, 0.32f, 0.3f, 0.24f, 0.12f, 0.05f, 0.0f);
                    Paint(new Vector3(0.62f, 0.52f, 0.4f));
                    Hill(0, 0.08f, 0.07f, 0.06f, 0.03f, 0.0f, 0.0f, 0.0f);
                    Paint(new Vector3(0.97f, 0.96f, 0.92f));
                    Rect(0.16f, 0.21f, 0.33f, 0.62f);
                    Paint(new Vector3(0.85f, 0.2f, 0.15f));
                    Rect(0.16f, 0.21f, 0.45f, 0.5f);
                    Rect(0.155f, 0.215f, 0.62f, 0.66f);
                    Bird(0.45f, 0.7f);
                    Bird(0.52f, 0.74f);
                    break;
                }
            case 2:
                {
                    // A lake under hills in October: the maples turned, the sky gold to blue, the hills in the water.
                    Paint(new Vector3(0.95f, 0.78f, 0.5f));
                    Rect(0, 1, 0.5f, 0.7f);
                    Paint(new Vector3(0.55f, 0.7f, 0.9f));
                    Rect(0, 1, 0.7f, 1f);
                    Sun(0.25f, 0.6f, 0.06f, new Vector3(1.0f, 0.85f, 0.5f));
                    Paint(new Vector3(0.35f, 0.42f, 0.6f));
                    Hill(0.5f, 0.62f, 0.7f, 0.66f, 0.58f, 0.64f, 0.72f, 0.6f);
                    Paint(new Vector3(0.82f, 0.38f, 0.15f));
                    Hill(0.42f, 0.5f, 0.56f, 0.52f, 0.48f, 0.55f, 0.6f, 0.52f);
                    Paint(new Vector3(0.3f, 0.45f, 0.62f));
                    Rect(0, 1, 0.12f, 0.42f);
                    // The hills again, upside down in the lake, paler.
                    Paint(new Vector3(0.55f, 0.42f, 0.35f));
                    for (int i = 0; i < 6; i++)
                        Rect(i / 6f, (i + 1) / 6f, 0.42f - 0.04f - i % 2 * 0.03f, 0.42f);
                    Paint(new Vector3(0.62f, 0.28f, 0.1f));
                    Hill(0, 0.18f, 0.14f, 0.1f, 0.08f, 0.1f, 0.15f, 0.2f);
                    foreach (var (u, s, c) in new[] { (0.08f, 0.22f, new Vector3(0.85f, 0.25f, 0.12f)), (0.16f, 0.18f, new Vector3(0.95f, 0.6f, 0.15f)),
                        (0.84f, 0.24f, new Vector3(0.75f, 0.2f, 0.1f)), (0.92f, 0.2f, new Vector3(0.2f, 0.4f, 0.22f)) })
                        Tree(u, 0.12f, s, c);
                    break;
                }
            default:
                {
                    // A valley in June at dawn: the sky gold to blue, blue hills far off, green fields with a river through them.
                    var bands = new[] { new Vector3(1.0f, 0.82f, 0.52f), new Vector3(0.95f, 0.85f, 0.65f), new Vector3(0.7f, 0.8f, 0.9f), new Vector3(0.45f, 0.65f, 0.9f) };
                    for (int i = 0; i < bands.Length; i++)
                    {
                        Paint(bands[i]);
                        Rect(0, 1, 0.5f + i * 0.125f, 0.5f + (i + 1) * 0.125f);
                    }
                    Sun(0.7f, 0.56f, 0.08f, new Vector3(1.0f, 0.9f, 0.55f));
                    Paint(new Vector3(0.45f, 0.52f, 0.7f));
                    Hill(0.45f, 0.6f, 0.68f, 0.62f, 0.55f, 0.6f, 0.66f, 0.58f);
                    Paint(new Vector3(0.45f, 0.68f, 0.32f));
                    Hill(0.25f, 0.5f, 0.52f, 0.47f, 0.45f, 0.5f, 0.48f, 0.46f);
                    Paint(new Vector3(0.3f, 0.55f, 0.22f));
                    Hill(0, 0.32f, 0.3f, 0.26f, 0.28f, 0.33f, 0.3f, 0.27f);
                    // The river, winding down toward us.
                    Paint(new Vector3(0.6f, 0.78f, 0.92f), 0.05f);
                    k.Quad(P(0.48f, 0.46f), P(0.52f, 0.46f), P(0.6f, 0.25f), P(0.5f, 0.25f), twoSided: back);
                    k.Quad(P(0.5f, 0.25f), P(0.6f, 0.25f), P(0.5f, 0), P(0.34f, 0), twoSided: back);
                    k.Emissive = 0;
                    foreach (var (u, v) in new[] { (0.12f, 0.27f), (0.2f, 0.25f), (0.78f, 0.3f), (0.86f, 0.28f), (0.92f, 0.33f) })
                        Tree(u, v, 0.16f, new Vector3(0.15f, 0.35f, 0.18f));
                    Bird(0.3f, 0.82f);
                    Bird(0.36f, 0.86f);
                    Bird(0.4f, 0.8f);
                    break;
                }
        }
        // Signed: a red D, bottom right, the same on every canvas and every wall.
        Paint(new Vector3(0.8f, 0.12f, 0.1f));
        // (as wide as it's tall, whatever the picture's shape)
        float dw = 0.06f * h / (2 * w);
        float du = 0.93f, dv = 0.035f, tall = 0.06f;
        Rect(du, du + dw * 0.3f, dv, dv + tall);
        Rect(du, du + dw, dv + tall * 0.85f, dv + tall);
        Rect(du, du + dw, dv, dv + tall * 0.15f);
        Rect(du + dw * 0.85f, du + dw * 1.15f, dv + tall * 0.15f, dv + tall * 0.85f);
        k.Emissive = 0;
        k.Tint = Vector3.One;
    }
}
