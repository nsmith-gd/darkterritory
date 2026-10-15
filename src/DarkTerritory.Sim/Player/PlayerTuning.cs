namespace DarkTerritory.Sim.Player;

/// <summary>Mirror of content/tuning/player.json.</summary>
public sealed record PlayerTuning(
    double Run, double Walk, double RoofRun, double RoofWalkSafe, double LadderClimb,
    double CarryHeavy, double JumpGap, int Health, ColdTuning Cold,
    double Gravity, double Radius, double Height, double StepUp,
    LandingTuning Landing, LadderTuning Ladder, HandTuning Hand, double JumpHeight = 0, double PushGun = 1.2, IReadOnlyList<string>? Kit = null)
{
    /// <summary>T108: what every player starts the night (and every respawn) with, packed (<see cref="Player.Kit"/>).</summary>
    public ulong StartingKit => Player.Kit.Of((Kit ?? ["crowbar"]).Select(k => Enum.Parse<Tool>(k, ignoreCase: true)));

    public const string File = "tuning/player.json";

    /// <summary>What the bots are called, in order (the roster and the incident report name them); past the list, "Crew n".</summary>
    public IReadOnlyList<string> BotNames { get; init; } = [];

    /// <summary>
    /// GDD v1.4 App. D.9 "solo remainer": with exactly one of the crew left alive, they can climb a ladder carrying a body, at
    /// this speed (a quarter of normal; note 181).
    /// </summary>
    public double SoloBodyClimb { get; init; } = 0.4;

    /// <summary>Note 664: crouching (the director's playtest, 13 Oct: "we should have a crouch function"). Docs in player.json.</summary>
    public CrouchTuning Crouch { get; init; } = new();

    /// <summary>GDD §22 wind, and spec B.2's "roof run: wind and balance penalty", on a roof's footing (note 201).</summary>
    public WindTuning Wind { get; init; } = new();

    /// <summary>Note 279: the steepest land (rise over run) someone on foot walks up. Docs in player.json.</summary>
    public double ClimbSlope { get; init; } = 1.25;

    /// <summary>T128 (note 273): a roof's edge holds whoever's only walking at it. Field docs live in player.json <c>edge</c>.</summary>
    public EdgeTuning Edge { get; init; } = new();

    /// <summary>A dropped player's place on the host, and the client's retries (spec E drop-out, GDD v1.4 App. D.2; note 253).</summary>
    public RejoinTuning Rejoin { get; init; } = new();

    /// <summary>How many the crew can be, and how a joiner past it is turned away (GDD §1 "2–8"; note 254).</summary>
    public CrewTuning Crew { get; init; } = new();

    /// <summary>The links in play: their timeout, keepalive and pump, and a client's pacing to its host (note 532).</summary>
    public LinkTuning Link { get; init; } = new();

    /// <summary>The emotes' lengths (GDD §9's yard, "dance"; note 298). Field docs live in player.json <c>emotes</c>.</summary>
    public EmoteTuning Emotes { get; init; } = new();
    public NewsTuning News { get; init; } = new();

    /// <summary>
    /// Horseplay among the crew (note 717; the director, 14 Oct 2026): crewmates solid to each other, the shove, coal off the
    /// shovel, a tool's bonk. Field docs live in player.json <c>horseplay</c>.
    /// </summary>
    public HorseplayTuning Horseplay { get; init; } = new();
    /// Voices as tools (the friendslop audit's Friends goal; the director, 14 Oct 2026; note 720): the speaking tube, singing
    /// together, and lamp signals. Field docs live in player.json <c>voices</c>.
    /// </summary>
    public VoicesTuning Voices { get; init; } = new();

    public string BotName(int i) => i < BotNames.Count ? BotNames[i] : $"Crew {i + 1}";

    /// <summary>
    /// Take-off speed that lifts the feet <see cref="JumpHeight"/>; without one, the old derivation (a flat jump at roof-run
    /// speed spans exactly <see cref="JumpGap"/>), which is also the least it may be.
    /// </summary>
    public double JumpVelocity => Math.Max(Gravity * JumpGap / (2 * RoofRun), Math.Sqrt(2 * Gravity * JumpHeight));
}

