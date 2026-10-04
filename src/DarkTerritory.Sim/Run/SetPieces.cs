using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// GDD §18's facility set pieces (WP15, ARCHITECTURE §8 note 185), on the host: the grain elevator's one spout, which
/// loads whichever car is under it while someone holds its lever, so the train's walked under it a car at a time; the
/// slaughterhouse's livestock ramp, two or more driving the herd up it, loud the whole time; the chemical works' hose,
/// which leaks if nobody minds its pressure and tears if the train moves with it on; and the depot's powder kegs, which go
/// up when they're dropped or thrown hard. Each works whether or not the engine is "stopped" there, so a train walked a
/// car's length under the spout still loads.
/// </summary>
public sealed partial class Run
{
    // Host: each powder keg's world velocity last tick, and whether it was in someone's hands then; who last carried it;
    // the kegs a blast has set off for next tick.
    readonly Dictionary<int, Double3> _kegVelocity = new();
    readonly HashSet<int> _kegHeld = new(), _kegFused = new();
    readonly Dictionary<int, int> _kegBy = new();
    // Host: last tick's blasts (and who set each off), each crewmate's share of a leak's dose not yet a whole point, and
    // who's held Use at a hose stand for how long.
    readonly List<(Double3 At, int By)> _blasts = new();
    readonly Dictionary<int, double> _dose = new(), _hoseHold = new();

    /// <summary>The set pieces' numbers (facilities.json), once the sites are laid out.</summary>
    public FacilityTuning? FacilityTuning => _facilityTuning;

    /// <summary>The herd's noise (spec D.2 livestock ramp: "raises Choir floor while active"): the floor while the herd at this stop is stirred.</summary>
    public double HerdFloor => _facilityTuning is { } t && CurrentSite is { Stirred: true } ? t.Ramp.ChoirFloor : 0;

    /// <summary>
    /// In among the chemical works' tanks and pipe racks (GDD §18 "do not fire indoors"): within <see cref="HoseTuning.IndoorsM"/> of a
    /// works' fluid gantry. A cannon fired from here gasses its gunner and whoever's by its car, as one fired beside a
    /// chemicals car does (note 182).
    /// </summary>
    public bool Indoors(Double3 at) =>
        _facilityTuning is { } t && _sites.Any(s => s is not null && s.Has(ModuleKind.Hose) && Flat(at - s.HoseStand) <= t.Hose.IndoorsM);

    static double Flat(Double3 d) => (d with { Y = 0 }).Length;

