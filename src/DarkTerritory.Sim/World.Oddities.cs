using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim;

/// <summary>
/// Odd outcomes to find (queue #464, GDD §3 and App. F.6; ARCHITECTURE §8 note 730; the director, 14 Oct 2026: things that
/// answer when poked, never in the Wiki): the host's rules for them. Where the stops' things stand is
/// <see cref="Oddities"/>'s; what's happened replicates as <see cref="OddEvents"/> (each heard and seen once by its id) and
/// the things' state. None of it hurts anyone, solves a creature or is anyone's horseplay: no crew mischief switch gates it.
/// </summary>
public sealed partial class World
{
    /// <summary>The night's odd things at the stops (note 730), built on every machine from the route; null with none.</summary>
    public Oddities? Oddities { get; private set; }

    /// <summary>
    /// What the odd things did lately (note 730): the host's, kept <c>oddities.keepSeconds</c> and replicated, for every
    /// machine's sounds, the townspeople's looks and the letter read out. Nothing in the night reads them back.
    /// </summary>
    public List<OddEvent> OddEvents { get; } = new();

    /// <summary>run.json <c>oddities</c>, while they're on (the bell needs no stop, so this isn't <see cref="Oddities"/>'s).</summary>
    OddityTuning? OddTuning => Run?.Tuning.Oddities is { Enabled: true } t ? t : null;

    // Host: who's holding Use at which odd thing, and for how long; whose Use was down last tick (a drop is a press).
    readonly Dictionary<int, double> _atBell = [];
    readonly Dictionary<int, double> _byDave = [];
    readonly Dictionary<int, double> _washing = [];
    readonly Dictionary<int, (int Thing, double Held)> _atPostbox = [], _atNote = [];
    readonly HashSet<int> _wellUseWas = [];
    // Host: when each town's bell last rang (its clock), the happenings still to come (a splash, the coin, an answer), the
    // scarecrows somebody's been up to, the cans kicked so far tonight, and the crew to soot or jolt once everyone's acted.
    readonly Dictionary<Towns.Town, double> _rungAt = [];
    readonly List<(uint Due, OddKind Kind, int By, int Thing, Double3 At)> _oddLater = [];
    readonly HashSet<int> _crowSeen = [];
    int _kicks;
    // Host: where each crewmate's feet were last tick (on the ground the motor keeps no walking velocity): their pace at a can.
    readonly Dictionary<int, Double3> _feetWere = [];
    // Every machine: the happenings and waves already reacted to (the townspeople's looks), so each is once.
    readonly HashSet<int> _oddSeen = [];
    readonly HashSet<int> _wavesSeen = [];

    /// <summary>
    /// Builds the night's odd things (from <see cref="EnableRun"/>, on every machine alike), knowing the forts: no note's left
    /// where the lineside isn't drawn (note 979). Built again by <see cref="EnableTown"/> once the towns' squares and walls are
    /// on the forts, before anything's been done to them.
    /// </summary>
    void EnableOddities(RunTuning tuning, Route.Route route) => Oddities = Sim.Run.Oddities.Of(tuning.Oddities, route, Train.Line, Forts);

    /// <summary>A happening (host): kept for the wire, and the townspeople's looks taken from it here as on every client.</summary>
    void Odd(OddKind kind, int by, int thing, Double3 at)
    {
        var e = new OddEvent(_nextFx, Tick, kind, by, thing, at);
        _nextFx = _nextFx % 0xFFFFFF + 1;
        OddEvents.Add(e);
        Notice(e);
    }

    /// <summary>The bell rope a crewmate on foot has to hand (note 730): in a walled town, within <c>bell.reach</c> of it.</summary>
    public Double3? BellRopeInReach(in PlayerState s)
    {
        if (!s.Alive || s.Parent != PlayerState.World || OddTuning is not { } t || TownOf(s) is not { BellRope: { } rope })
            return null;
        return ((PlayerMotor.WorldPosition(s, Train) - rope) with { Y = 0 }).Length <= t.Bell.Reach ? rope : null;
    }

