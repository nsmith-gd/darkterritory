namespace DarkTerritory.Game;

/// <summary>
/// The frame-rate targets and the budgets that keep them (content/tuning/perf.json; the director, 30 Sep: "72fps in VR
/// minimum and 90fps on PC"). `dt perf` measures a frame against them, and PerfBudgetTests holds the counts.
/// </summary>
public sealed record PerfTuning
{
    public const string File = "tuning/perf.json";
    public PerfTarget Pc { get; init; } = new();
    public PerfTarget Vr { get; init; } = new() { Fps = 72, Width = 1032, Height = 1104, Eyes = 2, Mirror = true };
    /// <summary>Of a frame's time, the share the main thread's drawing work may take (the scene built, uploaded and
    /// recorded): the rest is the sim's ticks, the audio's mix, the net, and headroom for a busy moment.</summary>
    public double CpuShare { get; init; } = 0.6;
    /// <summary>Triangles drawn in a frame over every pass (both shadows and the scene, every eye), at most.</summary>
    public int MaxFrameTriangles { get; init; } = 1_500_000;
    /// <summary>Draw calls in any one pass, at most.</summary>
    public int MaxPassDraws { get; init; } = 1500;
}

public sealed record PerfTarget
{
    public double Fps { get; init; } = 90;
    /// <summary>The frame's internal resolution (a headset's: each eye's).</summary>
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public int Eyes { get; init; } = 1;
    /// <summary>Whether the desktop window mirrors the flat view as well (a headset session does).</summary>
    public bool Mirror { get; init; }
    public double FrameMs => 1000 / Fps;
}
