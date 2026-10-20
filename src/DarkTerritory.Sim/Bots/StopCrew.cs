using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>A crew member's part in working a facility stop (GDD §17).</summary>
public enum StopJob : byte
{
    /// <summary>No part: on the roofs as usual, but says whether they're aboard.</summary>
    None,
    /// <summary>On the regulator: stops short of the points, runs the empties in, backs out onto the rest, goes.</summary>
    Driver,
    /// <summary>On the ground: cuts the train, throws the switch, rides in and out in the cab, sets it back.</summary>
    Shunter,
    /// <summary>On the winch's first handle, or its second (spec D.2: "2 mandatory"). With no winch to work, they carry crates.</summary>
    Winch0,
    Winch1,
    /// <summary>Carries the crates off the ground and up into the cars (spec D.2 manual crates).</summary>
    Crates,
}

/// <summary>
/// What a crew working a stop tell each other: who has which part, and where each of them is. People say it on the
/// radio ("I'm in the cab", "on the ground at the points"); bots share this instead. It carries only what they'd say.
/// Everything they do still goes through intent (CLAUDE.md), and what they see comes from their own client's world.
/// </summary>
public sealed partial class CrewCalls
{
    public readonly record struct Call(StopJob Job, int Vehicle, bool Alive);

    readonly SortedDictionary<int, Call> _crew = new();
    readonly Dictionary<int, uint> _followed = new();

    /// <summary>"Stand still, there's something on your back" (App. A.3): an observer calls who a Follower's on.</summary>
    public void Followed(int playerId, uint tick) => Heard(-1, "followed", playerId, true, () => _followed[playerId] = Voice is null ? tick : _now);
    /// <summary>Whether someone's called in the last second that there's a Follower on this player.</summary>
    public bool IsFollowed(int playerId, uint tick) => _followed.TryGetValue(playerId, out var at) && tick - at <= SimConstants.TickRate;
    readonly Dictionary<int, int> _carryingTo = new();
    readonly Dictionary<int, Double3> _standing = new();

    /// <summary>"I'm here": a bot that knows its player id says where it stands, so a hand coming to help knows which end is free.</summary>
    public void Standing(int playerId, Double3 world) => Heard(playerId, "standing", playerId, world, () => _standing[playerId] = world);
    public Double3? Where(int playerId) => _standing.TryGetValue(playerId, out var at) ? at : null;

    /// <summary>"This one's for car n": a crate hand says which car the crate in its arms is going to (−1 when empty-handed).</summary>
    public void CarryingTo(int member, int car) => Heard(member, "carrying", member, car, () => _carryingTo[member] = car);
    /// <summary>Crates other hands are taking to a car.</summary>
    public int BoundFor(int car, int except) => _carryingTo.Count(c => c.Key != except && c.Value == car);

    /// <summary>A crew member says where they are: a vehicle id, or <see cref="PlayerState.World"/> on the ground.</summary>
    public void Say(int member, StopJob job, in PlayerState s)
    {
        var call = new Call(job, s.Alive ? s.Parent : PlayerState.World, s.Alive);
        Heard(member, "say", member, call, () =>
        {
            _crew[member] = call;
            _held.Add(job);
        });
    }

    /// <summary>Every part anyone has said they have: one nobody alive has now was left, by a death or a stand-in.</summary>
    readonly HashSet<StopJob> _held = [];

    public IEnumerable<Call> Crew => _crew.Values;

    /// <summary>
    /// Whether this member should take over a part nobody alive has any more (the shunter mauled, say, or the winch hand who
    /// stood in for them): as a crew would sort it out on the radio. The shunter's goes to the first of the rest alive with a
    /// part of their own; a winch part to the first crate hand (a frontier:7 night lost its shunter on the way, the crane's
    /// operator stood in, and with nobody at the crane the Foundry loaded nothing).
    /// </summary>
    public bool StandIn(int member, StopJob part) =>
        !Has(part) && _held.Contains(part) && _crew.Where(c => c.Value.Alive && (part is StopJob.Winch0 or StopJob.Winch1
                ? c.Value.Job == StopJob.Crates : c.Value.Job is not (StopJob.None or StopJob.Driver)))
            .Select(c => c.Key).DefaultIfEmpty(-1).Min() == member;
    public bool Has(StopJob job) => _crew.Values.Any(c => c.Alive && c.Job == job);

    /// <summary>
    /// Note 609: the hand that throws a stop's points while the shunter's cutting the train: the first alive of the winch's
    /// first handle, then the crate hands, then the winch's second (a small crew's gunner, riding at the back), by member.
    /// </summary>
    public bool SwitchHand(int member) => SwitchHands.Select(c => c.Key).DefaultIfEmpty(-1).First() == member;
    /// <summary>Note 609: someone alive to throw a stop's points for the shunter (<see cref="SwitchHand"/>).</summary>
    public bool HasSwitchHand => SwitchHands.Any();
    IEnumerable<KeyValuePair<int, Call>> SwitchHands => _crew.Where(c => c.Value.Alive && c.Value.Job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates)
        .OrderBy(c => c.Value.Job switch { StopJob.Winch0 => 0, StopJob.Crates => 1, _ => 2 }).ThenBy(c => c.Key);
    /// <summary>Whether this member is among the first <paramref name="n"/> alive with that part (by member number).</summary>
    public bool AmongFirst(int member, StopJob job, int n) =>
        _crew.Where(c => c.Value.Alive && c.Value.Job == job).Select(c => c.Key).Take(n).Contains(member);
    /// <summary>Whether anyone alive is on <paramref name="job"/>.</summary>
    public bool AnyOn(StopJob job) => _crew.Values.Any(c => c.Alive && c.Job == job);
    // Note 261: the stop worked by the crew that's there. Spec D.2's "Crew" column counts people at a module, whoever they
    // are: the shunter's free once the train's in, the driver can get down (facilities.json crew.driverWorks), and people
    // playing are hands as much as bots are (crew.peopleAreHands).

    /// <summary>The driver gets down to make up a stop's crew (facilities.json crew.driverWorks; the driver says so).</summary>
    public bool DriverWorks { get; set; }
    /// <summary>People playing count as hands at a stop (facilities.json crew.peopleAreHands; the driver says so).</summary>
    public bool PeopleAreHands { get; set; } = true;
    /// <summary>People playing, alive, as the driver sees them (note 261): what anyone can see, so not over the voice.</summary>
    public int People { get; private set; }
    public void Playing(int people) => People = people;
    /// <summary>People counted as hands at a stop.</summary>
    public int PeopleHands => PeopleAreHands ? People : 0;

    /// <summary>The driver's alive and gets down to work a stop when there's no one else.</summary>
    public bool DriverHand => DriverWorks && Has(StopJob.Driver);
    /// <summary>Someone to throw the spur's switch and set it back: the shunter, or with none, the driver.</summary>
    public bool CanShunt => Has(StopJob.Shunter) || DriverHand;

    /// <summary>
    /// Bots for a pair module (spec D.2: the capstan winch and the gantry crane "2 mandatory", the livestock ramp 2–3), at
    /// most two: the winch pair, then the shunter (spare once the train's in), then the driver.
    /// </summary>
    public int PairBots => Math.Min(2, (Has(StopJob.Winch0) ? 1 : 0) + (Has(StopJob.Winch1) ? 1 : 0) + (Has(StopJob.Shunter) ? 1 : 0) + (DriverHand ? 1 : 0));
    /// <summary>The bots alone can work a pair module (and the train in and out).</summary>
    public bool CanWorkPair => CanShunt && PairBots >= 2;
    /// <summary>The bots and the people playing between them can: the bots take their places, and the rest is people's.</summary>
    public bool PairWithPeople => CanShunt && PairBots + PeopleHands >= 2;

    /// <summary>
    /// The driver's call that the loading's got nowhere for a while (facilities.json crew.peopleWait): whatever the bots can't
    /// do without people playing, nobody's doing, so the bots leave it (note 261).
    /// </summary>
    public bool PeopleIdle { get; private set; }
    public void Idle(bool idle) => PeopleIdle = idle;
    /// <summary>A pair module's worth being at now: the bots can work it, or people are making up the pair and getting somewhere.</summary>
    public bool PairNow => CanWorkPair || PairWithPeople && !PeopleIdle;
    /// <summary>Someone to mind the hose (D.2 fluid gantry "1–2"): the first of the pair, or a spare hand in their place.</summary>
    public bool HoseHand => Has(StopJob.Winch0) || PairPart(StopJob.Shunter) == StopJob.Winch0 || PairPart(StopJob.Driver) == StopJob.Winch0;

    /// <summary>
    /// The part of the pair a spare hand takes at the loading (note 261): the shunter the first one with nobody alive on it,
    /// the driver the next. None when the pair's whole without them.
    /// </summary>
    public StopJob PairPart(StopJob who)
    {
        var missing = new[] { StopJob.Winch0, StopJob.Winch1 }.Where(j => !Has(j)).ToList();
        int i = who == StopJob.Shunter ? 0 : who == StopJob.Driver && DriverHand ? Has(StopJob.Shunter) ? 1 : 0 : int.MaxValue;
        return i < missing.Count ? missing[i] : StopJob.None;
    }

    /// <summary>Bots with a part that carries crates (the winch pair carry where there's no winch, or once it's done).</summary>
    int CrateBots => _crew.Values.Count(c => c.Alive && c.Job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates);

    /// <summary>
    /// Whether a spare hand carries crates too (note 261): the shunter where fewer than two bots do (one or two carry, D.2's
    /// "heavy items need two"); the driver where nobody else does at all.
    /// </summary>
    public bool Carries(StopJob who) => who switch
    {
        StopJob.Shunter => Has(StopJob.Shunter) && (CrateBots < 2 || PairAtWork),
        StopJob.Driver => DriverHand && CrateBots == 0 && !Has(StopJob.Shunter),
        _ => false,
    };

    /// <summary>
    /// Note 617: the driver's call that the pair are at their own work (the winch, the herd, the hose) with the crates (or a wreck
    /// yard's salvage) still to carry: the shunter, spare, carries them meanwhile (the driver too had them out of the cab and
    /// further from it when the loading was called done: 40 s more of a frontier:7 Foundry's aboard wait). At
    /// frontier:1's wreck yard the pair cranked one sled in the loading a crew of bots gives it (note 609's 150 s), the shunter in
    /// the cab and the driver at the controls, and none of the salvage came out.
    /// </summary>
    public bool PairAtWork { get; private set; }
    public void PairWorking(bool on)
    {
        if (PairAtWork != on)
            Heard(-1, "pair at work", -1, on, () => PairAtWork = on);
    }

    /// <summary>
    /// What a spare hand (the shunter, the driver) takes on at the loading, with the train at the end of the spur (note 261):
    /// a missing part of the pair while there's that work left and the pair can be made up; else the crates, while there
    /// are any to carry and too few bots carrying them. None: back to the cab.
    /// </summary>
    public StopJob Lend(StopJob who, Func<StopJob, bool> pairWork, bool crateWork) =>
        SparesOnWinch && who is StopJob.Shunter or StopJob.Driver ? (who == StopJob.Shunter ? StopJob.Winch0 : StopJob.Winch1)
        : PairPart(who) is var pair && pair != StopJob.None && PairNow && pairWork(pair) ? pair
        : crateWork && Carries(who) ? StopJob.Crates : StopJob.None;

    /// <summary>
    /// Note 609: the driver's call that the spare hands (the shunter on the winch's first handle, the driver on its second)
    /// crank the winch while the pair work the crane (spec D.2: both "2 mandatory"; a crew of four is both pairs); the pair
    /// take it on once the crane's done. One after the other, the Foundry's crane and two sleds were 270 s of the loading, the
    /// shunter in the cab and the driver at the controls for all of it.
    /// </summary>
    public bool SparesOnWinch { get; private set; }
    public void SpareWinch(bool on)
    {
        if (SparesOnWinch != on)
            Heard(-1, "spares on the winch", -1, on, () => SparesOnWinch = on);
    }

    /// <summary>Hands for the crates: everyone with a part but the shunter (the winch pair carry where there's no winch), and a spare hand that carries.</summary>
    public int CrateHands => CrateBots + (Carries(StopJob.Shunter) ? 1 : 0) + (Carries(StopJob.Driver) ? 1 : 0);
    /// <summary>Crate hands that know their own player id, so can take a heavy crate (T45).</summary>
    public int HeavyHands => _crew.Count(c => c.Value.Alive && _knows.Contains(c.Key)
        && (c.Value.Job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates || c.Value.Job == StopJob.Shunter && Carries(StopJob.Shunter)));
    readonly HashSet<int> _knows = [];
    /// <summary>A member says it knows its own player id.</summary>
    public void Knows(int member) => Heard(member, "knows", member, true, () => _knows.Add(member));

    readonly Dictionary<int, int> _shutting = new();

    /// <summary>
    /// The driver's called the loading done ("that's it, all aboard"): done, given up or late for the dawn (T76). The crate
    /// hands put nothing more in, shut up behind them and come aboard; deadLines:2's two kept fetching crates through the
    /// whole aboard wait.
    /// </summary>
    public bool Leaving => _leaving || CalledAboard;
    bool _leaving;

    /// <summary>
    /// Note 740 (the director, 14 Oct: "When I pull the horn, the bots should understand that they need to get back on the
    /// train"; GDD §17): someone playing pulled the horn with the train standing. Everyone comes aboard, as at
    /// <see cref="Leaving"/>, and each calls "Aboard!" once it is (<see cref="HasCalledAboard"/>). It holds until the train
    /// moves off.
    /// </summary>
    public bool CalledAboard { get; private set; }
    readonly SortedSet<int> _calledBack = [];

    /// <summary>The horn heard (true), or the train moved off after it (false): a fresh call starts a fresh roll.</summary>
    public void CallAboard(bool on)
    {
        if (on != CalledAboard)
            Heard(-1, "call aboard", -1, on, () =>
            {
                CalledAboard = on;
                _calledBack.Clear();
            });
    }
    /// <summary>"Aboard!": this member's called back to the horn.</summary>
    public void CallBack(int member) => Heard(member, "aboard", member, true, () => _calledBack.Add(member));
    /// <summary>Whether this member's called back since the horn.</summary>
    public bool HasCalledAboard(int member) => _calledBack.Contains(member);

    /// <summary>
    /// Note 650: the driver's at a stop with loading to do and still on its moves before it (the approach, held for the
    /// points). A hand with a part in them doesn't leave it for a Holdout's breach (<see cref="StopHand.OnTheMoves"/>).
    /// </summary>
    public bool BeforeTheLoading { get; private set; }
    public void LoadingAhead(bool ahead)
    {
        if (ahead != BeforeTheLoading)
            Heard(-1, "loading ahead", -1, ahead, () => BeforeTheLoading = ahead);
    }

    // Note 326: the village's hiding spots claimed (by whom), and the hands out at the houses or bringing a find back.
    readonly Dictionary<int, int> _spots = new();
    readonly SortedSet<int> _out = [];

    /// <summary>"I'll take that cupboard": a hand going to search a spot, so the next goes elsewhere.</summary>
    public void ClaimSpot(int member, int spot) => Heard(member, "spot", member, spot, () => _spots[spot] = member);
    public void ReleaseSpot(int member, int spot) => Heard(member, "spot done", member, spot, () =>
    {
        if (_spots.TryGetValue(spot, out int who) && who == member)
            _spots.Remove(spot);
    });
    /// <summary>Whether someone else has said they'll search a spot.</summary>
    public bool SpotClaimed(int spot, int except) => _spots.TryGetValue(spot, out int who) && who != except;
    /// <summary>"I'm out at the houses" (or back): the driver doesn't call the loading done while anyone alive is.</summary>
    public void Out(int member, bool on)
    {
        if (_out.Contains(member) != on)
            Heard(member, "out", member, on, () =>
            {
                if (on)
                    _out.Add(member);
                else
                    _out.Remove(member);
            });
    }
    /// <summary>Hands out at the houses now, but this one.</summary>
    public int OutCount(int except) => _out.Count(m => m != except && Alive(m));
    /// <summary>Whether this hand has said it's out at the houses.</summary>
    public bool IsOut(int member) => _out.Contains(member);
    /// <summary>Anyone alive out at the houses.</summary>
    public bool Scavenging => _out.Any(Alive);
    bool Alive(int member) => !_crew.TryGetValue(member, out var c) || c.Alive;
    public void Leave(bool leaving) => Heard(-1, "leave", -1, leaving, () => _leaving = leaving);

    /// <summary>
    /// Note 609: the driver's call that the cars left waiting at a stop are lost, run away down the grade (note 261): the
    /// train goes on without them, and the points are set back behind it. Until the next stop's approach.
    /// </summary>
    public bool CutLost { get; private set; }
    public void LoseCut(bool lost = true)
    {
        if (CutLost != lost)
            Heard(-1, "cut lost", -1, lost, () => CutLost = lost);
    }
    /// <summary>
    /// The driver's away from the controls (T105: out on the running board sanding; T107: down on the ballast, or pulled off
    /// by something): the fireman minds them.
    /// </summary>
    public bool DriverAway { get; private set; }
    public void Away(bool away) => Heard(-1, "away", -1, away, () => DriverAway = away);
    /// <summary>The fireman's in the cab to mind the controls if the driver goes out (T107).</summary>
    public bool FiremanMinding { get; private set; }
    public void Mind(bool minding) => Heard(-1, "mind", -1, minding, () => FiremanMinding = minding);
    /// <summary>The fireman's out of the cab at the blow-off (T106): the driver doesn't leave the controls meanwhile.</summary>
    public bool Venting { get; private set; }
    public void Vent(bool venting) => Heard(-1, "vent", -1, venting, () => Venting = venting);

    /// <summary>
    /// Which door a hand shuts once the crates are in (T50): the one it has claimed while that's still open, else the
    /// nearest open one nobody else alive has claimed (two hands at one door undo each other). Null when there's none left
    /// for it. Claims go with the door: a hand that's gone aboard, or away to warm, holds none, so no door waits on it.
    /// </summary>
    public int? ClaimDoor(int member, IReadOnlyList<int> open, Func<int, double> distance)
    {
        foreach (var gone in _shutting.Where(c => !open.Contains(c.Value) || !(_crew.TryGetValue(c.Key, out var who) && who.Alive)).Select(c => c.Key).ToList())
            _shutting.Remove(gone);
        if (_shutting.TryGetValue(member, out var mine))
            return mine;
        var free = open.Where(car => !_shutting.ContainsValue(car)).OrderBy(distance).Select(car => (int?)car).FirstOrDefault();
        if (free is { } car)
            _shutting[member] = car;
        return free;
    }

    /// <summary>A hand lets go of the door it claimed (it's gone off to do something else).</summary>
    public void Unclaim(int member) => _shutting.Remove(member);

    /// <summary>
    /// A site this crew can load at: a winch with the pair for it, or crates with anyone to carry them; the grain elevator's
    /// spout with the shunter for its lever, the slaughterhouse's herd with two to drive it, the chemical works' hose with a
    /// hand to mind it (GDD §18; note 185).
    /// </summary>
    public bool CanWork(Site site) => CanShunt
        && (site.Has(ModuleKind.Winch) && site.SledsLeft > 0 && PairWithPeople
            || site.Has(ModuleKind.Crates) && site.CrateCount > 0 && CrateHands + PeopleHands > 0
            || site.Crane is { Left: > 0 } && PairWithPeople
            // The spout's lever is the shunter's, while the driver walks the cars under it.
            || site.Has(ModuleKind.Spout) && site.Bin > 0 && Has(StopJob.Shunter)
            // The steam lift's lever too (note 368), while the driver walks the cars under its chute and vents into it.
            || site.Has(ModuleKind.Lift) && site.Ore > 0 && Has(StopJob.Shunter)
            // And the conveyor line's drive house and belt (note 400), while the driver walks the cars under its head.
            || site.Has(ModuleKind.Conveyor) && site.Grain > 0 && Has(StopJob.Shunter)
            // And the tipple's lever (note 423), while the driver stands the cars true in its cradle.
            || site.Has(ModuleKind.Tipple) && site.TippleOre > 0 && Has(StopJob.Shunter)
            || site.Has(ModuleKind.Ramp) && site.Head > 0 && PairWithPeople
            // The hose wants one (D.2 fluid gantry "1–2"): its own hand, or a spare one (note 261).
            || site.Has(ModuleKind.Hose) && HoseHand
            // The wreck yard's salvage (note 187): carried out like crates.
            || site.Has(ModuleKind.Wreck) && site.Heaps.Count > 0 && CrateHands + PeopleHands > 0);

    /// <summary>Everyone alive with a part at the stop but <paramref name="except"/> is aboard one of these vehicles (the spout's lever hand stays down).</summary>
    public bool RidingBut(IReadOnlyCollection<int> vehicles, StopJob except) =>
        _crew.Where(c => c.Value.Alive && c.Value.Job is not (StopJob.None or StopJob.Driver) && c.Value.Job != except)
            .All(c => vehicles.Contains(c.Value.Vehicle));

    /// <summary>
    /// Everyone alive with a part at the stop is on one of these vehicles, bar anyone gone in out of the cold ("I'm getting
    /// warm in car nine"): they stay where they are, and the train comes back for the cut they're in (T64).
    /// </summary>
    public bool Riding(IReadOnlyCollection<int> vehicles) =>
        _crew.Where(c => c.Value.Alive && c.Value.Job is not (StopJob.None or StopJob.Driver) && !(_warming.Contains(c.Key) && c.Value.Vehicle != PlayerState.World))
            .All(c => vehicles.Contains(c.Value.Vehicle));

    readonly HashSet<int> _warming = [];
    /// <summary>A member says it's going in to get warm, or that it's back out.</summary>
    public void Warming(int member, bool on) => Heard(member, "warming", member, on, () =>
    {
        if (on)
            _warming.Add(member);
        else
            _warming.Remove(member);
    });

    /// <summary>Note 628: the shunter is off warming (in a car or the cab, too cold to work).</summary>
    public bool ShunterWarming => _crew.Any(c => c.Value.Alive && c.Value.Job == StopJob.Shunter && _warming.Contains(c.Key));

    /// <summary>
    /// Note 628: a hand that took the shunter's part from a shunter off warming, and the part it gave up for it, which the
    /// shunter takes once warm (a trade, as a crew would sort it out on the radio). Null with none.
    /// </summary>
    public (int Member, StopJob Gave)? ShuntTaken { get; private set; }

    /// <summary>Note 628: <paramref name="member"/> takes the shunter's part, giving up <paramref name="gave"/>.</summary>
    public void TakeShunt(int member, StopJob gave) => Heard(member, "shunt", member, gave, () => ShuntTaken = (member, gave));

    /// <summary>Note 628: the trade's done (the shunter has the part given up).</summary>
    public void Traded() => ShuntTaken = null;

    /// <summary>Note 628: someone alive with a lower member number than <paramref name="member"/> has the shunter's part too.</summary>
    public bool ShunterBefore(int member) => _crew.Any(c => c.Key < member && c.Value.Alive && c.Value.Job == StopJob.Shunter);

    /// <summary>Note 628: a part someone had and nobody alive has now (the winch's handles first), or a crate hand's.</summary>
    public StopJob FreePart() =>
        new[] { StopJob.Winch0, StopJob.Winch1 }.FirstOrDefault(p => _held.Contains(p) && !Has(p), StopJob.Crates);

    /// <summary>Nobody alive is on the ground.</summary>
    public bool AllAboard => _crew.Values.All(c => !c.Alive || c.Vehicle != PlayerState.World);

    // T96: who's going to breach which lit Holdout (GDD App. D.5), the nearest of those wanting to.
    readonly Dictionary<int, (int Member, double Distance)> _breach = new();

    /// <summary>A crewmate wanting to breach this Holdout from this far: true if it's theirs (the nearest takes it).</summary>
    public bool ClaimBreach(int holdout, int member, double distance)
    {
        if (!_breach.TryGetValue(holdout, out var c) || c.Member == member || distance < c.Distance - 3)
            _breach[holdout] = (member, distance);
        return _breach[holdout].Member == member;
    }

    /// <summary>This crewmate isn't going to any Holdout (any more).</summary>
    public void DropBreach(int member)
    {
        foreach (var h in _breach.Where(b => b.Value.Member == member).Select(b => b.Key).ToList())
            _breach.Remove(h);
    }

    // Note 496: who's going to the lamp the Fire Flies are on (App. A.5), the nearest hand in the first second they're there
    // (note 526), as for a Holdout; and theirs after that, while they're going. Taken from them as nearer hands came by on their own parts, the
    // lamp changed hands back and forth at car 1's ladder until the car caught.
    readonly Dictionary<int, (int Member, double Distance, uint Tick)> _lamp = new();

    /// <summary>A hand wanting to put out the lamp of the car the Fire Flies are on, from this far: true if it's theirs.</summary>
    public bool ClaimLamp(int car, int member, double distance, uint tick)
    {
        // Note 526: each hand's client sees the flies a tick or two apart, so for its first second the claim is the nearest's;
        // to the first to see them, the shunter at the switch took it from a walker in the cab beside car 1, and was 22 s getting there.
        if (!_lamp.TryGetValue(car, out var c) || !Alive(c.Member))
            _lamp[car] = (member, distance, tick);
        else if (c.Member != member && tick - c.Tick <= SimConstants.TickRate && distance < c.Distance - 1)
            _lamp[car] = (member, distance, c.Tick);
        return _lamp[car].Member == member;
    }

    // Note 511: which walker (by player id) is going to the loose coupling behind a car (note 356): the nearest the tick it
    // started working loose, and theirs while they say so (each tick on the way). One goes; the rest keep the gun fed.
    readonly Dictionary<int, (int Who, double Distance, uint Tick, uint Seen)> _pins = new();

    /// <summary>A walker going to the loose coupling behind <paramref name="car"/>, from this far: true if it's theirs.</summary>
    public bool ClaimPin(int car, int who, double distance, uint tick)
    {
        if (_pins.TryGetValue(car, out var c) && c.Who != who && tick - c.Seen <= SimConstants.TickRate && !(c.Tick == tick && distance < c.Distance))
            return false;
        _pins[car] = (who, distance, _pins.TryGetValue(car, out var mine) && mine.Who == who ? mine.Tick : tick, tick);
        return true;
    }

    // Note 627: which walker is going to a guttering lamp (note 346), as for a loose pin: the nearest the tick it's first
    // wanted, and theirs while they say so. Only those who'd take it want it: a gunner standing nearer, which never goes for a
    // lamp, held the choice from the walker at car 3's door on seed 4's night, and it went out.
    readonly Dictionary<int, (int Who, double Distance, uint Tick, uint Seen)> _gutters = new();

    /// <summary>A walker going to the guttering lamp of <paramref name="car"/>, from this far: true if it's theirs.</summary>
    public bool ClaimGutter(int car, int who, double distance, uint tick)
    {
        if (_gutters.TryGetValue(car, out var c) && c.Who != who && tick - c.Seen <= SimConstants.TickRate && !(c.Tick == tick && distance < c.Distance))
            return false;
        _gutters[car] = (who, distance, _gutters.TryGetValue(car, out var mine) && mine.Who == who ? mine.Tick : tick, tick);
        return true;
    }

    // Note 634 (the director, the inbox, 12 Oct: `D1-bots-two-extinguishers`, "a, two together"): who's on a car's fire,
    // two hands at most, in the order they said so, and theirs while they say so (each tick, as for a pin). A third leaves
    // it to them.
    readonly Dictionary<int, List<(int Who, uint Seen)>> _fireHands = new();

