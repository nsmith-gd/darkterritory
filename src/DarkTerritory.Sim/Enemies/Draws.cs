using Ballast;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// What a crew did that the dark can hear or see (GDD §14 "Noise", App. B.1; ARCHITECTURE §8 note 287): the whistle, a cannon
/// round, raised voices, a noisy toy carried, the firebox run hot, a car lamp lit, cargo taken aboard, and the engine itself at
/// speed. The order is the clerk's tie-break (the earlier wins a tie).
/// </summary>
public enum DrawCause : byte { None, Whistle, Cannon, Voices, Toy, Firebox, Lamp, Cargo, Engine }

/// <summary>
/// The night's first threat and what drew it (note 287), for the incident report (C.9) and the harness.
/// </summary>
/// <param name="Seconds">Run seconds it was sent.</param>
/// <param name="Cause">The draw it answered.</param>
/// <param name="Actor">Who made that draw, or −1.</param>
/// <param name="Amount">How much of that draw was standing in the ledger then.</param>
/// <param name="Answered">The dark answered the draw out loud first (it crossed <see cref="DrawTuning.AnswerAt"/>); false: the
/// pressure pressed with no draw that big, and the ledger's top draw takes it.</param>
/// <param name="AnsweredAt">Run seconds the answer was heard (equal to <paramref name="Seconds"/> when not answered).</param>
/// <param name="Distance">The train's distance along the line then.</param>
public readonly record struct FirstThreat(uint Tick, double Seconds, double Distance, EnemyKind Kind, DrawCause Cause, int Actor, double Amount, bool Answered,
    double AnsweredAt);

/// <summary>
/// The dark's answer to a draw (note 287), replicated on the world record so every machine hears and sees it: a distant
/// call from where it's heard (<see cref="At"/>), and a pair of eyes at the lamp's edge there while <see cref="Seconds"/>
/// runs down. Presentation only: nothing the sim predicts reads it.
/// </summary>
public readonly record struct DrawAnswer(double Seconds, DrawCause Cause, Double3 At, int Actor)
{
    public bool Showing => Seconds > 0;
}

/// <summary>
/// The draw ledger (note 287): each crewmate's draws by cause, decaying with <see cref="DrawTuning.HalfLifeSeconds"/>, so what
/// stands highest is what drew most, lately. Host-side, kept in key order (cause, then player id), so every read of it is
/// deterministic.
/// </summary>
public sealed class DrawLedger
{
    readonly SortedDictionary<(DrawCause Cause, int Actor), double> _draws = [];

    /// <summary>Credits <paramref name="actor"/> (−1 for nobody) with <paramref name="amount"/> of <paramref name="cause"/>.</summary>
    public void Add(DrawCause cause, int actor, double amount)
    {
        if (amount > 0 && cause != DrawCause.None)
            _draws[(cause, actor)] = _draws.GetValueOrDefault((cause, actor)) + amount;
    }

    /// <summary>Lets <paramref name="seconds"/> of the ledger fade (halving about every <paramref name="halfLife"/> s).</summary>
    public void Fade(double seconds, double halfLife)
    {
        if (halfLife <= 0 || _draws.Count == 0)
            return;
        // First-order (1 − ln 2 · dt / halfLife), not Math.Pow: plain arithmetic is the same bits on every OS, and at a second a
        // step it halves within a percent of halfLife.
        double keep = Math.Max(0, 1 - 0.6931471805599453 * seconds / halfLife);
        foreach (var key in _draws.Keys.ToList())
        {
            double left = _draws[key] * keep;
            if (left < 1e-4)
                _draws.Remove(key);
            else
                _draws[key] = left;
        }
    }

    /// <summary>The biggest draw standing (ties to the earlier cause, then the lower id), or none.</summary>
    public (DrawCause Cause, int Actor, double Amount) Top
    {
        get
        {
            (DrawCause, int, double) top = (DrawCause.None, -1, 0);
            foreach (var ((cause, actor), amount) in _draws)
                if (amount > top.Item3)
                    top = (cause, actor, amount);
            return top;
        }
    }

    /// <summary>What <paramref name="actor"/> has standing of <paramref name="cause"/>.</summary>
    public double Of(DrawCause cause, int actor) => _draws.GetValueOrDefault((cause, actor));

    /// <summary>Everything standing, in key order.</summary>
    public IEnumerable<(DrawCause Cause, int Actor, double Amount)> All => _draws.Select(d => (d.Key.Cause, d.Key.Actor, d.Value));

    /// <summary>The cause as the clerk says it: "the whistle", "a cannon shot".</summary>
    public static string Said(DrawCause cause) => cause switch
    {
        DrawCause.Whistle => "the whistle",
        DrawCause.Cannon => "the cannon",
        DrawCause.Voices => "raised voices",
        DrawCause.Toy => "a noisy toy",
        DrawCause.Firebox => "the firebox run hot",
        DrawCause.Lamp => "a lamp lit",
        DrawCause.Cargo => "the cargo taken aboard",
        DrawCause.Engine => "the engine's noise",
        _ => "nothing anyone did",
    };

    /// <summary>The cause's tuning name ("whistle", "cannon", …): the weights, the answers, and the answer's sound hook.</summary>
    public static string Key(DrawCause cause) => cause.ToString().ToLowerInvariant();
}

/// <summary>
/// The draw (note 287; enemies.json <c>director.draw</c>, where the field docs live): what the crew does that draws the
/// night's first threat, and how the dark answers it.
/// </summary>
public sealed record DrawTuning
{
    public bool Enabled { get; init; } = true;
    public double HalfLifeSeconds { get; init; } = 30;
    public double AnswerAt { get; init; } = 3;
    public bool HoldFirst { get; init; } = true;
    public double HoldUntil { get; init; } = 16;
    public double LeadSeconds { get; init; } = 5;
    public double ShowSeconds { get; init; } = 7;
    public double AnswerDistance { get; init; } = 60;
    public double AnswerLateral { get; init; } = 12;
    public double AnswerHeight { get; init; } = 0.7;
    public double VoiceFloor { get; init; } = 0.4;
    public double FireboxFrom { get; init; } = 4.5;
    public double EngineFullSpeed { get; init; } = 20;
    /// <summary>Each cause's draw: per second (whistle, voices, toy, firebox, engine) or per act (cannon, lamp, cargo per car-load).</summary>
    public Dictionary<string, double> Weights { get; init; } = new();
    /// <summary>What comes on more for each cause's answer: cause → (kind's tuning name) → weight.</summary>
    public Dictionary<string, Dictionary<string, double>> Answers { get; init; } = new();

    public double Weight(DrawCause cause) => Weights.GetValueOrDefault(DrawLedger.Key(cause), 0);
}