/// <param name="IndoorsRate">How fast the cold comes on inside a car's walls with a door open, against outside (spec B.2).</param>
/// <param name="PerColdStep">GDD §22 deep cold (note 183): each cold step where you are makes the cold climb this much faster.</param>
public sealed record ColdTuning(double OnsetSeconds, double DeathSeconds, double RecoverSecondsNearHeat, double OnsetSpeedScale, double IndoorsRate = 1,
    double PerColdStep = 0.25);
/// <summary>
/// GDD §22 wind on a roof (note 201): the wind across a moving train pushes whoever's up top sideways, in gusts from
/// either side. Field docs live in player.json <c>wind</c>.
/// </summary>
public sealed record WindTuning(double Drift = 0.3, double Still = 0.4, double Walking = 0.5, double GustMetres = 120);
/// <summary>
/// T128 (build 1121: "far too easy to fall off the train"; note 273): the edge of a roof (or a coupler plate) holds a player
/// walking at it, unless they mean to go over. Field docs live in player.json <c>edge</c>.
/// </summary>
/// <param name="WalkOff">
/// The director's 14 Oct playtest, line 4 (note 717): "I think we want to let people walk off the top of the cars". A roof's end
/// lets a walker off as its sides do (walking at it within <paramref name="StepOffDegrees"/> of straight off); false, only a jump,
/// a ladder or more of the train below takes you off an end.
/// </param>
public sealed record EdgeTuning(double Lip = 0.3, double StepOffDegrees = 30, double CatchDrop = 3.5, bool WindOverLip = false, bool WalkOff = false);
/// <summary>
/// Rejoining a night after a drop (note 253). Field docs live in player.json <c>rejoin</c>.
/// </summary>
public sealed record RejoinTuning(double ReserveSeconds = 180, bool ReclaimBody = true, double GreetSeconds = 1, int Retries = 5, double RetrySeconds = 3,
    double FollowSeconds = 600);
/// <summary>
/// The links in play (note 532). Field docs live in player.json <c>link</c>. A test's transports keep their own
/// <c>DatagramOptions</c>; these are the app's.
/// </summary>
public sealed record LinkTuning(double TimeoutSeconds = 15, double KeepaliveSeconds = 1, int PumpMilliseconds = 4,
    int PaceQueued = 4, double PaceSlow = 0.05, double PaceSeconds = 1, double HeldBackSeconds = 5,
    int ThinLossPct = 12, double ThinSeconds = 2, double ThinHoldSeconds = 20, int StrainCrew = 2, double StrainSeconds = 5);
/// <summary>
/// The crew cap (note 254). Field docs live in player.json <c>crew</c>. Never under one: the host's own player always fits.
/// </summary>
public sealed record CrewTuning(int Cap = 8, double RefuseLingerSeconds = 3)
{
    public int Places => Math.Max(1, Cap);
    /// <summary>
    /// GDD §33.1 (note 724): a human joining a night that bots crew takes a bot's place where it stands (its body, slots,
    /// health and post) rather than waiting for a stop or the Holdouts' queue, or being turned away from a full crew.
    /// </summary>
    public bool JoinTakesBotSeat { get; init; } = true;
    /// <summary>GDD §33.1 (note 724): putting a player off the run. Docs in player.json <c>crew.kick</c>.</summary>
    public KickTuning Kick { get; init; } = new();
}

/// <summary>
/// Note 724: a public lobby's vote to put a player off: open for <paramref name="VoteSeconds"/>, carried by more than
/// <paramref name="Majority"/> of the living humans aboard (the one voted on not counted).
/// </summary>
public sealed record KickTuning(double VoteSeconds = 30, double Majority = 0.5);
/// <summary>
/// How long each emote lasts (note 298), in seconds, unless its player moves first. Field docs live in player.json
/// <c>emotes</c>. Presentation only: the host keeps the event this long for the crew to see; nothing in the sim acts on it.
/// </summary>
/// <summary>Note 663: how long a line of news is up, and how fast the train runs before a crewmate's own cut is news.</summary>
public sealed record NewsTuning(double Seconds = 5, double CutToldAbove = 1.5);

