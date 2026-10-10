using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>How long the earth a Waker threw up rising keeps hanging and falling after it's up (s).</summary>
    public const double WakerRiseAfter = 6;

    /// <summary>
    /// A Waker getting up (note 599; docs/design/creatures/wakers.md §3: "the first sight of one reads as the land getting up"):
    /// over its rise and a few seconds after, clods of earth heaved up off its back and flanks and falling, and a bank of dust
    /// rolling out low from the ground round it. Worked out from <paramref name="seconds"/> since it began to rise and its id,
    /// as every effect is: nothing kept, nothing sent.
    /// </summary>
    /// <param name="feet">The middle of where it stands (camera-relative), on the ground.</param>
    /// <param name="right">Across it (unit, flat); <paramref name="back"/> along it, toward its back end.</param>
    /// <param name="rise">How far up it is (0 under the ground, 1 stood up): the clods come off the land breaking over it.</param>
    public void WakerRise(MeshBuilder mesh, Vector3 feet, Vector3 right, Vector3 back, double seconds, double riseSeconds, double rise, long seed)
    {
        if (seconds < 0 || seconds > riseSeconds + WakerRiseAfter)
            return;
        float a = (float)seconds, s0 = seed % 97 + 0.5f;
        var up = Vector3.UnitY;
        bool rising = seconds < riseSeconds;
        // Its footprint: a hill's worth of back, 14 m across and 34 long (the stand-in's boxes).
        Vector3 Spot(float h, float h2) => feet + right * ((h - 0.5f) * 14) + back * ((h2 - 0.5f) * 34 + 2);
        // Clods: thrown up off where the land's breaking over it, each its own throw, again and again while it heaves.
        const float Period = 1.7f;
        for (int k = 0; k < 70; k++)
        {
            float h = Hash(s0 * 3.7f + k * 1.31f), h2 = Hash(s0 * 1.3f + k * 2.77f), h3 = Hash(s0 * 0.7f + k * 4.13f);
            float born = (MathF.Floor((a + h * Period) / Period) * Period) - h * Period;
            float age = a - born;
            // Only throws that began while it was coming up; after, the last of them land.
            if (born < 0 || born > riseSeconds)
                continue;
            float speed = 5 + 9 * h3 * (0.6f + 0.6f * (float)rise);
            var from = Spot(h, h2) + up * (float)(rise * (4 + 10 * h3));
            var v = Vector3.Normalize(up * (1.4f + h) + right * (h - 0.5f) * 1.4f + back * (h2 - 0.5f) * 1.4f) * speed;
            float flight = 2 * v.Y / 9.8f + 0.6f;
            float t = MathF.Min(age, flight);
            var p = from + v * t - up * (4.9f * t * t);
            if (p.Y < feet.Y)
                p.Y = feet.Y + 0.1f;
            float settled = age > flight ? MathF.Max(0, 1 - (age - flight) / 1.5f) : 1;
            if (settled <= 0)
                continue;
            // Turf and boulders off a back a hill wide: the big ones metres across, dark against the pale stir behind.
            float size = 0.8f + 2.6f * h2 * h2;
            var earth = new Vector3(0.085f, 0.07f, 0.055f) * (0.75f + 0.5f * h3);
            mesh.Billboard(p, size, age * (2 + 5 * h), new Vector4(earth, settled), _spark, FxBlend.Alpha, (k & 1) == 0 ? 2 : 0, 2);
            // A trail of dirt falling off the bigger ones.
            if (size > 1.8f && age < flight)
                mesh.Billboard(p + up * size, size * 1.6f, h * 6, new Vector4(earth * 1.6f, 0.35f * settled), _smoke, FxBlend.Alpha, 3, 4);
        }
        // Dust: a bank rolling out low off the ground round it as it breaks out, thinning away after.
        for (int k = 0; k < 56; k++)
        {
            float h = Hash(s0 * 2.1f + k * 3.3f), h2 = Hash(s0 * 0.9f + k * 1.7f), h3 = Hash(s0 * 4.3f + k * 0.77f);
            float born = h3 * (float)riseSeconds * 0.8f, age = a - born;
            float life = (float)(riseSeconds + WakerRiseAfter) - born;
            if (age < 0 || age > life)
                continue;
            float s = age / life;
            var edge = Spot(h, h2);
            var outward = (edge - feet) with { Y = 0 };
            outward = outward.LengthSquared() > 1e-4f ? Vector3.Normalize(outward) : right;
            var p = edge + outward * (2 + 14 * (1 - MathF.Exp(-age * 0.5f))) + up * (1.5f + 5 * s);
            float size = 9 + 20 * MathF.Sqrt(s) * (0.7f + 0.5f * h2);
            float alpha = 0.95f * MathF.Pow(1 - s, 1.3f) * MathF.Min(1, age * 2) * (rising ? 1 : 0.8f);
            // Earth-dark, not the fog's grey, so it reads against the haze and the stir.
            var colour = new Vector3(0.07f, 0.06f, 0.05f) * (0.8f + 0.4f * h);
            mesh.Billboard(p, size, h * 6.28f + age * 0.1f * (h2 - 0.5f), new Vector4(colour, alpha), _smoke, FxBlend.Alpha, (int)(MathF.Min(s * 1.2f, 0.999f) * 16), 4);
        }
    }

    /// <summary>
    /// A Waker on the move (note 599): dust kicked up where each of its four feet comes down, a stride every
    /// <paramref name="stride"/> seconds, so one crossing the land toward the line is seen coming by what it raises.
    /// </summary>
    /// <param name="feet">The middle of where it stands (camera-relative), on the ground.</param>
    public void WakerStride(MeshBuilder mesh, Vector3 feet, Vector3 right, Vector3 back, double time, double stride, long seed)
    {
        float s0 = seed % 89 + 0.5f;
        var up = Vector3.UnitY;
        // Its four feet (the stand-in's arms and hind legs), each coming down half a stride after the one before.
        ReadOnlySpan<(float X, float Z)> foot = [(-5.5f, -11), (5.5f, 12), (5.5f, -11), (-5.5f, 12)];
        for (int f = 0; f < foot.Length; f++)
        {
            float since = (float)((time + f * stride * 0.5) % (stride * 2));
            const float Life = 2.4f;
            if (since > Life)
                continue;
            var at = feet + right * foot[f].X + back * foot[f].Z;
            for (int k = 0; k < 6; k++)
            {
                float h = Hash(s0 + f * 7.1f + k * 2.3f), h2 = Hash(s0 * 1.7f + f * 3.9f + k * 1.1f);
                float s = since / Life;
                var p = at + right * ((h - 0.5f) * (2 + 5 * s)) + back * ((h2 - 0.5f) * (2 + 5 * s)) + up * (0.6f + 2.5f * s);
                mesh.Billboard(p, 2.5f + 5 * MathF.Sqrt(s), h * 6.28f, new Vector4(new Vector3(0.08f, 0.07f, 0.06f), 0.6f * (1 - s) * MathF.Min(1, since * 6)),
                    _smoke, FxBlend.Alpha, (int)(MathF.Min(s, 0.999f) * 16), 4);
            }
        }
    }

    /// <summary>How often each of the town's guns fires on a Waker at the walls (s), and the shot's speed (m/s).</summary>
    const double WallGunEvery = 4.2, WallShotSpeed = 260;

    /// <summary>
    /// The town's guns at the walls (note 599; docs/design/creatures/wakers.md §4: "the Wakers stop stopShort metres out and
    /// stand there, roaring, while the town's guns fire at them"): each gun on the gatehouse and the towers fires on its own
    /// beat, the flash and smoke at its muzzle (<see cref="CannonShot"/>), and the shot lands on the Waker's back a moment
    /// after (<see cref="CannonImpact"/>), somewhere over its hill of a body. From <paramref name="time"/> alone.
    /// </summary>
    /// <param name="guns">The guns' muzzles (camera-relative).</param>
    /// <param name="target">The middle of the Waker's back (camera-relative); <paramref name="feet"/> the ground under it.</param>
    /// <param name="spread">How widely the shots land round the target, as a share of a Waker's back (a creature brought into
    /// town, note 589, is a man's size).</param>
    public void WallGuns(MeshBuilder mesh, IReadOnlyList<Vector3> guns, Vector3 target, Vector3 feet, double time, long seed, float spread = 1,
        Sim.Enemies.EnemyKind struck = Sim.Enemies.EnemyKind.Waker)
    {
        for (int g = 0; g < guns.Count; g++)
        {
            float off = Hash(seed % 53 + g * 2.9f) * (float)WallGunEvery;
            double since = (time + off) % WallGunEvery;
            long shot = (long)Math.Floor((time + off) / WallGunEvery) * 31 + g;
            var to = target - guns[g];
            float dist = to.Length();
            var along = to / Math.Max(dist, 1e-3f);
            CannonShot(mesh, guns[g], along, -along, Vector3.UnitY, 0, since, shot);
            // Where it lands: somewhere over its back, its own for each shot.
            float h = Hash(shot * 0.37f + 1.3f), h2 = Hash(shot * 1.13f + 0.7f);
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, along));
            var hit = target + side * ((h - 0.5f) * 10 * spread) + Vector3.UnitY * ((h2 - 0.5f) * 8 * spread);
            double flight = dist / WallShotSpeed;
            CannonImpact(mesh, hit, along, Sim.Combat.ImpactSurface.Creature, struck, since - flight, shot, feet);
        }
    }

    /// <summary>
    /// Coal shaken off the cab's bunker by a thud of the Wakers' stir (note 599; wakers.md §2: "coal sliding in the tender"):
    /// <paramref name="since"/> seconds after it, lumps off the heap's face sliding and dropping to the footplate, and a breath
    /// of coal dust; more of them the stronger it was (<paramref name="strength"/>, 0..1).
    /// </summary>
    /// <param name="face">Where the shovel goes in (camera-relative), on the footplate at the bunker's face.</param>
    /// <param name="outward">Out of the bunker's open face (unit, the car's back); <paramref name="along"/> across it.</param>
    public void CoalSlide(MeshBuilder mesh, Vector3 face, Vector3 outward, Vector3 along, double since, double strength, long seed)
    {
        const float Life = 2.2f;
        if (strength <= 0 || since < 0 || since > Life)
            return;
        float a = (float)since, s0 = seed % 61 + 0.5f;
        var up = Vector3.UnitY;
        int lumps = 4 + (int)(14 * strength);
        for (int k = 0; k < lumps; k++)
        {
            float h = Hash(s0 + k * 1.7f), h2 = Hash(s0 * 2.3f + k * 0.9f), h3 = Hash(s0 * 0.7f + k * 3.1f);
            float start = h3 * 0.25f, t = a - start;
            if (t < 0)
                continue;
            // Off the heap just inside its open face, across its width, a slide out and a drop to the plate (the deck's at the
            // face's height).
            var from = face - outward * (0.1f + 0.25f * h2) + along * ((h - 0.5f) * 0.6f) + up * (0.4f + 0.6f * h3);
            float fall = MathF.Min(t, 0.45f);
            var p = from + outward * (1.3f * fall * (0.6f + h)) - up * MathF.Min(4.9f * fall * fall, (from - face).Y - 0.05f);
            float fade = t > Life - 0.6f ? (Life - t) / 0.6f : 1;
            mesh.Billboard(p, 0.11f + 0.11f * h3, t * 9 * h, new Vector4(new Vector3(0.035f, 0.035f, 0.04f), fade), _spark, FxBlend.Alpha, 2, 2);
        }
        if (a < 1.2f)
            mesh.Billboard(face + up * 0.4f - outward * 0.1f, 0.5f + 0.7f * a, s0, new Vector4(new Vector3(0.05f, 0.05f, 0.05f), 0.35f * (float)strength * (1 - a / 1.2f)),
                _smoke, FxBlend.Alpha, (int)(a / 1.2f * 15.99f), 4);
    }
}
