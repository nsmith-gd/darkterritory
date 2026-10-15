using System.Numerics;
using System.Text.Json;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>One flag of the towns' series: its cell in the <c>town_flags</c> atlas (pixels), the custom and trade it's for.</summary>
public sealed record FlagCell(string Culture, string Trade, int Cell, int X, int Y, int W, int H, string Charge = "", string Field = "");

/// <summary>
/// The towns' flags (queue #405, note 674; the director, 13 Oct: "we need to do a series of flag designs for the towns"):
/// content/art/flags.json, written by tools/art/flags.py beside the atlas it describes (tools/art/texgen/mat_flags.py): a
/// field for each trade, the old province's saltire in the canton, a charge for each custom a town keeps.
/// </summary>
public sealed class FlagBook
{
    public string Texture { get; init; } = "town_flags";
    public int Size { get; init; } = 1024;
    public IReadOnlyList<FlagCell> Flags { get; init; } = [];

    public static readonly FlagBook Empty = new();

    /// <summary>The catalogue under <paramref name="content"/>, or none (a greybox build: the flag is plain).</summary>
    public static FlagBook Load(string content)
    {
        string path = Path.Combine(content, "art", "flags.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<FlagBook>(File.ReadAllText(path), Ballast.DataFile.Options) ?? Empty : Empty;
    }

    /// <summary>
    /// A town's flag: the one for its custom (<see cref="Sim.Towns.TownPlan.Culture"/>) in its trade (its industry, by the
    /// trade's name in it: "a pit town"), so the same town always flies the same flag. A custom or a trade the series hasn't
    /// got yet takes one by the town's own name, as its seed, from those it can.
    /// </summary>
    public FlagCell? For(string culture, string industry, string name)
    {
        if (Flags.Count == 0)
            return null;
        string? trade = Flags.Select(f => f.Trade).Distinct().FirstOrDefault(t => industry.Equals(t, StringComparison.OrdinalIgnoreCase)
            || Flags.Any(f => f.Trade == t && f.Field.Length > 0 && industry.Contains(f.Field, StringComparison.OrdinalIgnoreCase)));
        if (Flags.FirstOrDefault(f => f.Culture == culture && f.Trade == trade) is { } exact)
            return exact;
        var choice = Flags.Where(f => trade is null || f.Trade == trade).ToList();
        if (choice.Count == 0)
            choice = [.. Flags];
        // The town's name is its seed's: fixed for the town, the same on every machine (FNV-1a, not string.GetHashCode).
        uint h = 2166136261;
        foreach (char c in name)
            h = (h ^ c) * 16777619;
        return choice[(int)(h % (uint)choice.Count)];
    }
}

/// <summary>
/// A town's flag as cloth in the wind (queue #405, note 674; the director, 13 Oct: "it needs to be waving if it's going to
/// be there"). The cloth is a grid skinned to a chain of <see cref="Bones"/> bones from its hoist to its fly; each frame
/// <see cref="Pose"/> turns the chain downwind, lifts it out of its hang as the wind rises, and runs a ripple out along it,
/// quicker and wider in a gale and in the gusts, so it's never still and never stiff; in a calm it hangs down the pole,
/// stirring.
/// </summary>
public static class FlagKit
{
    /// <summary>The chain's bones, hoist (0) to fly.</summary>
    public const int Bones = 6;
    const int Columns = 12, Rows = 4;

    /// <summary>
    /// The cloth: <paramref name="t"/>'s length out along +X from its hoist at the origin, its height down −Y, both faces,
    /// mapped to <paramref name="cell"/> of the flag atlas (or the town's plain blue without one), skinned to the chain.
    /// </summary>
    public static MeshAsset Cloth(Look? look, FlagCell? cell, FlagBook book, FlagTuning t)
    {
        var k = new Kit(look, 7.1f);
        k.Use(cell is null ? "scarf" : book.Texture, cell is null ? Palette.BlueGrey : Vector3.One, 0.2f, 0, tile: 1);
        if (cell is null || look?.Layer(book.Texture) is null or < 0)
            k.Tint = new Vector3(0.45f, 0.6f, 1.1f);
        float size = book.Size;
        Vector2 Uv(float u, float v) => cell is null ? new Vector2(u * t.Length, v * t.Height)
            : new Vector2((cell.X + u * cell.W) / size, (cell.Y + v * cell.H) / size);
        Vector3 P(float u, float v) => new(u * t.Length, -v * t.Height, 0);
        for (int i = 0; i < Columns; i++)
            for (int j = 0; j < Rows; j++)
            {
                float u0 = i / (float)Columns, u1 = (i + 1) / (float)Columns, v0 = j / (float)Rows, v1 = (j + 1) / (float)Rows;
                // Both faces, each wound to face its own way: +Z, and −Z behind it (a flag's back is its front seen through).
                k.Tri(P(u0, v0), P(u0, v1), P(u1, v1), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Uv(u0, v0), Uv(u0, v1), Uv(u1, v1));
                k.Tri(P(u0, v0), P(u1, v1), P(u1, v0), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Uv(u0, v0), Uv(u1, v1), Uv(u1, v0));
                k.Tri(P(u0, v0), P(u1, v1), P(u0, v1), -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, Uv(u0, v0), Uv(u1, v1), Uv(u0, v1));
                k.Tri(P(u0, v0), P(u1, v0), P(u1, v1), -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, Uv(u0, v0), Uv(u1, v0), Uv(u1, v1));
            }
        var flat = k.Build("flag-cloth");
        var skin = new SkinWeights[flat.Vertices.Length];
        for (int i = 0; i < skin.Length; i++)
        {
            // Along the chain, between the two bones either side of it.
            float at = Math.Clamp(flat.Vertices[i].Position.X / t.Length, 0, 1) * (Bones - 1);
            int b = Math.Min((int)at, Bones - 2);
            float w = at - b;
            skin[i] = new SkinWeights(new Vector4(b, b + 1, 0, 0), new Vector4(1 - w, w, 0, 0));
        }
        return new MeshAsset($"flag-{cell?.Culture ?? "plain"}-{cell?.Trade ?? ""}", flat.Vertices, skin);
    }

