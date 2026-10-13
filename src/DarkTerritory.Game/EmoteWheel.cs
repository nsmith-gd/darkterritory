using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// The emote wheel (note 298; GDD §9's yard, where the crew "hang out, dance"): held on its key, the three emotes round the
/// crosshair, the mouse leaning toward one; let go, that one is sent as intent. A tap with no lean sends the last one picked
/// (the dance, the first time). While it's held the mouse picks rather than looks. The app feeds it; tests drive it.
/// </summary>
public sealed class EmoteWheel
{
    /// <summary>What's on the wheel, and where: the dance up, the wave to the left, the point to the right.</summary>
    public static readonly (Emote Emote, string Label, Vector2 At)[] Spokes =
    [
        (Emote.Dance, "DANCE", new(0, -1)),
        (Emote.Wave, "WAVE", new(-1, 0)),
        (Emote.Point, "POINT", new(1, 0)),
    ];

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
            return Spokes.MaxBy(s => Vector2.Dot(s.At, dir)).Emote;
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
        float cx = width / 2f, cy = height / 2f, r = 22;
        var picked = Picked;
        foreach (var (emote, label, at) in Spokes)
        {
            bool on = emote == picked;
            float w = o.Measure(label, scale);
            float x = cx + at.X * r - (at.X < 0 ? w : at.X > 0 ? 0 : w / 2), y = cy + at.Y * r - o.Font.Height * scale / 2;
            o.Text(MathF.Round(x), MathF.Round(y), label, on ? new Vector4(1.0f, 0.7f, 0.3f, 1) : new Vector4(0.88f, 0.84f, 0.74f, 0.75f), scale);
        }
    }
}
