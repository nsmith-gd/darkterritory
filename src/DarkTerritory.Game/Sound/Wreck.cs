namespace DarkTerritory.Game.Sound;

/// <summary>
/// A derailment as it's heard (the audio director, 2 Oct: "composed of many sounds of metal scraping and hitting when cars
/// collide and roll, scraping on rails when they do, metal and wood ripping apart from the force ... individual sounds that
/// trigger when the conditions to trigger them are met, so each derailment sounds different and unique to its conditions,
/// relying on what the physics creates"). The sim stops the train dead (World.Derail, GDD §23), so the physics is played
/// out here, from what every car was doing the tick before: a chain of masses along the line at their speeds, coupled
/// with slack. The first car off the rails digs into the sleepers and ploughs; the cars behind run into it and are shoved
/// off in turn while they're coming hard enough; the couplers take the draft until they tear; a car goes over on a curve,
/// or when it's slammed, and lands on its side, perhaps rolling on; anything off the rails grinds to a stop and settles.
/// Each of those is a <see cref="Happening"/> the audio plays (GameAudio.Train), so a slow derailment on a straight is a
/// few climbs and a ragged run-in, and a fast one on a curve is a pile-up.
/// <para>
/// Presentation only: nothing reads it back, and the cars don't move in the sim or on screen (the art's sparks and dust
/// are timed the same way, Art/Effects.Derailment). Deterministic in its inputs, so every machine hears the same wreck.
/// The numbers are how it sounds, not design numbers: they shape a few seconds of noise.
/// </para>
/// </summary>
public sealed class Wreck
{
    public enum Kind : byte
    {
        /// <summary>A car's wheels climbing the rail and dropping off it.</summary>
        Climb,
        /// <summary>A car slamming into the one ahead of it.</summary>
        Collide,
        /// <summary>A coupling closing up, gently (the slack running in).</summary>
        RunIn,
        /// <summary>A coupling snatched taut.</summary>
        RunOut,
        /// <summary>Iron and wood torn apart: a coupler pulled out, a body crushed in a collision, a car rolling on its roof.</summary>
        Tear,
        /// <summary>A car going over.</summary>
        Tip,
        /// <summary>A car hitting the ground: on its side, rolling, or dropped where the track went from under it.</summary>
        Impact,
        /// <summary>The wreck settling once it's stopped.</summary>
        Settle,
    }

    /// <param name="Car">The car it happened to (an index into <see cref="Cars"/>).</param>
    /// <param name="Other">The car it happened against (a collision's car ahead, a torn coupler's), or −1.</param>
    /// <param name="Strength">How hard, 0 to 1.</param>
    public readonly record struct Happening(Kind Kind, int Car, int Other, double Strength);

    /// <summary>A car as it was on the tick before the derailment.</summary>
    /// <param name="Id">Its vehicle id.</param>
    /// <param name="Mass">Tonnes.</param>
    /// <param name="Length">Front face to rear face, m.</param>
    /// <param name="Speed">Its speed along the line in the direction the train was going, m/s.</param>
    /// <param name="Lateral">How hard its curve was pulling it outwards, over what derails a train there (v²k / a_derail).</param>
    /// <param name="Top">How readily it goes over, about 1: the engine low and heavy, a full car higher.</param>
    /// <param name="Drop">How far it falls if the track's gone from under it (a washout, a span giving way), m; 0 none.</param>
    /// <param name="CoupledAhead">Coupled to the car ahead of it.</param>
    public sealed record Start(int Id, double Mass, double Length, double Speed, double Lateral = 0, double Top = 1, double Drop = 0, bool CoupledAhead = true);

    public sealed class Car
    {
        public required Start From { get; init; }
        /// <summary>Its centre along the direction of travel (m) and its speed that way (m/s, never backwards).</summary>
        public double X, V;
        public bool Off, Going, OnSide, Falling, Stopped;
        public bool CoupledAhead;
        internal double LandsAt = double.PositiveInfinity, Plough, Slide;
        internal int Rolls;
        internal bool Settled;

