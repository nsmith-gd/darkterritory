using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>The callouts' colour: the lamp's amber run hot, the one colour on the train that means "mend me here".</summary>
    static readonly Vector3 CalloutAmber = new(1.0f, 0.62f, 0.18f);

    /// <summary>
    /// Every break the crew can mend, called out where it is (note 301, queue #39: "every repairable breakage has a clear VFX
    /// callout"): a hot amber glow that beats about once a second at the break, a wider halo round it, and torn metal
    /// spitting sparks off it in short bursts. One language for every break (the burst boiler's fire door, a breach's hole, a
    /// battered car's dent), so a crew learns it once and finds the next by it. With <paramref name="mending"/>, someone's
    /// wrench is at it: a shower off each strike instead of the slow spit. Worked out from the time alone, like every effect.
    /// </summary>
    public void Repairs(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye, double time, IReadOnlyList<BreakCallout> breaks,
        IReadOnlySet<int>? mending = null)
    {
        for (int b = 0; b < breaks.Count; b++)
        {
            var brk = breaks[b];
            CarFrame? found = null;
            foreach (var f in frames)
                if (f.Index == brk.Vehicle)
                {
                    found = f;
                    break;
                }
            if (found is not { } frame || (frame.Origin - eye).Length > 120)
                continue;
            var at = frame.ToWorld(brk.At).RelativeTo(eye);
            var up = F(frame.Up);
            var right = F(frame.Right);
            // Out of the wall into the room: a dent's on the left wall, a breach's hole in the end wall or the roof, the fire
            // door faces back into the cab.
            var outward = brk.Kind switch
            {
                BreakKind.Dent => right,
                BreakKind.Breach when frame.Shape.Interior is { } room && brk.At.Y >= room.Max.Y - 0.05 => -up,
                _ => -F(frame.Back),
            };
            float seed = brk.Vehicle * 7.3f + (int)brk.Kind * 1.9f;
            float t = (float)time;
            float beat = 0.5f + 0.5f * MathF.Sin(t * 6.2832f * 1.1f + seed);
            var core = at + outward * 0.15f;
            // The glow and its halo: additive, so they read in the dark without lighting anything.
            mesh.Billboard(core, 0.7f + 0.3f * beat, 0, new Vector4(CalloutAmber * 2.2f, 0.7f + 0.3f * beat), _flash, FxBlend.Additive, 0, 2);
            mesh.Billboard(core, 2.0f + 0.6f * beat, 0, new Vector4(CalloutAmber, 0.35f + 0.25f * beat), _flash, FxBlend.Additive, 0, 2);
            // A ring that swells out of it each beat, so it catches the eye at the edge of the view.
            float swell = (t * 1.1f + seed / 6.2832f) % 1;
            mesh.Billboard(core, 0.5f + 2.2f * swell, 0, new Vector4(CalloutAmber * 1.5f, 0.6f * (1 - swell) * (1 - swell)), _flash, FxBlend.Additive, 0, 2);
            bool worked = mending?.Contains(b) == true;
            // Sparks off the torn edge: a burst every so often (every strike, while someone's at it), each spark out along
            // the wall's normal and splayed, falling as it goes.
            // Idle, a thin steady spit (each spark on its own clock), so a still frame always shows some; worked, a burst off
            // every strike.
            float period = worked ? 0.45f : 0.7f;
            int count = worked ? 16 : 9;
            for (int k = 0; k < count; k++)
            {
                float h = Hash(seed + k * 3.11f), h2 = Hash(seed + k * 5.37f + 1.3f), h3 = Hash(seed + k * 8.9f + 2.7f);
                float age = (t + (worked ? h * 0.12f : h * period)) % period;
                float life = worked ? 0.4f : 0.5f + 0.2f * h3;
                if (age > life)
                    continue;
                var splay = right * ((h - 0.5f) * 1.6f) + up * ((h2 - 0.3f) * 1.4f) + F(frame.Back) * ((h3 - 0.5f) * 1.2f);
                var dir = Vector3.Normalize(outward * 1.2f + splay);
                float speed = (worked ? 3.2f : 2.2f) * (0.6f + 0.6f * h2);
                var p = core + dir * (speed * age) - up * (4.9f * age * age);
                float fade = 1 - age / life;
                mesh.Billboard(p, 0.1f + 0.06f * h, 0, new Vector4(1.0f, 0.55f + 0.35f * h3, 0.15f, fade), _spark, FxBlend.Additive, k % 4, 2, stretch: 3f);
            }
        }
    }
}
