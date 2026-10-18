using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// The sense table, the host's side (GDD App. A.1, App. C, §21; queue #462, ARCHITECTURE §8 note 728; the director, 14 Oct 2026,
/// App. F.6): what the crew's things give off in the creatures' own senses (content/tuning/senses.json), read by every creature
/// through one query (<see cref="Emitters"/>), and thrown things doing what they are (a lamp breaks into fire, a crate on the
/// rail knocks). A creature answers only its own sense (App. A.1).
/// </summary>
public sealed partial class World
{
    /// <summary>The sense table (senses.json): the repo's own unless the session sets its content's.</summary>
    public SenseTuning Senses { get; set; } = SenseTuning.Load();

    // Knocks on the rail (note 728): where, the tick, and whose.
    readonly List<(Double3 At, uint Tick, int By)> _railKnocks = [];
    List<Emitter> _emitters = [];
    uint _emittersTick = uint.MaxValue;
    EnemyContext? _emittersFor;
    // Extinguishers whose spray has reached a creature this tick (drained once however many it reaches).
    readonly HashSet<int> _doused = [];
    // Each body's speed (world) last tick, and whether it was in someone's hands then: a thrown thing's landing is the change.
    readonly Dictionary<int, Double3> _wasMoving = [];
    readonly HashSet<int> _wasHeld = [];

    /// <summary>
    /// Everything giving off a sense this tick (host): lamps (in hand, set down, burning), the cars' lamps, toys playing, the
    /// extinguishers being sprayed, knocks on the rail, food finds and bodies. In a fixed order (the bodies', the cars', the
    /// crew's, the knocks'), the same on every run of the night.
    /// </summary>
    public IReadOnlyList<Emitter> AllEmitters()
    {
        if (_emittersTick == Tick && ReferenceEquals(_emittersFor, _context))
            return _emitters;
        _emittersTick = Tick;
        _emittersFor = _context;
        _doused.Clear();
        var list = new List<Emitter>();
        var t = Senses;
        void Give(string item, EmitterKind kind, Double3 at, int by, int source, int car)
        {
            foreach (var g in t.Of(item))
                list.Add(new Emitter(kind, g.Sense, at, g.Radius, g.Strength, by, source, car));
        }
        foreach (var b in Bodies.All)
        {
            if (b.Stowed || b.TakenBy >= 0)
                continue;
            int car = b.Parent >= 0 && b.Parent < Train.Frames.Count ? b.Parent : -1;
            var at = Physics.Bodies.WorldCentre(b, Train);
            switch (b.Kind)
            {
                case Physics.BodyKind.Lamp when b.Broken:
                    if (b.Burns > 0)
                        Give("burningLamp", EmitterKind.BurningLamp, at, b.LastHeld, b.Id, car);
                    break;
                // Put away in a slot, it's dark; in the hands or lying about, lit.
                case Physics.BodyKind.Lamp when !b.Stashed:
                    Give("handLamp", EmitterKind.HandLamp, at, b.Carrier >= 0 ? b.Carrier : b.LastHeld, b.Id, car);
                    break;
                case Physics.BodyKind.Toy when b.Playing && b.Noise != Physics.ToyNoise.None:
                    Give(SenseTuning.Key(b.Noise), b.Noise switch
                    {
                        Physics.ToyNoise.Squeaker => EmitterKind.Squeaker,
                        Physics.ToyNoise.MusicBox => EmitterKind.MusicBox,
                        _ => EmitterKind.Drummer,
                    }, at, b.Carrier >= 0 ? b.Carrier : b.LastHeld, b.Id, car);
                    break;
                case Physics.BodyKind.Loot when Run?.FindOf(b) is { } find && t.FoodFinds.Contains(find.Item):
                    Give("food", EmitterKind.Food, at, b.Carrier >= 0 ? b.Carrier : b.LastHeld, b.Id, car);
                    break;
                case Physics.BodyKind.Ragdoll:
                    Give("body", EmitterKind.Body, at, b.Owner, b.Id, car);
                    break;
            }
        }
        foreach (var v in Train.Vehicles)
            if (v.LampLit && v.Id < Train.Frames.Count && Train.Frames[v.Id].Shape.Interior is { } room)
                Give("carLamp", EmitterKind.CarLamp, Train.Frames[v.Id].ToWorld(room.Centre), -1, v.Id, v.Id);
        if (_context is { } ctx && t.Of("extinguisherSpray") is { Length: > 0 } spray)
        {
            double eye = Train.Dynamics.Tuning.Pick.EyeHeight, cone = DMath.Cos(t.Spray.ConeDeg * Math.PI / 180);
            foreach (var (p, intent) in ctx.Crew.OrderBy(c => c.Player.Id))
            {
                var s = p.State;
                if (!s.Alive || !intent.Has(PlayerButtons.Fire) || Bodies.CarriedBy(p.Id) is not { Kind: Physics.BodyKind.Extinguisher, Charge: > 0 } ext)
                    continue;
                var from = PlayerMotor.WorldPosition(s, Train) + Double3.Up * eye;
                var look = Sim.Run.Bookmarks.Forward(s.Yaw, s.Pitch);
                if (s.Parent >= 0 && s.Parent < Train.Frames.Count)
                    look = Train.Frames[s.Parent].DirToWorld(look);
                foreach (var g in spray)
                    list.Add(new Emitter(EmitterKind.Spray, g.Sense, from, g.Radius, g.Strength, p.Id, ext.Id, s.Parent >= 0 ? s.Parent : -1, look, cone));
            }
        }
        _railKnocks.RemoveAll(k => (Tick - k.Tick) * SimConstants.TickSeconds > t.Knock.Seconds);
        foreach (var (at, _, by) in _railKnocks)
            Give("knock", EmitterKind.Knock, at, by, -1, -1);
        _emitters = list;
        return list;
    }

