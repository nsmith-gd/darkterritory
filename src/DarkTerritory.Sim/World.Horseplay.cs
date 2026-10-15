using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// Horseplay, the host's side (GDD §23.1; ARCHITECTURE §8 note 717; the director, 14 Oct 2026: "The griefer ideas are funny"):
/// the shove, the coal off the shovel, the bonk, and what the report makes of them. The shared rules are
/// <see cref="Horseplay"/>'s; crewmates being solid to each other is the motor's (<see cref="PlayerMotor.Step"/>'s crowd).
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// The crew as they stood at the start of this tick (note 717), set by the host before anyone acts: whom a shove, a bonk or
    /// a lump can find. Empty on a client (and in a world nobody hosts), where none of it is decided.
    /// </summary>
    public IReadOnlyList<(int Id, PlayerState State)> Mates { get; set; } = [];

    /// <summary>Who's a bot (the host's, from each Hello; note 574): bots never shove, bonk a crewmate or hit one with coal.</summary>
    public HashSet<int> BotIds { get; } = [];

    /// <summary>
    /// Crewmates knocked lately (note 717): the host's, kept as long as a swing is and replicated, for the shover's arm, the
    /// bonk's clang and the coal's thud on every machine. Nothing in the sim reads them.
    /// </summary>
    public List<HorseplayEvent> Horseplays { get; } = new();

    /// <summary>Lumps of coal in flight (note 717): the host's, replicated each tick for every machine to draw.</summary>
    public List<LumpInFlight> Lumps { get; } = new();

    sealed class Lump(int id, int by, Double3 at, Double3 velocity, uint tick)
    {
        public readonly int Id = id, By = by;
        public readonly uint Thrown = tick;
        public Double3 At = at, Velocity = velocity;
        public double Hint;
    }

    readonly List<Lump> _lumps = new();
    readonly List<Knock> _knocks = new();
    readonly Dictionary<int, uint> _shoveReady = new();
    readonly HashSet<int> _shoveWas = new();
    /// <summary>Who shoved whom last, and how fast the train was going under them then (m/s): the report's line for a shove that killed.</summary>
    readonly Dictionary<int, (int By, double Speed)> _shovedBy = new();
    /// <summary>The bonks each crewmate took, by whom (note 717): the report's count.</summary>
    readonly SortedDictionary<int, SortedDictionary<int, int>> _bonks = new();

    readonly record struct Knock(int Target, int By, KnockKind Kind, Double3 Away);

    static readonly HorseplayTuning NoHorseplay = new();

    /// <summary>This night's horseplay (player.json <c>horseplay</c>, the sessions' <see cref="Movement"/>).</summary>
    public HorseplayTuning HorseplayTuning => Movement?.Horseplay ?? NoHorseplay;

    /// <summary>
    /// The lobby's CREW MISCHIEF switch (GDD §23.4; ARCHITECTURE §8 note 724): set on every machine from the night's setup
    /// (<c>SessionSetup.Mischief</c>), and on a client again whenever the host changes it in the yard. Off, a shove, a bonk
    /// and coal thrown at the crew land on nobody, as player.json <c>horseplay.enabled: false</c> does; crewmates stay solid.
    /// </summary>
    public bool CrewMischief { get; set; } = true;

    /// <summary>Whether horseplay lands on the crew tonight: the content allows it (<c>horseplay.enabled</c>) and the lobby has it on.</summary>
    public bool MischiefOn => HorseplayTuning.Enabled && CrewMischief;

    /// <summary>Whether <paramref name="by"/>'s mischief lands on the crew: horseplay's on, and they're not a bot.</summary>
    bool Mischief(int by) => MischiefOn && !BotIds.Contains(by) && Mates.Count > 0;

    /// <summary>Who last shoved <paramref name="player"/> (or bumped them on with a shove), and the train's speed then; null for nobody.</summary>
    public (int By, double Speed)? ShovedBy(int player) => _shovedBy.TryGetValue(player, out var s) ? s : null;

    // The marked (note 717): each shover whose shove killed, whom it killed and how fast the train was going under them. The
    // night's history: the report's lines read it, whether or not the mark still bites.
    readonly SortedDictionary<int, (int Victim, double Speed)> _marks = new();
    // Whose mark still bites (note 754): a marked shover's death ends theirs (horseplay.marked.endsAtDeath).
    readonly SortedSet<int> _marked = new();
    // The crew lying dead of a shove, already counted (a victim back through a Holdout and shoved to death again counts again).
    readonly HashSet<int> _shovedDead = new();

    /// <summary>
    /// Note 717 (the director, 14 Oct 2026: "if a player ever shoves another player and that results in their death all the
    /// monsters in the game should target more heavily the murderer player so they are heavily likely to die (even if they
    /// rescue the player from a holdout)"): marked for the rest of the night, whatever comes after, until the shover dies (note
    /// 754, the director, 14 Oct 2026: "Shovers mark should reset to normal targeting after they are killed").
    /// </summary>
    public bool IsMarked(int player) => _marked.Contains(player);

    /// <summary>Tonight's marked, the mark ended or not (the report's history): each shover, whom their shove killed, and the train's speed then (m/s).</summary>
    public IEnumerable<(int Shover, int Victim, double Speed)> Marks => _marks.Select(m => (m.Key, m.Value.Victim, m.Value.Speed));

    /// <summary>How heavily the creatures and the director weigh <paramref name="player"/> as a target: horseplay.marked.targetWeight marked, else 1.</summary>
    public double TargetBias(int player) => IsMarked(player) ? Math.Max(1, HorseplayTuning.Marked.TargetWeight) : 1;

    /// <summary>
    /// How near a crewmate counts as to a creature choosing whom to go for (<see cref="Enemies.EnemyContext.Weigh"/>): a marked
    /// one's <paramref name="distance"/> over the weight, less horseplay.marked.allowanceM.
    /// </summary>
    public double WeighTarget(int player, double distance) =>
        IsMarked(player) ? distance / TargetBias(player) - HorseplayTuning.Marked.AllowanceM : distance;

    /// <summary>Whether a marked crewmate is alive and on foot, off the train (the director builds faster while one is).</summary>
    public bool MarkedAfoot => _marked.Count > 0 && CrewThisTick.Any(c => c.State.Alive && c.State.Parent == PlayerState.World && IsMarked(c.Id));

    /// <summary>
    /// Note 717, host, once everyone has moved: a crewmate newly dead of a shove (<see cref="DeathCause.Shoved"/>) marks whoever
    /// shoved them for the night, the news says so, and the report will. A marked shover dead (of anything) is unmarked (note
    /// 754): back through a Holdout they're weighed like anyone; the news and the report keep the line.
    /// </summary>
    void MarkShovers(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (HorseplayTuning.Marked.EndsAtDeath)
            foreach (var (id, s) in crew)
                if (!s.Alive)
                    _marked.Remove(id);
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
        {
            if (s.Alive)
            {
                _shovedDead.Remove(id);
                continue;
            }
            if (s.Death != DeathCause.Shoved || !_shovedDead.Add(id) || !HorseplayTuning.Marked.Enabled)
                continue;
            if (ShovedBy(id) is not { } by || by.By < 0 || by.By == id)
                continue;
            _marks[by.By] = (id, by.Speed);
            _marked.Add(by.By);
            var e = new Sim.Run.NewsEvent(_nextFx, Tick, Sim.Run.NewsKind.Marked, [id], 0, 0, by.By, by.Speed);
            _nextFx = _nextFx % 0xFFFFFF + 1;
            NewsLog.Add(e);
            News.Add(e);
        }
    }

    /// <summary>The bonks <paramref name="player"/> has taken tonight, by whom.</summary>
    public IReadOnlyDictionary<int, int> BonksOn(int player) =>
        _bonks.TryGetValue(player, out var by) ? by : (IReadOnlyDictionary<int, int>)new SortedDictionary<int, int>();

    /// <summary>Everyone bonked tonight, and by whom (note 717): the report's count reads it.</summary>
    public IEnumerable<(int Victim, IReadOnlyDictionary<int, int> By)> Bonks =>
        _bonks.Select(b => (b.Key, (IReadOnlyDictionary<int, int>)b.Value));

    /// <summary>
    /// Note 717: Throw with coal on the shovel throws the lump (instead of it going into the fire). Run alike on the host and a
    /// predicting client, before the hands (a Throw that threw coal throws nothing else, and takes no radio off): the shovel's
    /// emptied and the tender's a unit short (the handful never reached the fire; nothing else counts it). The lump itself is
    /// the host's.
    /// </summary>
    void CoalAct(ref PlayerState s, ref PlayerIntent intent, int playerId)
    {
        if (!s.Alive || !s.Has(PlayerFlags.Shovelful) || !intent.Has(PlayerButtons.Throw) || s.Stagger > 0)
            return;
        s.Flags &= ~PlayerFlags.Shovelful;
        s.ActionProgress = 0;
        intent.Buttons &= ~PlayerButtons.Throw;
        Train.Boiler.Tender = Math.Max(0, Train.Boiler.Tender - 1);
        if (Mates.Count == 0)
            return;
        var t = HorseplayTuning.Coal;
        var feet = PlayerMotor.WorldPosition(s, Train);
        double yaw = PlayerMotor.WorldYaw(s, Train);
        var look = new Double3(-DMath.Sin(yaw) * DMath.Cos(s.Pitch), DMath.Sin(s.Pitch), -DMath.Cos(yaw) * DMath.Cos(s.Pitch));
        var eye = feet + Double3.Up * (s.Has(PlayerFlags.Crouched) ? 1.0 : 1.5);
        var velocity = PlayerMotor.WorldVelocity(s, Train) + look * t.Speed + Double3.Up * t.Lift;
        _lumps.Add(new Lump(_nextFx, playerId, eye + look * 0.35, velocity, Tick) { Hint = s.LineHint });
        _nextFx = _nextFx % 0xFFFFFF + 1;
        Horseplays.Add(new HorseplayEvent(_nextFx, Tick, playerId, KnockKind.Coal, -1));
        _nextFx = _nextFx % 0xFFFFFF + 1;
    }

    /// <summary>
    /// Note 717: the shove, on the press of its key, host only: the crewmate looked at within reach (<see cref="Horseplay.ShoveTarget"/>)
    /// is knocked away from the shover, and anyone just beyond them bumped on; the shover waits out the cooldown.
    /// </summary>
    void ShoveAct(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool held = intent.Has(PlayerMoves.Shove);
        bool pressed = held && !_shoveWas.Contains(playerId);
        if (held)
            _shoveWas.Add(playerId);
        else
            _shoveWas.Remove(playerId);
        if (!pressed || !Mischief(playerId) || Movement is not { } p || !Horseplay.CanShove(s))
            return;
        if (_shoveReady.TryGetValue(playerId, out uint ready) && Tick < ready)
            return;
        if (Horseplay.ShoveTarget(s, playerId, Mates, Train, p) is not { } target)
            return;
        var t = p.Horseplay.Shove;
        _shoveReady[playerId] = Tick + (uint)Math.Round(t.CooldownSeconds * SimConstants.TickRate);
        var from = PlayerMotor.WorldPosition(s, Train);
        var at = PlayerMotor.WorldPosition(target.State, Train);
        var away = Horseplay.Away(from, at, PlayerMotor.WorldYaw(s, Train));
        _knocks.Add(new Knock(target.Id, playerId, KnockKind.Shove, away));
        _shovedBy[target.Id] = (playerId, SpeedUnder(target.State));
        _thrown.Remove(target.Id);
        // Into whoever's just beyond them (a chain bump, one deep): off their feet more gently, the shove's all the same.
        foreach (var (id, o) in Mates)
        {
            if (id == playerId || id == target.Id || !Horseplay.Shoveable(o) || o.Parent >= Train.Frames.Count)
                continue;
            var beyond = (PlayerMotor.WorldPosition(o, Train) - at) with { Y = 0 };
            if (beyond.Length > t.ChainReach || Math.Abs(PlayerMotor.WorldPosition(o, Train).Y - at.Y) > p.Height * 0.6 || Double3.Dot(beyond, away) <= 0)
                continue;
            _knocks.Add(new Knock(id, playerId, KnockKind.Bump, Horseplay.Away(at, at + beyond, 0)));
            _shovedBy[id] = (playerId, SpeedUnder(o));
            _thrown.Remove(id);
        }
        Horseplays.Add(new HorseplayEvent(_nextFx, Tick, playerId, KnockKind.Shove, target.Id));
        _nextFx = _nextFx % 0xFFFFFF + 1;
    }

    /// <summary>How fast the train's going under a crewmate (m/s): their car's rake's speed, or nothing on the ground.</summary>
    double SpeedUnder(in PlayerState s) =>
        s.Parent >= 0 && s.Parent < Train.Frames.Count ? Math.Abs(Train.RakeOf(s.Parent).Speed) : 0;

    /// <summary>
    /// Note 717: a swing that meets a crewmate before a creature (App. C.2's reach and cone, nearer than the nearest creature in
    /// it) is a bonk: a clang, a short stagger, no damage, and a count for the report. Never a bot's, nor with horseplay off.
    /// </summary>
    bool Bonk(in PlayerState s, int playerId, Double3 eye, Double3 facing, MeleeTuning t, double enemyAt)
    {
        if (!Mischief(playerId))
            return false;
        double cos = DMath.Cos(t.ConeDegrees * Math.PI / 180);
        int best = -1;
        double bestD = enemyAt;
        foreach (var (id, o) in Mates)
        {
            if (id == playerId || !o.Alive || o.Has(PlayerFlags.Held) || o.Parent >= Train.Frames.Count)
                continue;
            var to = PlayerMotor.WorldPosition(o, Train) + Double3.Up * Horseplay.Chest - eye;
            double d = to.Length;
            if (d > t.Reach)
                continue;
            var flat = to with { Y = 0 };
            if (flat.Length > 0.4 && Double3.Dot(flat.Normalized, facing) < cos)
                continue;
            if (d < bestD)
                (best, bestD) = (id, d);
        }
        if (best < 0)
            return false;
        _knocks.Add(new Knock(best, playerId, KnockKind.Bonk, facing));
        if (!_bonks.TryGetValue(best, out var by))
            _bonks[best] = by = new SortedDictionary<int, int>();
        by[playerId] = by.GetValueOrDefault(playerId) + 1;
        Horseplays.Add(new HorseplayEvent(_nextFx, Tick, playerId, KnockKind.Bonk, best));
        _nextFx = _nextFx % 0xFFFFFF + 1;
        return true;
    }

    /// <summary>
    /// Host, once everyone has acted and the world has stepped (from <see cref="ApplyDamage"/>): the lumps in flight move and land
    /// (on a crewmate, a creature, the train or the ground), then every knock this tick is put on whom it landed on.
    /// </summary>
    void StepHorseplay(Func<int, PlayerState?> get, Action<int, PlayerState> set)
    {
        StepLumps();
        var t = HorseplayTuning;
        foreach (var k in _knocks)
        {
            if (get(k.Target) is not { Alive: true } s || s.Has(PlayerFlags.Held))
                continue;
            Horseplay.Knock(ref s, k.Kind, k.Away, Train, t);
            set(k.Target, s);
        }
        _knocks.Clear();
        // The human cannonball (note 746): fired from the gun, if they're still in it; in the air, a creature hit stops them dead
        // (they drop, unhurt); landed, the flight's over.
        foreach (var launch in _launches)
            if (get(launch.Who) is { } s && HumanCannonball.Fire(ref s, launch))
            {
                set(launch.Who, s);
                _flights[launch.Who] = launch.By;
                // The report (note 746): "Sam fired from the rear cannon. Fired by Dave."
                Attribution.Add(Sim.Run.IncidentLog.Event(this, Sim.Run.IncidentKind.Whimsy,
                    $"{Sim.Run.IncidentLog.NameOf(this, launch.Who)} fired from the rear cannon", launch.By, "Fired by {actor}.", launch.From));
                _flightHit.Remove(launch.Who);
            }
        _launches.Clear();
        foreach (int id in _flights.Keys.ToList())
        {
            if (get(id) is not { } s || !s.Has(PlayerFlags.Fired))
            {
                _flights.Remove(id);
                _flightHit.Remove(id);
                continue;
            }
            if (_flightHit.Contains(id) && (s.Velocity.X != 0 || s.Velocity.Z != 0))
            {
                s.Velocity = new Ballast.Double3(0, Math.Min(s.Velocity.Y, 0), 0);
                set(id, s);
            }
        }
    }

    /// <summary>How many steps a lump's flight is cut into each tick, so a fast one doesn't pass through a body between two.</summary>
    const int LumpSteps = 4;

    void StepLumps()
    {
        var t = HorseplayTuning.Coal;
        double g = Movement?.Gravity ?? 9.81, dt = SimConstants.TickSeconds / LumpSteps;
        for (int i = _lumps.Count - 1; i >= 0; i--)
        {
            var lump = _lumps[i];
            bool gone = (Tick - lump.Thrown) * SimConstants.TickSeconds > t.LifeSeconds;
            for (int k = 0; k < LumpSteps && !gone; k++)
            {
                lump.Velocity -= Double3.Up * (g * dt);
                lump.At += lump.Velocity * dt;
                gone = LumpLands(lump, t, (LumpSteps - 1 - k) * dt);
            }
            if (gone)
                _lumps.RemoveAt(i);
        }
        Lumps.Clear();
        foreach (var lump in _lumps)
            Lumps.Add(new LumpInFlight(lump.Id, lump.By, lump.At));
    }

    /// <summary>
    /// Whether a lump has come down on something this step, and what that did: a crewmate sooted, a creature a quarter of a blow.
    /// The train's already where it is at the tick's end, <paramref name="behind"/> seconds after this step: what rides it is
    /// met where it was then (the lump carried on by its car's motion for the comparison).
    /// </summary>
    bool LumpLands(Lump lump, CoalTuning t, double behind)
    {
        // A crewmate (not the thrower): the ball against the body's capsule, shins to crown.
        if (Mischief(lump.By) && Movement is { } p)
            foreach (var (id, o) in Mates)
            {
                if (id == lump.By || !o.Alive || o.Parent >= Train.Frames.Count)
                    continue;
                var feet = PlayerMotor.WorldPosition(o, Train);
                var at = o.Parent == PlayerState.World ? lump.At : lump.At + Train.Frames[o.Parent].Velocity * behind;
                double top = o.Has(PlayerFlags.Crouched) ? p.Crouch.Height : p.Height;
                double y = Math.Clamp(at.Y, feet.Y + 0.3, feet.Y + top - 0.1);
                if ((at - feet with { Y = y }).Length > p.Radius + t.Radius)
                    continue;
                _knocks.Add(new Knock(id, lump.By, KnockKind.Coal, lump.Velocity with { Y = 0 }));
                Horseplays.Add(new HorseplayEvent(_nextFx, Tick, lump.By, KnockKind.Coal, id));
                _nextFx = _nextFx % 0xFFFFFF + 1;
                return true;
            }
        // A creature's body (what a cannonball finds: World.Targets): a quarter of a blow (App. C.2's, counted in the crowbar's).
        if (_context is { } ctx)
            foreach (var target in Targets)
            {
                if ((lump.At - target.Position).Length > target.Radius + t.Radius)
                    continue;
                var e = _enemies.FirstOrDefault(x => x.Id == target.Id);
                if (e is null || e.Gone || !e.Strikable(lump.By) || !e.Reachable(this))
                    continue;
                e.Struck(ctx, lump.By, t.Blow * ctx.Tuning.Melee.Crowbar);
                Confirm(e, lump.By, HitSource.Melee, lump.At, lump.Velocity.Length > 1e-6 ? lump.Velocity.Normalized : Double3.Up);
                return true;
            }
        // The train's own body: a wall, a roof, the tender.
        foreach (var frame in Train.Frames)
        {
            if ((frame.Origin - lump.At).Length > 40)
                continue;
            var local = frame.ToLocal(lump.At + frame.Velocity * behind);
            var vehicle = Train.Vehicles[frame.Index];
            foreach (var solid in frame.Shape.Solids)
                if (solid.Present(vehicle) && solid.BoxIn(vehicle).Contains(local))
                    return true;
        }
        // The ground; on the rails, a knock what hunts by vibration hears (note 728).
        if (lump.At.Y > PlayerMotor.GroundAt(lump.At, Train.Line, ref lump.Hint))
            return false;
        if (OnRail(lump.At, lump.Hint))
            KnockRail(lump.At, lump.By);
        return true;
    }

    /// <summary>A client's copy of the host's horseplay (note 717): the knocks seen lately, and the lumps in flight.</summary>
    public void MirrorHorseplay(IEnumerable<HorseplayEvent> events, IEnumerable<LumpInFlight> lumps)
    {
        Horseplays.Clear();
        Horseplays.AddRange(events);
        Lumps.Clear();
        Lumps.AddRange(lumps);
    }

    /// <summary>Starts a tick's horseplay: last tick's knocks are spent, and the knocks seen kept as long as a swing is.</summary>
    void BeginHorseplay(double keepSeconds)
    {
        _knocks.Clear();
        if (Hosting)
            Horseplays.RemoveAll(h => Tick - h.Tick > keepSeconds * SimConstants.TickRate);
    }
}
