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

    /// <summary>The emotes' lengths (GDD §9's yard, "dance"; note 298). Field docs live in player.json <c>emotes</c>.</summary>
    public EmoteTuning Emotes { get; init; } = new();

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
public sealed record EdgeTuning(double Lip = 0.3, double StepOffDegrees = 30, double CatchDrop = 3.5, bool WindOverLip = false);
/// <summary>
/// Rejoining a night after a drop (note 253). Field docs live in player.json <c>rejoin</c>.
/// </summary>
public sealed record RejoinTuning(double ReserveSeconds = 180, bool ReclaimBody = true, double GreetSeconds = 1, int Retries = 5, double RetrySeconds = 3);
/// <summary>
/// The crew cap (note 254). Field docs live in player.json <c>crew</c>. Never under one: the host's own player always fits.
/// </summary>
public sealed record CrewTuning(int Cap = 8, double RefuseLingerSeconds = 3)
{
    public int Places => Math.Max(1, Cap);
}
/// <summary>
/// How long each emote lasts (note 298), in seconds, unless its player moves first. Field docs live in player.json
/// <c>emotes</c>. Presentation only: the host keeps the event this long for the crew to see; nothing in the sim acts on it.
/// </summary>
public sealed record EmoteTuning(double Dance = 8, double Wave = 2.5, double Point = 2.5, double Cooldown = 0.5)
{
    public double Seconds(Emote e) => e switch { Emote.Dance => Dance, Emote.Wave => Wave, Emote.Point => Point, _ => 0 };
    public double Longest => Math.Max(Dance, Math.Max(Wave, Point));
}
/// <param name="LethalAbove">Speed over the ground on landing that kills (spec B.3 after T90); 0 for the train's jump-off band.</param>
/// <param name="DamageAtLethal">A landing's knock rises from <paramref name="RollDamage"/> to this at the lethal edge.</param>
public sealed record LandingTuning(double RollAbove, int RollDamage, double LethalAbove = 0, int DamageAtLethal = 0);
public sealed record LadderTuning(double GrabRange, double GrabMaxRelativeSpeed);
/// <summary>A VR player's reaching hand (T29). Field docs live in player.json.</summary>
public sealed record HandTuning(double Arm, double Overhead, double Grab);
