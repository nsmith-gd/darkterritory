using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
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
    /// <see cref="Alight"/> flaming, <see cref="Since"/> seconds since it went out, <see cref="Peak"/> the worst it got.
    /// </summary>
    public readonly record struct Burning(float Burn, bool Alight, float Since, float Peak)
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
        if (amount < 0.02f || _smoke < 0)
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
        if (!fire.Alight)
            return;
        // The doors' cracks glowing, and the light through them on the ground and the next car.
        float flicker = 0.75f + 0.25f * MathF.Sin((float)t * 13.1f + seed) * MathF.Sin((float)t * 7.3f);
        foreach (float sd in (ReadOnlySpan<float>)[-1, 1])
        {
            mesh.Billboard(L(sd * (wide + 0.03f), 1.6f, 0), 1.1f + fire.Burn, 0, new Vector4(Palette.FurnaceOrange * 0.4f * fire.Burn * flicker, 1), -1,
                FxBlend.Additive, stretch: 0.35f);
            mesh.PointLights.Add(new PointLight(L(sd * (wide + 0.4f), 1.4f, 0), Palette.FurnaceOrange * (0.6f + 1.4f * fire.Burn) * flicker, 4 + 4 * fire.Burn));
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
    public void CarFire(MeshBuilder mesh, Vector3 o, Vector3 r, Vector3 u, Vector3 b, bool alight, double burn, double t, float ceiling = 2.0f)
    {
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
            float y = MathF.Min(climb, roof), spread = MathF.Max(0, climb - roof);
            float z = (h - 0.5f) * 2.2f + (h2 < 0.5f ? -1 : 1) * spread * 1.7f;
            float x = -0.1f - s * 0.7f;
            float size = 0.45f + s * (alight ? 1.9f : 1.3f);
            float a = MathF.Min(1, (alight ? 0.85f + 0.4f * burnF : 0.55f) * MathF.Sin(MathF.PI * MathF.Min(1, s * 1.3f)));
            // Lit from under by the flames while it's low and near them; black once it's rolled away along the roof.
            var grey = alight ? new Vector3(0.07f, 0.06f, 0.055f) + Palette.FurnaceOrange * 0.22f * burnF * MathF.Max(0, 1 - s * 1.6f)
                : new Vector3(0.24f, 0.24f, 0.25f);
            mesh.Billboard(L(x, y, z), size, h * 6.28f + age * 0.35f, new Vector4(grey * (0.85f + 0.3f * h2), a), _smoke, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
        // The pool under the roof: a ceiling of smoke out each way along the car, lower and thicker the worse it's got,
        // drifting slowly; what you crouch under.
        int pool = alight ? 30 : 12;
        float reach = (alight ? 2.2f + 3.5f * burnF : 1.6f), depth = alight ? 0.25f + 0.45f * burnF : 0.15f;
        for (int k = 0; k < pool; k++)
        {
            float h = Hash(k * 4.13f + 7.7f), h2 = Hash(k * 1.91f + 3.3f);
            float z = (k / (pool - 1f) - 0.5f) * 2 * reach + MathF.Sin((float)t * 0.15f + h * 6.28f) * 0.4f;
            float x = -0.5f + (h2 - 0.5f) * 0.9f;
            float y = ceiling - 0.2f - depth * h;
            float a = MathF.Min(1, (alight ? 0.8f + 0.5f * burnF : 0.45f) * (1.15f - MathF.Abs(z) / reach));
            // Thick and low over the fire, lit by it from under; thinner and darker away along the car.
            float near = MathF.Max(0, 1 - MathF.Abs(z) / 2.5f);
            var grey = alight ? new Vector3(0.09f, 0.075f, 0.065f) + Palette.FurnaceOrange * 0.3f * burnF * near : new Vector3(0.3f, 0.3f, 0.31f);
            mesh.Billboard(L(x, y, z), 1.3f + h * 0.9f, h * 6.28f + (float)t * 0.05f, new Vector4(grey, a), _smoke, FxBlend.Alpha, 6 + (int)(h2 * 8), 4,
                stretch: 0.6f);
        }
        if (!alight)
            return;

        // Flames: a front rank on the face of the load and a deeper one in it, each tongue on its own beat through the
        // flipbook (20 frames a second), its foot on the floor.
        int tongues = 4 + (int)(9 * burnF);
        for (int rank = 0; rank < 2; rank++)
            for (int i = 0; i < tongues; i++)
            {
                float h = Hash(i * 3.17f + rank * 9.1f);
                float along = (i - (tongues - 1) / 2f) * 0.3f + (h - 0.5f) * 0.12f;
                float tall = (0.55f + 1.5f * burnF) * (0.7f + 0.45f * h) * (rank == 0 ? 1 : 1.2f);
                float width = tall * 0.62f;
                int frame = (int)((t * 20 + i * 5.3 + rank * 7) % 16);
                float glow = rank == 0 ? 1f : 0.6f;
                // The front rank stands just off the face (else the crates cut it in half); the deeper one shows over the stack.
                mesh.Billboard(L(rank * 0.3f - 0.14f, tall * 0.48f, along), width, (h - 0.5f) * 0.25f, new Vector4(0.85f, 0.55f, 0.32f, glow), _flame, FxBlend.Additive, frame, 4,
                    stretch: tall / width);
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
    /// The fire in the firebox, seen through the open firehole (GDD §12, the fireman's game; §31 "furnace flare"): a bed
    /// of flames licking up off the coals, taller and whiter the hotter it's burning, a cinder now and then out of the hole
    /// at you, and its light thrown out into the cab. <paramref name="bed"/> is the middle of the coal bed (camera-relative),
    /// <paramref name="back"/> towards the cab.
    /// </summary>
    /// <param name="heat">The fire, 0..1 (the boiler's fire fraction).</param>
    /// <param name="colour">Its colour (orange, or the Stoker's sick green).</param>
    public void Furnace(MeshBuilder mesh, Vector3 bed, Vector3 right, Vector3 up, Vector3 back, float heat, Vector3 colour, double t)
    {
        float hot = Math.Clamp(heat, 0.05f, 1);
        var tint = Vector3.Normalize(colour + new Vector3(1e-3f)) * 1.25f;
        for (int i = 0; i < 9; i++)
        {
            float h = Hash(i * 2.37f + 0.9f);
            float x = (i / 8f - 0.5f) * 0.56f + (h - 0.5f) * 0.05f;
            float tall = (0.12f + 0.32f * hot) * (0.7f + 0.6f * h);
            int frame = (int)((t * 22 + i * 4.1) % 16);
            mesh.Billboard(bed + right * x + up * (tall * 0.45f) - back * (0.06f * h), tall * 0.7f, (h - 0.5f) * 0.3f,
                new Vector4(tint * (0.55f + 0.5f * hot), 1), _flame, FxBlend.Additive, frame, 4, stretch: 1.4f);
        }
        // The coals' glow under the flames.
        mesh.Billboard(bed + up * 0.03f, 0.8f, 0, new Vector4(tint * (0.25f + 0.35f * hot), 1), -1, FxBlend.Additive, stretch: 0.35f);
        // A cinder out of the hole now and then, up and out into the cab, going out.
        for (int k = 0; k < 4; k++)
        {
            float h = Hash(k * 6.1f + 3.3f);
            float period = 1.6f + 2.4f * h;
            float age = (float)((t + h * 7) % period);
            if (age > 0.9f)
                continue;
            var p = bed + up * (0.15f + age * 0.9f - age * age * 0.6f) + back * (age * 1.4f) + right * ((h - 0.5f) * 0.3f + age * (h - 0.5f));
            mesh.Billboard(p, 0.035f, 0, new Vector4(1.0f, 0.55f, 0.18f, (1 - age / 0.9f) * hot), _spark, FxBlend.Additive, 0, 2);
        }
        // Its light out through the hole into the cab: on the fireman, on the backhead, flickering.
        float flicker = 0.85f + 0.08f * MathF.Sin((float)t * 9.1f) + 0.07f * MathF.Sin((float)t * 23.7f);
        mesh.PointLights.Add(new PointLight(bed + back * 0.55f + up * 0.35f, colour * (1.4f + 2.2f * hot) * flicker, 4.5f));
    }

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
