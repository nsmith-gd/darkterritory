using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A harbour town's Hope Tower (queue #547, ARCHITECTURE §8 note 819; the World Building board's Cultures tab, the director:
/// "Extremely large lighthouses with bright burning flames above that people of the town call Flames of Hope for the world
/// to find in the darkness"). Not a lens light but the older kind, a coal fire in an iron basket on top (the Isle of May's
/// and Tynemouth's, before the lamps): a tapered stone tower of near eighty metres on its plinth (the tallest lights there are), string courses every storey of
/// stairs, slit windows going round it as the stair does (a few lit: the keeper's lamps on the way up), a corbelled gallery
/// railed in iron, and on it the basket on its pedestal with the coals in it. A davit off the gallery hauls the coal up on
/// a rope down the tower's side, a tub halfway. Cooked in its own frame, foot at the origin, its door toward −Z.
/// </summary>
public static class HopeKit
{
    /// <summary>The plinth's radius and height (m); TownFixtures "lighthouse" stands it in the Sim.</summary>
    public const float Plinth = 7.6f, PlinthTop = 2.4f;

    /// <summary>The shaft's radius at its foot and its top, and where its top is (m).</summary>
    public const float Foot = 6.0f, Neck = 4.0f, Top = 78f;

    /// <summary>The gallery's radius, and its floor over <see cref="Top"/> (m).</summary>
    public const float Gallery = 5.6f, Deck = Top + 0.4f;

    /// <summary>The basket: its floor's height and radius, its brim's height and radius (m).</summary>
    public const float BasketFloor = Deck + 2.2f, BasketBottom = 1.8f, BasketBrim = Deck + 5.4f, BasketTop = 3.4f;

    /// <summary>
    /// How far the tower reaches out from its middle at its foot (m): the coal bay under the hoist's rope, past the plinth.
    /// TownFixtures "lighthouse" stands that much in the Sim. And the coal's height in the bay.
    /// </summary>
    public const float Reach = 8.4f, Bay = 1.0f;

    /// <summary>Where the fire is: the coals' middle, in the basket (m; the Flame's foot).</summary>
    public static readonly Vector3 Fire = new(0, BasketFloor + 1.1f, 0);

    /// <summary>The shaft's radius at height y.</summary>
    static float RadiusAt(float y) => Foot + (Neck - Foot) * Math.Clamp((y - PlinthTop) / (Top - PlinthTop), 0, 1);