        /// <summary>Off the rails and upright, its trucks dragging over the rail and the sleepers.</summary>
        public bool Scraping => Off && !Going && !OnSide && !Falling && V > 0.5;
        /// <summary>On its side, grinding along the ballast.</summary>
        public bool Grinding => OnSide && V > 0.3;
    }

    const double G = 9.81, Slack = 0.12, Step = 1.0 / 240;
    // The car off the rails digs in, losing this share of its speed at once (at least, and at most; and never more than
    // DigInMost m/s), so a slow derailment stops where it is and a fast one piles the train up behind it; a car shoved harder
    // than this leaves the rails, and one hit harder than this tears; a coupler snatched apart faster than this pulls out.
    const double DigIn = 0.08, DigInMore = 0.2, DigInMost = 3.5, ShovedOff = 1.6, Crushed = 4.5, CouplerPulls = 2.4;

    readonly ulong _seed;
    readonly double _gap, _brake;
    readonly List<(double At, int Car, double Strength)> _settles = [];
    readonly bool[] _touching, _taut;
    readonly double[] _lastHeard;
    double _acc;

    /// <param name="cars">The cars front to back in the direction they were going.</param>
    /// <param name="origin">The one that comes off first.</param>
    /// <param name="couplingGap">Face to face between coupled cars at rest (train.json geometry), m.</param>
    /// <param name="seed">Varies what's left to chance (how a car digs in, which way it rolls): the same seed, the same wreck.</param>
    /// <param name="brake">What the brakes take off a car still on the rails, m/s²: the train line's parted, so they're on.</param>
    public Wreck(IReadOnlyList<Start> cars, int origin, double couplingGap, ulong seed, double brake = 2)
    {
        _seed = seed;
        _gap = couplingGap;
        _brake = brake;
        double x = 0;
        var list = new List<Car>();
        for (int i = 0; i < cars.Count; i++)
        {
            var s = cars[i];
            if (i > 0)
                x -= cars[i - 1].Length / 2 + couplingGap + s.Length / 2;
            list.Add(new Car
            {
                From = s,
                X = x,
                V = Math.Max(0, s.Speed),
                CoupledAhead = i > 0 && s.CoupledAhead,
                Plough = 0.32 + 0.2 * Hash(s.Id, 1),
                Slide = 0.55 + 0.2 * Hash(s.Id, 2),
            });
        }
        Cars = list;
        _touching = new bool[list.Count];
        _taut = new bool[list.Count];
        _lastHeard = Enumerable.Repeat(double.NegativeInfinity, list.Count).ToArray();
        Origin = Math.Clamp(origin, 0, Math.Max(0, list.Count - 1));
    }

    public IReadOnlyList<Car> Cars { get; }
    public int Origin { get; }
    /// <summary>Seconds since it came off.</summary>
    public double Time { get; private set; }
    /// <summary>Everything has stopped and settled.</summary>
    public bool Done { get; private set; }

    /// <summary>Plays the wreck on by <paramref name="dt"/> seconds, adding what happened to <paramref name="into"/>.</summary>
    public void Advance(double dt, List<Happening> into)
    {
        if (Cars.Count == 0 || Done)
            return;
        if (Time == 0 && _acc == 0)
        {
            // Where the track went from under them, they all go; otherwise the one that comes off first.
            for (int i = 0; i < Cars.Count; i++)
                if (Cars[i].From.Drop > 0)
                    Derail(i, 0, into);
            Derail(Origin, 0, into);
        }
        _acc += dt;
        while (_acc >= Step)
        {
            _acc -= Step;
            Time += Step;
            Tick(into);
        }
        Done = Time > 1 && _settles.Count == 0 && Cars.All(c => c.Stopped && !c.Going && !c.Falling);
    }