    /// <summary>
    /// A hand wanting to go to the fire in <paramref name="car"/>: 1 if it's the first hand on it, 2 the second, 0 with two
    /// on it already. Hands that stopped saying so (a second ago) are off it.
    /// </summary>
    public int ClaimFire(int car, int who, uint tick)
    {
        if (!_fireHands.TryGetValue(car, out var hands))
            _fireHands[car] = hands = [];
        hands.RemoveAll(h => h.Who != who && tick - h.Seen > SimConstants.TickRate);
        // Note 739: the bot sent to this fire by a ping is its first hand, ahead of whoever claimed it first (a gunner's legs
        // at their gun on that car claim it and sit on: the pinged walker came second and stayed out).
        if (OrderOf(who) is { Kind: Player.PingKind.Fire, Target: var pinged } && pinged == car)
        {
            hands.RemoveAll(h => h.Who == who);
            hands.Insert(0, (who, tick));
            if (hands.Count > 2)
                hands.RemoveRange(2, hands.Count - 2);
            return 1;
        }
        int at = hands.FindIndex(h => h.Who == who);
        if (at >= 0)
        {
            hands[at] = (who, tick);
            return at + 1;
        }
        if (hands.Count >= 2)
            return 0;
        hands.Add((who, tick));
        return hands.Count;
    }

    /// <summary>Note 634: how many hands are on the fire in <paramref name="car"/>, by what they said in the last second.</summary>
    public int FireHands(int car, uint tick) =>
        _fireHands.TryGetValue(car, out var hands) ? hands.Count(h => tick - h.Seen <= SimConstants.TickRate) : 0;

    /// <summary>The hand going to this car's swarmed lamp (or the fire the flies lit), or null.</summary>
    public int? LampHand(int car) => _lamp.TryGetValue(car, out var c) && Alive(c.Member) ? c.Member : null;

    /// <summary>This hand isn't going to any swarmed lamp (any more).</summary>
    public void DropLamp(int member)
    {
        foreach (var car in _lamp.Where(b => b.Value.Member == member).Select(b => b.Key).ToList())
            _lamp.Remove(car);
    }

    /// <summary>Someone's on their way to (or at) a Holdout's door: the driver waits for them (T96).</summary>
    public bool Breaching => _breach.Count > 0;

    // Note 258: who in the crew is a bot, and which of them could go and breach a Holdout (a walker or the gunner, alive).
    readonly SortedSet<int> _bots = [];
    readonly SortedSet<int> _breachers = [];

    /// <summary>A bot says which player it is: anyone else aboard is someone playing (note 259).</summary>
    public void Bot(int playerId) => _bots.Add(playerId);
    /// <summary>Whether this player is one of the bots (said so), not someone playing.</summary>
    public bool IsBot(int playerId) => _bots.Contains(playerId);

    // Note 377: the walkers free to bring a gun its powder right now (not the gunner, nor the driver).
    readonly SortedSet<int> _feeders = [];

    /// <summary>
    /// A walker says, each tick, whether it's free to bring the guns their powder (note 377, <see cref="PowderCarry"/>): not
    /// in for the cold, a tunnel or the Choir, nor at trouble in a car. Instant, like the claims: it's who goes where.
    /// </summary>
    public void CanFeed(int playerId, bool can)
    {
        if (can)
            _feeders.Add(playerId);
        else
            _feeders.Remove(playerId);
    }
    /// <summary>Whether this player said it's free to bring the guns their powder.</summary>
    public bool IsFeeder(int playerId) => _feeders.Contains(playerId);

    /// <summary>
    /// The walker gone forward to the cab (note 399, <see cref="ReliefDriver"/>): to take the controls from a dead driver, or to
    /// club a Climber in there with the driver. Claimed by the first to hear of it, and held while it lives and it's needed.
    /// Instant, like the claims: it's who goes where.
    /// </summary>
    public int? Relief { get; set; }
    /// <summary>
    /// The tick the walker gone forward last said so (note 655): a claim not said again for a second is nobody's. A gunner's
    /// legs claimed it for a Climber in the cab with the driver, then stood at the gun, and with the driver dead nobody else
    /// went (deepTerritory:7 seed 8, the train stood from 507 s till dawn).
    /// </summary>
    public uint ReliefSaid { get; set; }

    readonly int?[] _gunner = new int?[2];
    readonly bool[] _gunHeard = new bool[2];
    readonly int?[] _gunRelief = new int?[2];

    /// <summary>
    /// A gunner says, each tick, whether it's at its gun's post (note 456): alive and aboard, the guard gun's or, with
    /// <paramref name="forward"/>, the engine's. Instant, like the claims: it's who goes where.
    /// </summary>
    public void Gunning(int playerId, bool forward, bool on)
    {
        int g = forward ? 1 : 0;
        if (on)
            (_gunner[g], _gunHeard[g]) = (playerId, true);
        else if (_gunner[g] == playerId)
            _gunner[g] = null;
    }
    /// <summary>The gunner at that gun's post now, if any.</summary>
    public int? Gunner(bool forward) => _gunner[forward ? 1 : 0];
    /// <summary>That gun has had a gunner tonight (a crew too small for a forward gunner never has, and nobody goes to it).</summary>
    public bool GunHeard(bool forward) => _gunHeard[forward ? 1 : 0];
    /// <summary>The walker gone to take that gun from a gunner who's dead or left behind (note 456): one a gun.</summary>
    public int? GunRelief(bool forward) => _gunRelief[forward ? 1 : 0];
    public void SetGunRelief(bool forward, int? playerId) => _gunRelief[forward ? 1 : 0] = playerId;
    /// <summary>The walkers who said they're free to bring the powder (note 377): the ones free for other work too.</summary>
    public IReadOnlyCollection<int> Feeders => _feeders;

    /// <summary>
    /// A bot says whether it's one to breach a Holdout (a walker or the gunner, alive): the driver leaves the breach to them,
    /// and goes itself only with none of them left (note 259). Instant, like the claims: it's who goes where.
    /// </summary>
    public void CanBreach(int playerId, bool can)
    {
        if (can)
            _breachers.Add(playerId);
        else
            _breachers.Remove(playerId);
    }

    /// <summary>Some living walker or gunner could take a Holdout's breach (note 259).</summary>
    public bool AnyBreacher => _breachers.Count > 0;
}

/// <summary>
/// A facility stop as the crew see it coming (GDD §17): a spur with a winch down it, the point short of its points where
/// the engine stands, and how many cars go in behind it. Everyone works it out the same way from the route.
/// </summary>
/// <param name="Hold">Where the engine's front stands on the main line: two metres short of the points' reach.</param>
/// <param name="CutBehind">The vehicle whose rear coupling is cut to leave the rest waiting, or −1 if the train fits.</param>
public sealed record StopPlan(int Facility, Site Site, Branch Spur, double Hold, int Fit, int CutBehind)
{
    /// <summary>
    /// Note 533: another rake of this train at this stop (down its spur, or on the line near its hold): the cars left waiting,
    /// or the empties, or null. Not every rake the train has ever lost: frontier:7 seed 6's crew cut cars 8–10 loose at km 9,
    /// and for the rest of the night (counted by <see cref="TrainOnLine.TrainRakes"/>) the driver made no stop, the Foundry's
    /// plan was never over, and its hands rode to the cab at every stand till two were on the ballast at 1 hp.
    /// </summary>
    public TrainDynamics? NearRake(TrainOnLine train) =>
        train.Rakes.FirstOrDefault(r => r != train.Dynamics && !train.Standing(r) && r.Path == Spur.Index) ?? NearRake(train, Hold);

    /// <summary>Another rake of this train on the line within <see cref="RakeReach"/> of <paramref name="at"/>, or null.</summary>
    public static TrainDynamics? NearRake(TrainOnLine train, double at) =>
        train.Rakes.FirstOrDefault(r => r != train.Dynamics && !train.Standing(r) && r.Path == RailLine.MainPath && Math.Abs(r.Distance - at) <= RakeReach);

    /// <summary>A rake this near a stop's hold (m) is that stop's (the train's longest is about 330 m).</summary>
    const double RakeReach = 600;

    /// <summary>
    /// Only to couple up to a switchyard's cars standing on this siding and bring them out (GDD §18; note 187): nothing's
    /// loaded here. The engine goes in with whatever it's already picked up ahead of it and one car of its own behind.
    /// </summary>
    public bool PickUp { get; init; }

    /// <summary>What a crew member says it's done: the facility for its own stop, the siding for a pick-up.</summary>
    public int Key => PickUp ? PickUpKeys + Spur.Index : Facility;
    const int PickUpKeys = 1 << 16;

    /// <summary>The next stop the crew can work whose points are ahead of <paramref name="from"/>, if any.</summary>
    /// <param name="takes">Note 617: only a stop this says the crew would make (the driver's allowance for it): one turned down
    /// is passed over for the next, as a switchyard's own track for the pick-up on the siding beside it.</param>
    public static StopPlan? Ahead(World world, double from, IReadOnlySet<int> done, CrewCalls calls, Func<StopPlan, bool>? takes = null)
    {
        if (world.Run is not { } run)
            return null;
        var train = world.Train;
        double points = world.Switches?.Tuning.PointsLength ?? 12;
        var g = train.Dynamics.Tuning.Geometry;
        var vehicles = train.Dynamics.Consist.Vehicles;
        // The engine's place in its rake: a switchyard's cars picked up go on ahead of it (note 187).
        int engine = vehicles.ToList().FindIndex(v => v.IsEngine);
        StopPlan? best = null;
        foreach (var site in run.Sites)
        {
            if (site is not { Spur: >= 0 } || !calls.CanShunt)
                continue;
            foreach (int track in run.YardTracks(site.Index))
            {
                bool own = track == site.Spur;
                var spur = train.Line.Branches[track];
                int standing = Run.Run.StandingOn(train, track);
                // Its own track for its loading (and any cars standing there); any other only for the cars standing on it.
                if (own ? done.Contains(site.Index) || !calls.CanWork(site) && standing == 0 : standing == 0 || done.Contains(PickUpKeys + track))
                    continue;
                double hold = spur.Toe - points - 2;
                if (hold < from - 5 || best is not null && hold >= best.Hold)
                    continue;
                int fit = SpurDrill.Capacity(g, spur, points) - standing;
                if (own)
                {
                    // In front of the engine's own cars, what's ahead of it goes in too; and nothing to stop for if the cars that
                    // would go in have no room and there's nothing standing to fetch.
                    if (fit < engine || standing == 0 && !vehicles.Take(fit + 1).Any(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6))
                        continue;
                    // The driver shunting alone throws the switch from the ground but can't get between the cars to cut them
                    // (note 261): only a train that goes in whole.
                    if (!calls.Has(StopJob.Shunter) && vehicles.Count - 1 > fit)
                        continue;
                    var plan = new StopPlan(site.Index, site, spur, hold, fit, vehicles.Count - 1 > fit ? vehicles[fit].Id : -1);
                    if (takes is null || takes(plan))
                        best = plan;
                }
                else
                {
                    // In with what's ahead of the engine and a car of its own behind it (a coupling anyone can cut), the rest
                    // left waiting on the main line.
                    if (fit < engine + 1 || !calls.Has(StopJob.Shunter))
                        continue;
                    var pickUp = new StopPlan(site.Index, site, spur, hold, engine + 1, vehicles.Count - 1 > engine + 1 ? vehicles[engine + 1].Id : -1) { PickUp = true };
                    if (takes is null || takes(pickUp))
                        best = pickUp;
                }
            }
        }
        return best;
    }

    /// <summary>The engine's rake is standing where it stops for this, on the main line.</summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && train.Dynamics.Distance - Hold is > -3 and <= StopDriver.HoldOver;

    /// <summary>The engine's rake is at the buffer stop down the spur, standing (where the loading's done).</summary>
    public bool AtTheEnd(TrainOnLine train) =>
        train.Dynamics.Path == Spur.Index && train.Dynamics.Distance > Spur.End - 6 && Math.Abs(train.Dynamics.Velocity) < 0.05;

    /// <summary>Cargo cars in the engine's rake with their sliding door on the site's side open, in order along the train.</summary>
    public IEnumerable<int> OpenSideDoors(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo
            && StopHand.SideDoor(train.Frames[v.Id].Shape, Site.Side) is { } door && v.DoorOpen(door)).Select(v => v.Id);

    /// <summary>
    /// The grain elevator's next car (GDD §18 "one spout, one car at a time"): the car with room in the engine's rake whose
    /// middle comes under the spout with the engine furthest up the spur (short of its buffer stop), and where the engine's
    /// front stands for that. The train's walked back under the spout a car at a time, the way it backs out anyway.
    /// Null when there's no grain, no car left with room that it reaches, or the engine's not down the spur.
    /// </summary>
    public (int Car, double Front)? SpoutTarget(World world)
    {
        var rake = world.Train.Dynamics;
        if (!Site.Has(ModuleKind.Spout) || Site.Bin <= 1e-6 || rake.Path != Spur.Index)
            return null;
        double spout = Spur.Toe + Site.SpoutAlong;
        (int Car, double Front)? best = null;
        var vehicles = rake.Consist.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v.Kind != VehicleKind.Cargo || v.Load >= 1 - 1e-6)
                continue;
            double front = spout + rake.Consist.OffsetOf(i) + v.Length(rake.Consist.Tuning) / 2;
            if (front <= Spur.End - 1 && (best is null || front > best.Value.Front))
                best = (v.Id, front);
        }
        return best;
    }

    /// <summary>
    /// The steam lift's next car (note 368): as the spout's, the car with room that comes under the chute with the engine
    /// furthest up the spur, but only those that do with the engine's middle within its steam line's reach
    /// (facilities.json <c>lift.steamReach</c>, less a metre); and where the engine's front stands for that.
    /// </summary>
    public (int Car, double Front)? LiftTarget(World world)
    {
        var rake = world.Train.Dynamics;
        if (!Site.Has(ModuleKind.Lift) || Site.Ore <= 1e-6 || rake.Path != Spur.Index || world.Run?.FacilityTuning is not { } t)
            return null;
        double chute = Spur.Toe + Site.LiftAlong;
        var consist = rake.Consist;
        var vehicles = consist.Vehicles;
        int engine = vehicles.ToList().FindIndex(v => v.Kind == VehicleKind.Engine);
        if (engine < 0)
            return null;
        (int Car, double Front)? best = null;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v.Kind != VehicleKind.Cargo || v.Load >= 1 - 1e-6)
                continue;
            double front = chute + consist.OffsetOf(i) + v.Length(consist.Tuning) / 2;
            double engineMiddle = front - consist.OffsetOf(engine) - vehicles[engine].Length(consist.Tuning) / 2;
            if (front <= Spur.End - 1 && engineMiddle - chute <= t.Lift.SteamReach - 1 && (best is null || front > best.Value.Front))
                best = (v.Id, front);
        }
        return best;
    }

    /// <summary>
    /// The conveyor line's next car (note 400): as the spout's, the car with room that comes under its head with the engine
    /// furthest up the spur, and where the engine's front stands for that. Null when there's no grain for it, no car left with
    /// room that it reaches, or the engine's not down the spur.
    /// </summary>
    public (int Car, double Front)? ConveyorTarget(World world)
    {
        var rake = world.Train.Dynamics;
        if (!Site.Has(ModuleKind.Conveyor) || Site.Grain <= 1e-6 || rake.Path != Spur.Index)
            return null;
        double head = Spur.Toe + Site.ConveyorAlong;
        (int Car, double Front)? best = null;
        var vehicles = rake.Consist.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v.Kind != VehicleKind.Cargo || v.Load >= 1 - 1e-6)
                continue;
            double front = head + rake.Consist.OffsetOf(i) + v.Length(rake.Consist.Tuning) / 2;
            if (front <= Spur.End - 1 && (best is null || front > best.Value.Front))
                best = (v.Id, front);
        }
        return best;
    }

    /// <summary>
    /// The tipple's next car (note 423): as the conveyor's, the car with room that comes into its cradle with the engine furthest
    /// up the spur, and where the engine's front stands for its middle to be on the cradle's. Null when the bin's empty, no car
    /// left with room reaches it, or the engine's not down the spur.
    /// </summary>
    public (int Car, double Front)? TippleTarget(World world)
    {
        var rake = world.Train.Dynamics;
        if (!Site.Has(ModuleKind.Tipple) || Site.TippleOre <= 1e-6 || rake.Path != Spur.Index)
            return null;
        double cradle = Spur.Toe + Site.TippleAlong;
        (int Car, double Front)? best = null;
        var vehicles = rake.Consist.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v.Kind != VehicleKind.Cargo || v.Load >= 1 - 1e-6 || v.OffRails)
                continue;
            double front = cradle + rake.Consist.OffsetOf(i) + v.Length(rake.Consist.Tuning) / 2;
            if (front <= Spur.End - 1 && (best is null || front > best.Value.Front))
                best = (v.Id, front);
        }
        return best;
    }

    /// <summary>The herd still to go up the ramp, with a car at its top to take them, while the engine's at the end.</summary>
    public bool HerdLeft(World world) => Site.Has(ModuleKind.Ramp) && Site.Head > 0 && AtTheEnd(world.Train)
        && world.Run?.CarAtRamp(world.Train, Site) is not null;

    /// <summary>The hose still to work: on a car (to mind, and take off once it's full), or a car with room by the stand to put it on.</summary>
    public bool HoseLeft(World world) => Site.Has(ModuleKind.Hose) && AtTheEnd(world.Train)
        && (Site.HoseCar >= 0 || world.Run?.CarAtHose(world.Train, Site) is not null);

    /// <summary>
    /// Work left for a part of the pair (note 261), with the engine's rake at the end of the spur: the crane's castings, the
    /// herd, the hose (the first of the pair's), or the winch's sleds with somewhere to go.
    /// </summary>
    public bool PairWork(World world, StopJob part)
    {
        var train = world.Train;
        return Site.Crane is { } crane && (StopHand.CraneTarget(crane, train) is not null || crane.Hooked is not null)
            || HerdLeft(world) || part == StopJob.Winch0 && HoseLeft(world)
            || Site.Has(ModuleKind.Winch) && Site.SledsLeft > 0 && world.Run is { } r && r.SledHasRoom(train, Site);
    }

    /// <summary>Crates (or salvage) still to carry, or a door they went in by still to shut (note 261).</summary>
    public bool CrateWork(World world, int heavyHands) =>
        (Site.Has(ModuleKind.Crates) || Site.Has(ModuleKind.Wreck)) && (CratesToLoad(world, heavyHands) || OpenSideDoors(world.Train).Any());

    /// <summary>
    /// How far the loading's got (note 261), for telling whether anyone's at it: what's in the cars, the winch's sled along
    /// its haul and the castings set down.
    /// </summary>
    public double Progress(World world) =>
        world.Train.Dynamics.Consist.Vehicles.Sum(v => v.Load) + Site.Progress + Site.SledsLeft * -1.0
        + (Site.Crane?.Castings.Count(c => c.State == CastingState.Loaded) ?? 0);

    /// <summary>Cargo cars in the engine's rake with room for more.</summary>
    public static IEnumerable<Vehicle> WithRoom(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6);

    /// <summary>
    /// The stop's packing plan (queue #630, note 920; found by Rhea under #623, note 903): the whole halves the site's heavy
    /// crates still to load will need, set aside in the cars before the light crates take their room. Each light went to the
    /// roomiest car, so how a stop packed hung on the order the crates came in: with a half's room in each car, an odd light
    /// count went one to a car and left the heavies no whole half (a third light took the last one and left two heavies in
    /// the yard, and a train that arrived a second later loaded them).
    /// <para>
    /// Worked out from the stop as it stands, the same way by every hand: a car's room is what isn't loaded or lying in it
    /// settling (by each crate's size); the heavy crates at the site still to load (held, up, or lying loose there with two
    /// hands for them, T45) each take a whole half, in the cars nearest the crate stack. Only what fits is set aside: a heavy
    /// with no whole half anywhere takes nothing from the lights. The crane's castings go down through the roof, where it
    /// reaches, and the kegs' rules are the hands' own (set down, never thrown), whichever car they go to.
    /// </para>
    /// </summary>
    /// <returns>Each cargo car with room, by id: the room the light crates may take (never below zero).</returns>
    public Dictionary<int, double> LightRoom(World world, CrewCalls calls)
    {
        var (free, heavy) = Pack(world, calls);
        return free.ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - heavy.GetValueOrDefault(kv.Key)));
    }

    /// <summary>
    /// Note 920: whether <paramref name="car"/> has a whole half free for a heavy crate (what's loaded and settling in it taken
    /// off), and the nearest car to <paramref name="from"/> that has, or −1.
    /// </summary>
    public int HeavyCar(World world, int car, Double3 from)
    {
        var train = world.Train;
        var (free, _) = Pack(world, null);
        double half = Site.LoadPerHeavy - 1e-6;
        if (free.TryGetValue(car, out double room) && room >= half)
            return car;
        return free.Where(kv => kv.Value >= half).Select(kv => kv.Key)
            .OrderBy(c => (train.Frames[c].Origin - from).Length).ThenBy(c => c).DefaultIfEmpty(-1).First();
    }

    /// <summary>Note 920's plan: each car's free room, and the room set aside in it for the site's heavy crates (none without <paramref name="calls"/>).</summary>
    (Dictionary<int, double> Free, Dictionary<int, double> Heavy) Pack(World world, CrewCalls? calls)
    {
        var train = world.Train;
        var free = new SortedDictionary<int, double>();
        foreach (var v in WithRoom(train))
            free[v.Id] = 1 - v.Load;
        // Put down inside a car and settling: its room's taken, by the crate's size. (Loaded, note 659, it's in the load.)
        foreach (var b in world.Bodies.All)
            if (b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy && b.Carrier < 0 && !b.Banked && free.ContainsKey(b.Parent) && Inside(train, b))
                free[b.Parent] -= b.Kind == Physics.BodyKind.Heavy ? Site.LoadPerHeavy : Site.LoadPerCrate;
        var heavy = new Dictionary<int, double>();
        if (calls is null)
            return (new Dictionary<int, double>(free), heavy);
        // The crane's castings first, while the pair can work it: a whole casting's room in the cars its hook reaches, nearest
        // the hook first (the lights kept out of the room the castings need, they took the cars under the crane and it had
        // nowhere to set one down). (Set aside in the same tally as the heavies' halves.)
        if (Site.Crane is { Wrecked: false } crane && calls.PairWithPeople)
        {
            double size = crane.Tuning.LoadPerCasting;
            int castings = crane.Left;
            var hook = crane.HookAt;
            foreach (int car in free.Keys.Where(c => StopHand.Reaches(crane, train, c)).OrderBy(c => (train.Frames[c].Origin - hook).Length).ThenBy(c => c))
                while (castings > 0 && free[car] - heavy.GetValueOrDefault(car) >= size - 1e-6)
                {
                    heavy[car] = heavy.GetValueOrDefault(car) + size;
                    castings--;
                }
        }
        double half = Site.LoadPerHeavy;
        int heavies = world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Heavy && !b.Banked && !(free.ContainsKey(b.Parent) && Inside(train, b))
            && (b.Lifted || b.Carrier >= 0 || calls.HeavyHands >= 2 && Loose(world, b)));
        var stack = Site.CrateStack.Length > 0 ? Site.CrateStack[0] : Site.HeavyStack.Length > 0 ? Site.HeavyStack[0] : train.Frames[0].Origin;
        foreach (int car in free.Keys.OrderBy(c => (train.Frames[c].Origin - stack).Length).ThenBy(c => c))
            while (heavies > 0 && free[car] - heavy.GetValueOrDefault(car) >= half - 1e-6)
            {
                heavy[car] = heavy.GetValueOrDefault(car) + half;
                heavies--;
            }
        return (new Dictionary<int, double>(free), heavy);
    }

    /// <summary>
    /// Crates still to go in: in someone's arms, put down inside a car with room and not yet stowed, or lying where a
    /// hand can pick them up (<see cref="Loose"/>); and a car with room for them. Once they're all in (or nothing has
    /// room), the crates are done.
    /// </summary>
    /// <param name="hands">Crate hands in the crew: two can take a heavy crate between them, one can lend a hand to someone holding one (T45).</param>
    public bool CratesToLoad(World world, int hands = 0)
    {
        // The wreck yard's salvage is carried out as crates are (note 187): what the lamps have found of it.
        if (!Site.Has(ModuleKind.Crates) && !Site.Has(ModuleKind.Wreck))
            return false;
        if (Site.Has(ModuleKind.Crates) && !Site.Stocked)
            return Site.CrateCount > 0;
        var train = world.Train;
        var room = WithRoom(train).Select(v => v.Id).ToHashSet();
        if (room.Count == 0)
            return false;
        // A heavy crate while two have it up or it's down inside a car; held by one, while there's a hand to lend; lying
        // loose, while there are two to take it (T45). Otherwise it doesn't keep the train.
        // Loaded (note 659), one lies in its car for good: no work in that.
        return world.Bodies.All.Any(b => b.Kind == Physics.BodyKind.Cargo
            && (b.Carrier >= 0 || room.Contains(b.Parent) && Inside(train, b) && !b.Banked || Loose(world, b))
            || b.Kind == Physics.BodyKind.Heavy && (b.Lifted || room.Contains(b.Parent) && Inside(train, b) && !b.Banked
                || b.Carrier >= 0 && hands >= 1 || hands >= 2 && Loose(world, b)));
    }

    /// <summary>
    /// A crate a hand can pick up from the ground: lying at the site, or left on a car's steps or landing (put down to open
    /// a door), which is in reach from beside them.
    /// </summary>
    public bool Loose(World world, Physics.Body b)
    {
        if (b.Kind is not (Physics.BodyKind.Cargo or Physics.BodyKind.Heavy) || b.Carrier >= 0)
            return false;
        if (b.Parent == PlayerState.World)
        {
            // At the site, on its side of the train (there's no carrying one round the train): by the crate stack, or by a wreck
            // heap it came out of (note 187), and not by one that's groaning.
            bool atStack = Site.CrateStack.Length > 0 && (b.Centre - Site.CrateStack[0]).Length < 60;
            bool atHeap = Site.Heaps.Any(h => (b.Centre - h.Centre).Length < 60);
            if (!atStack && !atHeap || world.Run?.Groaning(b.Centre, 0.5) is not null)
                return false;
            double hint = Site.Mid;
            var s = Site.Track.Sample(Site.Track.Nearest(b.Centre, ref hint).Distance);
            return Math.Sign(Double3.Dot(b.Centre - s.Position, Double3.Cross(s.Tangent, Double3.Up))) == Site.Side;
        }
        return world.Train.Dynamics.Consist.IndexOf(b.Parent) >= 0 && !Inside(world.Train, b) && b.Pbd.Asleep;
    }

    /// <summary>A crate of freight's size (<see cref="Physics.Bodies.SpawnCargo"/>'s, m): what a hand that doesn't know what it holds stows for.</summary>
    public const double CrateRadius = 0.45;

    /// <summary>
    /// Where a crate goes down in a car so the next one gets by (note 659: what's put in a car stays there, solid, so a crew
    /// stacks it): a light one against a wall, clear of the doorways and the aisle down the middle, the wall across from the
    /// door first; a heavy one (two carrying it, along the car) down the middle, clear of the
    /// doorways. The first spot nothing lies on and no fitting stands in, as the crate's middle on the floor; null if the
    /// car's full up.
    /// </summary>
    public static Double3? Stow(TrainOnLine train, int car, int side, double radius, bool heavy)
    {
        var shape = train.Frames[car].Shape;
        if (shape.Interior is not { } room || train.Dynamics.Tuning.Geometry.Interior is not { } layout)
            return null;
        var vehicle = train.Vehicles[car];
        double floor = layout.FloorHeight, sd = layout.SideDoorWidth / 2, gap = 0.08;
        double ends = room.Max.Z - radius - gap, door = sd + radius + 0.3;
        var spots = new List<Double3>();
        if (heavy)
            for (double z = door + 0.3; z <= ends; z += 2 * radius + gap)
            {
                spots.Add(new Double3(0, floor, z));
                spots.Add(new Double3(0, floor, -z));
            }
        else
        {
            // Across from the door, out from beside the doorway (the shortest carry, and never in the way in); then the door's
            // own wall from the ends back (what's put by the doorway there is what the next one in walks round).
            double far = -side * (room.Max.X - radius - gap), near = side * (room.Max.X - radius - gap);
            for (double z = door; z <= ends; z += 2 * radius + gap)
            {
                spots.Add(new Double3(far, floor, -z));
                spots.Add(new Double3(far, floor, z));
            }
            for (double z = ends; z >= door; z -= 2 * radius + gap)
            {
                spots.Add(new Double3(near, floor, -z));
                spots.Add(new Double3(near, floor, z));
            }
        }
        foreach (var at in spots)
        {
            bool Over(Box b) => b.Max.Y > floor + 0.1 && b.Min.Y < floor + 2 * radius && Math.Abs(b.Centre.X - at.X) < radius + b.HalfSize.X
                && Math.Abs(b.Centre.Z - at.Z) < radius + b.HalfSize.Z;
            if (train.Lying.Any(l => l.Car == car && Over(l.Box)) || shape.Solids.Any(s => s.Present(vehicle) && Over(s.BoxIn(vehicle))))
                continue;
            return at;
        }
        return null;
    }

    internal static bool Inside(TrainOnLine train, Physics.Body b) =>
        b.Parent > 0 && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Pbd.Particles[0].Position);
}

