using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

// The train's dressing (ARCHITECTURE §8 note 701): what the crew bought from the blacksmith to be seen, drawn where it's
// fitted (docs/design/upgrades.md §8.1, the first tell: a thing on the train you can point at).
public sealed partial class GreyboxScene
{
    /// <summary>The train's dressing fitted (train.json composition.dress, from the campaign's upgrades): campaign.json ids.</summary>
    public IReadOnlyList<string> Dress { get; set; } = [];

    /// <summary>The crew's names, for the towels that have opinions of them (content/world/dress.json "named").</summary>
    public IReadOnlyList<string> CrewNames { get; set; } = [];

    /// <summary>The words on the dressing (content/world/dress.json): the stickers' puns and the towels' monograms.</summary>
    public DressWords? Words { get; set; }

    // How far the tail's turned against the car ahead of it (rad): the tiny caboose comes off on a bend. And the engine
    // against the car behind: the dice swing out.
    double _tailTurn, _bend;

    void Bends(IReadOnlyList<CarFrame> frames)
    {
        static double Turn(in CarFrame a, in CarFrame b) => Math.IEEERemainder(a.Heading - b.Heading, 2 * Math.PI);
        _bend = frames.Count > 1 ? Turn(frames[0], frames[1]) : 0;
        _tailTurn = frames.Count > 1 ? Turn(frames[^1], frames[^2]) : 0;
    }

    bool Dressed(string id) => Dress.Contains(id);

    void Dressing(MeshBuilder mesh, in CarFrame frame, Double3 eye)
    {
        if (Dress.Count == 0 || Look is null)
            return;
        var art = Look.Art;
        var m = Art.SceneArt.FrameMatrix(frame, eye);
        var shape = frame.Shape;
        float speed = (float)Math.Min(1, frame.Velocity.Length / 15);
        float t = (float)Time;
        if (frame.Index == 0 && shape.Cab is { } cab)
        {
            float z = (float)cab.Min.Z + 0.5f, ceiling = Art.TrainKit.CeilingAt(shape, z);
            // Hung from the roof by the front glass, swinging with the ride: more as she goes faster, and out on a bend.
            float sway = 0.12f * speed * MathF.Sin(t * 2.3f) + 0.05f * MathF.Sin(t * 0.7f) + (float)Math.Clamp(_bend * 6, -0.5, 0.5);
            float pitch = 0.06f * speed * MathF.Sin(t * 1.7f + 1);
            if (Dressed("fuzzyDice"))
                mesh.Append(art.Piece("dress-dice", () => Art.DressKit.FuzzyDice(Look)),
                    Matrix4x4.CreateRotationZ(sway) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(-0.35f, ceiling, z) * m);
            if (Dressed("airFreshener"))
                mesh.Append(art.Piece("dress-freshener", () => Art.DressKit.Freshener(Look)),
                    Matrix4x4.CreateRotationY(0.4f * MathF.Sin(t * 0.9f)) * Matrix4x4.CreateRotationZ(sway * 0.7f) * Matrix4x4.CreateTranslation(0.55f, ceiling, z + 0.05f) * m);
            // Mounted on the cab's front over the glass, looking down the line (inside the prow's reach: TrainKit.ProwReach).
            if (Dressed("mooseHead"))
                mesh.Instances.Add(new MeshInstance(art.Piece("dress-moose-head", () => Art.DressKit.MooseHead(Look)),
                    Matrix4x4.CreateTranslation(0, 3.95f, (float)cab.Min.Z - 0.03f) * m, Shadowless: true));
        }
        if (frame.Index == 0)
        {
            // Down both sides of the hood (the boiler's inside it), from the cab back.
            if (Dressed("flames") && shape.Cab is { } c)
            {
                float from = (float)c.Max.Z + 0.4f, length = 6.5f;
                var flames = art.Piece($"dress-flames-{length:0.0}", () => Art.DressKit.Flames(Look, length, 1.6f));
                foreach (int side in new[] { -1, 1 })
                    mesh.Append(flames, Matrix4x4.CreateScale(side, 1, 1) * Matrix4x4.CreateTranslation(side * 1.545f, 2.75f, from + length / 2) * m);
            }
            // On the driving wheels' faces, spinning on their own (still spinning at a stand).
            if (Dressed("spinnerHubcaps"))
            {
                var cap = art.Piece("dress-hubcap", () => Art.DressKit.Hubcap(Look, 0.32f));
                float spin = t * 5;
                foreach (float z in Art.TrainKit.Drivers(shape))
                    foreach (int side in new[] { -1, 1 })
                        mesh.Append(cap, Matrix4x4.CreateRotationX(spin * side) * Matrix4x4.CreateRotationY(side < 0 ? MathF.PI : 0)
                            * Matrix4x4.CreateTranslation(side * 0.855f, Art.TrainKit.DriverRadius, z) * m);
            }
        }
        // The back of the train (only the last car has the platform): the guard van's.
        if (shape.Platform is not { } platform)
            return;
        float end = (float)shape.HalfLength;
        // The back end: the stickers right of the door, clear of the ladder; the ribbon left of it.
        if (Dressed("bumperStickers") && Words is { Stickers.Count: > 0 } words)
        {
            var dealt = Deal(words.Stickers, 3, Route?.Seed ?? 0);
            for (int i = 0; i < dealt.Count; i++)
            {
                string text = dealt[i];
                mesh.Append(art.Piece($"dress-sticker-{text}", () => Art.DressKit.Sticker(Look, text, 0.82f)),
                    Matrix4x4.CreateTranslation(1.04f, 1.5f + 0.32f * i, end + 0.03f) * m);
            }
        }
        if (Dressed("participationRibbon"))
            mesh.Append(art.Piece("dress-ribbon", () => Art.DressKit.Ribbon(Look)), Matrix4x4.CreateTranslation(-1.2f, 2.6f, end + 0.03f) * m);
        // Towed on its string off the platform: on a bend (or at a lurch) it's off the rails, over on its side, dragged.
        if (Dressed("tinyCaboose"))
        {
            bool off = Math.Abs(_tailTurn) > 0.012 || speed > 0.5f && MathF.Sin(t * 0.37f) > 0.97f;
            float bump = speed * 0.015f * MathF.Abs(MathF.Sin(t * 13));
            var tow = off
                ? Matrix4x4.CreateRotationZ(1.45f * MathF.Sign((float)_tailTurn + 0.0001f)) * Matrix4x4.CreateRotationY(0.25f * MathF.Sin(t * 3)) * Matrix4x4.CreateTranslation(0.25f * MathF.Sign((float)_tailTurn), 0.09f, 0)
                : Matrix4x4.CreateTranslation(0, bump, 0);
            mesh.Append(art.Piece("dress-caboose", () => Art.DressKit.ToyCaboose(Look)),
                tow * Matrix4x4.CreateTranslation(0, 0, (float)platform.Max.Z + 1.0f) * m);
        }
        if (shape.Interior is { } room)
            GuardInside(mesh, m, room, eye, frame);
    }