    void Tick(List<Happening> into)
    {
        bool anyOff = Cars.Any(c => c.Off);
        foreach (var c in Cars)
        {
            double decel = c.Falling ? 0 : c.OnSide || c.Going ? c.Slide * G : c.Off ? c.Plough * G : anyOff ? _brake : 0;
            c.V = Math.Max(0, c.V - decel * Step);
            c.X += c.V * Step;
        }
        for (int i = 1; i < Cars.Count; i++)
            Couple(i, into);
        for (int i = 0; i < Cars.Count; i++)
        {
            var c = Cars[i];
            if (c.Going && Time >= c.LandsAt)
                Land(i, into);
            else if (c.Falling && Time >= c.LandsAt)
            {
                // Down into the washout or the river, and that's where it stays.
                c.Falling = false;
                c.OnSide = true;
                c.V = 0;
                double drop = Math.Clamp(Math.Sqrt(c.From.Mass / 40) * Math.Min(1, c.From.Drop / 6), 0.4, 1);
                into.Add(new Happening(Kind.Impact, i, -1, drop));
                into.Add(new Happening(Kind.Tear, i, -1, drop * 0.8));
            }
            bool still = c.V < 0.05 && !c.Going && !c.Falling;
            if (still && !c.Stopped)
            {
                c.V = 0;
                c.Stopped = true;
                if (c.Off && !c.Settled)
                {
                    // Wreckage settling: a few groans and shifts, further apart and quieter as it comes to rest.
                    c.Settled = true;
                    int n = 1 + (int)(Hash(c.From.Id, 3) * 3);
                    double at = Time + 0.3 + 0.6 * Hash(c.From.Id, 4);
                    for (int k = 0; k < n; k++)
                    {
                        _settles.Add((at, i, 0.7 * Math.Pow(0.65, k)));
                        at += 0.7 + 2.2 * Hash(c.From.Id, 5 + k);
                    }
                }
            }
            else if (!still)
                c.Stopped = false;
        }
        for (int k = _settles.Count - 1; k >= 0; k--)
            if (_settles[k].At <= Time)
            {
                into.Add(new Happening(Kind.Settle, _settles[k].Car, -1, _settles[k].Strength));
                _settles.RemoveAt(k);
            }
    }

    /// <summary>The coupling between car <paramref name="i"/> and the one ahead of it: buffing, or snatched taut.</summary>
    void Couple(int i, List<Happening> into)
    {
        Car a = Cars[i - 1], b = Cars[i];
        if (a.Falling || b.Falling)
            return;
        double gap = a.X - a.From.Length / 2 - (b.X + b.From.Length / 2);
        double ma = a.From.Mass, mb = b.From.Mass, reduced = ma * mb / (ma + mb);
        if (gap < _gap - Slack)
        {
            double closing = b.V - a.V;
            if (closing > 0)
            {
                double impulse = 1.25 * closing * reduced;
                a.V += impulse / ma;
                b.V = Math.Max(0, b.V - impulse / mb);
                if (!_touching[i])
                    Buffed(i, closing, reduced, into);
            }
            double overlap = _gap - Slack - gap;
            a.X += overlap * mb / (ma + mb);
            b.X -= overlap * ma / (ma + mb);
            _touching[i] = true;
        }
        else
            _touching[i] = gap < _gap - Slack + 0.02 && _touching[i];
        if (b.CoupledAhead && gap > _gap + Slack)
        {
            double opening = a.V - b.V;
            if (opening > 0)
            {
                double impulse = 1.15 * opening * reduced;
                a.V = Math.Max(0, a.V - impulse / ma);
                b.V += impulse / mb;
                if (!_taut[i])
                    Snatched(i, opening, into);
            }
            if (b.CoupledAhead)
            {
                double stretch = gap - _gap - Slack;
                a.X -= stretch * mb / (ma + mb);
                b.X += stretch * ma / (ma + mb);
            }
            _taut[i] = true;
        }
        else
            _taut[i] = gap > _gap + Slack - 0.02 && _taut[i];
    }

