using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class WorldArt
{
    /// <summary>
    /// What the railway left beside its line (GDD App. F.3, the director: "the same three things over and over again";
    /// note 325). Between the poles, the trees and the tufts: the gangs' stacks of ties and rail, their hut, drums, a cable
    /// reel, a milepost, a dead signal, a wheelset off a wreck, a trolley tipped off the rails, a heap of spilt coal, a camp
    /// gone cold, a grave, dumped stores and freight, a sandbagged post. One thing to a stretch of the line, dealt from a
    /// shuffled deck of every kind, so along a deck's length (<see cref="LeavingsDeck"/> stretches) no kind comes twice.
    /// </summary>
    void Leavings(MeshBuilder mesh, double from, double to, int seed, Func<double, bool> clear, Func<double, double, bool> onBranch,
        Func<double, double, float, float, float, Matrix4x4> place)
    {
        foreach (var l in LeavingsAlong(from, to, seed))
        {
            if (!clear(l.Slot * LeavingsEvery) || onBranch(l.Along, l.Lateral) || onBranch(l.Along, l.Lateral + Math.Sign(l.Lateral) * 3))
                continue;
            foreach (var (piece, offset, lift, turn) in Leaving(l.Kind, l.Variant))
            {
                if (piece is null)
                    continue;
                // The offset turns with the piece: a cluster keeps its shape whichever way it's laid.
                var o = Vector3.Transform(offset, Matrix4x4.CreateRotationY(l.Yaw));
                var m = place(l.Along - o.Z, l.Lateral + o.X, l.Yaw + turn, 1, 0.03f);
                mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateTranslation(0, lift, 0) * m));
            }
        }
    }

    /// <summary>
    /// Where the leavings lie between <paramref name="from"/> and <paramref name="to"/>, before the line's own ground
    /// (tunnels, bridges, stops, branches) clears any: the stretch, the spot along and out from the line, the kind, its
    /// variant and the way it's turned.
    /// </summary>
    public static IEnumerable<(int Slot, double Along, double Lateral, string Kind, int Variant, float Yaw)> LeavingsAlong(double from, double to, int seed)
    {
        for (double s = Math.Ceiling(from / LeavingsEvery) * LeavingsEvery; s < to; s += LeavingsEvery)
        {
            int slot = (int)Math.Round(s / LeavingsEvery);
            if (s < 400 || Hash(slot * 0.917f + seed * 0.31f) > LeavingsChance)
                continue;
            yield return Leaving(slot, seed);
        }
    }

    /// <summary>The leaving dealt to stretch <paramref name="slot"/>: one card from its deck (every kind once, shuffled per deck).</summary>
    static (int Slot, double Along, double Lateral, string Kind, int Variant, float Yaw) Leaving(int slot, int seed)
    {
        int n = LeavingKinds.Length;
        var deck = Deck(slot / n, seed);
        // Across the join: a deck's first few cards aren't any of the last few the deck before ended on. (Swapped only
        // with cards from its middle, so every deck's tail is as it was dealt and the deck before's is known.)
        if (slot / n > 0)
        {
            var tail = Deck(slot / n - 1, seed)[^LeavingsJoin..];
            for (int i = 0, j = LeavingsJoin; i < LeavingsJoin; i++)
                if (tail.Contains(deck[i]))
                {
                    while (tail.Contains(deck[j]))
                        j++;
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                    j++;
                }
        }
        var kind = LeavingKinds[deck[slot % n]];
        int side = Hash(slot * 1.71f + seed) < 0.5f ? -1 : 1;
        double along = slot * LeavingsEvery + (Hash(slot * 2.39f) - 0.5) * LeavingsEvery * 0.5;
        double lat = side * (kind.Near + Hash(slot * 3.13f) * (kind.Far - kind.Near));
        int v = (int)(Hash(slot * 4.41f + seed) * 6);
        // Facing the line (a hut's door, a grave's cross): +Z of the piece toward the rails, from either side.
        float face = side > 0 ? -MathF.PI / 2 : MathF.PI / 2;
        float yaw = kind.Facing switch
        {
            Facing.Line => face + (Hash(slot * 5.9f) - 0.5f) * 0.3f,
            Facing.Along => (Hash(slot * 5.9f) - 0.5f) * 0.2f,
            _ => Hash(slot * 5.9f) * MathF.Tau,
        };
        return (slot, along, lat, kind.Name, v, yaw);
    }

    static int[] Deck(int index, int seed)
    {
        var deck = Enumerable.Range(0, LeavingKinds.Length).ToArray();
        new Random(unchecked(seed * 92821 ^ index * 68917)).Shuffle(deck);
        return deck;
    }

    /// <summary>A stretch of the line, one leaving to it at most: about one thing beside the line to a train's length.</summary>
    const double LeavingsEvery = 85;
    /// <summary>How many stretches have something in them.</summary>
    const float LeavingsChance = 0.8f;
    /// <summary>How many cards either side of a join between decks are kept apart.</summary>
    const int LeavingsJoin = 4;

    enum Facing { Any, Line, Along }

    readonly record struct LeavingKind(string Name, float Near, float Far, Facing Facing);

    /// <summary>Every kind, how far out from the line it lies, and which way it faces.</summary>
    static readonly LeavingKind[] LeavingKinds =
    [
        new("ties", 3.8f, 7, Facing.Along),
        new("rails", 3.6f, 5, Facing.Along),
        new("drums", 4, 9, Facing.Any),
        new("reel", 5, 9, Facing.Any),
        new("milepost", 3.2f, 3.5f, Facing.Along),
        new("hut", 6.5f, 10, Facing.Line),
        new("wheelset", 4, 8, Facing.Any),
        new("coal", 3.8f, 6, Facing.Any),
        new("camp", 8, 14, Facing.Any),
        new("trolley", 3.5f, 4.5f, Facing.Along),
        new("grave", 5.5f, 9, Facing.Along),
        new("stores", 4, 8, Facing.Any),
        new("freight", 4, 7, Facing.Any),
        new("signal", 3.4f, 3.8f, Facing.Along),
        new("sandbags", 6, 10, Facing.Line),
    ];

    /// <summary>The leaving's pieces (one, or a little cluster), each with its offset from the spot, its lift, its own turn.</summary>
    IEnumerable<(MeshAsset? Piece, Vector3 Offset, float Lift, float Turn)> Leaving(string name, int v)
    {
        switch (name)
        {
            case "ties":
                yield return (Piece($"junk-ties-{v % 3}", () => JunkKit.Ties(_look, v % 3)), Vector3.Zero, 0, 0);
                break;
            case "rails":
                yield return (Piece($"junk-rails-{v % 2}", () => JunkKit.Rails(_look, v % 2)), Vector3.Zero, 0, 0);
                break;
            case "drums":
                yield return (Piece($"junk-drums-{v % 3}", () => JunkKit.Drums(_look, v % 3)), Vector3.Zero, 0, 0);
                break;
            case "reel":
                yield return (Piece("junk-reel", () => JunkKit.Reel(_look)), Vector3.Zero, 0, 0);
                if (v % 2 == 0)
                    yield return (Piece("junk-drums-0", () => JunkKit.Drums(_look, 0)), new Vector3(1.6f, 0, 0.8f), 0, 1.1f);
                break;
            case "milepost":
                yield return (Piece($"junk-milepost-{v % 2}", () => JunkKit.Milepost(_look, v % 2)), Vector3.Zero, 0, 0);
                break;
            case "hut":
                yield return (Piece($"junk-hut-{v % 2}", () => JunkKit.Hut(_look, v % 2)), Vector3.Zero, 0, 0);
                yield return (Piece($"junk-ties-{v % 3}", () => JunkKit.Ties(_look, v % 3)), new Vector3(2.8f, 0, 0.6f), 0, MathF.PI / 2);
                break;
            case "wheelset":
                yield return (Piece("junk-wheelset", () => JunkKit.Wheelset(_look)), Vector3.Zero, -0.12f, 0);
                break;
            case "coal":
                yield return (Piece($"junk-coal-{v % 2}", () => JunkKit.Coal(_look, v % 2)), Vector3.Zero, 0, 0);
                break;
            case "camp":
                yield return (Piece($"junk-camp-{v % 2}", () => JunkKit.Camp(_look, v % 2)), Vector3.Zero, 0, 0);
                yield return (_props.Get("stores_crate"), new Vector3(1.3f, 0, -0.6f), 0.35f, 0.4f);
                break;
            case "trolley":
                yield return (Piece("junk-trolley", () => JunkKit.Trolley(_look)), Vector3.Zero, 0, 0);
                break;
            case "grave":
                yield return (Piece($"junk-grave-{v % 2}", () => JunkKit.Grave(_look, v % 2)), Vector3.Zero, 0, 0);
                if (v >= 3)
                    yield return (Piece($"junk-grave-{(v + 1) % 2}", () => JunkKit.Grave(_look, (v + 1) % 2)), new Vector3(1.2f, 0, 0.2f), 0, 0.08f);
                break;
            case "stores":
                // Dumped off a train in a hurry: a heavy crate, a stores crate on it, another fallen by.
                yield return (_props.Get("heavy_crate"), Vector3.Zero, 0.44f, 0.15f);
                yield return (_props.Get("stores_crate"), new Vector3(0.1f, 0, 0), 0.89f + 0.36f, 0.5f);
                yield return (_props.Get("stores_crate"), new Vector3(1.4f, 0, 0.7f), 0.35f, 1.2f);
                break;
            case "freight":
                {
                    string[] goods = ["freight_timber", "freight_sacks", "freight_carboys"];
                    yield return (_props.Get(goods[v % 3]), Vector3.Zero, 0.44f, 0.3f);
                    yield return (_props.Get(goods[(v + 1) % 3]), new Vector3(1.0f, 0, 0.4f), 0.44f, 0.9f);
                    if (v % 2 == 1)
                        yield return (_props.Get(goods[v % 3]), new Vector3(0.3f, 0, 1.1f), 0.44f, 2.0f);
                    break;
                }
            case "signal":
                // A signal nobody works any more, its arm dropped (not a mail crane: those are the sim's, note 149).
                yield return (Piece("signal-False", () => WorldKit.Signal(_look, lit: false)), Vector3.Zero, 0, 0);
                break;
            case "sandbags":
                // An old checkpoint: a sandbagged wall, its ends turned in, drums behind.
                yield return (_props.Get("sandbags"), Vector3.Zero, 0, 0);
                yield return (_props.Get("sandbags"), new Vector3(-1.7f, 0, -1.2f), 0, MathF.PI / 2);
                yield return (Piece("junk-drums-1", () => JunkKit.Drums(_look, 1)), new Vector3(0.6f, 0, -1.6f), 0, 0);
                break;
        }
    }
}
