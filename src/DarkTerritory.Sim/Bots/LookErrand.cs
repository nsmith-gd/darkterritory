using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

/// <summary>Mirror of content/tuning/balance.json <c>combinations.look</c>: the look-out's errand (note 212). Field docs live there.</summary>
public sealed record LookTuning
{
    public double WaitSeconds { get; init; } = 12;
    public double SeekM { get; init; } = 80;
    public double GauntM { get; init; } = 3;
    public double RibbitM { get; init; } = 5;
    public double MooseM { get; init; } = 10;
    public double TowerJawM { get; init; } = 20;
    public double BeetleM { get; init; } = 12;
    public double EdgeM { get; init; } = 0.6;
}

/// <summary>
/// GDD §34's combination sweep (note 212): in an insisted night only, one walker is the look-out, and goes and looks at what
/// lies in wait for a crew that never comes near it. Bots keep to their posts, and the Gaunt asleep in the yard, a Dragger
/// under a roof's lip and a Ribbit pack out on the ballast were put there and never came on (note 186's "dormant"). The
/// look-out walks over the ballast to the sleeping Gaunt, toward the pack, away from the others, or up to a grazing Moose
/// (note 339), before boarding; up on
/// the roofs, it stays out there and walks to the lip over a Dragger. It's only feet and a facing (intent, as a player's):
/// what's there wakes by its own rules, and the bot answers it with its normal counters (talks to the Gaunt, steps back from
/// the lip and swings, runs from the tongues and is hauled free). Each thing it looks at once; then it's back to its post.
/// </summary>
public sealed class LookErrand(IReadOnlyList<EnemyKind> insisted, LookTuning t)
{
    readonly HashSet<int> _looked = [];
    readonly bool _ground = insisted.Any(k => k is EnemyKind.Gaunt or EnemyKind.Ribbit or EnemyKind.Moose or EnemyKind.TowerJaw or EnemyKind.FreightBeetle);
    readonly bool _roof = insisted.Contains(EnemyKind.Dragger);
    bool _metDragger;

    /// <summary>What it's doing (the harness's trace), or null.</summary>
    public string? Doing { get; private set; }

    static bool Lying(Enemy e) => e.Phase is SpinePhase.Dormant or SpinePhase.Alert;

    /// <summary>
    /// The things it's here for: the insisted Gaunt, Ribbits and Draggers that have yet to come on. One that's come on (for
    /// anyone) is crossed off for good: a Dragger that rearms after is looked at already.
    /// </summary>
    IEnumerable<Enemy> Waiting(World world)
    {
        foreach (var e in world.ActiveEnemies)
        {
            // Gone unmet (lingered out) isn't met.
            // Tower Jaw at its post and the Freight Beetle by its freight too (note NNN): what comes to the train, or only after
            // a death (the Mourners, the Brakeman, the Knotter, Hotbox), needs nobody to go and look.
            if (e.Gone || !insisted.Contains(e.Kind) || e.Kind is not (EnemyKind.Gaunt or EnemyKind.Ribbit or EnemyKind.Dragger or EnemyKind.Moose
                    or EnemyKind.TowerJaw or EnemyKind.FreightBeetle))
                continue;
            if (!Lying(e))
            {
                _looked.Add(e.Id);
                _metDragger |= e.Kind == EnemyKind.Dragger;
            }
            else if (!_looked.Contains(e.Id))
                yield return e;
        }
    }

    /// <summary>A Dragger's insisted and not yet met: stay out on the roofs (not in a car for a bag at a crane), so it's placed and met.</summary>
    public bool KeepOut => _roof && !_metDragger;

    /// <summary>
    /// This tick's step on the errand, or null (nothing to look at from here: on as usual). <paramref name="head"/>: up on
    /// another car's roof, the car whose lip it's making for (the walker's own legs take it along the roofs).
    /// </summary>
    public PlayerIntent? Decide(in PlayerState self, World world, uint tick, out int? head)
    {
        head = null;
        Doing = null;
        if (!self.Alive || self.Has(PlayerFlags.Held))
            return null;
        var train = world.Train;
        var waiting = Waiting(world).ToList();
        if (self.Parent == PlayerState.World && self.Surface == Surface.Ground && _ground)
        {
            // Out on the ballast: over to the nearest thing asleep or waiting out there, by its kind's distance.
            var me = self.Position;
            var near = waiting.Where(e => e.Kind is EnemyKind.Gaunt or EnemyKind.Ribbit or EnemyKind.Moose or EnemyKind.TowerJaw or EnemyKind.FreightBeetle)
                .Select(e => (e, At: e.WorldPosition(train))).Select(x => (x.e, x.At, D: ((x.At - me) with { Y = 0 }).Length))
                .Where(x => x.D <= t.SeekM).OrderBy(x => x.D).ThenBy(x => x.e.Id).FirstOrDefault();
            if (near.e is not null)
            {
                Doing = $"look:{near.e.Kind}";
                double stop = near.e.Kind switch
                {
                    EnemyKind.Gaunt => t.GauntM,
                    EnemyKind.Moose => t.MooseM,
                    EnemyKind.TowerJaw => t.TowerJawM,
                    EnemyKind.FreightBeetle => t.BeetleM,
                    _ => t.RibbitM,
                };
                return Walk(self, near.At, near.D, stop);
            }
            // Nothing out there yet that it's for: it waits on the ballast a while for it (it's sent once someone's down).
            if (_looked.Count == 0 && tick < t.WaitSeconds * SimConstants.TickRate)
            {
                Doing = "look:wait";
                return new PlayerIntent();
            }
            return null;
        }
        if (_roof && self.Surface == Surface.Roof && self.Parent > 0 && self.Parent < train.Frames.Count && self.Grounded)
        {
            int on = self.Parent;
            var under = waiting.OfType<Dragger>().Where(d => d.Attached > 0 && d.Attached < train.Frames.Count)
                .OrderBy(d => Math.Abs(d.Attached - on)).ThenBy(d => d.Id).FirstOrDefault();
            if (under is null)
                return null;
            Doing = "look:Dragger";
            if (under.Attached != self.Parent)
            {
                head = under.Attached;
                return null;
            }
            // To the lip on its side, beside it along the car, facing along the car (the way it walks).
            var shape = train.Frames[under.Attached].Shape;
            var lip = new Double3(under.Side * (shape.HalfWidth - t.EdgeM), 0, under.Local.Z);
            var (step, there) = WarmUp.Steer(self, lip, 0);
            return there ? new PlayerIntent() : step;
        }
        return null;
    }

    /// <summary>
    /// Whether there's anything to look at from where it stands this tick: a step on the errand, or a lip on another car to
    /// make for (note 222: what takes a gunner off its gun). The same reading <see cref="Decide"/> makes, so it agrees with it.
    /// </summary>
    public bool Wants(in PlayerState self, World world, uint tick) => Decide(self, world, tick, out var head) is not null || head is not null;

    /// <summary>Turned to it and walking (running from far off), until <paramref name="stop"/> from it.</summary>
    static PlayerIntent Walk(in PlayerState self, Double3 at, double d, double stop)
    {
        if (d <= stop || !self.Grounded)
            return new PlayerIntent();
        double dx = at.X - self.Position.X, dz = at.Z - self.Position.Z;
        double turn = Math.IEEERemainder(DMath.Atan2(-dx, -dz) - self.Yaw, 2 * Math.PI);
        bool aligned = Math.Abs(turn) < 0.15;
        return new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.4, 0.4),
            MoveZ = aligned ? 1 : 0,
            Buttons = d > stop + 6 ? PlayerButtons.Run : PlayerButtons.None,
        };
    }
}