/// <summary>One facility stop as the driver worked it, for the harness report.</summary>
/// <param name="Legs">Seconds spent on each leg, by name.</param>
/// <param name="Coal">Coal taken into the tender (a coaling stop).</param>
/// <param name="Castings">Castings the crane put on the cars at this stop (T54).</param>
public sealed record StopRecord(int Facility, string Kind, double Seconds, int SledsHauled, IReadOnlyDictionary<string, double> Legs, double Coal = 0, int Castings = 0);

/// <summary>
/// A dead line's switch set against the train (someone's hand, or a scripted set piece; the Switchman's work once, note 852):
/// its lamp reads wrong from the cab. The
/// crew stops short of its points and someone sets it back on the ground; if the train's already down the dead line, it
/// backs out first. A facility's spur set for it outside a stop is the same (note 550: frontier:7's seed 8, a shunter
/// threw the Foundry's points after the driver had given the stop up, and the train ran down the spur to its buffer and
/// stood there all night).
/// </summary>
/// <param name="Hold">Where the engine's front stands to wait: two metres short of the points' reach.</param>
public sealed record SwitchPlan(Branch Branch, double Hold)
{
    /// <summary>How far ahead a switch stand's lamp reads from the cab.</summary>
    const double LampSeen = 1200;

    /// <summary>
    /// A branch the train only goes down to work a stop, or never: a dead line or a facility's spur (an alternate route
    /// is the line going on).
    /// </summary>
    static bool Off(Branch b) => b.Kind is BranchKind.DeadLine or BranchKind.Spur;

    /// <summary>The nearest dead line or spur ahead whose switch is set for it, in sight of the cab.</summary>
    public static SwitchPlan? Ahead(World world)
    {
        var train = world.Train;
        double front = train.Dynamics.Distance, points = world.Switches?.Tuning.PointsLength ?? 12;
        return train.Line.Branches.Where(b => Off(b) && train.Diverging(b.Index)
                && b.Toe - points - 2 >= front - 3 && b.Toe - front <= LampSeen)
            .OrderBy(b => b.Toe).Select(b => new SwitchPlan(b, b.Toe - points - 2)).FirstOrDefault();
    }

    /// <summary>The engine's down a dead line or a spur past its points: it took a switch set wrong.</summary>
    public static SwitchPlan? DownOne(World world)
    {
        var train = world.Train;
        int path = train.Dynamics.Path;
        if (path < 0 || path >= train.Line.Branches.Count || train.Line.Branches[path] is not { } b || !Off(b)
            || train.Dynamics.Distance <= b.Toe)
            return null;
        return new SwitchPlan(b, b.Toe - (world.Switches?.Tuning.PointsLength ?? 12) - 2);
    }

    /// <summary>
    /// The whole train's standing short of the points on the main line: at the hold, or further back (backed off a dead
    /// line, the driver stops wherever the train's clear of them, which can be well short of the hold).
    /// </summary>
    /// <summary>
    /// The whole train standing on the main line short of the points: every rake of it. A car cut loose and left buffered
    /// up behind is still the train; frontier:7 stood at a dead line's switch till dawn with one, nobody setting it back.
    /// </summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && train.Dynamics.Distance - Hold <= StopDriver.HoldOver
        && train.Rakes.All(r => train.Standing(r) || train.Line.OnMain(r.Path, r.Distance) && Math.Abs(r.Velocity) < 0.05 && r.Distance - Hold <= StopDriver.HoldOver);
}

/// <summary>
/// A coaling stop (GDD §18, spec D.2 gravity chute): the tower stands over the main line, and the engine stops with its
/// tender under the spout while someone on the ground works the chute's lever.
/// </summary>
/// <param name="Hold">Where the engine's front stands: the tender's middle under the spout.</param>
public sealed record CoalPlan(int Facility, double Spout, double Hold, Double3 Lever)
{
    /// <summary>Worth stopping for: this much of the tender to fill.</summary>
    const double WorthFilling = 0.25;

    /// <summary>The next coaling tower ahead with coal left, if the tender has room enough and someone can work the lever.</summary>
    public static CoalPlan? Ahead(World world, double from, IReadOnlySet<int> done, CrewCalls calls)
    {
        var train = world.Train;
        if (world.Run is not { } run || train.BoilerTuning is not { } bt || !calls.Has(StopJob.Shunter)
            || bt.TenderCapacity - train.Boiler.Tender < bt.TenderCapacity * WorthFilling)
            return null;
        var g = train.Dynamics.Tuning.Geometry;
        double tender = EnginePlan.Of(g).CoalFromFront;
        var facilities = run.Route.Of(FeatureKind.Facility).ToList();
        for (int i = 0; i < facilities.Count; i++)
        {
            if (facilities[i].Facility != FacilityKind.CoalingTower || done.Contains(i) || run.ChuteLeft(i) <= 0)
                continue;
            var (spout, lever) = run.ChuteAt(facilities[i], train.Line);
            if (spout + tender >= from - 5)
                return new CoalPlan(i, spout, spout + tender, lever);
        }
        return null;
    }

    /// <summary>The engine's standing with its tender under the spout (well inside run.json's spout tolerance).</summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && StopPlan.NearRake(train, Hold) is null && Math.Abs(train.Dynamics.Velocity) < 0.05 && Math.Abs(train.Dynamics.Distance - Hold) < 1.5;
}

/// <summary>
/// The driver's side of a facility stop, through the cab's controls and nothing else (the scripted
/// <see cref="SpurDrill"/>, done by a crew): stop short of the spur's points; once the rest are cut off, the switch is
/// over and the ones riding in are aboard, run the empties in to the buffer stop; wait for the winch; back out onto the
/// cars left waiting, at a crawl so they couple; stop clear of the points; once the switch is back and everyone's
/// aboard, go. It only stops where the crew can work the stop (<see cref="CrewCalls.CanWork"/>; note 261: the crew that's there).
/// </summary>
public sealed class StopDriver(CrewCalls calls)
{
    public enum Leg : byte { Cruise, Approach, Held, SpurIn, Loading, BackOut, Clear, Depart, ToCoal, Coaling, ToSwitch, OffDeadLine, SetBack, Forward, Spouting, Lifting, Conveying, Tippling }

    // Long enough for a crew to do their part at walking pace; past it, the stop is given up rather than the night. (The
    // loading's was 300; cab forward, note 276, the warm a cold hand goes back to is 10 m further from the cars, and a lone
    // bot's crates ran up to it with a door still to shut.)
    const double HeldGiveUp = 240, LoadingGiveUp = 360, AboardGiveUp = 120, CoalGiveUp = 150, SpoutGiveUp = 300;
    /// <summary>
    /// Note 609: seconds a crew of bots gives a stop's loading before it calls it done, on a night without the spare for a whole
    /// loading (<see cref="LoadingGiveUp"/>) and another stop's <see cref="Allowance"/> after it (the spout, the conveyor,
    /// the lift and the tipple after it have their own give-ups). What a stop's
    /// worth is in its first couple of minutes (the crane's castings, a sled, the nearest crates), and cargo aboard draws the
    /// night's pressure (the director's cargo term): loaded until it was late for the dawn, sixteen frontier:7 nights with a
    /// crew of four drew 30% more creatures on the way home and came in 4 s before the dawn on the mean, four of them taken by
    /// the Wakers and 21 crew dead; given 150 s, they came in 221 s before it, none taken, 5 dead, with half again the
    /// cargo of main's.
    /// </summary>
    const double LoadingFor = 150;
    /// <summary>The steam lift's give-up (note 368): it goes at the fire's pace once the boiler's emptied, slower than the spout.</summary>
    const double LiftGiveUp = 480;
    // The conveyor's three car-loads at 25 s a car, a jam every 30-60 s of it, and the walk to each and back to the drive house.
    const double ConveyGiveUp = 420;
    // The tipple's three car-loads at two rolls a car (11 s each, clamp to let go) and the engine stood true for each car.
    const double TippleGiveUp = 300;
    /// <summary>
    /// How near the engine's front stands to where it puts a car's middle on the tipple's cradle (m): inside facilities.json
    /// tipple.goodClamp, so the shunter's clamp is a good one.
    /// </summary>
    const double TippleTrue = 0.45;
    /// <summary>
    /// Seconds of spare before the dawn a coaling stop wants; and note 609's one allowance for a facility stop (500), sized on
    /// frontier:7's Foundry, which a loading still weighs itself against with no stop ahead (<see cref="LoadingFor"/>). Note
    /// 617: each kind of stop its own, <see cref="Allowance"/>.
    /// </summary>
    const double StopAllowance = 500, CoalAllowance = 120;

    /// <summary>
    /// Note 617: seconds a stop's legs take up to its loading (the approach, held for the cut and the points, in down the
    /// spur), by what it is: the means of frontier:7, :1, :3, :4 and :6's harness nights with a crew of four (and the drills'
    /// for the mine head's), the spur's length and the cut's walk the most of it.
    /// </summary>
    static double ToTheLoading(StopPlan p) =>
        p.Site.Has(ModuleKind.Tipple) ? 100
        : p.Site.Has(ModuleKind.Conveyor) || p.Site.Has(ModuleKind.Spout) ? 140
        : p.Site.Feature.Facility == FacilityKind.Switchyard ? 145
        : p.Site.Crane is not null ? 125
        : p.Site.Feature.Facility == FacilityKind.WreckYard ? 95
        : 110;

    /// <summary>
    /// Note 617: seconds of spare before the dawn (at the pace the night's kept, <see cref="Spare"/>) a stop wants, by what it
    /// is. A stop with a loading: its legs up to the loading (<see cref="ToTheLoading"/>), the loading's least
    /// (<see cref="OnwardLoading"/>), and what <see cref="Late"/> keeps back for the leaving and the aboard wait
    /// (<see cref="LateSpare"/> and <see cref="AboardGiveUp"/>): 485 s for the wreck yard to 535 for a switchyard's own track
    /// and the grain elevator (note 609's one 500 was the Foundry's). A switchyard's pick-up (note 187) has no loading for
    /// <see cref="Late"/> to cut short: the stop itself (<see cref="PickUpStop"/>) and <see cref="PickUpNoise"/> over it (note 621).
    /// </summary>
    public static double Allowance(StopPlan p, World world) =>
        p.PickUp ? PickUpStop + PickUpNoise : ToTheLoading(p) + OnwardLoading + LateSpare + AboardGiveUp;

    /// <summary>
    /// Note 621: how long after the dawn a train still in is in time (s): with the Wakers up, when they'd catch a stood train
    /// (<see cref="Enemies.Waker.CatchAfterDawn"/>; a train still running is caught later); without them, <c>run.json
    /// dawnGraceSeconds</c>. What the night really allows past the dawn, and every spare is reckoned to it (<see cref="Deadline"/>).
    /// </summary>
    public static double Grace(World world) =>
        world.Enemies is { Wakers: { Enabled: true } t } ? Enemies.Waker.CatchAfterDawn(t, 0)
        : world.Run?.Tuning.DawnGraceSeconds ?? 0;

    /// <summary>Note 621: seconds to the night's real deadline, the dawn and its <see cref="Grace"/>: what every spare is reckoned to.</summary>
    static double Deadline(World world, Run.Run run) => run.DawnIn + Grace(world);

    /// <summary>
    /// Note 617: a switchyard pick-up's stop (s): in with the engine and a car, couple up, out onto the rest. 136 s in the drill
    /// (no creatures); on frontier:7's harness nights at Renwick Yard 148–518, 184 the median of seven.
    /// </summary>
    const double PickUpStop = 185;
    /// <summary>
    /// Note 621: what a pick-up wants over its own seconds (s), against the night's real deadline (<see cref="Deadline"/>): the
    /// run home after a stop is out by about 120 s on any one night however it's reckoned (frontier:7's sixteen, at Renwick
    /// Yard), and this was 1.3 of that, 160: with the pick-up itself out by up to 340 s more (148–528 s), 120 took three trains
    /// in nine. Note 628: with the pick-up's own stalls taken out (178–212 s on the mean, few over 250), 100: on sixteen nights
    /// each, frontier:6 made eight pick-ups against none (cargo 16.1 → 20.6, a dawn missed 1 → 0) and frontier:7 two.
    /// </summary>
    const double PickUpNoise = 100;
    /// <summary>Note 609: a stop's loading isn't cut short for the next stop (<see cref="Onward"/>) unless this much of it's to be had first.</summary>
    const double OnwardLoading = 90;
    /// <summary>Note 609: past the next stop's allowance, a stop's loading is called done this much early (the leaving takes less than reckoned).</summary>
    const double OnwardMargin = 30;
    /// <summary>Note 609: the next stop is one whose hold is at least this far past this one's (m).</summary>
    const double OnwardBeyond = 50;
    /// <summary>The spare a stop's loading had when it began (<see cref="Onward"/>), NaN before then; and whether there was a stop ahead.</summary>
    double _spareAtLoading = double.NaN;
    /// <summary>Note 617: the least allowance of the stops ahead this crew can work (<see cref="NextAllowance"/>), null with none.</summary>
    double? _onwardAllowance;

    /// <summary>
    /// Note 617: the least <see cref="Allowance"/> of the stops ahead of this one the crew can work, or null with none: the stop
    /// it would make next if any (a switchyard's own track turned down, its pick-up).
    /// </summary>
    double? NextAllowance(World world, StopPlan p)
    {
        double? least = null;
        var done = new HashSet<int>(_done) { p.Key };
        double from = p.Hold + OnwardBeyond;
        for (int i = 0; i < 16 && StopPlan.Ahead(world, from, done, calls) is { } next; i++)
        {
            // Note 621: not for a pick-up. Called done for Renwick Yard's after the Foundry, the Foundry's loading came to 1.0
            // car-loads instead of 1.5 on most of frontier:7's nights, for a pick-up of little or nothing.
            if (!next.PickUp)
                least = Math.Min(least ?? double.MaxValue, Allowance(next, world));
            done.Add(next.Key);
        }
        return least;
    }
    /// <summary>
    /// Note 609: the spare this stop's loading began with, past its leaving (<see cref="Onward"/>'s reckoning, for tests and
    /// traces); NaN before the loading.
    /// </summary>
    public double SpareAtLoading => _spareAtLoading;
    /// <summary>
    /// Note 617: the spare the driver reckoned it had when it last weighed a stop, cruising (the lesser of the run home at cruise
    /// and at the pace kept, <see cref="Home"/>), to the dawn and its grace since note 621 (<see cref="Deadline"/>; for tests and
    /// traces). NaN before.
    /// </summary>
    public double Spare { get; private set; } = double.NaN;

    /// <summary>
    /// Note 621: what the driver reckoned of the run home, cruising (every <see cref="ReckonEvery"/> s of the night): when, where
    /// on the line, the dawn then, its two reckonings of the spare (at cruise, and <see cref="Home"/>'s at the pace kept or the
    /// line's authority), the cargo aboard (car-loads) and the stops made. For the harness, to score against the arrival.
    /// </summary>
    public IReadOnlyList<Reckoning> Reckonings => _reckonings;
    readonly List<Reckoning> _reckonings = [];
    double _reckonedAt = double.NegativeInfinity;
    const double ReckonEvery = 30;

    /// <summary>
    /// Note 609: the loading called done for the stop ahead, as a crew says "that'll do, there's the yard still to make": another
    /// stop ahead this crew can work, the spare left once it's backed out (as <see cref="Late"/> reckons it) down to that stop's
    /// <see cref="Allowance"/>, and this one's loading had <see cref="OnwardLoading"/> to be going on with. Without it a stop
    /// loaded until it was late for the dawn, and the next one never had the spare to be made.
    /// </summary>
    bool Onward(World world, StopPlan p)
    {
        if (world.Run is not { } r || p.PickUp)
            return false;
        double spare = Deadline(world, r) - Home(world, r, p.Hold) - LateSpare;
        if (double.IsNaN(_spareAtLoading))
        {
            _spareAtLoading = spare;
            _onwardAllowance = NextAllowance(world, p);
        }
        return _onwardAllowance is { } next && _spareAtLoading > next + OnwardLoading && spare <= next + OnwardMargin;
    }
    /// <summary>Seconds a stop's leaving takes (backing out, clearing, the crew aboard): a stop's loading is late past this.</summary>
    const double LateSpare = 180;
    /// <summary>The run home reckoned at this share of the pace kept so far (T74: frontier:2 kept 13.7 m/s to its stop and 12.2 after).</summary>
    const double LatePace = 0.9;

    double _cruiseTop;

    /// <summary>Where and when the night got under way, to measure its pace by.</summary>
    (double Distance, double Seconds)? _underway;

    /// <summary>
    /// The pace the night's kept (m/s): how far along the main line (<paramref name="at"/>) it's come since it got under way,
    /// over the time it's spent moving (every stop so far, and this one, off the clock). Cruise until there's a minute of it.
    /// </summary>
    /// <summary>
    /// Seconds home from <paramref name="at"/> on the main line: at the pace kept so far, or at what the line allows from
    /// here, whichever's the longer, each with <see cref="LatePace"/>'s margin. The pace alone flattered a Dead Lines route,
    /// whose back half is slower than its front: both deadLines crews of eight made their stop and missed the dawn (T77).
    /// </summary>
    double Home(World world, Run.Run run, double at)
    {
        double end = HomeAt(world, run);
        double home = (end - at) / (Pace(world, run, at) * LatePace);
        if (world.TrackPlan is { } plan)
            home = Math.Max(home, LineGen.LineAuthority.For(plan, world.Train.Line).SecondsTo(at, end, _cruiseTop) / LatePace);
        return home * CargoPressure(world);
    }

    /// <summary>
    /// Note 621: the run home with cargo aboard takes longer than the night's pace so far, by this much a car-load: cargo is the
    /// director's cargo term (note 609: 30% more creatures on the run home loaded, and longer stands for them). On frontier:7's
    /// sixteen nights the run home after the Foundry, 1.5 car-loads aboard, took 1.13 times what <see cref="Home"/> reckoned.
    /// </summary>
    const double CargoPressurePerLoad = 0.09;

    double CargoPressure(World world) => 1 + CargoPressurePerLoad * Math.Max(0, Aboard(world) - _loadAtUnderway);

    /// <summary>
    /// The cargo aboard when the night got under way (car-loads): the pace kept since is already the pace with that much aboard,
    /// so only what's been loaded since weighs the run home more (a train out of the yard loaded, its pace counted twice, passed
    /// frontier:7's Foundry: CarFireTests seed 5).
    /// </summary>
    double _loadAtUnderway;

