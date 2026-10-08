namespace DarkTerritory.Sim.Combat;

/// <summary>Mirror of content/tuning/combat.json. Field docs live in that file.</summary>
public sealed record CombatTuning(GunTuning Guns, ChoirTuning Choir)
{
    public const string File = "tuning/combat.json";
    /// <summary>How long hits and cannonball impacts stay replicated for every client to show (T121).</summary>
    public HitTuning Hits { get; init; } = new();
    /// <summary>A cannon fired beside a chemicals car (combat.json <c>fumes</c>; GDD App. B.9; note 182). Unset, nothing.</summary>
    public FumesTuning? Fumes { get; init; }
}

/// <summary>
/// combat.json <c>fumes</c> (App. B.9: "firing a cannon near chemical cars is lethal to the crew"): a shot from a gun on a
/// loaded chemicals car, or within <see cref="Cars"/> cars of one, sets its fumes off; everyone within <see cref="GasM"/> of
/// that car, and the gunner, takes <see cref="Damage"/>.
/// </summary>
public sealed record FumesTuning(int Cars = 1, double GasM = 8, int Damage = 150);

/// <summary>
/// The crude cannons (GDD v1.1 §12, App. C.3): arc-limited, loud, and every shot a timed decision. After each shot a full
/// manual reload (powder, ball, ram) at the gun before it fires again. Field docs live in combat.json.
/// </summary>
public sealed record GunTuning(double RoundsPerSecond, double Range, double TraverseDegrees, double DeadZoneDegrees,
    double MinPitchDegrees, double MaxPitchDegrees, int Ammo, double Reach, double DamagePerRound, double MinPressure)
{
    public int TicksPerRound => (int)Math.Round(SimConstants.TickRate / RoundsPerSecond);
    /// <summary>The reload's steps: powder, ball, ram (0: the old magazine gun, no reload).</summary>
    public int ReloadSteps { get; init; } = 3;
    /// <summary>Seconds of Use held at the gun per step.</summary>
    public double ReloadStepSeconds { get; init; } = 1.5;
    /// <summary>How far behind the pivot the gunner's seat is (T112).</summary>
    public double SeatBehind { get; init; } = 0.75;
    /// <summary>How fast the seated gunner can turn the carriage, and lift or drop the barrel (T112).</summary>
    public double TraverseDegreesPerSecond { get; init; } = 70;
    public double ElevateDegreesPerSecond { get; init; } = 35;
    /// <summary>A bot fires once the barrel's within this of its mark.</summary>
    public double LaidDegrees { get; init; } = 2.5;
    /// <summary>How finely a ball's path is searched for the ground, water or a wall it lands on (m; T121).</summary>
    public double ImpactStep { get; init; } = 0.5;
    /// <summary>
    /// The ready rack at the gun (note 374, orchestrator.md §5.1 U4): how many of its rounds it holds; the rest are in the
    /// powder lockers. 0: no rack, the gun's whole stock is at it (the gun before note 374).
    /// </summary>
    public int Rack { get; init; }
    /// <summary>Seconds of Use held at the gun with a charge in hand to fill its rack.</summary>
    public double ChargeSeconds { get; init; } = 2;
    /// <summary>How near the powder locker a crewmate's hands must be to take a charge from it (m).</summary>
    public double LockerReach { get; init; } = 1.3;
    /// <summary>
    /// Bots bring the powder (note 377, orchestrator.md §5.1 U4): a manned gun's ready rack down to this many rounds or fewer
    /// sends a walker for a charge, so it's up before the gunner fires the last. -1: no walker goes (the gunner fetches its own).
    /// </summary>
    public int FeedAt { get; init; } = 2;
    /// <summary>
    /// <see cref="FeedAt"/> for a gun on another car than the powder locker's (the engine's forward gun): a walk the length of
    /// the train each way, so the walker goes sooner.
    /// </summary>
    public int FeedAtFar { get; init; } = 5;
    /// <summary>
    /// A gunner bot whose rack's run dry keeps its seat this long (s) for the walker bringing a charge (note 377), then goes
    /// for its own: up out of the seat, the gun's unmanned and no walker's sent to it.
    /// </summary>
    public double GunnerWaits { get; init; } = 30;
    /// <summary>
    /// A gun that's fired in the last this many seconds is in action (note 377): any round short of a full rack sends a walker
    /// for a charge, to stand by at the gun with it, so a wave doesn't wait on a walk the train's length.
    /// </summary>
    public double FeedWhileFiring { get; init; } = 20;
    /// <summary>
    /// A bot crew this big or bigger (bots and all) has a second gunner on the engine's forward gun (note 414), for the lane
    /// ahead (note 405), the Track Doll and the Switchman: its last place. 0: never.
    /// </summary>
    public int ForwardGunnerFrom { get; init; } = 6;
    /// <summary>
    /// GDD §23 "gun jams: someone repairs it by hand, under fire" (note 183): the chance a shot fouls the bore, times
    /// <see cref="FoulWetFactor"/> on wet rail (rain); then <see cref="ClearSeconds"/> of Use held at the gun clears it.
    /// </summary>
    public double FoulChance { get; init; } = 0.04;
    public double FoulWetFactor { get; init; } = 2.5;
    public double ClearSeconds { get; init; } = 4;
    /// <summary>GDD §22 wind (note 183): "gunfire carries much further": a round's loudness grows this much per unit of wind.</summary>
    public double WindLoudness { get; init; } = 0.6;
}

/// <summary>The loudness meter and the Choir it draws (GDD v1.1 App. A.7, C.7). Field docs live in combat.json.</summary>
public sealed record ChoirTuning
{
    public double WindowSeconds { get; init; } = 4;
    public double VoicePerPlayer { get; init; } = 0.45;
    public double RoundLoudness { get; init; } = 1.2;
    public double WhistleLoudness { get; init; } = 0.9;
    public double MachineryLoudness { get; init; } = 0.35;
    public double Threshold { get; init; } = 0.6;
    public double BuildSeconds { get; init; } = 40;
    public double QuietDecayPerSecond { get; init; } = 0.06;
    public double MaxLoudness { get; init; } = 3;
    public double LivestockFloor { get; init; } = 0.3;
    /// <summary>T113: seconds after it disperses (driven off quiet) before it can begin to gather again.</summary>
    public double RestSeconds { get; init; } = 300;
    /// <summary>A noisy toy in someone's hands (App. C.7), by its noise: how much it adds to the meter, in the carrier's name.</summary>
    public NoisyToyTuning Toys { get; init; } = new();
}

/// <summary>combat.json <c>choir.toys</c>: each noisy toy's loudness while carried (GDD v1.4 App. C item 4, C.7).</summary>
public sealed record NoisyToyTuning
{
    public double Squeaker { get; init; } = 0.2;
    public double MusicBox { get; init; } = 0.15;
    public double Drummer { get; init; } = 0.3;

    public double Of(Physics.ToyNoise noise) => noise switch
    {
        Physics.ToyNoise.Squeaker => Squeaker,
        Physics.ToyNoise.MusicBox => MusicBox,
        Physics.ToyNoise.Drummer => Drummer,
        _ => 0,
    };
}
