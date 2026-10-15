using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// What the railway left beside its line (GDD App. F.3, the director: "the same three things over and over again"; note 325,
/// E1's): the gangs' stacks of ties and rail, their hut, drums, a cable reel, a milepost, a dead signal, a wheelset off a
/// wreck, a trolley tipped off the rails, a heap of spilt coal, a camp gone cold, a grave, dumped stores and freight, a
/// sandbagged post. One thing to a stretch of the line, dealt from a shuffled deck of every kind, so along a deck's length no
/// kind comes twice. The sim deals them (note 792) so they stand solid where they're drawn, as the trees and props do (notes
/// 371, 389); a stop's yard piles them about its buildings from the same kinds and clusters (the art's YardLeavings).
/// </summary>
public static class Leavings
{
    /// <summary>A stretch of the line, one leaving to it at most: about one thing beside the line to a train's length.</summary>
    public const double Every = 85;
    /// <summary>How many stretches have something in them.</summary>
    public const double Chance = 0.8;
    /// <summary>How many cards either side of a join between decks are kept apart.</summary>
    const int Join = 4;
    /// <summary>Nothing's left in the first stretches out of the departure yard.</summary>
    const double FirstM = 400;

    /// <summary>Which way a leaving's laid: any way, facing the line (a hut's door, a grave's cross), or along it.</summary>
    public enum Facing : byte { Any, Line, Along }

    /// <summary>A kind: its name, how far out from the line it lies (m), and which way it faces.</summary>
    public readonly record struct Kind(string Name, double Near, double Far, Facing Facing);

    /// <summary>Every kind (E1's, note 325).</summary>
    public static readonly Kind[] Kinds =
    [
        new("ties", 3.8, 7, Facing.Along),
        new("rails", 3.6, 5, Facing.Along),
        new("drums", 4, 9, Facing.Any),
        new("reel", 5, 9, Facing.Any),
        new("milepost", 3.2, 3.5, Facing.Along),
        new("hut", 6.5, 10, Facing.Line),
        new("wheelset", 4, 8, Facing.Any),
        new("coal", 3.8, 6, Facing.Any),
        new("camp", 8, 14, Facing.Any),
        new("trolley", 3.5, 4.5, Facing.Along),
        new("grave", 5.5, 9, Facing.Along),
        new("stores", 4, 8, Facing.Any),
        new("freight", 4, 7, Facing.Any),
        new("signal", 3.4, 3.8, Facing.Along),
        new("sandbags", 6, 10, Facing.Line),
    ];

    /// <summary>A leaving dealt to a stretch: where along and out from the line, what, its variant and the way it's turned.</summary>
    public readonly record struct Dealt(int Slot, double Along, double Lateral, string Kind, int Variant, double Yaw);

    /// <summary>
    /// A piece of a leaving, by the name the lineside's footprints and the art know it by (content/linegen/footprints.json,
    /// LinesideFootprints): its variant, where it lies off the leaving's spot in the leaving's own frame (x across, z along,
    /// before it's turned), how far it's lifted (a crate on a crate), and its own turn on the leaving's.
    /// </summary>
    public readonly record struct Piece(string Name, int Variant, double X, double Z, double Lift, double Turn);

    static readonly string[] Goods = ["freightTimber", "freightSacks", "freightCarboys"];

