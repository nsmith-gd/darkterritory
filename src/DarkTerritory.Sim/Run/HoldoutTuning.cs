namespace DarkTerritory.Sim.Run;

/// <summary>Where a Holdout stands (GDD App. D.4): a facility pad, a halt, or a dead town's station.</summary>
public enum HoldoutSiteKind : byte { Facility, Halt, DeadTown }

/// <summary>D.4 "types": a derelict penal transport, a wildlander's barricaded hideout, a caged waiting room.</summary>
public enum HoldoutType : byte { PrisonCar, BarricadedShelter, HaltLockup }

/// <summary>D.7 "breach": smash the lock or pry the barricade with a melee tool, or open the lock with the repair kit.</summary>
public enum BreachMethod : byte { Smash, Pry, Open }

/// <summary>What a breach needs in the hands (D.7): any of the train's melee tools, or the repair kit.</summary>
public enum BreachTool : byte { Melee, RepairKit }

/// <summary>Mirror of content/tuning/holdouts.json (App. D.13). Field docs live in that file.</summary>
public sealed record HoldoutTuning
{
    public const string File = "tuning/holdouts.json";

    public required HoldoutPlacement Placement { get; init; }
    public int SecondCrew { get; init; }
    public double ReleaseM { get; init; }
    public required CallOutTuning CallOut { get; init; }
    public required BreachTuning Breach { get; init; }
    public required Dictionary<HoldoutType, BreachMethod[]> Methods { get; init; }
    public int FreedHealth { get; init; }
    public double FeeShare { get; init; }
    public double RefundShare { get; init; }
    public double SoloClimb { get; init; }
    public required VoteTuning Vote { get; init; }
    public required CommendationTuning Commendations { get; init; }
    public required SurvivorPools Survivors { get; init; }

    /// <summary>The breach a method is (D.7's table).</summary>
    public BreachStep Step(BreachMethod m) => m switch
    {
        BreachMethod.Smash => Breach.Smash,
        BreachMethod.Pry => Breach.Pry,
        _ => Breach.Open,
    };

    /// <summary>
    /// D.13's ranges, and the one rule that keeps death unprofitable: the body refund stays under the fee it refunds
    /// (D.9 "every death costs the crew more than the body returns"). Throws on content that breaks them, so a bad edit
    /// is refused at load (and hot reload keeps the last good file) rather than played.
    /// </summary>
    public HoldoutTuning Validate()
    {
        var bad = new List<string>();
        void Range(string name, double v, double lo, double hi)
        {
            if (!(v >= lo && v <= hi))
                bad.Add($"{name} {v} is outside {lo}-{hi}");
        }
        var p = Placement;
        if (p.FacilityFromConsistM is not [var near, var far] || near <= 0 || far < near)
            bad.Add("placement.facilityFromConsistM must be [near, far]");
        if (p.LampHeightM is not [var low, var high] || low <= 0 || high < low)
            bad.Add("placement.lampHeightM must be [low, high]");
        Range("secondCrew", SecondCrew, 4, 6);
        Range("releaseM", ReleaseM, 250, 600);
        Range("callOut.activeRadiusM", CallOut.ActiveRadiusM, 150, 300);
        Range("callOut.audibleM", CallOut.AudibleM, 40, 80);
        Range("callOut.cooldownSeconds", CallOut.CooldownSeconds, 5, 10);
        Range("soloClimb", SoloClimb, 0.3, 0.6);
        Range("freedHealth", FreedHealth, 1, 100);
        Range("feeShare", FeeShare, 0, double.MaxValue);
        // D.13 "body refund: must stay < 1.0".
        if (!(RefundShare >= 0 && RefundShare < 1))
            bad.Add($"refundShare {RefundShare} must stay under 1.0 (D.9: a recovered death always costs the crew)");
        if (Vote.PerVote < 1 || Vote.Cap < 1 || Vote.PerRun < 1)
            bad.Add("vote multipliers are at least x1 and perRun at least 1");
        if (Commendations.PerPlayer < 1 || Commendations.Awards.Length == 0)
            bad.Add("commendations need a per-player allowance and at least one award");
        foreach (HoldoutType t in Enum.GetValues<HoldoutType>())
        {
            if (!Methods.TryGetValue(t, out var ms) || ms.Length == 0)
                bad.Add($"methods.{t} is missing");
            if (!p.Size.TryGetValue(t, out var size) || size.Length != 3 || size.Any(x => x <= 0))
                bad.Add($"placement.size.{t} must be [halfWidth, halfLength, height]");
        }
        if (Survivors.Appearances.Length == 0 || Survivors.VoiceSets.Length == 0)
            bad.Add("survivors need appearances and voice sets");
        // D.7 "holdout occupants are always adults ... never a child": a child calling for help is always the Soot
        // Children's roll, never a Holdout, so the two can't be confused.
        foreach (var v in Survivors.VoiceSets)
        {
            if (!v.Adult)
                bad.Add($"voice set {v.Id} isn't an adult's: Holdout occupants are always adults (D.7)");
            if (v.Sounds.Length == 0)
                bad.Add($"voice set {v.Id} has no sounds");
        }
        if (bad.Count > 0)
            throw new InvalidDataException($"{File}: " + string.Join("; ", bad));
        return this;
    }
}

/// <summary>D.4 "where they are", as the line generator places them. Field docs in holdouts.json <c>placement</c>.</summary>
public sealed record HoldoutPlacement
{
    public double[] FacilityFromConsistM { get; init; } = [];
    public double HaltFromMainM { get; init; }
    public double TownFromMainM { get; init; }
    public double SecondPadScaleM { get; init; }
    public string[] SecondAtKinds { get; init; } = [];
    public double SecondApartM { get; init; }
    public double TrackClearanceM { get; init; }
    public double RouteClearanceM { get; init; }
    public double BoardEyeM { get; init; }
    public double[] LampHeightM { get; init; } = [];
    public double WalkStepM { get; init; }
    public int Tries { get; init; }
    public string[] SpareSidingAt { get; init; } = [];
    public string[] PrisonCarPreferredAt { get; init; } = [];
    public string[] ShelterAlwaysAt { get; init; } = [];
    public required PrisonCarChance PrisonCarChance { get; init; }
    public required Dictionary<HoldoutType, double[]> Size { get; init; }
}

public sealed record PrisonCarChance(double Preferred, double Other);

public sealed record CallOutTuning(double ActiveRadiusM, double AudibleM, double CooldownSeconds);

/// <summary>One way in (D.7): what it takes in the hands, how long the hold is, and how loud (combat.json loudness levels).</summary>
public sealed record BreachStep(BreachTool Tool, double Seconds, string Loudness);

public sealed record BreachTuning(BreachStep Smash, BreachStep Pry, BreachStep Open, double ReachM);

public sealed record VoteTuning(double PerVote, double Cap, int PerRun)
{
    /// <summary>D.11: the multiplier <paramref name="votes"/> votes give a creature, capped.</summary>
    public double Multiplier(int votes) => votes <= 0 ? 1 : Math.Min(Cap, Math.Pow(PerVote, votes));
}

public sealed record CommendationTuning(int PerPlayer, string[] Awards);

public sealed record SurvivorAppearance(string Id, string Kind);

/// <summary>An occupant's voice (D.7): the sounds its Call Out plays. Always an adult's.</summary>
public sealed record SurvivorVoiceSet(string Id, string Kind, bool Adult, string[] Sounds);

public sealed record SurvivorPools(SurvivorAppearance[] Appearances, SurvivorVoiceSet[] VoiceSets);
