using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The hat rack's hats (ARCHITECTURE §8 note 714; the director approved "the hat rack": "four more looks in OUTFIT: a
/// stovepipe, a fez, a straw boater, a deerstalker"): built in the crew figure's bind pose (its feet at the origin, facing −Z,
/// its head's middle at about (0, 1.675, −0.02): tools/blender/crew.py), worn on its head bone over the cap or helmet.
/// </summary>
public static class HatKit
{
    const float CentreZ = -0.02f, Seat = 1.775f;
    static readonly Dictionary<(Look?, int), MeshAsset> Cache = [];

    /// <summary>Hat <paramref name="hat"/> (1 the stovepipe, 2 the fez, 3 the straw boater, 4 the deerstalker), built once.</summary>
    public static MeshAsset? Of(Look? look, int hat)
    {
        if (hat is < 1 or > 4)
            return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue((look, hat), out var piece))
                Cache[(look, hat)] = piece = Build(look, hat);
            return piece;
        }
    }

    static MeshAsset Build(Look? look, int hat)
    {
        var k = new Kit(look, 1730 + hat);
        var c = new Vector3(0, Seat, CentreZ);
        switch (hat)
        {
            case 1:
                // The stovepipe: a stiff brim and a tall black crown, its band a shade off.
                k.Use("hat_felt", new Vector3(0.05f, 0.05f, 0.06f), 0.5f, 0.15f);
                k.Cylinder(c, c + new Vector3(0, 0.012f, 0), 0.175f, 20);
                k.Cylinder(c, c + new Vector3(0, 0.22f, 0), 0.105f, 20, radiusB: 0.112f);
                k.Use("hat_band", new Vector3(0.16f, 0.15f, 0.17f), 0.4f, 0.2f);
                k.Cylinder(c + new Vector3(0, 0.012f, 0), c + new Vector3(0, 0.045f, 0), 0.107f, 20);
                break;
            case 2:
                // The fez: red, a cone cut off flat, its black tassel over the side.
                k.Use("hat_felt", new Vector3(0.62f, 0.08f, 0.07f), 0.5f, 0.1f);
                k.Cylinder(c - new Vector3(0, 0.02f, 0), c + new Vector3(0, 0.12f, 0), 0.11f, 18, radiusB: 0.085f);
                k.Use("hat_band", new Vector3(0.04f, 0.04f, 0.04f), 0.7f, 0);
                k.Rod(c + new Vector3(0, 0.12f, 0), c + new Vector3(0.07f, 0.1f, 0.02f), 0.004f);
                k.Rod(c + new Vector3(0.07f, 0.1f, 0.02f), c + new Vector3(0.085f, 0.04f, 0.02f), 0.008f);
                break;
            case 3:
                // The straw boater: a wide flat brim, a low flat crown, the black ribbon round it.
                k.Use("hat_straw", new Vector3(0.86f, 0.74f, 0.45f), 0.8f, 0.05f);
                k.Cylinder(c, c + new Vector3(0, 0.01f, 0), 0.19f, 22);
                k.Cylinder(c, c + new Vector3(0, 0.075f, 0), 0.108f, 22);
                k.Use("hat_band", new Vector3(0.06f, 0.06f, 0.1f), 0.5f, 0.1f);
                k.Cylinder(c + new Vector3(0, 0.01f, 0), c + new Vector3(0, 0.035f, 0), 0.11f, 22, caps: false);
                break;
            default:
                // The deerstalker: a tweed dome, its peaks fore and aft, the ear-flaps tied up over the top.
                k.Use("hat_tweed", new Vector3(0.45f, 0.36f, 0.24f), 0.95f, 0);
                k.Lathe(c - new Vector3(0, 0.02f, 0), [new(0.115f, 0), new(0.118f, 0.04f), new(0.105f, 0.085f), new(0.07f, 0.115f), new(0.0f, 0.125f)], 16);
                foreach (int fore in new[] { -1, 1 })
                {
                    var root = c + new Vector3(0, 0.005f, fore * 0.1f);
                    k.Quad(root + new Vector3(-0.08f, 0, 0), root + new Vector3(0.08f, 0, 0), root + new Vector3(0.06f, -0.025f, fore * 0.075f),
                        root + new Vector3(-0.06f, -0.025f, fore * 0.075f), twoSided: true);
                }
                k.Use("hat_band", new Vector3(0.3f, 0.22f, 0.14f), 0.9f, 0);
                k.Rod(c + new Vector3(-0.1f, 0.06f, 0), c + new Vector3(0, 0.12f, 0), 0.006f);
                k.Rod(c + new Vector3(0.1f, 0.06f, 0), c + new Vector3(0, 0.12f, 0), 0.006f);
                break;
        }
        return k.Build($"hat-{hat}");
    }
}