    /// <summary>What gives off <paramref name="sense"/> and reaches <paramref name="at"/> (world) this tick, in the table's order.</summary>
    public IEnumerable<Emitter> Emitters(Sense sense, Double3 at) => AllEmitters().Where(e => e.Sense == sense && e.Reaches(at));

    /// <summary>The strongest of <paramref name="sense"/> reaching <paramref name="at"/> (the first of equals), or null; maskers aren't counted.</summary>
    public Emitter? Loudest(Sense sense, Double3 at, Func<Emitter, bool>? which = null)
    {
        Emitter? best = null;
        foreach (var e in Emitters(sense, at))
            if (!e.Masks && (which is null || which(e)) && (best is null || e.Strength > best.Value.Strength))
                best = e;
        return best;
    }

    /// <summary>
    /// The extinguisher's spray reaching <paramref name="at"/> (world) this tick, if any: the sprayer's emitter. Its charge goes
    /// as it's spent on something (App. C.5, the fire's <c>chargeSeconds</c>), once a tick however many things it reaches.
    /// </summary>
    public Emitter? Sprayed(Double3 at, Func<Emitter, bool>? which = null)
    {
        foreach (var e in Emitters(Sense.Heat, at))
        {
            if (e.Kind != EmitterKind.Spray || which is not null && !which(e))
                continue;
            if (_doused.Add(e.Source) && Bodies.All.FirstOrDefault(b => b.Id == e.Source) is { } ext)
                ext.Charge = Math.Max(0, ext.Charge - SimConstants.TickSeconds / Math.Max(1e-6, Enemies?.CarFire.ChargeSeconds ?? 15));
            return e;
        }
        return null;
    }

    /// <summary>A toy playing where it lies within its own sound's reach of a living crewmate (the Choir listens to the crew).</summary>
    bool HeardByCrew(Physics.Body toy)
    {
        if (_context is not { } ctx || Senses.Of(SenseTuning.Key(toy.Noise), Sense.Sound) is not { } sound)
            return false;
        var at = Physics.Bodies.WorldCentre(toy, Train);
        return ctx.Crew.Any(c => c.Player.State.Alive && (PlayerMotor.WorldPosition(c.Player.State, Train) - at).Length <= sound.Radius);
    }

    /// <summary>A knock on the rail at <paramref name="at"/> (world), in <paramref name="by"/>'s name: what hunts by vibration hears it.</summary>
    public void KnockRail(Double3 at, int by)
    {
        _railKnocks.Add((at, Tick, by));
        _emittersTick = uint.MaxValue;
    }

    /// <summary>Whether a point on the ground is on the rails: within the table's <c>knock.railWithin</c> of a track's centre.</summary>
    public bool OnRail(Double3 at, double hint) => Moose.TrackOff(Train, at, hint) <= Senses.Knock.RailWithin;

    /// <summary>
    /// A car lit inside (App. A.5's "a lit lamp inside a car"): its own lamps, or a hand lamp lying lit in it (note 728,
    /// senses.json <c>answers.fireFlies.lampsAboardCount</c>).
    /// </summary>
    public bool CarLit(int car)
    {
        if (car < 0 || car >= Train.Vehicles.Count)
            return false;
        if (Train.Vehicles[car].LampLit)
            return true;
        if (!Senses.Answers.FireFlies.LampsAboardCount || Train.Frames[car].Shape.Interior is not { } room)
            return false;
        return Bodies.All.Any(b => b is { Kind: Physics.BodyKind.Lamp, Broken: false, Stashed: false, Carrier: < 0, Locker: < 0 } && b.Parent == car && room.Contains(b.Centre));
    }

