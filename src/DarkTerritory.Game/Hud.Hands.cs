using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The HUD's hands (note 281; GDD §32 "The HUD: your hands and the dark"). The director, 7 Oct: "There's too much UI on
/// screen. I like the way Repo and Lethal Company do their UI/UX designs." As theirs do: a dot to aim with; small slots
/// with a picture of what's in them, the name only for a moment after a change of hands; in the corner, in fine print,
/// what what you hold (or the cab, or the gun) does; at the crosshair only what you're looking at. Everything else comes
/// up when it matters and fades when it doesn't, and nothing in play sits on a plate.
/// </summary>
public static partial class Hud
{
    /// <summary>The HUD's timings and thresholds (content/tuning/hud.json): the app and <c>dt</c> set it from the content.</summary>
    public static HudTuning Tuning { get; set; } = new();

    /// <summary>Fine print: the HUD's own text at half size where that's still crisp (<see cref="PromptScaleAt"/>).</summary>
    static float Fine => _promptScale;

    /// <summary>
    /// What the HUD remembers from frame to frame, to show a thing for a while after it changes: a change of hands, a place
    /// coming up, a deeper step of the cold. Per session, on its tick, so a still frame (<c>dt screenshot</c>) and a test
    /// see what a player would at that moment.
    /// </summary>
    sealed class Memory
    {
        public WeakReference<IPlaySession>? Session;
        public int Slot;
        public bool Carrying;
        public long SlotTick = long.MinValue / 2;
        public (string Name, double At)? Place;
        public long PlaceTick = long.MinValue / 2;
        public int ColdStep;
        public long ColdTick = long.MinValue / 2;
    }

    [ThreadStatic] static Memory? _memory;

    /// <summary>The memory for this session, brought up to its tick: a new session starts one.</summary>
    static Memory Observe(IPlaySession s)
    {
        if (_memory is not { Session: { } held } m || !held.TryGetTarget(out var was) || !ReferenceEquals(was, s))
            _memory = m = new Memory { Session = new(s), Slot = s.Player.HeldSlot, Carrying = s.World.Bodies.CarriedBy(s.PlayerId) is not null };
        long now = s.Tick;
        bool carrying = s.World.Bodies.CarriedBy(s.PlayerId) is not null;
        if (s.Player.HeldSlot != m.Slot || carrying != m.Carrying)
            (m.Slot, m.Carrying, m.SlotTick) = (s.Player.HeldSlot, carrying, now);
        if (s.Route is { } route && Ahead(route, s.Train.Dynamics.Distance) is { } next
            && next.At - s.Train.Dynamics.Distance <= Tuning.PlaceAheadMetres && m.Place != next)
            (m.Place, m.PlaceTick) = (next, now);
        int step = s.Player.Alive ? PlayerMotor.ColdStep(s.Player, s.Train) : 0;
        if (step > m.ColdStep)
            m.ColdTick = now;
        m.ColdStep = step;
        return m;
    }

    /// <summary>Seconds since a tick of this session's.</summary>
    static double Since(IPlaySession s, long tick) => (s.Tick - tick) * SimConstants.TickSeconds;

    /// <summary>
    /// How much of something shown for <paramref name="hold"/> seconds is left to see at <paramref name="age"/>: all of it
    /// at once (it pops up, as a call should), fading over the last <see cref="HudTuning.FadeSeconds"/>; none outside it.
    /// </summary>
    static float Shown(double age, double hold) =>
        age < 0 || age > hold ? 0 : (float)Math.Clamp((hold - age) / Math.Max(1e-3, Tuning.FadeSeconds), 0, 1);

