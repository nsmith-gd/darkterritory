using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// One death in a run (GDD App. D.2): whose, when, where along the main line, what did it, and the body it left. What the
/// incident report lists (D.12) and what settlement charges for (D.9).
/// </summary>
/// <param name="Body">The body it left (a <see cref="Physics.Body"/> id).</param>
/// <param name="Chainage">Main-line distance where they fell.</param>
/// <param name="Site">The Holdout site whose zone it was in (D.5 eligibility), if any.</param>
/// <param name="DropOut">A disconnect, not a death (D.2): an inert body with no fee and no refund.</param>
public sealed record DeathRecord(int Player, int Body, double Seconds, double Chainage, DeathCause Cause, string? Site, bool DropOut = false);

/// <summary>
/// A body as loot (D.9): the crew-loss fee its death costs the crew at settlement, what bringing it home refunds, and the
/// kit it kept (D.2 "it keeps everything the player was carrying"), which goes back to stores when it's delivered.
/// </summary>
public sealed record BodyRecord(int Body, int Owner, double Fee, double Refund, bool DropOut, IReadOnlyList<BodyKind> Kit)
{
    /// <summary>
    /// The record for a body: 0.5 of the tier's per-car value as its fee, 0.75 of that as its refund (holdouts.json), and
    /// neither for a drop-out's.
    /// </summary>
    public static BodyRecord For(HoldoutTuning t, double perCar, int body, int owner, bool dropOut, IReadOnlyList<BodyKind> kit)
    {
        // Whole scrip, halves up: D.9's table (a Frontier refund of 262.5 is 263).
        // And never all of it back (D.1 "recovering the body gets most of it back, never all of it"), rounding or not.
        double fee = dropOut ? 0 : Math.Round(t.FeeShare * perCar, MidpointRounding.AwayFromZero);
        double refund = fee <= 0 ? 0 : Math.Min(Math.Round(t.RefundShare * fee, MidpointRounding.AwayFromZero), fee - 1);
        return new BodyRecord(body, owner, fee, Math.Max(0, refund), dropOut, kit);
    }

    /// <summary>What a loot-seeking enemy ranks this body at (D.9 "a body is valued at its refund").</summary>
    public double LootValue => Refund;
}

/// <summary>
/// A player's character (App. D.8): the survivor they came out of a Holdout as, carried into later runs until they die and
/// are freed again. Indices into holdouts.json's survivor pools.
/// </summary>
public sealed record Character(int Appearance, int VoiceSet);
