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
public sealed partial class Effects(Look look)
{
    readonly int _smoke = look.Layer("fx_smoke"), _steam = look.Layer("fx_steam"), _spark = look.Layer("fx_spark"), _fog = look.Layer("fx_fog"),
        _flame = look.Layer("fx_flame"), _flash = look.Layer("fx_flash");

    /// <summary>Whether the look has the flipbooks the art pass draws its fire with (the greybox draws boxes without).</summary>
    public bool HasFlames => _flame >= 0 && _smoke >= 0;

    static float Hash(float x) => Frac(MathF.Sin(x * 12.9898f) * 43758.5453f);
    static float Frac(float x) => x - MathF.Floor(x);

    static Vector3 F(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>
    /// The engine's: smoke and cinders from the stack, steam from the cylinder cocks when she's working slow, sparks at
    /// the brake shoes under a hard brake, and the headlamp's beam and halo.
    /// </summary>
    /// <param name="vent">Someone's holding the blow-off open on the running board (T101): a roaring white jet out sideways,
    /// seen from the cab.</param>
    /// <param name="safety">The safety valve's lifting: a column of steam straight up off the boiler.</param>
    /// <param name="whistle">The whistle's blowing (a crewmate on the cord, or the Whistler on it): a hard white jet straight up
    /// off the whistle on the boiler's top, ahead of the cab, rolling back over the roof with the going.</param>
    /// <param name="tailBite">What a Car Hugger's eaten of the last car (Art/BiteKit): its tail lamp goes with its corner.</param>
    public void Train(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye, double time, TrainControls controls, float fire, bool emergency,
        bool vent = false, bool safety = false, Bite tailBite = default, bool whistle = false)
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

        // The blow-off (T101): out sideways from the valve on the running board, fast and white, rising as it slows.
        if (vent)
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Vent))
            {
                // T109: the valve's in the cab now; its blow-off pipe goes up through the cab roof, and the steam out of it.
                var at = engine.ToWorld(i.Position with { Y = shape.Bounds.Max.Y + 0.15 }).RelativeTo(eye);
                float outward = Math.Sign((float)i.Position.X);
                for (int k = 0; k < 26; k++)
                {
                    float h = Hash(k * 5.31f);
                    float period = 0.7f + h * 0.5f;
                    float age = (float)((time * 1.3 + h * 7) % period);
                    float t = age / period;
                    var p = at + right * outward * (0.3f + age * 6f) + up * (age * age * 3f) + back * ((h - 0.5f) * 0.6f) - velocity * age;
                    mesh.Billboard(p, 0.35f + t * 2.6f, h * 6.28f, new Vector4(0.62f, 0.63f, 0.66f, 0.7f * (1 - t)), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
                }
            }
        // The whistle: a thin hard jet up off it, opening into a plume and laid back over the cab by the train's going.
        if (whistle && shape.Cab is { } wcab)
        {
            var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
            var at = engine.ToWorld(new Double3(0.25, boiler.Max.Y + 0.45, wcab.Min.Z - 0.4)).RelativeTo(eye);
            for (int k = 0; k < 22; k++)
            {
                float h = Hash(k * 4.43f);
                float period = 0.6f + h * 0.4f;
                float age = (float)((time * 1.4 + h * 3) % period);
                float t = age / period;
                var p = at + up * (age * 9f - age * age * 3f) + (right * (h - 0.5f) + back * (Hash(k + 0.3f) - 0.5f)) * age * 0.8f - velocity * age;
                mesh.Billboard(p, 0.25f + t * 2.6f, h * 6.28f, new Vector4(0.78f, 0.79f, 0.82f, 0.9f * (1 - t)), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
            }
        }
        // The safety valve lifting: straight up off the boiler ahead of the cab.
        if (safety && shape.Cab is { } cab)
        {
            var boiler = shape.Solids.First(s => s.Part == PartKind.Boiler).Box;
            var at = engine.ToWorld(new Double3(0, boiler.Max.Y + 0.35, cab.Min.Z - 1.2)).RelativeTo(eye);
            for (int k = 0; k < 18; k++)
            {
                float h = Hash(k * 2.17f);
                float period = 1.0f + h * 0.6f;
                float age = (float)((time + h * 5) % period);
                float t = age / period;
                var p = at + up * (age * 5f) + (right * (h - 0.5f) + back * (Hash(k + 0.7f) - 0.5f)) * age - velocity * age;
                mesh.Billboard(p, 0.3f + t * 2.0f, h * 6.28f, new Vector4(0.6f, 0.61f, 0.64f, 0.6f * (1 - t)), _steam, FxBlend.Alpha, (int)(t * 15.99f), 4);
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
        // The halo is glare seen from afar; close to, it would hide the lamp it's round, so it fades in with distance.
        float glare = Math.Clamp((at0.Length() - 4) / 16, 0, 1);
        mesh.Billboard(at0 - lamp.LampDirection * 0.2f, 2.6f, 0, new Vector4(lamp.LampColour * 0.5f * glare, 1), -1, FxBlend.Additive);
        mesh.Billboard(at0 - lamp.LampDirection * 0.25f, 0.9f, 0, new Vector4(lamp.LampColour * (0.3f + 0.7f * glare), 1), -1, FxBlend.Additive);
        Beam(mesh, at0, lamp.LampDirection, lamp.LampConeDegrees * 0.8f, 40, lamp.LampColour * 0.07f);
        // The tail lamp's glow at the back of the train.
        var last = frames[^1];
        var corner = new Double3(-last.Shape.HalfWidth + 0.25, last.Shape.RoofHeight - 0.3, last.Shape.HalfLength + 0.15);
        if (tailBite.Eats(new Vector3((float)corner.X, (float)corner.Y, (float)corner.Z - 0.2f)))
            return;
        var tail = last.ToWorld(corner).RelativeTo(eye);
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
            return new FxVertex(p, new(0.5f, 0.5f), new Vector4(colour, along * (0.06f + 0.94f * facing * facing * facing) * near), -2);
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
    /// Rain, when the night's wet: streaks in a box of world cells round the eye (so they fall past you rather than moving
    /// with you), slanted by the wind, grey where the night lights them and gone into the fog.
    /// </summary>
    readonly List<(Vector3 At, float Born, float Size)> _dust = [];
    double _wreckSeen = -1, _lastPuff;

    /// <summary>
    /// The derailment (T117): sparks where steel drags on the ground, dust thrown up behind every car that's ploughing and
    /// left hanging over the wreck after, and the wrecked engine pouring steam and smoke. The dust is kept (it lingers), so
    /// this one isn't worked out from the time alone: a screenshot of a wreck draws it as it has built up.
    /// </summary>
    /// <param name="ground">The land's height at (x, z).</param>
    public void Wreck(MeshBuilder mesh, Sim.Train.Wreck wreck, IReadOnlyList<CarFrame> frames, Double3 eye, double time, Func<double, double, double> ground)
    {
        if (_wreckSeen < 0 || time < _wreckSeen)
        {
            _wreckSeen = time;
            _dust.Clear();
        }
        float since = (float)(time - _wreckSeen);
        bool puff = time - _lastPuff > 0.06;
        if (puff)
            _lastPuff = time;
        foreach (var b in wreck.Bodies)
        {
            if (b.Vehicle >= frames.Count)
                continue;
            var f = frames[b.Vehicle];
            float speed = (float)b.Velocity.Length;
            if (speed < 1.2f)
                continue;
            var dir = F(b.Velocity) / speed;
            // Where it's on the ground: its bottom and top edges every quarter of its length.
            for (int k = 0; k <= 4; k++)
                for (int side = -1; side <= 1; side += 2)
                    foreach (double y in (ReadOnlySpan<double>)[0, b.Height])
                    {
                        var p = f.ToWorld(new Double3(side * b.HalfWidth, y, -b.HalfLength + k * b.HalfLength / 2));
                        if (p.Y - ground(p.X, p.Z) > 0.4)
                            continue;
                        var at = p.RelativeTo(eye);
                        float h = Hash(b.Vehicle * 31 + k * 7 + side + (float)y + (float)(time * 37));
                        int sparks = (int)MathF.Min(5, speed / 3);
                        for (int i = 0; i < sparks; i++)
                        {
                            float r = Hash(h * 91 + i);
                            var fly = (-dir * (0.4f + r * 1.6f) + new Vector3(Hash(r * 17) - 0.5f, 0.3f + Hash(r * 29), Hash(r * 43) - 0.5f) * 1.2f) * MathF.Min(1, speed / 10);
                            mesh.Billboard(at + fly, 0.08f + 0.06f * r, 0, new Vector4(1.0f, 0.55f + 0.3f * r, 0.15f, 1), _spark, FxBlend.Additive, i % 4, 2, stretch: 3.5f);
                        }
                        if (h > 0.85f)
                            mesh.PointLights.Add(new PointLight(at, new Vector3(1.4f, 0.75f, 0.3f) * MathF.Min(1, speed / 12), 9));
                        if (puff && _dust.Count < 500 && h < 0.35f)
                            _dust.Add((F(p) + new Vector3(0, 0.5f, 0), (float)time, 1.2f + speed * 0.08f));
                    }
        }
        // The dust: billowing, rising a little, thinning over eight seconds.
        _dust.RemoveAll(d => time - d.Born > 8);
        foreach (var (at, born, size) in _dust)
        {
            float age = (float)(time - born), t = age / 8;
            var p = new Double3(at.X, at.Y + age * 0.35f, at.Z).RelativeTo(eye);
            mesh.Billboard(p, size + age * 1.9f, Hash(born) * 6.28f + age * 0.1f, new Vector4(0.42f, 0.38f, 0.33f, 0.5f * (1 - t) * MathF.Min(1, age * 4)), _smoke, FxBlend.Alpha, (int)(t * 15.99f), 4);
        }
        // The engine's boiler gone: steam and smoke pouring up off it, for most of a minute.
        if (wreck.Bodies.Count > 0 && frames.Count > 0 && since < 50)
        {
            var e = frames[wreck.Bodies[0].Vehicle];
            var top = e.ToWorld(new Double3(0, wreck.Bodies[0].Height * 0.6, -wreck.Bodies[0].HalfLength * 0.4));
            float strength = MathF.Max(0, 1 - since / 50);
            for (int k = 0; k < 30; k++)
            {
                float life = 6, age = (float)((time * 5 + k) % (life * 5)) / 5;
                float h = Hash(k * 13.1f);
                var p = (top + new Double3((h - 0.5) * 1.5, age * (1.6 + h), (Hash(h * 7) - 0.5) * 1.5)).RelativeTo(eye);
                var colour = k % 3 == 0 ? new Vector4(0.12f, 0.11f, 0.1f, 0.55f) : new Vector4(0.62f, 0.63f, 0.66f, 0.45f);
                mesh.Billboard(p, 0.8f + age * 1.4f, h * 6.28f, colour with { W = colour.W * strength * (1 - age / life) }, k % 3 == 0 ? _smoke : _steam, FxBlend.Alpha, (int)(age / life * 15.99f), 4);
            }
        }
    }

    /// <summary>
    /// Sparks off steel being chewed (the Car Hugger feeding on its car, App. A.3: "the teeth grinding"): spat out of the
    /// mouth at <paramref name="at"/> in bursts, flying out and falling, around <paramref name="back"/> (the way out of it).
    /// </summary>
    public void Grind(MeshBuilder mesh, Vector3 at, Vector3 up, Vector3 back, double time, int seed)
    {
        var right = Vector3.Normalize(Vector3.Cross(up, back));
        // In bursts, as the jaws close on the plate.
        float burst = MathF.Max(0, MathF.Sin((float)time * 5.3f + seed)) * 0.7f + 0.3f;
        for (int k = 0; k < 30; k++)
        {
            float h = Hash(k * 3.37f + seed * 0.71f);
            float period = 0.35f + h * 0.3f;
            float age = (float)((time + h * 2) % period);
            float t = age / period;
            var fly = back * (0.6f + h * 1.4f) + right * ((Hash(k + 0.5f) - 0.5f) * 5f) + up * (2.0f + Hash(k + 1.5f) * 2.4f);
            var p = at + fly * age - up * (4.9f * age * age);
            mesh.Billboard(p, 0.45f + 0.35f * h, 0.3f, new Vector4(1.6f, 0.95f, 0.35f, burst * (1 - t * t)), _spark, FxBlend.Additive, k % 4, 2, stretch: 3f);
        }
    }

    /// <summary>
    /// Soot falling in the cab (the checklist's Stoker glow: "the firebox glows the wrong colour; soot falls in the cab"):
    /// black flakes coming down out of the roof over the footplate, turning as they fall, settling slowly. <paramref name="o"/>
    /// is the cab floor's middle, <paramref name="half"/> its half extent across (x) and along (z), <paramref name="height"/>
    /// the roof's height over the floor.
    /// </summary>
    public void SootFall(MeshBuilder mesh, Vector3 o, Vector3 right, Vector3 up, Vector3 back, Vector2 half, float height, double time, float amount)
    {
        // Flakes as tiny flat cards (geometry, not a puff of smoke: a flake's edge is hard), each tumbling as it comes down.
        float e = mesh.Emissive;
        for (int k = 0; k < 240; k++)
        {
            float h = Hash(k * 1.913f), g = Hash(k * 7.37f + 2), q = Hash(k * 3.11f + 5);
            float period = 3.5f + h * 3f;
            float age = (float)((time + h * 40) % period);
            float t = age / period;
            float sway = MathF.Sin(age * 2.3f + k) * 0.08f;
            var p = o + right * ((g * 2 - 1) * half.X + sway) + back * ((q * 1.4f - 1) * half.Y) + up * (height * (1 - t));
            float spin = age * (2.5f + h * 3);
            var a = Vector3.Normalize(right * MathF.Cos(spin) + up * MathF.Sin(spin) * 0.6f + back * 0.3f);
            var b = Vector3.Normalize(Vector3.Cross(a, up + right * 0.3f));
            var c = Vector3.Normalize(Vector3.Cross(a, b));
            float size = (0.018f + 0.02f * g) * amount;
            // Some catch the fire's light along an edge.
            mesh.Emissive = k % 6 == 0 ? 0.6f : 0;
            mesh.Box(p, a, b, c, new Vector3(size, size * 0.7f, 0.001f), k % 6 == 0 ? new Vector3(0.05f, 0.11f, 0.05f) : new Vector3(0.006f, 0.005f, 0.005f));
        }
        mesh.Emissive = e;
    }

    public void Rain(MeshBuilder mesh, Double3 eye, double time, float wind, Vector3 fogColour)
    {
        const float cell = 2.5f, height = 12, fall = 9;
        const int reach = 9;
        int cx = (int)Math.Floor(eye.X / cell), cz = (int)Math.Floor(eye.Z / cell);
        var slant = new Vector3(0.6f, 0, 0.35f) * wind;
        for (int x = -reach; x <= reach; x++)
            for (int z = -reach; z <= reach; z++)
            {
                int wx = cx + x, wz = cz + z;
                for (int k = 0; k < 2; k++)
                {
                    float h = Hash(wx * 12.9898f + wz * 78.233f + k * 3.1f);
                    float h2 = Hash(wx * 3.7f + wz * 9.1f + k * 7.3f);
                    float drop = (float)((time * fall + h * height) % height);
                    var world = new Double3((wx + h) * cell, eye.Y + height * 0.55 - drop, (wz + h2) * cell);
                    var p = world.RelativeTo(eye) + slant * (height * 0.55f - drop) * 0.15f;
                    float d = p.Length();
                    if (d < 0.6f || d > reach * cell)
                        continue;
                    // Drops right at the lens would be white bars across the frame: they fade in from 3 m.
                    float a = 0.22f * (1 - d / (reach * cell)) * (0.6f + 0.4f * h2) * Math.Clamp((d - 1.5f) / 2.5f, 0, 1);
                    mesh.Billboard(p, 0.03f, 0.06f * wind, new Vector4(fogColour * 2.2f + new Vector3(0.04f), a), -1, FxBlend.Alpha, stretch: 20);
                }
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