    static double Aboard(World world) => world.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load);

    /// <summary>Where the run to the end of the line ends, as the night's paced (note 600: not a town terminus's long yard's end).</summary>
    static double HomeAt(World world, Run.Run run) => run.Route.RunLength;

    double Pace(World world, Run.Run run, double at)
    {
        if (_underway is null)
        {
            _underway = (at, run.Seconds);
            _loadAtUnderway = Aboard(world);
        }
        double stopped = _log.Sum(r => r.Seconds) + Seconds(_ticks - _stopStart);
        double moving = run.Seconds - _underway.Value.Seconds - stopped;
        // Cruise is whatever it's set to now (down a spur, a crawl): the fastest it's been set is the line's.
        _cruiseTop = Math.Max(_cruiseTop, Reckoned);
        return moving < 60 ? _cruiseTop : Math.Clamp((at - _underway.Value.Distance) / moving, 3, Math.Max(_cruiseTop, 3));
    }

    readonly HashSet<int> _done = [];
    readonly List<StopRecord> _log = [];
    readonly Dictionary<string, double> _legs = [];
    int _ticks, _legStart, _stopStart, _stillTicks, _sledsAtStart;

    public Leg Doing { get; private set; }

    /// <summary>
    /// A switch the train stands waiting on to be set back with nobody alive to do it (the shunter's dead and no hand has
    /// taken it over): the driver has to get down and do it (a crew of two, one of them lost). Null otherwise.
    /// </summary>
    public Branch? SetBackAlone(World world)
    {
        var train = world.Train;
        Branch? branch = Doing switch
        {
            Leg.SetBack => Switch?.Branch,
            Leg.Clear => Plan?.Spur,
            _ => null,
        };
        // Or a shunter's alive and hasn't come (on the guard van's roof, a deepTerritory:1 crew of two stood at a switch from
        // 1914 s till dawn): the driver waits so long and no longer, and does it itself.
        return branch is { } b && train.Diverging(b.Index) && (!calls.Has(StopJob.Shunter) && Waited > AloneAfter || Waited > ShunterGiveUp) ? b : null;
    }

    /// <summary>
    /// The spur's switch at a stop the driver's making with no shunter (note 261): it gets down and throws it over itself,
    /// once it's waited as long as for a dead shunter's set-back (someone playing may be on their way to it). Null otherwise.
    /// </summary>
    public Branch? ThrowAlone(World world) =>
        Doing == Leg.Held && Plan is { PickUp: false } p && !world.Train.Diverging(p.Spur.Index) && !calls.Has(StopJob.Shunter) && calls.DriverHand
            && (p.CutBehind < 0 || p.NearRake(world.Train) is not null) && Waited > AloneAfter ? p.Spur : null;

    /// <summary>
    /// The most the engine may stand past a hold and still be there: under the hold's two metres short of the points. It was
    /// three, and a train standing 2.5 m over had its front on the points; they wouldn't go over, and a crew of one stood at
    /// the lever till the cold took it (frontier:2, six cars; note 300).
    /// </summary>
    public const double HoldOver = 1.5;
    /// <summary>Seconds the driver waits for someone else to take a dead shunter's part before it gets down itself.</summary>
    const double AloneAfter = 15;
    /// <summary>Seconds it waits for a live shunter who hasn't come: the set-backs that work take well under (46 to 93 s).</summary>
    const double ShunterGiveUp = 150;
    public StopPlan? Plan { get; private set; }
    /// <summary>The coaling stop it's making, if that's what it's doing.</summary>
    public CoalPlan? Coal { get; private set; }
    /// <summary>The switch set wrong it's stopped for (or backing off the dead line of).</summary>
    public SwitchPlan? Switch { get; private set; }
    readonly HashSet<int> _coaled = [];
    double _tenderAtStart;
    /// <summary>The speed it runs up to a stop at (the driver's cruise).</summary>
    public double CruiseSpeed { get; set; } = 14;
    /// <summary>
    /// How far either side of <see cref="CruiseSpeed"/> the run in to a stop is held out on its open stretch (note 820): the
    /// driver's own band when its cruise is the open line's (<c>ConductorBot.OnTheOpenLine</c>), else 0, as it was.
    /// </summary>
    public double CruiseBand { get; set; }
    /// <summary>
    /// The most the run home is reckoned at: the driver's open-line cruise. A cruise put up for a few seconds to drive away
    /// from Fire Flies (note 188) isn't the night's pace.
    /// </summary>
    public double ReckonTop { get; set; } = double.MaxValue;
    double Reckoned => Math.Min(CruiseSpeed, ReckonTop);
    /// <summary>The stops worked so far.</summary>
    public IReadOnlyList<StopRecord> Log => _log;

    double Seconds(int ticks) => ticks * SimConstants.TickSeconds;
    double Waited => Seconds(_ticks - _legStart);

    /// <summary>When (on the leg's clock) the loading was done, or −1 before it is.</summary>
    double _loadedAt = -1;

    void Begin(Leg leg)
    {
        _loadedAt = -1;
        calls.SpareWinch(false);
        calls.PairWorking(false);
        // Called all aboard for the loading until the next stop's (backing out and clearing, they're coming aboard).
        if (leg == Leg.Approach)
        {
            calls.Leave(false);
            calls.LoseCut(false);
            _spareAtLoading = double.NaN;
        }
        if (Doing != Leg.Cruise)
            _legs[Doing.ToString()] = Math.Round(_legs.GetValueOrDefault(Doing.ToString()) + Waited, 1);
        Doing = leg;
        calls.LoadingAhead(leg is Leg.Approach or Leg.Held && Plan is { PickUp: false });
        _legStart = _ticks;
        if (leg == Leg.Loading)
        {
            _progressAt = _ticks;
            _progress = double.NaN;
            calls.Idle(false);
        }
    }

    /// <summary>This tick's cab intent while working a stop; null while there's none to work (drive as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, World world)
    {
        _ticks++;
        var train = world.Train;
        var engine = train.Dynamics;
        _stillTicks = Math.Abs(engine.Velocity) < 0.05 ? _stillTicks + 1 : 0;
        Watch(world);
        if (!self.Alive || !PlayerMotor.InCab(self, train))
            return null;
        bool still = _stillTicks >= SimConstants.TickRate;
        switch (Doing)
        {
            case Leg.Cruise:
                {
                    // Down a dead line (a switch set wrong, taken): stop, and back out onto the main line.
                    if (SwitchPlan.DownOne(world) is { } down)
                    {
                        BeginSwitch(down, Leg.OffDeadLine);
                        return Hold(world);
                    }
                    if (StopPlan.NearRake(train, train.Dynamics.Distance) is not null || !train.OnMain || world.Run is not { } run)
                        return null;
                    // A switch lamp ahead reading wrong: stop short of its points and have it set back (App. A.7).
                    if (SwitchPlan.Ahead(world) is { } wrong && wrong.Hold <= engine.Distance + StoppingDistance(train) + 80)
                    {
                        BeginSwitch(wrong, Leg.ToSwitch);
                        return Toward(world, wrong.Hold, +1, CruiseSpeed, band: CruiseBand);
                    }
                    // Every stop is optional (GDD §18): only one there's time for before the dawn, after the run to the end of
                    // the line. The tender's the exception once it's low: without coal there's no getting there at all.
                    if (run.Seconds > 0 && _underway is null)
                    {
                        _underway = (engine.Distance, run.Seconds);
                        _loadAtUnderway = Aboard(world);
                    }
                    _cruiseTop = Math.Max(_cruiseTop, Reckoned);
                    // Note 621: at the night's cruise (the lamp's or the dark's), not this moment's: a crawl for a Track Doll or a
                    // switch reckoned the run home at it (frontier:7 seeds 4 and 14: spares of -1,205 and -13,989 s).
                    double spare = Deadline(world, run) - (HomeAt(world, run) - engine.Distance) / Math.Max(1, Math.Min(_cruiseTop, ReckonTop)) * CargoPressure(world);
                    // A stop's worth making only with its loading's time in hand, reckoned as the loading's own lateness is
                    // (T74): at the pace the night's kept, a stop that would be late the moment it starts loading isn't one.
                    double spareAtPace = Deadline(world, run) - Home(world, run, engine.Distance);
                    Spare = Math.Min(spare, spareAtPace);
                    if (run.Seconds >= _reckonedAt + ReckonEvery)
                    {
                        _reckonedAt = run.Seconds;
                        double load = engine.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load);
                        _reckonings.Add(new Reckoning(run.Seconds, engine.Distance, run.DawnIn, Grace(world), spare, spareAtPace, load, _log.Count));
                    }
                    // Note 617: each kind of stop with its own allowance, the nearest that has it (a switchyard's own track turned
                    // down, the pick-up on the siding beside it: that's only the coupling up).
                    double reckoned = Spare;
                    var plan = StopPlan.Ahead(world, engine.Distance, _done, calls, a => reckoned > Allowance(a, world));
                    bool low = train.BoilerTuning is { } bt && train.Boiler.Tender < bt.TenderCapacity * 0.2;
                    var coal = spare > CoalAllowance || low ? CoalPlan.Ahead(world, engine.Distance, _coaled, calls) : null;
                    double reach = engine.Distance + StoppingDistance(train) + 80;
                    // Whichever comes first, when it's near enough to start stopping for.
                    if (coal is not null && coal.Hold <= reach && (plan is null || coal.Hold < plan.Hold))
                    {
                        Coal = coal;
                        _legs.Clear();
                        _stopStart = _ticks;
                        _tenderAtStart = train.Boiler.Tender;
                        Begin(Leg.ToCoal);
                        return Toward(world, coal.Hold, +1, CruiseSpeed, band: CruiseBand);
                    }
                    if (plan is null || plan.Hold > reach)
                        return null;
                    Plan = plan;
                    _legs.Clear();
                    _stopStart = _ticks;
                    _sledsAtStart = plan.Site.SledsLeft;
                    Begin(Leg.Approach);
                    return Toward(world, plan.Hold, +1, CruiseSpeed, band: CruiseBand);
                }
            case Leg.ToSwitch:
                {
                    var w = Switch!;
                    if (SwitchPlan.DownOne(world) is not null)
                    {
                        Begin(Leg.OffDeadLine);
                        return Hold(world);
                    }
                    if (still && w.StandingAt(train))
                    {
                        Begin(Leg.SetBack);
                        return Hold(world);
                    }
                    return engine.Distance > w.Hold + HoldOver && (still || engine.Velocity < 0) ? Toward(world, w.Hold, -1, 1) : Toward(world, w.Hold, +1, CruiseSpeed, band: CruiseBand);
                }
            case Leg.OffDeadLine:
                {
                    var w = Switch!;
                    if (train.OnMain && still && engine.Distance <= w.Hold + HoldOver)
                    {
                        Begin(Leg.SetBack);
                        return Hold(world);
                    }
                    return Toward(world, w.Hold, -1, SetBackTop);
                }
            case Leg.SetBack:
                // Until it's set back for the main line and everyone's aboard (or given the time to be, as at a stop). Nobody
                // to do it, nobody goes anywhere: over those points is the dead line again.
                if (!train.Diverging(Switch!.Branch.Index) && (calls.AllAboard || Waited > AboardGiveUp))
                    Begin(Leg.Forward);
                return Hold(world);
            case Leg.Forward:
                if (world.Controls.Reverser < 0)
                    return Toward(world, Switch!.Hold + 50, +1, SetBackTop); // flips it at a stand
                FinishSwitch();
                return null;
            case Leg.ToCoal:
                {
                    var c = Coal!;
                    if (c.StandingAt(train) && still)
                    {
                        Begin(Leg.Coaling);
                        return Hold(world);
                    }
                    // A little past it (or short and stopped): creep to it.
                    return engine.Distance > c.Hold + 0.5 && (still || engine.Velocity < 0) ? Toward(world, c.Hold, -1, 1) : Toward(world, c.Hold, +1, CruiseSpeed, band: CruiseBand);
                }
            case Leg.Coaling:
                {
                    // Until the chute's been opened and shut again (or nobody's come to it), and everyone's back aboard.
                    bool poured = train.Boiler.Tender > _tenderAtStart + 1 && world.Run is { ChuteOpen: false };
                    bool nobody = !calls.Has(StopJob.Shunter) || Waited > CoalGiveUp;
                    if ((poured || nobody) && (calls.AllAboard || Waited > CoalGiveUp + AboardGiveUp))
                        Begin(Leg.Depart);
                    return Hold(world);
                }
            case Leg.Approach:
                {
                    var p = Plan!;
                    if (still && engine.Distance - p.Hold is > -3 and <= HoldOver)
                    {
                        Begin(Leg.Held);
                        return Hold(world);
                    }
                    // Overran it: back up to it (the points won't go over with a wheel on them).
                    return engine.Distance > p.Hold + HoldOver && (still || engine.Velocity < 0) ? Toward(world, p.Hold, -1, 1) : Toward(world, p.Hold, +1, CruiseSpeed, band: CruiseBand);
                }
            case Leg.Held:
                {
                    var p = Plan!;
                    bool set = train.Diverging(p.Spur.Index);
                    bool cut = p.CutBehind < 0 || p.NearRake(train) is not null;
                    // Note 261: the driver throws it alone, but can't cut the train: with the shunter lost before the cut, go on.
                    bool canThrow = calls.Has(StopJob.Shunter) || calls.DriverHand && cut;
                    if (!set && (!canThrow || Waited > HeldGiveUp))
                    {
                        // Nobody to throw it: couple back up if the rest were cut off, and go. Called off, so a shunter
                        // still on its way to the stand throws nothing (note 550).
                        calls.Leave(true);
                        Begin(p.NearRake(train) is not null ? Leg.BackOut : Leg.Clear);
                        return Hold(world);
                    }
                    // Note 655: set, but the cut never made and nobody to make it (deepTerritory:7 seed 7: the shunter grabbed off
                    // the coupling by a Whistler after the points went over, and the train stood there till dawn with the other
                    // three in the cab). Called off as above: the driver throws the points back itself (SetBackAlone), and on.
                    if (set && !cut && (!calls.Has(StopJob.Shunter) && Waited > AloneAfter || Waited > HeldGiveUp))
                    {
                        calls.Leave(true);
                        Begin(Leg.Clear);
                        return Hold(world);
                    }
                    // Everyone with a part aboard the engine's rake, or long enough waiting that someone isn't coming (kept off
                    // it by something in a car, say: T64's Climbers): in without them, as the loading leg goes on without them.
                    if (set && cut && (calls.Riding(OnTheTrain(train)) || Waited > HeldGiveUp + AboardGiveUp))
                        Begin(Leg.SpurIn);
                    return Hold(world);
                }
            case Leg.SpurIn:
                {
                    var p = Plan!;
                    if (engine.Path == p.Spur.Index && still && engine.Distance > p.Spur.End - 6)
                    {
                        Begin(Leg.Loading);
                        return Hold(world);
                    }
                    // A switchyard's cars standing at the end (note 187): onto them at a crawl, so they couple, as onto a cut.
                    var standing = train.Rakes.FirstOrDefault(r => r.Path == p.Spur.Index && train.Standing(r) && !TrainOnLine.IsHandcar(r));
                    if (standing is not null)
                    {
                        var g = engine.Tuning.Geometry;
                        double onto = standing.RearDistance - g.CouplingGap + 0.5;
                        return Toward(world, onto, +1, onto - engine.Distance < 15 ? 0.8 : SetBackTop);
                    }
                    return Toward(world, p.Spur.End - 1, +1, SetBackTop);
                }
            case Leg.Loading:
                {
                    var p = Plan!;
                    // Late: the dawn won't wait for the rest (T70).
                    bool late = Late(world, p) || Onward(world, p);
                    // A pick-up's loading is the coupling, done once it's run up to the end (note 187).
                    // Note 609: or it's had its time, on a short night, for a crew of bots (people playing call their own done).
                    bool loaded = p.PickUp || Loaded(world, p) || Waited > LoadingGiveUp || late
                        || calls.PeopleHands == 0 && Waited > LoadingFor && _spareAtLoading < (_onwardAllowance ?? StopAllowance) + LoadingGiveUp;
                    // The rest in, the grain elevator's conveyor first (note 400): walked under its head a car at a time, the
                    // shunter starting it at its drive house and roaming its belt for the jams. It reaches the cars ahead of the
                    // spout's, so the spout takes the rest after it.
                    if (loaded && !late && Waited <= LoadingGiveUp && calls.Has(StopJob.Shunter) && p.ConveyorTarget(world) is not null)
                    {
                        Begin(Leg.Conveying);
                        return Hold(world);
                    }
                    // Then its spout (GDD §18): walked under it a car at a time, the shunter on its lever, the rest of the crew
                    // aboard.
                    if (loaded && !late && Waited <= LoadingGiveUp && calls.Has(StopJob.Shunter) && p.SpoutTarget(world) is not null)
                    {
                        Begin(Leg.Spouting);
                        return Hold(world);
                    }
                    // Or the mine head's steam lift (note 368), the same way, the engine's steam to it at the chute.
                    if (loaded && !late && Waited <= LoadingGiveUp && calls.Has(StopJob.Shunter) && p.LiftTarget(world) is not null)
                    {
                        Begin(Leg.Lifting);
                        return Hold(world);
                    }
                    // And then its tipple (note 423): each car stood true in the cradle, the shunter on its lever.
                    if (loaded && !late && Waited <= LoadingGiveUp && calls.Has(StopJob.Shunter) && p.TippleTarget(world) is not null)
                    {
                        Begin(Leg.Tippling);
                        return Hold(world);
                    }
                    // Everyone aboard, or long enough waited for them since the loading was done (not since it began: a stop
                    // given up for the dawn waited out the whole give-up again for a hand still out, T70).
                    if (loaded && _loadedAt < 0)
                        _loadedAt = Waited;
                    calls.Leave(loaded);
                    if (loaded && (calls.Riding(OnTheTrain(train)) || Waited - _loadedAt > AboardGiveUp))
                        Begin(Leg.BackOut);
                    return Hold(world);
                }
            case Leg.Spouting:
                {
                    var p = Plan!;
                    bool late = world.Run is { } sr && Deadline(world, sr) < Home(world, sr, p.Hold) + LateSpare + AboardGiveUp || Onward(world, p);
                    var target = p.SpoutTarget(world);
                    calls.Leave(false);
                    if (target is null || late || !calls.Has(StopJob.Shunter) || Waited > SpoutGiveUp)
                    {
                        // Done (or given up): all aboard, and back out as from any stop.
                        calls.Leave(true);
                        if (calls.Riding(OnTheTrain(train)) || Waited > SpoutGiveUp + AboardGiveUp)
                            Begin(Leg.BackOut);
                        return Hold(world);
                    }
                    // Nobody moves the train with a hand still climbing aboard, or while the grain's coming down.
                    if (!calls.RidingBut(OnTheTrain(train), StopJob.Shunter) && Waited < AboardGiveUp || world.Run?.Sites[p.Facility]?.Pouring == true)
                        return Hold(world);
                    double front = target.Value.Front;
                    if (Math.Abs(engine.Distance - front) < 0.5)
                        return Hold(world);
                    return Toward(world, front, engine.Distance > front ? -1 : 1, 1.5);
                }
            case Leg.Conveying:
                {
                    var p = Plan!;
                    bool late = world.Run is { } cr && Deadline(world, cr) < Home(world, cr, p.Hold) + LateSpare + AboardGiveUp || Onward(world, p);
                    var target = p.ConveyorTarget(world);
                    calls.Leave(false);
                    if (target is null || late || !calls.Has(StopJob.Shunter) || Waited > ConveyGiveUp)
                    {
                        // Done (or given up): the spout next if it has a car to fill, else all aboard and back out.
                        if (!late && calls.Has(StopJob.Shunter) && p.SpoutTarget(world) is not null)
                        {
                            Begin(Leg.Spouting);
                            return Hold(world);
                        }
                        calls.Leave(true);
                        if (calls.Riding(OnTheTrain(train)) || Waited > ConveyGiveUp + AboardGiveUp)
                            Begin(Leg.BackOut);
                        return Hold(world);
                    }
                    // Nobody moves the train with a hand still climbing aboard, or while the belt's carrying into a car; jammed
                    // or stopped, the car under the head waits for it.
                    if (!calls.RidingBut(OnTheTrain(train), StopJob.Shunter) && Waited < AboardGiveUp || world.Run?.Sites[p.Facility]?.Carrying == true)
                        return Hold(world);
                    double front = target.Value.Front;
                    if (Math.Abs(engine.Distance - front) < 0.5)
                        return Hold(world);
                    return Toward(world, front, engine.Distance > front ? -1 : 1, 1.5);
                }
            case Leg.Tippling:
                {
                    var p = Plan!;
                    bool late = world.Run is { } tr && Deadline(world, tr) < Home(world, tr, p.Hold) + LateSpare + AboardGiveUp || Onward(world, p);
                    var site = world.Run?.Sites[p.Facility];
                    var target = p.TippleTarget(world);
                    calls.Leave(false);
                    // A car off its rails in the cradle holds the train where it is, late or not, till a wrench puts it back
                    // (the shunter's); and nothing moves with a car clamped or being clamped: moved, it comes off its rails.
                    if (site is not null && (world.Run!.OffRailsAt(train, site) is not null || site.Clamped >= 0 || site.Clamp > 0))
                        return Hold(world);
                    if (target is null || late || !calls.Has(StopJob.Shunter) || Waited > TippleGiveUp)
                    {
                        calls.Leave(true);
                        if (calls.Riding(OnTheTrain(train)) || Waited > TippleGiveUp + AboardGiveUp)
                            Begin(Leg.BackOut);
                        return Hold(world);
                    }
                    if (!calls.RidingBut(OnTheTrain(train), StopJob.Shunter) && Waited < AboardGiveUp)
                        return Hold(world);
                    // The car stood true in the cradle, slowly: a clamp shut on one stood off its middle derails it on the roll.
                    double front = target.Value.Front;
                    if (Math.Abs(engine.Distance - front) < TippleTrue)
                        return Hold(world);
                    return Toward(world, front, engine.Distance > front ? -1 : 1, 1.0);
                }
            case Leg.Lifting:
                {
                    var p = Plan!;
                    bool late = world.Run is { } lr && Deadline(world, lr) < Home(world, lr, p.Hold) + LateSpare + AboardGiveUp || Onward(world, p);
                    var target = p.LiftTarget(world);
                    calls.Leave(false);
                    if (target is null || late || !calls.Has(StopJob.Shunter) || Waited > LiftGiveUp)
                    {
                        // Done: the tipple next if it has a car to fill (the shunter goes from the lift's lever to it by the same
                        // rule; given up, the lift's still the shunter's, so it's all aboard), else all aboard and back out.
                        if (target is null && !late && calls.Has(StopJob.Shunter) && p.TippleTarget(world) is not null)
                        {
                            Begin(Leg.Tippling);
                            return Hold(world);
                        }
                        calls.Leave(true);
                        if (calls.Riding(OnTheTrain(train)) || Waited > LiftGiveUp + AboardGiveUp)
                            Begin(Leg.BackOut);
                        return Hold(world);
                    }
                    if (!calls.RidingBut(OnTheTrain(train), StopJob.Shunter) && Waited < AboardGiveUp)
                        return Hold(world);
                    var site = world.Run?.Sites[p.Facility];
                    double front = target.Value.Front;
                    if (Math.Abs(engine.Distance - front) < 0.5)
                    {
                        // At the chute, the steam to the lift while the shunter's on its lever (spec D.2: "venting pressure to
                        // power it"): the gauge to nothing, and the next car waits on the fire.
                        var standing = Hold(world);
                        return site is { LeverHeld: true } ? standing with { Actions = standing.Actions | PlayerActions.Vent } : standing;
                    }
                    if (site?.Winding == true)
                        return Hold(world);
                    return Toward(world, front, engine.Distance > front ? -1 : 1, 1.5);
                }
            case Leg.BackOut:
                {
                    var p = Plan!;
                    var left = p.NearRake(train);
                    // Note 261: a cut running away down the grade (the Passenger let its brakes off) faster than a set-back, or
                    // still rolling once the engine's gone a long way back after it, is lost, not chased: deepTerritory:2's
                    // engine went after one at 22 m/s for 7 km, its crew left at the mine head, and a crew of eight's followed
                    // one at 6 m/s for a kilometre and more, never closing. Back to the points without it, and on.
                    if (left is { Velocity: < -RunawayAbove } || left is { Velocity: < -0.5 } && engine.Distance < p.Hold - ChaseFor)
                    {
                        left = null;
                        calls.LoseCut();
                    }
                    bool together = left is null;
                    // Note 609: back from the chase after a cut lost down the grade, up to the points again first, where the
                    // shunter sets them back from the cab: frontier:3 seed 1's driver stood 400 m short of them while the
                    // shunter walked there and back, and went on without it.
                    if (together && train.OnMain && engine.Distance < p.Hold - ChaseBackShort)
                        return Toward(world, p.Hold, +1, SetBackTop);
                    if (together && train.OnMain && still && engine.Distance <= p.Hold + HoldOver)
                    {
                        Begin(Leg.Clear);
                        return Hold(world);
                    }
                    // Onto the cars left waiting: aim a little into them so the rakes touch (at a crawl, so they couple) rather
                    // than stop just short; then the whole train back clear of the points.
                    var g = engine.Tuning.Geometry;
                    double target = left is null ? p.Hold : left.Distance + g.CouplingGap - 0.5;
                    bool close = left is not null && engine.RearDistance - target < 15;
                    // Closing on them at a crawl, over whatever they're doing themselves: cars left on a grade roll away down it
                    // (deepTerritory:1's mine head: at a metre a second, with the engine creeping after them at 0.8 for 1,010 s
                    // and a kilometre, and a hand left on the ballast).
                    double rolling = left is { Velocity: < 0 } ? -left.Velocity : 0, crawl = 0.8 + rolling;
                    return Toward(world, target, -1, close ? crawl : SetBackTop + rolling, rear: !together, away: rolling);
                }
            case Leg.Clear:
                if (!train.Diverging(Plan!.Spur.Index) && (calls.AllAboard || Waited > AboardGiveUp))
                    Begin(Leg.Depart);
                return Hold(world);
            case Leg.Depart:
                if (world.Controls.Reverser < 0)
                    return Toward(world, (Plan?.Hold ?? Coal!.Hold) + 50, +1, SetBackTop); // brakes and flips the reverser at a stand
                if (Coal is not null)
                    FinishCoaling(train);
                else
                    Finish();
                return null; // away as usual
            default:
                return null;
        }
    }

    /// <summary>
    /// Late for the dawn (T70): what's left of the night against the run home at the pace the night's actually kept (its
    /// curves and grades and what's been on the line; cruise is flattery), and the leaving. Measured from where the train
    /// left the main line: down the spur, the engine's distance is the spur's. And the wait for everyone aboard after it,
    /// which can run to its give-up (T74: hands down off the cars for trouble in them kept frontier:2's crew of eight waiting
    /// the whole of it), on a line that needn't be as quick after the stop as before it.
    /// </summary>
    bool Late(World world, StopPlan p) => world.Run is { } lr && Deadline(world, lr) < Home(world, lr, p.Hold) + LateSpare + AboardGiveUp;

    /// <summary>
    /// Note 609: the shunter and the driver on the winch while the pair work the crane (<see cref="CrewCalls.SparesOnWinch"/>),
    /// once the train's at the end with both to do and a crew whole for both: the pair, the shunter, and the driver free to
    /// get down. Called off once the crane's done (the pair take the winch on from there: the driver's back to its fire every
    /// minute or so, and the winch stands while it's gone), the sleds are in (or have nowhere to go), the loading's done or
    /// late, or one of them's lost.
    /// </summary>
    void SpareWinch(World world, StopPlan p, bool late)
    {
        var train = world.Train;
        bool sleds = p.Site.Has(ModuleKind.Winch) && p.Site.SledsLeft > 0 && world.Run is { } r && r.SledHasRoom(train, p.Site);
        bool crew = calls.Has(StopJob.Winch0) && calls.Has(StopJob.Winch1) && calls.Has(StopJob.Shunter) && calls.DriverHand;
        bool on = !p.PickUp && sleds && crew && !late && _loadedAt < 0 && Waited <= LoadingGiveUp && p.AtTheEnd(train) && calls.CanWorkPair
            && p.Site.Crane is { } crane && (StopHand.CraneTarget(crane, train) is not null || crane.Hooked is not null);
        calls.SpareWinch(on);
    }

    /// <summary>
    /// Everything at the stop this crew can load, loaded (note 261: by the crew that's there). The winch: nothing left to haul,
    /// nobody to haul it, or nowhere for it to go (T66: the cars the sleds load full already, with the crane's castings, and
    /// cranking on hauls nothing). The castings on (T54), until there's none left or no room under the gantry. The crates in,
    /// and the doors they went in by shut again: nobody moves a train with its doors open. GDD §18's set pieces (note 185):
    /// the herd up the ramp, the hose off again. A pair module the bots can't make up alone is waited on while the people
    /// playing are getting somewhere with the loading (<see cref="CrewCalls.PairNow"/>), and so are crates only they carry.
    /// </summary>
    bool Loaded(World world, StopPlan p)
    {
        var train = world.Train;
        bool pairs = calls.PairNow;
        bool winched = !p.Site.Has(ModuleKind.Winch) || p.Site.SledsLeft == 0 || !pairs || world.Run is { } wr && !wr.SledHasRoom(train, p.Site);
        // (A crane pinged, note 739 part two, is waited on as the pair's would be: its two are sent whatever the pair.)
        bool craned = p.Site.Crane is not { } crane || !pairs && !calls.CraneOrdered || StopHand.CraneTarget(crane, train) is null;
        bool crated = calls.CrateHands == 0 && (calls.PeopleHands == 0 || calls.PeopleIdle)
            || !p.CratesToLoad(world, calls.HeavyHands) && !p.OpenSideDoors(train).Any();
        bool herded = !pairs || !p.HerdLeft(world);
        bool hosed = !calls.HoseHand || !p.HoseLeft(world);
        // Note 326: nor while a hand's out searching the village's houses, or bringing a find back.
        bool searched = !calls.Scavenging;
        return winched && crated && craned && herded && hosed && searched;
    }

    double _progress;
    int _progressAt;

    /// <summary>
    /// Note 261: what the driver gets down to do at the loading, with the train at the end of the spur and too few others for
    /// it (<see cref="CrewCalls.Lend"/>): a winch handle (or the crane's, the ramp's or the hose's part), or the crates. None
    /// while it's not at a loading, the loading's done or given up, or there's nothing for it.
    /// </summary>
    public StopJob DriverPart(World world)
    {
        if (Doing != Leg.Loading || Plan is not { PickUp: false } p || _loadedAt >= 0 || calls.Leaving || !p.AtTheEnd(world.Train)
            || Waited > LoadingGiveUp || Late(world, p) || !calls.DriverHand)
            return StopJob.None;
        return calls.Lend(StopJob.Driver, part => p.PairWork(world, part), p.CrateWork(world, calls.HeavyHands));
    }

    /// <summary>
    /// At the loading, whether it's getting anywhere (note 261): with nothing in the cars, the sleds or the castings moving
    /// for <c>crew.peopleWait</c>, the driver calls it idle, and what only the people playing could do is left.
    /// </summary>
    void Watch(World world)
    {
        if (Doing != Leg.Loading || Plan is not { } p)
            return;
        // Note 609: called from here, every tick: the driver's out at the winch itself for much of it, not in the cab.
        bool late = Late(world, p) || Onward(world, p);
        SpareWinch(world, p, late);
        // Note 617: and whether the pair are at their own work with the crates still to carry. Not at a crane's (the Foundry's):
        // there the shunter's on the winch with the driver while the pair are at the crane (note 609), and out at the crates
        // after it, it was 15–35 s further from the train when the loading was called done, and at its points later.
        calls.PairWorking(!late && _loadedAt < 0 && !p.PickUp && p.Site.Crane is null && p.AtTheEnd(world.Train) && calls.Has(StopJob.Winch0) && calls.Has(StopJob.Winch1)
            && calls.PairNow && p.PairWork(world, StopJob.Winch0) && p.CrateWork(world, calls.HeavyHands));
        double now = p.Progress(world);
        if (double.IsNaN(_progress) || Math.Abs(now - _progress) > 1e-6)
        {
            _progress = now;
            _progressAt = _ticks;
        }
        double wait = world.Run?.FacilityTuning?.Crew.PeopleWait ?? new StopCrewTuning().PeopleWait;
        calls.Idle(Seconds(_ticks - _progressAt) > wait);
    }

    void BeginSwitch(SwitchPlan plan, Leg leg)
    {
        Switch = plan;
        _legs.Clear();
        _stopStart = _ticks;
        Begin(leg);
    }

    void FinishSwitch()
    {
        Begin(Leg.Cruise);
        _log.Add(new StopRecord(-1, "SwitchSetBack", Math.Round(Seconds(_ticks - _stopStart), 1), 0, new Dictionary<string, double>(_legs)));
        Switch = null;
    }

    void FinishCoaling(TrainOnLine train)
    {
        var c = Coal!;
        Begin(Leg.Cruise);
        _coaled.Add(c.Facility);
        _log.Add(new StopRecord(c.Facility, nameof(FacilityKind.CoalingTower), Math.Round(Seconds(_ticks - _stopStart), 1), 0,
            new Dictionary<string, double>(_legs), Math.Round(train.Boiler.Tender - _tenderAtStart, 1)));
        Coal = null;
    }

    void Finish()
    {
        var p = Plan!;
        Begin(Leg.Cruise);
        _done.Add(p.Key);
        _log.Add(new StopRecord(p.Facility, (p.Site.Feature.Facility?.ToString() ?? "") + (p.PickUp ? $"PickUp{p.Spur.Index}" : ""), Math.Round(Seconds(_ticks - _stopStart), 1),
            _sledsAtStart - p.Site.SledsLeft, new Dictionary<string, double>(_legs),
            Castings: p.Site.Crane?.Castings.Count(c => c.State == CastingState.Loaded) ?? 0));
        Plan = null;
    }

    /// <summary>
    /// Aboard, for a stop's waits: on any car of the train, the ones cut off on the main as much as the engine's rake (T76).
    /// The engine comes back for the cut and couples up to it, as it does for anyone warming in it (T64). deadLines:2's
    /// gunner and two crate hands were on the car behind the cut, which nothing sent them across, and the driver waited
    /// out both give-ups for them.
    /// </summary>
    static IReadOnlyCollection<int> OnTheTrain(TrainOnLine train) => [.. train.Vehicles.Select(v => v.Id)];

    /// <summary>
    /// What the driver plans a stop on (m/s²): half of what the brake does now (faded or not), and never more than 0.8 of what
    /// it does net of the steam. With steam
    /// driving (T97) the engine pulls at full effort against the brake under the speed its steam makes, so a stop from line
    /// speed takes nearly twice the bare brake's distance. Reckoned on the bare brake, the driver set off for a switch set
    /// wrong too late and ran onto the dead line (note 289: frontier:7's harness night took both of its Switchmen's).
    /// </summary>
    static double BrakeRate(TrainOnLine train)
    {
        var engine = train.Dynamics;
        double mass = engine.Consist.MassTonnes;
        double brake = engine.MaxBrakeForce * engine.BrakeEfficiency / mass;
        bool pulling = train.BoilerTuning is { SteamDrive: true } bt && train.Boiler.SteamSpeed(bt, engine.Tuning.MaxSpeed) > 0;
        double pull = pulling ? engine.MaxTractiveForce / mass : 0;
        return Math.Max(0.1, Math.Min(0.5 * brake, 0.8 * (brake - pull)));
    }

    static double StoppingDistance(TrainOnLine train) => train.Dynamics.Speed * train.Dynamics.Speed / (2 * BrakeRate(train));

    /// <summary>Throttle notches to move from where it's set to <paramref name="to"/>.</summary>
    static sbyte Notch(in TrainControls c, double to) => (sbyte)Math.Clamp(Math.Round((to - c.Throttle) * 4), -4, 4);

    static PlayerIntent Hold(World world) => new() { Buttons = PlayerButtons.Brake, ThrottleNotch = Notch(world.Controls, 0) };

    /// <summary>
    /// Drives the engine's rake to put its front (or with <paramref name="rear"/>, its back) at a distance, at up to
    /// <paramref name="top"/> m/s and gently at the end, on the regulator and the brake handle like someone watching the
    /// ground: the reverser first, which only moves at a stand.
    /// </summary>
    /// <summary>
    /// The top speed of a shunting move: back to a switch, onto a cut, along a spur, back for a crewmate (m/s). T110
    /// playtest: at 3 m/s ("agonizingly slow") a set-back of a few hundred metres took a minute and more; the moves brake to
    /// their mark by <see cref="Toward"/>'s own curve, so the top is only the open stretch between.
    /// </summary>
    public const double SetBackTop = 6;
    /// <summary>A cut left on the main line rolling away faster than this (m/s) is lost, not chased (note 261).</summary>
    const double RunawayAbove = SetBackTop;
    /// <summary>How far back past its points the engine goes after a cut that's still rolling away (m; note 261).</summary>
    const double ChaseFor = 400;
    /// <summary>Note 609: further short of its points than this (m), back from such a chase, the engine runs up to them again.</summary>
    public const double ChaseBackShort = 20;

    /// <summary>
    /// A shunt out on its open stretch is held this far either side of its top (m/s; note 820). The brake's all or nothing,
    /// and with steam driving (T97) the engine pulls at full effort the moment it's off: held to 0.2 over the top and let off
    /// at once, a 6 m/s set-back was on and off every tick (AU1's audit: 1,247 of a bot night's 1,471 applications, each heard).
    /// </summary>
    public const double ShuntBand = 1;

    /// <summary>On the curve to its mark, how far under the speed it wants (m/s) a brake that's on is let off (note 820): a
    /// third of it, from 0.3 at a crawl (coupling up as gently as ever) to 2.</summary>
    public static double LetOffUnder(double wanted) => Math.Clamp(wanted / 3, 0.3, 2);

    /// <param name="away">How fast the target itself is going on the way we're going (m/s): rolling cars to catch up.</param>
    /// <param name="band">Out on the open stretch, held this far either side of <paramref name="top"/> on a line plan (note 820);
    /// 0: a shunt's <see cref="ShuntBand"/> at a shunt's top, else held to 0.2 over the speed wanted.</param>
    public static PlayerIntent Toward(World world, double target, int direction, double top, bool rear = false, double away = 0, double band = 0)
    {
        var engine = world.Train.Dynamics;
        var controls = world.Controls;
        if ((controls.Reverser >= 0 ? 1 : -1) != direction)
        {
            var stop = Hold(world);
            if (Math.Abs(engine.Velocity) < 0.05)
                stop.Buttons |= PlayerButtons.Reverser;
            return stop;
        }
        double left = (target - (rear ? engine.RearDistance : engine.Distance)) * direction;
        if (left <= 0.3)
            return Hold(world);
        // Closing on it at what stops us there, over its own speed: braking for a target that moves away from under the curve,
        // the engine settled a couple of metres behind rolling cars at their speed and never touched them (note 188).
        double curve = Math.Max(0, away) + Math.Sqrt(2 * BrakeRate(world.Train) * left);
        double wanted = Math.Min(top, curve);
        double speed = engine.Velocity * direction;
        // A brake that's on is held (note 820). Out on its open stretch, the curve to its mark well above, about its top: on
        // over it, off under it, so it runs at its top on the whole (T110: a slower shunt was agonizing). On the curve, till
        // well under it (LetOffUnder). On a line plan, as every night the game runs has: the hand-built lines' nights were
        // found on the driver as it was. Not a chase: a cut rolling away down the grade is caught at once or not at all
        // (RollBackTests).
        bool plan = world.TrackPlan is not null && away <= 0;
        double b = band > 0 ? band : top <= SetBackTop ? ShuntBand : 0;
        bool open = plan && b > 0 && curve >= top + b;
        double under = !plan ? 0 : open ? b : LetOffUnder(wanted);
        bool held = under > 0 && controls.Brake > 0 && engine.Speed >= Net.CabControls.StandingBelow && speed > wanted - under;
        return speed < wanted - 0.3 && !held ? new PlayerIntent { ThrottleNotch = Notch(controls, 0.5) }
            : speed > wanted + (open ? b : 0.2) || held ? Hold(world)
            : new PlayerIntent { ThrottleNotch = Notch(controls, 0) };
    }
}

