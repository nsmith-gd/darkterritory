using Ballast;
using Ballast.Render;
using DarkTerritory.Dev.Gym;
using DarkTerritory.Dev.Live;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Dev.Tells;

/// <summary>
/// <c>dt tells</c> (systems-spec A.4a; GDD Part Eleven q6; ARCHITECTURE §8 note 1037): the visual twin of A.3's tell audit. Each
/// creature called in the creature gym as the gym calls it (note 676), a crewmate's eye put at the tell's warning distance from
/// it, looking at it, and the frame drawn twice per night (clear, dark, rain, fog): with the creature and without. What it puts
/// on the screen is what differs; how many pixels differ by a visible step, and by how much (CIE L*), is measured against
/// tells.json's floor. A tell that can't be seen is a bug.
/// </summary>
public static class TellAudit
{
    /// <summary>One frame's measure: <paramref name="Pixels"/> that show it (at 720p), the brightest tenth's lightness step
    /// (<paramref name="Contrast"/>, L*), and its whole footprint's mean; <paramref name="Seen"/> when both clear the floor.</summary>
    public sealed record Frame(string Condition, string From, double Pixels, double Contrast, double Mean, bool Seen, string? Shot);

    /// <summary>A tell: where the gym put it, what it is, and its frames; <paramref name="Staged"/> false when it couldn't be
    /// called (<paramref name="Why"/>).</summary>
    /// <param name="ByDesign">Why it's hidden on purpose until it acts (tells.json <c>hidden</c>): measured, never failing.</param>
    public sealed record Tell(string Kind, string What, double WarnM, bool Staged, string? Why, IReadOnlyList<Frame> Frames, string? Sheet,
        string? ByDesign = null)
    {
        /// <summary>Its frames that can't be seen, as "condition/from".</summary>
        public IEnumerable<string> Unseen => Frames.Where(f => !f.Seen).Select(f => $"{f.Condition}/{f.From}");
    }

    public sealed record Report(string Route, int Width, int Height, double MinPixels, double MinContrast, double Jnd,
        IReadOnlyList<Tell> Tells, int Frames, int Unseen, IReadOnlyList<string> Failing, IReadOnlyList<string> NotStaged,
        IReadOnlyList<string> ByDesign, string? Table);