    public static MeshAsset Tower(Look? look)
    {
        var k = new Kit(look, 4710);
        // The plinth: eight-sided granite, a step all round, the town's ground at its foot.
        k.Use("granite_lichen", new Vector3(0.5f, 0.5f, 0.48f), 0.8f, 0.05f, tile: 3);
        k.Cylinder(Vector3.Zero, new Vector3(0, PlinthTop, 0), Plinth, 8, smooth: false);
        k.Cylinder(new Vector3(0, PlinthTop, 0), new Vector3(0, PlinthTop + 0.25f, 0), Foot + 0.5f, 12, smooth: false);
        // The shaft: dressed stone, tapering, twelve faces.
        k.Use("stone_block", new Vector3(0.55f, 0.53f, 0.5f), 0.75f, 0.05f, tile: 4);
        k.Cylinder(new Vector3(0, PlinthTop, 0), new Vector3(0, Top, 0), Foot, 12, caps: false, smooth: false, radiusB: Neck);
        // A string course every storey of the stair, darker with the soot that's come down it.
        k.Shade(0.7f);
        for (float y = 14; y < Top - 4; y += 12)
            k.Cylinder(new Vector3(0, y, 0), new Vector3(0, y + 0.45f, 0), RadiusAt(y) + 0.22f, 12, smooth: false);
        k.Shade(1 / 0.7f);
        // The slit windows, round the tower as the stair climbs: dark, and every third one lit (the keeper's lamps).
        for (int i = 0; i < 11; i++)
        {
            float y = 6 + i * (Top - 14) / 10, a = MathF.PI * 1.5f + i * 2.2f;
            var n = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var at = n * (RadiusAt(y) + 0.03f) + new Vector3(0, y, 0);
            if (i % 3 == 1)
            {
                k.Use("window_lit", Palette.LampAmber, 0, 0, tile: 1);
                k.Emissive = 1;
                k.Panel(at, n, Vector3.UnitY, 0.45f, 1.6f, Vector2.Zero, Vector2.One);
                k.Emissive = 0;
            }
            else
            {
                k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.6f, tile: 1);
                k.Panel(at, n, Vector3.UnitY, 0.45f, 1.6f, Vector2.Zero, Vector2.One);
            }
        }
        // The door at the foot, an iron-studded oak one under a lintel, and steps up the plinth to it.
        k.Use("stone_block", new Vector3(0.55f, 0.53f, 0.5f), 0.75f, 0.05f, tile: 4);
        k.Box(new Vector3(-1.3f, 0, -Plinth - 1.6f), new Vector3(1.3f, 0.7f, -Plinth + 0.3f), Kit.Faces.All);
        k.Box(new Vector3(-1.3f, 0, -Plinth - 0.8f), new Vector3(1.3f, 1.4f, -Plinth + 0.3f), Kit.Faces.All);
        k.Box(new Vector3(-1.1f, PlinthTop + 2.5f, -Foot - 0.35f), new Vector3(1.1f, PlinthTop + 2.9f, -Foot + 0.4f), Kit.Faces.All);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Shade(0.35f);
        k.Doorway(new Vector3(0, PlinthTop + 0.25f, -Foot + 0.05f), -Vector3.UnitZ, 1.2f);
        k.Shade(1 / 0.35f);
        // The gallery: corbelled out from the neck, its deck, and the iron rail round it.
        k.Use("stone_block", new Vector3(0.5f, 0.48f, 0.46f), 0.75f, 0.05f, tile: 3);
        k.Cylinder(new Vector3(0, Top - 1.6f, 0), new Vector3(0, Top, 0), Neck, 12, caps: false, smooth: false, radiusB: Gallery);
        k.Cylinder(new Vector3(0, Top, 0), new Vector3(0, Deck, 0), Gallery, 12, smooth: false);
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f, tile: 1);
        const int posts = 16;
        for (int i = 0; i < posts; i++)
        {
            float a0 = i * MathF.Tau / posts, a1 = (i + 1) * MathF.Tau / posts, r = Gallery - 0.15f;
            var p0 = new Vector3(MathF.Cos(a0) * r, Deck, MathF.Sin(a0) * r);
            var p1 = new Vector3(MathF.Cos(a1) * r, Deck, MathF.Sin(a1) * r);
            k.Rod(p0, p0 + new Vector3(0, 1.1f, 0), 0.03f);
            k.Rod(p0 + new Vector3(0, 1.1f, 0), p1 + new Vector3(0, 1.1f, 0), 0.03f);
            k.Rod(p0 + new Vector3(0, 0.55f, 0), p1 + new Vector3(0, 0.55f, 0), 0.02f);
        }
        // The basket on its pedestal: a flared iron cage of bars, banded top and bottom, a plate under the coals.
        k.Cylinder(new Vector3(0, Deck, 0), new Vector3(0, BasketFloor, 0), 1.1f, 8, smooth: false, radiusB: 0.7f);
        k.Cylinder(new Vector3(0, BasketFloor - 0.1f, 0), new Vector3(0, BasketFloor, 0), BasketBottom + 0.05f, 12, smooth: false);
        const int bars = 14;
        for (int i = 0; i < bars; i++)
        {
            float a0 = i * MathF.Tau / bars, a1 = (i + 1) * MathF.Tau / bars;
            var lo0 = new Vector3(MathF.Cos(a0) * BasketBottom, BasketFloor, MathF.Sin(a0) * BasketBottom);
            var hi0 = new Vector3(MathF.Cos(a0) * BasketTop, BasketBrim, MathF.Sin(a0) * BasketTop);
            var lo1 = new Vector3(MathF.Cos(a1) * BasketBottom, BasketFloor, MathF.Sin(a1) * BasketBottom);
            var hi1 = new Vector3(MathF.Cos(a1) * BasketTop, BasketBrim, MathF.Sin(a1) * BasketTop);
            k.Rod(lo0, hi0, 0.08f);
            k.Rod(hi0, hi1, 0.09f);
            k.Rod((lo0 + hi0) / 2, (lo1 + hi1) / 2, 0.06f);
        }
        // The coals: heaped in the basket, white-hot at the heart (they light themselves; the Flame is Effects.Beacon's).
        k.Use("ember_crack", Palette.FurnaceOrange, 0, 0, tile: 1);
        k.Emissive = 1;
        k.Cylinder(new Vector3(0, BasketFloor, 0), new Vector3(0, BasketFloor + 1.4f, 0), BasketBottom - 0.05f, 12, smooth: true,
            radiusB: (BasketBottom + BasketTop) / 2 - 0.3f, capA: false);
        k.Emissive = 0;
        // The coal hoist: a davit off the gallery on the line's side, its sheave, and the rope down the tower to the plinth
        // with a tub on it halfway (the keeper hauls a tub up at the hour).
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f, tile: 1);
        var foot = new Vector3(Gallery - 0.4f, Deck, 0);
        var head = new Vector3(Gallery + 1.4f, Deck + 3.0f, 0);
        k.Rod(foot, foot + new Vector3(0, 3.2f, 0), 0.09f);
        k.Rod(foot + new Vector3(0, 3.0f, 0), head, 0.08f);
        k.Rod(foot + new Vector3(0, 1.0f, 0), head - new Vector3(0.3f, 0.2f, 0), 0.05f);
        k.Cylinder(head + new Vector3(0, -0.25f, -0.06f), head + new Vector3(0, -0.25f, 0.06f), 0.3f, 10);
        k.Use("leather", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        var rope = head + new Vector3(0.3f, -0.25f, 0);
        k.Rod(rope, new Vector3(rope.X, Bay, 0), 0.025f);
        k.Use("wood_crate", Palette.DeepBrown, 0.85f, 0.05f, tile: 1);
        k.BoxAt(new Vector3(rope.X, Top * 0.55f, 0), new Vector3(0.4f, 0.35f, 0.4f));
        k.Use("coal", Palette.SootBlack, 0.6f, 0.2f, tile: 1);
        k.BoxAt(new Vector3(rope.X, Top * 0.55f + 0.38f, 0), new Vector3(0.34f, 0.05f, 0.34f), bottom: false);
        // The coal waiting at the foot under the rope, in a stone bay against the plinth.
        k.Use("granite_lichen", new Vector3(0.5f, 0.5f, 0.48f), 0.8f, 0.05f, tile: 3);
        k.Box(new Vector3(Plinth - 1.0f, 0, -1.2f), new Vector3(Reach, Bay + 0.2f, -1.0f), Kit.Faces.All);
        k.Box(new Vector3(Plinth - 1.0f, 0, 1.0f), new Vector3(Reach, Bay + 0.2f, 1.2f), Kit.Faces.All);
        k.Box(new Vector3(Reach - 0.2f, 0, -1.0f), new Vector3(Reach, Bay + 0.2f, 1.0f), Kit.Faces.All);
        k.Use("coal", Palette.SootBlack, 0.6f, 0.2f, tile: 1);
        k.Box(new Vector3(Plinth - 1.0f, 0, -1.0f), new Vector3(Reach - 0.2f, Bay, 1.0f), Kit.Faces.PosY);
        return k.Build("hope-tower");
    }
}