    /// <summary>
    /// Host, each tick before the creatures: a noisy toy in the hands plays, and is wound; let go, a music box or a drummer plays
    /// on where it lies until it runs down (senses.json <c>toys.playsSeconds</c>). Whatever's in someone's hands is theirs last.
    /// A thrown lamp's burning oil burns down, and the broken lamp's gone when it's out.
    /// </summary>
    void StepSenses()
    {
        double dt = SimConstants.TickSeconds;
        foreach (var b in Bodies.All)
        {
            if (b.Carrier >= 0)
                b.LastHeld = b.Carrier;
            if (b.Kind == Physics.BodyKind.Toy)
            {
                if (b.Noise == Physics.ToyNoise.None)
                    b.Playing = false;
                else if (b.Carrier >= 0)
                {
                    b.Plays = Senses.Toys.Plays(b.Noise);
                    b.Playing = true;
                }
                else
                {
                    b.Plays = Math.Max(0, b.Plays - dt);
                    b.Playing = b.Plays > 0 && !b.Stowed;
                }
            }
            else if (b.Kind == Physics.BodyKind.Lamp && b.Broken)
                b.Burns = Math.Max(0, b.Burns - dt);
        }
        Bodies.All.Where(b => b.Kind == Physics.BodyKind.Lamp && b.Broken && b.Burns <= 0).ToList().ForEach(Bodies.Remove);
    }

    /// <summary>
    /// Host, after the bodies' step: thrown things do what they are (note 728). Brought up short (the change in its speed this
    /// tick, the tick it's let go aside: that's the hands'), a lamp breaks: on or in a car it lights the car's fire grid at the
    /// cell nearest where it broke, on the ground its oil burns a while. Anything else landing hard on the rails knocks them.
    /// (A powder keg goes up by its own rule, <see cref="Sim.Run.SetPieces"/>'s, at the same kind of knock.)
    /// </summary>
    void Thrown()
    {
        var t = Senses;
        List<Physics.Body>? broke = null;
        foreach (var b in Bodies.All)
        {
            if (b.Kind is not (Physics.BodyKind.Lamp or Physics.BodyKind.Crate or Physics.BodyKind.Cargo or Physics.BodyKind.Heavy or Physics.BodyKind.Powder
                    or Physics.BodyKind.Toy or Physics.BodyKind.Loot or Physics.BodyKind.RepairKit or Physics.BodyKind.Extinguisher)
                || b.Stowed || b.Pbd.Particles.Length != 1 || b.Kind == Physics.BodyKind.Lamp && b.Broken)
            {
                _wasMoving.Remove(b.Id);
                _wasHeld.Remove(b.Id);
                continue;
            }
            var p = b.Pbd.Particles[0];
            var local = (p.Position - p.Previous) * (1 / SimConstants.TickSeconds);
            var moving = b.Parent == PlayerState.World || b.Parent >= Train.Frames.Count ? local : Train.Frames[b.Parent].VelocityToWorld(local);
            bool held = b.Carrier >= 0 || b.TakenBy >= 0;
            double knock = !held && !_wasHeld.Contains(b.Id) && _wasMoving.TryGetValue(b.Id, out var was) ? (moving - was).Length : 0;
            _wasMoving[b.Id] = moving;
            if (held)
                _wasHeld.Add(b.Id);
            else
                _wasHeld.Remove(b.Id);
            if (knock <= 0)
                continue;
            if (b.Kind == Physics.BodyKind.Lamp && knock >= t.Thrown.LampBreakSpeed)
                (broke ??= []).Add(b);
            else if (b.Parent == PlayerState.World && knock >= t.Knock.SpeedFrom && OnRail(Physics.Bodies.WorldCentre(b, Train), b.LineHint))
                KnockRail(Physics.Bodies.WorldCentre(b, Train), b.LastHeld);
        }
        foreach (var lamp in broke ?? [])
            BreakLamp(lamp);
    }

    /// <summary>
    /// A lamp broken (note 728): on or in a car with a fire grid (not the engine), its oil lights the cell nearest where it broke
    /// (a fire there already, that cell's hotter), and the lamp's gone; anywhere else (the ground, the engine), it burns where it
    /// lies a while.
    /// </summary>
    void BreakLamp(Physics.Body lamp)
    {
        var t = Senses.Thrown;
        int car = lamp.Parent;
        if (car > 0 && car < Train.Frames.Count && Enemies is { } et && FireGrid.Of(Train, car, et.CarFire.CellSize) is { } grid)
        {
            var local = lamp.Centre;
            Bodies.Remove(lamp);
            _wasMoving.Remove(lamp.Id);
            // C.9: a fire has a cause, and this one's whoever threw the lamp (the record and the news line are FireStarted's, note 851).
            if (_enemies.OfType<CarFire>().FirstOrDefault(f => !f.Gone && f.Attached == car) is { } fire)
                fire.Kindle(grid, local, t.LampFireHeat);
            else
                AddEnemy(id => CarFire.Kindled(id, car, local, t.LampFireHeat, lamp.LastHeld));
            return;
        }
        // On the ground, or on something that doesn't burn (the engine's iron): the glass breaks, and the oil burns where it lies.
        lamp.Broken = true;
        lamp.Burns = t.GroundBurnSeconds;
        _emittersTick = uint.MaxValue;
    }
}
