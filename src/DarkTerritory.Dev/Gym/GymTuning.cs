namespace DarkTerritory.Dev.Gym;

/// <summary>
/// The creature gym's own numbers (content/tuning/gym.json; GDD §34.1, ARCHITECTURE §8 note 676). A developer build's only:
/// nothing on a real night reads them.
/// </summary>
public sealed record GymTuning
{
    public const string File = "tuning/gym.json";

    /// <summary>The line the gym stands on (tier:seed), and the train's length.</summary>
    public string Route { get; init; } = "frontier:7";
    public int Cars { get; init; } = 6;
    /// <summary>The kind of stop it stands at (stops' <c>StopKind</c>, camel-cased): the first of it on the line.</summary>
    public string Stop { get; init; } = "yardAndVillage";
    /// <summary>
    /// The engine stands this far short of the stop's first points (m), so the Switchman has points ahead to work (the
    /// director, 13 Oct, GDD App. F.5); 0 stands the train's middle beside the village.
    /// </summary>
    public double PointsAhead { get; init; } = 60;
    /// <summary>Nothing called comes nearer you than this (m), nor than its own reach.</summary>
    public double MinDistance { get; init; } = 45;
    /// <summary>And nothing farther than this (m): brought in, or only chosen within it.</summary>
    public double MaxDistance { get; init; } = 200;
    /// <summary>Seconds from the killing blow to standing up again.</summary>
    public double RespawnSeconds { get; init; } = 4;
    public RingTuning SafeRing { get; init; } = new();
    public LampsTuning Lamps { get; init; } = new();
    /// <summary>The gym's light and air, over the night's (look.json's "atmosphere"): what it sets, it sets.</summary>
    public GymLook Look { get; init; } = new();
    /// <summary>The weather's fog, thicker than the line's own by this much.</summary>
    public double FogScale { get; init; } = 1;
    /// <summary>What the panel says of each kind (by its enemies.json name): where it comes from, and what answers it.</summary>
    public Dictionary<string, KindNote> Notes { get; init; } = [];

    public sealed record RingTuning
    {
        /// <summary>From the track's centre, beside the engine, on the side away from the village (m).</summary>
        public double Out { get; init; } = 9;
        public double Radius { get; init; } = 5;
    }

    public sealed record LampsTuning
    {
        public int Count { get; init; } = 8;
        /// <summary>How far out from the track's centre (m), least and most.</summary>
        public double[] Out { get; init; } = [6, 14];
    }

    /// <summary>Each value set replaces the night's own in the frame's lighting; one left out keeps it.</summary>
    public sealed record GymLook
    {
        public float? FogHeightFalloff { get; init; }
        public float? FogFloor { get; init; }
        public float? FogCurve { get; init; }
        public float[]? FogColour { get; init; }
        public float[]? MoonDirection { get; init; }
        public float[]? MoonColour { get; init; }
        public float? MoonStrength { get; init; }
        public float? Ambient { get; init; }
        public float[]? LampColour { get; init; }
        public float? LampIntensity { get; init; }
    }

    public sealed record KindNote(string From = "", string Answer = "");

    public static GymTuning Load(string content) => Ballast.DataFile.Load<GymTuning>(Path.Combine(content, File));
}