    /// <param name="only">The tells to audit (tells.json's names); null for all of them.</param>
    /// <param name="conditions">The nights to draw them in; null for tells.json's.</param>
    /// <param name="output">Where the table, the contact sheets and (with <paramref name="frames"/>) each frame go; null for none.</param>
    public static Report Run(string content, IReadOnlyList<string>? only = null, IReadOnlyList<string>? conditions = null, string? output = "out/tells",
        bool frames = false)
    {
        var tuning = TellTuning.Load(content);
        var gymTuning = GymTuning.Load(content) with { Lamps = new GymTuning.LampsTuning { Count = 0 } };
        var gym = CreatureGym.Of(gymTuning, content);
        var setup = gym.Setup();
        using var night = NetPlaySession.HostGame(content, setup, port: null, bots: 0, listed: false);
        var host = night.Host!;
        gym.Begin(host);
        byte you = (byte)night.PlayerId;
        Step(night, gym, host, 2);
        using var shot = new WorldShot(content, host.World.Route, tuning.Width, tuning.Height);
        if (output is not null)
            Directory.CreateDirectory(output);
        var names = only ?? [.. tuning.Tells.Keys];
        var nights = conditions ?? [.. tuning.Conditions.Keys];
        double scale = 720.0 * 720.0 / ((double)tuning.Height * tuning.Height);
        var tells = new List<Tell>();
        foreach (var name in names)
        {
            if (!tuning.Tells.TryGetValue(name, out var tell))
                throw new ArgumentException($"no tell '{name}' in {TellTuning.File} (tells: {string.Join(", ", tuning.Tells.Keys)})");
            var kind = EnemyNames.Parse(name) ?? throw new ArgumentException($"'{name}' is no enemy kind");
            host.World.Clear();
            Step(night, gym, host, 0.5);
            host.Stand(you, Fresh(host, you));
            Step(night, gym, host, 0.2);
            var called = gym.Call(host, you, kind);
            Step(night, gym, host, tell.Settle ?? tuning.Settle);
            // The Choir gathers before it comes: its ghosts are what came.
            var ids = kind == EnemyKind.Choir
                ? [.. host.World.ActiveEnemies.Where(e => e.Kind == EnemyKind.Choir && !e.Gone).Select(e => e.Id)]
                : called.Ids.Where(id => host.World.ActiveEnemies.Any(e => e.Id == id && !e.Gone)).ToList();
            var subject = host.World.ActiveEnemies.FirstOrDefault(e => ids.Contains(e.Id) && e is not TippyToesie { Hidden: true });
            if (!called.Came || subject is null)
            {
                tells.Add(new Tell(name, tell.What, tell.WarnM, false, called.Came ? "came and went before the shot" : called.Where, [], null));
                continue;
            }
            var train = host.World.Train;
            var me = PlayerMotor.WorldPosition(host.Players.First(p => p.Id == you).State, train);
            var at = subject.WorldPosition(train);
            var crew = host.Players.ToList();
            var list = new List<Frame>();
            var tiles = new List<(byte[] Pixels, bool Seen)>();
            foreach (var from in tell.From)
            {
                // From the crew's side first; where that frame doesn't have it at all (a wall, a building, the car between),
                // from its other sides at the same distance: the audit is of the creature, not of where the gym stood it.
                var camera = Eye(tuning, tell, from, at, me, train.Line, subject.LineDistance, 0);
                foreach (double turn in (double[])[90, -90, 180])
                {
                    if (Measure(shot.Render(host.World, crew, camera), shot.Render(host.World, crew, camera, drawn: e => !ids.Contains(e.Id)), tuning.Jnd).Mean > 0)
                        break;
                    camera = Eye(tuning, tell, from, at, me, train.Line, subject.LineDistance, turn);
                }
                foreach (var c in nights)
                {
                    var weather = tuning.Conditions[c];
                    var lit = LampsOut(host.World, weather.LampsOut);
                    FrameLighting Grade(FrameLighting l)
                    {
                        if (weather.FogDensity > 0)
                            l.FogDensity = (float)weather.FogDensity;
                        if (weather.Wet)
                            l.Wetness = 1;
                        if (weather.LampsOut)
                            l.LampRange = 0.01f;
                        return l;
                    }
                    var with = shot.Render(host.World, crew, camera, grade: Grade);
                    var without = shot.Render(host.World, crew, camera, drawn: e => !ids.Contains(e.Id), grade: Grade);
                    LampsOut(host.World, false, lit);
                    var (pixels, contrast, mean) = Measure(with, without, tuning.Jnd);
                    bool seen = pixels * scale >= tuning.MinPixels && contrast >= tuning.MinContrast;
                    string? path = null;
                    if (output is not null && frames)
                        shot.Save(path = Path.Combine(output, $"{name}-{from}-{c}.png"), with);
                    list.Add(new Frame(c, from, Math.Round(pixels * scale), Math.Round(contrast, 1), Math.Round(mean, 1), seen, path));
                    tiles.Add((with, seen));
                }
            }
            string? sheet = output is null ? null : Sheet(Path.Combine(output, $"{name}.png"), tiles, tuning.Width, tuning.Height, nights.Count);
            tells.Add(new Tell(name, tell.What, tell.WarnM, true, null, list, sheet, tell.Hidden));
        }
        host.World.Clear();
        var failing = tells.Where(t => t.Staged && t.ByDesign is null && t.Unseen.Any()).Select(t => $"{t.Kind}: {string.Join(", ", t.Unseen)}").ToList();
        var report = new Report(gymTuning.Route, tuning.Width, tuning.Height, tuning.MinPixels, tuning.MinContrast, tuning.Jnd, tells,
            tells.Sum(t => t.Frames.Count), tells.Sum(t => t.ByDesign is null ? t.Frames.Count(f => !f.Seen) : 0), failing,
            [.. tells.Where(t => !t.Staged).Select(t => $"{t.Kind}: {t.Why}")],
            [.. tells.Where(t => t.ByDesign is not null).Select(t => $"{t.Kind}: {t.ByDesign}")], null);
        if (output is null)
            return report;
        string table = Path.Combine(output, "tells.md");
        File.WriteAllText(table, Table(report, nights));
        return report with { Table = Path.GetFullPath(table) };
    }