    /// <summary>Dave, painting, near enough to sit with while his painting's still to give (note 730); null otherwise. Alike on host and client.</summary>
    public Sim.Enemies.Dave? DaveToSitWith(in PlayerState s)
    {
        if (!s.Alive || s.Parent != PlayerState.World || OddTuning is not { } t || Oddities is { PaintingGiven: true })
            return null;
        var at = PlayerMotor.WorldPosition(s, Train);
        return _enemies.OfType<Sim.Enemies.Dave>().FirstOrDefault(d => !d.Gone && d.Phase == Sim.Enemies.SpinePhase.Dormant
            && ((d.Local - at) with { Y = 0 }).Length <= t.Painting.Reach);
    }

    /// <summary>The odd thing of <paramref name="kind"/> a crewmate on foot is at (within its reach, level), or null.</summary>
    public OddThing? OddThingInReach(in PlayerState s, OddThingKind kind)
    {
        if (!s.Alive || s.Parent != PlayerState.World || Oddities is not { } odd)
            return null;
        double reach = kind switch
        {
            OddThingKind.Well => odd.Tuning.Well.Reach,
            OddThingKind.Postbox => odd.Tuning.Postbox.Reach,
            OddThingKind.Note => odd.Tuning.Note.Reach,
            _ => odd.Tuning.Can.Reach,
        };
        var at = PlayerMotor.WorldPosition(s, Train);
        OddThing? best = null;
        double nearest = reach;
        foreach (var thing in odd.Things)
        {
            if (thing.Kind != kind)
                continue;
            var d = odd.At(thing.Index) - at;
            double flat = (d with { Y = 0 }).Length;
            if (flat <= nearest && Math.Abs(d.Y) < 2)
                (best, nearest) = (thing, flat);
        }
        return best;
    }

