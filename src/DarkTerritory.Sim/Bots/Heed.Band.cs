using System.Runtime.CompilerServices;
using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>m: how far along its own car's floor a bot goes for an instrument lying there, when the band strikes up.</summary>
    const double BandFetch = 8;

    /// <summary>Under this much stick, a bot's standing about (riding, waiting): free to play.</summary>
    const float Idle = 0.2f;

    // Each bot's Use last tick, and whether that's its hold on an instrument, by its world and id: a tap is a press and a
    // release; playing is a fresh press held (the press that took it up isn't one).
    static readonly ConditionalWeakTable<World, Dictionary<int, (bool Used, bool Playing)>> BandHands = new();

    /// <summary>
    /// The band (ARCHITECTURE §8 note 711; the director, 14 Oct 2026: "We do want bots to participate"): with the band playing,
    /// a bot with nothing else to do joins in. An instrument in its hands, it holds Use and plays; with empty hands, one lying
    /// on its car's floor within <see cref="BandFetch"/> m is walked to and taken up. When the band stops, or its job wants it
    /// (it's moving off, or its hands are wanted), it puts the instrument down where it is (a tap: a press and a release). Never
    /// the driver (who's never asked), never a bot busy with its hands.
    /// </summary>
    public static PlayerIntent Band(PlayerIntent intent, in PlayerState self, World world, int me)
    {
        var hands = BandHands.GetOrCreateValue(world);
        var (usedLastTick, holding) = hands.GetValueOrDefault(me);
        var result = BandIntent(intent, self, world, me, usedLastTick, ref holding);
        hands[me] = (result.Has(PlayerButtons.Use), holding && result.Has(PlayerButtons.Use));
        return result;
    }

    static PlayerIntent BandIntent(PlayerIntent intent, in PlayerState self, World world, int me, bool usedLastTick, ref bool holding)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || self.Has(PlayerFlags.Seated))
            return intent;
        bool band = world.Band.Playing;
        bool idle = intent.Buttons == PlayerButtons.None && intent.Actions == PlayerActions.None
            && Math.Abs(intent.MoveX) < Idle && Math.Abs(intent.MoveZ) < Idle;
        var carried = world.Bodies.CarriedBy(me);
        if (carried is { Kind: BodyKind.Instrument })
        {
            if (band && idle)
            {
                // Held from a fresh press: let go of the one that took it up first.
                if (holding || !usedLastTick)
                {
                    holding = true;
                    return intent with { Buttons = PlayerButtons.Use };
                }
                return intent;
            }
            holding = false;
            // Put it down: a fresh press, then its release before it's a hold. (Its own Use does that anyway.)
            if (intent.Has(PlayerButtons.Use))
                return intent;
            return usedLastTick ? intent : intent with { Buttons = intent.Buttons | PlayerButtons.Use };
        }
        if (!band || !idle || carried is not null)
            return intent;
        // Empty-handed and the band's on: an instrument within reach is taken up (a press), one along the floor walked to.
        if (world.Bodies.InReach(self, world.Train, world.Hand, playerId: me) is { Kind: BodyKind.Instrument })
            return usedLastTick ? intent : intent with { Buttons = PlayerButtons.Use };
        Body? near = null;
        double best = BandFetch;
        foreach (var b in world.Bodies.All)
            if (b is { Kind: BodyKind.Instrument, Carrier: < 0, Stowed: false } && b.Parent == self.Parent && self.Parent >= 0)
            {
                var d = b.Centre - self.Position;
                double flat = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                if (flat < best)
                    (best, near) = (flat, b);
            }
        if (near is null)
            return intent;
        var to = near.Centre - self.Position;
        return WarmUp.Steer(self, near.Centre, DMath.Atan2(-to.X, -to.Z)).Step;
    }
}
