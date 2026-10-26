using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// A stove's smoke out of its pipe on a car's roof (the crew's car, the guard van): a thin grey thread rising, the
    /// train's wind laying it back over the roof behind.
    /// </summary>
    public void StoveSmoke(MeshBuilder mesh, Vector3 top, Vector3 u, Vector3 b, float speed, double t, int seed)
    {
        if (_smoke < 0)
            return;
        for (int k = 0; k < 10; k++)
        {
            float h = Hash(k * 2.13f + seed * 3.7f), period = 4.0f + h * 1.5f;
            float age = (float)((t * (0.9 + 0.2 * h) + h * 11) % period), s = age / period;
            var p = top + u * (age * 0.55f / (1 + MathF.Abs(speed) * 0.1f)) + b * (MathF.Abs(speed) * age * 0.6f)
                + new Vector3(MathF.Sin(age * 1.3f + h * 6) * 0.1f, 0, MathF.Cos(age * 0.9f + h * 4) * 0.1f) * s;
            float a = 0.38f * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.2f + 0.05f));
            mesh.Billboard(p, 0.18f + s * 0.9f, h * 6.28f + age * 0.2f, new Vector4(new Vector3(0.22f, 0.21f, 0.2f), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
    }

    /// <summary>
    /// A hot axle box (note 331): smoke curling off the bogie, thin as it starts to squeal and thick and dark as it's about to
    /// catch (<paramref name="heat"/> 0..1), the train's wind dragging it back along the car; and the box glowing dull red
    /// from a third of the way. <paramref name="box"/> the box, <paramref name="u"/>/<paramref name="b"/> the car's up and back.
    /// </summary>
    public void HotBoxSmoke(MeshBuilder mesh, Vector3 box, Vector3 u, Vector3 b, float heat, float speed, double t, int seed)
    {
        if (_smoke < 0 || heat <= 0.12f)
            return;
        float thick = Math.Clamp((heat - 0.12f) / 0.6f, 0, 1);
        int puffs = 4 + (int)(thick * 10);
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(k * 1.71f + seed * 5.3f), period = 1.8f + h;
            float age = (float)((t * (0.9 + 0.3 * h) + h * 7) % period), s = age / period;
            var p = box + u * (age * 0.7f) + b * (MathF.Abs(speed) * age * 0.5f)
                + new Vector3(MathF.Sin(age * 2.1f + h * 6) * 0.15f, 0, MathF.Cos(age * 1.7f + h * 4) * 0.15f) * s;
            float a = (0.35f + 0.45f * thick) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.3f + 0.05f));
            float grey = 0.72f - 0.22f * thick;
            mesh.Billboard(p, 0.25f + s * (0.9f + thick * 1.1f), h * 6.28f + age * 0.3f, new Vector4(new Vector3(grey, grey * 0.97f, grey * 0.94f), a),
                _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        if (heat > 0.35f && _spark >= 0)
        {
            float glow = (heat - 0.35f) / 0.65f * (0.75f + 0.25f * MathF.Sin((float)t * 7 + seed));
            mesh.Billboard(box, 0.9f, 0, new Vector4(1.0f, 0.35f, 0.08f, 0.8f * glow), _spark, FxBlend.Additive, 0, 2);
        }
    }

    /// <summary>
    /// Note 939 (the director's playtest of 20 Oct evening, line 9: "it's got to be one of these dudes on this wheel"): a car
    /// fire its hot box started, shown where it started for as long as it burns. The box (at <paramref name="box"/>, outside
    /// the wheel) glows red-hot and smokes as it did at its worst, and flames lick up out of it against the car's side, its
    /// light on the wheels and the ground: the box that caught is the one burning.
    /// </summary>
    public void HotBoxAlight(MeshBuilder mesh, Vector3 box, Vector3 u, Vector3 b, float speed, double t, int seed)
    {
        HotBoxSmoke(mesh, box, u, b, 1, speed, t, seed);
        if (_flame < 0)
            return;
        float flicker = 0.8f + 0.2f * MathF.Sin((float)t * 11 + seed * 1.3f);
        for (int k = 0; k < 3; k++)
        {
            float h = Hash(k * 3.17f + seed * 2.3f);
            int frame = (int)((t * (12 + 4 * h) + h * 16) % 16);
            float tall = 0.35f + 0.25f * h;
            mesh.Billboard(box + u * (0.12f + tall * 0.45f) + b * ((k - 1) * 0.12f + MathF.Abs(speed) * 0.02f), tall * 0.7f, (h - 0.5f) * 0.4f,
                new Vector4(1.0f, 0.55f, 0.25f, 0.9f * flicker), _flame, FxBlend.Additive, frame, 4, stretch: 1.5f);
        }
        mesh.PointLights.Add(new PointLight(box + u * 0.25f, Palette.FurnaceOrange * 1.3f * flicker, 3.5f));
    }

    /// <summary>
    /// Note 939 (the director's playtest of 20 Oct evening, line 9: "how does this keep relighting?"): a Cinder Hound on a car's
    /// roof, seen from inside the car under it: its heat through the roof's boards at <paramref name="under"/> (the roof's
    /// underside below it, <paramref name="down"/> into the car), a dull glow and cinders sifting down out of the seams,
    /// stronger as it winds up to light the car (<paramref name="windUp"/> 0..1, its ignite's wind-up). The crew fighting
    /// its fire below see where it'll come from again.
    /// </summary>
    public void HoundThroughRoof(MeshBuilder mesh, Vector3 under, Vector3 down, float windUp, double t, int seed)
    {
        if (_spark < 0 || _flash < 0)
            return;
        float hot = 0.35f + 0.65f * Math.Clamp(windUp, 0, 1);
        float flicker = 0.75f + 0.25f * MathF.Sin((float)t * 6 + seed);
        // The boards glowing where it stands (the flash's soft round, dull red), a hotter heart in it.
        mesh.Billboard(under + down * 0.05f, 1.4f + 0.8f * hot, seed, new Vector4(0.9f, 0.22f, 0.04f, 0.45f * hot * flicker), _flash, FxBlend.Additive, 0, 2);
        mesh.Billboard(under + down * 0.08f, 0.5f + 0.4f * hot, seed + 1, new Vector4(1.0f, 0.5f, 0.15f, 0.6f * hot * flicker), _flash, FxBlend.Additive, 0, 2);
        int cinders = 4 + (int)(10 * hot);
        for (int k = 0; k < cinders; k++)
        {
            float h = Hash(k * 1.93f + seed * 4.1f), h2 = Hash(k * 4.37f + seed * 0.7f), period = 1.1f + 0.8f * h;
            float age = (float)((t * (0.8 + 0.4 * h2) + h * 5) % period), s = age / period;
            var side = new Vector3(MathF.Sin(h * 6.28f), 0, MathF.Cos(h * 6.28f)) * (0.15f + 0.45f * h2);
            var p = under + side + down * (s * (1.2f + 0.8f * h)) + new Vector3(MathF.Sin(age * 3 + h * 9), 0, 0) * 0.08f;
            mesh.Billboard(p, 0.12f + 0.06f * h, 0, new Vector4(1.0f, 0.5f + 0.3f * h2, 0.15f, (1 - s) * (0.6f + 0.4f * hot)), _spark, FxBlend.Additive, 0, 2);
        }
        mesh.PointLights.Add(new PointLight(under + down * 0.3f, Palette.FurnaceOrange * (0.35f + 1.0f * hot) * flicker, 2.5f + 1.5f * hot));
    }

    /// <summary>
    /// Note 821: a crewmate a car fire's heat reaches (<paramref name="heat"/> 0..1, the burn's own measure: Sim's
    /// CarFire.HeatOn), from their <paramref name="feet"/>: smoke curling up off their shoulders, back and sleeves, thicker and
    /// darker the hotter, and embers flicked up off them, so whoever sees them sees it burning them (the 14 Oct playtest's
    /// line 25: "I wasn't getting any feedback that I was being burned").
    /// </summary>
    public void Scorched(MeshBuilder mesh, Vector3 feet, float heat, double t, int seed)
    {
        if (_smoke < 0 || heat <= 0.01f)
            return;
        float thick = Math.Clamp(heat / 0.7f, 0, 1);
        int puffs = 5 + (int)(thick * 9);
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(k * 2.13f + seed * 3.7f), h2 = Hash(k * 5.31f + seed * 1.9f), period = 1.0f + 0.7f * h;
            float age = (float)((t * (0.9 + 0.3 * h) + h * 5) % period), s = age / period;
            // Off the shoulders and the arms (their outline, chest to shoulder height), rising clear of them and spreading.
            float side = h2 < 0.5f ? -1 : 1;
            var from = feet + new Vector3(side * (0.18f + 0.18f * Hash(k * 7.7f + seed)), 1.2f + 0.35f * h, (Hash(k * 9.1f + seed) - 0.5f) * 0.3f);
            var p = from + Vector3.UnitY * (age * (0.8f + 0.4f * thick))
                + new Vector3(MathF.Sin(age * 2.6f + h * 6) * 0.12f, 0, MathF.Cos(age * 2.1f + h * 4) * 0.12f) * s;
            float a = (0.6f + 0.3f * thick) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.25f + 0.05f));
            // The smoke sheet is dark (a third grey): tinted up pale, and warm with the fire under it, so it reads against the
            // car's dark boards.
            float grey = 3.2f - 0.8f * thick;
            mesh.Billboard(p, 0.25f + s * (0.7f + 0.6f * thick), h * 6.28f + age * 0.4f, new Vector4(grey * 1.08f, grey * 0.9f, grey * 0.76f, a),
                _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        if (_spark < 0)
            return;
        // Embers flicked up off them, round their outline (the spark sheet's dot is a fraction of its cell: these are sized for it).
        int embers = 6 + (int)(thick * 10);
        for (int k = 0; k < embers; k++)
        {
            float h = Hash(k * 3.37f + seed * 2.9f), period = 0.6f + 0.5f * h;
            float age = (float)((t * (1.1 + 0.4 * h) + h * 3) % period), s = age / period;
            float side = Hash(k * 1.9f + seed) < 0.5f ? -1 : 1;
            var p = feet + new Vector3(side * (0.22f + 0.2f * Hash(k * 6.1f + seed)), 0.7f + 1.0f * h + s * 1.0f, (Hash(k * 4.3f + seed) - 0.5f) * 0.5f)
                + new Vector3(MathF.Sin(age * 9 + h * 6), 0, MathF.Cos(age * 7 + h * 4)) * (0.08f * s);
            mesh.Billboard(p, 0.16f + 0.08f * h, MathF.PI / 2, new Vector4(2.4f, 1.0f + 0.4f * h, 0.3f, (1 - s) * (0.7f + 0.3f * thick)), _spark,
                FxBlend.Additive, k % 4, 2, stretch: 1.8f);
        }
    }

    /// <summary>
    /// A breath in the cold (GDD §26): every few seconds (quicker when they're working) a puff out of the mouth, out the
    /// way they face, spreading and rising and gone. <paramref name="amount"/> how much it shows (the night's cold).
    /// </summary>
    public void Breath(MeshBuilder mesh, Vector3 mouth, Vector3 facing, float amount, double t, int seed, bool hard)
    {
        if (_steam < 0 || amount <= 0)
            return;
        float period = hard ? 1.6f : 3.4f;
        float phase = (float)((t + seed * 0.83) % period);
        const float Out = 1.3f;
        if (phase > Out)
            return;
        for (int k = 0; k < 4; k++)
        {
            float age = phase - k * 0.07f;
            if (age < 0)
                continue;
            float s = age / Out;
            var p = mouth + facing * (0.08f + s * 0.45f) + Vector3.UnitY * (s * s * 0.25f);
            float a = amount * 0.5f * (1 - s) * MathF.Min(1, age * 8);
            mesh.Billboard(p, 0.08f + s * 0.5f, seed + k * 1.3f, new Vector4(new Vector3(0.78f, 0.8f, 0.83f), a), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
    }

    /// <summary>
    /// A damaged boiler's leaks (<see cref="DamageKit.Leaks"/>): a hard white jet hissing out of each split seam, thinning
    /// to a plume the train's own wind lays back along the boiler. <paramref name="o"/>/<paramref name="r"/>/<paramref name="u"/>/
    /// <paramref name="b"/> the engine's frame; <paramref name="pressure"/> 0..1 how hard it blows (a dead boiler doesn't).
    /// </summary>
    public void SteamLeaks(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, IEnumerable<(Vector3 At, Vector3 Out)> leaks,
        float pressure, float speed, double t, int seed)
    {
        if (_steam < 0 || pressure <= 0.02f)
            return;
        int n = 0;
        foreach (var (at, dir) in leaks)
        {
            Vector3 L(Vector3 p) => o + r * p.X + u * p.Y + b * p.Z;
            var jet = r * dir.X + u * dir.Y + b * dir.Z;
            for (int k = 0; k < 14; k++, n++)
            {
                float h = Hash(n * 1.37f + seed), period = 0.6f + h * 0.5f;
                float age = (float)((t * (1.1 + 0.2 * h) + h * 7) % period), s = age / period;
                // Out hard along the jet, slowing, rising, and laid back by the wind.
                var p = L(at) + jet * (s * 2.4f * pressure) + u * (s * s * 1.0f) + b * (MathF.Abs(speed) * age * 0.7f);
                float a = (1 - s) * 0.85f * MathF.Min(1, pressure * 1.3f);
                mesh.Billboard(p, 0.16f + s * 1.5f, h * 6.28f, new Vector4(new Vector3(0.82f, 0.84f, 0.86f), a), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
            }
        }
    }

    /// <summary>
    /// A car's fire seen from outside (GDD App. C.5): <see cref="Burn"/> how big it is this frame (0 out),
    /// <see cref="Alight"/> flaming, <see cref="Since"/> seconds since it went out, <see cref="Peak"/> the worst it got,
    /// <see cref="Hot"/> its hottest cell this frame (0 out; note 1041).
    /// </summary>
    public readonly record struct Burning(float Burn, bool Alight, float Since, float Peak, float Hot = 0)
    {
        /// <summary>How charred the car is: as bad as the fire got, the paint going first.</summary>
        public float Char => Math.Clamp(Peak * 1.15f - 0.1f, 0, 1);

        /// <summary>How much smoke it gives: the fire's own while it burns; once out, a smoulder dying away over minutes.</summary>
        public float Smoke => Burn > 0 ? 0.35f + 0.65f * Burn : Peak * 0.45f * MathF.Exp(-Since / SmoulderSeconds);

        public const float SmoulderSeconds = 150;
    }

    /// <summary>
    /// The outside of a car on fire, or gutted by one (GDD App. C.5: a fire that's jumped the couplings is seen from the
    /// roofs before anyone's inside). Smoke forces out of every seam: along the eaves under the roof's overhang, round
    /// the side doors, out of the end doors; it rises off the car and the train's own wind lays it back along the roofs
    /// behind, so a train with a fire aboard trails a dirty banner. Alight, the doors' cracks glow and the smoke's lit
    /// orange from beneath where it leaves them. Out, it smoulders: thinner, paler wisps out of the eaves, dying away over
    /// minutes. <paramref name="o"/>/<paramref name="r"/>/<paramref name="u"/>/<paramref name="b"/> the car's frame (its
    /// origin on the rail between the bogies, b toward the rear), <paramref name="speed"/> the train's (m/s).
    /// </summary>
    public void CarSmoke(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, Sim.Train.CarShape shape, Burning fire, float speed, double t, int seed)
    {
        float amount = fire.Smoke;
        if (amount < 0.02f && fire.Hot <= 0 || _smoke < 0)
            return;
        float half = (float)shape.HalfLength, wide = (float)shape.HalfWidth, roof = (float)shape.RoofHeight;
        Vector3 L(float x, float y, float z) => o + r * x + u * y + b * z;
        int puffs = (int)(24 + 64 * amount);
        // The wind of the train's own going lays it back (the air moves past the car rearward at its speed), less as it
        // slows in the open air; standing, it just rises and spreads.
        float wind = MathF.Abs(speed);
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(k * 1.73f + seed * 9.1f), h2 = Hash(k * 3.17f + seed * 2.3f + 0.5f), h3 = Hash(k * 5.31f + seed);
            float period = 2.6f + h * 2.2f;
            float age = (float)((t * (0.85 + 0.3 * h2) + h3 * 23) % period);
            float s = age / period;
            // Where it's leaving the car: mostly the eaves on either side, then the side doors' cracks, then the end doors.
            float side = h2 < 0.5f ? -1 : 1;
            Vector3 from = h < 0.6f ? new Vector3(side * (wide - 0.05f), roof - 0.15f, (h3 - 0.5f) * 2 * (half - 0.6f))
                : h < 0.85f ? new Vector3(side * wide, 1.2f + h3 * 1.6f, (h3 - 0.5f) * 1.4f)
                : new Vector3((h3 - 0.5f) * 1.0f, 1.4f + h2 * 1.4f, (h2 < 0.5f ? -1 : 1) * half);
            float rise = age * (fire.Alight ? 0.95f : 0.55f) * (1 + h * 0.4f);
            float out_ = MathF.Min(age, 0.6f) * 0.5f;
            float laid = wind * age * MathF.Max(0.25f, 1 - s * 0.5f);
            // Laid back by the wind it hugs the roofs; standing, it climbs.
            float climb = rise / (1 + wind * 0.08f);
            var at = L(from.X + MathF.Sign(from.X) * out_, from.Y + climb, from.Z + laid);
            float size = (0.55f + s * (2.2f + 2.0f * amount)) * (1 + wind * 0.02f * s);
            float a = MathF.Min(1, (fire.Burn > 0 ? 0.32f + 0.3f * amount : 0.2f + amount * 0.45f) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.15f + 0.08f)));
            var grey = fire.Burn > 0 ? new Vector3(0.075f, 0.065f, 0.058f) : new Vector3(0.2f, 0.195f, 0.19f);
            if (fire.Alight)
                grey += Palette.FurnaceOrange * 0.28f * fire.Burn * MathF.Max(0, 1 - s * 2.5f);
            mesh.Billboard(at, size, h * 6.28f + age * 0.3f, new Vector4(grey * (0.85f + 0.3f * h3), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        float hot = fire.Hot;
        if (hot <= 0)
            return;
        // Note 1041 (the visual tell audit, note 1037: 15 s in, a fire on the farthest car showed nothing outside at 60 m):
        // from its first cell alight, a column of smoke out of the roof's seams, rising well clear of the train and laid back
        // by its wind, lit from under where it leaves the car; and the doors' cracks and the roof's seams glowing, and the light
        // through them on the ground and the next car: dull red while it smoulders, orange and brighter as it takes hold. The
        // sim's intensity is the mean of the car's cells (one cell alight is a sixteenth of it), so the hottest scales it.
        var fo = look.Tuning.FireOutside;
        int column = (int)(fo.Column + fo.ColumnPerHeat * hot);
        for (int k = 0; k < column; k++)
        {
            float h = Hash(k * 2.31f + seed * 4.7f), h2 = Hash(k * 6.13f + seed * 1.3f + 0.25f), h3 = Hash(k * 3.71f + seed * 8.9f);
            float period = fo.ColumnSeconds * (0.8f + 0.4f * h);
            float age = (float)((t * (0.9 + 0.2 * h2) + h3 * 31) % period), s = age / period;
            var from = new Vector3((h2 - 0.5f) * wide, roof + 0.1f, (h - 0.5f) * 1.6f * MathF.Max(0.5f, half - 0.8f));
            float climb = age * fo.ColumnRise * (0.8f + 0.4f * hot) / (1 + wind * 0.08f);
            var at = L(from.X + MathF.Sin(age * 0.7f + h * 6) * 0.4f * s, from.Y + climb, from.Z + wind * age * 0.8f);
            float size = (fo.ColumnSize + fo.ColumnGrow * s) * (0.75f + 0.5f * hot);
            float a = MathF.Min(1, (fo.ColumnAlpha + fo.ColumnAlphaPerHeat * hot) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.1f + 0.06f)));
            var grey = fo.Grey + Palette.FurnaceOrange * fo.ColumnLit * hot * MathF.Max(0, 1 - s * 3);
            mesh.Billboard(at, size, h * 6.28f + age * 0.25f, new Vector4(grey * (0.85f + 0.3f * h3), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        float flicker = 0.75f + 0.25f * MathF.Sin((float)t * 13.1f + seed) * MathF.Sin((float)t * 7.3f);
        // Smouldering it's the embers' red; alight, the flames' orange.
        var colour = fire.Alight ? Palette.FurnaceOrange : Palette.FurnaceOrange * new Vector3(1, 0.5f, 0.35f);
        float glow = (fo.Seams + fo.SeamsPerHeat * hot) * flicker;
        // The fire's light on its own smoke, over the roof where it leaves the car: what's seen of it through the fog, far off.
        // (By the root of the heat: there from the first, and not a fireball once it's well alight.)
        mesh.Billboard(L(0, roof + 0.6f + 0.6f * hot, 0), fo.UnderGlowSize * (0.7f + 0.4f * hot), 0,
            new Vector4(colour * fo.UnderGlow * MathF.Sqrt(hot) * flicker, 1), -1, FxBlend.Additive, stretch: 0.6f);
        foreach (float sd in (ReadOnlySpan<float>)[-1, 1])
        {
            // The side door's crack, floor to lintel, and the eave's seam along the roof, a glow at a time down it.
            mesh.Billboard(L(sd * (wide + 0.03f), 1.6f, 0), 1.1f + hot, 0, new Vector4(colour * glow, 1), -1, FxBlend.Additive, stretch: 0.35f);
            for (int k = 0; k < 6; k++)
                mesh.Billboard(L(sd * (wide + 0.03f), roof - 0.12f, (k / 5f - 0.5f) * 2 * (half - 0.5f)), 0.9f + 0.5f * hot, 0,
                    new Vector4(colour * glow * 0.45f, 1), -1, FxBlend.Additive, stretch: 0.3f);
            mesh.PointLights.Add(new PointLight(L(sd * (wide + 0.4f), 1.4f, 0), colour * (fo.Light + fo.LightPerHeat * hot) * flicker,
                fo.LightRange + fo.LightRangePerHeat * hot));
        }
    }

    /// <summary>
    /// A car fire (GDD App. C.5, the in-car incident), at the face of the burning load: <paramref name="o"/> on the floor,
    /// <paramref name="r"/> into the load, <paramref name="u"/> up, <paramref name="b"/> along the car. Smouldering, smoke
    /// seeps out of the stack and rises to the roof, where it pools and rolls along under it (a closed car fills from the
    /// top down: GDD §26, the inside is where you're safe until it isn't). Alight, the flames lick up the face of the load,
    /// taller the further it's gone, a deeper rank of them back in the stack; cinders spiral up out of them; the light
    /// flickers on the walls; and the smoke thickens and blackens. Worked out from the time like the train's (no state).
    /// </summary>
    /// <param name="burn">How far it's gone, 0..1 (the sim's intensity).</param>
    /// <param name="ceiling">How high the roof is over the floor (m), where the smoke pools.</param>
    /// <summary>
    /// A smouldering load's smoke: pale, so it shows grey against lamp-lit planking. The audit's playthrough found the
    /// darker grey it was the same brightness as the walls, and the telegraph unreadable.
    /// </summary>
    static readonly Vector3 SmoulderGrey = new(1.05f, 1.0f, 0.95f);

    /// <param name="spread">How far it's got to jumping the coupling (App. A.5 "grows, jumps couplings"), 0..1: the sim's blaze
    /// over enemies.json carFire.spreadSeconds. The flames creep out along the floor and up the walls toward both ends of the
    /// car as it builds, so the crew can see the next car's about to go.</param>
    public void CarFire(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, bool alight, double burn, double t, float ceiling = 2.0f,
        float spread = 0)
    {
        // Note 866 (the director, 19 Oct: "its still going out once the whole car is done only so I cant work down the car for
        // the fire out"): a fire on its cells is drawn all on its cells, its flames (FireCell) and its smoke, glow, cinders and
        // light a stretch of the car at a time (FireStretch), so what the spray puts out goes dark where it lands. Drawn here
        // at the heart of it, they burnt on to the fire's last cell.
        if (CellFlames)
            return;
        Vector3 L(float x, float y, float z) => o + r * x + u * y + b * z;
        float burnF = (float)Math.Clamp(burn, 0.05, 1);

        // Smoke: out of the load at chest height, up to the roof, then rolling out along under it each way.
        int puffs = alight ? 30 : 16;
        float roof = ceiling - 0.35f;
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(k * 1.37f + 0.3f), h2 = Hash(k * 2.71f + 1.9f);
            float period = 3.2f + h * 2.4f;
            float age = (float)((t * (0.9 + 0.3 * h) + h2 * 17) % period);
            float s = age / period;
            float climb = 0.55f + age * (alight ? 1.5f : 0.8f);
            float y = MathF.Min(climb, roof), rolled = MathF.Max(0, climb - roof);
            float z = (h - 0.5f) * 2.2f + (h2 < 0.5f ? -1 : 1) * rolled * 1.7f;
            float x = -0.1f - s * 0.7f;
            float size = alight ? 0.45f + s * 1.9f : 0.6f + s * 1.6f;
            float a = MathF.Min(1, (alight ? 0.85f + 0.4f * burnF : 1.3f) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.3f)));
            // Lit from under by the flames while it's low and near them; black once it's rolled away along the roof.
            var grey = alight ? new Vector3(0.07f, 0.06f, 0.055f) + Palette.FurnaceOrange * 0.22f * burnF * MathF.Max(0, 1 - s * 1.6f)
                : SmoulderGrey;
            mesh.Billboard(L(x, y, z), size, h * 6.28f + age * 0.35f, new Vector4(grey * (0.85f + 0.3f * h2), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        // The pool under the roof: a ceiling of smoke out each way along the car, lower and thicker the worse it's got,
        // drifting slowly; what you crouch under.
        int pool = alight ? 30 : 12;
        float reach = (alight ? 2.2f + 3.5f * burnF + 0.8f * spread : 2.2f), depth = alight ? 0.25f + 0.45f * burnF : 0.3f;
        for (int k = 0; k < pool; k++)
        {
            float h = Hash(k * 4.13f + 7.7f), h2 = Hash(k * 1.91f + 3.3f);
            float z = (k / (pool - 1f) - 0.5f) * 2 * reach + MathF.Sin((float)t * 0.15f + h * 6.28f) * 0.4f;
            float x = -0.5f + (h2 - 0.5f) * 0.9f;
            float y = ceiling - 0.2f - depth * h;
            float a = MathF.Min(1, (alight ? 0.8f + 0.5f * burnF : 1.1f) * (1.15f - MathF.Abs(z) / reach));
            // Thick and low over the fire, lit by it from under; thinner and darker away along the car.
            float near = MathF.Max(0, 1 - MathF.Abs(z) / 2.5f);
            var grey = alight ? new Vector3(0.09f, 0.075f, 0.065f) + Palette.FurnaceOrange * 0.3f * burnF * near : SmoulderGrey * 0.9f;
            mesh.Billboard(L(x, y, z), 1.3f + h * 0.9f, h * 6.28f + (float)t * 0.05f, new Vector4(grey, a), _smoke, FxBlend.Alpha, 6 + (int)(h2 * 8), 4,
                stretch: 0.6f);
        }
        if (!alight)
        {
            // The seat of it (the TELEGRAPH, App. C.5: smoke before flame): a red glow down in the load's cracks, breathing,
            // so a smouldering car reads as one from the aisle whatever the lamp's doing to the smoke.
            for (int k = 0; k < 4; k++)
            {
                float h = Hash(k * 3.7f + 0.9f);
                float breathe = 0.55f + 0.45f * MathF.Sin((float)t * (1.1f + h) + k * 1.9f);
                mesh.Billboard(L(-0.05f, 0.25f + 0.35f * h, (h - 0.5f) * 1.2f), 0.35f + 0.25f * h, 0, new Vector4(Palette.FurnaceOrange * 0.55f * breathe, 1), -1,
                    FxBlend.Additive);
            }
            mesh.PointLights.Add(new PointLight(L(-0.2f, 0.5f, 0), Palette.FurnaceOrange * 0.35f, 2.2f));
            return;
        }

        // Flames: a front rank on the face of the load and a deeper one in it, each tongue on its own beat through the
        // flipbook (20 frames a second). Their feet are where the load burns, not on one line: some down at the floor,
        // some up on the crates' tops and in the gaps between, in and out from the face. (On one line, side by side, their
        // roots joined into a straight bright edge: a row of cards.)
        int tongues = 4 + (int)(9 * burnF);
        for (int rank = 0; rank < (CellFlames ? 0 : 2); rank++)
            for (int i = 0; i < tongues; i++)
            {
                float h = Hash(i * 3.17f + rank * 9.1f), h2 = Hash(i * 7.31f + rank * 2.3f + 0.5f);
                float along = (i - (tongues - 1) / 2f) * 0.3f + (h - 0.5f) * 0.16f;
                float tall = (0.55f + 1.5f * burnF) * (0.7f + 0.45f * h) * (rank == 0 ? 1 : 1.2f);
                float width = tall * 0.62f;
                float foot = h2 * h2 * (0.35f + 0.4f * rank);
                float inset = rank * 0.3f - 0.14f + (h2 - 0.5f) * 0.22f;
                int frame = (int)((t * 20 + i * 5.3 + rank * 7) % 16);
                float glow = (rank == 0 ? 1f : 0.6f) * (0.85f + 0.3f * h);
                // The front rank stands just off the face (else the crates cut it in half); the deeper one shows over the stack.
                mesh.Billboard(L(inset, foot + tall * 0.46f, along), width, (h - 0.5f) * 0.3f, new Vector4(0.85f, 0.55f, 0.32f, glow), _flame, FxBlend.Additive, frame, 4,
                    stretch: tall / width);
            }
        // The creep (the spread): flames out along the floor and licking up the walls toward the car's ends, further the
        // nearer it is to jumping, lower and patchier at the front of it, each tongue on its own beat.
        if (spread > 0 && !CellFlames)
        {
            float front = 1.2f + 3.6f * Math.Clamp(spread, 0, 1);
            int creep = (int)(4 + 22 * Math.Clamp(spread, 0, 1));
            for (int i = 0; i < creep; i++)
            {
                float h = Hash(i * 5.13f + 0.77f), h2 = Hash(i * 1.37f + 4.1f);
                float side = i % 2 == 0 ? 1 : -1;
                float along = side * (0.9f + (front - 0.9f) * h);
                float lead = 1 - MathF.Abs(along) / front;
                float tall = (0.3f + 0.9f * lead) * (0.6f + 0.6f * h) * (0.6f + 0.6f * burnF);
                // Along the floor in the aisle, off the faces of the crates (which stand further out than the burning one).
                float x = -0.55f - 0.6f * h2;
                int frame = (int)((t * 19 + i * 3.3) % 16);
                mesh.Billboard(L(x, tall * 0.46f, along), tall * 0.62f, (h - 0.5f) * 0.3f, new Vector4(0.85f, 0.55f, 0.32f, 0.55f + 0.45f * lead),
                    _flame, FxBlend.Additive, frame, 4, stretch: 1.6f);
            }
            mesh.Billboard(L(-0.1f, 0.3f, 0), 1.2f + 2.4f * front, 0, new Vector4(Palette.FurnaceOrange * 0.18f * spread, 1), -1, FxBlend.Additive, stretch: 0.25f);
        }
        // The heat: a broad, soft glow along the base of it.
        mesh.Billboard(L(-0.2f, 0.45f, 0), 1.4f + 2.2f * burnF, 0, new Vector4(Palette.FurnaceOrange * 0.35f, 1), -1, FxBlend.Additive, stretch: 0.55f);
        // Cinders: up out of the flames in slow spirals, going out before the roof.
        int cinders = 8 + (int)(14 * burnF);
        for (int k = 0; k < cinders; k++)
        {
            float h = Hash(k * 5.77f + 2.2f);
            float period = 1.4f + h * 1.6f;
            float age = (float)((t + h * 9) % period);
            float s = age / period;
            float a0 = h * 6.28f + age * 2.5f;
            var p = L(-0.05f + MathF.Cos(a0) * 0.15f * s, 0.3f + age * (ceiling - 0.4f) / period, (h - 0.5f) * tongues * 0.3f + MathF.Sin(a0) * 0.2f * s);
            mesh.Billboard(p, 0.05f + 0.04f * h, 0, new Vector4(1.0f, 0.5f, 0.15f, (1 - s) * 0.9f), _spark, FxBlend.Additive, 0, 2);
        }
        // Its light: two sources out in the aisle, each flickering on its own (never strobing: summed slow sines).
        for (int k = 0; k < 2; k++)
        {
            float flicker = 0.78f + 0.12f * MathF.Sin((float)t * (7.3f + k * 2.1f)) + 0.1f * MathF.Sin((float)t * (17.1f + k * 3.7f) + k);
            mesh.PointLights.Add(new PointLight(L(-0.45f, 0.7f + 0.3f * k, (k - 0.5f) * 1.2f), Palette.FurnaceOrange * (1.2f + 2.2f * burnF) * flicker,
                3.5f + 5f * burnF));
        }
    }

    /// <summary>
    /// Whether a car fire is drawn on its cells (<see cref="FireCell"/>, note 267; <see cref="FireStretch"/>, note 866), so
    /// <see cref="CarFire"/> draws nothing at the heart of it. Set by the scene each frame.
    /// </summary>
    public bool CellFlames { get; set; }

    /// <summary>
    /// One cell of a car fire's grid alight (GDD App. F.1, "Fire is a grid"; note 267), camera-relative: tongues off the floor
    /// standing up, up a wall licking up it, and on the roof rolling along under it, flat; a cell short of alight (smoulder)
    /// is a red glow in the boards. <paramref name="along"/> and <paramref name="across"/> span the cell's patch (half-sizes),
    /// <paramref name="normal"/> faces into the car.
    /// </summary>
    public void FireCell(MeshBuilder mesh, Vector3 at, Vector3 along, Vector3 across, Vector3 normal, Sim.Enemies.FireFace face, float heat, bool alight,
        double t, int seed)
    {
        if (heat <= 0.02f)
            return;
        var up = Vector3.UnitY;
        if (!alight)
        {
            float breathe = 0.55f + 0.45f * MathF.Sin((float)t * 1.3f + seed);
            mesh.Billboard(at + normal * 0.05f, 0.5f + 0.6f * heat, 0, new Vector4(Palette.FurnaceOrange * 0.5f * heat * breathe, 1), -1, FxBlend.Additive, stretch: 0.5f);
            return;
        }
        int tongues = 1 + (int)(3.99f * heat);
        for (int i = 0; i < tongues; i++)
        {
            float h = Hash(seed * 1.73f + i * 3.17f), h2 = Hash(seed * 2.91f + i * 7.31f + 0.5f);
            // On a wall, rooted in the lower part of its patch and licking up over the rest: from the boards, not hung on them.
            bool wall = face is Sim.Enemies.FireFace.Left or Sim.Enemies.FireFace.Right;
            var foot = at + along * (h * 2 - 1) * 0.8f + across * (wall ? h2 * 0.9f - 0.95f : (h2 * 2 - 1) * 0.8f) + normal * 0.08f;
            int frame = (int)((t * 20 + seed * 1.7 + i * 5.3) % 16);
            float glow = 0.75f + 0.35f * h;
            if (face == Sim.Enemies.FireFace.Ceiling)
            {
                // Rolling along under the roof: wide, flat, hanging down from it.
                float wide = 0.6f + 0.9f * heat;
                mesh.Billboard(foot + normal * (0.1f + 0.15f * heat), wide, (h - 0.5f) * 0.6f, new Vector4(0.85f, 0.5f, 0.3f, glow * 0.8f), _flame, FxBlend.Additive, frame, 4,
                    stretch: 0.45f);
                continue;
            }
            // Off the floor and up the walls, standing up; on a wall, licking up it from where it's caught.
            float tall = (face == Sim.Enemies.FireFace.Floor ? 0.35f + 1.2f * heat : 0.3f + 0.9f * heat) * (0.75f + 0.4f * h);
            float width = tall * 0.6f;
            mesh.Billboard(foot + up * (tall * 0.46f), width, (h - 0.5f) * 0.3f, new Vector4(0.85f, 0.55f, 0.32f, glow), _flame, FxBlend.Additive, frame, 4,
                stretch: tall / width);
        }
        // The cell's heat on its boards: a broad soft glow over its patch.
        mesh.Billboard(at + normal * 0.06f, 1.0f + 0.8f * heat, 0, new Vector4(Palette.FurnaceOrange * 0.22f * heat, 1), -1, FxBlend.Additive, stretch: 0.6f);
    }

    /// <summary>
    /// One stretch of a burning car (note 866): its floor, walls and roof at one place along it, the cells the spray works
    /// together (note 662), drawn with its own smoke, glow, cinders and light, by its hottest cell's <paramref name="heat"/>;
    /// out, nothing. So the crew put a fire out down the car stretch by stretch, and each goes dark as it's put out.
    /// Camera-relative: <paramref name="floor"/> the middle of its floor, <paramref name="along"/> half its length along the
    /// car, <paramref name="across"/> half the car's width; <paramref name="ceiling"/> the roof's height over the floor (m).
    /// </summary>
    public void FireStretch(MeshBuilder mesh, Vector3 floor, Vector3 along, Vector3 across, float ceiling, float heat, bool alight, double t, int seed)
    {
        if (heat <= 0.02f)
            return;
        var up = Vector3.UnitY;
        float h0 = Math.Clamp(heat, 0, 1), roof = ceiling - 0.35f;
        float length = along.Length();
        // Smoke: up off the boards to the roof, then rolling a little way along under it.
        int puffs = alight ? 8 : 4;
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(seed * 0.71f + k * 1.37f + 0.3f), h2 = Hash(seed * 1.13f + k * 2.71f + 1.9f);
            float period = 3.2f + h * 2.4f;
            float age = (float)((t * (0.9 + 0.3 * h) + h2 * 17) % period);
            float s = age / period;
            float climb = 0.55f + age * (alight ? 1.5f : 0.8f);
            float y = MathF.Min(climb, roof), rolled = MathF.Max(0, climb - roof);
            float z = (h - 0.5f) * 1.4f + (h2 < 0.5f ? -1 : 1) * MathF.Min(rolled * 1.2f, 0.4f);
            var p = floor + along * z + across * ((h2 - 0.5f) * 0.6f) + up * y;
            float size = alight ? 0.45f + s * (0.9f + 1.0f * h0) : 0.6f + s * 1.2f;
            float a = MathF.Min(1, (alight ? 0.85f + 0.4f * h0 : 1.3f * MathF.Max(0.4f, h0 * 3)) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.3f)));
            var grey = alight ? new Vector3(0.07f, 0.06f, 0.055f) + Palette.FurnaceOrange * 0.22f * h0 * MathF.Max(0, 1 - s * 1.6f) : SmoulderGrey;
            mesh.Billboard(p, size, h * 6.28f + age * 0.35f, new Vector4(grey * (0.85f + 0.3f * h2), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        if (!alight)
        {
            // Smouldering: a small red light in the boards, short of flames (the TELEGRAPH, App. C.5: smoke before flame).
            mesh.PointLights.Add(new PointLight(floor + up * 0.5f, Palette.FurnaceOrange * 0.35f * MathF.Max(0.3f, h0 * 3), 2.2f));
            return;
        }
        // The pool under the roof over it: what you crouch under, there while it burns and gone with it.
        int pool = 6;
        float depth = 0.25f + 0.45f * h0;
        for (int k = 0; k < pool; k++)
        {
            float h = Hash(seed * 0.37f + k * 4.13f + 7.7f), h2 = Hash(seed * 0.53f + k * 1.91f + 3.3f);
            float z = (k / (pool - 1f) - 0.5f) * 2 * 0.9f + MathF.Sin((float)t * 0.15f + h * 6.28f) * 0.1f;
            var p = floor + along * z + across * ((h2 - 0.5f) * 0.9f) + up * (ceiling - 0.2f - depth * h);
            var grey = new Vector3(0.09f, 0.075f, 0.065f) + Palette.FurnaceOrange * 0.3f * h0;
            mesh.Billboard(p, 1.1f + h * 0.7f, h * 6.28f + (float)t * 0.05f, new Vector4(grey, 0.8f + 0.4f * h0), _smoke, FxBlend.Alpha, 6 + (int)(h2 * 8), 4,
                stretch: 0.6f);
        }
        // The heat: a soft glow along its floor.
        mesh.Billboard(floor + up * 0.45f, MathF.Min(length * 2, 1.0f + 1.6f * h0), 0, new Vector4(Palette.FurnaceOrange * 0.3f, 1), -1, FxBlend.Additive, stretch: 0.55f);
        // Cinders: up out of it in slow spirals, going out before the roof.
        int cinders = 2 + (int)(5 * h0);
        for (int k = 0; k < cinders; k++)
        {
            float h = Hash(seed * 0.91f + k * 5.77f + 2.2f);
            float period = 1.4f + h * 1.6f;
            float age = (float)((t + h * 9) % period);
            float s = age / period;
            float a0 = h * 6.28f + age * 2.5f;
            var p = floor + along * ((h - 0.5f) * 1.6f) + across * (MathF.Cos(a0) * 0.15f * s) + up * (0.3f + age * (ceiling - 0.4f) / period);
            mesh.Billboard(p, 0.05f + 0.04f * h, 0, new Vector4(1.0f, 0.5f, 0.15f, (1 - s) * 0.9f), _spark, FxBlend.Additive, 0, 2);
        }
        // Its light, out of the flames there, flickering on its own (never strobing: summed slow sines), short enough that
        // the stretch beside it put out goes dark.
        float flicker = 0.78f + 0.12f * MathF.Sin((float)t * (7.3f + (seed % 5) * 0.7f)) + 0.1f * MathF.Sin((float)t * (17.1f + (seed % 3) * 1.9f) + seed);
        mesh.PointLights.Add(new PointLight(floor + up * 0.8f, Palette.FurnaceOrange * (0.9f + 1.7f * h0) * flicker, 2.5f + 3f * h0));
    }

    /// <summary>
    /// A house alight (ARCHITECTURE §8 note 608): one a creature brought in through a town's gate set burning, the night it
    /// was fought in the yard. Flames out of its front, two rows up the wall, and standing along its roof; a thick column of
    /// smoke off it; its light thrown out over the street. Camera-relative: <paramref name="front"/> the middle of its front
    /// wall on the ground, <paramref name="along"/> across that front, <paramref name="into"/> back into the house.
    /// </summary>
    public void HouseFire(MeshBuilder mesh, Vector3 front, Vector3 along, Vector3 into, float width, float depth, float height, double t, int seed)
    {
        var up = Vector3.UnitY;
        // Tongues out of its windows and door, two storeys of them, licking up the front: a storey high, not a stove's.
        int across = Math.Max(2, (int)(width / 2.0f));
        for (int i = 0; i < across; i++)
            for (int row = 0; row < 2; row++)
            {
                float h = Hash(seed * 1.31f + i * 3.17f + row * 7.7f), h2 = Hash(seed * 2.9f + i * 5.3f + row);
                float u = ((i + 0.5f) / across - 0.5f) * width * 0.9f;
                float tall = 2.4f + 2.0f * h + 0.5f * MathF.Sin((float)t * (5 + 3 * h) + h2 * 6.28f);
                var foot = front + along * u + up * (0.8f + row * 3.0f) - into * 0.6f;
                int frame = (int)((t * 18 + h * 16 + i * 5.3) % 16);
                mesh.Billboard(foot + up * (tall * 0.45f), tall * 0.7f, (h - 0.5f) * 0.3f, new Vector4(1.1f, 0.62f, 0.3f, 1f), _flame, FxBlend.Additive, frame, 4,
                    stretch: 1.5f);
            }
        // Out of the roof: a sheet of it along the ridge, three to six metres up.
        var ridge = front + into * (depth / 2) + up * height;
        int crown = Math.Max(3, (int)(width / 1.6f));
        for (int i = 0; i < crown; i++)
        {
            float h = Hash(seed * 4.1f + i * 2.37f);
            float tall = 3.0f + 3.0f * h + 0.8f * MathF.Sin((float)t * (4 + 2 * h) + i);
            var foot = ridge + along * (((i + 0.5f) / crown - 0.5f) * width) + into * ((h - 0.5f) * depth * 0.5f);
            int frame = (int)((t * 16 + h * 16 + i * 3.1) % 16);
            mesh.Billboard(foot + up * (tall * 0.42f), tall * 0.6f, (h - 0.5f) * 0.25f, new Vector4(0.95f, 0.6f, 0.32f, 1f), _flame, FxBlend.Additive, frame, 4,
                stretch: 1.6f);
        }
        // The column of smoke off it: thick, dark, lit from under, climbing high over the town.
        if (_smoke >= 0)
        {
            const float life = 12f, rate = 4f;
            double emitted = Math.Floor(t * rate);
            var wind = new Vector3(0.6f, 0, 0.35f);
            for (int k = 0; k < (int)(life * rate); k++)
            {
                double index = emitted - k;
                float age = (float)(t - index / rate);
                if (age < 0 || age > life)
                    continue;
                float h = Hash((float)(index * 0.618 + seed * 1.7)), a = age / life;
                var at = ridge + up * (2.5f + 2.2f * age) + wind * age * (0.7f + 0.6f * h)
                    + along * ((h - 0.5f) * width * 0.6f) + into * ((Hash((float)index + seed) - 0.5f) * depth * 0.5f);
                float size = 3.0f + 9f * MathF.Sqrt(a);
                float alpha = 0.75f * MathF.Pow(1 - a, 1.2f) * MathF.Min(1, age * 2);
                // Under-lit by the fire close to it, sooty above.
                var tint = Vector3.Lerp(new Vector3(0.42f, 0.24f, 0.13f), new Vector3(0.09f, 0.085f, 0.08f), MathF.Min(1, a * 3));
                mesh.Billboard(at, size, h * 6.28f + age * 0.1f, new Vector4(tint, alpha), _smoke, FxBlend.Alpha, (int)(a * 15.99f), 4);
            }
        }
        // Cinders up off it, and the glow on the street.
        for (int k = 0; k < 14; k++)
        {
            float h = Hash(seed * 0.7f + k * 1.93f), period = 2.5f + 2 * h;
            float age = (float)((t + h * 9) % period);
            var spark = ridge + up * (age * 3.5f) + along * ((h - 0.5f) * width + MathF.Sin(age * 3 + k) * 0.8f) + into * ((Hash(k + seed) - 0.5f) * depth);
            mesh.Billboard(spark, 0.12f, 0, new Vector4(Palette.FurnaceOrange * (1.6f * (1 - age / period)), 1), -1, FxBlend.Additive);
        }
        float flicker = 0.85f + 0.15f * MathF.Sin((float)t * 9.1f + seed);
        // Out on the street, so the light falls on the ground and the houses opposite, not flat on its own front.
        mesh.PointLights.Add(new PointLight(front - into * 7 + up * 2, Palette.FurnaceOrange * (1.8f * flicker), 22));
        mesh.PointLights.Add(new PointLight(ridge + up * 4, Palette.FurnaceOrange * (2.0f * flicker), 30));
    }

    /// <summary>
    /// A Hope Tower's Flame of Hope (queue #547, note 819; the director's Cultures tab: "bright burning flames above ... for
    /// the world to find in the darkness"): a coal fire in an iron basket eighty metres up, kept all night. Tongues standing
    /// out of the basket, the tallest three men high, leaning with the wind; a white-hot heart; cinders going up off it and
    /// away; a column of smoke lit orange from under, leaning off with the wind; its light thrown over the gallery and the
    /// roofs below. Camera-relative: <paramref name="fire"/> the coals' middle, <paramref name="radius"/> the basket's brim.
    /// <paramref name="near"/> 0..1 how much of it is worth drawing (1 within a few hundred metres; far off the tongues and
    /// the heart are all that shows, and the town's glow carries it: WorldArt.Glow).
    /// </summary>
    public void Beacon(MeshBuilder mesh, Vector3 fire, float radius, double t, int seed, float near = 1)
    {
        var up = Vector3.UnitY;
        var wind = new Vector3(0.5f, 0, 0.3f);
        // (Drawn for a basket of 2.6 m; a bigger one's fire is bigger.)
        float sc = radius / 2.6f;
        float flicker = 0.85f + 0.1f * MathF.Sin((float)t * 7.3f + seed) + 0.05f * MathF.Sin((float)t * 17.1f);
        // The heart: white-hot in the basket, and a broad glow round it that the fog takes up.
        mesh.Billboard(fire + up * 0.8f, radius * 2.4f, 0, new Vector4(new Vector3(1.6f, 1.15f, 0.7f) * flicker, 1), -1, FxBlend.Additive);
        mesh.Billboard(fire + up * 2.5f, radius * 7f, 0, new Vector4(Palette.FurnaceOrange * (0.55f * flicker), 1), -1, FxBlend.Additive);
        // From far off, a point of fire that stays a point (never under 1.3% of the way to it): what a crew sees of it
        // from down the line, over the town's own glow.
        float far = fire.Length() * 0.013f;
        if (far > radius * 1.5f)
            mesh.Billboard(fire + up * 1.5f, far, 0, new Vector4(new Vector3(1.8f, 1.25f, 0.75f) * flicker, 1), -1, FxBlend.Additive);
        // The tongues: a ring of them round the brim leaning in, and the tall ones out of the middle, all leaning off downwind.
        int tongues = near > 0.3f ? 14 : 5;
        for (int i = 0; i < tongues; i++)
        {
            float h = Hash(seed * 1.7f + i * 2.93f), h2 = Hash(seed * 0.9f + i * 4.1f);
            bool middle = i < 5;
            float a = i * 2.4f + h * 0.8f, r = middle ? radius * 0.35f * h : radius * (0.55f + 0.3f * h);
            float tall = sc * ((middle ? 6.5f : 3.6f) + 2.5f * h + 1.2f * MathF.Sin((float)t * (4.5f + 3 * h) + h2 * 6.28f));
            var foot = fire + new Vector3(MathF.Cos(a) * r, 0.2f, MathF.Sin(a) * r);
            var lean = wind * (0.18f * tall);
            int frame = (int)((t * 17 + h * 16 + i * 4.7) % 16);
            var colour = middle ? new Vector4(1.25f, 0.78f, 0.38f, 1f) : new Vector4(1.05f, 0.55f, 0.26f, 1f);
            mesh.Billboard(foot + up * (tall * 0.45f) + lean * 0.5f, tall * 0.62f, (h - 0.5f) * 0.3f, colour, _flame, FxBlend.Additive, frame, 4,
                stretch: 1.7f);
        }
        if (near <= 0.05f)
            return;
        // Cinders off it, going up and away downwind, winking out.
        for (int k = 0; k < 24; k++)
        {
            float h = Hash(seed * 0.53f + k * 1.37f), period = 3.0f + 3 * h;
            float age = (float)((t + h * 11) % period), a = age / period;
            var spark = fire + up * (2 + age * 4.5f) + wind * (age * age * 0.9f)
                + new Vector3(MathF.Sin(age * 2.1f + k) * radius * 0.6f, 0, MathF.Cos(age * 1.7f + k * 0.7f) * radius * 0.6f);
            mesh.Billboard(spark, 0.16f, 0, new Vector4(Palette.FurnaceOrange * (1.8f * (1 - a) * near), 1), -1, FxBlend.Additive);
        }
        // The column of smoke: coal smoke, thick, the fire's orange under it, sooty above, leaning off with the wind.
        if (_smoke >= 0)
        {
            const float life = 16f, rate = 2.5f;
            double emitted = Math.Floor(t * rate);
            for (int k = 0; k < (int)(life * rate); k++)
            {
                double index = emitted - k;
                float age = (float)(t - index / rate);
                if (age < 0 || age > life)
                    continue;
                float h = Hash((float)(index * 0.618 + seed * 2.3)), a = age / life;
                var at = fire + up * (7f * sc + 2.0f * age) + wind * age * (1.6f + 0.8f * h)
                    + new Vector3(h - 0.5f, 0, Hash((float)index + seed) - 0.5f) * (radius + age * 0.6f);
                float size = sc * (3.5f + 11f * MathF.Sqrt(a));
                float alpha = 0.6f * MathF.Pow(1 - a, 1.3f) * MathF.Min(1, age) * near;
                var tint = Vector3.Lerp(new Vector3(0.5f, 0.27f, 0.12f), new Vector3(0.1f, 0.095f, 0.09f), MathF.Min(1, a * 2.5f));
                mesh.Billboard(at, size, h * 6.28f + age * 0.08f, new Vector4(tint, alpha), _smoke, FxBlend.Alpha, (int)(a * 15.99f), 4);
            }
        }
        // Its light: over the gallery and the tower's top, and thrown wide over the head's roofs below.
        mesh.PointLights.Add(new PointLight(fire + up * 2.5f, Palette.FurnaceOrange * (2.6f * flicker), 46));
        mesh.PointLights.Add(new PointLight(fire + up * 6f, Palette.LampAmber * (1.6f * flicker), 110));
    }

    /// <summary>
    /// What a fire left of a cell (App. F.1: "burnt cells char the textures"; note 267; note 763, the director, 13 Oct:
    /// "There's a weird brown circle leftover where fires are"): the boards burnt to alligatored char (<c>fx_char</c>),
    /// laid along their grain in a few overlapping patches, each its own ragged shape, so a cell's char runs into its
    /// neighbours' and never reads as a disc. A little burnt is a scorch or two, browned round the edge; burnt out, the
    /// cell is black from side to side, sooted out past it. Camera-relative, on the surface by <paramref name="along"/>
    /// (the grain) and <paramref name="across"/> (half-sizes), just off it into the car. The decals are soot's colour,
    /// alpha-blended, so they only ever darken the boards under them, whatever light is on them.
    /// </summary>
    public void Char(MeshBuilder mesh, Vector3 at, Vector3 along, Vector3 across, Vector3 normal, float amount, int seed)
    {
        if (amount <= 0.02f || _char < 0 || at.Length() > 160)
            return;
        float big = Math.Clamp(amount, 0, 1);
        // One patch over the cell's heart, the worse it burnt the more of the cell it takes; up to four more about it.
        int count = 1 + (int)(big * 4.99f);
        for (int k = 0; k < count; k++)
        {
            float h0 = Hash(seed * 1.31f + k * 7.7f), h1 = Hash(seed * 2.17f + k * 3.9f), h2 = Hash(seed * 0.73f + k * 5.3f);
            float h3 = Hash(seed * 3.53f + k * 1.1f), h4 = Hash(seed * 4.41f + k * 2.3f);
            Vector2 centre;
            float size;
            if (k == 0)
            {
                centre = new Vector2(h0 - 0.5f, h1 - 0.5f) * 0.3f;
                size = 0.55f + 0.75f * big;
            }
            else
            {
                centre = new Vector2(h0 - 0.5f, h1 - 0.5f) * 1.5f;
                size = (0.3f + 0.45f * big) * (0.7f + 0.6f * h2);
            }
            // A little burnt is a scorch: the char's heart only where it burnt longest.
            float strength = Math.Clamp(0.45f + 0.75f * big - 0.12f * k, 0.3f, 1);
            var c = at + along * centre.X + across * centre.Y + normal * (0.012f + 0.003f * k);
            // Longer along the grain than across it; mirrored or not, so no two patches match.
            var u = along * (size * (1.05f + 0.25f * h3)) * (h4 < 0.5f ? 1 : -1);
            var v = across * (size * (0.9f + 0.2f * h2));
            int frame = (int)(Hash(seed * 5.9f + k * 9.1f) * 15.99f);
            var uv0 = new Vector2(frame % 4, frame / 4) * 0.25f;
            var colour = new Vector4(1, 1, 1, strength);
            var p00 = new FxVertex(c - u - v, uv0, colour, _char);
            var p10 = new FxVertex(c + u - v, uv0 + new Vector2(0.25f, 0), colour, _char);
            var p11 = new FxVertex(c + u + v, uv0 + new Vector2(0.25f, 0.25f), colour, _char);
            var p01 = new FxVertex(c - u + v, uv0 + new Vector2(0, 0.25f), colour, _char);
            mesh.FxTriangle(FxBlend.Alpha, p00, p10, p11);
            mesh.FxTriangle(FxBlend.Alpha, p00, p11, p01);
        }
    }

    /// <summary>
    /// The fire in the firebox, seen through the open firehole (GDD §12, the fireman's game; §31 "furnace flare"): a bed
    /// of flames licking up off the coals, taller and whiter the hotter it's burning, a cinder now and then out of the hole
    /// at you, and its light thrown out into the cab. <paramref name="bed"/> is the middle of the coal bed (camera-relative),
    /// <paramref name="back"/> towards the cab.
    /// </summary>
    /// <param name="heat">The fire, 0..1 (the boiler's fire fraction).</param>
    /// <param name="colour">Its colour (orange, or the Stoker's sick green).</param>
    /// <param name="sinceCoal">Seconds since the last shovelful (the boiler's <c>SinceShovel</c>): for <see cref="FlareSeconds"/>
    /// after it the fire flares (§31): the bed roars up, a gout of flame licks out of the hole and a shower of sparks is
    /// thrown into the cab, its light jumping, the fireman's reward for a shovelful landed.</param>
    public void Furnace(MeshBuilder mesh, Vector3 bed, Vector3 right, Vector3 up, Vector3 back, float heat, Vector3 colour, double t,
        double sinceCoal = double.PositiveInfinity)
    {
        var tint = Vector3.Normalize(colour + new Vector3(1e-3f)) * 1.25f;
        // Out (GreyboxScene.FireLook: no coal at all): a dead grate, the faintest heat left in the ash, no flames, no light.
        if (heat <= 0)
        {
            mesh.Billboard(bed + up * 0.02f, 0.7f, 0, new Vector4(tint * 0.04f, 1), -1, FxBlend.Additive, stretch: 0.3f);
            return;
        }
        float hot = Math.Clamp(heat, 0.05f, 1);
        // Its colour by its heat (the checklist's firebox: "low-fire and roaring states"): a low fire is a deep, sullen red
        // over dull coals; a roaring one goes yellow and then white at its heart. (A Stoker's green keeps its own hue.)
        bool natural = tint.X >= tint.Y;
        if (natural)
            tint = hot < 0.35f ? Vector3.Lerp(new Vector3(1.6f, 0.45f, 0.15f), tint, hot / 0.35f)
                : hot > 0.8f ? Vector3.Lerp(tint, new Vector3(1.35f, 1.15f, 0.75f), (hot - 0.8f) / 0.2f) : tint;
        // The flare: strongest the moment the coal lands, gone by FlareSeconds.
        float coal = (float)Math.Max(0, sinceCoal);
        float flare = coal < FlareSeconds ? MathF.Pow(1 - coal / (float)FlareSeconds, 1.5f) : 0;
        Coals(mesh, bed, right, up, back, hot, tint, t);
        // The fire's levels as it builds (T121): a few tongues off a low bed, the whole grate alight at capacity (and all of
        // it for a moment as the coal catches). Each licks up off the heap where it is, its root up the mound or down in a
        // hollow and in or out over the bed, not off a line.
        int tongues = Math.Clamp((int)MathF.Round(1 + 8 * (hot - 0.3f) / 0.7f) + (int)(9 * flare), 1, 9);
        for (int i = 0; i < 9; i++)
        {
            if ((i * 5 + 2) % 9 >= tongues)
                continue;
            float h = Hash(i * 2.37f + 0.9f), h2 = Hash(i * 1.71f + 5.3f);
            float x = (i / 8f - 0.5f) * 0.56f + (h - 0.5f) * 0.05f;
            float root = Mound(x) - 0.02f + 0.03f * h2;
            float tall = (0.12f + 0.32f * hot) * (0.7f + 0.6f * h) * (1 + 1.3f * flare);
            int frame = (int)((t * 22 + i * 4.1) % 16);
            mesh.Billboard(bed + right * x + up * (root + tall * 0.45f) - back * (0.02f + 0.035f * h2), tall * 0.7f, (h - 0.5f) * 0.3f,
                new Vector4(tint * (0.55f + 0.5f * hot), 1), _flame, FxBlend.Additive, frame, 4, stretch: 1.4f);
        }
        // The coals' glow, low among them (soft, not a sheet over the bed).
        mesh.Billboard(bed - back * 0.05f, 0.5f, 0, new Vector4(tint * (0.12f + 0.2f * hot), 1), -1, FxBlend.Additive, stretch: 0.6f);
        // Roaring: the heart of it white-hot, a sheet of light over the whole bed.
        if (hot > 0.8f)
            mesh.Billboard(bed + up * 0.12f - back * 0.03f, 0.5f, 0, new Vector4(new Vector3(1.2f, 1.05f, 0.75f) * (hot - 0.8f) * 2.0f, 1), -1, FxBlend.Additive, stretch: 0.55f);
        // A cinder out of the hole now and then, up and out into the cab, going out.
        for (int k = 0; k < (hot > 0.8f ? 10 : 4); k++)
        {
            float h = Hash(k * 6.1f + 3.3f);
            float period = 1.6f + 2.4f * h;
            float age = (float)((t + h * 7) % period);
            if (age > 0.9f)
                continue;
            var p = bed + up * (0.15f + age * 0.9f - age * age * 0.6f) + back * (age * 1.4f) + right * ((h - 0.5f) * 0.3f + age * (h - 0.5f));
            mesh.Billboard(p, 0.035f, 0, new Vector4(1.0f, 0.55f, 0.18f, (1 - age / 0.9f) * hot), _spark, FxBlend.Additive, 0, 2);
        }
        if (flare > 0)
        {
            // The gout: tongues licking up and out of the hole at the fireman, curling up as they come.
            for (int k = 0; k < 4; k++)
            {
                float h = Hash(k * 4.7f + 1.3f);
                float out_ = (0.15f + 0.55f * (1 - flare)) * (0.6f + 0.4f * h);
                float tall = (0.25f + 0.3f * h) * flare;
                int frame = (int)((t * 26 + k * 3.7) % 16);
                mesh.Billboard(bed + back * out_ + up * (0.22f + out_ * 0.5f) + right * ((h - 0.5f) * 0.35f), tall, (h - 0.5f) * 0.6f,
                    new Vector4(tint * 1.1f, flare), _flame, FxBlend.Additive, frame, 4, stretch: 1.3f);
            }
            // The shower: sparks thrown out of the hole into the cab, falling, going out.
            for (int k = 0; k < 26; k++)
            {
                float h = Hash(k * 2.9f + 7.1f), h2 = Hash(k * 5.3f + 0.7f);
                float a = coal - 0.05f * h;
                if (a < 0 || a > 0.35f + 0.5f * h2)
                    continue;
                var v = back * (1.8f + 2.2f * h) + up * (0.8f + 1.6f * h2) + right * ((h2 - 0.5f) * 1.8f);
                var p = bed + up * 0.25f + v * a - up * (4.9f * a * a);
                mesh.Billboard(p, 0.12f + 0.06f * h, 0, new Vector4(1.2f, 0.75f, 0.3f, 1 - a / (0.35f + 0.5f * h2)), _spark, FxBlend.Additive, 0, 2, stretch: 1.6f);
            }
            // The flash of it, over the hole.
            mesh.Billboard(bed + back * 0.25f + up * 0.25f, 0.9f, 0, new Vector4(tint * 0.6f * flare, 1), -1, FxBlend.Additive);
        }
        // Its light out through the hole into the cab: on the fireman, on the backhead, flickering (jumping as the coal catches).
        float flicker = 0.85f + 0.08f * MathF.Sin((float)t * 9.1f) + 0.07f * MathF.Sin((float)t * 23.7f);
        mesh.PointLights.Add(new PointLight(bed + back * 0.55f + up * 0.35f, colour * (1.4f + 2.2f * hot) * flicker * (1 + 1.6f * flare), 4.5f + 2 * flare));
    }

    /// <summary>
    /// The bed of coals through the firehole (the checklist's firebox): a heap of lumps across the grate, mounded in the
    /// middle and banked up at the back, each turned its own way. The dark ones are coal, lit by the fire. The ones in the
    /// heart of it glow, more of them the hotter it is, each breathing on its own slow beat. It replaces the flat bright
    /// band a bed of coals was.
    /// </summary>
    void Coals(MeshBuilder mesh, Vector3 bed, Vector3 right, Vector3 up, Vector3 back, float hot, Vector3 tint, double t)
    {
        float was = mesh.Emissive;
        for (int k = 0; k < 72; k++)
        {
            float h1 = Hash(k * 3.17f + 0.4f), h2 = Hash(k * 1.93f + 2.2f), h3 = Hash(k * 4.41f + 1.1f), h4 = Hash(k * 2.63f + 7.9f);
            float x = (h1 - 0.5f) * 0.6f, d = 0.01f + 0.12f * h2;
            float y = Mound(x) + 0.025f * (d / 0.13f) - 0.012f * h3;
            float size = 0.012f + 0.016f * h4;
            float a = h3 * 6.283f, c = MathF.Cos(a), sn = MathF.Sin(a), yaw = h4 * 6.283f;
            var r = right * c + up * sn;
            var u = up * c - right * sn;
            var b = back;
            (r, b) = (r * MathF.Cos(yaw) + b * MathF.Sin(yaw), b * MathF.Cos(yaw) - r * MathF.Sin(yaw));
            var at = bed + right * x + up * y - back * d;
            var half = new Vector3(size, size * (0.45f + 0.5f * h1), size * (0.55f + 0.45f * h2));
            // Glowing: down in the heart of the bed (the middle, the back), more of it alight the hotter the fire.
            float heart = 1 - MathF.Abs(x) / 0.3f * 0.6f;
            bool lit = Hash(k * 5.77f + 3.3f) < (0.2f + 0.55f * hot) * heart;
            if (lit)
            {
                mesh.Emissive = 1;
                float breath = 0.65f + 0.35f * MathF.Sin((float)t * (0.9f + 0.8f * h2) + k * 1.7f);
                mesh.Box(at, r, u, b, half, tint * new Vector3(1, 0.62f, 0.42f) * (0.18f + 0.5f * hot) * breath * (0.7f + 0.6f * h3));
            }
            else
            {
                mesh.Emissive = was;
                mesh.Box(at, r, u, b, half, new Vector3(0.011f, 0.010f, 0.010f) * (0.8f + 0.4f * h4));
            }
        }
        mesh.Emissive = was;
    }

    // The heap's height off the bed's line across the grate (m): mounded in the middle, lower at the sides.
    static float Mound(float x) => -0.07f + 0.05f * (1 - MathF.Min(1, MathF.Abs(x) / 0.3f) * MathF.Min(1, MathF.Abs(x) / 0.3f));

    /// <summary>How long a shovelful's flare lasts (s), from the coal landing: the roar up and the shower out of the hole.</summary>
    public const double FlareSeconds = 0.9;

    /// <summary>
    /// An extinguisher at work (GDD App. C.5, the counter to a car fire): a jet of white powder out of the nozzle at
    /// <paramref name="from"/>, fanning as it goes, onto <paramref name="to"/> (the fire's foot), where it billows into a
    /// low white cloud that hangs in the smoke.
    /// </summary>
    public void Spray(MeshBuilder mesh, Vector3 from, Vector3 to, double t)
    {
        var along = to - from;
        float length = along.Length();
        if (length < 0.1f)
            return;
        var dir = along / length;
        var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY));
        var lift = Vector3.Cross(side, dir);
        for (int k = 0; k < 34; k++)
        {
            float h = Hash(k * 3.3f + 1.1f), h2 = Hash(k * 7.9f + 0.4f);
            float period = 0.45f + 0.2f * h;
            float u = (float)((t + h * 3) % period) / period;
            var p = from + along * u + (side * (h2 - 0.5f) + lift * (h - 0.5f)) * (0.05f + 0.55f * u);
            mesh.Billboard(p, 0.14f + 0.85f * u, h * 6.28f, new Vector4(0.95f, 0.96f, 0.98f, 0.8f * (1 - 0.4f * u)), _steam, FxBlend.Alpha, (int)(u * 9.99f), 4);
        }
        for (int k = 0; k < 12; k++)
        {
            float h = Hash(k * 5.7f + 9.2f), h2 = Hash(k * 2.1f + 4.4f);
            float period = 1.8f + 1.2f * h;
            float s = (float)((t + h * 5) % period) / period;
            var p = to + side * ((h2 - 0.5f) * (0.6f + 1.4f * s)) + Vector3.UnitY * (0.1f + 0.7f * s) - dir * (0.2f * s);
            mesh.Billboard(p, 0.5f + 1.6f * s, h * 6.28f, new Vector4(0.8f, 0.81f, 0.83f, 0.5f * MathF.Sin(MathF.PI * s)), _steam, FxBlend.Alpha, 4 + (int)(s * 11.99f), 4);
        }
    }
}