    /// <summary>In the guard van: the towels on their rail and the disco ball.</summary>
    void GuardInside(MeshBuilder mesh, Matrix4x4 m, Box room, Double3 eye, in CarFrame frame)
    {
        var art = Look!.Art;
        if (Dressed("monogrammedTowels") && Words is { } words)
        {
            // On a rail along the right-hand wall, their monograms to the aisle: one for each of the crew, with an opinion,
            // and the rest of the stack.
            var seed = Route?.Seed ?? 0;
            var named = CrewNames.Take(3).Select((n, i) => words.Named[(int)((seed + (ulong)i * 7) % (ulong)Math.Max(1, words.Named.Count))].Replace("{NAME}", n.ToUpperInvariant()));
            var towels = named.Concat(Deal(words.Towels, 5 - Math.Min(3, CrewNames.Count), seed + 11)).Take(5).ToList();
            float x = (float)room.Max.X - 0.08f, y = 2.75f;
            mesh.Append(art.Piece("dress-towel-rail", () => Art.DressKit.TowelRail(Look, 2.6f)), Matrix4x4.CreateTranslation(x, y, 1.3f) * m);
            Vector3[] colours = [new(0.75f, 0.2f, 0.2f), new(0.9f, 0.88f, 0.8f), new(0.2f, 0.35f, 0.6f), new(0.85f, 0.7f, 0.3f), new(0.3f, 0.55f, 0.4f)];
            for (int i = 0; i < towels.Count; i++)
            {
                string monogram = towels[i];
                var colour = colours[i % colours.Length];
                mesh.Append(art.Piece($"dress-towel-{monogram}-{i % colours.Length}", () => Art.DressKit.Towel(Look, monogram, colour)),
                    Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(x, y, 0.25f + 0.52f * i) * m);
            }
        }
        if (Dressed("discoBall"))
        {
            float spin = (float)Time * 0.6f;
            var at = new Vector3(0, (float)room.Max.Y - 0.55f, 0);
            mesh.Append(art.Piece("dress-disco-ball", () => Art.DressKit.DiscoBall(Look)), Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateTranslation(at) * m);
            // The lamps' light thrown round the walls in spots, turning with it.
            for (int i = 0; i < 16; i++)
            {
                float a = spin + i * MathF.Tau / 16, rise = -0.35f + 0.25f * (i % 4);
                var dir = new Vector3(MathF.Cos(a), rise, MathF.Sin(a) * 2.2f);
                float reach = ((float)room.Max.X - 0.02f) / MathF.Max(0.05f, MathF.Abs(dir.X));
                var spot = at + dir * reach;
                if (spot.Y < room.Min.Y + 0.1 || spot.Y > room.Max.Y - 0.05 || Math.Abs(spot.Z) > room.Max.Z - 0.1)
                    continue;
                var hue = (i % 3) switch { 0 => new Vector4(1f, 0.85f, 0.6f, 1), 1 => new Vector4(0.7f, 0.8f, 1f, 1), _ => new Vector4(1f, 0.6f, 0.85f, 1) };
                mesh.Billboard(Vector3.Transform(spot, m), 0.09f, 0, hue * 0.6f, -1, FxBlend.Additive);
            }
        }
    }

    /// <summary><paramref name="count"/> of <paramref name="from"/>, different, dealt from <paramref name="seed"/>: the same on every machine for a night.</summary>
    static List<string> Deal(IReadOnlyList<string> from, int count, ulong seed)
    {
        var dealt = new List<string>();
        ulong x = seed * 6364136223846793005UL + 1442695040888963407UL;
        for (int tries = 0; dealt.Count < Math.Min(count, from.Count) && tries < 64; tries++)
        {
            x ^= x >> 29;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 32;
            string pick = from[(int)(x % (ulong)from.Count)];
            if (!dealt.Contains(pick))
                dealt.Add(pick);
        }
        return dealt;
    }
}

/// <summary>The words on the train's dressing (content/world/dress.json; note 701). Field docs live in that file.</summary>
public sealed record DressWords(IReadOnlyList<string> Stickers, IReadOnlyList<string> Towels, IReadOnlyList<string> Named)
{
    public const string File = "world/dress.json";
}