    /// <summary>The next place along the main line and where it is (m along the line): a landmark, the terminus, a stop.</summary>
    public static (string Name, double At)? Ahead(Route route, double s) =>
        route.Plan?.Landmarks.Where(p => p.Edge == "main" && p.S0 > s).MinBy(p => p.S0) is { } place ? (place.Name, place.S0)
        : route.Plan is { } plan ? plan.Terminus.GateM > s ? (plan.Terminus.Name, plan.Terminus.GateM) : null
        : route.NextLandmark(s) is { } l ? ((l.Kind == FeatureKind.Facility ? $"{l.Facility}" : $"{l.Kind}"), l.Start) : null;

    // Each tool's picture for its slot, 11 by 11: '#' drawn. The top-left corner is left clear for the slot's number.
    static readonly Dictionary<Tool, string[]> Pictures = new()
    {
        [Tool.None] = ["...........", "......#....", "....#.#.#..", "....#.#.#.#", "..#.#.#.#.#", "..#.#####.#", "..########.",
            "..#######..", "...######..", "....####...", "....####..."],
        [Tool.Crowbar] = ["........##.", ".......#..#", ".......#...", "......##...", ".....##....", "....##.....", "...##......",
            "..##.......", ".##........", "##.........", "#.#........"],
        [Tool.Shovel] = ["........###", "........#.#", "........##.", ".......#...", "......#....", "..#..#.....", "..####.....",
            ".#####.....", "######.....", ".####......", "..##......."],
        [Tool.Wrench] = ["........#.#", ".......##.#", ".......####", "......###..", ".....##....", "....##.....", "...##......",
            "..##.......", ".###.......", "#.##.......", "##........."],
    };

    static readonly string[] RadioPicture = ["......#....", "......#....", "....#####..", "....#...#..", "....#####..", "....#.#.#..",
        "....#####..", "....#.#.#..", "....#####..", "....#.#.#..", "....#####.."];

    /// <summary>A tool's name, as the hotbar says it for a moment after a change of hands.</summary>
    public static string ToolName(Tool tool) => tool == Tool.None ? "HANDS" : tool.ToString().ToUpperInvariant();

    /// <summary>The side of a hotbar slot, in canvas pixels (60 screen pixels at 1080p).</summary>
    public const float SlotSize = 15;

    /// <summary>
    /// The hotbar (T108; note 281): bottom centre, a small square for each slot with a tool in it and for the one in hand
    /// (empty, your hands), each with its tool's picture and its number; the one in hand lit. The tool's name over it for a
    /// moment after a change of hands. With something in both hands the slots dim (the corner names what's held). A radio
    /// you're wearing is a slot of its own at the right, with its key; a broken one's drawn red.
    /// </summary>
    static void Hotbar(Overlay o, int width, int height, IPlaySession s, Memory m)
    {
        var p = s.Player;
        var bodies = s.World.Bodies;
        bool carrying = bodies.CarriedBy(s.PlayerId) is not null;
        var slots = Enumerable.Range(0, Kit.Slots).Where(i => Kit.At(p.Kit, i) != Tool.None || (i == p.HeldSlot && !carrying)).ToList();
        bool radio = bodies.RadiosCarried && bodies.HasRadio(s.PlayerId);
        bool broken = !radio && bodies.All.Any(b => b.Kind == BodyKind.Radio && b.Carrier == s.PlayerId && b.Broken);
        const float Gap = 2, RadioGap = 6;
        float w = slots.Count * (SlotSize + Gap) - Gap + (radio || broken ? RadioGap + SlotSize : 0);
        float x = MathF.Round((width - w) / 2), y = height - SlotSize - 6;
        foreach (int i in slots)
        {
            bool held = i == p.HeldSlot && !carrying;
            Slot(o, x, y, Pictures[Kit.At(p.Kit, i)], $"{i + 1}", held, carrying ? 0.45f : 1);
            x += SlotSize + Gap;
        }
        if (radio || broken)
            Slot(o, x - Gap + RadioGap, y, RadioPicture, Controls.KeyLabel(Keys.KeyFor(Control.Radio)), false, 1, broken ? Red : null);
        float named = carrying ? 0 : Shown(Since(s, m.SlotTick), Tuning.ToolNameSeconds);
        if (named > 0)
            o.TextCentred(width / 2f, y - 4 - o.Font.Height * Fine, ToolName(Kit.At(p.Kit, p.HeldSlot)), Ink with { W = named }, Fine);
    }

