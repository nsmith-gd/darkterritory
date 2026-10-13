using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// The boiler rupturing (spec B.6, GDD §23 "loud, spectacular, run-changing"; §23.2), <paramref name="since"/> seconds
    /// after it: a flash and a blast of steam out of the seam that's torn open in the boiler's flank (TrainKit.RuptureSeam),
    /// the jet roaring out sideways for a few seconds and dying to a hiss that thins as what's left in her bleeds away;
    /// and in the cab, the fire blown back out of the firehole in a gout of steam, ash and embers (the fire's gone to
    /// nothing in the sim the same tick). With <paramref name="locked"/> (the cylinders seized and the train still
    /// dragging down to coasting speed: TrainOnLine's ruptureDecel) the drivers slide on the rail, a stream of sparks off
    /// every tyre.
    /// </summary>
    public void Rupture(MeshBuilder mesh, in CarFrame engine, Double3 eye, double since, bool locked)
    {
        var shape = engine.Shape;
        if (shape.Cab is null || since < 0 || (engine.Origin - eye).Length > 300)
            return;
        float a = (float)since;
        var velocity = F(engine.Velocity);
        var up = F(engine.Up);
        var back = F(engine.Back);
        var outward = -F(engine.Right);
        var seam = TrainKit.RuptureSeam(shape);
        var at = engine.ToWorld(new Double3(seam.X, seam.Y, seam.Z)).RelativeTo(eye);

        // The burst: the first four seconds the jet's at full blast, then a hiss that thins over the next half minute.
        float burst = a < 4 ? 1 - a / 4 : 0;
        float hiss = 0.25f + 0.35f * MathF.Exp(-a / 10);
        // The flash and the first cloud: the boiler's whole head of steam out at once, a white ball blown out and up.
        if (a < 0.15f)
            mesh.Billboard(at + outward * 0.6f, 3.5f, 0, new Vector4(1.0f, 0.92f, 0.8f, 1 - a / 0.15f), -1, FxBlend.Additive);
        if (a < 1.2f)
            for (int k = 0; k < 14; k++)
            {
                float h = Hash(k * 3.17f + 0.4f), h2 = Hash(k * 7.1f + 2.2f);
                float s = 1 - MathF.Exp(-a * 3.5f);
                var p = at + outward * (0.4f + 4.5f * s * (0.6f + 0.6f * h)) + up * (s * (1 + 2.5f * h2)) + back * ((h2 - 0.5f) * 5 * s) - velocity * a;
                mesh.Billboard(p, 1.2f + 6f * s, h * 6.28f, new Vector4(0.8f, 0.81f, 0.84f, 0.85f * (1 - a / 1.2f)), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
            }
        // The jet: hard out of the tear and checked by the air (out fast, slowing: 1 - e^-3t over 3), opening and rising
        // as it slows, laid back down the train by its going. A dense white core at the tear, the plume round it.
        for (int k = 0; k < 56; k++)
        {
            float h = Hash(k * 2.31f + 1.7f), h2 = Hash(k * 5.77f + 0.3f), h3 = Hash(k * 9.13f + 4.1f);
            float period = 0.5f + 0.7f * h;
            float age = (float)((since * (1 + 0.3 * h) + h * 7) % period);
            float t = age / period;
            float push = (6 + 22 * burst) * (0.7f + 0.6f * h3);
            float reach = push * (1 - MathF.Exp(-3 * age)) / 3;
            var p = at + outward * reach + up * (reach * (0.05f + 0.25f * h2) + age * age * 1.5f) + back * ((h2 - 0.5f) * reach * 0.9f)
                - velocity * age;
            float alpha = 0.9f * MathF.Min(1, burst + hiss) * (1 - t) * (k < 12 ? 1 : 0.75f);
            float size = (0.35f + t * (1.6f + 5f * burst)) * (k < 12 ? 0.7f : 1);
            var tone = k < 12 ? new Vector3(0.86f, 0.87f, 0.9f) : new Vector3(0.7f, 0.71f, 0.74f);
            mesh.Billboard(p, size, h * 6.28f, new Vector4(tone, alpha), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
        }
        // The jet's core: a hard white cone stood out of the tear, as long as the burst is strong (a few metres at the
        // blast, a hand's breadth hissing at the end), shivering; it's where the steam's too fast to be blown back yet.
        float length = 0.4f + 5.5f * burst + 1.2f * hiss;
        for (int k = 0; k < 12; k++)
        {
            float f = (k + 0.5f) / 12;
            float shiver = Hash((float)Math.Floor(since * 24) + k * 3.3f) - 0.5f;
            float d = f * length;
            var p = at + outward * d + up * (d * 0.08f + shiver * 0.05f * d) + back * (shiver * 0.12f * d) - velocity * (d / (25 + 30 * burst));
            mesh.Billboard(p, 0.25f + d * 0.55f, k * 1.3f + shiver, new Vector4(0.9f, 0.91f, 0.94f, MathF.Min(1, burst + hiss * 1.6f) * (1 - f * 0.7f)), _steam, FxBlend.Alpha, k % 4, 4);
        }
        // Scale and hot grit off the tear: thrown out and falling.
        if (a < 1.4f)
            for (int k = 0; k < 18; k++)
            {
                float h = Hash(k * 4.9f + 0.8f), h2 = Hash(k * 1.3f + 5.5f);
                float age = a - h * 0.3f;
                if (age < 0 || age > 1.1f)
                    continue;
                var p = at + outward * (age * (6 + 6 * h)) + up * (age * (2 + 4 * h2) - 4.9f * age * age) + back * ((h2 - 0.5f) * age * 5) - velocity * age;
                mesh.Billboard(p, 0.14f, 0, new Vector4(1.0f, 0.55f, 0.2f, 1 - age / 1.1f), _spark, FxBlend.Additive, k % 4, 2, stretch: 2f);
            }

        // In the cab: the fire blown back out of the firehole, steam, ash and embers across the footplate.
        if (a < 3 && (engine.Origin - eye).Length < 60 && shape.Interactables.Any(i => i.Kind == InteractableKind.Firebox))
        {
            var hole = engine.ToWorld(TrainKit.InFirebox(shape, 0, -0.05)).RelativeTo(eye);
            var into = F(engine.DirToWorld(TrainKit.OutOfBackhead(shape)));
            var across = Vector3.Cross(up, into);
            float gout = 1 - a / 3;
            for (int k = 0; k < 22; k++)
            {
                float h = Hash(k * 6.13f + 0.9f), h2 = Hash(k * 2.71f + 3.3f);
                float s = 1 - MathF.Exp(-(a + h * 0.2f) * 2.2f);
                var p = hole + into * (s * (1.8f + 2.2f * h)) + across * ((h2 - 0.5f) * 2.4f * s) + up * (s * (0.2f + 1.2f * h2));
                var colour = Vector3.Lerp(new Vector3(0.62f, 0.6f, 0.58f), new Vector3(0.2f, 0.18f, 0.16f), h2);
                mesh.Billboard(p, 0.4f + 1.8f * s, h * 6.28f + a, new Vector4(colour, 0.75f * gout * gout), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
            }
            for (int k = 0; k < 16; k++)
            {
                float h = Hash(k * 8.3f + 0.2f), h2 = Hash(k * 3.9f + 4.4f);
                float age = a - h * 0.25f;
                if (age < 0 || age > 1.5f)
                    continue;
                var p = hole + into * (age * (2.5f + 2.5f * h)) + across * ((h2 - 0.5f) * age * 3) + up * (age * (1.5f * h2) - 2.5f * age * age);
                mesh.Billboard(p, 0.06f + 0.04f * h, 0, new Vector4(1.0f, 0.42f, 0.1f, 1 - age / 1.5f), _spark, FxBlend.Additive, k % 4, 2);
            }
        }

        // The drivers locked and sliding: sparks off each tyre where it bears on the rail, streamed back along it.
        float speed = velocity.Length();
        if (locked && speed > 0.5f && (engine.Origin - eye).Length < 150)
        {
            float amount = MathF.Min(1, speed / 6);
            foreach (float z in TrainKit.Drivers(shape))
                foreach (int side in new[] { -1, 1 })
                {
                    var contact = engine.ToWorld(new Double3(side * (TrainKit.HalfGauge + 0.03), 0.06, z + 0.1)).RelativeTo(eye);
                    // The tyre's flat, hot where it's skidding: a dull glow on the rail under it.
                    mesh.Billboard(contact, 0.35f, 0, new Vector4(1.0f, 0.45f, 0.12f, 0.6f * amount), -1, FxBlend.Additive);
                    for (int k = 0; k < 7; k++)
                    {
                        float flick = Hash((float)Math.Floor(since * 30) + k * 1.9f + z * 0.7f + side * 2.3f);
                        if (flick < 0.25f)
                            continue;
                        var p = contact + back * (flick * 1.1f) + up * (flick * flick * 0.25f) + outward * (-side * (flick - 0.5f) * 0.25f);
                        mesh.Billboard(p, 0.2f, 0.2f, new Vector4(1.0f, 0.62f, 0.25f, amount * flick), _spark, FxBlend.Additive, k % 4, 2, stretch: 4f);
                    }
                }
        }
    }
}
