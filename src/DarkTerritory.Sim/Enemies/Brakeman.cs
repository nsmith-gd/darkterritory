using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Brakeman is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum BrakemanMode : byte { Climb, Walk, Wind, Flee, Hidden, Cornered }

/// <summary>
/// THE BRAKEMAN · sight · flank (GDD §21, App. A.4, B.4; ARCHITECTURE §8 note 364; the director's brief of 8 Oct 2026,
/// docs/design/creatures/brakeman.md). A dead railwayman still doing his job: up at one end of a moving train, he walks the
/// roofs toward the other end car by car, winding each car's handbrake on (a wound car's own handbrake drags the train:
/// on a climb it stalls you). One crewmate on the roofs can chase him off: he runs from them along the roofs, faster than a
/// roof run, ducking their blows; out of their sight he drops over the side, hides under or in the train a while, and comes
/// up somewhere else to start again. Only crewmates closing on him from both sides corner him, and only cornered can he be
/// hurt; cornered he lashes with his chain (a hurt, never a kill). Never with a crew of one. Rule: chase it alone, catch it
/// together.
/// </summary>
/// <remarks>
/// He rides the cars' frames (<see cref="Enemy.Attached"/>, <see cref="Enemy.Local"/> on the roof), moving in the train's
/// own length: <see cref="Enemy.Extra"/> is how far back he is from the engine's front (metres; the train moves under him
/// and he with it), <see cref="Enemy.Extra2"/> the way
/// he's working (+1 toward the engine, −1 toward the tail), <see cref="Enemy.Lateral"/> his heading (a yaw in the car's
/// frame), <see cref="Enemy.Height"/> his <see cref="BrakemanMode"/>.
/// </remarks>
public sealed class Brakeman(int id) : Enemy(id)
{
    double _modeSeconds, _hideFor, _wound;
    int _hides;

    public override EnemyKind Kind => EnemyKind.Brakeman;
    public override PressureZone Zone => PressureZone.Flank;
    public override Sense Sense => Sense.Sight;
    /// <summary>He stops the train: what stalls it on a climb leaves it standing in the dark.</summary>
    public override Want Want => Want.Split;
    /// <summary>A blow can reach him on the roofs; it lands only when he's cornered (<see cref="Struck"/>).</summary>
    public override double MeleeRadius => Mode == BrakemanMode.Hidden ? 0 : 1.2;
    public override bool Exposed => !Gone && Mode != BrakemanMode.Hidden;
    /// <summary>He's the train's until he's killed: hidden, he's still aboard.</summary>
    public override bool StaysAboard => true;

    public BrakemanMode Mode => (BrakemanMode)(int)Height;
    /// <summary>Metres back from the engine's front.</summary>
    public double Back => Extra;
    public int Way => Extra2 >= 0 ? 1 : -1;

    /// <summary>Up the end ladder of the car at <paramref name="along"/>'s end, to work the other way (+1 toward the engine).</summary>
    public static Brakeman Up(int id, TrainOnLine train, double along, int way, BrakemanTuning t)
    {
        var b = new Brakeman(id) { Extra = Span(train).Head - along, Extra2 = way, Health = t.Health };
        b.Place(train);
        b.SetMode(BrakemanMode.Climb);
        return b;
    }

    /// <summary>Climbing up at the end of the train furthest from the crew on the roofs (or the tail), to work toward the other.</summary>
    public static Brakeman AtFarEnd(int id, TrainOnLine train, IEnumerable<(int Id, PlayerState State)> crew, BrakemanTuning t)
    {
        var (head, tail) = Span(train);
        var roofs = Roofs(train, crew).ToList();
        double where = roofs.Count > 0 ? roofs.Average(r => r.Along) : head;
        bool atTail = Math.Abs(where - tail) >= Math.Abs(where - head);
        return Up(id, train, atTail ? tail + 0.5 : head - 0.5, atTail ? 1 : -1, t);
    }

    void SetMode(BrakemanMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Brakeman;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        // Off the train (staged loose by a test or an audit): nothing of his to work.
        if (Attached < 0)
            return;
        var (head, tail) = Span(train);
        if (head - tail < 1)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }

        if (Mode == BrakemanMode.Hidden)
        {
            _hideFor -= dt;
            if (_hideFor <= 0)
                ComeUpElsewhere(ctx, t, head, tail);
            return;
        }
        if (Mode == BrakemanMode.Climb)
        {
            if (_modeSeconds >= t.ClimbSeconds)
                SetMode(BrakemanMode.Walk);
            Place(train);
            return;
        }