    /// <summary>
    /// The chain at <paramref name="time"/> in a wind of <paramref name="wind"/> (m/s, world axes: <see cref="WindTuning.Of"/>)
    /// gusting by <paramref name="gusts"/>: each bone's matrix, bind space to the flag's (world-aligned) object space, its hoist
    /// at the origin. <paramref name="seed"/> keeps two flags out of step. <paramref name="clock"/>, the flag's own, runs its
    /// ripple on from frame to frame at the pace of the moment (without one, as if it had run at this pace all night).
    /// </summary>
    public static void Pose(FlagTuning t, Vector3 wind, float gusts, double time, float seed, Span<Matrix4x4> skin, FlagClock? clock = null)
    {
        float s = (float)time + seed * 7.3f;
        // The gusts roll through as the foliage's do (scene.vert): the wind a little up and down on a slow swell.
        float gust = 1 + gusts * 0.35f * (MathF.Sin(s * 0.45f) * 0.6f + MathF.Sin(s * 1.3f + 1.7f) * 0.4f);
        var flat = wind with { Y = 0 };
        float speed = flat.Length() * gust;
        float lift = t.Lift(speed);
        // Downwind, and in a calm wherever it last blew, wandering a little.
        float yaw = (flat.LengthSquared() > 1e-6f ? MathF.Atan2(-flat.Z, flat.X) : 0) + (1 - lift) * 0.35f * MathF.Sin(s * 0.23f);
        var turn = Matrix4x4.CreateRotationY(yaw);
        float ripple = t.Ripple + (t.RippleFull - t.Ripple) * lift;
        float pace = t.Pace + (t.PaceFull - t.Pace) * lift * gust;
        // How far the ripple's run (rad). Its pace rises and falls with every gust, so it's run on from where it was: the
        // clock's time by the pace would jump it on by the whole night's change at each one (the 14 Oct playtest's "a little
        // fast sometimes": the fly flung about frame to frame, worse the later it got).
        float run = (float)((clock?.Run(s, pace) ?? s * pace) % (2 * Math.PI));
        float seg = t.Length / (Bones - 1);
        var at = Vector3.Zero;
        for (int i = 0; i < Bones; i++)
        {
            float f = i / (float)(Bones - 1);
            // Hanging: the fly falls toward the pole, more the further out; lifted, it streams out nearly level.
            // (Slack, it still stirs: the hang swings a little on its own slow breath.)
            float droop = t.Droop * (1 - lift) * MathF.Pow(f, 0.8f) * (1 + 0.1f * MathF.Sin(s * 1.1f + f * 1.5f)) + 0.08f * lift * f;
            // Flying, it billows up and down as well as side to side, the wave a little behind the snake.
            float billow = t.Flutter * lift * f * MathF.Sin(f * t.Waves * MathF.Tau - run + 0.9f);
            // The ripple runs out from the hoist (phase back along the chain), nothing at the pole, widest at the fly.
            float phase = f * t.Waves * MathF.Tau - run;
            float snake = ripple * f * MathF.Sin(phase);
            // A lift and fall across it as the wave passes, and the fly's flick.
            float roll = 0.12f * lift * f * MathF.Sin(phase + 1.1f);
            float flick = f > 0.75f ? 0.15f * (0.3f + lift) * MathF.Sin(run * 1.7f + seed) * (f - 0.75f) * 4 : 0;
            var r = Matrix4x4.CreateRotationX(roll) * Matrix4x4.CreateRotationY(snake + flick) * Matrix4x4.CreateRotationZ(billow - droop) * turn;
            var pose = r with { Translation = at };
            skin[i] = Matrix4x4.CreateTranslation(-seg * i, 0, 0) * pose;
            // The next bone a segment on along this one.
            at += Vector3.TransformNormal(Vector3.UnitX, r) * seg;
        }
    }
}

/// <summary>
/// A flag's ripple run on frame to frame (<see cref="FlagKit.Pose"/>): its pace integrated over the time it's drawn, so a gust
/// quickens it from where it is. Presentation only: each flag's own, kept by whoever draws it. A gap of more than a second
/// (the flag out of sight) or a clock run back starts it again as if it had run at that pace.
/// </summary>
public sealed class FlagClock
{
    double _at = double.NaN, _run;

    /// <summary>How far the ripple's run (rad) by <paramref name="time"/> (s) at <paramref name="pace"/> (rad/s) since last asked.</summary>
    public double Run(double time, double pace)
    {
        double dt = time - _at;
        _run = double.IsNaN(_at) || dt < 0 || dt > 1 ? time * pace % (2 * Math.PI) : (_run + dt * pace) % (2 * Math.PI);
        _at = time;
        return _run;
    }
}