    /// <summary>The cargo car (in any rake) whose middle is under the spout, within the spout's tolerance; null if none.</summary>
    public Vehicle? CarUnderSpout(TrainOnLine train, Site site) =>
        _facilityTuning is not { } t || !site.Has(ModuleKind.Spout) ? null
        : train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Id < train.Frames.Count && Flat(train.Frames[v.Id].Origin - site.Spout) <= t.Spout.Tolerance)
            .OrderBy(v => Flat(train.Frames[v.Id].Origin - site.Spout)).FirstOrDefault();

    /// <summary>A spout with grain in it, within reach of its lever (on foot).</summary>
    public Site? SpoutLeverInReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (Over || !s.Alive || s.Parent != PlayerState.World || _facilityTuning is not { } t)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var site in _sites)
            if (site is { Bin: > 0 } && site.Has(ModuleKind.Spout)
                && PlayerMotor.Grips(s, train, hand, site.SpoutLever, (at - site.SpoutLever).Length <= t.Spout.LeverReach))
                return site;
        return null;
    }

    /// <summary>In the pen of a herd with head left in it (on foot): where you drive them from.</summary>
    public Site? InPen(in PlayerState s, TrainOnLine train)
    {
        if (Over || !s.Alive || s.Parent != PlayerState.World)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var site in _sites)
            if (site is { Head: > 0 } && site.Has(ModuleKind.Ramp) && Flat(at - site.Pen) <= site.PenRadius + 1)
                return site;
        return null;
    }

    /// <summary>At a fluid gantry's stand (on foot).</summary>
    public Site? AtHoseStand(in PlayerState s, TrainOnLine train)
    {
        if (Over || !s.Alive || s.Parent != PlayerState.World || _facilityTuning is not { } t)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var site in _sites)
            if (site is not null && site.Has(ModuleKind.Hose) && Flat(at - site.HoseStand) <= t.Hose.Reach)
                return site;
        return null;
    }

    /// <summary>How far through connecting or disconnecting the hose a player is (0..1), for the HUD.</summary>
    public double HoseHold(int playerId) => _facilityTuning is { } t ? Math.Min(1, _hoseHold.GetValueOrDefault(playerId) / t.Hose.ConnectSeconds) : 0;

    /// <summary>The cargo car with room nearest a point, within reach of it, in any rake; null if none.</summary>
    static Vehicle? CarWithRoom(TrainOnLine train, Double3 at, double reach) =>
        train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6 && v.Id < train.Frames.Count && Flat(train.Frames[v.Id].Origin - at) <= reach)
            .OrderBy(v => Flat(train.Frames[v.Id].Origin - at)).FirstOrDefault();

    /// <summary>The cargo car with room the ramp's next head goes into, if one stands at its top.</summary>
    public Vehicle? CarAtRamp(TrainOnLine train, Site site) =>
        _facilityTuning is { } t && site.Has(ModuleKind.Ramp) ? CarWithRoom(train, site.RampTop, t.Ramp.CarReach) : null;

    /// <summary>The cargo car with room the hose would go on, if one stands by the stand.</summary>
    public Vehicle? CarAtHose(TrainOnLine train, Site site) =>
        _facilityTuning is { } t && site.Has(ModuleKind.Hose) ? CarWithRoom(train, site.HoseStand, t.Hose.CarReach) : null;

    /// <summary>A player's hands on the set pieces this tick (from <see cref="CrewAct"/>): the lever, the herd, the hose.</summary>
    void SetPiecesAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand)
    {
        if (_facilityTuning is not { } t)
            return;
        bool use = intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5;
        if (use && SpoutLeverInReach(s, train, hand) is { } spout && (spout.Pourer < 0 || playerId < spout.Pourer))
            spout.Pourer = playerId;
        if (use && InPen(s, train) is { } pen)
            pen.Herders.Add(playerId);
        if (AtHoseStand(s, train) is not { } stand)
        {
            _hoseHold.Remove(playerId);
            return;
        }
        // Standing by it is minding it; holding Use there long enough puts the hose on the car by it, or takes it off.
        stand.Attended = true;
        if (!use)
        {
            _hoseHold.Remove(playerId);
            return;
        }
        double before = _hoseHold.GetValueOrDefault(playerId);
        _hoseHold[playerId] = before + SimConstants.TickSeconds;
        if (before >= t.Hose.ConnectSeconds || before + SimConstants.TickSeconds < t.Hose.ConnectSeconds)
            return;
        if (stand.HoseCar >= 0)
        {
            stand.HoseCar = -1;
            stand.Pressure = 0;
        }
        else if (CarAtHose(train, stand) is { } car)
        {
            stand.HoseCar = car.Id;
            stand.Pressure = 0;
            HoseHand = playerId;
        }
    }

    /// <summary>Who last put a hose on a car: a leak's contributing action (App. C.9).</summary>
    public int HoseHand { get; private set; } = -1;

    /// <summary>
    /// The harm the set pieces do a player this tick (from the world's crew step, so it lands this tick): last tick's keg
    /// blasts and wreck heaps shifting, and a leak's gas, a point at a time.
    /// </summary>
    public IEnumerable<DamageEvent> Harm(in PlayerState s, int playerId, TrainOnLine train)
    {
        if (!s.Alive || _facilityTuning is not { } t)
            return [];
        var hurt = new List<DamageEvent>();
        var at = PlayerMotor.WorldPosition(s, train) + Double3.Up;
        var k = t.Kegs;
        foreach (var blast in _blasts)
        {
            double d = (at - blast.At).Length;
            if (d > k.Radius)
                continue;
            double share = d <= k.KillRadius ? 1 : 1 - (d - k.KillRadius) / Math.Max(1e-6, k.Radius - k.KillRadius);
            int amount = (int)Math.Round(k.Damage * share);
            if (amount > 0)
                hurt.Add(new DamageEvent(playerId, amount, DeathCause.Keg, Lethal: true));
        }
        // A wreck heap shifting (note 187): everyone by it.
        foreach (var shift in _shifts)
            if (Flat(at - shift.At) <= shift.Radius)
                hurt.Add(new DamageEvent(playerId, t.Wreck.Damage, DeathCause.Wreckage, Lethal: true));
        bool gassed = _sites.Any(site => site is { Leaking: true } && Flat(at - site.HoseStand) <= t.Hose.LeakRadius);
        if (gassed)
        {
            double dose = _dose.GetValueOrDefault(playerId) + t.Hose.LeakDamagePerSecond * SimConstants.TickSeconds;
            int whole = (int)dose;
            _dose[playerId] = dose - whole;
            if (whole > 0)
                hurt.Add(new DamageEvent(playerId, whole, DeathCause.Leak, Lethal: true));
        }
        else
            _dose.Remove(playerId);
        return hurt;
    }

    /// <summary>Who last carried the keg that went up nearest a player: a keg death's contributing action (App. C.9).</summary>
    public int KegBy { get; private set; } = -1;

    /// <summary>Host: a tick of every set piece (whether or not the engine's stopped by it).</summary>
    void StepSetPieces(World world, FacilityTuning t, double dt)
    {
        var train = world.Train;
        _blasts.Clear();
        _shifts.Clear();
        foreach (var site in _sites)
        {
            if (site is null)
                continue;
            var cargo = site.Feature.Facility is { } kind ? t.CargoOf(kind) : CargoKind.Goods;
            if (site.Has(ModuleKind.Spout))
                Pour(train, site, t.Spout, cargo, dt);
            if (site.Has(ModuleKind.Ramp))
                Drive(train, site, t.Ramp, cargo, dt);
            if (site.Has(ModuleKind.Hose))
                Flow(train, site, t.Hose, cargo, dt);
            // The wreck yard's heaps (WP15b, note 187).
            if (site.Has(ModuleKind.Wreck))
                Wreckage(world, site, t.Wreck, dt);
        }
        Kegs(world, t.Kegs);
    }

    /// <summary>
    /// The spout (GDD §18 "one spout, one car at a time"): while someone holds its lever, grain comes down. Into the car
    /// under it, if there's one with room; onto the ballast if not; and a full car under it overflows, which strains it
    /// (spec D.2's chute: "overfill damages car and spills").
    /// </summary>
    void Pour(TrainOnLine train, Site site, SpoutTuning sp, CargoKind cargo, double dt)
    {
        site.Pouring = !Over && site.Pourer >= 0 && site.Bin > 1e-9;
        site.Pourer = -1;
        if (!site.Pouring)
            return;
        double pour = Math.Min(sp.PourPerSecond * dt, site.Bin);
        site.Bin = Math.Max(0, site.Bin - pour);
        if (CarUnderSpout(train, site) is not { } car)
            return; // on the ballast
        double taken = Math.Min(pour, Math.Max(0, 1 - car.Load));
        if (taken > 0)
        {
            car.Load += taken;
            car.Cargo = cargo;
        }
        car.Integrity = Math.Max(0, car.Integrity - (pour - taken) * sp.OverfillDamagePerLoad);
    }

    /// <summary>
    /// The livestock ramp (spec D.2: "herd animals up a ramp into a stock car. 2–3 crew"): with enough in the pen driving
    /// them and a car at the ramp's top with room, the next one goes up it; each is a share of a car-load.
    /// </summary>
    void Drive(TrainOnLine train, Site site, RampTuning r, CargoKind cargo, double dt)
    {
        int herders = site.Herders.Count;
        site.Herders.Clear();
        var car = site.Head > 0 && !Over ? CarWithRoom(train, site.RampTop, r.CarReach) : null;
        site.Herding = car is not null && herders >= r.Herders;
        if (!site.Herding)
            return;
        double pace = (1 + r.ExtraHerder * (herders - r.Herders)) / r.SecondsPerHead;
        site.Herd += pace * dt;
        if (site.Herd < 1)
            return;
        site.Herd = 0;
        site.Head--;
        car!.Load = Math.Min(1, car.Load + r.LoadPerHead);
        car.Cargo = cargo;
    }

    /// <summary>
    /// The fluid gantry (spec D.2: "connect hoses, monitor pressure, disconnect cleanly. Leaks if unattended. Chemical
    /// spill contaminates the car"): on a car, it fills it, and its pressure climbs unless someone's at the stand; at the
    /// top it leaks, gassing whoever's near and spoiling the car's load. The train moving off with it on tears it.
    /// </summary>
    void Flow(TrainOnLine train, Site site, HoseTuning h, CargoKind cargo, double dt)
    {
        bool attended = site.Attended;
        site.Attended = false;
        site.Leak = Math.Max(0, site.Leak - dt);
        if (site.HoseCar < 0 || site.HoseCar >= train.Vehicles.Count)
        {
            site.HoseCar = -1;
            site.Pressure = Math.Max(0, site.Pressure - h.Bleed * dt);
            return;
        }
        var car = train.Vehicles[site.HoseCar];
        if (Flat(train.Frames[car.Id].Origin - site.HoseStand) > h.CarReach + h.TearSlack)
        {
            // Torn off: it sprays until the line's empty, and what was in the car is fouled.
            site.HoseCar = -1;
            site.Pressure = 0;
            site.Leak = Math.Max(site.Leak, h.TornSeconds);
            if (car.Load > 0.01)
                car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - h.TornSpoil);
            return;
        }
        double flow = Math.Min(h.FlowPerSecond * dt, Math.Max(0, 1 - car.Load));
        if (flow > 0)
        {
            car.Load += flow;
            car.Cargo = cargo;
        }
        site.Pressure = Math.Clamp(site.Pressure + (attended ? -h.Bleed : h.PressureRise) * dt, 0, 1);
        if (site.Leaking && car.Load > 0.01)
            car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - h.SpoilPerSecond * dt);
    }

    /// <summary>
    /// The powder (GDD §18-19: "best payout, worst cargo", "explodes"): a keg of it that's brought up short, from a throw or
    /// a fall off a roof, goes up, and sets off the kegs near it the tick after. Set down by hand, it's fine. The blast's
    /// harm lands on the crew next tick (<see cref="Harm"/>), and every client sees and hears it.
    /// </summary>
    void Kegs(World world, KegTuning k)
    {
        var train = world.Train;
        var blown = new List<Physics.Body>();
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind is not (Physics.BodyKind.Cargo or Physics.BodyKind.Heavy) || b.Cargo != CargoKind.Ammunition || b.Stowed)
                continue;
            if (_kegFused.Contains(b.Id))
            {
                blown.Add(b);
                continue;
            }
            var p = b.Pbd.Particles[0];
            var local = (p.Position - p.Previous) * (1 / SimConstants.TickSeconds);
            var velocity = b.Parent == PlayerState.World || b.Parent >= train.Frames.Count ? local : train.Frames[b.Parent].VelocityToWorld(local);
            bool held = b.Carrier >= 0;
            if (held)
                _kegBy[b.Id] = b.Carrier;
            // Let go of this tick (put down or thrown), its speed is the hands': no knock in that.
            if (!held && !_kegHeld.Contains(b.Id) && _kegVelocity.TryGetValue(b.Id, out var was) && (velocity - was).Length >= k.BlowSpeed)
                blown.Add(b);
            _kegVelocity[b.Id] = velocity;
            if (held)
                _kegHeld.Add(b.Id);
            else
                _kegHeld.Remove(b.Id);
        }
        foreach (var b in blown)
        {
            var at = Physics.Bodies.WorldCentre(b, train);
            int by = _kegBy.GetValueOrDefault(b.Id, -1);
            _blasts.Add((at, by));
            KegBy = by;
            world.Blast(at);
            world.Bodies.Remove(b);
            _kegFused.Remove(b.Id);
            _kegVelocity.Remove(b.Id);
            _kegHeld.Remove(b.Id);
            foreach (var v in train.Vehicles)
                if (v.Id < train.Frames.Count && (train.Frames[v.Id].Origin - at).Length <= k.KillRadius + train.Frames[v.Id].Shape.HalfLength)
                    v.Integrity = Math.Max(0, v.Integrity - k.CarDamage);
        }
        // Whatever powder lay near each blast goes next.
        foreach (var b in world.Bodies.All)
            if (b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy && b.Cargo == CargoKind.Ammunition && !b.Stowed
                && _blasts.Any(x => (Physics.Bodies.WorldCentre(b, train) - x.At).Length <= k.Chain))
                _kegFused.Add(b.Id);
    }
}
