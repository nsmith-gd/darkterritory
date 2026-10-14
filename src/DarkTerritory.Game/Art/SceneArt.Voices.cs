using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>The whistle codes' card's words (content/ui/whistle-card.json; note 720).</summary>
public sealed record WhistleCardText(string Title, IReadOnlyList<string> Codes)
{
    public const string File = "ui/whistle-card.json";
}

/// <summary>The speaking tube's tags over its horns, by end (content/ui/speaking-tube.json; note 720).</summary>
public sealed record TubeTags(IReadOnlyList<string> Tags)
{
    public const string File = "ui/speaking-tube.json";
}

// The voice's fittings (note 720): the speaking tube's ends, the whistle codes' card, and a lamp signal's swing.
public sealed partial class SceneArt
{
    /// <summary>How far off the voice's fittings are drawn (m): they're for whoever's in the car.</summary>
    const double VoiceFittingsSeen = 40;

    /// <summary>
    /// The speaking tube's ends in this car (<see cref="SpeakingTube.Ends"/>): each a brass horn on its wall where the sim's
    /// mouthpiece is, turned into the room, and its pipe down into the floor.
    /// </summary>
    public void SpeakingTubes(MeshBuilder mesh, in CarFrame frame, Double3 eye, IReadOnlyList<TubeEnd> ends)
    {
        if ((frame.Origin - eye).Length > VoiceFittingsSeen)
            return;
        var m = FrameMatrix(frame, eye);
        var tags = Read<TubeTags>(TubeTags.File, ref _tubeTags)?.Tags ?? [];
        for (int i = 0; i < ends.Count; i++)
        {
            var e = ends[i];
            if (e.Car != frame.Index)
                continue;
            float drop = (float)SpeakingTube.MouthHeight + 0.1f;
            var at = (e.Facing.X < 0 ? Matrix4x4.CreateRotationY(MathF.PI) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(ToF(e.Mouth)) * m;
            mesh.Instances.Add(new MeshInstance(Piece($"speaking-tube-{drop:0.00}", () => VoiceKit.TubeEnd(Look, drop)), at));
            // Its tag on the wall beside the horn (under the window slit, clear of the plug), saying where it goes.
            if (i < tags.Count && tags[i] is { Length: > 0 } tag)
                mesh.Instances.Add(new MeshInstance(Piece($"tube-tag-{tag}", () => VoiceKit.Tag(Look, tag)),
                    Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(-VoiceKit.Reach + 0.012f, 0, -VoiceKit.BellRadius - 0.1f) * at));
        }
    }

    (bool Read, TubeTags? Value) _tubeTags;

    /// <summary>A content file's words, read once (none: null).</summary>
    T? Read<T>(string file, ref (bool Read, T? Value) cache) where T : class
    {
        if (!cache.Read)
        {
            string path = Path.Combine(PropArt.Of(Look).ContentRoot, file);
            cache = (true, File.Exists(path) ? DataFile.Load<T>(path) : null);
        }
        return cache.Value;
    }

    WhistleCardText? _whistleCard;
    bool _whistleCardRead;

    /// <summary>The card's words from content, read once (none: no card).</summary>
    WhistleCardText? WhistleCardWords()
    {
        if (_whistleCardRead)
            return _whistleCard;
        _whistleCardRead = true;
        string path = Path.Combine(PropArt.Of(Look).ContentRoot, WhistleCardText.File);
        _whistleCard = File.Exists(path) ? DataFile.Load<WhistleCardText>(path) : null;
        return _whistleCard;
    }

    /// <summary>
    /// Where the whistle codes' card hangs in the cab (engine frame; GDD §12, note 720): on the driver's side wall beside the
    /// whistle cord, a hand behind it, its top under the side window's slit; null without the cord.
    /// </summary>
    public static Double3? WhistleCardAt(CarShape engine)
    {
        foreach (var i in engine.Interactables)
            if (i.Kind == InteractableKind.Whistle)
                return new Double3(engine.HalfWidth - 0.1 - 0.004, i.Position.Y + 1.58, i.Position.Z + 0.32);
        return null;
    }

    /// <summary>How long one swing, lift or circle of a lamp signal takes (s).</summary>
    public const double SignalBeat = 1.2;

    /// <summary>
    /// Where the lamp hand goes, <paramref name="seconds"/> into a lamp signal (GDD §12; note 720), in the crewmate's model space
    /// (x right, y up, z behind, from the feet): STOP swung across the line at arm's length, a pendulum from the shoulder; GO
    /// raised high and lowered, out in front; BACK UP round in a circle across the body. The lamp hangs under the fist, so the
    /// light draws the shape.
    /// </summary>
    public static Vector3 LampSignalHand(Sim.Player.LampSignal signal, double seconds)
    {
        float a = (float)(seconds / SignalBeat * Math.Tau);
        return signal switch
        {
            Sim.Player.LampSignal.Stop => Swing(1.15f * MathF.Sin(a)),
            Sim.Player.LampSignal.Go => new Vector3(0.3f, 1.2f + 0.5f * MathF.Sin(a), -0.38f),
            Sim.Player.LampSignal.Back => new Vector3(0.22f + 0.34f * MathF.Cos(a), 1.32f + 0.34f * MathF.Sin(a), -0.5f),
            _ => new Vector3(0.25f, 0.9f, -0.2f),
        };
        static Vector3 Swing(float phi) => new(0.12f + 0.52f * MathF.Sin(phi), 1.3f - 0.52f * MathF.Cos(phi), -0.42f);
    }

    /// <summary>How much of a lamp signal's path its light leaves behind it (s), and in how many glows.</summary>
    const double TrailSeconds = 0.55;
    const int TrailGlows = 9;

    /// <summary>
    /// The lamp's light along where it's just been (note 720), fading behind it, as the eye keeps a swung lamp's streak in the
    /// dark: a signal is read by the shape the light draws, from the cab a train's length off. <paramref name="model"/> is the
    /// crewmate's (camera-relative), <paramref name="flame"/> where the lamp's flame is now.
    /// </summary>
    void SignalTrail(MeshBuilder mesh, in Matrix4x4 model, Vector3 flame, Sim.Player.LampSignal signal, double seconds)
    {
        // The flame hangs under the fist: the same drop from each of the hand's past places.
        var hung = flame - Vector3.Transform(LampSignalHand(signal, seconds), model);
        for (int k = 1; k <= TrailGlows; k++)
        {
            double ago = TrailSeconds * k / TrailGlows;
            if (seconds - ago < 0)
                break;
            var at = Vector3.Transform(LampSignalHand(signal, seconds - ago), model) + hung;
            float fade = 1 - (float)k / (TrailGlows + 1);
            mesh.Billboard(at, 0.4f * fade + 0.15f, 0, new Vector4(Palette.LampAmber * 0.8f * fade, 1), -1, FxBlend.Additive);
        }
    }

    /// <summary>The whistle codes' card, by the cord in the cab (note 720): read from the footplate.</summary>
    public void WhistleCard(MeshBuilder mesh, in CarFrame frame, Double3 eye)
    {
        if ((frame.Origin - eye).Length > VoiceFittingsSeen || WhistleCardAt(frame.Shape) is not { } at || WhistleCardWords() is not { } words)
            return;
        var piece = Piece("whistle-card", () => VoiceKit.WhistleCard(Look, words.Title, words.Codes));
        // Its face (+Z) turned to the cab (−X: it's on the right-hand wall).
        mesh.Instances.Add(new MeshInstance(piece, Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(ToF(at)) * FrameMatrix(frame, eye)));
    }
}