    /// <summary>One slot: a dark square, its edge lit for the one in hand, the picture, and its key small in the corner.</summary>
    static void Slot(Overlay o, float x, float y, string[] picture, string key, bool lit, float alpha, Vector4? tint = null)
    {
        o.Rect(x, y, SlotSize, SlotSize, UiStyle.Iron with { W = (lit ? 0.6f : 0.4f) * alpha });
        o.Outline(x, y, SlotSize, SlotSize, (lit ? UiStyle.Lit : Dim) with { W = (lit ? 0.95f : 0.3f) * alpha });
        var ink = (tint ?? (lit ? Ink : Dim)) with { W = alpha };
        float px = x + (SlotSize - 11) / 2, py = y + (SlotSize - 11) / 2;
        for (int row = 0; row < picture.Length; row++)
            for (int col = 0; col < picture[row].Length;)
            {
                if (picture[row][col] != '#')
                {
                    col++;
                    continue;
                }
                int from = col;
                while (col < picture[row].Length && picture[row][col] == '#')
                    col++;
                o.Rect(px + from, py + row, col - from, 1, ink);
            }
        o.Text(x + 1.5f, y + 1.5f, key, (lit ? UiStyle.Lit : Dim) with { W = 0.9f * alpha }, Fine);
    }

    /// <summary>
    /// The corner (note 281): what what's in your hands, or where you are, lets you do; Lethal Company's item tips. A head
    /// (the speed when you can drive, what you're carrying, the gun and its shot, the crane) over lines of a key and a few
    /// words. Null head and no lines when there's nothing to say, which is most of the time.
    /// </summary>
    public static (string? Head, List<string> Lines) Hints(IPlaySession s)
    {
        var p = s.Player;
        var train = s.Train;
        var world = s.World;
        var lines = new List<string>();
        if (!p.Alive || world.Run?.Report is not null)
            return (null, lines);
        // Carried in both hands (spec D.2, note 264): what it is, what it's for, and how to be rid of it.
        if (world.Bodies.CarriedBy(s.PlayerId) is { } carried)
        {
            switch (carried.Kind)
            {
                case BodyKind.Heavy when !carried.Lifted:
                    lines.AddRange(["IT NEEDS TWO", "[E] LET GO"]);
                    break;
                case BodyKind.Heavy:
                    lines.AddRange(["TOGETHER, INTO A CAR", "[E] PUT IT DOWN"]);
                    break;
                case BodyKind.RepairKit when train.Boiler.Ruptured:
                    lines.AddRange(["TO THE FIREBOX WITH IT", "[E] PUT DOWN"]);
                    break;
                default:
                    lines.AddRange(carried.Kind switch
                    {
                        BodyKind.Cargo => ["INTO A CAR TO LOAD IT"],
                        // A village find (level-design P12): it pays once it's put down aboard, in any car.
                        BodyKind.Loot => ["INTO ANY CAR TO KEEP IT"],
                        BodyKind.RepairKit => ["MENDS THE BOILER, OPENS A LOCK QUIETLY"],
                        _ => Array.Empty<string>(),
                    });
                    lines.AddRange(["[E] PUT DOWN", "[RMB] THROW"]);
                    break;
            }
            return (Called(world, carried), lines);
        }
        // T112: sat at the gun, its own controls.
        if (world.Combat is { } combat && p.Has(PlayerFlags.Seated) && Guns.MannedGun(p, train, combat.Guns) is { } manned)
        {
            var gun = train.Vehicles[manned].Gun;
            bool steam = train.BoilerTuning is null || train.Boiler.Pressure >= combat.Guns.MinPressure;
            if (!gun.Jammed && gun.ReloadNeeded == 0 && gun.Ammo > 0 && steam)
                lines.Add("[LMB] FIRE");
            lines.Add("[SPACE] GET UP");
            return ($"THE GUN: {gun.Ammo} SHOT", lines);
        }
        // The crane (T48), from its cab.
        if (p.Has(PlayerFlags.Operating) && world.Run?.CurrentSite?.CraneNear(PlayerMotor.WorldPosition(p, train)) is { } crane)
        {
            lines.AddRange(["[WASD] BRIDGE AND TROLLEY", "[SPACE] HOOK"]);
            if (crane.Hooked is not null)
                lines.Add("[LMB] LET GO (SET IT DOWN FIRST)");
            lines.Add("LET GO OF [E] TO STEP DOWN");
            return ("THE CRANE", lines);
        }
        if (CabControls.CanDrive(p, train))
        {
            // T97: steam drives it. At a stand on the brake, R lets it off; otherwise B brakes (coal and the vent do the rest).
            bool steam = train.BoilerTuning?.SteamDrive == true;
            if (!steam)
                lines.AddRange(["[R/F] REGULATOR", "[B] BRAKE"]);
            else
                lines.Add(s.Controls.Brake > 0 && train.Dynamics.Speed < CabControls.StandingBelow ? "[R] RELEASE BRAKE" : "[B] BRAKE");
            // Note 267: the brake and the vent, one key each, held.
            if (train.BoilerTuning is not null)
                lines.Add("[VENT] HOLD: VENT");
            lines.Add(world.LampOutSeconds > 0 ? $"LAMP SMASHED ({world.LampOutSeconds:0}S)" : $"[L] LAMP {(world.LampLit ? "OFF" : "ON")}");
            // The reverser only when it's back: forward goes without saying, and going backwards shouldn't.
            if (s.Controls.Reverser < 0)
                lines.Add("[X] IN REVERSE");
            string speed = $"{Math.Abs(train.Dynamics.Speed) * 3.6:0} KM/H";
            return (steam ? speed : $"{speed}  REG {s.Controls.Throttle * 100:0}%", lines);
        }
        return (null, lines);
    }