    /// <summary>
    /// The crewmate's eye: <see cref="TellTuning.Entry.WarnM"/> from the creature, toward where the crew are (the caller in the
    /// cab), at the viewpoint's height over the ground under it, looking at the tell's height on the creature.
    /// </summary>
    static Camera Eye(TellTuning tuning, TellTuning.Entry tell, string from, Double3 at, Double3 crew, RailLine line, double along, double turn)
    {
        var toward = (crew - at) with { Y = 0 };
        var way = toward.Length > 0.5 ? toward.Normalized : Double3.Cross(line.Sample(along).Tangent, Double3.Up).Normalized;
        double a = turn * Math.PI / 180;
        way = new Double3(way.X * Math.Cos(a) - way.Z * Math.Sin(a), 0, way.X * Math.Sin(a) + way.Z * Math.Cos(a));
        var spot = at + way * tell.WarnM;
        double hint = along;
        spot = spot with { Y = PlayerMotor.GroundAt(spot, line, ref hint) + tuning.Viewpoints[from] };
        return Camera.LookAt(spot, at + Double3.Up * tell.Aim, (float)tuning.Fov);
    }

    /// <summary>The headlamp and every car's lamp put out (or put back as <paramref name="was"/> had them); what they were.</summary>
    static bool[] LampsOut(World world, bool @out, bool[]? was = null)
    {
        var cars = world.Train.Vehicles;
        var before = new bool[cars.Count + 1];
        before[0] = world.LampLit;
        for (int i = 0; i < cars.Count; i++)
            before[i + 1] = cars[i].LampLit;
        if (was is not null)
        {
            world.LampLit = was[0];
            for (int i = 0; i < cars.Count; i++)
                cars[i].LampLit = was[i + 1];
        }
        else if (@out)
        {
            world.LampLit = false;
            foreach (var v in cars)
                v.LampLit = false;
        }
        return before;
    }

    /// <summary>L* of an RGBA pixel (Rec. 709 luminance of the linearised sRGB).</summary>
    static double L(byte[] p, int i)
    {
        double y = 0.2126 * Linear[p[i]] + 0.7152 * Linear[p[i + 1]] + 0.0722 * Linear[p[i + 2]];
        return y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : y * 24389.0 / 27;
    }