    /// <summary>A kind's pieces for variant <paramref name="v"/> (0-5): one, or a little cluster.</summary>
    public static IEnumerable<Piece> Cluster(string kind, int v)
    {
        switch (kind)
        {
            case "ties":
                yield return new("junkTies", v % 3, 0, 0, 0, 0);
                break;
            case "rails":
                yield return new("junkRails", v % 2, 0, 0, 0, 0);
                break;
            case "drums":
                yield return new("junkDrums", v % 3, 0, 0, 0, 0);
                break;
            case "reel":
                yield return new("junkReel", 0, 0, 0, 0, 0);
                if (v % 2 == 0)
                    yield return new("junkDrums", 0, 1.6, 0.8, 0, 1.1);
                break;
            case "milepost":
                yield return new("junkMilepost", v % 2, 0, 0, 0, 0);
                break;
            case "hut":
                yield return new("junkHut", v % 2, 0, 0, 0, 0);
                yield return new("junkTies", v % 3, 2.8, 0.6, 0, Math.PI / 2);
                break;
            case "wheelset":
                yield return new("junkWheelset", 0, 0, 0, -0.12, 0);
                break;
            case "coal":
                yield return new("junkCoal", v % 2, 0, 0, 0, 0);
                break;
            case "camp":
                yield return new("junkCamp", v % 2, 0, 0, 0, 0);
                yield return new("storesCrate", 0, 1.3, -0.6, 0.35, 0.4);
                break;
            case "trolley":
                yield return new("junkTrolley", 0, 0, 0, 0, 0);
                break;
            case "grave":
                yield return new("junkGrave", v % 2, 0, 0, 0, 0);
                if (v >= 3)
                    yield return new("junkGrave", (v + 1) % 2, 1.2, 0.2, 0, 0.08);
                break;
            case "stores":
                // Dumped off a train in a hurry: a heavy crate, a stores crate on it, another fallen by.
                yield return new("heavyCrate", 0, 0, 0, 0.44, 0.15);
                yield return new("storesCrate", 0, 0.1, 0, 0.89 + 0.36, 0.5);
                yield return new("storesCrate", 0, 1.4, 0.7, 0.35, 1.2);
                break;
            case "freight":
                yield return new(Goods[v % 3], 0, 0, 0, 0.44, 0.3);
                yield return new(Goods[(v + 1) % 3], 0, 1.0, 0.4, 0.44, 0.9);
                if (v % 2 == 1)
                    yield return new(Goods[v % 3], 0, 0.3, 1.1, 0.44, 2.0);
                break;
            case "signal":
                // A signal nobody works any more, its arm dropped (not a mail crane: those are the sim's, note 149).
                yield return new("deadSignal", 0, 0, 0, 0, 0);
                break;
            case "sandbags":
                // An old checkpoint: a sandbagged wall, its ends turned in, drums behind.
                yield return new("sandbags", 0, 0, 0, 0, 0);
                yield return new("sandbags", 0, -1.7, -1.2, 0, Math.PI / 2);
                yield return new("junkDrums", 1, 0.6, -1.6, 0, 0);
                break;
        }
    }

    /// <summary>Where a piece of a leaving laid at <paramref name="yaw"/> lies off its spot (m, across and along the line): its offset turns with it.</summary>
    public static (double Across, double Along) Offset(Piece piece, double yaw)
    {
        double c = DMath.Cos(yaw), s = DMath.Sin(yaw);
        // As System.Numerics turns about y: x' = x cos + z sin, z' = z cos - x sin; the line's along runs down −z.
        return (piece.X * c + piece.Z * s, -(piece.Z * c - piece.X * s));
    }

    /// <summary>
    /// Where the leavings lie between <paramref name="from"/> and <paramref name="to"/>, before the line's own ground
    /// (tunnels, bridges, stops, branches) clears any: the stretch, the spot along and out from the line, the kind, its
    /// variant and the way it's turned. The same for the same seed on every machine.
    /// </summary>
    public static IEnumerable<Dealt> Along(double from, double to, ulong seed)
    {
        for (double s = Math.Ceiling(from / Every) * Every; s < to; s += Every)
        {
            int slot = (int)Math.Round(s / Every);
            if (s < FirstM || Streams.Rng(seed, "leavings", "", slot).NextDouble() > Chance)
                continue;
            yield return Of(slot, seed);
        }
    }

    /// <summary>The leaving dealt to stretch <paramref name="slot"/>: one card from its deck (every kind once, shuffled per deck).</summary>
    static Dealt Of(int slot, ulong seed)
    {
        int n = Kinds.Length;
        var deck = Deck(slot / n, seed);
        // Across the join: a deck's first few cards aren't any of the last few the deck before ended on. (Swapped only with
        // cards from its middle, so every deck's tail is as it was dealt and the deck before's is known.)
        if (slot / n > 0)
        {
            var tail = Deck(slot / n - 1, seed)[^Join..];
            for (int i = 0, j = Join; i < Join; i++)
                if (tail.Contains(deck[i]))
                {
                    while (tail.Contains(deck[j]))
                        j++;
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                    j++;
                }
        }
        var kind = Kinds[deck[slot % n]];
        var rng = Streams.Rng(seed, "leaving", "", slot);
        int side = rng.NextDouble() < 0.5 ? -1 : 1;
        double along = slot * Every + (rng.NextDouble() - 0.5) * Every * 0.5;
        double lateral = side * (kind.Near + rng.NextDouble() * (kind.Far - kind.Near));
        int variant = Math.Min(5, (int)(rng.NextDouble() * 6));
        // Facing the line (a hut's door, a grave's cross): +z of the piece toward the rails, from either side.
        double face = side > 0 ? -Math.PI / 2 : Math.PI / 2, turn = rng.NextDouble();
        double yaw = kind.Facing switch
        {
            Facing.Line => face + (turn - 0.5) * 0.3,
            Facing.Along => (turn - 0.5) * 0.2,
            _ => turn * Math.Tau,
        };
        return new(slot, along, lateral, kind.Name, variant, yaw);
    }

    static int[] Deck(int index, ulong seed)
    {
        var deck = Enumerable.Range(0, Kinds.Length).ToArray();
        var rng = Streams.Rng(seed, "leavings-deck", "", index);
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = Math.Min(i, (int)(rng.NextDouble() * (i + 1)));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }
}
