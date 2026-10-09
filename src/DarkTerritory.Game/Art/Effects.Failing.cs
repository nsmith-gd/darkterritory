using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// A battered car or engine let go, seen from anywhere along the train (note 576: "clearly signalled before it's lost").
    /// A failing car sheds: splinters and flakes of its plating drop off its sides and underframe and fall away behind it as
    /// it runs, and, with freight aboard, dust and chaff pour out of its doors (its load spilling). Breaking, it sheds twice
    /// as hard and its underframe drags sparks off the rail. A failing engine smokes dark from its boiler's flanks, and
    /// breaking or broken down it spits sparks and steam. Worked out from the time alone, like every effect; the cars'
    /// visible damage stages are the art's (DamageKit), these are what's coming off them.
    /// </summary>
    public void Failing(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye, double time, IReadOnlyList<FailingCar> failing)
    {
        float t = (float)time;
        foreach (var fc in failing)
        {
            CarFrame? found = null;
            foreach (var f in frames)
                if (f.Index == fc.Vehicle)
                {
                    found = f;
                    break;
                }
            if (found is not { } frame || (frame.Origin - eye).Length > 160)
                continue;
            var up = F(frame.Up);
            var right = F(frame.Right);
            var back = F(frame.Back);
            var velocity = F(frame.Velocity);
            float half = (float)frame.Shape.HalfLength;
            float width = (float)frame.Shape.HalfWidth;
            bool breaking = fc.Stage >= FailStage.Breaking;
            float seed = fc.Vehicle * 11.7f;
            var origin = frame.Origin.RelativeTo(eye);
            if (frame.Shape.Cab is not null || fc.Vehicle == 0)
            {
                // The engine: dark smoke from its boiler's flanks, thicker breaking; sparks off its frame then.
                int puffs = breaking ? 10 : 5;
                for (int k = 0; k < puffs; k++)
                {
                    float h = Hash(seed + k * 2.3f), h2 = Hash(seed + k * 4.1f + 0.7f);
                    float period = 2.2f;
                    float age = (t + h * period) % period;
                    var at = origin + right * ((h > 0.5f ? 1 : -1) * width * 0.9f) + up * (1.6f + 0.5f * h2) + back * (half * (h2 - 0.5f))
                        + up * (0.8f * age) - velocity * (0.6f * age);
                    float a = (1 - age / period) * (breaking ? 0.75f : 0.5f);
                    mesh.Billboard(at, 0.6f + 1.1f * age, h * 6.28f, new Vector4(0.12f, 0.11f, 0.1f, a), _smoke, FxBlend.Alpha, (int)(h * 15.99f), 4);
                }
                if (breaking)
                    Underframe(mesh, origin, up, right, back, velocity, half, width, t, seed, 14);
                continue;
            }
            // A car: a haze of its own dust and grit round its lower sides, worked out of it as it runs.
            int haze = breaking ? 10 : 6;
            for (int k = 0; k < haze; k++)
            {
                float h = Hash(seed + k * 4.3f + 7), h2 = Hash(seed + k * 2.1f + 8);
                float period = 2.6f;
                float age = (t + h * period) % period;
                float side = k % 2 == 0 ? 1 : -1;
                var at = origin + right * (side * (width + 0.8f + 0.5f * age)) + up * (0.6f + 0.6f * age) + back * (half * (1.8f * h2 - 0.9f))
                    - velocity * (0.5f * age);
                float a = MathF.Sin(MathF.PI * age / period) * (breaking ? 0.6f : 0.45f);
                mesh.Billboard(at, 1.2f + 1.4f * age, h * 6.28f, new Vector4(0.62f, 0.56f, 0.48f, a), _smoke, FxBlend.Alpha, (int)(h * 15.99f), 4);
            }
            // Torn plate grinding where its seams have opened: sparks spat off its sides, more as it goes. What reads at night.
            int spits = breaking ? 26 : 12;
            for (int k = 0; k < spits; k++)
            {
                float h = Hash(seed + k * 5.9f + 13), h2 = Hash(seed + k * 3.3f + 14), h3 = Hash(seed + k * 7.1f + 15);
                float life = 0.5f + 0.3f * h3;
                float age = (t + h * life) % life;
                float side = k % 2 == 0 ? 1 : -1;
                var from = origin + right * (side * (width + 0.05f)) + up * (1.2f + 1.6f * h2) + back * (half * (1.8f * h - 0.9f));
                var at = from + right * (side * 2.4f * age) + up * (1.2f * age - 4.9f * age * age) - velocity * (0.7f * age);
                mesh.Billboard(at, 0.28f + 0.14f * h, 0, new Vector4(2.4f, 1.3f + 0.6f * h3, 0.35f, 1 - age / life), _spark, FxBlend.Additive, k % 4, 2, stretch: 2.5f);
            }
            // Splinters and flakes of plating off its sides and underframe, falling and left behind as it runs.
            int pieces = breaking ? 28 : 16;
            for (int k = 0; k < pieces; k++)
            {
                float h = Hash(seed + k * 3.7f), h2 = Hash(seed + k * 6.1f + 1.1f), h3 = Hash(seed + k * 9.3f + 2.2f);
                float period = 1.4f + 0.6f * h3;
                float age = (t + h * period) % period;
                float side = h2 > 0.5f ? 1 : -1;
                var from = origin + right * (side * (width + 0.15f)) + up * (1.0f + 2.0f * h3) + back * (half * (2 * h - 1));
                var at = from - up * (4.9f * age * age) - velocity * age + right * (side * 0.6f * age);
                float fade = 1 - age / period;
                // Weathered wood and rust, pale against the dark car.
                var colour = new Vector4(0.85f + 0.15f * h2, 0.66f + 0.1f * h3, 0.45f, fade);
                mesh.Billboard(at, 0.3f + 0.3f * h3, t * (2 + 4 * h), colour, _smoke, FxBlend.Alpha, k % 16, 4, stretch: 1.6f);
            }
            // Its frame working on the trucks: sparks off the underframe, a few failing, a shower breaking.
            Underframe(mesh, origin, up, right, back, velocity, half, width, t, seed, breaking ? 22 : 8);
            if (fc.Spilling)
            {
                // The freight pouring out of its doors and sprung boards: dust and chaff in a stream that's left behind.
                int puffs = breaking ? 14 : 8;
                for (int k = 0; k < puffs; k++)
                {
                    float h = Hash(seed + k * 1.9f + 5), h2 = Hash(seed + k * 7.7f + 3);
                    float period = 1.8f;
                    float age = (t + h * period) % period;
                    float side = h2 > 0.5f ? 1 : -1;
                    var at = origin + right * (side * (width + 0.2f + 0.5f * age)) + up * (0.9f - 1.4f * age * age) + back * (half * 0.3f * (h - 0.5f))
                        - velocity * age;
                    float a = (1 - age / period) * 0.55f;
                    mesh.Billboard(at, 0.6f + 1.2f * age, h * 6.28f, new Vector4(0.7f, 0.6f, 0.42f, a), _smoke, FxBlend.Alpha, (int)(h2 * 15.99f), 4);
                }
            }
        }
    }

    /// <summary>A frame dragging on the rail as it comes apart: hot sparks off the underframe at both ends, streaming back.</summary>
    void Underframe(MeshBuilder mesh, Vector3 origin, Vector3 up, Vector3 right, Vector3 back, Vector3 velocity, float half, float width, float t,
        float seed, int count)
    {
        for (int k = 0; k < count; k++)
        {
            float h = Hash(seed + k * 2.9f + 9), h2 = Hash(seed + k * 5.3f + 4), h3 = Hash(seed + k * 8.1f + 6);
            float life = 0.35f + 0.2f * h3;
            float age = (t + h * life) % life;
            var from = origin + back * (half * (h2 > 0.5f ? 0.8f : -0.8f)) + right * (width * (2 * h - 1)) + up * 0.25f;
            var at = from - velocity * (0.8f * age) + up * (1.5f * age - 4.9f * age * age);
            mesh.Billboard(at, 0.25f + 0.12f * h, 0, new Vector4(2.4f, 1.2f + 0.6f * h3, 0.3f, 1 - age / life), _spark, FxBlend.Additive, k % 4, 2, stretch: 2.5f);
        }
    }
}
