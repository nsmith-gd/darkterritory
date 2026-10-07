using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A line of the incident report (GDD v1.4 App. D.12): who, and what the clerk says, with a death's fee and refund.</summary>
public sealed record ReportLine(IncidentKind Kind, string Who, string Text, double Fee = 0, double Refund = 0)
{
    /// <summary>When it happened (run seconds), or −1 for a line read at the end (cars lost).</summary>
    public double Seconds { get; init; } = -1;
    /// <summary>Whom it's about, or −1.</summary>
    public int Victim { get; init; } = -1;
    /// <summary>What a death was (<see cref="IncidentKind.Death"/>), or none: the run-end sounds mark the ones the crew did to themselves.</summary>
    public DeathCause Cause { get; init; }
    /// <summary>The bookmarks shown beside it (GDD v1.4 App. D.12), by <see cref="Bookmark.Id"/>.</summary>
    public IReadOnlyList<int> Marks { get; init; } = [];
}

/// <summary>
/// Writing the failure-attribution log (App. C.9) and reading it out as the incident report (D.12), in the settlement
/// clerk's voice (§9): flat, procedural and specific. "Struck by own consist at Mile 4 Halt. Throttle: Dave, 38 km/h. Fee
/// 350. Body recovered. Refund 263."
/// </summary>
public static class IncidentLog
{
    /// <summary>A crewmate's name for the report: the session's name for them, or "Crew n".</summary>
    public static string NameOf(World world, int player) =>
        player < 0 ? "nobody" : world.Names.TryGetValue(player, out var name) && name.Length > 0 ? name : $"Crew {player}";

