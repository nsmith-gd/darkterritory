using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The train's effects (GDD §31 "VFX supports readability": steam venting, sparks, smoke trails, cinders, furnace
/// flare, drifting fog; "mechanical first, supernatural second"). Every one is worked out from the time and the train's
/// state, not simulated: puff <c>i</c> left the stack at a known time and has drifted since, so a screenshot is
/// repeatable, a client draws what the host would, and nothing needs replicating.
/// </summary>
public sealed class Effects(Look look)
{
    readonly int _smoke = look.Layer("fx_smoke"), _steam = look.Layer("fx_steam"), _spark = look.Layer("fx_spark"), _fog = look.Layer("fx_fog");

    static float Hash(float x) => Frac(MathF.Sin(x * 12.9898f) * 43758.5453f);
    static float Frac(float x) => x - MathF.Floor(x);

    static Vector3 F(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>
    /// The engine's: smoke and cinders from the stack, steam from the cylinder cocks when she's working slow, sparks at
    /// the brake shoes under a hard brake, and the headlamp's beam and halo.
    /// </summary>
    public void Train(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye, double time, TrainControls controls, float fire, bool emergency)
    {
        if (frames.Count == 0 || (frames[0].Origin - eye).Length > 400)
            return;
        var engine = frames[0];
        var shape = engine.Shape;
        var velocity = F(engine.Velocity);
        float speed = velocity.Length();
        var stackBox = shape.Solids.FirstOrDefault(s => s.Part == PartKind.Stack).Box;
        var stackTop = engine.ToWorld(new Double3(0, stackBox.Max.Y, stackBox.Centre.Z)).RelativeTo(eye);
        var up = F(engine.Up);
        var back = F(engine.Back);
        var right = F(engine.Right);

        // Smoke: puffs at a rate the regulator sets, each drifting up and left behind where the stack was. The wind
        // leans it off the line a little. Dark, sooty, lit only by the night (and faintly by the fire underneath).
        float work = (float)Math.Clamp(controls.Throttle, 0, 1);
        float rate = 3.5f + 7f * work;
        const float life = 5.5f;
        int count = (int)(rate * life);
        double emitted = Math.Floor(time * rate);
        var wind = new Vector3(0.7f, 0, 0.4f);
        for (int k = 0; k < count; k++)
        {
            double index = emitted - k;
            float age = (float)(time - index / rate);
            if (age < 0 || age > life)
                continue;
            float h = Hash((float)(index * 0.618));
            float t = age / life;
            var drift = -velocity * age + wind * age + up * (2.2f * MathF.Pow(age, 0.6f) * (1 + work)) + (right * (h - 0.5f) + back * (Hash((float)index + 3.3f) - 0.5f)) * (0.4f + age * 0.5f);
            float size = 0.9f + 3.6f * MathF.Sqrt(t) * (0.8f + 0.4f * h);
            float alpha = 0.6f * MathF.Pow(1 - t, 1.4f) * MathF.Min(1, age * 6) * (0.6f + 0.4f * work);
            var colour = new Vector4(new Vector3(0.05f, 0.046f, 0.044f) * (0.9f + 0.3f * h) + FurnaceTint(fire) * MathF.Max(0, 0.4f - t), alpha);
            mesh.Billboard(stackTop + drift, size, h * 6.28f + age * 0.3f, colour, _smoke, FxBlend.Alpha, (int)(t * 15.99f), 4);
        }
        // Cinders when she's worked hard: sparks up through the smoke, falling back.
        if (work > 0.55f && !emergency)
            for (int k = 0; k < 14; k++)
            {
                float h = Hash(k * 7.13f);
                float period = 1.2f + h;
                float age = (float)((time + h * 5) % period);
                var p = stackTop - velocity * age + up * (3.5f * age - 2.2f * age * age) + (right * (Hash(k + 1.1f) - 0.5f) + back * (Hash(k + 2.2f) - 0.5f)) * age * 2.5f;
                float a = (1 - age / period) * (work - 0.5f) * 2;
                mesh.Billboard(p, 0.12f, 0, new Vector4(1.0f, 0.45f, 0.12f, a), _spark, FxBlend.Additive, k % 4, 2);
            }

        // Steam from the cylinder cocks: working slow with the regulator open (starting away), white and low.
        if (work > 0.05f && speed < 6)
        {
            float amount = work * (1 - speed / 6);
            var front = engine.ToWorld(new Double3(0, 0.55, -shape.HalfLength + 1.6)).RelativeTo(eye);
            for (int k = 0; k < 18; k++)
            {
                float h = Hash(k * 3.71f);
                float period = 1.4f + h * 0.8f;
                float age = (float)((time * (1 + h) + h * 9) % period);
                float side = k % 2 == 0 ? -1 : 1;
                var p = front + right * side * (1.2f + age * 2.5f) + up * (age * 0.8f) + back * ((h - 0.5f) * 0.8f) - velocity * age;
                float t = age / period;
                mesh.Billboard(p, 0.6f + t * 2.2f, h * 6.28f, new Vector4(0.34f, 0.35f, 0.37f, 0.45f * amount * (1 - t)), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
            }
        }

        // Sparks at the brake shoes under a hard brake at speed.
        if (controls.Brake > 0.55 && speed > 3)
        {
            float amount = (float)(controls.Brake - 0.55) / 0.45f * MathF.Min(1, (speed - 3) / 8);
            foreach (var frame in frames)
            {
                if ((frame.Origin - eye).Length > 120)
                    continue;
                float l = (float)frame.Shape.HalfLength;
                foreach (float z in new[] { -l * 0.6f, l * 0.6f })
                    foreach (int side in new[] { -1, 1 })
                        for (int k = 0; k < 3; k++)
                        {
                            float flick = Hash((float)Math.Floor(time * 30) + k * 1.7f + z + side * 3.1f + frame.Index * 11);
                            if (flick < 0.4f)
                                continue;
                            var at = frame.ToWorld(new Double3(side * (TrainKit.HalfGauge + 0.05), 0.2 + flick * 0.1, z + (flick - 0.5) * 0.6)).RelativeTo(eye);
                            mesh.Billboard(at + back * flick * 0.3f, 0.16f, 0.3f, new Vector4(1.0f, 0.55f, 0.18f, amount * flick), _spark, FxBlend.Additive, k % 4, 2, stretch: 2.5f);
                        }
            }
        }

        if (emergency)
            return;
        // The headlamp: a halo round the lens, and the beam through the fog (the one light that reaches out; §28's
        // "headlamp and lantern cones"). Additive and faint, strongest at the lamp.
        var lamp = Views.Lighting(engine, look);
        var at0 = lamp.LampPosition.RelativeTo(eye);
        mesh.Billboard(at0 - lamp.LampDirection * 0.2f, 2.6f, 0, new Vector4(lamp.LampColour * 0.5f, 1), -1, FxBlend.Additive);
        mesh.Billboard(at0 - lamp.LampDirection * 0.25f, 0.9f, 0, new Vector4(lamp.LampColour, 1), -1, FxBlend.Additive);
        Beam(mesh, at0, lamp.LampDirection, lamp.LampConeDegrees * 0.8f, 40, lamp.LampColour * 0.09f);
        // The tail lamp's glow at the back of the train.
        var last = frames[^1];
        var tail = last.ToWorld(new Double3(-last.Shape.HalfWidth + 0.25, last.Shape.RoofHeight - 0.3, last.Shape.HalfLength + 0.15)).RelativeTo(eye);
        mesh.Billboard(tail, 1.0f, 0, new Vector4(0.6f, 0.08f, 0.05f, 1), -1, FxBlend.Additive);
    }

    static Vector3 FurnaceTint(float fire) => new Vector3(0.12f, 0.05f, 0.01f) * fire;

    /// <summary>A cone of light: rings along its length, bright at the lamp and gone at the far end.</summary>
    static void Beam(MeshBuilder mesh, Vector3 apex, Vector3 dir, float halfAngle, float length, Vector3 colour)
    {
        const int sides = 14, rings = 6;
        dir = Vector3.Normalize(dir);
        var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        var up = Vector3.Cross(side, dir);
        float tan = MathF.Tan(halfAngle * MathF.PI / 180);
        Vector3 P(int ring, int i)
        {
            float d = length * ring / rings;
            float a = i * MathF.Tau / sides;
            return apex + dir * d + (side * MathF.Cos(a) + up * MathF.Sin(a)) * (0.2f + d * tan);
        }
        // Each corner's alpha: brightest at the lamp, gone at the far end, and faded where the cone's surface is seen
        // edge-on (its silhouette), so it reads as lit air rather than a solid wedge; and faint right up at the eye.
        FxVertex V(int ring, int i)
        {
            var p = P(ring, i);
            float a = i * MathF.Tau / sides;
            var radial = side * MathF.Cos(a) + up * MathF.Sin(a);
            var toEye = -Vector3.Normalize(p);
            float facing = MathF.Abs(Vector3.Dot(radial, toEye));
            float along = MathF.Pow(1 - (float)ring / rings, 2);
            float near = Math.Clamp((p.Length() - 2) / 10, 0, 1);
            return new FxVertex(p, new(0.5f, 0.5f), new Vector4(colour, along * (0.18f + 0.82f * facing * facing) * near), -2);
        }
        for (int r = 0; r < rings; r++)
            for (int i = 0; i < sides; i++)
            {
                var v00 = V(r, i);
                var v01 = V(r, i + 1);
                var v10 = V(r + 1, i);
                var v11 = V(r + 1, i + 1);
                mesh.FxTriangle(FxBlend.Additive, v00, v10, v11);
                mesh.FxTriangle(FxBlend.Additive, v00, v11, v01);
            }
    }

    /// <summary>
    /// Drifting fog lying along the line near the eye (pipeline "fog cards along the track spline"): big, faint, slow,
    /// low to the ground; thicker in hollows. They break the fog into banks, so it moves and hides things by turns.
    /// </summary>
    public void Fog(MeshBuilder mesh, Sim.Rail.RailLine line, Double3 eye, double centre, double time, Vector3 fogColour, float density)
    {
        if (_fog < 0)
            return;
        double from = centre - 110, to = centre + 110;
        for (double s = Math.Floor(from / 14) * 14; s < to; s += 14)
        {
            if (s < 0 || s > line.Length)
                continue;
            float h = Hash((float)(s * 0.071));
            for (int k = 0; k < 3; k++)
            {
                float hk = Hash((float)s + k * 13.1f);
                double lateral = (hk - 0.5) * 70;
                var t = line.Sample(s + k * 4.7);
                var r = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var drift = r * Math.Sin(time * 0.05 + hk * 6) * 3 + t.Tangent * ((time * 0.4 + hk * 20) % 14 - 7);
                var p = (t.Position + r * lateral + Double3.Up * (0.4 + h * 1.2) + drift).RelativeTo(eye);
                float dist = p.Length();
                if (dist < 6)
                    continue;
                // Faint up close (a card in your face reads as a card), strongest mid-distance.
                float a = 0.16f * density / 0.016f * Math.Clamp((dist - 6) / 20, 0, 1) * (0.5f + 0.5f * h);
                mesh.Billboard(p, 12 + hk * 10, hk * 0.5f - 0.25f, new Vector4(fogColour * 1.25f, MathF.Min(a, 0.28f)), _fog, FxBlend.Alpha, stretch: 0.45f);
            }
        }
    }
}