    /// <summary>A well a find or a toy in a crewmate's hands would go down with a Use (note 730), or null.</summary>
    public OddThing? WellToDropIn(in PlayerState s, int playerId) =>
        Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Loot or Physics.BodyKind.Toy } ? OddThingInReach(s, OddThingKind.Well) : null;

    /// <summary>
    /// Host, before the hands (note 730): a find or a toy in hand at a well's mouth goes down it on a press of Use, and the
    /// hands don't put it down (true: the hands keep out of it this tick).
    /// </summary>
    bool WellDropAct(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool use = intent.Has(PlayerButtons.Use), pressed = use && !_wellUseWas.Contains(playerId);
        if (use)
            _wellUseWas.Add(playerId);
        else
            _wellUseWas.Remove(playerId);
        if (Oddities is not { } odd || WellToDropIn(s, playerId) is not { } well)
            return false;
        if (pressed && Bodies.CarriedBy(playerId) is { } thing)
        {
            Bodies.Remove(thing);
            var mouth = odd.At(well.Index) + Double3.Up * 0.8;
            Odd(OddKind.Drop, playerId, well.Index, mouth);
            uint splash = Tick + (uint)Math.Round(odd.Tuning.Well.SplashSeconds * SimConstants.TickRate);
            _oddLater.Add((splash, OddKind.Splash, playerId, well.Index, mouth));
            // The night's first gets something back: a coin thrown up out of the dark, a while after its splash.
            if (!odd.CoinGiven)
            {
                odd.CoinGiven = true;
                _oddLater.Add((splash + (uint)Math.Round(odd.Tuning.Well.CoinSeconds * SimConstants.TickRate), OddKind.Coin, playerId, well.Index, mouth));
            }
        }
        return true;
    }

    /// <summary>
    /// Host, empty-handed acts once the hands have had their go (note 730): the bell rope, sitting with Dave, a wash at a
    /// well, a postbox's letter. Each is Use held its time; let go and it starts again.
    /// </summary>
    void OddAct(ref PlayerState s, in PlayerIntent intent, int playerId, bool emptyHanded)
    {
        bool use = emptyHanded && intent.Has(PlayerButtons.Use) && OddTuning is not null;
        var t = OddTuning;
        // The bell.
        if (use && BellRopeInReach(s) is { } rope && TownOf(s) is { } town)
        {
            if (Held(_atBell, playerId, t!.Bell.HoldSeconds))
            {
                double clock = Tick * SimConstants.TickSeconds;
                if (!_rungAt.TryGetValue(town, out double last) || clock - last >= t.Bell.EverySeconds - 1e-9)
                {
                    _rungAt[town] = clock;
                    Odd(OddKind.Bell, playerId, -1, rope + Double3.Up * BellOverRope);
                }
            }
        }
        else
            _atBell.Remove(playerId);
        // Dave's painting: a while sat with him, never having struck him.
        if (use && DaveToSitWith(s) is { } dave && dave.BlowsBy(playerId) == 0)
        {
            if (Held(_byDave, playerId, t!.Painting.HoldSeconds) && Oddities is { } odd && !odd.PaintingGiven)
            {
                odd.PaintingGiven = true;
                var at = PlayerMotor.WorldPosition(s, Train);
                var toward = (at - dave.Local) with { Y = 0 };
                var put = dave.Local + (toward.Length > 1e-6 ? toward.Normalized * Math.Min(0.8, toward.Length) : Double3.Zero);
                double hint = s.LineHint;
                put = put with { Y = PlayerMotor.GroundAt(put, Train.Line, ref hint) };
                Bodies.SpawnLoot(put, hint, Sim.Run.Run.OddOwner(coin: false), 0.15);
                Odd(OddKind.Painting, playerId, -1, put);
            }
        }
        else
            _byDave.Remove(playerId);
        // A wash at the well, sooted.
        if (use && s.Has(PlayerFlags.Sooted) && OddThingInReach(s, OddThingKind.Well) is { } well)
        {
            if (Held(_washing, playerId, t!.Well.WashSeconds))
            {
                s.Flags &= ~PlayerFlags.Sooted;
                Odd(OddKind.Wash, playerId, well.Index, Oddities!.At(well.Index) + Double3.Up * 0.8);
            }
        }
        else
            _washing.Remove(playerId);
        // A postbox's letter, once a box.
        if (use && OddThingInReach(s, OddThingKind.Postbox) is { } box && !Oddities!.Done(box.Index))
        {
            double held = (_atPostbox.TryGetValue(playerId, out var was) && was.Thing == box.Index ? was.Held : 0) + SimConstants.TickSeconds;
            _atPostbox[playerId] = (box.Index, held);
            if (held >= t!.Postbox.HoldSeconds - 1e-9)
            {
                _atPostbox.Remove(playerId);
                Oddities.Finish(box.Index);
                Odd(OddKind.Letter, playerId, box.Index, Oddities.At(box.Index) + Double3.Up * 1.0);
                if (Oddities.LetterIn(box.Index) is { } letter)
                    Attribution.Add(IncidentLog.Event(this, IncidentKind.Letter, "A letter left in a postbox", playerId,
                        $"To {letter.To}: \"{letter.Text}\" Read by {{actor}}.", Oddities.At(box.Index)));
            }
        }
        else
            _atPostbox.Remove(playerId);
        // A note left out along the line (note 961): picked up and read, once a note; it's gone from the doorstep after.
        if (use && OddThingInReach(s, OddThingKind.Note) is { } note && !Oddities!.Done(note.Index))
        {
            double held = (_atNote.TryGetValue(playerId, out var was) && was.Thing == note.Index ? was.Held : 0) + SimConstants.TickSeconds;
            _atNote[playerId] = (note.Index, held);
            if (held >= t!.Note.HoldSeconds - 1e-9)
            {
                _atNote.Remove(playerId);
                Oddities.Finish(note.Index);
                Odd(OddKind.Letter, playerId, note.Index, Oddities.At(note.Index) + Double3.Up * 0.3);
                if (Oddities.NoteIn(note.Index) is { } read)
                    Attribution.Add(IncidentLog.Event(this, IncidentKind.Letter, "A note found by the line", playerId,
                        $"{read.Heading}: \"{read.Text}\" Picked up by {{actor}}.", Oddities.At(note.Index)));
            }
        }
        else
            _atNote.Remove(playerId);
    }

    /// <summary>How far over the bell rope's foot the bell hangs, for where it's heard and looked up at (m). Not a design number.</summary>
    const double BellOverRope = 6;

    /// <summary>A hold counted on a tick: true on the tick it reaches <paramref name="seconds"/> (then it's let go, to start again).</summary>
    static bool Held(Dictionary<int, double> holds, int playerId, double seconds)
    {
        double held = holds[playerId] = holds.GetValueOrDefault(playerId) + SimConstants.TickSeconds;
        if (held < seconds - 1e-9)
            return false;
        holds.Remove(playerId);
        return true;
    }

    /// <summary>
    /// Host, once everyone's acted and the world's stepped (from <see cref="ApplyDamage"/>): the happenings come due, the cans
    /// walked into, the scarecrows, the coal falling on whoever's under the chute, the switches thrown under somebody, and the
    /// waves the townspeople answer.
    /// </summary>
    void StepOddities(Func<int, PlayerState?> get, Action<int, PlayerState> set, IEnumerable<int> crewIds)
    {
        if (!Authority || OddTuning is not { } t)
            return;
        var crew = crewIds.Order().Select(id => (Id: id, State: get(id))).Where(c => c.State is not null).Select(c => (c.Id, State: c.State!.Value)).ToList();
        for (int i = 0; i < _oddLater.Count; i++)
        {
            var (due, kind, by, thing, at) = _oddLater[i];
            if (Tick < due)
                continue;
            _oddLater.RemoveAt(i--);
            if (kind == OddKind.Coin)
            {
                // Thrown up out of the well onto the ground beside it, on the side it was dropped from.
                var lip = Oddities!.At(thing);
                var from = crew.FirstOrDefault(c => c.Id == by).State;
                var side = (PlayerMotor.WorldPosition(from, Train) - lip) with { Y = 0 };
                var put = lip + (side.Length > 1e-6 ? side.Normalized : new Double3(1, 0, 0)) * CoinOut;
                double hint = Train.Dynamics.Distance;
                put = put with { Y = PlayerMotor.GroundAt(put, Train.Line, ref hint) };
                Bodies.SpawnLoot(put, hint, Sim.Run.Run.OddOwner(coin: true), 0.1);
            }
            Odd(kind, by, thing, at);
        }
        if (Oddities is { } odd)
        {
            KickCans(odd, crew);
            TurnScarecrows(odd, crew);
        }
        foreach (var (id, s) in crew)
            _feetWere[id] = PlayerMotor.WorldPosition(s, Train);
        // The coaling chute: whoever's where the coal falls comes out of it sooted.
        if (Run?.Pouring(Train.Line) is { } spout)
            foreach (var (id, s) in crew)
                if (s.Alive && !s.Has(PlayerFlags.Sooted) && ((PlayerMotor.WorldPosition(s, Train) - spout) with { Y = 0 }).Length <= t.Chute.Reach)
                {
                    set(id, s with { Flags = s.Flags | PlayerFlags.Sooted });
                    Odd(OddKind.Sooted, id, -1, PlayerMotor.WorldPosition(s, Train));
                }
        // A switch thrown with somebody on its blades: a lurch, the way it went.
        foreach (var thrown in SwitchThrows)
        {
            if (!thrown.Moved || Switches is not { } stands || thrown.Branch < 0 || thrown.Branch >= Train.Line.Branches.Count)
                continue;
            var b = Train.Line.Branches[thrown.Branch];
            double toe = b.Toe, end = toe + stands.Tuning.PointsLength;
            foreach (var (id, s) in crew)
            {
                if (!s.Alive || s.Parent != PlayerState.World || s.Surface != Surface.Ground || s.Has(PlayerFlags.Held))
                    continue;
                var at = PlayerMotor.WorldPosition(s, Train);
                double hint = s.LineHint;
                var (path, along) = Train.Line.Nearest(at, ref hint);
                if (path != Rail.RailLine.MainPath || along < toe - 0.5 || along > end)
                    continue;
                var sample = Train.Line.Sample(along);
                var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
                if (Math.Abs(Double3.Dot(at - sample.Position, right)) > t.Lurch.BladesHalfWidth)
                    continue;
                // Thrown for the branch, the blades go over toward its side; back for the main line, the other way.
                var away = right * (b.Side * (thrown.Diverging ? 1 : -1));
                var lurched = s;
                lurched.Velocity = away * t.Lurch.Speed + Double3.Up * t.Lurch.Lift;
                lurched.Surface = Surface.Air;
                // Off their own footing, the ground's: what they land from is their own little hop (note 573), no knock.
                lurched.Flags = (lurched.Flags | PlayerFlags.OffTheGround) & ~(PlayerFlags.Pushing | PlayerFlags.Crouched);
                lurched.ActionProgress = 0;
                lurched.Stagger = (byte)Math.Clamp(Math.Max(s.Stagger, (int)Math.Round(t.Lurch.StaggerSeconds * SimConstants.TickRate)), 0, byte.MaxValue);
                lurched.Placed++;
                set(id, lurched);
                Odd(OddKind.Lurch, thrown.PlayerId, thrown.Branch, at);
            }
        }
        NoticeWaves(crew);
    }

    /// <summary>A crewmate's feet moving faster than this between ticks were moved, not walked or run (m/s). Not a design number.</summary>
    const double MaxPace = 12;

    /// <summary>How far from a well's middle the coin comes down (m): just clear of its wall. Not a design number.</summary>
    const double CoinOut = 1.4;

    /// <summary>
    /// Host: a can walked into at a pace is kicked on the way they were going (<c>can.kickM</c>, the night's seed and how many
    /// have gone before), rattling; something answers it from the dark a moment later, away from the train.
    /// </summary>
    void KickCans(Oddities odd, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        var c = odd.Tuning.Can;
        foreach (var thing in odd.Things)
        {
            if (thing.Kind != OddThingKind.Can)
                continue;
            var can = odd.At(thing.Index);
            foreach (var (id, s) in crew)
            {
                if (!s.Alive || s.Parent != PlayerState.World || !s.Grounded)
                    continue;
                var feet = PlayerMotor.WorldPosition(s, Train);
                var pace = _feetWere.TryGetValue(id, out var was) ? ((feet - was) * SimConstants.TickRate) with { Y = 0 } : Double3.Zero;
                // (Faster than anyone runs is a respawn or a placement, not a foot.)
                if (pace.Length < c.KickSpeed || pace.Length > MaxPace || ((feet - can) with { Y = 0 }).Length > c.Reach || Math.Abs(feet.Y - can.Y) > 1)
                    continue;
                var rng = new Pcg32(Stops.StopSeed.Of(odd.Seed, 0x4b49434b, (ulong)_kicks++));
                double far = rng.Range(c.Kick[0], c.Kick[1]), wait = rng.Range(c.Answer[0], c.Answer[1]);
                var to = can + pace.Normalized * far;
                double hint = s.LineHint;
                to = to with { Y = PlayerMotor.GroundAt(to, Train.Line, ref hint) };
                odd.Move(thing.Index, to);
                Odd(OddKind.Kick, id, thing.Index, to);
                // Out in the dark beyond it, away from the line.
                double near = hint;
                var (_, along) = Train.Line.Nearest(to, ref near);
                var off = (to - Train.Line.Sample(along).Position) with { Y = 0 };
                var dark = to + (off.Length > 1e-6 ? off.Normalized : pace.Normalized) * c.AnswerM;
                _oddLater.Add((Tick + (uint)Math.Round(wait * SimConstants.TickRate), OddKind.Answer, id, thing.Index, dark));
                break;
            }
        }
    }

    /// <summary>
    /// Host: a scarecrow somebody's been up to (<c>scarecrow.nearM</c>) is turned to face the line once nobody living is
    /// within <c>turnBeyondM</c> of it or looking its way.
    /// </summary>
    void TurnScarecrows(Oddities odd, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        var c = odd.Tuning.Scarecrow;
        double cos = DMath.Cos(c.LookDegrees * Math.PI / 180);
        foreach (var thing in odd.Things)
        {
            if (thing.Kind != OddThingKind.Scarecrow || odd.Done(thing.Index))
                continue;
            bool near = false, watched = false;
            foreach (var (_, s) in crew)
            {
                if (!s.Alive)
                    continue;
                var to = (thing.At - PlayerMotor.WorldPosition(s, Train)) with { Y = 0 };
                double d = to.Length;
                near |= d <= c.NearM;
                double yaw = PlayerMotor.WorldYaw(s, Train);
                var view = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
                watched |= d <= c.TurnBeyondM || d > 1e-6 && Double3.Dot(to.Normalized, view) >= cos;
            }
            if (near)
                _crowSeen.Add(thing.Index);
            if (!_crowSeen.Contains(thing.Index) || watched)
                continue;
            odd.Finish(thing.Index);
            Odd(OddKind.Turned, -1, thing.Index, thing.At);
        }
    }

    /// <summary>
    /// Every machine (note 730): what a happening does to a town's people, once. The bell: everyone within earshot stops and
    /// looks up at it a while.
    /// </summary>
    void Notice(in OddEvent e)
    {
        if (!_oddSeen.Add(e.Id) || e.Kind != OddKind.Bell || Run?.Tuning.Oddities is not { } t || TownAt(e.At) is not { } town)
            return;
        double start = e.Tick * SimConstants.TickSeconds;
        foreach (var p in town.Plan.People)
            if (((town.Feet(p) - e.At) with { Y = 0 }).Length <= t.Bell.EarshotM)
                town.React(p.Id, start, t.Bell.LookSeconds, e.At, "idle");
    }

    /// <summary>
    /// Every machine (note 730): a crewmate's wave (the emote) at a townsperson in front of them is waved back, once a wave:
    /// the nearest within <c>wave.reachM</c> and <c>wave.coneDegrees</c> of where they face.
    /// </summary>
    void NoticeWaves(IEnumerable<(int Id, PlayerState State)> crew)
    {
        if (Run?.Tuning.Oddities is not { Enabled: true } t)
            return;
        foreach (var e in Emotes)
        {
            if (e.Kind != Emote.Wave || !_wavesSeen.Add(e.Id) || e.By < 0)
                continue;
            var (id, s) = crew.FirstOrDefault(c => c.Id == e.By);
            if (id != e.By || !s.Alive || TownOf(s) is not { } town)
                continue;
            var feet = PlayerMotor.WorldPosition(s, Train);
            double yaw = PlayerMotor.WorldYaw(s, Train);
            var view = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
            double cos = DMath.Cos(t.Wave.ConeDegrees * Math.PI / 180), best = t.Wave.ReachM;
            Towns.Townsperson? who = null;
            foreach (var p in town.Plan.People)
            {
                var to = (town.Feet(p) - feet) with { Y = 0 };
                double d = to.Length;
                if (d <= best && d > 1e-6 && Double3.Dot(to.Normalized, view) >= cos)
                    (who, best) = (p, d);
            }
            if (who is not null)
                town.React(who.Id, e.Tick * SimConstants.TickSeconds, t.Wave.Seconds, feet, "wave");
        }
    }

    /// <summary>Starts a tick's odd outcomes (host): happenings kept as long as <c>oddities.keepSeconds</c>.</summary>
    void BeginOddities()
    {
        if (Hosting)
            OddEvents.RemoveAll(e => Tick - e.Tick > (OddTuning?.KeepSeconds ?? 4) * SimConstants.TickRate);
    }

    /// <summary>A client's copy of the host's happenings and the odd things' state (note 730); the townspeople's looks taken from them.</summary>
    public void MirrorOddities(IEnumerable<OddEvent> events, long[]? state, Func<long, double> pos, IEnumerable<(int Id, PlayerState State)> crew)
    {
        OddEvents.Clear();
        OddEvents.AddRange(events);
        foreach (var e in OddEvents)
            Notice(e);
        if (state is not null)
            Oddities?.Mirror(state, pos);
        NoticeWaves(crew);
    }
}