/// <summary>
/// A crew member's part on the ground at a facility stop (GDD §17: "someone is on the ground at every junction"), all
/// through intent. The shunter drops into the gap and cuts the train, walks up to the stand and throws the switch, rides
/// in and out in the cab, then sets it back and climbs aboard. The two on the winch ride in, crank it together until the
/// sleds are in, and climb aboard. With no part, or nothing to do this tick, it returns null and the walker walks.
/// </summary>
public sealed partial class StopHand(StopJob job, CrewCalls calls, int member)
{
    /// <summary>Walking beside the train, keep this far from its track's centreline (a car is 3 m wide, 4.2 at its steps).</summary>
    const double Clear = 2.6;
    /// <summary>Stand this far from the track at the switch stand: in reach of the lever (2.6 m out), clear of the train.</summary>
    const double StandOff = 3.2;


    readonly HashSet<int> _done = [];
    StopPlan? _plan;
    /// <summary>Its part in a facility stop is under way: the train's standing for it, or in at it, and not yet away (note 601).</summary>
    public bool AtAStop => _plan is not null;
    bool _wentIn, _reachedEnd;

    public StopJob Job => job;
    /// <summary>
    /// The bot's own player id, once it has one (the harness says): heavy crates (T45) need to know which end is whose.
    /// Without it, a hand leaves heavy crates to others.
    /// </summary>
    public int? PlayerId { get; set; }
    /// <summary>What it's doing (for tests and traces).</summary>
    public string Doing { get; private set; } = "";
    /// <summary>Stops it has done its part at.</summary>
    public int Worked { get; private set; }

    /// <summary>
    /// Note 609: says where it is, every tick, whatever it's about. Only its part's own work said so, and a hand off at
    /// anything else (the Fire Flies' lamp, a crewmate held, the cold) was heard where it last worked from: frontier:7's
    /// gunner said "in the cab" and went to a lamp, the driver took the train down the spur with it on the ballast, and the
    /// crane stood without its rigger for the whole of the loading (seeds 4 and 8 loaded nothing).
    /// </summary>
    public void Report(in PlayerState self) => calls.Say(member, job, self);

    /// <summary>It's gone in off the roofs (and says where it is), or come out again.</summary>
    public void Warming(in PlayerState self, bool on)
    {
        calls.Warming(member, on);
        if (on)
            calls.Say(member, job, self);
    }

    /// <summary>
    /// Whether this hand is one to leave the loading for trouble in a car: anyone but a crate hand, and of the crate hands
    /// the first <see cref="TroubleHands"/>. A frontier:11 Slaughterhouse had Gnawers and loose loads one after another the
    /// whole stop; with every crate hand going to each, nothing was loaded in 420 s and four died of it.
    /// </summary>
    public bool TakesTrouble => job != StopJob.Crates || calls.AmongFirst(member, StopJob.Crates, TroubleHands);

    /// <summary>
    /// Note 496: Fire Flies on a car's lamp at a stop (App. A.5: about 20 s on it and the car's alight) are every hand's,
    /// whatever its part: putting the lamp out is one press, and the car's lost if nobody does. Only the nearest goes (the
    /// rest keep at their parts). A crew of four has no crate hand, its parts are the shunter and the winch pair, and the
    /// lamps the flies came to were never put out: frontier:7's Talbot Foundry lost a car to them on every seed, held on the
    /// main with its hands cutting the train and boarding the cab. Too far to get there in time (the guard van, 75 m back
    /// along the ballast), the fire they light is the same hand's: in its first seconds it's smoke and burns nobody
    /// (App. C.5), an extinguisher puts it out a cell a second, and the one who went for the lamp sees it through.
    /// </summary>
    public bool TakesLamp(in PlayerState self, World world, Enemies.Enemy trouble)
    {
        var train = world.Train;
        int car = trouble.Attached;
        // The flies only while the train stands (note 526): pulling away breaks them off by itself (App. A.5), and a hand sent
        // along the roofs for them as the train went in under it was dragged off car 2's roof (seed 6).
        bool flies = trouble is Enemies.FireFlies && Math.Abs(train.Dynamics.Velocity) < 0.05
            || trouble is Enemies.CarFire { Phase: Enemies.SpinePhase.Dormant or Enemies.SpinePhase.Telegraph };
        // At a stop only (its plan taken): between them, the walkers' own rounds see to it, and a gunner's gun comes first.
        if (job == StopJob.Driver || _plan is null || !self.Alive || car <= 0 || car >= train.Frames.Count
            || !flies && !(trouble is Enemies.CarFire && calls.LampHand(car) == member))
        {
            calls.DropLamp(member);
            return false;
        }
        double distance = (PlayerMotor.WorldPosition(self, train) - train.Frames[car].ToWorld(Double3.Zero)).Length;
        return calls.ClaimLamp(car, member, distance, world.Tick);
    }

    /// <summary>Not going to a swarmed lamp.</summary>
    public void NoLamp() => calls.DropLamp(member);

    /// <summary>Crate hands to a stop's trouble in a car at most: enough to beat it, and the rest keep loading.</summary>
    public const int TroubleHands = 2;

