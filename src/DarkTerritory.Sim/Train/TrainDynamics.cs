namespace DarkTerritory.Sim.Train;

/// <summary>Driver inputs for one tick, as set by the cab controls (or the Deadman).</summary>
public struct TrainControls
{
    /// <summary>0..1 regulator position.</summary>
    public double Throttle;
    /// <summary>0..1 brake application.</summary>
    public double Brake;
    /// <summary>+1 forward, -1 reverse.</summary>
    public int Reverser;
}

/// <summary>Track conditions under the train this tick.</summary>
public struct TrackConditions
{
    /// <summary>Grade in percent, measured in the direction the engine faces along the line.</summary>
    public double GradePercent;
    /// <summary>1 is dry rail. Grease drops it to ~0.2 (GDD A.2).</summary>
    public double Traction;

    public static TrackConditions Flat => new() { GradePercent = 0, Traction = 1 };
}

/// <summary>
/// Longitudinal train simulation along the line: speed, distance, brake fade and coal burn.
/// The train is a 1D body on the track; 3D placement of cars comes from the rail spline.
/// </summary>
public sealed class TrainDynamics
{
    public TrainDynamics(Consist consist) => Consist = consist;

    /// <summary>The vehicles in this rake. Replaced when rakes are cut or coupled.</summary>
    public Consist Consist { get; internal set; }
    public TrainTuning Tuning => Consist.Tuning;

    /// <summary>Signed speed along the line, m/s.</summary>
    public double Velocity { get; set; }
    /// <summary>Distance along the line, m. Double: routes run 18–40 km.</summary>
    public double Distance { get; set; }
    /// <summary>
    /// Which track the front is on (<see cref="Rail.RailLine.MainPath"/>, or a branch taken at its switch). Distance
    /// is along that path, which is the main line up to the branch's points.
    /// </summary>
    public int Path { get; set; } = Rail.RailLine.MainPath;
    /// <summary>1 = fresh brakes; drops while braking on descents.</summary>
    public double BrakeEfficiency { get; private set; } = 1;
    public double Acceleration { get; private set; }

    public double Speed => Math.Abs(Velocity);

    /// <summary>Adopts authoritative state from the host; clients then re-simulate forward from it.</summary>
    public void Restore(double distance, double velocity, double brakeEfficiency)
    {
        Distance = distance;
        Velocity = velocity;
        BrakeEfficiency = brakeEfficiency;
    }

    /// <summary>Tractive force in kN at full throttle for the current length.</summary>
    public double MaxTractiveForce => Consist.HasEngine ? Lookup(Tuning.Performance, r => r.Cars, r => r.Accel) * Consist.LoadedMassTonnes(Tuning, Consist.CarCount) : 0;
    /// <summary>Brake force in kN at full application for the current length, before fade.</summary>
    /// <remarks>A rake without an engine has no air brakes working; only its handbrakes, if wound on.</remarks>
    public double MaxBrakeForce => Consist.HasEngine
        ? Lookup(Tuning.Performance, r => r.Cars, r => r.Brake) * Consist.LoadedMassTonnes(Tuning, Consist.CarCount)
        : Handbrake ? Tuning.Couplings.HandbrakeDecel * Consist.MassTonnes : 0;

    /// <summary>Handbrakes wound on across a rake without an engine (parked cars, GDD §17).</summary>
    public bool Handbrake { get; set; }
    /// <summary>Front-of-rake distance at the start of the current tick, for render interpolation.</summary>
    public double PreviousDistance { get; set; }
    /// <summary>This rake's front coupler was just cut; it won't re-couple until it has pulled clear.</summary>
    public bool FrontCouplerLocked { get; set; }
    public double RearDistance => Distance - Consist.LengthMetres;

    public void Step(double dt, in TrainControls controls, in TrackConditions track)
    {
        double mass = Consist.MassTonnes; // tonnes; forces in kN give m/s²
        double throttle = Math.Clamp(controls.Throttle, 0, 1);
        double brake = Math.Clamp(controls.Brake, 0, 1);
        int reverser = controls.Reverser >= 0 ? 1 : -1;
        double traction = Math.Clamp(track.Traction, 0, 1);

        double gravityAccel = -Tuning.Gravity * Math.Sin(Math.Atan(track.GradePercent / 100.0));
        // The spec's accel and brake figures are what the train achieves, net of rolling and air
        // resistance. So full throttle overcomes resistance and full brake includes it; resistance
        // only shows on its own when coasting, which is when it matters (GDD §23: boiler dies).
        double resistance = Tuning.Resistance.Rolling + Tuning.Resistance.Air * Velocity * Velocity;
        double drive = reverser * throttle * (MaxTractiveForce * traction / mass + resistance);
        double brakeAccel = brake * Math.Max(MaxBrakeForce * BrakeEfficiency * traction / mass - resistance, 0) + resistance;

        double a = drive + gravityAccel;
        double v = Velocity;
        if (v != 0)
        {
            // Brakes oppose motion and cannot reverse it.
            double dir = Math.Sign(v);
            double next = v + (a - dir * brakeAccel) * dt;
            v = Math.Sign(next) != dir && Math.Abs(a) <= brakeAccel ? 0 : next;
        }
        else if (Math.Abs(a) > brakeAccel)
        {
            v += (a - Math.Sign(a) * brakeAccel) * dt;
        }

        v = Math.Clamp(v, -Tuning.MaxSpeed, Tuning.MaxSpeed);
        Acceleration = (v - Velocity) / dt;
        Velocity = v;
        Distance += v * dt;

        double travelDir = v != 0 ? Math.Sign(v) : reverser;
        UpdateFade(dt, brake, track.GradePercent * travelDir);
    }

    void UpdateFade(double dt, double brake, double gradeInTravelDirection)
    {
        var f = Tuning.BrakeFade;
        bool fading = brake > 0 && Speed > 0 && (!f.OnlyOnDescent || gradeInTravelDirection < 0);
        BrakeEfficiency = fading
            ? Math.Max(f.MinEfficiency, BrakeEfficiency - f.FadePerSecond * brake * dt)
            : Math.Min(1, BrakeEfficiency + f.RecoverPerSecond * dt);
    }

    /// <summary>Steepest grade (percent) this consist can hold speed on at full throttle.</summary>
    public double MaxClimbableGradePercent()
    {
        double ratio = MaxTractiveForce / Consist.MassTonnes / Tuning.Gravity;
        return ratio >= 1 ? double.PositiveInfinity : Math.Tan(Math.Asin(ratio)) * 100;
    }

    int CarCount => Consist.CarCount;

    /// <summary>Linear interpolation over a table keyed by car count, clamped at both ends.</summary>
    double Lookup<T>(IReadOnlyList<T> rows, Func<T, int> key, Func<T, double> value)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("empty tuning table");
        if (CarCount <= key(rows[0]))
            return value(rows[0]);
        for (int i = 1; i < rows.Count; i++)
        {
            if (CarCount <= key(rows[i]))
            {
                double t = (double)(CarCount - key(rows[i - 1])) / (key(rows[i]) - key(rows[i - 1]));
                return value(rows[i - 1]) + t * (value(rows[i]) - value(rows[i - 1]));
            }
        }
        return value(rows[^1]);
    }
}