        var roofs = Roofs(train, ctx.Crew.Select(c => ((int)c.Player.Id, c.Player.State))).ToList();
        double me = head - Extra;
        // Crewmates up top within reach on each side of him: two closing from both sides corner him.
        var ahead = roofs.Where(r => r.Along > me && r.Along - me <= t.CornerSpan).ToList();
        var behind = roofs.Where(r => r.Along < me && me - r.Along <= t.CornerSpan).ToList();
        if (ahead.Count > 0 && behind.Count > 0)
        {
            Cornered(ctx, t, roofs);
            return;
        }
        if (Mode == BrakemanMode.Cornered)
        {
            // Let out: he runs again.
            Enter(ctx, SpinePhase.Alert);
            SetMode(BrakemanMode.Flee);
        }

        // Someone up top he can see within spook: he runs from the nearest of them, along the roofs.
        var chasers = roofs.Where(r => Math.Abs(r.Along - me) <= t.Spook).OrderBy(r => Math.Abs(r.Along - me)).ThenBy(r => r.Id).ToList();
        if (chasers.Count > 0)
        {
            var chaser = chasers[0];
            SetMode(BrakemanMode.Flee);
            if (Phase == SpinePhase.Dormant)
                Enter(ctx, SpinePhase.Alert);
            int away = me >= chaser.Along ? 1 : -1;
            double next = me + away * t.Flee * dt;
            // At the end of the train, or clear away: over the side, out of sight.
            if (next >= head || next <= tail)
            {
                Hide(ctx, t);
                return;
            }
            Extra = head - next;
            Place(train, away);
            return;
        }
        if (Mode == BrakemanMode.Flee)
        {
            // Out of their reach: clear of them all by lose, he drops out of sight to come up elsewhere.
            if (roofs.Count == 0 || roofs.All(r => Math.Abs(r.Along - me) >= t.Lose))
            {
                Hide(ctx, t);
                return;
            }
            SetMode(BrakemanMode.Walk);
        }
        if (Phase == SpinePhase.Alert)
            Enter(ctx, SpinePhase.Dormant);