    /// <summary>The corner, drawn: bottom right, the head at the HUD's size over the lines in fine print, no plate.</summary>
    static void Corner(Overlay o, int width, int height, IPlaySession s)
    {
        var (head, lines) = Hints(s);
        if (head is null && lines.Count == 0)
            return;
        float k = Fine, right = width - 6, row = (o.Font.LineHeight + 4) * k;
        float y = height - 6 - lines.Count * row;
        foreach (string written in lines)
        {
            string text = Bound(written);
            UiStyle.Keyed(o, Overlay.Snap(right - UiStyle.MeasureKeyed(o, text, k), k), y + 2 * k, text, text.Contains('[') ? Ink : Dim, k);
            y += row;
        }
        if (head is not null)
            o.TextRight(right, height - 6 - lines.Count * row - o.Font.LineHeight - 2, head, Ink, 1);
    }

    /// <summary>
    /// The crosshair (note 281): a dot, as Lethal Company's; with something under it you can use (a prompt), four corners
    /// round it, so the eye finds the prompt under it. The hit marker over both.
    /// </summary>
    static void Crosshair(Overlay o, int width, int height, IPlaySession s, bool usable)
    {
        float cx = MathF.Round(width / 2f), cy = MathF.Round(height / 2f);
        o.Rect(cx, cy, 1, 1, Ink with { W = 0.7f });
        if (usable)
            foreach (var (dx, dy) in new[] { (-3, -3), (3, -3), (-3, 3), (3, 3) })
                o.Rect(cx + dx, cy + dy, 1, 1, Ink with { W = 0.55f });
        HitMarker(o, cx, cy, s);
    }
}
