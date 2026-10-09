using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Run;

/// <summary>What a record in the failure-attribution log is about (GDD v1.4 App. C.9).</summary>
/// <remarks>
/// Note 190: the rows that aren't a death, each the tick it happens: the Track Doll or the Sleepers struck (<see cref="Struck"/>),
/// the Fire Flies setting a car alight (<see cref="Fire"/>), Followers nesting (<see cref="Nest"/>), a Grumbler craned
/// aboard (<see cref="Aboard"/>), a Stoker in the firebox (<see cref="Runaway"/>), the Switchman's points (<see cref="Points"/>),
/// and any other PUNISH that held nobody (<see cref="Punished"/>). Note 287: the night's first threat and the draw that
/// brought it (<see cref="Drawn"/>): "Cinder Hounds came first at km 2. Drawn by the whistle: Dave."
/// </remarks>
public enum IncidentKind : byte { Grab, Death, Rescue, Rupture, CarLost, Derailed, Stranded, Voted, Struck, Fire, Nest, Aboard, Runaway, Points, Punished, Drawn, Slain }

/// <summary>
/// One fact for the incident report (C.9, D.12): what happened, to whom and where, and the <b>contributing action</b>: the
/// most recent crew input the rules of that failure name, and who made it. Facts, not fault: no demerits.
/// </summary>
/// <param name="Victim">The player it happened to, or −1 (a car, the train).</param>
/// <param name="What">The failure, in the clerk's words ("Swallowed by the Car Hugger").</param>
/// <param name="Where">Where it happened ("on the roof of car 3 at km 4").</param>
/// <param name="Actor">Who made the contributing action, or −1 for nobody (the crew wasn't involved, or nobody was near).</param>
/// <param name="Action">The contributing action as the clerk reads it, with <c>{actor}</c> where the actor's name goes.</param>
/// <param name="Body">For a death, the body it left (to say at settlement whether it came home).</param>
public sealed record Incident(IncidentKind Kind, double Seconds, int Victim, string What, string Where, int Actor = -1, string Action = "",
    DeathCause Cause = DeathCause.None, int Body = -1);

/// <summary>
/// The failure-attribution log (GDD v1.4 App. C.9): a host-side record written at every GRAB start, death, car loss,
/// boiler rupture, stranding and derailment, and at every rescue. It feeds the incident report and nothing else: never
/// money, progression or the director. It also keeps the crew inputs C.9's table names (who drove, who fired, who lit
/// each car's lamp, who pulled each coupler, who ran the crane, who last held the kit), as the host sees them happen.
/// </summary>
public sealed class Attribution
{
    readonly List<Incident> _log = [];
    readonly Dictionary<int, int> _lampBy = [], _couplerBy = [];
    // Note 511: the cars a loose coupling's dropped pin parted from the train (note 356), nobody's hand on it.
    readonly HashSet<int> _parted = [];
    readonly HashSet<int> _apart = [];

    public IReadOnlyList<Incident> Log => _log;

    /// <summary>The last crewmate at the cab controls: "who was on the throttle".</summary>
    public int Driver { get; private set; } = -1;
    /// <summary>The last crewmate to fire or vent the boiler, and when (run seconds).</summary>
    public int Fireman { get; private set; } = -1;
    public double FiredAt { get; private set; }
    /// <summary>
    /// The last crewmate to fuel or tend the firebox, and when (run seconds): firing and venting, and working its door or
    /// clubbing a Stoker in it. C.9's Stoker row: "who last fuelled or tended the firebox, and how long it had been unattended".
    /// </summary>
    public int Tender { get; private set; } = -1;
    public double TendedAt { get; private set; }
    public int CraneOperator { get; private set; } = -1;
    /// <summary>The last living crewmate with the engineering kit in their hotbar.</summary>
    public int KitHolder { get; private set; } = -1;
    /// <summary>Who last fired a cannon beside a chemicals car (App. B.9; note 182): the gas's contributing action.</summary>
    public int Gasser { get; private set; } = -1;
    public void Gassed(int player) => Gasser = player;

    public void Drove(int player) => Driver = player;
    public void Fired(int player, double seconds) => (Fireman, FiredAt, Tender, TendedAt) = (player, seconds, player, seconds);
    public void Tended(int player, double seconds) => (Tender, TendedAt) = (player, seconds);
    public void Craned(int player) => CraneOperator = player;
    public void HeldKit(int player) => KitHolder = player;
    public void LitLamp(int car, int player) => _lampBy[car] = player;
    public void PulledCoupler(int vehicle, int player) => _couplerBy[vehicle] = player;
    public int LampLitBy(int car) => _lampBy.GetValueOrDefault(car, -1);
    public int CouplerPulledBy(int vehicle) => _couplerBy.GetValueOrDefault(vehicle, -1);
    public void PartedAt(int vehicle) => _parted.Add(vehicle);
    public bool Parted(int vehicle) => _parted.Contains(vehicle);
    /// <summary>A car that came apart, battered and let go (note 576): what parted the train ahead of it.</summary>
    public void CameApart(int vehicle) => _apart.Add(vehicle);
    public bool Apart(int vehicle) => _apart.Contains(vehicle);

    public void Add(Incident incident) => _log.Add(incident);

    public IEnumerable<Incident> Of(IncidentKind kind) => _log.Where(i => i.Kind == kind);
}