        // At work: the next car along his way whose brake isn't wound, its wheel; there, he winds it on.
        var wheel = NextWheel(train, me, Way);
        if (wheel is not { } w)
        {
            // Done to the end: down off it, and up again elsewhere.
            Hide(ctx, t);
            return;
        }
        double to = w.Along - me;
        if (Math.Abs(to) > 0.2)
        {
            SetMode(BrakemanMode.Walk);
            _wound = 0;
            Extra = head - (me + Math.Sign(to) * Math.Min(Math.Abs(to), t.Walk * dt));
            Place(train, Math.Sign(to));
            return;
        }
        SetMode(BrakemanMode.Wind);
        _wound += dt;
        Place(train, Way);
        if (_wound >= t.WindSeconds)
        {
            if (ctx.World.Authority)
                train.Vehicles[w.Car].Wound = true;
            _wound = 0;
        }
    }

    /// <summary>Cornered: he backs, his chain up (the telegraph), and lashes the nearer of them every <c>lashEvery</c>.</summary>
    void Cornered(EnemyContext ctx, BrakemanTuning t, List<(int Id, double Along)> roofs)
    {
        if (Mode != BrakemanMode.Cornered)
        {
            SetMode(BrakemanMode.Cornered);
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        if (Phase != SpinePhase.Telegraph)
            Enter(ctx, SpinePhase.Telegraph);
        if (PhaseSeconds < t.LashEvery)
            return;
        double me = Span(ctx.Train).Head - Extra;
        var nearest = roofs.OrderBy(r => Math.Abs(r.Along - me)).ThenBy(r => r.Id).First();
        if (Math.Abs(nearest.Along - me) <= t.LashReach && Enter(ctx, SpinePhase.Commit))
        {
            ctx.Bite(nearest.Id, t.LashDamage, DeathCause.Taken);
            Enter(ctx, SpinePhase.Telegraph);
        }
    }

    void Hide(EnemyContext ctx, BrakemanTuning t)
    {
        SetMode(BrakemanMode.Hidden);
        _wound = 0;
        // Out of sight a while (by its own count of hides and its id: the same on every machine).
        double u = (Hash((uint)Id * 7919u + (uint)_hides++) & 0xFFFF) / 65535.0;
        _hideFor = t.Hide[0] + u * (t.Hide[1] - t.Hide[0]);
        if (Phase != SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Dormant);
    }

    /// <summary>Up again at the end of the train furthest from the crew on the roofs, to work back toward them.</summary>
    void ComeUpElsewhere(EnemyContext ctx, BrakemanTuning t, double head, double tail)
    {
        var roofs = Roofs(ctx.Train, ctx.Crew.Select(c => ((int)c.Player.Id, c.Player.State))).ToList();
        double crew = roofs.Count > 0 ? roofs.Average(r => r.Along) : (head + tail) / 2;
        bool atTail = Math.Abs(crew - tail) >= Math.Abs(crew - head);
        Extra = head - (atTail ? tail + 0.5 : head - 0.5);
        Extra2 = atTail ? 1 : -1;
        Place(ctx.Train);
        SetMode(BrakemanMode.Climb);
    }

    /// <summary>
    /// Struck: running, he ducks it (a lone chaser never lands a blow); cornered, it lands (health in blows), and the last
    /// pitches him off the roof, dead for the night.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        Marked(ctx, by);
        if (Mode != BrakemanMode.Cornered)
            return;
        Health -= damage;
        if (Health <= 0)
            Slay(ctx);
    }

    /// <summary>Where he stands, from where he is along the train: on the roof of the car there (in a gap, over the nearer end).</summary>
    void Place(TrainOnLine train, int facing = 0)
    {
        double along = Span(train).Head - Extra;
        int car = CarAt(train, along);
        var pose = train.Cars[car];
        double l = pose.Length / 2;
        double z = Math.Clamp(pose.FrontDistance - along - l, -l, l);
        var shape = train.Frames[car].Shape;
        double y = shape.TopAt(0, z)?.Top ?? shape.RoofHeight;
        Attached = car;
        Local = new Double3(0, y, z);
        if (facing != 0)
            Lateral = facing > 0 ? 0 : Math.PI;
    }

    /// <summary>The engine's rake's cars, the engine's front to the tail, as distances along the line.</summary>
    static (double Head, double Tail) Span(TrainOnLine train)
    {
        var cars = Cars(train);
        if (cars.Count == 0)
            return (0, 0);
        var first = train.Cars[cars[0]];
        var last = train.Cars[cars[^1]];
        return (first.FrontDistance, last.FrontDistance - last.Length);
    }

    /// <summary>The cars he works (the engine's rake, bar the engine): their indices, front to back.</summary>
    static List<int> Cars(TrainOnLine train) =>
        [.. train.Dynamics.Consist.Vehicles.Select(v => v.Id).Where(id => id > 0 && id < train.Cars.Count)];

    static int CarAt(TrainOnLine train, double along)
    {
        var cars = Cars(train);
        int best = cars.Count > 0 ? cars[0] : 1;
        double gap = double.MaxValue;
        foreach (int c in cars)
        {
            var p = train.Cars[c];
            double d = along > p.FrontDistance ? along - p.FrontDistance : along < p.FrontDistance - p.Length ? p.FrontDistance - p.Length - along : 0;
            if (d < gap)
            {
                gap = d;
                best = c;
            }
        }
        return best;
    }

    /// <summary>The next car's brake wheel along <paramref name="way"/> from <paramref name="along"/> whose brake isn't wound, or none.</summary>
    static (int Car, double Along)? NextWheel(TrainOnLine train, double along, int way)
    {
        (int, double)? best = null;
        double bestGap = double.MaxValue;
        foreach (int c in Cars(train))
        {
            if (train.Vehicles[c].Wound)
                continue;
            var shape = train.Frames[c].Shape;
            var wheel = shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Handbrake);
            if (wheel.Kind != InteractableKind.Handbrake)
                continue;
            var pose = train.Cars[c];
            double at = pose.FrontDistance - wheel.Position.Z - pose.Length / 2;
            double gap = (at - along) * way;
            if (gap < -0.25 || gap >= bestGap)
                continue;
            bestGap = gap;
            best = (c, at);
        }
        return best;
    }

    /// <summary>The living crew up on the roofs (of the engine's rake), where they are along the train.</summary>
    static IEnumerable<(int Id, double Along)> Roofs(TrainOnLine train, IEnumerable<(int Id, PlayerState State)> crew)
    {
        foreach (var (id, s) in crew)
            if (s.Alive && s.Surface == Surface.Roof && s.Parent > 0 && s.Parent < train.Cars.Count)
            {
                var pose = train.Cars[s.Parent];
                yield return (id, pose.FrontDistance - s.Position.Z - pose.Length / 2);
            }
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