    void Buffed(int i, double closing, double reduced, List<Happening> into)
    {
        if (closing < 0.15)
            return;
        double hard = Math.Clamp(Math.Sqrt(reduced / 20) * closing / 7, 0.1, 1);
        if (closing < 0.7)
            into.Add(new Happening(Kind.RunIn, i, i - 1, hard));
        else if (Time - _lastHeard[i] > 0.1)
        {
            into.Add(new Happening(Kind.Collide, i, i - 1, hard));
            _lastHeard[i] = Time;
        }
        if (closing > Crushed)
            into.Add(new Happening(Kind.Tear, i, i - 1, Math.Clamp(closing / 9, 0.4, 1)));
        // Slammed from behind hard enough, a car rides up and off; and so does the one that hit it, kinked.
        if (closing > ShovedOff)
        {
            if (!Cars[i].Off)
                Derail(i, closing, into);
            if (!Cars[i - 1].Off && closing > ShovedOff * 1.6)
                Derail(i - 1, closing * 0.6, into);
        }
    }

    void Snatched(int i, double opening, List<Happening> into)
    {
        if (opening > CouplerPulls)
        {
            // The coupler's pulled out of the car: they part, and it's torn metal.
            Cars[i].CoupledAhead = false;
            into.Add(new Happening(Kind.Tear, i, i - 1, Math.Clamp(opening / 6, 0.4, 1)));
            return;
        }
        if (opening > 0.25)
            into.Add(new Happening(Kind.RunOut, i, i - 1, Math.Clamp(opening / CouplerPulls, 0.15, 1)));
        // A railed car on the end of a ploughing one is dragged off after it.
        var (a, b) = (Cars[i - 1], Cars[i]);
        if (opening > 1.2 && a.Off != b.Off)
            Derail(a.Off ? i : i - 1, opening * 0.5, into);
    }

    /// <summary>A car leaves the rails: it digs in, and it may go over.</summary>
    void Derail(int i, double kick, List<Happening> into)
    {
        var c = Cars[i];
        if (c.Off)
            return;
        c.Off = true;
        double speed = c.V;
        into.Add(new Happening(Kind.Climb, i, -1, Math.Clamp(speed / 15, 0.25, 1)));
        if (c.From.Drop > 0)
        {
            c.Falling = true;
            c.LandsAt = Time + Math.Sqrt(2 * c.From.Drop / G);
            return;
        }
        c.V = Math.Max(0, c.V - Math.Min(DigInMost, speed * (DigIn + (DigInMore - DigIn) * Hash(c.From.Id, 6))));
        // Over it goes if its curve was throwing it out, or it was slammed off, and it's top-heavy enough.
        double over = 0.5 * c.From.Lateral * c.From.Top + 0.12 * kick * c.From.Top + 0.35 * Hash(c.From.Id, 7) * Math.Min(1, speed / 8);
        if (over > 0.8 && speed > 3)
        {
            c.Going = true;
            c.LandsAt = Time + 0.5 + 0.3 * c.From.Top + 0.35 * Hash(c.From.Id, 8);
            into.Add(new Happening(Kind.Tip, i, -1, Math.Clamp(Math.Sqrt(c.From.Mass / 40) * speed / 12, 0.3, 1)));
        }
    }

    /// <summary>A car going over hits the ground; going fast enough, it rolls on over its roof.</summary>
    void Land(int i, List<Happening> into)
    {
        var c = Cars[i];
        double hard = Math.Clamp(Math.Sqrt(c.From.Mass / 40) * (0.45 + 0.55 * Math.Min(1, c.V / 10)) * Math.Pow(0.65, c.Rolls), 0.15, 1);
        into.Add(new Happening(Kind.Impact, i, -1, hard));
        if (c.Rolls > 0)
            into.Add(new Happening(Kind.Tear, i, -1, hard * 0.8));
        c.Rolls++;
        c.V *= 0.8;
        if (c.V > 6 && c.Rolls < 4 && Hash(c.From.Id, 9 + c.Rolls) < 0.45 * c.From.Top)
        {
            c.LandsAt = Time + 0.35 + 0.15 * Hash(c.From.Id, 13 + c.Rolls);
            return;
        }
        c.Going = false;
        c.OnSide = true;
    }

    double Hash(int id, int salt)
    {
        ulong z = _seed + 0x9E3779B97F4A7C15UL * (ulong)(id * 64 + salt + 1);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }
}