    /// <summary>
    /// At a stop, trouble in a car (a fire, a loose load, Gnawers): in by its side door, as a crate goes in, from the
    /// ground, up its steps, open up and inside. The walker's own way in (a car's rear door, down from its roof or the one
    /// behind) has nothing to go by at the end of a cut rake, where the stop's cars stand, and the hands stood about on
    /// its roof for the whole of the loading (the 100-night rerun's frontier:7 Foundry). Null when that's not the way:
    /// between stops, in the car already (the walker works it from the aisle), or the car has no door this side.
    /// </summary>
    /// <param name="held">Note 496: or on the main short of the points, the train standing (the Fire Flies' lamp: they come
    /// only to a stopped train, and a hand on the roofs from the cab was five cars short of it when the car caught).</param>
    public PlayerIntent? IntoTrouble(in PlayerState self, World world, int car, bool held = false)
    {
        var train = world.Train;
        if (_plan is not { } p || car <= 0 || car >= train.Frames.Count || !self.Alive
            || !(held && Math.Abs(train.Dynamics.Velocity) < 0.05) && (!_reachedEnd || !p.AtTheEnd(train)))
            return null;
        // Note 609: at the crane's controls, let go of them first (the press that takes them lets them go). Its stick drives
        // the crane while it has them: frontier:7 seed 4's operator, off to a car fire, walked the gantry to the end of its
        // rails and stood there at the controls for two minutes.
        if (self.Has(PlayerFlags.Operating) || _atControls)
        {
            _atControls = false;
            Doing = "off the crane for the trouble";
            return self.Has(PlayerFlags.Operating) ? new PlayerIntent { Actions = PlayerActions.Seat } : new PlayerIntent();
        }
        int side = p.Site.Side;
        var frame = train.Frames[car];
        var shape = frame.Shape;
        var layout = train.Dynamics.Tuning.Geometry.Interior;
        if (layout is null || self.Surface is Surface.Air)
            return null;
        // Note 496: a car with no side door this side (the guard van, a crew car) for the Fire Flies' lamp, the train held:
        // along the ballast to its own side ladder and up, and in from its roof the walker's way (by its end door). Along
        // the roofs from the cab, the gunner was three cars short of the guard van when the flies set it alight.
        if (SideDoor(shape, side) is not { } door)
        {
            if (!held || self.Parent == car && self.Surface == Surface.Roof)
                return null;
            Doing = "to the trouble";
            if (self.Surface == Surface.Ladder)
                return self.Parent == car ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : null;
            if (self.Parent != PlayerState.World)
                return self.Surface == Surface.Roof ? null : GetDown(self, train, side);
            return ToARoofLadder(self, train, car);
        }
        if (self.Surface is Surface.Ladder)
            return null;
        bool open = train.Vehicles[car].DoorOpen(door);
        double w = shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        bool onThisCar = self.Parent == car && self.Surface == Surface.Deck;
        if (onThisCar && Math.Abs(self.Position.X) < w - layout.WallThickness)
            return null;
        if (onThisCar)
        {
            if (!open)
            {
                if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, landing, facingIn, "up to the trouble");
                Doing = "opening up for the trouble";
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            }
            return Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "in to the trouble");
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, side);
        var (_, across) = TrackCoords(train.Line, p.Spur.Index, self.Position, self.LineHint);
        if (Math.Sign(across) != side && Math.Abs(across) > 1.2)
        {
            Doing = "over the train";
            return null;
        }
        var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
        double outward = side * local.X - w;
        bool inLane = outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3;
        if (inLane)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps to the trouble");
        Doing = "to the trouble";
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        var (walk, atFoot) = WalkTo(self, train.Line, p.Spur.Index, foot, null);
        return atFoot ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "up the steps to the trouble") : walk;
    }

    /// <summary>This tick's intent for its part in a stop; null when there's nothing for it to do (walk as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, World world)
    {
        // Someone has to shunt, and set the switches back (GDD §17): with the shunter dead, a hand takes it over, between
        // stops (never mid-part).
        if (job is not (StopJob.None or StopJob.Driver or StopJob.Shunter) && self.Alive && _plan is null && _coal is null && _switch is null
            && calls.StandIn(member, StopJob.Shunter))
            job = StopJob.Shunter;
        // Note 628: a switchyard's pick-up with the shunter off warming (frontier:7 seed 2: shut in car 3 the whole of the
        // 240 s the driver held for the points), before the train's gone in: the switch hand (note 609) takes the shunter's
        // part, and the shunter its part once warm. Others stay aboard at a pick-up, so the switch hand is free for it.
        if (job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates && self.Alive && _plan is { PickUp: true } && !_wentIn
            && calls.ShuntTaken is null && calls.ShunterWarming && calls.SwitchHand(member))
        {
            calls.TakeShunt(member, job);
            job = StopJob.Shunter;
        }
        else if (job == StopJob.Shunter && calls.ShuntTaken is { } taken && taken.Member != member)
        {
            job = taken.Gave;
            calls.Traded();
        }
        // Note 628: two with the shunter's part, between stops, as when one's back from the dead (out of a Holdout) with its
        // stand-in still on it: frontier:7 seed 11's two went to the points by turns and set them back and forth for the 180 s
        // of the clear. The one with the higher number gives it up, for a part nobody has.
        else if (job == StopJob.Shunter && self.Alive && _plan is null && _coal is null && _switch is null && calls.ShuntTaken is null
            && calls.ShunterBefore(member))
            job = calls.FreePart();
        // And the winch pair, where one's died or gone to shunt: a crate hand takes the part (never mid-part).
        foreach (var winch in new[] { StopJob.Winch0, StopJob.Winch1 })
            if (job == StopJob.Crates && self.Alive && _plan is null && _coal is null && _switch is null && calls.StandIn(member, winch))
                job = winch;
        calls.Say(member, job, self);
        if (PlayerId is { } id)
        {
            calls.Knows(member);
            calls.Standing(id, PlayerMotor.WorldPosition(self, world.Train));
        }
        Doing = "";
        if (job == StopJob.None || !self.Alive || world.Run is null)
            return job == StopJob.None ? LeftOnTheBallast(self, world) : null;
        var train = world.Train;
        if (job == StopJob.Shunter && _plan is null && _coal is null && SettingBack(self, world, out var setting))
            return setting;
        if (job == StopJob.Shunter && _plan is null && Coaling(self, world, out var coaling))
            return coaling;
        if (_plan is null)
        {
            // The train's standing short of the points for a stop (the driver only makes the ones the crew can work).
            if (StopPlan.Ahead(world, train.Dynamics.Distance - 10, _done, calls) is not { } plan || !plan.StandingAt(train))
                return job == StopJob.Driver || _coal is not null || _switch is not null ? null : LeftOnTheBallast(self, world);
            _plan = plan;
            _wentIn = _reachedEnd = false;
        }
        var p = _plan;
        if (train.Dynamics.Path == p.Spur.Index && train.Dynamics.Distance > p.Spur.Toe)
            _wentIn = true;
        _reachedEnd |= p.AtTheEnd(train);
        // The train's back together and away up the main line: that stop's over, done or not.
        if (train.OnMain && p.NearRake(train) is null && train.Dynamics.Distance > p.Hold + 20 && !train.Diverging(p.Spur.Index))
        {
            _done.Add(p.Key);
            _plan = null;
            return null;
        }
        // A pick-up (note 187) is the shunter's and the driver's: everyone else stays aboard, and one on the ballast already
        // comes back aboard (note 628: frontier:6 seed 3's gunner, down from the yard's own track, stood beside the train
        // through the pick-up, and the driver waited out the aboard and the clear give-ups for it, 240 s).
        if (p.PickUp && job != StopJob.Shunter)
            return job == StopJob.Driver ? null : LeftOnTheBallast(self, world);
        // Note 261: the shunter's free once the train's in: a place in the pair nobody else has, or the crates with too few
        // carrying them. Back to the cab once there's none left (anything in its arms put down first).
        _lent = job == StopJob.Shunter && !p.PickUp && _reachedEnd && p.AtTheEnd(train) && !calls.Leaving
            ? calls.Lend(StopJob.Shunter, j => p.PairWork(world, j), p.CrateWork(world, calls.HeavyHands)) : StopJob.None;
        if (job == StopJob.Shunter && _lent == StopJob.None && self.Has(PlayerFlags.Heavy))
            return Press();
        // Mid-air, nothing to do; on a ladder or inside a car, the walker knows the way out (unless it's in there to load).
        var part = Part(p, world);
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        // The Choir gathering: on a car's floor, shut into it (note 439); on the ground, behind a house's door, or aboard (the
        // walker's way, note 413); before any of the work.
        if (job != StopJob.Driver && (_carHiding != CarHiding.Off || self.Surface == Surface.Deck && self.Parent > 0) && ShutIn(self, world, p) is var shut
            && (shut is not null || _carHiding != CarHiding.Off))
            return shut;
        if (job != StopJob.Driver && (_hiding != Hiding.Off || self.Surface == Surface.Ground) && Shelter(self, world, p) is var hiding
            && (hiding is not null || _hiding != Hiding.Off))
            return hiding;
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0 && !(part == StopJob.Crates && _reachedEnd))
            return null;
        // Note 609: the points, while the shunter's at the cut (a crew's second pair of hands; StopDriver's Held leg was the
        // shunter's walk back to the cut, then forward past the engine to the stand, then to the cab: 68 to 115 s).
        if (ThrowsForTheShunter(world, p))
            return Throw(self, train, p.Spur);
        // Note 639: the yard's power down (level-design D.1: "the cranes wait on a walk to the powerhouse and a restart"), the
        // crane's operator walks to the powerhouse and holds the restart till it's live, before the crane: dead, it doesn't
        // move at all, and at low power at half speed. Nobody did, and a dead yard's crane never lifted a casting.
        if (Pair == StopJob.Winch0 && !WinchOnly && !calls.Leaving && p.Site.Crane is { } powered && CraneTarget(powered, train) is not null
            && PowerUp(self, world, p) is { } restart)
            return restart;
        // The crane pinged (note 739 part two; the director, 14 Oct: "If I point at the crane"): the two sent to it work it, the
        // first at the controls (the power restarted first, as the pair's operator does) and the second rigging, whatever
        // their own parts and however few the pair; whoever has those parts otherwise stands aside for them.
        if (OrderedCrane(world, p) is ({ } sent, var ordered))
            return ordered == StopJob.Winch0 ? PowerUp(self, world, p) ?? Operate(self, world, p, sent) : RigCasting(self, world, p, sent);
        // The gantry crane (T54, spec D.2): the pair work it first, one at the controls and one rigging, while the cars under
        // the gantry still have room (the crates and the sleds would fill them); then the winch.
        if (Pair is StopJob.Winch0 or StopJob.Winch1 && !WinchOnly && !calls.CranePartOrdered(Pair) && p.Site.Crane is { } crane
            && (calls.PairNow || crane.Hooked is not null) && (CraneTarget(crane, train) is not null || crane.Hooked is not null))
            return Pair == StopJob.Winch0 ? Operate(self, world, p, crane) : RigCasting(self, world, p, crane);
        if (_atControls || self.Has(PlayerFlags.Operating))
        {
            // Done at the crane: the press that lets go of the controls (and so steps down) before anything else.
            _atControls = false;
            return self.Has(PlayerFlags.Operating) ? new PlayerIntent { Actions = PlayerActions.Seat } : new PlayerIntent();
        }
        // GDD §18's set pieces (note 185): the pair drive the herd up the ramp; the first of them minds the hose.
        if (Pair is StopJob.Winch0 or StopJob.Winch1 && !WinchOnly && calls.PairNow && !calls.Leaving && p.HerdLeft(world))
            return Herd(self, world, p);
        if (Pair == StopJob.Winch0 && !WinchOnly && !calls.Leaving && p.HoseLeft(world))
            return Hose(self, world, p);
        return part switch
        {
            StopJob.Shunter => Shunt(self, world, p),
            StopJob.Crates => Carry(self, world, p),
            _ => Crank(self, world, p),
        };
    }

    /// <summary>
    /// Note 639: to the yard's powerhouse (its door, or an open one's switchboard inside, note 509) by the way round the walls,
    /// and Use held there till the power's live (facilities.json <c>power.restartSeconds</c>; loud while it's held). Null once
    /// it's live, or with no powerhouse to restart it at. Off a car first, if it's on one.
    /// </summary>
    PlayerIntent? PowerUp(in PlayerState self, World world, StopPlan p)
    {
        if (world.Run?.CurrentSite is not { Power: not Stops.PowerState.Live, Powerhouse: { } door } site || site != p.Site)
            return null;
        var train = world.Train;
        if (_atControls || self.Has(PlayerFlags.Operating))
        {
            _atControls = false;
            return self.Has(PlayerFlags.Operating) ? new PlayerIntent { Actions = PlayerActions.Seat } : new PlayerIntent();
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, door, self.LineHint));
        double reach = world.Run.PowerTuning.Reach;
        var here = PlayerMotor.WorldPosition(self, train);
        if (((here - door) with { Y = 0 }).Length > reach - 0.5)
        {
            Doing = "to the powerhouse";
            return OnFoot(self, train, p.Spur.Index, door, null).Step;
        }
        Doing = "restarting the power";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>The part a spare hand (the shunter, the driver) has taken at this loading (note 261), or None.</summary>
    StopJob _lent;
    /// <summary>A spare hand (the shunter, the driver) on the winch while the pair work the crane (note 609, <see cref="CrewCalls.SparesOnWinch"/>).</summary>
    bool WinchOnly => job is StopJob.Shunter or StopJob.Driver && _lent is StopJob.Winch0 or StopJob.Winch1 && calls.SparesOnWinch;
    /// <summary>Its place in the pair (spec D.2's winch, crane and ramp): its own part, or one it's taken in a missing hand's place.</summary>
    StopJob Pair => job is StopJob.Winch0 or StopJob.Winch1 ? job : _lent is StopJob.Winch0 or StopJob.Winch1 ? _lent : StopJob.None;

    /// <summary>
    /// The driver down at the loading (note 261, <see cref="StopDriver.DriverPart"/>): this tick's intent for the part it's
    /// taken, the train standing at the end of the spur. Null when there's nothing more it can do there (it goes back up).
    /// </summary>
    public PlayerIntent? Lend(in PlayerState self, World world, StopPlan plan, StopJob part)
    {
        _plan = plan;
        _wentIn = _reachedEnd = true;
        _lent = part;
        Doing = "";
        var train = world.Train;
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (Pair is StopJob.Winch0 or StopJob.Winch1 && !WinchOnly && !calls.CranePartOrdered(Pair) && plan.Site.Crane is { } crane
            && (CraneTarget(crane, train) is not null || crane.Hooked is not null))
            return Pair == StopJob.Winch0 ? Operate(self, world, plan, crane) : RigCasting(self, world, plan, crane);
        if (_atControls || self.Has(PlayerFlags.Operating))
        {
            _atControls = false;
            return self.Has(PlayerFlags.Operating) ? new PlayerIntent { Actions = PlayerActions.Seat } : new PlayerIntent();
        }
        if (Pair is StopJob.Winch0 or StopJob.Winch1 && !WinchOnly && plan.HerdLeft(world))
            return Herd(self, world, plan);
        if (Pair == StopJob.Winch0 && !WinchOnly && plan.HoseLeft(world))
            return Hose(self, world, plan);
        // On a car it isn't working from (a crate put down, and the next for another car): off it the way it came, as a walker
        // would find its own way.
        return Part(plan, world) switch
        {
            StopJob.Crates => Carry(self, world, plan),
            StopJob.Winch0 or StopJob.Winch1 => Crank(self, world, plan),
            _ => null,
        } ?? OffTheCar(self, world, plan);
    }

    /// <summary>Lets go of what's in its arms (the press that put it down), for the driver going back to the cab (note 261).</summary>
    public PlayerIntent LetGo() => Press();

    /// <summary>On a car's landing or inside it at a stop: out by its side door and down its steps. Null when it isn't.</summary>
    /// <para>Note 633 (the director, the inbox, 12 Oct: a lit car shut up keeps the Climbers out): the door shut behind it, but a
    /// cargo car's on the site's side: the crates and the village's finds go in by it, and the crate hands shut those before the
    /// train leaves (<see cref="StopDriver.Loaded"/>, T50). Shut behind the last crate, the driver left before the village.</para>
    PlayerIntent? OffTheCar(in PlayerState self, World world, StopPlan p) =>
        OffTheCar(self, world.Train, p.Site.Side, shutBehind: !(self.Parent > 0 && world.Train.Vehicles[self.Parent].Kind == VehicleKind.Cargo));

    /// <summary>
    /// On a car's landing or inside it: out by its side door on one side (+1 right) and down its steps; with
    /// <paramref name="shutBehind"/>, the door shut behind it on a lit lamp (not at a loading, where the crates go in by it).
    /// </summary>
    PlayerIntent? OffTheCar(in PlayerState self, TrainOnLine train, int side, bool shutBehind = false)
    {
        if (self.Surface != Surface.Deck || self.Parent <= 0 || train.Dynamics.Tuning.Geometry.Interior is not { } layout)
            return null;
        double w = train.Frames[self.Parent].Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        var car = train.Vehicles[self.Parent];
        int? door = SideDoor(train.Frames[self.Parent].Shape, side);
        bool open = door is not { } d || car.DoorOpen(d);
        if (Math.Abs(self.Position.X) < w - layout.WallThickness)
        {
            // Note 619: its door shut, opened from inside first. Sent down at local:486's Knotter stand (slot seed 9) from a car
            // shut up for the cold, two walkers walked into the shut door for 180 s, the warm-up's reopening it drowned out.
            if (!open)
            {
                var inside = new Double3(side * (w - layout.WallThickness - 0.45), layout.FloorHeight, 0);
                if (!Near(self, inside, 0.25) || !Aligned(self, facingIn + Math.PI))
                    return Walk(self, inside, facingIn + Math.PI, "to the door");
                Doing = "opening the door";
                return new PlayerIntent { Buttons = PlayerButtons.Use }; // held: the press's edge works the door, once
            }
            return Walk(self, landing, facingIn + Math.PI, "out of the car");
        }
        // Note 619: out on the landing of a car whose lamp is lit, the door shut behind it (the Fire Flies' rule, note 286: a
        // lit lamp draws them, and a car shut up keeps them off it). Left open, it was the way in to every car they set alight.
        if (shutBehind && open && door is not null && car.LampLit && Math.Abs(self.Position.Z) < sd)
        {
            if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                return Walk(self, landing, facingIn, "shutting the door behind");
            Doing = "shutting the door behind";
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        }
        return Walk(self, landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.5 }, Math.PI, "down the steps");
    }

    /// <summary>
    /// Done with its part at the loading: aboard, for a hand whose stop that is. The shunter still has the switch to set back
    /// and the driver the train to drive (note 261): off the car it was shutting up, and back to the cab.
    /// </summary>
    PlayerIntent? Done(in PlayerState self, World world, StopPlan p)
    {
        // Note 867: a two-person jackpot in reach first (not from inside the errand's own carry).
        if (job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates && !_inErrand)
        {
            _inErrand = true;
            try
            {
                if (JackpotErrand(self, world, p) is { } errand)
                    return errand;
            }
            finally
            {
                _inErrand = false;
            }
        }
        if (job is not (StopJob.Shunter or StopJob.Driver))
            return Aboard(self, p);
        return OffTheCar(self, world, p) ?? Ride(self, world.Train, p);
    }

    bool _atControls, _fired;
    (int Casting, double Bridge, double Trolley)? _castingAt;
    (int Car, double Bridge, double Trolley)? _carAt;

    /// <summary>
    /// The crane this hand's been sent to by a ping (note 739 part two), at this stop, with its loading still to do, and its
    /// part there (the controls or the rigging); null with none.
    /// </summary>
    (Crane Crane, StopJob Part)? OrderedCrane(World world, StopPlan p)
    {
        if (calls.Leaving || PlayerId is not { } me || calls.OrderOf(me) is not { Target: var i } || world.Run?.CurrentSite != p.Site || i < 0 || i >= p.Site.Cranes.Count)
            return null;
        var part = calls.CranePart(me);
        if (part is not (StopJob.Winch0 or StopJob.Winch1))
            return null;
        var crane = p.Site.Cranes[i];
        return crane.Hooked is not null || CraneTarget(crane, world.Train) is not null ? (crane, part) : null;
    }

    /// <summary>
    /// What's left for the crane: the next casting still stacked (in order, so the operator and the rigger agree on it
    /// without seeing each other: spec D.3's "blind instruction", called as the plan) and a cargo car with room that the
    /// hook reaches over. Null when there's nothing to load or nowhere to put it.
    /// </summary>
    public static (int Casting, int Car)? CraneTarget(Crane crane, TrainOnLine train)
    {
        int casting = Array.FindIndex(crane.Castings, c => c.State == CastingState.Stacked);
        if (casting < 0 && crane.Hooked is null)
            return null;
        return RoofFor(crane, train) is { } roof ? (casting, roof.Car) : null;
    }

    /// <summary>
    /// A cargo car with room and where on its roof the hook reaches (anywhere along it, clear of its ends: the gantry may
    /// only span part of a car), the nearest car first: null for none in reach.
    /// </summary>
    static (int Car, double Bridge, double Trolley)? RoofFor(Crane crane, TrainOnLine train) => RoofFor(crane, train, StopPlan.WithRoom(train));

    /// <summary>Note 920: whether the hook reaches anywhere along <paramref name="car"/>'s roof (the packing plan's casting cars).</summary>
    public static bool Reaches(Crane crane, TrainOnLine train, int car) => RoofFor(crane, train, train.Vehicles.Where(v => v.Id == car)) is not null;

    static (int Car, double Bridge, double Trolley)? RoofFor(Crane crane, TrainOnLine train, IEnumerable<Vehicle> cars)
    {
        (int Car, double Bridge, double Trolley, double Off)? best = null;
        foreach (var v in cars)
        {
            var frame = train.Frames[v.Id];
            double half = frame.Shape.HalfLength - 1;
            for (double z = -half; z <= half + 1e-9; z += 1)
            {
                var (b, x, off) = crane.Over(frame.ToWorld(new Double3(0, frame.Shape.RoofHeight, z)));
                if (off < 0.3 && (best is null || Math.Abs(z) < Math.Abs(best.Value.Off)))
                    best = (v.Id, b, x, z);
            }
            if (best is { } found && found.Car == v.Id)
                break;
        }
        return best is { } w ? (w.Car, w.Bridge, w.Trolley) : null;
    }

    /// <summary>
    /// At the crane's controls (spec D.2): down on the stand's side, to the stand, and the press there that takes the controls
    /// (Use's, sent as <see cref="PlayerActions.Seat"/>, only while it hasn't got them: a second would let them go); the stick
    /// drives the crane. Hook down over the next casting and held there while it's rigged; up high, over a car with room, down onto
    /// its roof, and let go (only ever when it's set down: a load let go of high kills).
    /// </summary>
    PlayerIntent? Operate(in PlayerState self, World world, StopPlan p, Crane c)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, c.Controls, self.LineHint));
        if (!_atControls)
        {
            // By the way round the walls from further off (note 639: back from the powerhouse, the track-side walk stuck
            // against its walls for a minute, and the Gaunt had the operator there).
            var (step, there) = OnFoot(self, train, p.Spur.Index, c.Controls, null);
            if (!there && ((self.Position - c.Controls) with { Y = 0 }).Length > c.Tuning.ControlsReach - 0.4)
            {
                Doing = "to the crane";
                return step;
            }
            _atControls = true;
        }
        Doing = "at the crane";
        var drive = new PlayerIntent { Actions = self.Has(PlayerFlags.Operating) ? PlayerActions.None : PlayerActions.Seat };
        var t = c.Tuning;
        if (c.Hooked is null)
        {
            _fired = false;
            _carAt = null;
            if (CraneTarget(c, train) is not { Casting: >= 0 } target)
                return drive;
            if (_castingAt is not { } at || at.Casting != target.Casting)
            {
                var (b, x, _) = c.Over(c.Castings[target.Casting].At);
                _castingAt = at = (target.Casting, b, x);
            }
            bool over = Steer(ref drive, c, at.Bridge, at.Trolley);
            // Traversing, the hook up out of the way; over the casting, down to where the rigger can reach it.
            if (!over && c.Hook < t.Height - 0.5)
                drive.Buttons |= PlayerButtons.Jump;
            else if (over && c.Hook > t.RigHeight - 0.8)
                drive.Buttons |= PlayerButtons.Brake;
            return drive;
        }
        _castingAt = null;
        if (_carAt is not { } car || train.Vehicles[car.Car].Load >= 1 - 1e-6)
        {
            if (RoofFor(c, train) is not { } roof)
                return drive;
            _carAt = car = roof;
        }
        // Up first, clear of the roofs; then across; then down onto it and let go once it's sitting on the roof.
        if (c.Hook < t.Height - 0.3 && !Near(c, car.Bridge, car.Trolley))
        {
            drive.Buttons |= PlayerButtons.Jump;
            return drive;
        }
        if (!Steer(ref drive, c, car.Bridge, car.Trolley))
            return drive;
        var (under, roofY) = c.Under(train);
        double above = c.HookedBase.Y - roofY;
        if (under == car.Car && above <= Math.Min(0.3, t.DropAbove - 0.1))
        {
            if (!_fired)
            {
                drive.Buttons |= PlayerButtons.Fire;
                _fired = true;
            }
            return drive;
        }
        drive.Buttons |= PlayerButtons.Brake;
        return drive;
    }

    /// <summary>The stick towards a bridge and trolley setting (a little ahead of time for the lag); true once it's there.</summary>
    static bool Steer(ref PlayerIntent drive, Crane c, double bridge, double trolley)
    {
        double db = bridge - c.Bridge, dx = trolley - c.Trolley;
        // The stick's as the cab sees it (note 845): what each axis wants, turned into it.
        var (right, forward) = c.StickFor(Math.Abs(db) < 0.04 ? 0 : Math.Clamp(db * 2.5, -1, 1), Math.Abs(dx) < 0.04 ? 0 : Math.Clamp(dx * 2.5, -1, 1));
        drive.MoveX = (float)right;
        drive.MoveZ = (float)forward;
        return Near(c, bridge, trolley);
    }

    static bool Near(Crane c, double bridge, double trolley) => Math.Abs(bridge - c.Bridge) < 0.08 && Math.Abs(trolley - c.Trolley) < 0.08;

    /// <summary>
    /// Rigging on the ground (spec D.2): down on the castings' side, beside the next one, and once the hook's come down
    /// over it, holding Use until it's hooked on. Then back out of the way while it goes up and over.
    /// </summary>
    PlayerIntent? RigCasting(in PlayerState self, World world, StopPlan p, Crane c)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        int next = Array.FindIndex(c.Castings, k => k.State == CastingState.Stacked);
        var at = next >= 0 ? c.Castings[next].At : c.Castings[0].At;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, at, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, at, self.LineHint);
        if (c.Hooked is not null || next < 0)
        {
            // Stand off beyond the stack while it's lifted away.
            Doing = "standing clear";
            return WalkTo(self, train.Line, p.Spur.Index, TrackPoint(train.Line, p.Spur.Index, along, across + Math.Sign(across) * 2.5), null).Step;
        }
        var stand = TrackPoint(train.Line, p.Spur.Index, along + 1.0, across);
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the castings";
            return step;
        }
        if (c.Riggable(PlayerMotor.WorldPosition(self, train)) is null)
        {
            Doing = "calling the hook down";
            return new PlayerIntent();
        }
        Doing = "rigging";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>Which side of the spur a point on the ground is (+1 right looking up it).</summary>
    // Note 533: by the track the engine's on. By the spur's, with the train stood on the main past its toe, a hand on the
    // ballast beside the engine read as on the other side, and ran at the cab's far door into the engine all night.
    static int SideOf(TrainOnLine train, StopPlan p, Double3 world, double hint) =>
        TrackCoords(train.Line, train.Dynamics.Path, world, hint).Across >= 0 ? 1 : -1;

    /// <summary>
    /// The part this stop: the winch pair carry crates where there's no winch, or once its sleds are in; note 261: and where
    /// the pair can't be made up (one of them, and nobody else for the other handle). A spare hand that's taken the crates
    /// carries them.
    /// </summary>
    StopJob Part(StopPlan p, World world) => Pair is StopJob.Winch0 or StopJob.Winch1
        ? (!p.Site.Has(ModuleKind.Winch) || p.Site.SledsLeft == 0 || !calls.PairNow || world.Run is { } r && !r.SledHasRoom(world.Train, p.Site))
            && (p.Site.Has(ModuleKind.Crates) || p.Site.Has(ModuleKind.Wreck)) ? StopJob.Crates : Pair
        : _lent == StopJob.Crates ? StopJob.Crates : job;

    /// <summary>
    /// Note 609: this hand throws the spur's points while the shunter's cutting the train (<see cref="CrewCalls.SwitchHand"/>):
    /// the train standing short of them for a stop with a cut, the shunter alive, and the points not over yet.
    /// </summary>
    bool ThrowsForTheShunter(World world, StopPlan p) =>
        !_wentIn && !calls.Leaving && !p.PickUp && p.CutBehind >= 0 && job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates
        && !world.Train.Diverging(p.Spur.Index) && calls.Has(StopJob.Shunter) && calls.SwitchHand(member);

    /// <summary>
    /// Note 609: seconds the shunter, its cut made, waits aboard for the hand at the points (<see cref="ThrowsForTheShunter"/>)
    /// before it goes to them itself: the hand's a few metres from them and has them over before the cut's made, unless
    /// something's taken it elsewhere (the Fire Flies' lamp).
    /// </summary>
    const double SwitchHandWait = 20;
    uint _cutAt;

    PlayerIntent? Shunt(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        bool set = train.Diverging(p.Spur.Index);
        if (!_wentIn)
        {
            // The driver's called the stop off before the train went in (note 550): nothing thrown, and the points put back
            // if they'd gone over as it did.
            if (calls.Leaving)
                return set ? Throw(self, train, p.Spur) : Ride(self, train, p);
            // Cut first (note 609: whoever has the points over meanwhile).
            if (p.CutBehind >= 0 && p.NearRake(train) is null)
            {
                _cutAt = world.Tick;
                return Cut(self, train, p);
            }
            // Note 609: cut, with a hand at the points: up onto the engine's rake where it is (its end car's ladder, beside
            // the cut) and in on it, as a crew's shunter rides the end of the cut in. To the stand itself only once the hand's
            // been given time; never to the cab, which was the walk back past the whole rake again. (A pick-up's shunter, its cut
            // a car behind the engine, is at the points and the cab already.)
            if (!p.PickUp && p.CutBehind >= 0 && (set || calls.HasSwitchHand && world.Tick - _cutAt <= SwitchHandWait * SimConstants.TickRate))
            {
                if (train.Dynamics.Consist.IndexOf(self.Parent) > 0)
                {
                    Doing = "riding in";
                    return new PlayerIntent();
                }
                if (self.Parent == PlayerState.World && self.Surface == Surface.Ground)
                {
                    Doing = "aboard at the cut";
                    return ToARoofLadder(self, train);
                }
            }
            return set ? Ride(self, train, p) : Throw(self, train, p.Spur);
        }
        // In the cab while the empties are down the spur, until the whole train's back together short of the points. At the
        // grain elevator, down on the spout's lever first while there's a car to fill under it (GDD §18; note 185).
        // Note 609: or the cars left waiting are lost (run away down the grade), as the driver's called it: frontier:3 seed 1's
        // shunter sat in the cab beside the points for 300 s for a cut rolling off down the main line, the driver waiting on it.
        bool back = (p.NearRake(train) is null || calls.CutLost) && train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05
            && train.Dynamics.Distance is var at && at <= p.Hold + 3 && at >= p.Hold - StopDriver.ChaseBackShort;
        // The conveyor's drive house and belt first (note 400), while there's a car to fill that comes under its head.
        if (!back && _reachedEnd && p.ConveyorTarget(world) is not null && !calls.Leaving)
            return Convey(self, world, p);
        if (!back && _reachedEnd && p.SpoutTarget(world) is not null && !calls.Leaving)
            return Spout(self, world, p);
        // Or on the steam lift's (note 368), while there's a car to fill under its chute in the engine's reach.
        if (!back && _reachedEnd && p.LiftTarget(world) is not null && !calls.Leaving)
            return Lift(self, world, p);
        // A car off its rails at the tipple (note 423) holds the train fast, leaving or not: the wrench to it first. Then the
        // tipple's lever, while there's a car to fill that comes into its cradle (after the lift's, as the driver works them).
        if (_reachedEnd && world.Run?.OffRailsAt(train, p.Site) is { } off)
            return Rerail(self, world, p, off);
        if (!back && _reachedEnd && p.TippleTarget(world) is not null && !calls.Leaving)
            return Tipple(self, world, p);
        // Note 628: at a pick-up, aboard by the nearest ladder of the engine's rake, not the walk to the cab: frontier:7 seed 3's
        // shunter, on the ballast in the siding as the engine coupled up, walked for the cab steps for the whole 120 s of the
        // aboard wait.
        if (!back)
            return p.PickUp && self.Parent == PlayerState.World ? LeftOnTheBallast(self, world) ?? Ride(self, train, p) : Ride(self, train, p);
        return set ? Throw(self, train, p.Spur) : Aboard(self, p);
    }

    PlayerIntent? Crank(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var engine = train.Dynamics;
        bool atEnd = p.AtTheEnd(train);
        if (!_reachedEnd)
            return Ride(self, train, p);
        // Nothing to haul, nowhere for a sled to go (T66), or the driver's called all aboard (T76): aboard. And the pair, done
        // at the crane, with the spare hands on the winch already (note 609).
        if (!atEnd || p.Site.SledsLeft == 0 || calls.Leaving || world.Run is { } r && !r.SledHasRoom(train, p.Site)
            || calls.SparesOnWinch && !WinchOnly)
            return Done(self, world, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, p.Spur.Side);
        // At its handle, on the track side of it, holding on. It only turns with both handles held (spec D.2).
        var handle = p.Site.Handles[Pair == StopJob.Winch0 ? 0 : 1];
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, handle, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) - 0.4));
        var (step, there) = OnFoot(self, train, p.Spur.Index, stand, null);
        if (!there)
        {
            Doing = "to the winch";
            return step;
        }
        Doing = "cranking";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>
    /// On the grain elevator's lever (GDD §18): down on the lever's side, beside it, and holding it while a car with room
    /// stands under the spout, letting go as it fills (the overflow strains the car) or once it's moving.
    /// </summary>
    PlayerIntent? Spout(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.SpoutLever, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, site.SpoutLever, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) + 0.5));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the spout";
            return step;
        }
        // Let go the tick it's full: what comes down meanwhile is a tick's pour, a fraction of a percent of a car-load.
        var car = world.Run?.CarUnderSpout(train, site);
        bool fill = car is not null && car.Load < 1 - 1e-6 && Math.Abs(train.Dynamics.Velocity) < 0.05;
        Doing = fill ? "pouring" : "at the spout";
        return fill ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// The conveyor line (note 400; spec D.2 "1 + 1 roaming", done by one): stopped, to its drive house and holding the starter
    /// till it runs; jammed, to the ground beside the jam and holding Use there till it's clear (a jam comes first: a stalled
    /// belt that's still jammed only jams again); running, beside the middle of its low run, where every jam is a short walk.
    /// </summary>
    PlayerIntent? Convey(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        // Note 639: the belt doesn't start on a dead yard's power, and runs at half speed on low: the powerhouse first.
        if (PowerUp(self, world, p) is { } restart)
            return restart;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.ConveyorStarter, self.LineHint));
        var (goal, beyond, going, doing, use) = site.Jam >= 0 ? (site.JamAt, -0.8, "to the jam", "clearing the jam", true)
            : !site.Running ? (site.ConveyorStarter, 0.5, "to the drive house", "starting the belt", true)
            : (Double3.Lerp(site.ConveyorTail, site.ConveyorKnee, 0.5), -1.0, "along the belt", "minding the belt", false);
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, goal, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) + beyond));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = going;
            return step;
        }
        Doing = doing;
        return use ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// On the steam lift's lever (note 368): as on the spout's, down on its side, beside it, and holding it while a car with
    /// room stands still under the chute; the driver vents for it while it's held.
    /// </summary>
    PlayerIntent? Lift(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.LiftLever, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, site.LiftLever, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) + 0.5));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the lift";
            return step;
        }
        var car = world.Run?.CarUnderChute(train, site);
        bool fill = car is not null && car.Load < 1 - 1e-6 && Math.Abs(train.Dynamics.Velocity) < 0.05;
        Doing = fill ? "winding" : "at the lift";
        return fill ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// On the tipple's lever (note 423; spec D.2 "1 crew"): down on its side, beside it, and holding it while a car with room
    /// stands still and true in the cradle (never on one stood off its middle: that's the bad clamp that derails it), and on
    /// through the roll once it's clamped.
    /// </summary>
    PlayerIntent? Tipple(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.TippleLever, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, site.TippleLever, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) + 0.5));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the tipple";
            return step;
        }
        var car = world.Run?.CarInCradle(train, site);
        bool tip = site.Clamped >= 0
            || car is { OffRails: false } && car.Load < 1 - 1e-6 && Math.Abs(train.Dynamics.Velocity) < 0.05 && world.Run!.StoodTrue(train, site, car);
        Doing = tip ? "tipping" : "at the tipple";
        return tip ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// A car off its rails at the tipple (note 423): down beside its middle, on the site's side, the wrench in hand and Use
    /// held till it's back on.
    /// </summary>
    PlayerIntent? Rerail(in PlayerState self, World world, StopPlan p, Vehicle car)
    {
        var train = world.Train;
        var middle = train.Frames[car.Id].Origin;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, p.Site.TippleLever, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, middle, self.LineHint);
        var (_, side) = TrackCoords(train.Line, p.Spur.Index, p.Site.TippleLever, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, across + Math.Sign(side) * 1.6);
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.5)
        {
            Doing = "to the car off its rails";
            return step;
        }
        if (Repairs.ByWrench(train) && Repairs.WrenchKey(self) is var key and > 0)
            return new PlayerIntent { Select = key };
        Doing = "putting the car back on its rails";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>
    /// Driving the herd (spec D.2 livestock ramp, "2–3 crew"): down on the pen's side, into the pen (each of the pair at
    /// its own end of it), and holding Use there while there's head left and a car at the ramp.
    /// </summary>
    PlayerIntent? Herd(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        if (!_reachedEnd)
            return Ride(self, train, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.Pen, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, site.Pen, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along + (Pair == StopJob.Winch0 ? -1.5 : 1.5), across);
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.6)
        {
            Doing = "to the pen";
            return step;
        }
        Doing = "driving the herd";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    // The hose stand's state when this hand began holding Use at it: once it's changed, let go before holding again.
    int? _hoseFrom;

    /// <summary>
    /// The fluid gantry (spec D.2: "connect hoses, monitor pressure, disconnect cleanly"): down to the stand, hold Use to put
    /// the hose on the car by it, stand there minding it while it fills (the pressure climbs with nobody there), and hold
    /// Use again to take it off once the car's full.
    /// </summary>
    PlayerIntent? Hose(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var site = p.Site;
        if (!_reachedEnd)
            return Ride(self, train, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, site.HoseStand, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, site.HoseStand, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) + 0.6));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the hose";
            _hoseFrom = null;
            return step;
        }
        bool full = site.HoseCar >= 0 && site.HoseCar < train.Vehicles.Count && train.Vehicles[site.HoseCar].Load >= 1 - 1e-6;
        bool want = site.HoseCar < 0 || full;
        if (!want || _hoseFrom is { } from && from != site.HoseCar)
        {
            _hoseFrom = null;
            Doing = "minding the hose";
            return new PlayerIntent();
        }
        _hoseFrom ??= site.HoseCar;
        Doing = site.HoseCar < 0 ? "putting the hose on" : "taking the hose off";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    CoalPlan? _coal;
    readonly HashSet<int> _coaled = [];
    bool _poured;
    SwitchPlan? _switch;

    /// <summary>
    /// A switch set wrong ahead (true while there's one to see to): once the train's standing short of its points, down to
    /// the stand and set it back for the main line (App. A.7: "verify every switch on the ground"), then aboard.
    /// </summary>
    bool SettingBack(in PlayerState self, World world, out PlayerIntent? intent)
    {
        intent = null;
        var train = world.Train;
        if (_switch is null)
        {
            if (SwitchPlan.Ahead(world) is not { } plan || !plan.StandingAt(train))
                return false;
            _switch = plan;
        }
        var branch = _switch.Branch;
        if (!train.Diverging(branch.Index))
        {
            // Set back: aboard (the walker climbs the nearest car), and done.
            if (self.Parent == PlayerState.World && self.Surface == Surface.Ground)
            {
                Doing = "boarding";
                return true;
            }
            _switch = null;
            return false;
        }
        if (!_switch.StandingAt(train))
            return false; // it's moving: wait aboard for it to stop
        if (self.Surface == Surface.Air)
        {
            intent = new PlayerIntent();
            return true;
        }
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0)
            return true; // the walker knows the way off a ladder or out of a car
        intent = Throw(self, train, branch);
        return true;
    }

    /// <summary>
    /// A coaling stop (true while there's one to work): down to the chute's lever beside the line, open it, shut it again a
    /// moment before the tender's full (the overflow damages the engine) or once the tower's empty, and back aboard.
    /// </summary>
    bool Coaling(in PlayerState self, World world, out PlayerIntent? intent)
    {
        intent = null;
        var train = world.Train;
        var run = world.Run!;
        if (_coal is null)
        {
            if (CoalPlan.Ahead(world, train.Dynamics.Distance - 10, _coaled, calls) is not { } plan || !plan.StandingAt(train))
                return false;
            _coal = plan;
            _poured = false;
        }
        var coal = _coal;
        // The train's gone on: that stop's over.
        if (Math.Abs(train.Dynamics.Distance - coal.Hold) > 5 || train.BoilerTuning is not { } bt)
        {
            _coaled.Add(coal.Facility);
            _coal = null;
            return false;
        }
        if (self.Surface == Surface.Air)
        {
            intent = new PlayerIntent();
            return true;
        }
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0)
            return true; // the walker knows the way off a ladder or out of a car
        bool open = run.ChuteOpen;
        _poured |= open;
        if (_poured && !open)
        {
            // Opened and shut again: aboard (the walker climbs the nearest car), and done.
            if (self.Parent == PlayerState.World)
            {
                Doing = "boarding";
                return true;
            }
            _coaled.Add(coal.Facility);
            _coal = null;
            return false;
        }
        bool enough = train.Boiler.Tender >= bt.TenderCapacity - run.Tuning.Chute.PourPerSecond * 1.5 || run.ChuteLeft(coal.Facility) <= 0;
        var (along, across) = TrackCoords(train.Line, RailLine.MainPath, coal.Lever, self.LineHint);
        if (self.Parent != PlayerState.World)
        {
            intent = GetDown(self, train, Math.Sign(across));
            return true;
        }
        // Just beyond the lever from the track, in reach of it. Up to it between the train and the tower (note 758: the
        // trestle's posts stand 3.5 m off the line, solid, the lever just past its end), and out to it once abreast.
        var (a, x) = TrackCoords(train.Line, RailLine.MainPath, self.Position, self.LineHint);
        bool abreast = Math.Abs(a - along) < 0.75 || Math.Abs(x) > Clear + 0.3 && Math.Abs(a - along) < 2;
        var stand = TrackPoint(train.Line, RailLine.MainPath, along, Math.Sign(across) * (abreast ? Math.Abs(across) + 0.5 : Clear));
        var (step, there) = WalkTo(self, train.Line, RailLine.MainPath, stand, null);
        if (!there)
        {
            Doing = "to the chute";
            intent = step;
            return true;
        }
        bool wantOpen = !enough;
        Doing = open == wantOpen ? "at the chute" : open ? "shutting the chute" : "opening the chute";
        intent = open == wantOpen ? new PlayerIntent() : new PlayerIntent { Buttons = PlayerButtons.Use };
        return true;
    }

    int _car = -1;
    /// <summary>Note 920: the heavy crate whose car was checked for a whole half (−1, none).</summary>
    int _heavyFor = -1;
    bool _pressed;
    /// <summary>A find from the village it put down on a car's landing to open the door (note 326), to take up again.</summary>
    int? _setDown;

    /// <summary>
    /// Note 867: the winch done, a two-person jackpot before going aboard: our end of one in our arms home with it (the crate
    /// hand's way, <see cref="Carry"/>), the other end of one someone's holding alone, or one lying within jackpotReach fetched
    /// (<see cref="Yard"/>, jackpots only). Null with none of those, or with the train leaving.
    /// </summary>
    PlayerIntent? JackpotErrand(in PlayerState self, World world, StopPlan p)
    {
        var errand = JackpotStep(self, world, p);
        // Out on it (as at the houses): the driver doesn't call the loading done while anyone is; back, it can. (Only ours to
        // take back: another errand's out is that errand's.)
        if (errand is not null || _jackpotOut)
            calls.Out(member, errand is not null);
        _jackpotOut = errand is not null;
        return errand;
    }

    bool _jackpotOut, _inErrand;

    PlayerIntent? JackpotStep(in PlayerState self, World world, StopPlan p)
    {
        var t = Crew(world);
        if (PlayerId is not { } me || t.JackpotReach <= 0 || !p.AtTheEnd(world.Train) || world.Run is not { } run || run.DawnIn < t.VillageDawnSpare)
            return null;
        if (world.Bodies.CarriedBy(me) is { } mine)
            return Run.Run.IsJackpot(mine) ? Carry(self, world, p) : null;
        var here = PlayerMotor.WorldPosition(self, world.Train);
        if (Wanting(world, p, here) is { } held && Run.Run.IsJackpot(held))
            return LendAHand(self, world, held);
        bool lying = world.Bodies.All.Any(b => Run.Run.IsJackpot(b) && b.Parent == PlayerState.World && b.Carrier < 0
            && (Flat(b.Centre) - Flat(here)).Length <= t.JackpotReach && !calls.SpotClaimed(CrateKey(b.Id), member)
            && !_cantReach.Contains(CrateKey(b.Id)));
        if (_yardCrate < 0 && !lying)
            return null;
        _jackpotsOnly = true;
        try
        {
            return Yard(self, world, p);
        }
        finally
        {
            _jackpotsOnly = false;
        }
    }

    /// <summary>Fetching for <see cref="JackpotErrand"/>: <see cref="NextCrate"/> takes jackpots alone, two hands or not.</summary>
    bool _jackpotsOnly;

    /// <summary>
    /// The front end of a jackpot up between two (note 867) walks at the back end's pace, as a heavy crate from the yard does
    /// (<see cref="HeavyPace"/>): at a walk to the steps the back end fell behind and it was down (too far apart), again and
    /// again at the foot of them.
    /// </summary>
    static PlayerIntent Paced(Physics.Body? mine, PlayerIntent step) =>
        mine is { Kind: Physics.BodyKind.Heavy, Second: >= 0 } ? step with { MoveZ = Math.Min(step.MoveZ, HeavyPace) } : step;

    /// <summary>Use for one tick, then not: picking up and putting down are on the press, not the hold.</summary>
    PlayerIntent Press()
    {
        _pressed = !_pressed;
        return _pressed ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// Crates off the ground and into the cars (spec D.2): pick one up at the stack, walk it to the car with the most room,
    /// up the steps to its side door (opened first: Use with your arms full puts the crate down), in, and put it down
    /// inside, where it's stowed once it lies still. Then back for the next, until they're in or there's no room.
    /// </summary>
    PlayerIntent? Carry(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        // Arms full: what's two-handed, or a find in the hands (note 897: it doesn't slow you, but it's still carried in and
        // set down aboard as a crate is).
        bool heavy = self.Has(PlayerFlags.Heavy)
            || PlayerId is { } holder && world.Bodies.CarriedBy(holder) is { Kind: Physics.BodyKind.Loot or Physics.BodyKind.Toy };
        int side = p.Site.Side;
        // The train's leaving: down with it if it's in our arms, and aboard.
        if (!p.AtTheEnd(train))
        {
            _car = -1;
            EndLampRun();
            return heavy ? Press() : Done(self, world, p);
        }
        // Heavy crates (T45): holding an end, wait for a hand; at the back end of one, follow it in; someone holding one
        // alone, go and take the other end.
        var mine = PlayerId is { } me ? world.Bodies.CarriedBy(me) : null;
        // Note 594's four slots: finds in them. A slot still free and another house worth searching, the one in hand is put
        // away (the number key of a slot without a find) and on to the next; else one out into the hand, and home with it.
        if (_setDown is null && SlotFinds(self, world, p, mine) is { } slots)
            return slots;
        if (mine is { Kind: Physics.BodyKind.Heavy })
        {
            if (!mine.Lifted)
            {
                Doing = "holding an end, waiting for a hand";
                calls.CarryingTo(member, -1);
                return new PlayerIntent();
            }
            if (mine.Second == PlayerId)
                return _yardHelp == mine.Id && mine.Parent == PlayerState.World ? BackEnd(self, world, mine) : Follow(self, world, mine);
        }
        else if (mine is null && PlayerId is not null && Wanting(world, p, PlayerMotor.WorldPosition(self, train)) is { } wanting)
            return LendAHand(self, world, wanting);
        // A find from the village we put down to open a door (note 326), gone (someone took it): nothing to go back for.
        if (_setDown is int put0 && world.Bodies.All.FirstOrDefault(b => b.Id == put0 && b.Carrier < 0) is null)
            _setDown = null;
        // A find pinged for us (note 739 part two, GDD §12 "Ping the bots"): out to it and up with it, before the yard's crates.
        if (!heavy && _setDown is null && mine is null && Pinged(self, world, p) is { } pinged)
            return pinged;
        // A find from the village in our arms (note 326): back round the houses to a car first.
        if (mine is { Kind: Physics.BodyKind.Loot } && FindHome(self, world, p) is { } home)
            return home;
        // The wreck yard's dark heaps (note 492): one hand brings a lamp out to them, and on to the next as each is found.
        if (!heavy && _setDown is null && WreckLamp(self, world, p, mine) is { } lit)
            return lit;
        // Nothing more to carry: the village's houses, if there's time and a share of hands for it (note 326); else the doors
        // shut behind us (an open car is a cold one), and aboard.
        // The site's crates in: the rest of the yard's, on foot (note 403); then the village.
        // (On with one we've set out for, whoever else has a crate in their arms: that's a crate to load too.)
        // Note 617: not the shunter lent to them while the pair are at their own work: it's the points' to set back once the
        // loading's done, and out at the yard's far crates it kept a frontier:7 Foundry's clear waiting 150 s for it.
        bool spare = job == StopJob.Shunter && calls.PairAtWork;
        // The town pinged for us (note 739 part two, GDD §12 "Ping the bots"): its houses searched, before the yard's crates.
        if (!heavy && _setDown is null && mine is null && world.Run is { } run && TownOrdered(run.StopOf(p.Site.Feature).Stop) && Village(self, world, p) is { } town)
            return town;
        if (!heavy && _setDown is null && !spare && (Fetching || !p.CratesToLoad(world, calls.HeavyHands)) && Yard(self, world, p) is { } fetch)
            return fetch;
        if (!heavy && _setDown is null && !spare && (!p.CratesToLoad(world, calls.HeavyHands) || calls.Leaving) && Village(self, world, p) is { } errand)
            return errand;
        if (!heavy && _setDown is null && (!p.CratesToLoad(world, calls.HeavyHands) || calls.Leaving))
        {
            calls.CarryingTo(member, -1);
            // The lamp we set down out at the wreck (note 492): up again first, and aboard with it.
            if (LampAboard(self, world, p) is { } lamp)
                return lamp;
            return OpenSideDoor(world, p, calls, member, self) is { } car ? ShutUp(self, world, p, car) : Done(self, world, p);
        }
        bool forHeavy = false;
        if (!heavy && _setDown is null || _car < 0)
        {
            _car = Roomiest(world, p, calls, member);
            // Note 920: no room the plan leaves the lights, but a heavy crate for two to fetch: it's the next one (else the
            // hands waited for room the heavies' halves held).
            if (_car < 0 && mine is null && PlayerId is not null && calls.HeavyHands >= 2 && HeavyCrate(world, p, self) is { } next
                && p.HeavyCar(world, -1, Physics.Bodies.WorldCentre(next, train)) is var half and >= 0)
                (_car, forHeavy) = (half, true);
        }
        // Note 920: a heavy crate up in our hands for a car without a whole half (the lights' car it was set out for) goes to
        // the nearest car that has one, or as ever if none has.
        // (Not one fetched from the yard: its end and its way home were worked out toward its car, note 440.)
        else if (mine is { Kind: Physics.BodyKind.Heavy, Lifted: true } && mine.Carrier == PlayerId && _heavyFor != mine.Id && !Fetching
            && !Run.Run.IsJackpot(mine))
        {
            _heavyFor = mine.Id;
            if (p.HeavyCar(world, _car, PlayerMotor.WorldPosition(self, train)) is var whole and >= 0)
                _car = whole;
        }
        if (mine is not { Kind: Physics.BodyKind.Heavy })
            _heavyFor = -1;
        calls.CarryingTo(member, heavy ? _car : -1);
        // Every car's room spoken for by crates on their way in: wait for them to land (then either there's room again,
        // or the crates are done and the doors want shutting). Going aboard now would leave this stop for good.
        if (_car < 0)
        {
            if (heavy)
                return Press();
            Doing = "waiting for room";
            return new PlayerIntent();
        }
        var frame = train.Frames[_car];
        var shape = frame.Shape;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        int door = SideDoor(shape, side) ?? 0;
        bool open = train.Vehicles[_car].DoorOpen(door);
        double w = shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2; // looking across the car from its door
        bool onThisCar = self.Parent == _car && self.Surface == Surface.Deck;
        bool inside = onThisCar && Math.Abs(self.Position.X) < w - layout.WallThickness;

        if (inside)
        {
            if (!heavy)
                return Walk(self, landing, facingIn + Math.PI, "out of the car");
            // Note 659: what's put down stays, so it's stacked: a crate against a wall, clear of the doorway and the aisle, from
            // the car's far ends in (StopPlan.Stow), set down in front, facing the wall. A find (under a step's height) or a car
            // full up: in the middle of the car, facing away from the door, as ever.
            var drop = new Double3(0, layout.FloorHeight, 0);
            double toward = facingIn;
            // (What's in our arms, if we know who we are; else a crate's size, the stack's: a find's the one thing put down in
            // the middle, and a find's in our hands only when we know.)
            double? size = mine is null ? StopPlan.CrateRadius : mine.Kind == Physics.BodyKind.Cargo ? mine.Pbd.Particles[0].Radius : null;
            if (size is { } radius && StopPlan.Stow(train, _car, side, radius, heavy: false) is { } stow)
            {
                int wall = Math.Sign(stow.X);
                toward = wall > 0 ? -Math.PI / 2 : Math.PI / 2;
                drop = stow with { X = stow.X - wall * world.Bodies.Hands.CarryForward };
            }
            // A jackpot between two (note 867): the front end goes on to the far wall, so what's between their hands is down
            // well inside, not in the doorway (where it rolled out onto the ballast again).
            else if (mine is { Second: >= 0 } && Run.Run.IsJackpot(mine))
                drop = drop with { X = -side * (w - layout.WallThickness - 0.45) };
            if (!Near(self, drop, 0.25) || !Aligned(self, toward))
                return Walk(self, drop, toward, "carrying in");
            Doing = "putting it down";
            return Press();
        }
        if (onThisCar)
        {
            // A find from the village put down here to open the door (note 326): the door's open, so up with it again.
            if (!heavy && open && _setDown is int put && world.Bodies.All.FirstOrDefault(b => b.Id == put && b.Carrier < 0) is { } setDown
                && (Physics.Bodies.WorldCentre(setDown, train) - PlayerMotor.WorldPosition(self, train)).Length < 2.5)
            {
                // In the car's frame, as the hands' yaw is: a world heading turned them away from it wherever the car didn't
                // point up world −Z (note 473's higher jump moved one night's timing onto that, and onto the door below).
                var lies = frame.ToLocal(Physics.Bodies.WorldCentre(setDown, train));
                var to = new Double3(lies.X - self.Position.X, 0, lies.Z - self.Position.Z);
                double toward = DMath.Atan2(-to.X, -to.Z);
                // Still at the door, Use works the door before it reaches the find (CrewActions first, Bodies.Hands); and a find
                // set down on the steps is a step down from the landing: over to stand with the hands above it first.
                if (to.Length > 0.8 || CrewActions.Nearest(self, train) is not null)
                {
                    var shortOf = self.Position + to * Math.Max(0, 1 - 0.6 / Math.Max(to.Length, 1e-6));
                    return Walk(self, shortOf with { Y = layout.FloorHeight }, toward, "picking the find up again");
                }
                if (!Aligned(self, toward))
                    return new PlayerIntent { LookYaw = Turn(self, toward) };
                Doing = "picking the find up again";
                return Press();
            }
            if (heavy || mine is not null)
                _setDown = null;
            // On its steps or the landing.
            if (!open)
            {
                if (heavy)
                {
                    Doing = "putting it down to open up";
                    // And back up again with it once it's open (a jackpot too, note 867: its other end's taken as ever).
                    if (mine is { Kind: Physics.BodyKind.Loot } || mine is not null && Run.Run.IsJackpot(mine))
                        _setDown = mine.Id;
                    return Press();
                }
                if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, landing, facingIn, "up to the door");
                Doing = "opening up";
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            }
            if (heavy)
                return Math.Abs(self.Position.Z) > sd - 0.3
                    ? Walk(self, landing, facingIn, "up to the door with it")
                    : Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "carrying in");
            // Down the steps to the ground, forward along the car, and off the bottom tread.
            return Walk(self, landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.5 }, Math.PI, "down the steps");
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, side);
        // A crate from elsewhere in the yard in our arms (note 403): back round the sheds, and round the train if it was
        // across it, to these steps first.
        if (mine is { Kind: Physics.BodyKind.Cargo or Physics.BodyKind.Heavy }
            && CrateHome(self, world, p, mine, frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 })) is { } back)
            return back;
        // On the far side of the train from the steps there's no way round on foot: put it down, over the train (the
        // walker climbs the nearest car; off it, we get down on this side), and back to it.
        var (_, across) = TrackCoords(train.Line, p.Spur.Index, self.Position, self.LineHint);
        // Empty-handed out in the yard, or across the train from the steps (beaten to a heavy crate's other end, note 440):
        // back round the sheds and the train to a car's steps first. The walker's way aboard goes straight at a ladder, and
        // between a hero and the cars beside it, it stood there till we left.
        if (mine is null && BackFromTheYard(self, world, p, Math.Sign(across) != side && Math.Abs(across) > 1.2) is { } yardBack)
            return yardBack;
        if (Math.Sign(across) != side && Math.Abs(across) > 1.2)
        {
            Doing = "over the train";
            return heavy ? Press() : null;
        }
        // A wreck heap groaning by us (note 187): clear of it first, whatever's in our arms.
        var here = PlayerMotor.WorldPosition(self, train);
        if (world.Run?.Groaning(here, 1.0) is { } groaning && world.Run.FacilityTuning is { } ft)
        {
            var away = (here - groaning.Centre) with { Y = 0 };
            var clear = groaning.Centre + (away.Length > 0.01 ? away.Normalized : new Double3(1, 0, 0)) * (ft.Wreck.CrushRadius + 2.5);
            Doing = "clear of the wreck";
            return WalkTo(self, train.Line, p.Spur.Index, clear with { Y = here.Y }, null).Step;
        }
        // On the ground: the door first, then a crate, then up the steps with it.
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        if (!open || heavy)
        {
            // In the lane in front of the steps, go on up them along the treads; anywhere else (beside the landing too: its
            // side is a wall as high as the floor), to the foot of them first.
            var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
            double outward = side * local.X - w;
            bool inLane = outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3;
            if (inLane)
                return Paced(mine, Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), heavy ? "up the steps with it" : "up the steps"));
            Doing = heavy ? "carrying" : "to the door";
            // Arrived at the foot but a hand's width outside the lane (the 100-night playtest's crate hands, stood there for
            // good with the crates in their arms): straight on up from there.
            var (walk, atFoot) = WalkTo(self, train.Line, p.Spur.Index, foot, null);
            return Paced(mine, atFoot ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), heavy ? "up the steps with it" : "up the steps") : walk);
        }
        // Nothing loose just now, though some are still on their way in (in someone's arms, settling): wait by the stack
        // for the next, rather than give up the stop.
        // The light ones gone, two hands take a heavy one between them: one takes an end, and the other comes to help.
        var crate = (forHeavy ? null : Crate(world, p, self))
            ?? (PlayerId is not null && calls.HeavyHands >= 2 ? HeavyCrate(world, p, self) : null);
        if (crate is null)
        {
            Doing = "waiting for a crate";
            return new PlayerIntent();
        }
        var at = crate.Parent == PlayerState.World ? crate.Centre : train.Frames[crate.Parent].ToWorld(crate.Centre);
        var from = new Double3(self.Position.X - at.X, 0, self.Position.Z - at.Z);
        var stand = at + (from.Length > 0.01 ? from.Normalized : new Double3(1, 0, 0)) * 0.8;
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand with { Y = at.Y }, null);
        if (!there && (Flat(self.Position) - Flat(stand)).Length > 0.25)
        {
            Doing = "to a crate";
            return step;
        }
        double yaw = DMath.Atan2(-(at.X - self.Position.X), -(at.Z - self.Position.Z));
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        Doing = "picking one up";
        return Press();
    }

    /// <summary>
    /// A heavy crate with one holding it, waiting for a hand (a bot or anyone else): at the site, or one a hand of ours has
    /// gone for out in the yard (its claim on the crew's calls, note 440), if we're a crate hand not out at the houses ourselves
    /// (a hand lent to the crates goes back to its own part when it's done, and the crate would be left half way).
    /// </summary>
    Physics.Body? Wanting(World world, StopPlan p, Double3 here) =>
        world.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.Heavy && b.Carrier >= 0 && b.Carrier != PlayerId && b.Second < 0
            && ((calls.SpotClaimed(CrateKey(b.Id), member) ? job == StopJob.Crates && _spot is null
                : (Physics.Bodies.WorldCentre(b, world.Train) - p.Site.CrateStack.FirstOrDefault()).Length < 60)
                // Note 867: a jackpot held alone anywhere in reach, the yard's or the village's, a person's too: any hand at the
                // stop with nothing of its own on (not off to a house) comes, whatever its part.
                || Run.Run.IsJackpot(b) && b.Parent == PlayerState.World && _spot is null && Crew(world).JackpotReach > 0
                    && (Flat(Physics.Bodies.WorldCentre(b, world.Train)) - Flat(here)).Length <= Crew(world).JackpotReach));

    /// <summary>
    /// To the free end of a heavy crate someone's holding: across it from them (if they've said where they are, else from
    /// the side we come from), facing it, and take hold.
    /// </summary>
    PlayerIntent? LendAHand(in PlayerState self, World world, Physics.Body crate)
    {
        var train = world.Train;
        var at = Physics.Bodies.WorldCentre(crate, train);
        var me = PlayerMotor.WorldPosition(self, train);
        var away = (calls.Where(crate.Carrier) is { } holder ? at - holder : me - at) with { Y = 0 };
        var stand = at + (away.Length > 0.01 ? away.Normalized : new Double3(1, 0, 0)) * EndOff(crate);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, _plan!.Site.Side);
        // Out in the yard (note 440): round the sheds and the train to it, as the one holding it went; and walked home by the
        // same reckoning (BackEnd), not along where the crate's been.
        _yardHelp = calls.SpotClaimed(CrateKey(crate.Id), member) ? crate.Id : -1;
        if ((Flat(me) - Flat(stand)).Length > 4)
        {
            if (_path is null && OutFromTheTrain(self, train) is { } clear)
                return clear;
            Doing = "to lend a hand in the yard";
            return Follow(self, train, stand);
        }
        _path = null;
        var (step, there) = WalkTo(self, train.Line, _plan!.Spur.Index, stand with { Y = at.Y }, null);
        if (!there && (Flat(self.Position) - Flat(stand)).Length > 0.25)
        {
            Doing = "to lend a hand";
            return step;
        }
        double yaw = DMath.Atan2(-(at.X - self.Position.X), -(at.Z - self.Position.Z));
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        Doing = "taking the other end";
        return Press();
    }

    /// <summary>Where the heavy crate in our hands has been, most recent last (T45): the back end walks where the front end went.</summary>
    readonly List<Double3> _trail = [];
    int _following = -1;

    /// <summary>
    /// The back end of a heavy crate: follow where it's been, a pace behind it, so the steps and the door come in the order
    /// the front end took them. Whoever has the front leads (and puts it down), a bot or not.
    /// </summary>
    PlayerIntent Follow(in PlayerState self, World world, Physics.Body crate)
    {
        var train = world.Train;
        var at = Physics.Bodies.WorldCentre(crate, train);
        if (_following != crate.Id)
        {
            _trail.Clear();
            _following = crate.Id;
        }
        if (_trail.Count == 0 || (_trail[^1] - at).Length > 0.2)
            _trail.Add(at);
        // After it, it's this car we'll be walking out of.
        if (crate.Parent > 0)
            _car = crate.Parent;
        // The point on the trail a pace back from the crate.
        var target = _trail[^1];
        double back = 0;
        for (int i = _trail.Count - 1; i > 0 && back < TrailBehind; i--)
        {
            back += (_trail[i] - _trail[i - 1]).Length;
            target = _trail[i - 1];
        }
        var local = self.Parent == PlayerState.World ? target : train.Frames[self.Parent].ToLocal(target);
        if (back < TrailBehind * 0.5 || Near(self, local, 0.3))
        {
            // Keep facing it while the front end stands.
            var to = self.Parent == PlayerState.World ? at : train.Frames[self.Parent].ToLocal(at);
            double yaw = DMath.Atan2(-(to.X - self.Position.X), -(to.Z - self.Position.Z));
            Doing = "holding the back end";
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        }
        return Head(self, local, "carrying the back end");
    }

    /// <summary>How far behind the crate, along where it's been, the back end walks.</summary>
    const double TrailBehind = 1.0;

    /// <summary>
    /// The cargo car in the engine's rake with the most room, counting crates already put down in it and the ones others
    /// say they're taking there; of equal ones, the nearest the stack.
    /// </summary>
    static int Roomiest(World world, StopPlan p, CrewCalls calls, int member)
    {
        var train = world.Train;
        // Note 920: the room the stop's packing plan leaves the lights (the heavy crates' whole halves set aside; what's
        // settling in a car taken off by size), less what other hands have said they're taking there.
        var room = p.LightRoom(world, calls);
        var stack = p.Site.CrateStack.Length > 0 ? p.Site.CrateStack[0] : train.Frames[0].Origin;
        return room.Select(kv => (Id: kv.Key, Room: kv.Value - p.Site.LoadPerCrate * calls.BoundFor(kv.Key, member)))
            .Where(x => x.Room > 1e-6).OrderByDescending(x => Math.Round(x.Room, 3)).ThenBy(x => (train.Frames[x.Id].Origin - stack).Length)
            .Select(x => x.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>The nearest crate a hand can pick up from the ground (<see cref="StopPlan.Loose"/>) on the working side.</summary>
    static Physics.Body? Crate(World world, StopPlan p, in PlayerState self)
    {
        var train = world.Train;
        var me = self.Position;
        Double3 At(Physics.Body b) => b.Parent == PlayerState.World ? b.Centre : train.Frames[b.Parent].ToWorld(b.Centre);
        return world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Cargo && p.Loose(world, b) && !Pushed(world, b)).OrderBy(b => (At(b) - me).Length).FirstOrDefault();
    }

    /// <summary>
    /// A crate the Freight Beetle has (its load, note 366): it shoves it away from whoever's nearest, so a hand going to take
    /// it chases it off the platform. Left till it's driven off it (the bots club it: <see cref="Heed.Beetle"/>; note 367).
    /// </summary>
    static bool Pushed(World world, Physics.Body b) =>
        world.ActiveEnemies.Any(e => e is Enemies.FreightBeetle { Gone: false } beetle && beetle.Load == b.Id);

    /// <summary>The nearest heavy crate lying loose on the working side (T45).</summary>
    static Physics.Body? HeavyCrate(World world, StopPlan p, in PlayerState self)
    {
        var train = world.Train;
        var me = self.Position;
        return world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Heavy && p.Loose(world, b) && !Pushed(world, b))
            .OrderBy(b => (Physics.Bodies.WorldCentre(b, train) - me).Length).FirstOrDefault();
    }

    /// <summary>A cargo car in the engine's rake with its door on the working side still open, that's this hand's to shut.</summary>
    static int? OpenSideDoor(World world, StopPlan p, CrewCalls calls, int member, in PlayerState self)
    {
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        return calls.ClaimDoor(member, [.. p.OpenSideDoors(train)], car => (train.Frames[car].Origin - me).Length);
    }

    /// <summary>A car's sliding door on one side (+1 right), if it has one.</summary>
    public static int? SideDoor(CarShape shape, int side) =>
        shape.DoorList.Where(d => Math.Sign(d.Box.Centre.X) == side && Math.Abs(d.Box.Centre.Z) < 1).Select(d => (int?)d.Index).FirstOrDefault();

    /// <summary>Up a car's steps to its side door and shut it.</summary>
    PlayerIntent? ShutUp(in PlayerState self, World world, StopPlan p, int car)
    {
        _car = car;
        var train = world.Train;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        var frame = train.Frames[car];
        int side = p.Site.Side;
        double w = frame.Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (self.Parent == car && self.Surface == Surface.Deck)
        {
            if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                return Walk(self, landing, facingIn, "shutting up");
            Doing = "shutting up";
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, side);
        var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
        double outward = side * local.X - w;
        // (Out to the step's whole width: the walk to the foot ends within a quarter metre of the tread's middle, which is past
        // a tread's width less 0.1. A hand that stopped there stood still for good, the door open: note 873.)
        if (outward > 0.1 && outward < layout.StepWidth && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "shutting up");
        Doing = "shutting up";
        return WalkTo(self, train.Line, p.Spur.Index, frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 }), null).Step;
    }

    /// <summary>A step towards a point on the player's own car (its frame), facing a way at the end.</summary>
    PlayerIntent Walk(in PlayerState self, Double3 target, double yaw, string doing)
    {
        Doing = doing;
        var (step, _) = WarmUp.Steer(self, target, yaw);
        return step;
    }

    /// <summary>Straight at a world point, facing where it's going (up a flight of steps).</summary>
    PlayerIntent Head(in PlayerState self, Double3 target, string doing)
    {
        Doing = doing;
        var to = new Double3(target.X - self.Position.X, 0, target.Z - self.Position.Z);
        double turn = Wrap(DMath.Atan2(-to.X, -to.Z) - self.Yaw);
        return new PlayerIntent { LookYaw = (float)Math.Clamp(turn, -0.4, 0.4), MoveZ = Math.Abs(turn) < 0.3 ? 0.6f : 0 };
    }

    static bool Near(in PlayerState self, Double3 target, double within) => (Flat(self.Position) - Flat(target)).Length < within;
    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);

    /// <summary>Done here once aboard: on the ground, the walker climbs the nearest car's ladder.</summary>
    PlayerIntent? Aboard(in PlayerState self, StopPlan p)
    {
        if (self.Parent == PlayerState.World)
        {
            Doing = "boarding";
            return null;
        }
        _done.Add(p.Key);
        _plan = null;
        Worked++;
        return null;
    }

    /// <summary>Down into the gap behind the cut car along the roofs, and on the coupler plate, look down and hold Uncouple.</summary>
    /// <summary>How far a bot looks down to cut: past the tuning's least (couplings.uncoupleLookDownDegrees), at the plate.</summary>
    const double CutPitch = -1.3;

    PlayerIntent? Cut(in PlayerState self, TrainOnLine train, StopPlan p) => CutAt(self, train, p.CutBehind);

    /// <summary>
    /// Note 343: the driver on its own, cutting loose the car a hound pack has boarded (v1.1 App. A.3: "they go only dead, or
    /// with their car cut loose"; note 269), the train standing: down out of the cab, along the ballast and up the nearest
    /// car's side ladder (the walker's way aboard, <see cref="RoofWalkerBot.Board"/>), along the roofs to the gap behind
    /// <paramref name="car"/>, and Uncouple on its plate. Only the climb, not a walker's judgement: hurt, a walker keeps clear
    /// of a pack aboard, and a lone driver at 1 hp stood between the ground and car 1's landing for the rest of the night.
    /// </summary>
    public PlayerIntent? CutLoose(in PlayerState self, World world, int car)
    {
        var train = world.Train;
        if (CutAt(self, train, car) is { } cutting)
            return cutting;
        Doing = "to the cut";
        // Out of the cab, or out of a car, or off the wrong gap's plate: down onto the ballast. A car's side-door landing is on
        // the way to its ladder (the walk along the train goes up its steps): on along to the ladder from there.
        if (self.Parent == 0 && self.Surface == Surface.Deck)
            return GetDown(self, train, +1);
        if (self.Parent > 0 && self.Surface == Surface.Deck)
        {
            var layout = train.Dynamics.Tuning.Geometry.Interior;
            bool inside = layout is null || Math.Abs(self.Position.X) < train.Frames[self.Parent].Shape.Bounds.Max.X - layout.WallThickness;
            return inside ? OffTheCar(self, train, +1, shutBehind: true) ?? new PlayerIntent() : ToARoofLadder(self, train);
        }
        if (self.Surface == Surface.Coupler)
            return GetDown(self, train, +1) ?? new PlayerIntent();
        // Mid-climb: keep going up. On the ballast: to the nearest side ladder.
        if (self.Surface == Surface.Ladder)
            return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        return self.Parent == PlayerState.World ? ToARoofLadder(self, train) : new PlayerIntent();
    }

    /// <summary>
    /// On foot to the nearest side ladder that reaches a roof, round the train (<see cref="WalkTo"/>: out past the side-door
    /// steps, which a walk straight along the car's side goes up), and at its foot, take hold (<see cref="RoofWalkerBot.Board"/>).
    /// </summary>
    /// <param name="only">Only that car's ladders (note 496: up onto the roof of a car with no side door, for its lamp).</param>
    /// <summary>
    /// Note 533: on the ballast with the train standing and no stop's part to go back aboard by (out of a Holdout someone
    /// broke them out of, say): to the nearest roof ladder of the engine's rake by <see cref="OnFoot"/>, round whatever's in
    /// the way. The walker's own way (<see cref="RoofWalkerBot.Board"/>) is a straight line: on frontier:7 seed 13 a walker
    /// freed from the Holdout at km 10 ran into its wall for two minutes, the driver went on, and the Ribbits had it. Its
    /// stale stop plan used to take it to the cab instead (the bug that kept two hurt hands at the engine's side on seed 6).
    /// </summary>
    PlayerIntent? LeftOnTheBallast(in PlayerState self, World world)
    {
        var train = world.Train;
        if (!self.Alive || world.Run is not { Phase: not Run.RunPhase.Yard } || self.Parent != PlayerState.World || self.Surface != Surface.Ground
            || self.Has(PlayerFlags.Held) || Math.Abs(train.Dynamics.Velocity) > 0.05)
        {
            _blockedLadders.Clear();
            _ballastStill = 0;
            return null;
        }
        Double3? best = null;
        double bestD = double.MaxValue;
        int bestKey = -1;
        var here = PlayerMotor.WorldPosition(self, train);
        foreach (var frame in train.Frames)
        {
            if (frame.Index == 0 || train.StandingCar(frame.Index) || train.Dynamics.Consist.IndexOf(frame.Index) < 0 || AheadOfTheEngine(train, frame.Index))
                continue;
            for (int l = 0; l < frame.Shape.Ladders.Count; l++)
            {
                var ladder = frame.Shape.Ladders[l];
                if (Math.Abs(ladder.Inward.X) < 0.9 || ladder.Foot.Y > 0.5 || ladder.Top < frame.Shape.RoofHeight - 0.5
                    || _blockedLadders.Contains(frame.Index * 64 + l))
                    continue;
                var at = frame.ToWorld(ladder.Foot - ladder.Inward * 0.3);
                // Note 614: not up by a gap a Whistler's watching, which a bot on its own keeps clear of (Heed.Gaps): local:7
                // seed 5's gunner, the nearest ladder by one, stood off it for three minutes and the driver waited on it.
                if (Heed.Watched(world, at))
                    continue;
                double d = (Flat(at) - Flat(here)).Length;
                if (d < bestD)
                    (best, bestD, bestKey) = (at, d, frame.Index * 64 + l);
            }
        }
        if (best is not { } foot)
        {
            _blockedLadders.Clear();
            return null;
        }
        // Note 609: walked at a ladder's foot and got no nearer (frontier:7 seed 3's gunner, a metre from car 4's, stood pressed
        // against its side-door steps for the 90 s the driver waited): another ladder.
        if (_ballastAt is { } was && (Flat(here) - Flat(was)).Length < 0.3)
        {
            if (++_ballastStill > BallastStuck * SimConstants.TickRate)
            {
                _blockedLadders.Add(bestKey);
                _ballastStill = 0;
            }
        }
        else
            (_ballastAt, _ballastStill) = (here, 0);
        // Note 609: nobody climbs with freight in their arms. Its stop given up for the dawn with a crate in its arms on a car's
        // steps, frontier:7 seed 3's gunner stood at a ladder's foot with it, and the driver waited out the aboard give-up.
        if (self.Has(PlayerFlags.Heavy))
        {
            Doing = "putting it down to board";
            return Press();
        }
        Doing = "back aboard";
        if (bestD > 1.0)
            return OnFoot(self, train, train.Dynamics.Path, foot, null).Step;
        return RoofWalkerBot.Board(self, train, roofOnly: true);
    }

    /// <summary>Note 609: seconds on the ballast without getting anywhere before a hand tries another ladder.</summary>
    const double BallastStuck = 3;
    readonly HashSet<int> _blockedLadders = [];
    Double3? _ballastAt;
    int _ballastStill;

    /// <summary>
    /// A car the engine's rake carries ahead of the engine: a switchyard's, picked up off its siding (note 187: the crew go in
    /// for each siding's with every car they've already picked up still ahead of the engine). Its roof's no way to the train's:
    /// the engine's between, its cab forward and its tender lower than a roof (note 618).
    /// </summary>
    public static bool AheadOfTheEngine(TrainOnLine train, int car)
    {
        var consist = train.Dynamics.Consist;
        int at = consist.IndexOf(car);
        return at >= 0 && at < consist.IndexOf(0);
    }

    PlayerIntent ToARoofLadder(in PlayerState self, TrainOnLine train, int? only = null)
    {
        Double3? best = null;
        double bestD = double.MaxValue;
        foreach (var frame in train.Frames)
        {
            if (frame.Index == 0 || train.StandingCar(frame.Index) || train.Dynamics.Consist.IndexOf(frame.Index) < 0 || AheadOfTheEngine(train, frame.Index)
                || only is { } o && frame.Index != o)
                continue;
            foreach (var ladder in frame.Shape.Ladders)
            {
                if (Math.Abs(ladder.Inward.X) < 0.9 || ladder.Foot.Y > 0.5 || ladder.Top < frame.Shape.RoofHeight - 0.5)
                    continue;
                var at = frame.ToWorld(ladder.Foot - ladder.Inward * 0.3);
                double d = (Flat(at) - Flat(self.Position)).Length;
                if (d < bestD)
                    (best, bestD) = (at, d);
            }
        }
        if (best is not { } foot)
            return new PlayerIntent();
        if (bestD > 1.0)
            return WalkTo(self, train.Line, train.Dynamics.Path, foot, null).Step;
        return RoofWalkerBot.Board(self, train, roofOnly: true);
    }

    /// <summary>
    /// Note 380: the cut behind <paramref name="car"/> by the roofs only, for a train running faster than anyone runs: along
    /// them to the gap, down onto its plate, and Uncouple. Null off the roofs (in a car, on a ladder): <see cref="CutLoose"/>'s
    /// way round there is the ballast, which only a standing train's crew can take. On frontier:3's hot run two walkers in
    /// cars 3 and 4, sent to a cut at 9 m/s, stepped out of their side doors to go round and were left 48 hp each.
    /// </summary>
    public PlayerIntent? CutFromTheRoofs(in PlayerState self, World world, int car) => CutAt(self, world.Train, car);

    PlayerIntent? CutAt(in PlayerState self, TrainOnLine train, int car)
    {
        if (self.Surface == Surface.Coupler && self.Parent == car)
        {
            // Facing out to the right, away from both doors: Use facing one works the door instead.
            const double yaw = -Math.PI / 2;
            if (!Aligned(self, yaw))
                return new PlayerIntent { LookYaw = Turn(self, yaw) };
            Doing = "cutting";
            // Looking down at the coupler, as anyone has to (T91).
            return new PlayerIntent { Actions = PlayerActions.Uncouple, LookPitch = (float)(CutPitch - self.Pitch) };
        }
        // On the ground or in the wrong gap, the walker gets back up onto the roofs.
        if (self.Surface != Surface.Roof || self.Parent <= 0)
            return null;
        Doing = "to the cut";
        int here = self.Parent;
        int direction = here <= car ? +1 : -1; // +1 walks toward the back
        bool last = here == car || here == train.VehicleBehind(car);
        return AlongRoofs(self, train, direction, jumpGaps: !last);
    }

    /// <summary>Off the train on the switch's side, to the stand beside the points, and hold Use until they go over.</summary>
    PlayerIntent? Throw(in PlayerState self, TrainOnLine train, Branch branch)
    {
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, branch.Side);
        Doing = "at the switch";
        var stand = TrackPoint(train.Line, RailLine.MainPath, branch.Toe, branch.Side * StandOff);
        var (step, there) = WalkTo(self, train.Line, RailLine.MainPath, stand, null);
        return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
    }

    /// <summary>
    /// A crewmate waiting in a lit Holdout (GDD App. D.5, T96): down off the standing train on its side, along to its door,
    /// and hold Use there until it's breached. Null once it isn't lit.
    /// </summary>
    /// <summary>
    /// Note 650: a part in the stop's own moves before its loading (the shunter's, or the points thrown for the shunter,
    /// note 609), not left for a Holdout's breach. frontier:6's switchyard Holdout, now beside the line in reach of the
    /// throat: its switch hand went to it instead of the points, the driver stood Held 52 → 63 s, the loading began later,
    /// and the pick-up after it no longer reckoned to fit (16 nights' 16 → 8). Any other hand breaches at that stand, as
    /// before (frontier:7's Foundry: kept to the loading's stand, it took a pair's hand off the work there).
    /// </summary>
    public bool OnTheMoves(World world) =>
        calls.BeforeTheLoading && !calls.Leaving && _plan is { } p
        && (job == StopJob.Shunter && calls.Has(StopJob.Shunter) || ThrowsForTheShunter(world, p));

    public PlayerIntent? Breach(in PlayerState self, World world, Run.Holdout h)
    {
        var train = world.Train;
        if (!h.Lit || world.Holdouts is not { } hs)
            return null;
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, Side(train, h.Door, h.LineHint));
        Doing = "at the Holdout";
        var at = PlayerMotor.WorldPosition(self, train);
        // Smash and pry want a melee tool in hand (D.7; note 275): a hand put free picks the first one up again.
        if (((h.Door - at) with { Y = 0 }).Length <= hs.Tuning.BreachReach * 0.8)
            return new PlayerIntent { Buttons = PlayerButtons.Use, Select = Kit.ToolToHand(self) };
        return OnFoot(self, train, RailLine.MainPath, h.Door, null).Step;
    }

    /// <summary>
    /// The driver on its own (<see cref="StopDriver.SetBackAlone"/>): down out of the cab, the switch set back, and back up
    /// into the cab. Null once it's back at the controls with the switch right.
    /// </summary>
    /// <param name="toBranch">Over for the branch instead (a stop's spur with no shunter, note 261).</param>
    public PlayerIntent? SetBackAlone(in PlayerState self, World world, Branch? branch, bool toBranch = false)
    {
        var train = world.Train;
        if (branch is { } b && train.Diverging(b.Index) != toBranch)
            return self.Surface == Surface.Air ? new PlayerIntent() : Throw(self, train, b);
        if (PlayerMotor.InCab(self, train))
            return null;
        if (self.Surface == Surface.Air || Math.Abs(train.Dynamics.Velocity) > 0.05)
            return new PlayerIntent();
        // Still up on a car (the last door it shut, on that car's landing): off it by the side it's on first. The walk to the
        // cab is on the ground; from a car's deck it went nowhere, and a crew of one stood there while the fire died (note 300).
        if (self.Parent > 0 && (OffTheCar(self, train, self.Position.X < 0 ? -1 : 1, shutBehind: true) ?? GetDown(self, train, self.Position.X < 0 ? -1 : 1)) is { } off)
            return off;
        return IntoCab(self, train, self.LineHint >= 0 && self.Parent == PlayerState.World ? Side(train, self.Position, self.LineHint) : 1);
    }

    /// <summary>Which side of the main line a point on the ground is (+1 right).</summary>
    static int Side(TrainOnLine train, Double3 at, double lineHint)
    {
        var t = train.Line.Sample(RailLine.MainPath, lineHint);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return Double3.Dot(at - t.Position, right) >= 0 ? 1 : -1;
    }

    /// <summary>Into the engine's cab up its steps on the working side, and stand there (it's warm while the fire's lit).</summary>
    PlayerIntent? Ride(in PlayerState self, TrainOnLine train, StopPlan p)
    {
        if (PlayerMotor.InCab(self, train))
        {
            Doing = "in the cab";
            return new PlayerIntent();
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, p.Spur.Side);
        // You don't climb onto an engine that's moving.
        if (Math.Abs(train.Dynamics.Velocity) > 0.05)
            return new PlayerIntent();
        // By the cab's door on the side it's on (the crane's operator is across the track from the rest, T54): there's no
        // walking through the train.
        return IntoCab(self, train, SideOf(train, p, self.Position, self.LineHint));
    }

    /// <summary>From the ground on one side (+1 right), to the foot of the cab's steps there and up into it.</summary>
    PlayerIntent? IntoCab(in PlayerState self, TrainOnLine train, int side)
    {
        Doing = "to the cab";
        // On the cab's steps: on up (with nothing held, a hand lets go, and it was back on the ballast to try again, all night).
        if (self.Parent == 0 && self.Surface == Surface.Ladder)
            return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        var engine = train.Frames[0];
        var foot = engine.ToWorld(new Double3(side * (CabStepsOut(train) + 0.05), 0, CabDoorZ(train)));
        var inward = engine.DirToWorld(new Double3(-side, 0, 0));
        var (step, there) = OnFoot(self, train, train.Dynamics.Path, foot, DMath.Atan2(-inward.X, -inward.Z));
        return there ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : step;
    }

    /// <summary>
    /// Note 406: further than this from where it's going on the ground (m), a hand goes by <see cref="FootPath"/>, round the
    /// walls and the lineside trees, and only beside the train by <see cref="WalkTo"/>. A Holdout's door is up to
    /// <see cref="Heed.HoldoutRange"/> off the line, and <see cref="WalkTo"/> keeps to the track's own across and along: a
    /// driver gone to breach one 160 m from the cab walked into the spruce beside the line, slid along it the wrong way, and
    /// stood out there till the dawn with the train on its brake (frontier:7, seed 2).
    /// </summary>
    const double ByFootPath = 10;

    /// <summary>
    /// To a point on the ground: by <see cref="FootPath"/> while it's far and there's a way, then round the train by
    /// <see cref="WalkTo"/>. Beside the train the foot path's margins (a car's steps, the coupling gaps) can leave no way
    /// between it and the trees where a crewmate fits: a lone driver back from cutting a car loose found none to the cab
    /// from 150 m back, walked straight at the cars, and stood against them (frontier:7, seed 5). There, the track-side walk.
    /// </summary>
    (PlayerIntent Step, bool There) OnFoot(in PlayerState self, TrainOnLine train, int path, Double3 target, double? yaw)
    {
        var here = PlayerMotor.WorldPosition(self, train);
        if (self.Parent == PlayerState.World && ((Flat(target) - Flat(here)).Length > ByFootPath || Across(train, path, here, target, self.LineHint)
                || OffTheTrack(train, path, target, self.LineHint))
            && WayTo(train, here, target))
            return (Follow(self, train, target), false);
        return WalkTo(self, train.Line, path, target, yaw);
    }

    /// <summary>
    /// Note 620: <paramref name="target"/> is well off the track (past <see cref="TrackSide"/>), a Holdout's door say: the
    /// foot path all the way, round the walls, and never <see cref="WalkTo"/>'s track-side walk, which keeps to the track's own
    /// across and along. Within <see cref="ByFootPath"/> of a door on the far side of its house it walked into the wall and
    /// stood there, 6.7 m short: the breach never began, the driver stood its give-up out, and the crewmate in it was left
    /// (deadLines:7 seed 4).
    /// </summary>
    static bool OffTheTrack(TrainOnLine train, int path, Double3 target, double hint) =>
        Math.Abs(TrackCoords(train.Line, path, target, hint).Across) > TrackSide;

    /// <summary>How far either side of the track the track-side walk is the way (m): the cars, their steps and a stride.</summary>
    const double TrackSide = 6;

    /// <summary>
    /// Note 486: <paramref name="target"/> is across the track from <paramref name="here"/> with the train standing between:
    /// <see cref="WalkTo"/> keeps to its own side of the track and steps across it at the end, straight into the cars. A
    /// 2-bot crew's shunter, lent to the winch on the far side of the spur, walked into car 3's side (and up its steps, and
    /// off) for the six minutes the driver cranked alone. The way round is the foot path's.
    /// </summary>
    static bool Across(TrainOnLine train, int path, Double3 here, Double3 target, double hint)
    {
        var (a, x) = TrackCoords(train.Line, path, here, hint);
        var (ta, tx) = TrackCoords(train.Line, path, target, hint);
        if (Math.Sign(x) == Math.Sign(tx) || Math.Abs(x) < 0.5 || Math.Abs(tx) < 0.5)
            return false;
        // The train between them: a car on this path within the stretch from here to there, or the engine (note 533: two hands
        // beside it, the cab's door across it, walked into its side for the rest of the night at seed 6's Renwick Yard).
        double lo = Math.Min(a, ta) - 2, hi = Math.Max(a, ta) + 2;
        foreach (var f in train.Frames)
            if (train.Line.Nearest(f.Origin, ref hint) is var (_, along) && along >= lo - f.Shape.HalfLength && along <= hi + f.Shape.HalfLength
                && Math.Abs(TrackCoords(train.Line, path, f.Origin, hint).Across) < 1)
                return true;
        return false;
    }

    /// <summary>A way to <paramref name="goal"/> by <see cref="FootPath"/>, kept or planned; false when there's none, and it
    /// isn't looked for again for <see cref="NoWayFor"/> ticks (each look is a search over the whole stretch).</summary>
    bool WayTo(TrainOnLine train, Double3 here, Double3 goal)
    {
        if (_path is not null && (Flat(_pathGoal) - Flat(goal)).Length <= 1)
            return true;
        if (_noWayLeft > 0 && (Flat(_noWayTo) - Flat(goal)).Length <= 1)
        {
            _noWayLeft--;
            return false;
        }
        _path = FootPath.Plan(train, here, goal);
        _pathGoal = goal;
        _pathAt = 0;
        _stuckTicks = 0;
        if (_path is not null)
            return true;
        (_noWayTo, _noWayLeft) = (goal, NoWayFor);
        return false;
    }

    const int NoWayFor = SimConstants.TickRate * 3;
    Double3 _noWayTo;
    int _noWayLeft;

    /// <summary>The middle of the cab's side doorways along the engine (between the side wall and the back pillar).</summary>
    static double CabDoorZ(TrainOnLine train)
    {
        var g = train.Dynamics.Tuning.Geometry;
        var plan = EnginePlan.Of(g);
        return (plan.DoorFront + plan.CabBack - 0.15) / 2;
    }

    /// <summary>The stick, at most, stepping off the engine's running board: a shuffle, so the drop is all but straight down.</summary>
    const float Shuffle = 0.1f;

    /// <summary>
    /// How far out from the engine's middle its cab steps hang (m): off the running board's outer edge (note 559). Their foot
    /// on the ground is here, and the board's edge a hand inside it.
    /// </summary>
    static double CabStepsOut(TrainOnLine train)
    {
        var shape = train.Frames[0].Shape;
        double reach = shape.HalfWidth + 0.15;
        foreach (var l in shape.Ladders)
            if (l.Foot.Y < 0.2 && Math.Abs(l.Inward.X) > 0.9)
                reach = Math.Max(reach, Math.Abs(l.Foot.X));
        return reach;
    }

    /// <summary>
    /// Off the train onto the ballast on one side (+1 right): out through the cab's doorway and off its step, or off the
    /// edge of a roof or the coupler plate, slowly enough to land without a roll (the train is standing).
    /// </summary>
    /// <summary>Within this of the roof's middle (m) is on its centreline (App. A.4: about 1.4 m wide).</summary>
    const double CentreLine = 0.4;

    PlayerIntent? GetDown(in PlayerState self, TrainOnLine train, int side)
    {
        // Note 573: down only where there's ground to land on. deepTerritory:1 seed 2's walker and gunner stepped off car 8's
        // roof and car 2's plate at a stand by a tunnel's mouth, the ground 26 m under the rail (a fall that kills now). The
        // other side if it has ground; neither, and it stays where it is.
        if (self.Parent >= 0 && self.Surface is Surface.Roof or Surface.Coupler or Surface.Deck)
        {
            var at = PlayerMotor.WorldPosition(self, train);
            if (!GroundBeside(train, at, side))
            {
                if (!GroundBeside(train, at, -side))
                    return new PlayerIntent();
                side = -side;
            }
        }
        double facing = side > 0 ? -Math.PI / 2 : Math.PI / 2;
        // Never off anything that's moving. On a roof, out to its centreline meanwhile (note 526; App. A.4: "walk the
        // centreline"): sent along the roofs at a stop for the Fire Flies' lamp, the train going in under it, a hand stood
        // where it was on car 2's roof and a Dragger pulled it off (frontier:7, seed 6).
        if (self.Parent >= 0 && Math.Abs(train.RakeOf(self.Parent).Velocity) > 0.05)
            return self.Surface == Surface.Roof && Math.Abs(self.Position.X) > CentreLine
                ? new PlayerIntent { MoveX = (float)(-Math.Sign(self.Position.X) * DMath.Cos(self.Yaw)), MoveZ = (float)(Math.Sign(self.Position.X) * DMath.Sin(self.Yaw)) }
                : new PlayerIntent();
        // In the cab or on the engine's deck by its doorway.
        if (self.Parent == 0 && self.Surface == Surface.Deck)
        {
            Doing = "out of the cab";
            double door = CabDoorZ(train);
            // Back down the cab to abreast of the doorway first, at a walk (the stroll's for the step off), by the way down that
            // side: note 716's big cab has its doorways at its back, 10 m behind the controls, past the chart table and up over
            // the gallery (and the coal bunker along the left wall at the front, note 280, is passed clear of it).
            if (PlayerMotor.InCab(self, train) && Math.Abs(self.Position.Z - door) > 0.25)
            {
                var lane = new Double3(side * EnginePlan.Of(train.Dynamics.Tuning.Geometry).Lane, self.Position.Y, door);
                return WarmUp.SteerCab(self, train, lane, facing).Step;
            }
            // Out across the running board in front of the doorway (note 559) and off its edge, the last of it at a shuffle so
            // the drop's straight down: on a bridge the deck's no wider than 2.6 m from the track (note 552), the board's edge
            // is 2.35, and stepped off at a stroll a hand landed 2.75 out, walked back along the train out there, and went
            // off the deck of frontier:7's Stroud Bridge.
            double edge = CabStepsOut(train) - 0.15;
            var off = Edge(self, new Double3(side * (edge + 0.3), self.Position.Y, door), facing);
            return Math.Abs(self.Position.X) < edge - 0.3 ? off
                : off with { MoveX = Math.Clamp(off.MoveX, -Shuffle, Shuffle), MoveZ = Math.Clamp(off.MoveZ, -Shuffle, Shuffle) };
        }
        if (self.Surface is Surface.Roof or Surface.Coupler && self.Parent >= 0)
        {
            Doing = "getting down";
            return Edge(self, new Double3(side * (train.Frames[self.Parent].Shape.Bounds.Max.X + 1), self.Position.Y, self.Position.Z), facing);
        }
        return null;
    }

    /// <summary>
    /// Ground to land on beside the train at <paramref name="at"/>, on that side (+1 right): no further under the rail there
    /// than <see cref="ConductorBot.NoGroundBelow"/> (#294's test for a walk beside the train, note 552).
    /// </summary>
    public static bool GroundBeside(TrainOnLine train, Double3 at, int side)
    {
        var line = train.Line;
        double hint = train.Dynamics.Distance;
        var (path, along) = line.Nearest(at, ref hint);
        var t = line.Sample(path, along);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        double h = along;
        return PlayerMotor.GroundAt(t.Position + right * (side * ConductorBot.AfootOut), line, ref h) >= t.Position.Y - ConductorBot.NoGroundBelow;
    }

    /// <summary>The share of a walk a hand steps off the train at (a step off the edge, not a stride).</summary>
    const float EdgeStep = 0.5f;

    /// <summary>Walk to a point on the player's own car in its frame, facing a way, at a stroll (so it's not a roll).</summary>
    static PlayerIntent Edge(in PlayerState self, Double3 target, double yaw)
    {
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        var (step, _) = WarmUp.Steer(self, target, yaw);
        // Half a walk however it's headed (note 553): held to half on each axis, a step off on a slant was 0.71 of one, over
        // spec B.3's roll (landing.rollAbove, 1.5 m/s), and a hand getting down out of the cab at a stand landed 11 the worse.
        float len = MathF.Sqrt(step.MoveX * step.MoveX + step.MoveZ * step.MoveZ);
        if (len > EdgeStep)
        {
            step.MoveX *= EdgeStep / len;
            step.MoveZ *= EdgeStep / len;
        }
        return step;
    }

    /// <summary>Along the roofs toward the back (+1) or front (−1), jumping the gaps unless it's to step off into one.</summary>
    internal static PlayerIntent AlongRoofs(in PlayerState self, TrainOnLine train, int direction, bool jumpGaps)
    {
        double yaw = direction < 0 ? 0 : Math.PI;
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        double half = train.Frames[self.Parent].Shape.HalfLength, z = self.Position.Z;
        // Jumping the gaps, along the middle; stepping off into one, over its plate (on the end doors' line, note 231).
        var g = train.Dynamics.Tuning.Geometry;
        double across = (jumpGaps ? 0 : g.PlateX) - self.Position.X;
        double lateral = Math.Clamp(across * 0.8 * (direction < 0 ? 1 : -1), -1, 1);
        var intent = new PlayerIntent { MoveZ = 1, MoveX = (float)lateral, Buttons = jumpGaps ? PlayerButtons.Run : PlayerButtons.None };
        bool nearEnd = direction < 0 ? z < -half + 0.45 : z > half - 0.45;
        int beyond = direction < 0 ? train.VehicleAhead(self.Parent) : train.VehicleBehind(self.Parent);
        if (!jumpGaps && Math.Abs(direction < 0 ? z + half : z - half) < 0.8
            && (Math.Abs(across) >= g.CouplerWidth / 2 - WarmUp.PlateMargin || Heed.Knotted(train, self.Parent, beyond)))
            intent.MoveZ = 0; // square up over the plate first (and never down onto a Knotter's back: note 367)
        if (jumpGaps && nearEnd && beyond > 0)
        {
            if (WarmUp.CanJumpGap(self, train, beyond))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0; // square up on the centreline first (or wait out the curve)
        }
        return intent;
    }

    /// <summary>A point beside a path: along it, and across it (+ right, looking up the line).</summary>
    static Double3 TrackPoint(RailLine line, int path, double along, double across)
    {
        var s = line.Sample(path, along);
        return s.Position + Double3.Cross(s.Tangent, Double3.Up).Normalized * across;
    }

    /// <summary>Where a world point is in terms of a path: how far along it, and how far across (+ right).</summary>
    static (double Along, double Across) TrackCoords(RailLine line, int path, Double3 world, double hint)
    {
        var (_, along) = line.Nearest(world, ref hint);
        var s = line.Sample(path, along);
        return (along, Double3.Dot(world - s.Position, Double3.Cross(s.Tangent, Double3.Up).Normalized));
    }

    /// <summary>
    /// One step on foot towards a point on the ground, going round the train rather than into it: out to the side first
    /// if it's close to the track, along beside it, then in. True once it's there (and facing <paramref name="yaw"/>).
    /// </summary>
    static (PlayerIntent Step, bool There) WalkTo(in PlayerState self, RailLine line, int path, Double3 target, double? yaw)
    {
        var (a, x) = TrackCoords(line, path, self.Position, self.LineHint);
        var (ta, tx) = TrackCoords(line, path, target, self.LineHint);
        int side = tx >= 0 ? 1 : -1;
        double da = ta - a;
        var aim = Math.Abs(da) > 1.5 && Math.Abs(x) < Clear - 0.2 ? TrackPoint(line, path, a, side * Clear)
            : Math.Abs(da) > 6 ? TrackPoint(line, path, a + Math.Sign(da) * 6, side * Math.Max(Clear, Math.Abs(tx)))
            : target;
        var to = new Double3(aim.X - self.Position.X, 0, aim.Z - self.Position.Z);
        double distance = to.Length;
        if (aim == target && distance < 0.25)
            return yaw is not { } y || Aligned(self, y) ? (new PlayerIntent(), true) : (new PlayerIntent { LookYaw = Turn(self, y) }, false);
        double heading = DMath.Atan2(-to.X, -to.Z);
        double turn = Wrap(heading - self.Yaw);
        bool far = (target - self.Position).Length > 4;
        return (new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.4, 0.4),
            MoveZ = Math.Abs(turn) < 0.3 ? (float)Math.Clamp(distance * 1.5, 0.2, 1) : 0,
            Buttons = far ? PlayerButtons.Run : PlayerButtons.None,
        }, false);
    }

    static float Turn(in PlayerState self, double yaw) => (float)Math.Clamp(Wrap(yaw - self.Yaw), -0.5, 0.5);
    static bool Aligned(in PlayerState self, double yaw) => Math.Abs(Wrap(yaw - self.Yaw)) < 0.1;
    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}

/// <summary>Note 621: one of the driver's reckonings of the run home (<see cref="StopDriver.Reckonings"/>).</summary>
public readonly record struct Reckoning(double Seconds, double At, double DawnIn, double Grace, double AtCruise, double AtPace, double Load, int StopsDone);
