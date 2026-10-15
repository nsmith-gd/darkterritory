using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// The emote wheel (note 298; GDD §9's yard, where the crew "hang out, dance"): held on its key, the emotes round the
/// crosshair, the mouse leaning toward one; let go, that one is sent as intent. A tap with no lean sends the last one picked
/// (the dance, the first time). While it's held the mouse picks rather than looks. The app feeds it; tests drive it.
/// </summary>
public sealed class EmoteWheel
{
    /// <summary>
    /// What's on the wheel, and where, round a clock face (note 722: twelve places, 30° apart): the dance up (12), the wave
    /// to the left (9), the point to the right (3), ABOARD down (6, note 740); the emotes for two up to the right of the
    /// dance (the high five at 1, the fist bump at 2), the edges down to the right of the point (sit at 4, lean at 5).
    /// <see cref="Kept"/> are the "more emotes" upgrade's (note 714); 11 is free. A place with nothing in it isn't drawn, and
    /// leaning at it picks a neighbour.
    /// </summary>
    public static readonly (Emote Emote, string Label, Vector2 At)[] Spokes =
    [
        (Emote.Dance, "DANCE", Clock(12)),
        (Emote.HighFive, "HIGH FIVE", Clock(1)),
        (Emote.FistBump, "FIST BUMP", Clock(2)),
        (Emote.Point, "POINT", Clock(3)),
        (Emote.SitEdge, "SIT", Clock(4)),
        (Emote.LeanOut, "LEAN OUT", Clock(5)),
        // Note 740 (GDD §17): the call back to the horn, down.
        (Emote.Aboard, "ABOARD", Clock(6)),
        (Emote.Wave, "WAVE", Clock(9)),
    ];

    /// <summary>
    /// The "more emotes" upgrade's (note 714; docs/design/upgrades.md §4), in the places note 722 kept for it: the bow at 7,
    /// the salute at 8, the conga at 10. On the wheel while the train has it (<see cref="More"/>).
    /// </summary>
    public static readonly (Emote Emote, string Label, Vector2 At)[] Kept =
        [(Emote.Bow, "BOW", Clock(7)), (Emote.Salute, "SALUTE", Clock(8)), (Emote.Conga, "CONGA", Clock(10))];

    /// <summary>The bow, the salute and the conga are on the wheel (the train has the "more emotes" upgrade, note 714).</summary>
    public bool More { get; set; }

    /// <summary>The spokes on offer: <see cref="Spokes"/>, and <see cref="Kept"/> with <see cref="More"/>.</summary>
    public IEnumerable<(Emote Emote, string Label, Vector2 At)> Offered => More ? Spokes.Concat(Kept) : Spokes;

    /// <summary>A place round the wheel by the clock's hour (12 up), on screen (+y down).</summary>
    public static Vector2 Clock(int hour) => new(MathF.Round(MathF.Sin(hour * MathF.PI / 6), 5), MathF.Round(-MathF.Cos(hour * MathF.PI / 6), 5));

    /// <summary>How far the mouse has to lean (its counts) before a spoke is picked over the last one.</summary>
    public const float Lean = 40;

    Vector2 _lean;

    public bool Open { get; private set; }
    /// <summary>The last emote sent: a tap sends it again.</summary>
    public Emote Last { get; private set; } = Emote.Dance;

    /// <summary>The spoke the mouse leans toward, or the last one sent while it hasn't leant far.</summary>
    public Emote Picked
    {
        get
        {
            if (_lean.Length() < Lean)
                return Last;
            var dir = Vector2.Normalize(_lean);
            return Offered.MaxBy(s => Vector2.Dot(s.At, dir)).Emote;
        }
    }

    /// <summary>The key's held this frame (or not), and the mouse moved by (dx, dy) (screen counts: +y down). Returns the emote let go on, or none.</summary>
    public Emote Update(bool held, float dx, float dy)
    {
        if (held)
        {
            if (!Open)
            {
                Open = true;
                _lean = Vector2.Zero;
            }
            _lean += new Vector2(dx, dy);
            // Leaning further only says the same thing: kept short, so a change of mind needs no long way back.
            if (_lean.Length() > Lean * 2)
                _lean = Vector2.Normalize(_lean) * Lean * 2;
            return Emote.None;
        }
        if (!Open)
            return Emote.None;
        Open = false;
        Last = Picked;
        return Last;
    }

    /// <summary>
    /// The wheel round the crosshair in the prompts' fine print (<see cref="Hud.PromptScaleAt"/>; GDD §32 on #206: no frames
    /// in play), the picked spoke lit.
    /// </summary>
    public void Draw(Overlay o, int width, int height, float scale = 0.5f)
    {
        if (!Open)
            return;
        // (Note 722: wider than the four-spoke wheel's 22, so the labels at 30° don't overlap.)
        float cx = width / 2f, cy = height / 2f, r = 30;
        var picked = Picked;
        foreach (var (emote, label, at) in Offered)
        {
            bool on = emote == picked;
            float w = o.Measure(label, scale);
            float x = cx + at.X * r - (at.X < -0.1f ? w : at.X > 0.1f ? 0 : w / 2), y = cy + at.Y * r * 1.15f - o.Font.Height * scale / 2;
            o.Text(MathF.Round(x), MathF.Round(y), label, on ? new Vector4(1.0f, 0.7f, 0.3f, 1) : new Vector4(0.88f, 0.84f, 0.74f, 0.75f), scale);
        }
    }
}
