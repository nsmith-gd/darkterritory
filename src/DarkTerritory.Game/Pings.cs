using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// Where a point in the world falls on the screen (the gym's pointer's, note 676; the crew's pings', note 739): on it, or
/// (behind the eye, or past an edge) the point on a frame inset from the edge in its direction, with that direction.
/// </summary>
public static class ScreenPoint
{
    /// <summary>
    /// Where <paramref name="at"/> falls on a <paramref name="width"/> by <paramref name="height"/> screen through
    /// <paramref name="camera"/>: on it, or the point on a frame <paramref name="inset"/> in from the edge its way (screen space,
    /// +X right, +Y down).
    /// </summary>
    public static (bool OnScreen, float X, float Y, float DirX, float DirY) Of(in Camera camera, Double3 at, float width, float height, float inset)
    {
        var rel = at - camera.Position;
        var clip = Vector4.Transform(new Vector4((float)rel.X, (float)rel.Y, (float)rel.Z, 1), camera.ViewProjection(width / height));
        float cx = width / 2, cy = height / 2;
        if (clip.W > 1e-3f)
        {
            float x = (clip.X / clip.W * 0.5f + 0.5f) * width, y = (clip.Y / clip.W * 0.5f + 0.5f) * height;
            if (x >= inset && x <= width - inset && y >= inset && y <= height - inset)
                return (true, x, y, 0, 0);
        }
        // In front, the way it lies on the screen. Behind the eye, the side it's on (the way to turn to it), at the edge's
        // middle; straight behind, the bottom.
        float dx = clip.X, dy = clip.Y;
        if (clip.W <= 1e-3f)
            (dx, dy) = Math.Abs(clip.X) < 1e-3f * Math.Abs(clip.W) + 1e-4f ? (0f, 1f) : (Math.Sign(clip.X), 0f);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        dx /= len;
        dy /= len;
        // Out from the middle along it to the inset frame.
        float sx = dx == 0 ? float.MaxValue : (cx - inset) / Math.Abs(dx), sy = dy == 0 ? float.MaxValue : (cy - inset) / Math.Abs(dy);
        float s = Math.Min(sx, sy);
        return (false, cx + dx * s, cy + dy * s, dx, dy);
    }
}

/// <summary>
/// The crew's pings as everyone sees them (GDD §12 "Ping the bots", note 739): a mark on each with what it is and how far,
/// an arrow at the screen's edge the way to it when it's out of view, and a line naming each with whose it is.
/// </summary>
public static class PingMarks
{
    static readonly Vector4 Mark = new(0.55f, 0.85f, 1f, 0.95f);
    const float Edge = 14;

    /// <summary>
    /// What a ping's on, as said: "FIRE, CAR 3", "THE CONTROLS", "LOOT", "THE CRANE", "THE TOWN"; note 936's "THE SPUR" (a
    /// switch's points: "THE POINTS" off a branch that isn't a spur), "THE CARGO" and "CAR 4" (an open car for it).
    /// </summary>
    public static string What(PingEvent p, World? world = null) => p.Kind switch
    {
        PingKind.Fire => $"FIRE, CAR {p.Target}",
        PingKind.Cab => "THE CONTROLS",
        PingKind.Loot => "LOOT",
        PingKind.Crane => "THE CRANE",
        PingKind.Town => "THE TOWN",
        PingKind.Points => Spur(p, world) ? "THE SPUR" : "THE POINTS",
        PingKind.Cargo => p.Target > 0 ? $"CAR {p.Target}" : "THE CARGO",
        _ => "THERE",
    };

    /// <summary>
    /// Note 936 (the director, 20 Oct: "oh, the spur? Yeah, of course, you want me to throw the spur. Oh, an abandoned house?
    /// Sure, I'll go search it"): what the crewmate who's taken a ping says back, on it.
    /// </summary>
    public static string Answer(PingEvent p, World? world = null) => p.Kind switch
    {
        PingKind.Fire => $"ON THE FIRE, CAR {p.Target}",
        PingKind.Cab => "TAKING THE CONTROLS",
        PingKind.Loot => "FETCHING THE LOOT",
        PingKind.Crane => "ON THE CRANE",
        PingKind.Town => "SEARCHING THE HOUSE",
        PingKind.Points => Spur(p, world) ? "ON THE SPUR" : "ON THE POINTS",
        PingKind.Cargo => p.Target > 0 ? $"LOADING CAR {p.Target}" : "LOADING THE CARGO",
        _ => "ON IT",
    };

    internal static bool Spur(PingEvent p, World? world) =>
        world is null || p.Target < 0 || p.Target >= world.Train.Line.Branches.Count || world.Train.Line.Branches[p.Target].Kind == Sim.Rail.BranchKind.Spur;

    /// <summary>
    /// The HUD's lines (note 739): "PRIYA: FIRE, CAR 3", newest first, yours as "YOU". Note 936: one a crewmate's answered is
    /// their answer, "DUNMORE: ON THE SPUR".
    /// </summary>
    public static IEnumerable<string> Lines(World world, int me) =>
        world.Pings.OrderByDescending(p => p.Tick).ThenByDescending(p => p.Id).Take(3)
            .Select(p => p.Taker >= 0 ? $"{Who(world, p.Taker, me)}: {Answer(p, world)}" : $"{Who(world, p.By, me)}: {What(p, world)}");

    static string Who(World world, int id, int me) => id == me ? "YOU" : IncidentLog.NameOf(world, id).ToUpperInvariant();

    /// <summary>Each ping's mark, as this machine draws the world through <paramref name="camera"/>.</summary>
    public static void Draw(Overlay overlay, int width, int height, in Camera camera, World world)
    {
        var eye = camera.Position;
        foreach (var p in world.Pings)
        {
            string label = $"{What(p, world)} {(p.At - eye).Length:0} M";
            var at = ScreenPoint.Of(camera, p.At, width, height, Edge);
            if (at.OnScreen)
            {
                overlay.Outline(at.X - 5, at.Y - 5, 10, 10, Mark);
                overlay.Rect(at.X - 1, at.Y - 1, 2, 2, Mark);
                overlay.TextCentred(at.X, at.Y + 8, label, Mark);
                continue;
            }
            for (int i = 0; i < 5; i++)
            {
                float back = 2.5f * i, half = 0.8f + i * 1.1f;
                overlay.Rect(at.X - at.DirX * back - half, at.Y - at.DirY * back - half, half * 2, half * 2, Mark);
            }
            if (at.DirX > 0.6f)
                overlay.TextRight(at.X - 16, at.Y - 4, label, Mark);
            else if (at.DirX < -0.6f)
                overlay.Text(at.X + 16, at.Y - 4, label, Mark);
            else
                overlay.TextCentred(Math.Clamp(at.X, 60, width - 60), at.DirY > 0 ? at.Y - 22 : at.Y + 14, label, Mark);
        }
    }
}
