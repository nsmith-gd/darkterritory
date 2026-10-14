using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// The lamp signals (GDD §12; the director's calls of 14 Oct 2026, friendslop.md queue #454; ARCHITECTURE §8 note 720): with a
/// hand lamp in the fist, held on its key, the three signals round the crosshair, the mouse leaning toward one; let go, it's
/// given (sent as intent). GO up (the lamp raised and lowered), STOP to the left (swung across the line), BACK UP to the
/// right (a circle). A tap with no lean gives the last one again (STOP, the first time). It's the lamp's own, beside the
/// emote wheel (note 298), and shaped like it, so the wheel can host it later. The app feeds it; tests drive it.
/// </summary>
public sealed class SignalWheel
{
    /// <summary>What's on the wheel, and where.</summary>
    public static readonly (LampSignal Signal, string Label, Vector2 At)[] Spokes =
    [
        (LampSignal.Go, "GO", new(0, -1)),
        (LampSignal.Stop, "STOP", new(-1, 0)),
        (LampSignal.Back, "BACK UP", new(1, 0)),
    ];

    Vector2 _lean;

    public bool Open { get; private set; }
    /// <summary>The last signal given: a tap gives it again.</summary>
    public LampSignal Last { get; private set; } = LampSignal.Stop;

    /// <summary>The spoke the mouse leans toward, or the last one given while it hasn't leant far (<see cref="EmoteWheel.Lean"/>).</summary>
    public LampSignal Picked
    {
        get
        {
            if (_lean.Length() < EmoteWheel.Lean)
                return Last;
            var dir = Vector2.Normalize(_lean);
            return Spokes.MaxBy(s => Vector2.Dot(s.At, dir)).Signal;
        }
    }

    /// <summary>The key's held this frame (and there's a lamp in the fist), and the mouse moved by (dx, dy). Returns the signal let go on, or none.</summary>
    public LampSignal Update(bool held, float dx, float dy)
    {
        if (held)
        {
            if (!Open)
            {
                Open = true;
                _lean = Vector2.Zero;
            }
            _lean += new Vector2(dx, dy);
            if (_lean.Length() > EmoteWheel.Lean * 2)
                _lean = Vector2.Normalize(_lean) * EmoteWheel.Lean * 2;
            return LampSignal.None;
        }
        if (!Open)
            return LampSignal.None;
        Open = false;
        Last = Picked;
        return Last;
    }

    /// <summary>The wheel round the crosshair in the prompts' fine print, the picked spoke lit (as the emote wheel's).</summary>
    public void Draw(Overlay o, int width, int height, float scale = 0.5f)
    {
        if (!Open)
            return;
        float cx = width / 2f, cy = height / 2f, r = 22;
        var picked = Picked;
        foreach (var (signal, label, at) in Spokes)
        {
            float w = o.Measure(label, scale);
            float x = cx + at.X * r - (at.X < 0 ? w : at.X > 0 ? 0 : w / 2), y = cy + at.Y * r - o.Font.Height * scale / 2;
            o.Text(MathF.Round(x), MathF.Round(y), label, signal == picked ? new Vector4(1.0f, 0.7f, 0.3f, 1) : new Vector4(0.88f, 0.84f, 0.74f, 0.75f), scale);
        }
    }
}