    /// <summary>"CarHugger" as said: "Car Hugger".</summary>
    public static string Spoken(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");

    static double Seconds(World world) => world.Run?.Seconds ?? world.Tick * SimConstants.TickSeconds;

    static string Kmh(double metresPerSecond) => $"{Math.Abs(metresPerSecond) * 3.6:0} km/h";

    /// <summary>Where a crewmate is, as the clerk would put it: "on the roof of car 3 at km 4", "in the cab at Hollin Halt".</summary>
    public static string Where(World world, in PlayerState s) => $"{Place(world, s)} {At(world, PlayerMotor.WorldPosition(s, world.Train), s.LineHint)}";

    /// <summary>Where on the train a crewmate is, without the mile: "on the roof of car 3", "in the cab".</summary>
    public static string Place(World world, in PlayerState s)
    {
        var train = world.Train;
        return s.Parent switch
        {
            PlayerState.World => "on the line",
            0 when PlayerMotor.InCab(s, train) => "in the cab",
            0 => "on the engine",
            var car when car > 0 && car < train.Frames.Count && train.Frames[car].Shape.Interior is { } room && room.Contains(s.Position) => $"in car {car}",
            var car when car > 0 && car < train.Frames.Count && s.Position.Y >= train.Frames[car].Shape.RoofHeight - 0.2 => $"on the roof of car {car}",
            var car => $"on car {car}",
        };
    }

    /// <summary>
    /// What a crewmate was doing, for the derailment film's name card (GDD v1.4 App. E.5 "the role taken from where they
    /// stood", §12): on the throttle, at the firebox, on the gun, else where they were.
    /// </summary>
    public static string Role(World world, in PlayerState s, int player)
    {
        var train = world.Train;
        if (PlayerMotor.InCab(s, train))
            return Net.CabControls.CanDrive(s, train) && world.Attribution.Driver == player ? "on the throttle" : "at the firebox";
        if (s.Has(PlayerFlags.Seated))
            return "on the gun";
        return Place(world, s);
    }

    /// <summary>
    /// The derailment film's cause card (GDD v1.4 App. E.5), the clerk reading the derail's attribution line (C.9): "Consist
    /// derailed at km 14, 68 km/h. Took the 45 km/h bend at 68 km/h, 23 km/h too fast. Throttle: Dave. Recovery not scheduled."
    /// </summary>
    public static string CauseCard(World world)
    {
        var train = world.Train;
        string at = At(world, train.Frames[0].Origin, train.Dynamics.Distance);
        string cause = world.DerailCause is { Length: > 0 } c ? char.ToUpperInvariant(c[0]) + c[1..].TrimEnd('.') + ". " : "";
        // C.9's contributing action for this cause: the throttle, the forward cannon (the Switchman), the firebox (a Stoker's runaway).
        string blame = world.DerailAction is { Length: > 0 } action ? action.Replace("{actor}", NameOf(world, world.DerailActor)) + " "
            : world.DerailDriver >= 0 ? $"Throttle: {NameOf(world, world.DerailDriver)}. " : "Nobody on the throttle. ";
        return $"Consist derailed {at}, {Kmh(world.DerailSpeed)}. {cause}{blame}Recovery not scheduled.";
    }

    /// <summary>"at Hollin Halt" near a named stop on the plan, else "at km 12".</summary>
    public static string At(World world, Double3 position, double lineHint)
    {
        var line = world.Train.Line;
        double hint = lineHint;
        double s = line.Nearest(position, ref hint).Distance;
        if (world.Route?.Plan is { } plan)
        {
            if (plan.Pois.Where(p => Math.Abs(p.S - s) < 400).OrderBy(p => Math.Abs(p.S - s)).FirstOrDefault() is { } poi)
                return $"at {poi.Name}";
            if (plan.Terminus is { } t && s >= plan.TerminusM - 600)
                return $"at {t.Name}";
            return $"at km {Math.Max(0, plan.Km(s)):0}";
        }
        return $"at km {Math.Max(0, (s - (world.Route?.Gate ?? 0)) / 1000):0}";
    }

    /// <summary>The failure, as the clerk reads it, for each way to die.</summary>
    public static string What(DeathCause cause) => cause switch
    {
        DeathCause.JumpedAtSpeed => "Jumped from the moving train",
        DeathCause.Derailed => "Killed in the derailment",
        DeathCause.Cold => "Froze, left behind",
        DeathCause.Mauled => "Mauled by the hounds",
        DeathCause.Hollow => "Taken by the Hollow",
        DeathCause.Choir or DeathCause.Seized => "Taken by the Choir",
        DeathCause.Taken => "Taken by something wearing a crewmate",
        DeathCause.Dragged => "Dragged off the roof",
        DeathCause.Crushed => "Crushed under a dropped load",
        DeathCause.PulledUnder => "Pulled under between the cars",
        DeathCause.Lamplighter => "Torn down at the lamp",
        DeathCause.Deadman => "Killed retaking the cab",
        DeathCause.Stoker => "Burned driving the Stoker from the firebox",
        DeathCause.Ferryman => "Taken by the Ferryman",
        DeathCause.Climbed => "Killed by a Climber come in off the roof",
        DeathCause.TornOff => "Went off the rails with the rear car",
        DeathCause.Gaunt => "Taken by the Gaunt",
        DeathCause.Struck => "Struck by a tunnel mouth",
        DeathCause.Thrown => "Thrown off on a curve",
        DeathCause.Burned => "Burned in a blazing car",
        DeathCause.Exploded => "Killed when a powder car went up",
        DeathCause.Poisoned => "Gassed by the chemicals a cannon was fired beside",
        DeathCause.Keg => "Killed when a powder keg went up",
        DeathCause.Leak => "Gassed by a leaking chemical hose",
        DeathCause.Wreckage => "Crushed when the wreck shifted",
        DeathCause.Gnawed => "Eaten by the gnawers",
        DeathCause.Replaced => "Replaced",
        DeathCause.Nested => "Killed by Followers nested aboard",
        DeathCause.Drift => "Fell from the moving train",
        DeathCause.Eaten => "Swallowed by the Car Hugger",
        DeathCause.Suffocated => "Smothered by Tippy Toesie",
        DeathCause.Devoured => "Eaten by the Ribbits",
        DeathCause.Drained => "Drained by a Soot Child",
        DeathCause.Carried => "Carried off to the Whistler's nest",
        DeathCause.Uncoupled => "Taken with the caboose by the Passenger",
        _ => cause.ToString(),
    };

    /// <summary>
    /// Host: a crewmate died this tick (the tick their body went down). The contributing action is the one C.9's table names
    /// for that failure: the throttle for a train's own dangers, the crane for a dropped load, the lamp for a fire, who last
    /// fired for the Stoker, and for any other GRAB death the nearest living crewmate and how far off they were.
    /// </summary>
    public static Incident Death(World world, int victim, in PlayerState s, Body? body, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var a = world.Attribution;
        var train = world.Train;
        var at = PlayerMotor.WorldPosition(s, train);
        int actor = -1;
        string action;
        switch (s.Death)
        {
            case DeathCause.JumpedAtSpeed or DeathCause.Drift:
                actor = victim;
                action = $"The train at {Kmh(train.Dynamics.Speed)}.";
                break;
            case DeathCause.Cold:
                {
                    double back = train.Dynamics.Consist.Vehicles.Min(v => (train.Frames[v.Id].Origin - at).Length);
                    actor = a.Driver;
                    action = $"Throttle: {{actor}}. {back:0} m from the train.";
                    break;
                }
            case DeathCause.Derailed or DeathCause.Struck or DeathCause.Thrown or DeathCause.TornOff:
                // In a derailment, its own contributing action (note 190: the forward cannon for the Switchman, the firebox for
                // the Stoker), and the speed it came off at.
                if (world.Derailed && world.DerailAction is { Length: > 0 } derail)
                {
                    actor = world.DerailActor;
                    action = $"{derail.TrimEnd('.')}, {Kmh(world.DerailSpeed)}.";
                }
                else
                {
                    actor = world.Derailed ? world.DerailDriver : a.Driver;
                    action = $"Throttle: {{actor}}, {Kmh(world.Derailed ? world.DerailSpeed : train.Dynamics.Speed)}.";
                }
                break;
            case DeathCause.Crushed:
                actor = a.CraneOperator;
                action = "Crane: {actor}.";
                break;
            case DeathCause.Burned or DeathCause.Exploded:
                actor = s.Parent > 0 ? a.LampLitBy(s.Parent) : -1;
                action = actor >= 0 ? "Lamp lit by {actor}." : s.Parent > 0 && s.Parent < train.Vehicles.Count && train.Vehicles[s.Parent].LampLit
                    ? "Lamp lit since the yard." : "Lamp out.";
                break;
            case DeathCause.Poisoned:
                actor = a.Gasser;
                action = actor >= 0 ? "Cannon fired by {actor}." : "Nobody fired it.";
                break;
            case DeathCause.Keg:
                actor = world.Run?.KegBy ?? -1;
                action = actor >= 0 ? "Last carried by {actor}." : "Nobody had carried it.";
                break;
            case DeathCause.Leak:
                actor = world.Run?.HoseHand ?? -1;
                action = actor >= 0 ? "Hose put on by {actor}." : "Nobody put it on.";
                break;
            case DeathCause.Wreckage:
                actor = world.Run?.WreckBy ?? -1;
                action = actor >= 0 ? "Last piece pulled out by {actor}." : "Nobody had touched it.";
                break;
            case DeathCause.Seized:
                {
                    // C.9: the victim's share of the build's loudness, and the top contributor if that wasn't them.
                    double all = world.ChoirShares.Values.Sum();
                    int share = all > 0 ? (int)Math.Round(100 * world.ChoirShare(victim) / all) : 0;
                    actor = world.ChoirLoudest;
                    action = actor < 0 ? "Nobody was loud." : actor == victim ? $"Loudest on the line: {{actor}}, {share}% of the noise."
                        : $"{share}% of the noise. Loudest on the line: {{actor}}.";
                    break;
                }
            case DeathCause.Stoker:
                actor = a.Fireman;
                action = actor >= 0 ? $"Last fired: {{actor}}, {Math.Max(0, Seconds(world) - a.FiredAt):0} s before." : "Nobody had fired it.";
                break;
            default:
                {
                    // Any other GRAB death: the nearest living crewmate, and their distance (C.9's last row).
                    (actor, action) = Nearest(world, at, crew, victim);
                    break;
                }
        }
        return new Incident(IncidentKind.Death, Seconds(world), victim, What(s.Death), Where(world, s), actor, action, s.Death, body?.Id ?? -1);
    }

    /// <summary>A record of C.9's that isn't about a crewmate's death, rescue or the night's end: a creature's or the line's doing.</summary>
    public static bool IsEvent(IncidentKind kind) => kind >= IncidentKind.Struck && kind != IncidentKind.Drawn;

    /// <summary>
    /// A record of something that happened to the train, not to a crewmate (note 190: C.9's rows that aren't deaths): what,
    /// where (<paramref name="car"/>'s place on the line, else the engine's), and the contributing action and who made it.
    /// </summary>
    public static Incident Event(World world, IncidentKind kind, string what, int actor, string action, int car = 0)
    {
        var train = world.Train;
        return Event(world, kind, what, actor, action, train.Frames[car >= 0 && car < train.Frames.Count ? car : 0].Origin);
    }

    /// <summary>The same, somewhere in the world (where the thing was).</summary>
    public static Incident Event(World world, IncidentKind kind, string what, int actor, string action, Double3 at) =>
        new(kind, Seconds(world), -1, what, At(world, at, world.Train.Dynamics.Distance), actor, action);

    /// <summary>
    /// C.9's Track Doll and track debris rows: struck, with who was on the throttle and the speed at impact. "Struck the Track
    /// Doll at km 4. Throttle: Dave, 38 km/h."
    /// </summary>
    public static Incident Struck(World world, string what)
    {
        int driver = world.Attribution.Driver;
        string speed = Kmh(world.Train.Dynamics.Speed);
        return Event(world, IncidentKind.Struck, what, driver, driver >= 0 ? $"Throttle: {{actor}}, {speed}." : $"Nobody on the throttle, {speed}.");
    }

    /// <summary>
    /// C.9's Fire Flies row, in the clerk's flat voice (T131, the director: "'Nobody lit that lamp' set car 2 alight": car lamps
    /// start lit, note 269, so it always said that): what drew them, the car's lamp, and whether it had burned since the yard
    /// or who relit it; and, as a shut car keeps them out (note 286), the way in it was left with. Returns the actor (who
    /// relit it, or −1) and the line.
    /// </summary>
    public static (int Actor, string Action) FireFliesDrawn(World world, int car)
    {
        var train = world.Train;
        int lit = world.Attribution.LampLitBy(car);
        string drawn = lit >= 0 ? "Drawn by the car's lamp, relit by {actor}." : "Drawn by the car's lamp, lit since the yard.";
        if (car <= 0 || car >= train.Vehicles.Count)
            return (lit, drawn);
        var v = train.Vehicles[car];
        int doors = v.DoorsOpen & ~(1 << Train.CarShape.HatchBit);
        string way = v.Breached ? " In through the breach." : doors != 0 ? " Door left open." : (v.DoorsOpen & (1 << Train.CarShape.HatchBit)) != 0 ? " Hatch left open." : "";
        return (lit, drawn + way);
    }

    /// <summary>
    /// C.9's Switchman row: whether the forward cannon was crewed this tick, and by whom (the lowest id, if more than one
    /// forward gun was). A forward cannon is one mounted facing the way the train goes, on a vehicle still on the engine.
    /// </summary>
    public static (int Actor, string Action) ForwardCannon(World world)
    {
        var train = world.Train;
        var guns = train.Dynamics.Consist.Vehicles.Select(v => v.Id).Where(id => train.Vehicles[id] is { HasGun: true, Gun.Facing: < 0 }).ToList();
        if (guns.Count == 0)
            return (-1, "No forward cannon on the consist.");
        int gunner = world.Combat?.Guns is { } t
            ? world.CrewThisTick.Where(c => c.State.Alive && Combat.Guns.MannedGun(c.State, train, t) is { } g && guns.Contains(g))
                .Select(c => c.Id).DefaultIfEmpty(-1).Min()
            : -1;
        return gunner >= 0 ? (gunner, "Forward cannon crewed by {actor}.") : (-1, "Forward cannon not crewed.");
    }

    /// <summary>C.9's Stoker row: who last fuelled or tended the firebox, and how long it had gone unattended since.</summary>
    public static (int Actor, string Action) Firebox(World world)
    {
        var a = world.Attribution;
        double since = Math.Max(0, (world.Run?.Seconds ?? 0) - a.TendedAt);
        return a.Tender >= 0 ? (a.Tender, $"Firebox last tended: {{actor}}, unattended {since:0} s.") : (-1, "Nobody had tended the firebox.");
    }

    /// <summary>C.9's last row, for a crewmate or a thing: the nearest living crewmate to <paramref name="at"/>, and how far.</summary>
    public static (int Actor, string Action) Nearest(World world, Double3 at, IEnumerable<(int Id, PlayerState State)> crew, int except = -1)
    {
        var near = crew.Where(c => c.Id != except && c.State.Alive)
            .Select(c => (c.Id, D: (PlayerMotor.WorldPosition(c.State, world.Train) - at).Length))
            .OrderBy(c => c.D).ThenBy(c => c.Id).FirstOrDefault((-1, 0));
        return near.Item1 >= 0 ? (near.Item1, $"Nearest crew: {{actor}}, {near.Item2:0} m.") : (-1, "Nobody near.");
    }

    /// <summary>A GRAB begins (C.9's first row, and D.12's auto-bookmark later): who has whom, where.</summary>
    public static Incident Grab(World world, int victim, in PlayerState s, string what) =>
        new(IncidentKind.Grab, Seconds(world), victim, what, Where(world, s));

    /// <summary>
    /// The incident report (D.12): deaths with their cause line and itemised fee and refund, rescues, the boiler, cars lost
    /// and how the night ended, in order.
    /// </summary>
    /// <param name="fee">The crew-loss fee per death (D.9); <paramref name="refund"/> what a body home earns back.</param>
    /// <param name="home">Whether a body (by id) came home.</param>
    public static List<ReportLine> Lines(World world, double fee, double refund, Func<int, bool> home)
    {
        var lines = new List<ReportLine>();
        foreach (var i in world.Attribution.Log.Where(i => i.Kind != IncidentKind.Grab))
        {
            string who = i.Victim >= 0 ? NameOf(world, i.Victim) : "";
            string action = i.Action.Replace("{actor}", NameOf(world, i.Actor));
            if (i.Kind == IncidentKind.Death)
            {
                bool recovered = i.Body >= 0 && home(i.Body);
                string settle = fee > 0 ? recovered ? $" Fee {fee:0}. Body recovered. Refund {refund:0}." : $" Fee {fee:0}. Body not recovered." : "";
                lines.Add(new ReportLine(i.Kind, who, $"{i.What} {i.Where}. {action}{settle}".Trim(), fee, recovered ? refund : 0) { Seconds = i.Seconds, Victim = i.Victim, Cause = i.Cause });
            }
            else
                lines.Add(new ReportLine(i.Kind, who, $"{i.What} {i.Where}. {action}".Trim()) { Seconds = i.Seconds, Victim = i.Victim });
        }
        // D.12 "what the dead voted for", hidden from the living till now (D.11): each creature voted for, and by whom.
        if (world.Director is { } director)
            foreach (var voted in director.Votes.GroupBy(v => v.Kind).OrderBy(g => g.Min(v => v.Voter)))
                lines.Add(new ReportLine(IncidentKind.Voted, "", $"The dead voted for the {Spoken(voted.Key.ToString())}: {string.Join(", ", voted.Select(v => NameOf(world, v.Voter)))}."));
        return lines;
    }
}