public sealed record EmoteTuning(double Dance = 8, double Wave = 2.5, double Point = 2.5, double Cooldown = 0.5)
{
    public double Seconds(Emote e) => e switch { Emote.Dance => Dance, Emote.Wave => Wave, Emote.Point => Point, _ => 0 };
    public double Longest => Math.Max(Dance, Math.Max(Wave, Point));
}
/// <param name="LethalAbove">Speed over the ground on landing that kills (spec B.3 after T90); 0 for the train's jump-off band.</param>
/// <param name="DamageAtLethal">A landing's knock rises from <paramref name="RollDamage"/> to this at the lethal edge.</param>
/// <param name="FallHurtFrom">Note 573: a fall from at least this high (m, by the speed it lands at) knocks, from <paramref name="RollDamage"/>
/// rising to death at <paramref name="FallLethalFrom"/>; 0 for no fall knock.</param>
/// <param name="StandBelow">
/// The director's 14 Oct playtest, line 7 (note 717): off a train going slower than this (m/s), as off the ground's own footing,
/// a crewmate's own run is no knock (spec B.3's speed table is for leaving a moving train); 0 for the old rule.
/// </param>
public sealed record LandingTuning(double RollAbove, int RollDamage, double LethalAbove = 0, int DamageAtLethal = 0, double FallHurtFrom = 0,
    double FallLethalFrom = 0, double StandBelow = 0);
public sealed record LadderTuning(double GrabRange, double GrabMaxRelativeSpeed, double HatchLookUpDegrees = 25);
/// <summary>A VR player's reaching hand (T29). Field docs live in player.json.</summary>
public sealed record HandTuning(double Arm, double Overhead, double Grab);

/// <summary>Note 664: crouching. Mirror of player.json <c>crouch</c>, where each field is documented.</summary>
public sealed record CrouchTuning
{
    public double Height { get; init; } = 1.2;
    public double EyeDrop { get; init; } = 0.55;
    public double SpeedScale { get; init; } = 0.5;
    public double FootstepGainDb { get; init; } = -8;
    public double HeadsetBelow { get; init; } = 1.2;
}

/// <summary>Note 720: the speaking tube, singing together and lamp signals. Mirror of player.json <c>voices</c>.</summary>
public sealed record VoicesTuning
{
    public TubeTuning Tube { get; init; } = new();
    public SingingTuning Singing { get; init; } = new();
    public LampSignalTuning LampSignals { get; init; } = new();
}

/// <summary>The speaking tube from the cab to the guard van (note 720). Field docs live in player.json <c>voices.tube</c>.</summary>
public sealed record TubeTuning
{
    public double MouthReach { get; init; } = 1.0;
    public double HeardWithin { get; init; } = 10;
}

/// <summary>
/// Singing together (GDD §14; note 720; the director, 14 Oct 2026: "Singing does not fight the choir"). Field docs live in
/// player.json <c>voices.singing</c>.
/// </summary>
public sealed record SingingTuning
{
    public double WindowSeconds { get; init; } = 4;
    public double Earshot { get; init; } = 12;
    public int Floor { get; init; } = 40;
    public int Swing { get; init; } = 12;
    public double InTime { get; init; } = 0.6;
    public bool FeedsChoir { get; init; }
    public double CalmsLivestock { get; init; } = 0.6;
}

/// <summary>Lamp signals (GDD §12; note 720). Field docs live in player.json <c>voices.lampSignals</c>.</summary>
public sealed record LampSignalTuning
{
    public double Seconds { get; init; } = 3;
    public double Cooldown { get; init; } = 0.5;
    public double SeenWithin { get; init; } = 60;
    public double StandSeconds { get; init; } = 90;
    public double BackMetres { get; init; } = 40;
    public double BackSpeed { get; init; } = 1.5;
    public double StoppedWithin { get; init; } = 40;
}