    static readonly double[] Linear = [.. Enumerable.Range(0, 256).Select(i => i / 255.0 is var c && c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4))];

    /// <summary>
    /// How the creature's frame differs from the frame without it: the pixels a step of <paramref name="jnd"/> L* or more apart,
    /// the brightest tenth of those steps (the tell's own contrast: an eye, a lit flank, an edge against the sky), and the mean
    /// over every pixel it changed at all.
    /// </summary>
    public static (int Pixels, double Contrast, double Mean) Measure(byte[] with, byte[] without, double jnd)
    {
        var steps = new List<double>();
        double sum = 0;
        int touched = 0;
        for (int i = 0; i < with.Length; i += 4)
        {
            if (with[i] == without[i] && with[i + 1] == without[i + 1] && with[i + 2] == without[i + 2])
                continue;
            double d = Math.Abs(L(with, i) - L(without, i));
            sum += d;
            touched++;
            if (d >= jnd)
                steps.Add(d);
        }
        if (steps.Count == 0)
            return (0, 0, touched == 0 ? 0 : sum / touched);
        steps.Sort();
        return (steps.Count, steps[(int)(steps.Count * 0.9)], sum / touched);
    }

    /// <summary>The tell's frames in a grid (a row per viewpoint, a column per night), each halved, framed green if seen and red if not.</summary>
    static string Sheet(string path, List<(byte[] Pixels, bool Seen)> tiles, int width, int height, int columns)
    {
        int w = width / 2, h = height / 2, pad = 4;
        int rows = (tiles.Count + columns - 1) / columns;
        int sw = columns * (w + pad * 2), sh = rows * (h + pad * 2);
        var sheet = new byte[sw * sh * 4];
        for (int t = 0; t < tiles.Count; t++)
        {
            int ox = t % columns * (w + pad * 2), oy = t / columns * (h + pad * 2);
            byte r = tiles[t].Seen ? (byte)40 : (byte)220, g = tiles[t].Seen ? (byte)190 : (byte)40;
            for (int y = 0; y < h + pad * 2; y++)
                for (int x = 0; x < w + pad * 2; x++)
                {
                    int o = ((oy + y) * sw + ox + x) * 4;
                    sheet[o + 3] = 255;
                    if (x < pad || y < pad || x >= w + pad || y >= h + pad)
                    {
                        (sheet[o], sheet[o + 1], sheet[o + 2]) = (r, g, 40);
                        continue;
                    }
                    int sx = (x - pad) * 2, sy = (y - pad) * 2;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        int a = (sy * width + sx) * 4 + ch, b = a + width * 4;
                        sheet[o + ch] = (byte)((tiles[t].Pixels[a] + tiles[t].Pixels[a + 4] + tiles[t].Pixels[b] + tiles[t].Pixels[b + 4]) / 4);
                    }
                }
        }
        PngWriter.Write(path, sheet, sw, sh, 1);
        return Path.GetFullPath(path);
    }

    static string Table(Report report, IReadOnlyList<string> nights)
    {
        var w = new System.Text.StringBuilder();
        w.AppendLine($"# Visual tells ({report.Route}, {report.Width}x{report.Height}; seen: >= {report.MinPixels} px at 720p and >= {report.MinContrast} L*)");
        w.AppendLine();
        w.AppendLine("Each cell: the pixels that show the creature (a step of the jnd or more, at 720p) and the brightest tenth's step; **bold** under the floor, **hidden** not drawn at all from there (behind a wall, indoors).");
        w.AppendLine();
        w.AppendLine($"| Tell | From | {string.Join(" | ", nights)} |");
        w.AppendLine($"|---|---|{string.Concat(nights.Select(_ => "---|"))}");
        foreach (var t in report.Tells)
        {
            if (!t.Staged)
            {
                w.AppendLine($"| {t.Kind} | — | not staged: {t.Why} |");
                continue;
            }
            foreach (var from in t.Frames.Select(f => f.From).Distinct())
                w.AppendLine($"| {t.Kind} ({t.WarnM} m){(t.ByDesign is null ? "" : $" *hidden by design: {t.ByDesign}*")} | {from} | {string.Join(" | ", nights.Select(n => t.Frames.FirstOrDefault(f => f.From == from && f.Condition == n) is { } f
                    ? f.Pixels == 0 && f.Mean == 0 ? "**hidden**" : $"{(f.Seen ? "" : "**")}{f.Pixels} px, {f.Contrast} L*{(f.Seen ? "" : "**")}" : "—"))} |");
        }
        return w.ToString();
    }

    static PlayerState Fresh(HostSession host, byte you)
    {
        var was = host.Players.First(p => p.Id == you).State;
        return PlayerMotor.SpawnInCab(host.World.Train, host.PlayerTuning) with { Placed = (byte)(was.Placed + 1) };
    }

    static void Step(NetPlaySession night, CreatureGym gym, HostSession host, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * SimConstants.TickRate); i++)
        {
            night.Step(default);
            gym.Step(host, host.Tick * SimConstants.TickSeconds);
        }
    }
}
