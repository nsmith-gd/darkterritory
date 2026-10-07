using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// A bend taken too fast (BendStrain; App. F.1's overspeed telegraph): off each straining car's wheels on the outer
    /// rail, where the flanges are grinding on it, sparks streaming back, more and hotter the nearer it is to off; and past
    /// halfway, a haze of hot iron off the tyres. The audio's squeal and scream (GameAudio.Train) go with it.
    /// </summary>
    /// <param name="strain">Per car, by frame index: its strain (0..1) and the outer rail's side (+1 its right).</param>
    public void Flanges(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye, double time, IReadOnlyList<(float Stress, int Outer)> strain)
    {
        for (int c = 0; c < frames.Count && c < strain.Count; c++)
        {
            var (stress, outer) = strain[c];
            var f = frames[c];
            if (stress <= 0.05f || outer == 0 || (f.Origin - eye).Length > 150)
                continue;
            var back = F(f.Back);
            var up = F(f.Up);
            var right = F(f.Right);
            float l = (float)f.Shape.HalfLength;
            // The grinding lights the ballast and the car's skirt under it.
            if (stress > 0.35f)
                mesh.PointLights.Add(new PointLight(f.ToWorld(new Double3(outer * (TrainKit.HalfGauge + 0.2), 0.3, 0)).RelativeTo(eye),
                    new Vector3(1.0f, 0.5f, 0.15f) * (1.2f * stress), 3.5f));
            int sparks = 4 + (int)(16 * stress);
            foreach (float z in new[] { -l * 0.65f, l * 0.65f })
            {
                // The leading wheel of each truck bites hardest: the sparks come off its flange's face, the rail's inside.
                var contact = f.ToWorld(new Double3(outer * (TrainKit.HalfGauge - 0.04), 0.08, z - 0.6)).RelativeTo(eye);
                mesh.Billboard(contact, 0.35f + 0.5f * stress, 0, new Vector4(1.0f, 0.5f, 0.15f, 0.4f + 0.6f * stress), -1, FxBlend.Additive);
                for (int k = 0; k < sparks; k++)
                {
                    float flick = Hash((float)Math.Floor(time * 30) + k * 2.7f + z * 0.37f + c * 5.1f);
                    if (flick < 0.3f)
                        continue;
                    // Most stream back along the rail; some are flung out off it, up and over the ballast.
                    float fling = Hash(k * 7.9f + c * 1.3f + z) > 0.6f ? 1 : 0;
                    var p = contact + back * (flick * (0.6f + 1.4f * stress)) + up * (flick * flick * (0.15f + 0.3f * stress) + fling * flick * 0.5f)
                        + right * (outer * ((flick - 0.4f) * 0.25f + fling * flick * 0.9f));
                    mesh.Billboard(p, 0.22f + 0.14f * stress, 0.2f, new Vector4(1.0f, 0.6f + 0.2f * stress, 0.25f, stress * flick), _spark,
                        FxBlend.Additive, k % 4, 2, stretch: 3f + 2f * stress);
                }
                if (stress > 0.5f)
                    for (int k = 0; k < 3; k++)
                    {
                        float h = Hash(k * 3.3f + c + z), period = 0.8f + 0.4f * h;
                        float age = (float)((time + h * 3) % period), t = age / period;
                        var p = contact + back * (age * 2.5f) + up * (0.1f + age * 0.8f);
                        mesh.Billboard(p, 0.3f + t * 1.2f, h * 6.28f, new Vector4(0.3f, 0.28f, 0.26f, (stress - 0.5f) * 0.8f * (1 - t)), _smoke,
                            FxBlend.Alpha, (int)(t * 15.99f), 4);
                    }
            }
        }
    }
}
