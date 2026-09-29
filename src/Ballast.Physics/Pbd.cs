using Ballast;

namespace Ballast.Physics;

/// <summary>A point mass. Velocity is implicit (Position − Previous), which is what makes PBD stable.</summary>
public struct Particle(Double3 position, double inverseMass, double radius)
{
    public Double3 Position = position;
    public Double3 Previous = position;
    public double InverseMass = inverseMass;
    public double Radius = radius;
    /// <summary>Touched something this step (for sleep and landing).</summary>
    public bool Contact;

    public readonly Double3 Velocity(double dt) => (Position - Previous) * (1 / dt);
    public void SetVelocity(Double3 v, double dt) => Previous = Position - v * dt;
}

/// <summary>Keeps two particles a set distance apart (a bone, a crate's diagonal).</summary>
public readonly record struct DistanceConstraint(int A, int B, double Length, double Stiffness = 1);

/// <summary>Where a particle hits something solid: the corrected position and the surface normal.</summary>
public readonly record struct Contact(Double3 Position, Double3 Normal);

/// <summary>
/// A body's particles and constraints, stepped by position-based dynamics (Müller et al. 2007): predict
/// with gravity, then iterate constraints and collisions, then derive velocity. It's cheap, unconditionally
/// stable, and the whole state is positions, so it replicates and quantises like everything else.
/// The caller supplies collision in whatever frame the body lives in.
/// </summary>
public sealed class PbdBody
{
    public Particle[] Particles { get; }
    public DistanceConstraint[] Constraints { get; }
    /// <summary>Tangential velocity kept per step while touching something (0 = sticks, 1 = ice).</summary>
    public double Friction { get; set; } = 0.35;
    /// <summary>Normal velocity kept on impact.</summary>
    public double Bounce { get; set; } = 0.15;
    public double LinearDamping { get; set; } = 0.002;
    public int Iterations { get; set; } = 6;
    public bool Asleep { get; private set; }
    int _still;

    public PbdBody(Particle[] particles, DistanceConstraint[]? constraints = null)
    {
        Particles = particles;
        Constraints = constraints ?? [];
    }

    public Double3 Centre
    {
        get
        {
            var sum = Double3.Zero;
            foreach (var p in Particles)
                sum += p.Position;
            return sum * (1.0 / Particles.Length);
        }
    }

    /// <summary>Puts it to sleep as it is: a mirror adopting the simulating side's word that it's at rest.</summary>
    public void Sleep() => Asleep = true;

    public void Wake()
    {
        Asleep = false;
        _still = 0;
    }

    /// <summary>
    /// One step. <paramref name="collide"/> takes a position and radius and returns contacts to resolve
    /// (surfaces it's inside or touching). <paramref name="gravity"/> is in the body's frame.
    /// </summary>
    public void Step(double dt, Double3 gravity, Func<Double3, double, Contact?> collide)
    {
        if (Asleep)
            return;
        var ps = Particles;
        for (int i = 0; i < ps.Length; i++)
        {
            ref var p = ref ps[i];
            if (p.InverseMass <= 0)
                continue;
            var v = (p.Position - p.Previous) * (1 - LinearDamping);
            p.Previous = p.Position;
            p.Position += v + gravity * (dt * dt);
            p.Contact = false;
        }
        for (int it = 0; it < Iterations; it++)
        {
            foreach (var c in Constraints)
                Solve(c);
            for (int i = 0; i < ps.Length; i++)
            {
                ref var p = ref ps[i];
                if (collide(p.Position, p.Radius) is not { } hit)
                    continue;
                p.Position = hit.Position;
                p.Contact = true;
                // Friction and restitution act on the implicit velocity, via Previous.
                var v = p.Position - p.Previous;
                double vn = Double3.Dot(v, hit.Normal);
                var normal = hit.Normal * vn;
                var tangent = v - normal;
                var reflected = vn < 0 ? normal * -Bounce : normal;
                p.Previous = p.Position - (tangent * Friction + reflected);
            }
        }

        // Sleep once everything has been touching and nearly still for half a second: resting cargo costs nothing.
        double fastest = 0;
        bool touching = true;
        foreach (var p in ps)
        {
            fastest = Math.Max(fastest, (p.Position - p.Previous).Length / dt);
            touching &= p.Contact || p.InverseMass <= 0;
        }
        _still = touching && fastest < 0.05 ? _still + 1 : 0;
        if (_still > 15)
        {
            Asleep = true;
            for (int i = 0; i < ps.Length; i++)
                ps[i].Previous = ps[i].Position;
        }
    }

    void Solve(in DistanceConstraint c)
    {
        ref var a = ref Particles[c.A];
        ref var b = ref Particles[c.B];
        double w = a.InverseMass + b.InverseMass;
        if (w <= 0)
            return;
        var d = b.Position - a.Position;
        double len = d.Length;
        if (len < 1e-9)
            return;
        var correction = d * ((len - c.Length) / (len * w) * c.Stiffness);
        a.Position += correction * a.InverseMass;
        b.Position -= correction * b.InverseMass;
    }

    /// <summary>Moves every particle (and its history, so velocity is kept) by the same transform: a change of frame.</summary>
    public void Transform(Func<Double3, Double3> point, Func<Double3, Double3> velocityMap, double dt)
    {
        for (int i = 0; i < Particles.Length; i++)
        {
            ref var p = ref Particles[i];
            var v = velocityMap(p.Velocity(dt));
            p.Position = point(p.Position);
            p.SetVelocity(v, dt);
        }
    }
}

/// <summary>Sphere against an oriented-free box (both in the same frame): the push-out, if touching.</summary>
public static class Collide
{
    public static Contact? SphereBox(Double3 centre, double radius, Double3 min, Double3 max)
    {
        var closest = new Double3(Math.Clamp(centre.X, min.X, max.X), Math.Clamp(centre.Y, min.Y, max.Y), Math.Clamp(centre.Z, min.Z, max.Z));
        var d = centre - closest;
        double dist = d.Length;
        if (dist > radius)
            return null;
        if (dist > 1e-9)
        {
            var n = d * (1 / dist);
            return new Contact(closest + n * radius, n);
        }
        // Centre inside the box: out through the nearest face.
        double[] depth = [centre.X - min.X, max.X - centre.X, centre.Y - min.Y, max.Y - centre.Y, centre.Z - min.Z, max.Z - centre.Z];
        int face = Array.IndexOf(depth, depth.Min());
        return face switch
        {
            0 => new Contact(centre with { X = min.X - radius }, new Double3(-1, 0, 0)),
            1 => new Contact(centre with { X = max.X + radius }, new Double3(1, 0, 0)),
            2 => new Contact(centre with { Y = min.Y - radius }, new Double3(0, -1, 0)),
            3 => new Contact(centre with { Y = max.Y + radius }, new Double3(0, 1, 0)),
            4 => new Contact(centre with { Z = min.Z - radius }, new Double3(0, 0, -1)),
            _ => new Contact(centre with { Z = max.Z + radius }, new Double3(0, 0, 1)),
        };
    }
}
