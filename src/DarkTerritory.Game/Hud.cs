using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The flat-screen HUD (T23), drawn in the low-res frame's own pixels with the pixel font. Since note 285 (the director,
/// 7 Oct: "too much UI on screen ... I like the way Repo and Lethal Company do their UI/UX designs"; GDD §32 "The HUD: your
/// hands and the dark") only the crosshair and your hands are always there, and nothing in play sits on a plate:
/// <list type="bullet">
/// <item>bottom centre: the hotbar, a picture to a slot, the name for a moment after a change of hands (Hud.Hands);</item>
/// <item>bottom right, in fine print: what what you hold, or the cab, or the gun, lets you do; the speed if you're driving;</item>
/// <item>under the crosshair, in fine print: what you're looking at, and its key;</item>
/// <item>top centre, for a while: a place coming up, the cold getting deeper, and the dawn clock in the night's last stretch;</item>
/// <item>top right: the ping, big in the lobby (spec E: "shown prominently"), out on the line only when it's bad or lost;</item>
/// <item>top left, in fine print: the lobby in the yard, and what a stop's waiting on while the train's at one;</item>
/// <item>centre: alarms (a rupture, a grab, a bend too fast), death and the night's result.</item>
/// </list>
/// The rest is read in the world (the cab's gauges, the route card on C, the supplies on I, the crew on Q) or heard.
/// </summary>
public static partial class Hud
{
    static readonly Vector4 Ink = new(0.88f, 0.84f, 0.74f, 1);
    static readonly Vector4 Dim = new(0.60f, 0.58f, 0.53f, 1);
    static readonly Vector4 Amber = new(1.00f, 0.70f, 0.30f, 1);
    static readonly Vector4 Red = new(0.95f, 0.26f, 0.18f, 1);
    static readonly Vector4 Green = new(0.55f, 0.82f, 0.45f, 1);
    static readonly Vector4 Track = new(0.25f, 0.24f, 0.22f, 0.9f);

    /// <param name="crosshair">The aiming cross at the middle. Not on a headset's panel (T36): it lags the head, which
    /// does the aiming, so a cross on it would point somewhere else.</param>
    /// <param name="commendations">The awards given at the run's end (GDD App. D.12), shown under its report: to whom, what,
    /// and from whom. Awarding isn't in the game yet (it needs the run-end screen's input and the profile's tally), so only
    /// a still frame passes them (<c>dt screenshot --hud --report ... --commend</c>).</param>
    /// <param name="stills">The night's bookmark stills taken on this machine (GDD v1.4 App. D.12, <see cref="BookmarkStills"/>),
    /// by bookmark id: the report shows each beside its line.</param>
    /// <param name="pixels">How many of the drawn image's pixels each of the canvas's is (the renderer's height over the
    /// canvas's): the prompt's fine print is as small as stays crisp at that (<see cref="PromptScaleAt"/>).</param>
    public static void Build(Overlay o, int width, int height, IPlaySession s, bool crosshair = true,
        IReadOnlyList<(string To, UiStyle.Commendation What, string From)>? commendations = null, IReadOnlyDictionary<int, Still>? stills = null,
        float pixels = 4)
    {
        _promptScale = PromptScaleAt(pixels);
        _commendations = commendations;
        _stills = stills;
        o.Clear();
        int line = o.Font.LineHeight;
        var p = s.Player;
        // The derailment's sequence (T117, T121) has the screen: first-hand, the replay with its cause, the orbit. Nothing
        // but the replay's caption over it.
        if (s.WreckCinematic)
        {
            Film(o, width, height, s);
            Alerts(o, width, height, s, line);
            Skip(o, width, height, s);
            return;
        }
        // App. F.1's damage feedback (note 272): under everything else, so the text stays readable through it.
        EdgeFlash(o, width, height, HurtStrength(s));
        if (s.StrandedOutro)
            Skip(o, width, height, s);
        // GDD §9: the fortress on the radio (the manifest leaving, the tally home) has the top of the screen while it reads.
        if (s.RadioReading is { } reading)
            RadioCard(o, width, height, reading, s.RadioSeconds, s.World.Run?.Tuning.Radio ?? new(), s.RadioTimes);
        var memory = Observe(s);
        // The night over, its report has the screen: no prompts, hands or places over it.
        bool over = s.World.Run?.Report is not null;
        Link(o, width, s);
        if (!over)
        {
            TopCentre(o, width, s, memory, line);
            Situation(o, s);
        }
        Alerts(o, width, height, s, line);
        // (Not while the link is lost: who's aboard is stale, and the reconnecting message has the screen.)
        if (s.Link is { Lost: false } lobby && s.World.Run is { Phase: Sim.Run.RunPhase.Yard })
            Lobby(o, s, lobby, line);
        string? prompt = over ? null : Prompt(s);
        if (prompt is not null)
            PromptPlate(o, width, height, Bound(prompt));
        if (p.Alive && !over)
        {
            Hotbar(o, width, height, s, memory);
            Corner(o, width, height, s);
            Noise(o, width, height, s.World);
            if (crosshair)
                Crosshair(o, width, height, s, prompt is not null);
        }
    }

    /// <summary>
    /// The director's damage model (GDD App. F.1, 6 Oct 2026; note 272): "damage feedback is minimal: an edge flash and a
    /// sound". The flash fades over this long (s) from a hit; a hit this big (a heavy hit, enemies.json <c>damage</c>) fills
    /// it, a smaller one fills it in proportion, never under <see cref="HurtFlashLeast"/>.
    /// </summary>
    public const double HurtFlashSeconds = 0.6, HurtFlashFullAt = 60, HurtFlashLeast = 0.35;

    // Per session: whose health the flash watches, what it was last frame, and the last hit's tick and strength.
    sealed class Hurt
    {
        public int Watching = -1, Health;
        public long Tick = long.MinValue;
        public double Strength;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPlaySession, Hurt> _hurts = new();

    /// <summary>A still frame's flash (<c>dt screenshot --hud --hurt s</c>): at this strength, fresh, whatever the health did.</summary>
    public static double? StagedHurt { get; set; }

    /// <summary>
    /// How strong the edge flash is now (0 to 1): this machine's player's health fell, by how much, and how long ago (from
    /// the replicated health, so it's what the host applied). Called once a frame, by <see cref="Build"/>.
    /// </summary>
    public static double HurtStrength(IPlaySession s)
    {
        int health = s.Player.Health;
        long now = s.HostTick;
        var hurt = _hurts.GetOrCreateValue(s);
        if (hurt.Watching != s.PlayerId)
            (hurt.Watching, hurt.Tick) = (s.PlayerId, long.MinValue);
        else if (health < hurt.Health)
            (hurt.Tick, hurt.Strength) = (now, Math.Clamp((hurt.Health - health) / HurtFlashFullAt, HurtFlashLeast, 1));
        hurt.Health = health;
        if (StagedHurt is { } staged)
            return Math.Clamp(staged, 0, 1);
        double age = (now - hurt.Tick) * Sim.SimConstants.TickSeconds;
        if (hurt.Tick == long.MinValue || age < 0 || age > HurtFlashSeconds)
            return 0;
        double left = 1 - age / HurtFlashSeconds;
        return hurt.Strength * left * left;
    }

    /// <summary>
    /// The red edge flash (note 272): a band round the frame's edge, deepest at the rim and gone a ninth of the way in, the
    /// corners a little deeper where the bands cross. Nothing in the middle of the screen: you see what hit you.
    /// </summary>
    public static void EdgeFlash(Overlay o, int width, int height, double strength)
    {
        if (strength <= 0.01)
            return;
        int bands = Math.Max(8, height / 9);
        for (int k = 0; k < bands; k++)
        {
            float fall = 1 - (float)k / bands;
            var colour = new Vector4(0.62f, 0.03f, 0.02f, (float)strength * 0.6f * fall * fall);
            o.Rect(0, k, width, 1, colour);
            o.Rect(0, height - 1 - k, width, 1, colour);
            o.Rect(k, k + 1, 1, height - 2 * k - 2, colour);
            o.Rect(width - 1 - k, k + 1, 1, height - 2 * k - 2, colour);
        }
    }

    /// <summary>How long the crosshair's hit marker shows after a blow or a ball of yours lands (s).</summary>
    public const double HitMarkerSeconds = 0.3;

    /// <summary>
    /// T121 playtest ("all creatures need hit confirm feedback"): a blow or a ball of yours that landed on a creature puts four
    /// short ticks round the cross for a moment, off the diagonals, opening out as they fade; red when it was the kill.
    /// From the host's replicated record (World.Hits), so it's what landed, not what you hoped did.
    /// </summary>
    public static void HitMarker(Overlay o, float cx, float cy, IPlaySession s)
    {
        Sim.Combat.HitConfirm? mine = null;
        foreach (var h in s.World.Hits)
            if (h.By == s.PlayerId && (mine is null || h.Tick > mine.Value.Tick))
                mine = h;
        if (mine is not { } hit)
            return;
        double age = (s.HostTick - hit.Tick) * Sim.SimConstants.TickSeconds;
        if (age < 0 || age > HitMarkerSeconds)
            return;
        float fade = (float)(1 - age / HitMarkerSeconds);
        var colour = (hit.Killed ? Red : Ink) with { W = 0.9f * fade };
        float from = 4 + 3 * (1 - fade);
        // Each tick a short run of pixels out along a diagonal.
        foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            for (int k = 0; k < 4; k++)
                o.Rect(MathF.Round(cx + dx * (from + k)), MathF.Round(cy + dy * (from + k)), 1, 1, colour);
    }

    /// <summary>
    /// The lobby (T116, the co-op games' way: Lethal Company's ship, PEAK's airport): top left while the train's in the yard,
    /// with no plate and in fine print (note 285): who's aboard, how friends get in, and how the night starts.
    /// Drop-in is open here; once the train's out the gate, only at a facility (spec E).
    /// </summary>
    static void Lobby(Overlay o, IPlaySession s, LinkInfo link, int line)
    {
        var crew = s.Roster();
        float k = Fine, x = 6, y = 6;
        // Of the crew cap (note 254), when there is one.
        o.Text(x, y, $"THE LOBBY: {crew.Count} ABOARD{(link.Cap > 0 ? $" OF {link.Cap}" : "")}", Amber, k);
        y += (line + 3) * k;
        var lines = crew.Select(c => ($"  {c.Name}{(c.You && c.Name != "YOU" ? " (YOU)" : "")}", c.You ? Ink : Dim)).ToList();
        // The host starts the night; a joiner waits for it (the 4 Oct rehearsal: a joiner was told to drive out).
        bool hosting = link.PingMs is null && !link.Lost;
        // Hosting at the cap: the lobby's shut (listed FULL, the platform lobby closed) till a place frees. The places taken
        // can be more than the names above: a dropped player's held place, someone waiting to board.
        if (link.Full)
            lines.Add(($"CREW FULL ({link.Places}/{link.Cap}): NOBODY ELSE CAN JOIN", Red));
        else if (link.JoinAt is { } at)
        {
            lines.Add((link.Listed ? "FRIENDS: JOIN, YOUR GAME'S LISTED" : "A PRIVATE LOBBY: FRIENDS JOIN BY INVITE", Dim));
            lines.Add(($"  (OR THEY TYPE {at})", Dim));
        }
        else if (hosting)
            lines.Add(("A PRIVATE NIGHT: NOBODY ELSE CAN JOIN", Dim));
        lines.Add((hosting ? "EVERYONE IN? DRIVE OUT OF THE YARD" : "THE HOST DRIVES OUT WHEN EVERYONE'S IN", Ink));
        foreach (var (text, colour) in lines)
        {
            o.Text(x, y, text, colour, k);
            y += (line + 1) * k;
        }
    }

    /// <summary>
    /// The crew's loudness meter (T113 playtest: "no counterplay" for the Choir), over the hotbar, and only once it matters
    /// (note 285): the crew loud enough to count (hud.json <c>noiseShowAt</c> of the Choir's threshold), or the Choir
    /// gathering or here. How loud against its threshold (the tick), and how far it's gathered. Seeing it climb is the
    /// counterplay: go quiet before it fills.
    /// </summary>
    static void Noise(Overlay o, int width, int height, World world)
    {
        if (world.Combat is not { } c || world.Choir.Spent)
            return;
        var ch = world.Choir;
        bool over = ch.Loudness >= c.Choir.Threshold;
        if (!ch.Present && ch.Build <= 0 && ch.Loudness < c.Choir.Threshold * Tuning.NoiseShowAt)
            return;
        float k = Fine, w = 60, h = 2, x = MathF.Round((width - w) / 2), y = height - SlotSize - 6 - 16;
        double loud = Math.Clamp(ch.Loudness / (c.Choir.Threshold * 2), 0, 1);
        o.TextCentred(width / 2f, y - 2 - o.Font.Height * k, ch.Present ? "THE CHOIR IS HERE: SILENCE"
            : ch.Rest > 0 ? "NOISE (THE CHOIR'S DRIVEN OFF)" : over ? "TOO LOUD" : "NOISE", ch.Present || over ? Amber : Dim, k);
        o.Rect(x, y, w, h, Dim with { W = 0.3f });
        o.Rect(x, y, (float)(w * loud), h, over ? Amber : Ink with { W = 0.6f });
        o.Rect(x + w / 2, y - 1, 1, h + 2, Ink); // the threshold
        if (ch.Build > 0 && !ch.Present)
            o.Rect(x, y + h + 1, (float)(w * ch.Build), 1, Red);
    }

    /// <summary>
    /// The crew roster (T69, held Q): the session's crew by name, and who's speaking now. Not who's aboard, or where, or
    /// alive: GDD v1.4 made roll call verbal (open question 2). <paramref name="heard"/>: seconds since a crewmate's voice
    /// last came in, null for never.
    /// </summary>
    public static void Roster(Overlay o, int width, int height, IReadOnlyList<RosterLine> lines, Func<byte, double?>? heard)
    {
        const string title = "THE CREW. ROLL CALL IS SHOUTED";
        float k = Fine;
        float names = lines.Count == 0 ? 0 : lines.Max(l => o.Measure(l.Name, k));
        var (x, y, w) = Panel(o, width, height, title, Math.Max(o.Measure(title, k), names + 12 * k + o.Measure("SPEAKING", k)), lines.Count, Ink, k);
        foreach (var l in lines)
        {
            o.Text(x + PanelPad * k, y, l.Name, Ink, k);
            if (!l.You && heard?.Invoke(l.Id) is < 2)
                o.Text(Overlay.Snap(x + w - PanelPad * k - o.Measure("SPEAKING", k), k), y, "SPEAKING", Green, k);
            y += PanelRow(o, k);
        }
    }

    const float PanelPad = 6;

    static float PanelRow(Overlay o, float k) => (o.Font.LineHeight + 2) * k;

    /// <summary>
    /// A panel you open (the roster, the supplies; note 316): note 285's form, as the ballot is. Fine print on a dark
    /// backing lit along its top, no rivets, a fifth of the way down the middle. Draws the backing and the title and returns
    /// where its rows start, and its width, for <paramref name="rows"/> rows under a title in <paramref name="titleColour"/>.
    /// </summary>
    static (float X, float Y, float W) Panel(Overlay o, int width, int height, string title, float content, int rows, Vector4 titleColour, float k)
    {
        float pad = PanelPad * k;
        // The last row's leading isn't kept below it, so the bottom margin matches the top's.
        float w = MathF.Round(content + 2 * pad), h = MathF.Round((o.Font.LineHeight + 4) * k + rows * PanelRow(o, k) - (rows > 0 ? 2 * k : 0) + 2 * pad);
        float x = MathF.Round((width - w) / 2), y = MathF.Round(height * 0.2f);
        o.Rect(x, y, w, h, UiStyle.Iron with { W = 0.6f });
        o.Rect(x, y, w, 1, titleColour with { W = 0.8f });
        o.Text(x + pad, y + pad, title, titleColour, k);
        return (x, y + pad + (o.Font.LineHeight + 4) * k, w);
    }

    /// <summary>
    /// The supplies aboard (the director's decision of 2026-10-06, GDD §10; note 264): one panel, toggled on (its key, I),
    /// never always on: the coal, the repair kit and where it is, the extinguishers, the cargo and crates, the stores (lamps,
    /// radios, toys, finds) and the powder and shot. Like the manifest board in a guard van, read when it's wanted.
    /// </summary>
    public static void Supplies(Overlay o, int width, int height, IPlaySession s)
    {
        var lines = SuppliesLines(s.World, s.PlayerId);
        const string title = "SUPPLIES ABOARD";
        string close = Bound("CLOSE : [I]");
        float k = Fine, pad = PanelPad * k;
        float col = lines.Max(l => o.Measure(l.Item, k)) + 12 * k;
        float content = Math.Max(o.Measure(title, k) + 12 * k + UiStyle.MeasureKeyed(o, close, k), col + lines.Max(l => o.Measure(l.Value, k)));
        var (x, y, w) = Panel(o, width, height, title, content, lines.Count, Amber, k);
        UiStyle.Keyed(o, Overlay.Snap(x + w - pad - UiStyle.MeasureKeyed(o, close, k), k), y - (o.Font.LineHeight + 4) * k, close, Dim, k);
        foreach (var (item, value, warn) in lines)
        {
            o.Text(x + pad, y, item, Dim, k);
            o.Text(Overlay.Snap(x + pad + col, k), y, value, warn ? Amber : Ink, k);
            y += PanelRow(o, k);
        }
    }

    /// <summary>The supplies panel's rows (note 264): what, how much or where, and whether it's short.</summary>
    public static List<(string Item, string Value, bool Warn)> SuppliesLines(Sim.World world, int playerId)
    {
        var train = world.Train;
        var consist = train.Dynamics.Consist;
        var bodies = world.Bodies.All.Where(b => b.Carrier >= 0 || consist.IndexOf(b.Parent) >= 0).ToList();
        int Count(BodyKind kind) => bodies.Count(b => b.Kind == kind);
        var rows = new List<(string, string, bool)>();
        if (train.BoilerTuning is { } bt)
            rows.Add(("COAL", $"{train.Boiler.Tender:0} IN THE TENDER, FIRE {train.Boiler.Firebox:0.0}", train.Boiler.Tender < 40 || train.Boiler.LowFire(bt)));
        int kits = Count(BodyKind.RepairKit);
        rows.Add(("REPAIR KIT", KitWhere(world, playerId) + (kits > 1 ? $" (+{kits - 1} SPARE)" : ""), kits == 0));
        rows.Add(("EXTINGUISHERS", $"{Count(BodyKind.Extinguisher)} ABOARD", Count(BodyKind.Extinguisher) == 0));
        var cargo = consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        rows.Add(("CARGO", $"{cargo.Count(v => v.Load > 0.01)} OF {cargo.Count} CARS LOADED, {cargo.Sum(v => v.Load):0.0} LOADS; {Count(BodyKind.Crate) + Count(BodyKind.Cargo)} CRATES", false));
        int finds = world.Run?.Stowed.Count ?? 0;
        rows.Add(("STORES", $"{Count(BodyKind.Lamp)} LAMPS, {Count(BodyKind.Radio)} RADIOS, {Count(BodyKind.Toy)} TOYS, {finds + Count(BodyKind.Loot)} FINDS", false));
        int rounds = train.Vehicles.Where(v => v is not null).Sum(v => v.Gun.Ammo);
        if (world.Combat is not null)
            rows.Add(("POWDER AND SHOT", $"{rounds} ROUNDS", rounds < 5));
        return rows;
    }

    /// <summary>Where the handiest repair kit is, in a few words (the supplies panel's; note 264).</summary>
    static string KitWhere(Sim.World world, int playerId)
    {
        var consist = world.Train.Dynamics.Consist;
        var kit = world.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit)
            .OrderBy(b => b.Carrier == playerId ? 0 : b.Carrier >= 0 ? 1 : consist.IndexOf(b.Parent) >= 0 ? 2 + consist.IndexOf(b.Parent) : 1000).ThenBy(b => b.Id)
            .FirstOrDefault();
        if (kit is null)
            return "NONE ABOARD";
        if (kit.Carrier == playerId)
            return "IN YOUR HANDS";
        if (kit.Carrier >= 0)
            return $"WITH {IncidentLog.NameOf(world, kit.Carrier).ToUpperInvariant()}";
        int car = consist.IndexOf(kit.Parent);
        if (car > 0 && kit.Stowed && kit.Locker < world.Train.Frames[kit.Parent].Shape.Lockers.Count)
            return $"THE {world.Train.Frames[kit.Parent].Shape.Lockers[kit.Locker].Name}'S LOCKER, CAR {car}";
        return car > 0 ? $"ON THE FLOOR, CAR {car}" : car == 0 ? "ON THE ENGINE" : "OFF THE TRAIN";
    }

    /// <summary>
    /// The link, top right (note 285). In the lobby the ping to the host, big (spec E: "shown prominently ... in browser and
    /// lobby"); out on the line only once it's bad (hud.json <c>pingWarnMs</c>) or gone, and then what's being done about it.
    /// The host has no ping to show; the crew's count and roles are the roster's (Q).
    /// </summary>
    static void Link(Overlay o, int width, IPlaySession s)
    {
        if (s.Link is not { } link)
            return;
        float k = Fine, right = width - 6;
        int line = o.Font.LineHeight;
        if (link.Lost)
        {
            o.TextRight(right, 5, "NO LINK", Red, 1);
            // Note 253: a joiner whose link went tries to get back, and says how it's going; out of tries, F5 tries again.
            // Note 254: turned away on the way back (the place ran out, and the crew's full).
            string? how = link.Attempt > 0 ? $"RECONNECTING: TRY {link.Attempt} OF {link.Attempts}"
                : link.Refused is { } refused ? $"{refused}: [F5] TRY AGAIN" : link.CanReconnect ? "[F5] RECONNECT" : null;
            if (how is not null)
                UiStyle.Keyed(o, Overlay.Snap(right - UiStyle.MeasureKeyed(o, how, k), k), 5 + line + 2 * k, how, link.Attempt > 0 ? Amber : Red, k);
            return;
        }
        if (link.PingMs is not { } ping)
            return;
        var colour = ping < 80 ? Green : ping < Tuning.PingWarnMs ? Amber : Red;
        if (s.World.Run is null or { Phase: Sim.Run.RunPhase.Yard })
        {
            // Just the milliseconds at the big size: "PING 100 MS" ran into the route strip at 1280 wide (the 4 Oct rehearsal).
            o.TextRight(right, 5, $"{ping:0} MS", colour, scale: 2);
            o.TextRight(right, 5 + 2 * line + 1, "PING TO HOST", Dim, k);
        }
        else if (ping >= Tuning.PingWarnMs)
            o.TextRight(right, 5, $"PING {ping:0} MS", colour, k);
    }

    /// <summary>An open house's hiding spot as the prompt says it (note 326).</summary>
    static string SpotName(DarkTerritory.Sim.Stops.ContainerKind kind) => kind switch
    {
        DarkTerritory.Sim.Stops.ContainerKind.Cupboard => "CUPBOARD",
        DarkTerritory.Sim.Stops.ContainerKind.Cabinet => "CABINET",
        DarkTerritory.Sim.Stops.ContainerKind.Cellar => "CELLAR",
        DarkTerritory.Sim.Stops.ContainerKind.UnderFloor => "LOOSE BOARDS",
        _ => "PLACE",
    };

    /// <summary>Which way a switch goes when it's thrown: back to the main line, or over for its branch.</summary>
    /// <remarks>
    /// An alternate goes by the route card's name for it ("high line", "low line"; linegen plan §9.7). Note 289: every branch
    /// that wasn't a spur said "the dead line", so a crew at a junction choosing the card's high line was told it was throwing
    /// the train onto a dead line.
    /// </remarks>
    static string SwitchTo(Sim.World world, TrainOnLine train, int branch)
    {
        if (train.Diverging(branch))
            return "THE MAIN LINE";
        return train.Line.Branches[branch].Kind switch
        {
            BranchKind.Spur => "THE SPUR",
            BranchKind.Alternate => $"THE {(CardName(world, branch) ?? "ALTERNATE").ToUpperInvariant()}",
            _ => "THE DEAD LINE",
        };
    }

    /// <summary>What the route card calls a generated line's alternate (its known grades: "high line (alt1)"), if it says.</summary>
    static string? CardName(Sim.World world, int branch)
    {
        if (world.Run?.Route.Plan is not { } plan || plan.Alignment.FirstOrDefault(a => a.Branch == branch) is not { } edge)
            return null;
        string tag = $" ({edge.Edge})";
        var grade = plan.RouteCard.KnownGrades.FirstOrDefault(k => k.Route.EndsWith(tag, StringComparison.Ordinal));
        return grade?.Route[..^tag.Length];
    }

    /// <summary>
    /// Where the repair kit is, for a ruptured boiler (T109; GDD §23.2: where it is decides the night): where, not what it's
    /// for (note 285, the director: "consequences need to be learned").
    /// </summary>
    public static string RepairKitWhere(Sim.World world, int playerId)
    {
        // With spares (E.12 question 4), the one that's handiest: in your hands, a crewmate's, then the nearest car's.
        var consist = world.Train.Dynamics.Consist;
        var kit = world.Bodies.All.Where(b => b.Kind == BodyKind.RepairKit)
            .OrderBy(b => b.Carrier == playerId ? 0 : b.Carrier >= 0 ? 1 : consist.IndexOf(b.Parent) >= 0 ? 2 + consist.IndexOf(b.Parent) : 1000).ThenBy(b => b.Id)
            .FirstOrDefault();
        // This machine only has the bodies within its interest radius (note 263: left behind by a runaway train, the director
        // saw none, and was told the train had none). Out of sight, the host's word on it (Run.Kit, replicated).
        if (kit is null)
            return world.Run?.Kit is { Place: not KitPlace.None } far ? RepairKitFar(far, consist) : "NO REPAIR KIT ABOARD";
        if (kit.Carrier == playerId)
            return "REPAIR KIT: IN YOUR HANDS";
        if (kit.Carrier >= 0)
            return "REPAIR KIT: WITH A CREWMATE";
        int car = consist.IndexOf(kit.Parent);
        // In its locker (note 173): the crew learn which.
        if (car > 0 && kit.Stowed && kit.Locker < world.Train.Frames[kit.Parent].Shape.Lockers.Count)
            return $"REPAIR KIT: THE {world.Train.Frames[kit.Parent].Shape.Lockers[kit.Locker].Name}'S LOCKER, CAR {car}";
        return car > 0 ? $"REPAIR KIT: CAR {car}" : car == 0 ? "REPAIR KIT: ON THE ENGINE" : "REPAIR KIT: OFF THE TRAIN";
    }

    /// <summary>Where the host says the kit is, when it's beyond what this machine is sent (note 263).</summary>
    static string RepairKitFar(KitWhere kit, Consist consist)
    {
        if (kit.Place == KitPlace.Carried)
            return "REPAIR KIT: WITH A CREWMATE";
        if (kit.Place == KitPlace.Lost)
            return "THE REPAIR KIT IS GONE";
        int car = kit.Vehicle >= 0 ? consist.IndexOf(kit.Vehicle) : -1;
        return car > 0 ? $"REPAIR KIT: CAR {car}" : car == 0 ? "REPAIR KIT: ON THE ENGINE" : "REPAIR KIT: OFF THE TRAIN";
    }

    /// <summary>A hand-sized thing by name, for the lockers' prompts.</summary>
    static string Called(Sim.World world, Body b) => b.Kind switch
    {
        BodyKind.RepairKit => "THE REPAIR KIT",
        BodyKind.Lamp => "THE LAMP",
        BodyKind.Radio => "THE RADIO",
        BodyKind.Toy => b.Noise switch
        {
            ToyNoise.Squeaker => "THE SQUEAKER",
            ToyNoise.MusicBox => "THE MUSIC BOX",
            ToyNoise.Drummer => "THE DRUMMER",
            _ => "THE TOY",
        },
        BodyKind.Extinguisher => "THE EXTINGUISHER",
        BodyKind.Loot => world.Run?.FindName(b)?.ToUpperInvariant() ?? "THE FIND",
        // What else is carried, for the hotbar's IN HANDS (note 264).
        BodyKind.Crate or BodyKind.Cargo => "A CRATE",
        BodyKind.Heavy => "AN END OF THE HEAVY CRATE",
        BodyKind.Ragdoll => "A BODY",
        BodyKind.Child => "THE CHILD",
        _ => "IT",
    };

    /// <summary>
    /// At a crew locker's door (note 173): held, Use opens or shuts it; tapped, it takes the top thing off its shelves or
    /// puts what's in your hands on one. Null away from one, or with something in your hands that doesn't go in.
    /// </summary>
    static string? LockerPrompt(Sim.World world, in PlayerState p, int playerId)
    {
        var train = world.Train;
        if (Lockers.AtHand(p, train, world.Hand) is not { } at)
            return null;
        var carried = world.Bodies.CarriedBy(playerId);
        if (carried is not null && !Lockers.Holds(train, carried.Kind))
            return null;
        string name = $"THE {at.Bay.Name}'S LOCKER";
        bool full = Lockers.FreeSlot(world.Bodies, train, at.Car, at.Bay.Index) < 0;
        // Note 267 ("there needs to be some telegraphing that there's a repair kit inside"): the tag on its door says
        // what's in it, shut or open; a tap opens a shut one, and puts what's in your hands in.
        if (!train.Vehicles[at.Car].LockerOpen(at.Bay.Index))
            return carried is not null && !full ? $"{name}   PUT {Called(world, carried)} IN : [E]" : $"{name}: {Holding(world, at.Car, at.Bay.Index)}   OPEN : [E]";
        if (carried is not null)
            return !full ? $"PUT {Called(world, carried)} IN : [E]   SHUT : HOLD [E]" : $"{name} IS FULL   SHUT : HOLD [E]";
        return Lockers.Contents(world.Bodies, at.Car, at.Bay.Index).LastOrDefault() is { } top
            ? $"TAKE {Called(world, top)} : [E]   SHUT : HOLD [E]" : $"{name}: EMPTY   SHUT : HOLD [E]";
    }

    /// <summary>What's on a locker's shelves, as its door's tag has it (note 264): "THE REPAIR KIT", "2 LAMPS", "EMPTY".</summary>
    public static string Holding(Sim.World world, int car, int locker)
    {
        var things = Lockers.Contents(world.Bodies, car, locker).Select(b => Called(world, b))
            .GroupBy(n => n).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()} {(g.Key.StartsWith("THE ", StringComparison.Ordinal) ? g.Key[4..] : g.Key)}S").ToList();
        return things.Count == 0 ? "EMPTY" : string.Join(" AND ", things);
    }

    /// <summary>
    /// At a Holdout with someone in it (GDD App. D.7): break them out, or, with the repair kit in hand at a lock, open it
    /// quietly (at a barricade the kit's no help: it's pried, and the kit stays in hand). Null away from one.
    /// </summary>
    static string? HoldoutPrompt(Sim.World world, in PlayerState p, TrainOnLine train, bool kit)
    {
        if (world.Holdouts is not { } ho || p.Parent != PlayerState.World)
            return null;
        var at = PlayerMotor.WorldPosition(p, train);
        foreach (var h in ho.All)
        {
            if (!h.Lit || ((h.Door - at) with { Y = 0 }).Length > ho.Tuning.BreachReach)
                continue;
            if (h.State == HoldoutState.Breaching)
                return $"{(h.Quiet ? "OPENING THE LOCK" : h.Layout.Kind == HoldoutKind.Shelter ? "PRYING IT OPEN" : "SMASHING IT OPEN")} ({h.Progress / h.Breach(ho.Tuning).Seconds * 100:0}%)";
            return kit && h.Lockable ? "OPEN THE LOCK : HOLD [E]"
                : $"{(h.Layout.Kind == HoldoutKind.Shelter ? "PRY THE BARRICADE" : "SMASH THE LOCK")} : HOLD [E]";
        }
        return null;
    }

    /// <summary>
    /// Top centre (note 285), each only while it matters: the dawn clock in the night's last stretch (hud.json
    /// <c>dawnClockSeconds</c>; Lethal Company's clock), a place coming up (its name and how far, for a few seconds once
    /// it's near), and the cold getting deeper where you are (GDD §22, note 201: how much faster it comes on outside, for a
    /// few seconds as you go into it). The whole night, and where you are in it, is the route card's (C).
    /// </summary>
    static void TopCentre(Overlay o, int width, IPlaySession s, Memory m, int line)
    {
        float k = Fine, cx = width / 2f, y = 6;
        var run = s.World.Run;
        if (s.Route is { } route && run is not { Phase: Sim.Run.RunPhase.Yard })
        {
            double dawn = run?.DawnIn ?? route.DawnSeconds;
            if (dawn <= 0)
                o.TextCentred(cx, y, "DAWN: THE LINE IS LIVE. GET IN", Red, 1);
            else if (dawn <= Tuning.DawnClockSeconds)
                o.TextCentred(cx, y, $"DAWN {(int)dawn / 60}:{(int)dawn % 60:00}", dawn <= 120 ? Red : Amber, 1);
            if (dawn <= Tuning.DawnClockSeconds)
                y += line + 4;
        }
        if (m.Place is { } place && Shown(Since(s, m.PlaceTick), Tuning.PlaceSeconds) is > 0 and var a)
        {
            double left = place.At - s.Train.Dynamics.Distance;
            o.TextCentred(cx, y, place.Name.ToUpperInvariant(), Ink with { W = a }, 1);
            y += line;
            o.TextCentred(cx, y, left > 50 ? $"IN {left / 1000:0.0} KM" : "HERE", Dim with { W = a }, k);
            y += (line + 2) * k;
        }
        if (s.Player.Alive && Shown(Since(s, m.ColdTick), Tuning.ColdSeconds) is > 0 and var c
            && ColdLine(s.Player, s.Train, s.PlayerTuning) is { } cold)
            o.TextCentred(cx, y + 2, cold, Amber with { W = c }, k);
    }

    /// <summary>
    /// The cold step where a player is, in words (note 201), or null on a normal night. Every machine builds the night's
    /// conditions from its seed, so a client knows it as the host does.
    /// </summary>
    public static string? ColdLine(in PlayerState p, TrainOnLine train, PlayerTuning tuning)
    {
        int step = PlayerMotor.ColdStep(p, train);
        if (step <= 0)
            return null;
        string name = step switch { 1 => "DEEP COLD", 2 => "BITTER COLD", _ => "KILLING COLD" };
        return $"{name}: OUTSIDE, IT COMES ON {1 + tuning.Cold.PerColdStep * step:0.##}X FASTER";
    }

    static void Alerts(Overlay o, int width, int height, IPlaySession s, int line)
    {
        var p = s.Player;
        var world = s.World;
        float y = height * 0.28f;
        // Note 285: an alarm's headline is big only when it's urgent (a rupture counting, a grab, the rail coming off); what
        // to do about it is in fine print under it, keys as keycaps. The run's end keeps the HUD's own size.
        void Big(string text, Vector4 colour, bool urgent = true)
        {
            int scale = urgent ? 2 : 1;
            o.TextCentred(width / 2f, y, text, colour, scale: scale);
            y += scale * line + 2;
        }
        void Small(string text, Vector4 colour, bool fine = true)
        {
            if (!fine)
            {
                o.TextCentred(width / 2f, y, text, colour);
                y += line;
                return;
            }
            float k = Fine;
            UiStyle.Keyed(o, Overlay.Snap((width - UiStyle.MeasureKeyed(o, text, k)) / 2, k), y + 2 * k, text, colour, k);
            y += (line + 4) * k;
        }
        if (s.Link is { Waiting: { } waiting })
        {
            Big("WAITING", Amber, urgent: false);
            Small(waiting, Ink);
        }
        // Note 253: the link's gone. Back in time, the crewmate's still theirs: limp where they stood till then.
        if (s.Link is { Lost: true } lost && world.Run is not { Over: true })
        {
            if (lost.Attempt > 0)
            {
                Big("RECONNECTING", Amber, urgent: false);
                Small($"TRY {lost.Attempt} OF {lost.Attempts}", Ink);
            }
            // Note 254: back too late, the place had gone, and the crew had filled it.
            else if (lost.Refused is { } refused)
            {
                Big(refused, Red);
                Small("TRY AGAIN : [F5]", Ink);
            }
            else if (lost.CanReconnect)
            {
                Big("CONNECTION LOST", Red);
                Small("RECONNECT : [F5]", Ink);
            }
        }
        // The derailment's cinematic plays out first (T117): no run's end or death screen over it. Over the replay (T121),
        // what did it: "TOOK THE 45 KM/H BEND AT 68 KM/H, 23 KM/H TOO FAST".
        if (s.WreckCinematic)
        {
            if (DerailSequence.Beat(s.SequenceTuning, s.WreckSeconds) == DerailBeat.Replay)
            {
                Big("REPLAY", Ink, urgent: false);
                // The host has the cause; a client has it from the incident report (sent as the run ends, on the derail tick).
                string? why = s.DerailCause;
                if (why is { Length: > 0 })
                    Small(why.ToUpperInvariant(), Amber);
            }
            return;
        }
        // GDD v1.4 App. E.9: the clerk on the radio over the pull-back; the end screen after it.
        if (s.StrandedOutro)
        {
            if (s.OutroSeconds > world.WreckTuning.Stranded.RackSeconds)
                Small(Sim.Run.Radio.Stranded(world.Run?.Report?.DistanceKm ?? 0).ToUpperInvariant(), Dim);
            return;
        }
        // GDD §9: the clerk tallies first; the end screen after.
        if (s.ClerkTally)
            return;
        if (world.Run?.Report is { } r)
        {
            if (r.End == RunEnd.Delivered)
            {
                Big("DELIVERED", Green);
                Small($"{r.CarsDelivered} CARS, {r.CarsLost} LOST. {r.Net:0} SCRIP. CREW HOME {r.CrewHome}", Ink, fine: false);
            }
            else if (r.End == RunEnd.Stranded)
            {
                // GDD v1.4 §23.2: nobody died of it, and nobody much cares.
                Big("STRANDED", Amber);
                Small(EngineeringKit.Line(r.KitLoss), Ink, fine: false);
                Small($"RECOVERY AT FIRST LIGHT. RECOVERY IS CHARGEABLE: {r.Recovery:0} SCRIP", Dim, fine: false);
            }
            else
            {
                Big("RUN LOST", Red);
                Small(r.End switch { RunEnd.Derailed => "DERAILED", RunEnd.CrewLost => "THE WHOLE CREW IS DEAD", _ => "STILL OUT WHEN THE LINE WENT LIVE" }, Ink, fine: false);
            }
            // GDD v1.4 App. D.12: the incident report, every line in the clerk's voice, under the result; the night's
            // commendations under it.
            // The night's own (D.12), as the host has them, unless a still frame passed some in.
            var given = s.World.Commendations;
            if (_commendations is null && given.Count > 0)
                _commendations = [.. given.Select(c => (IncidentLog.NameOf(s.World, c.To), (UiStyle.Commendation)c.Which, IncidentLog.NameOf(s.World, c.From)))];
            if (s.CommendPick is { } pick)
            {
                string text = pick.Given ? $"YOU COMMENDED {pick.To}: {pick.What}"
                    : Headset ? $"COMMEND [STICK LEFT/RIGHT] {pick.To}   [STICK UP/DOWN] {pick.What}   [CLICK STICK] GIVE"
                    : $"COMMEND [LEFT/RIGHT] {pick.To}   [UP/DOWN] {pick.What}   [SPACE] GIVE";
                UiStyle.Keyed(o, MathF.Round((width - UiStyle.MeasureKeyed(o, text)) / 2), height - 12, text, pick.Given ? Green : Amber);
            }
            bool awards = _commendations is { Count: > 0 };
            IncidentReport(o, width, awards ? height - (int)Commendations(o, width, height, _commendations!, draw: false) : height, y + line, r, line, _stills);
            if (awards)
                Commendations(o, width, height, _commendations!);
            return;
        }
        if (!p.Alive)
        {
            DeadCard(o, width, height, s, line);
            BallotPlate(o, width, s, line);
        }
        if (p.Alive && PlayerMotor.Chilled(p, s.PlayerTuning))
            Small($"COLD: {Math.Max(0, s.PlayerTuning.Cold.DeathSeconds - p.Cold):0}S", p.Cold > s.PlayerTuning.Cold.DeathSeconds - 30 ? Red : Amber);
        if (world.Derailed)
            Big("DERAILED", Red);
        // T109 playtest ("feedback for the player to understand in multiple ways that pressure is too high"): the boiler in
        // the red, pinned at the top (the spec's 20 s to a rupture, counting), and ruptured.
        if (p.Alive && world.Train.BoilerTuning is { } bt)
        {
            var b = world.Train.Boiler;
            bool flash = world.Tick / 10 % 2 == 0;
            if (b.Ruptured)
            {
                Big("BOILER RUPTURED", Red);
                // GDD v1.4 §23.2: where the repair kit is decides the night; lost to the Territory, it's over once she stops.
                if (world.Run?.Kit.Lost == true)
                    Small("THE REPAIR KIT IS GONE", Red);
                else
                    Small(RepairKitWhere(world, s.PlayerId), Ink);
            }
            else if (b.AtMaxSeconds > 0)
            {
                Big($"VENT! RUPTURE IN {Math.Max(0, bt.RuptureHoldSeconds - b.AtMaxSeconds):0}S", flash ? Red : Amber);
                Small(Bound("VENT : HOLD [VENT]"), Ink);
            }
            else if (b.Pressure >= bt.Redline)
                Small(Bound("PRESSURE IN THE RED   VENT : HOLD [VENT]"), flash ? Red : Amber);
        }
        // Note 267: a crewmate's whistle names whose hand is on the cord (GDD §12). The Whistler's has no hand on it, and no
        // line (App. A.4: "the whistle sounds with no hand on the cord" is its tell), so one with no name is the Whistler.
        if (p.Alive && world.WhistleSeconds > 0 && world.WhistleBy >= 0)
            Small(world.WhistleBy == s.PlayerId ? "THE WHISTLE: YOUR HAND'S ON THE CORD" : $"THE WHISTLE: {IncidentLog.NameOf(world, world.WhistleBy).ToUpperInvariant()} ON THE CORD", Dim);
        // T115 playtest ("suddenly I can't move and then a few seconds later I die"): held, say so, and what to do. Alone
        // (the solo rule) Use held struggles free; with a crew, a friend has to pull it off or hit it.
        if (p.Alive && p.Has(PlayerFlags.Held))
        {
            Big("SOMETHING HAS YOU", world.Tick / 10 % 2 == 0 ? Red : Amber);
            // Alone, Use held struggles free (the solo rule). With a crew, who can help is learned (note 285).
            if (s.Roster().Count(l => l.Alive) <= 1)
                Small(Bound("STRUGGLE : HOLD [E]"), Ink);
        }
        // Note 260 (T115 playtest, "random death walking outside"; GDD App. A.1): the line's own kills are telegraphed. Up
        // top with a tunnel's mouth or a bend taken too fast coming, say so, how long, and what to do.
        if (RoofWarningLines(s) is { } roof)
        {
            Big(roof.Head, world.Tick / 8 % 2 == 0 ? Red : Amber);
            Small(roof.Line, Ink);
        }
        // Note 266 (build 1121: "people should know they're going too fast for a spot"): a bend the speed now would derail
        // the train on, coming or under it, to whoever's in the cab; and its stress rising, short of that.
        else if (BendWarningLines(s) is { } bend)
        {
            Big(bend.Head, bend.Urgent ? (world.Tick / 6 % 2 == 0 ? Red : Amber) : Amber, bend.Urgent);
            Small(bend.Line, Ink);
        }
        // Note 286 (the director, 7 Oct 2026: "lines that lead nowhere"): down a dead line, to whoever's in the cab; and its
        // buffers coming up too fast to stop under the speed that goes through them, urgent, as a bend's warning is.
        else if (DeadEndLines(s) is { } dead)
        {
            Big(dead.Head, dead.Urgent ? (world.Tick / 6 % 2 == 0 ? Red : Amber) : Amber);
            Small(dead.Line, Ink);
        }
        // T113: the Choir's long telegraph, said plainly once it's well along, and what to do about it.
        if (p.Alive && world.Combat is not null && !world.Choir.Present && world.Choir.Build > 0.25)
            Small("THE CHOIR IS GATHERING", world.Tick / 15 % 2 == 0 ? Red : Amber);
    }

    /// <summary>
    /// The incident report (GDD v1.4 App. D.12): deaths with who, where and the cause line (C.9) beside the fee and refund,
    /// rescues, the boiler, cars lost, and how it ended; then the night's money. The clerk's flat voice, top to bottom; what
    /// won't fit says how many more.
    /// </summary>
    /// <summary>
    /// The dead's card (GDD App. D.6-D.10), in the lower middle with no plate (note 285), clear of what they're watching: DEAD
    /// and how, who they're watching and the keys to change it, and the way back (where they'll wait, or the Holdout
    /// they're in and what's happening at its door), each key a keycap.
    /// </summary>
    static void DeadCard(Overlay o, int width, int height, IPlaySession s, int line)
    {
        var p = s.Player;
        var world = s.World;
        var rows = new List<(string Text, Vector4 Colour)> { (DeathLine(p.Death), Ink) };
        // App. D.10: the dead watch the living, through their eyes. Networked only: alone, there's nobody.
        if (s.Watching >= 0)
            rows.Add(($"WATCHING CREW {s.Watching}   NEXT : [{Controls.KeyLabel(Keys.KeyFor(Control.Fire))}] OR [{Controls.KeyLabel(Keys.KeyFor(Control.Right))}]   " +
                $"BACK : [{Controls.KeyLabel(Keys.KeyFor(Control.Left))}]", Ink));
        else if (s.Link is not null && world.Run is not { Over: true })
            rows.Add(("NOBODY LEFT ALIVE TO WATCH", Dim));
        // D.10's Bookmark (D.12): a still of what you're watching, for the run-end screen; how many are left, and the last.
        if (s.Watching >= 0 && world.Run is { Over: false } run)
        {
            var t = world.Bookmarks.Tuning;
            var mine = world.Bookmarks.All.Where(b => b.Kind == BookmarkKind.Manual && b.Taker == s.PlayerId).ToList();
            int left = Math.Min(t.ManualPerPlayer - mine.Count, t.ManualPerRun - world.Bookmarks.All.Count(b => b.Kind == BookmarkKind.Manual));
            if (mine.Count > 0 && run.Seconds - mine[^1].Seconds < 3)
                rows.Add(($"BOOKMARKED AT {Clock(mine[^1].Seconds)}", Green));
            else if (left > 0)
                rows.Add(($"BOOKMARK : [{Controls.KeyLabel(Keys.KeyFor(Control.Bookmark))}] ({left} LEFT)", Dim));
        }
        // GDD App. D: the way back is a Holdout at the next halt or yard, if the crew stops for you.
        if (world.Holdouts is { } holdouts)
        {
            if (holdouts.All.FirstOrDefault(h => h.Occupant == s.PlayerId && h.Lit) is { } mine)
            {
                if (mine.State == HoldoutState.Breaching)
                    rows.Add(($"THEY'RE {(mine.Quiet ? "OPENING THE LOCK" : "BREAKING YOU OUT")}: {mine.Progress / mine.Breach(holdouts.Tuning).Seconds * 100:0}%", Green));
                else
                {
                    rows.Add(($"YOU'RE IN THE {HoldoutName(mine)}", Ink));
                    rows.Add(("CALL OUT : [E]   LET SOMEONE ELSE GO FIRST : [RMB]", Dim));
                }
                // D.7 Live Mic: theirs alone, off by default; on, the rescuer at the door hears what they say to the dead.
                rows.Add((mine.LiveMic ? "LIVE MIC ON : [SPACE]" : "LIVE MIC OFF : [SPACE]", mine.LiveMic ? Green : Dim));
            }
            else
            {
                // (Where the dead wait, and whether the crew stops for them, is learned: note 285.)
                rows.Add(("LET SOMEONE ELSE GO FIRST : [RMB]", Dim));
            }
            // D.11: the creature vote has a plate of its own (BallotPlate, note 202); once cast, the card keeps a line of it.
            if (s.Ballot is { Cast: { } cast })
                rows.Add(($"YOU CALLED THE {Creature(cast)}", Dim));
            if (s.VoteCue is { } cue)
                rows.Add((cue, Red));
            // D.6: the dead and lobbied see the whole queue, and where they are in it (the living see nothing).
            if (QueueLine(world, holdouts, s.PlayerId) is { } queue)
                rows.Add((queue, Ink));
        }
        // Note 285: no plate, low in the frame, clear of what they're watching: DEAD, then how and the rest in fine print.
        float k = Fine, rowH = (line + 4) * k;
        float h = 2 * line + 4 + rows.Count * rowH;
        float y = MathF.Round(Math.Min(height * 0.6f, height - h - 12));
        o.TextCentred(width / 2f, y, "DEAD", Red, scale: 2);
        y += 2 * line + 4;
        foreach (var (text, colour) in rows)
        {
            UiStyle.Keyed(o, Overlay.Snap((width - UiStyle.MeasureKeyed(o, text, k)) / 2, k), y + 2 * k, text, colour, k);
            y += rowH;
        }
    }

    /// <summary>A creature as the dead's ballot and cue name it: "CAR HUGGER".</summary>
    static string Creature(Sim.Enemies.EnemyKind kind) => IncidentLog.Spoken(kind.ToString()).ToUpperInvariant();

    /// <summary>
    /// The dead's creature vote as a screen (GDD v1.4 App. D.11; note 202), top to bottom: each creature on the ballot with
    /// its key, and the want it serves as its <c>Note</c> (the vote only moves weight within a want); then what to do next:
    /// pick, cast (locked once cast), casting, cast. A row's <c>Picked</c> is the one lit. Keys as the player has them; a
    /// headset's stick and its click in one (<see cref="Headset"/>). Null with no ballot to show (alive, none offered, the
    /// run over).
    /// </summary>
    public static List<(string Text, Vector4 Colour, bool Picked, string? Note)>? BallotRows(IPlaySession s)
    {
        if (s.Player.Alive || s.Ballot is not { Options.Count: > 0 } ballot || s.World.Run is { Over: true })
            return null;
        var picker = s.Picker;
        int pick = ballot.Cast is { } cast ? ballot.Options.ToList().IndexOf(cast) : picker?.Pick ?? -1;
        var rows = new List<(string, Vector4, bool, string?)>();
        for (int i = 0; i < ballot.Options.Count; i++)
        {
            var k = ballot.Options[i];
            bool lit = i == pick;
            var colour = ballot.Cast is null ? lit ? Amber : Ink : lit ? Green : Dim;
            rows.Add(($"[{i + 1}] {Creature(k)}", colour, lit, Sim.Enemies.Director.WantOf(k).ToString().ToUpperInvariant()));
        }
        int n = ballot.Options.Count;
        if (ballot.Cast is not null)
            rows.Add(("CAST, AND LOCKED", Green, false, null));
        else if (picker is { Sent: true })
            rows.Add(("CASTING ...", Amber, false, null));
        else if (pick < 0)
            rows.Add((Headset ? "[STICK UP/DOWN] PICK ONE" : $"[1]-[{n}] PICK ONE", Ink, false, null));
        else
        {
            rows.Add((Headset ? "[CLICK STICK] CAST IT" : $"[{pick + 1}] AGAIN OR [ENTER] CAST IT", Amber, false, null));
            rows.Add(("IT'S LOCKED ONCE CAST", Dim, false, null));
        }
        return rows;
    }

    /// <summary>The ballot plate's title (D.11: once per run per player).</summary>
    public const string BallotTitle = "THE DEAD'S VOTE: ONCE A RUN";

    /// <summary>
    /// The ballot's plate (D.11; note 202): high on the right, clear of the dead card below, of what they're watching in the
    /// middle and of the yard's lobby list on the left; <see cref="BallotTitle"/> over <see cref="BallotRows"/>, the picked
    /// creature on a lit bar with its want at the right, and the trim lit while there's a vote to cast.
    /// </summary>
    static void BallotPlate(Overlay o, int width, IPlaySession s, int line)
    {
        if (BallotRows(s) is not { } rows)
            return;
        // Note 285: fine print on a dark backing, lit along its top while there's a vote to cast; no rivets.
        float k = Fine, rowH = (line + 5) * k, pad = 6 * k;
        float notes = rows.Max(r => r.Note is null ? 0 : o.Measure(r.Note, k) + 12 * k);
        float w = MathF.Round(Math.Max(o.Measure(BallotTitle, k), rows.Max(r => UiStyle.MeasureKeyed(o, r.Text, k) + (r.Note is null ? 0 : notes))) + 2 * pad);
        float h = MathF.Round((line + 4) * k + rows.Count * rowH + 2 * pad);
        float x = MathF.Round(width - w - 6), y = 34;
        o.Rect(x, y, w, h, UiStyle.Iron with { W = 0.6f });
        o.Rect(x, y, w, 1, (s.Ballot is { Cast: null } ? Amber : Dim) with { W = 0.8f });
        o.Text(x + pad, y + pad, BallotTitle, Amber, k);
        y += pad + (line + 4) * k;
        foreach (var (text, colour, picked, note) in rows)
        {
            if (picked)
                o.Rect(x + 2, y, w - 4, rowH, colour with { W = 0.18f });
            UiStyle.Keyed(o, x + pad, y + 2.5f * k, text, colour, k);
            if (note is not null)
                o.Text(Overlay.Snap(x + w - pad - o.Measure(note, k), k), y + 2.5f * k, note, picked ? colour : Dim, k);
            y += rowH;
        }
    }

    /// <summary>Whether this is a headset's panel (T36): the ballot and the commendations say the stick, not the keys (note 202).</summary>
    public static bool Headset { get; set; }

    /// <summary>
    /// The respawn queue as the dead see it (GDD v1.4 App. D.6; note 179): "QUEUE: 1 PRIYA  2 YOU  3 SAM (JOINING)", null when
    /// it's empty.
    /// </summary>
    public static string? QueueLine(Sim.World world, Holdouts holdouts, int me)
    {
        if (holdouts.Queue.Count == 0)
            return null;
        var names = holdouts.Queue.Select((e, i) =>
            $"{i + 1} {(e.PlayerId == me ? "YOU" : IncidentLog.NameOf(world, e.PlayerId).ToUpperInvariant())}{(e.Lobbied ? " (JOINING)" : "")}");
        return $"QUEUE: {string.Join("   ", names)}";
    }

    [ThreadStatic] static IReadOnlyList<(string To, UiStyle.Commendation What, string From)>? _commendations;
    [ThreadStatic] static IReadOnlyDictionary<int, Still>? _stills;

    /// <summary>
    /// The night's commendations (App. D.12) on a plate above the foot of the run-end screen, two to a row: each a badge,
    /// whose, and from whom. Returns the height it takes (for the report above to leave room).
    /// </summary>
    static float Commendations(Overlay o, int width, int height, IReadOnlyList<(string To, UiStyle.Commendation What, string From)> list, bool draw = true)
    {
        var cells = list.Select(c => (c, Text: $"{c.To.ToUpperInvariant()}: {UiStyle.Name(c.What)}", From: $"FROM {c.From.ToUpperInvariant()}")).ToList();
        const int perRow = 2, cellH = 20;
        float each = cells.Max(c => Math.Max(o.Font.Measure(c.Text), o.Font.Measure(c.From))) + 26;
        int rows = (cells.Count + perRow - 1) / perRow;
        float w = Math.Min(cells.Count, perRow) * each + 8, h = rows * cellH + 8;
        float x0 = MathF.Round((width - w) / 2), y0 = height - 24 - h;
        if (!draw)
            return h + 36;
        UiStyle.Plate(o, x0, y0, w, h);
        o.Text(x0 + 4, y0 - 9, "COMMENDATIONS", Amber);
        for (int i = 0; i < cells.Count; i++)
        {
            var (c, text, from) = cells[i];
            float x = x0 + 4 + i % perRow * each, y = y0 + 4 + i / perRow * cellH;
            UiStyle.Badge(o, x, y, c.What);
            o.Text(x + 19, y + 1, text, Ink);
            o.Text(x + 19, y + 10, from, Dim);
        }
        return h + 36;
    }

    /// <summary>A bookmark's thumbnail beside its line on the report (GDD v1.4 App. D.12), in UI pixels: 16:9, two lines high.</summary>
    public const int ThumbWidth = 32, ThumbHeight = 18;
    /// <summary>The dead's own bookmarks are the larger, in their row: there's no line beside them to read.</summary>
    public const int ManualWidth = 48, ManualHeight = 27;

    /// <summary>
    /// The incident report (GDD v1.4 App. D.12): deaths with who, where and the cause line (C.9) beside the fee and refund,
    /// rescues, the boiler, cars lost, and how it ended; then the night's money. The clerk's flat voice, top to bottom; what
    /// won't fit says how many more. Each automatic bookmark's still sits at the right of the line it belongs to, and the
    /// dead's own bookmarks have a row of their own under the lines, each with its time and whom it was following.
    /// </summary>
    public static void IncidentReport(Overlay o, int width, int height, float top, RunReport r, int line, IReadOnlyDictionary<int, Still>? stills = null)
    {
        float w = Math.Min(width - 40, 980), x = MathF.Round((width - w) / 2);
        float glyph = Math.Max(1, o.Font.Measure("M") + 1);
        int chars = Math.Max(20, (int)((w - 16) / glyph));
        var marks = r.Bookmarks.ToDictionary(b => b.Id);
        // Each line a block: its wrapped rows, and the thumbnails at its right (narrowing the text beside them).
        var blocks = new List<(List<(string Text, Vector4 Colour)> Rows, List<Bookmark> Marks, float Height)>();
        foreach (var l in r.Lines)
        {
            var colour = l.Kind switch { IncidentKind.Death => Ink, IncidentKind.Rescue => Green, IncidentKind.Derailed or IncidentKind.Stranded => Amber, _ => Dim };
            string text = l.Who.Length > 0 ? $"{l.Who.ToUpperInvariant()}: {l.Text}" : l.Text;
            var shown = l.Marks.Where(marks.ContainsKey).Select(id => marks[id]).ToList();
            int perRow = Math.Max(1, (int)((w * 0.45f) / (ThumbWidth + 3)));
            int thumbRows = (shown.Count + perRow - 1) / perRow;
            float thumbsW = shown.Count == 0 ? 0 : Math.Min(shown.Count, perRow) * (ThumbWidth + 3) + 2;
            int fit = Math.Max(20, (int)((w - 16 - thumbsW) / glyph));
            var rows = new List<(string, Vector4)>();
            bool first = true;
            foreach (var part in Wrap(text, fit))
            {
                rows.Add((first ? part : "    " + part, colour));
                first = false;
            }
            blocks.Add((rows, shown, Math.Max(rows.Count * line, thumbRows * (ThumbHeight + 2))));
        }
        if (r.Lines.Count == 0)
            blocks.Add(([("Nothing to report.", Dim)], [], line));
        var money = new List<string> { $"Gross {r.Gross:0}" };
        if (r.CrewLossFees > 0)
            money.Add($"crew-loss fees {r.CrewLossFees:0}");
        if (r.BodyRefunds > 0)
            money.Add($"refunds {r.BodyRefunds:0}");
        if (r.Recovery > 0)
            money.Add($"recovery {r.Recovery:0}");
        money.Add($"running costs {r.CoalCost + r.AmmoCost + r.RepairCost:0}");
        string sum = $"{string.Join(", ", money)}. Net {r.Net:0} scrip.";
        // The dead's own (D.12 "manual"), a row of stills across, each with when and whom they were following.
        var manual = r.Bookmarks.Where(b => b.Kind == BookmarkKind.Manual).ToList();
        // Each a still with its time and whom it followed at its right.
        float cell = ManualWidth + 6 + glyph * Math.Clamp(manual.Select(b => b.Name.Length).DefaultIfEmpty(5).Max(), 5, 12) + 6;
        int across = Math.Max(1, (int)((w - 8) / cell));
        float manualH = manual.Count == 0 ? 0 : line + ((manual.Count + across - 1) / across) * (ManualHeight + 4);
        // What fits: the heading, as many lines as there's room for (the rest counted), the dead's row, the money.
        float room = height - top - 8 - 2 * line - manualH;
        float used = 0;
        int keep = 0;
        while (keep < blocks.Count && used + blocks[keep].Height <= room - (keep + 1 < blocks.Count ? line : 0))
            used += blocks[keep++].Height;
        int more = blocks.Skip(keep).Sum(b => b.Rows.Count);
        float total = line + used + (more > 0 ? line : 0) + manualH + line;
        UiStyle.Plate(o, x - 4, top - 4, w + 8, total + 8);
        float y = top;
        o.Text(x + 4, y, "INCIDENT REPORT", Amber);
        y += line;
        foreach (var (rows, shown, h) in blocks.Take(keep))
        {
            float ty = y;
            foreach (var (text, colour) in rows)
            {
                o.Text(x + 4, ty, text, colour);
                ty += line;
            }
            int perRow = Math.Max(1, (int)((w * 0.45f) / (ThumbWidth + 3)));
            for (int i = 0; i < shown.Count; i++)
            {
                // Right-aligned, in the order they were taken.
                int row = i / perRow, inRow = Math.Min(perRow, shown.Count - row * perRow);
                Thumb(o, x + w - 2 - (inRow - i % perRow) * (ThumbWidth + 3), y + 1 + row * (ThumbHeight + 3), stills?.GetValueOrDefault(shown[i].Id));
            }
            y += h;
        }
        if (more > 0)
        {
            o.Text(x + 4, y, $"... and {more} more lines", Dim);
            y += line;
        }
        if (manual.Count > 0)
        {
            o.Text(x + 4, y, "BOOKMARKS", Amber);
            y += line;
            for (int i = 0; i < manual.Count; i++)
            {
                var b = manual[i];
                float tx = x + 5 + i % across * cell, ty = y + 1 + i / across * (ManualHeight + 4);
                Thumb(o, tx, ty, stills?.GetValueOrDefault(b.Id), ManualWidth, ManualHeight);
                o.Text(tx + ManualWidth + 5, ty + 2, Clock(b.Seconds), Ink);
                string name = b.Name.ToUpperInvariant();
                o.Text(tx + ManualWidth + 5, ty + 2 + line, name.Length > 12 ? name[..12] : name, Dim);
            }
            y += manualH - line;
        }
        o.Text(x + 4, y, sum, Ink);
    }

    /// <summary>Run seconds as the report's timestamp: "1:04:12" over an hour, else "12:04".</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>A bookmark's still in a thin frame; a dark plate where this machine has none (it joined after).</summary>
    static void Thumb(Overlay o, float x, float y, Still? still, int w = ThumbWidth, int h = ThumbHeight)
    {
        o.Rect(x - 1, y - 1, w + 2, h + 2, UiStyle.Lit with { W = 0.6f });
        if (still is null)
        {
            o.Rect(x, y, w, h, new Vector4(0.05f, 0.05f, 0.06f, 1));
            o.Rect(x + w / 2f - 4, y + h / 2f, 8, 1, Dim);
            return;
        }
        // Half-pixel cells on the UI's canvas: it's drawn scaled up, so the still keeps more than the canvas's own pixels.
        o.Image(x, y, w, h, still.Rgba, still.Width, still.Height, w * 2, h * 2, levels: 16);
    }

    /// <summary>
    /// The derailment film's cards (GDD v1.4 App. E.5; note 177): over each player's shot a lower third in the clerk's
    /// typewriter face, "DAVE - ON THE THROTTLE"; over the cause card the clerk's line, typed out as it's read.
    /// </summary>
    static void Film(Overlay o, int width, int height, IPlaySession s)
    {
        var t = s.SequenceTuning;
        if (s.Film is not { } film || DerailSequence.Beat(t, s.WreckSeconds, film) != DerailBeat.Film
            || film.CutAt(DerailSequence.FilmSeconds(t, s.WreckSeconds)) is not { } at)
            return;
        var (shot, into) = at;
        if (shot.Kind == ShotKind.Player && shot.Card.Length > 0)
        {
            int dash = shot.Card.IndexOf(" - ", StringComparison.Ordinal);
            string name = dash < 0 ? shot.Card : shot.Card[..dash], role = dash < 0 ? "" : shot.Card[(dash + 3)..];
            int scale = Math.Max(2, height / 240);
            float x = width * 0.07f, y = height * 0.76f;
            float w = Math.Max(o.Font.Measure(name, scale * 2), o.Font.Measure(role, scale)) + 12 * scale;
            float h = o.Font.Height * scale * 3 + 10 * scale;
            // In over a fifth of a second, a blink of the typewriter's carriage.
            float shown = (float)Math.Clamp(into / 0.2, 0, 1);
            UiStyle.Plate(o, x, y, w * shown, h, UiStyle.Brass);
            if (shown >= 1)
            {
                o.Text(x + 6 * scale, y + 4 * scale, name, UiStyle.Enamel, scale * 2);
                o.Text(x + 6 * scale, y + 6 * scale + o.Font.Height * scale * 2, role, Amber, scale);
            }
        }
        else if (shot.Kind == ShotKind.Cause)
        {
            // The clerk on the radio: the picture dims under the card, and the line types out as it's read.
            o.Rect(0, 0, width, height, new Vector4(0, 0, 0, (float)Math.Clamp(into / 0.3, 0, 0.72)));
            int scale = Math.Max(1, height / 300);
            // A glyph advances its width and a pixel of spacing (note 251: by the width alone the clerk ran off the screen).
            int chars = Math.Max(20, (int)(width * 0.7f / (o.Font.Measure("M", scale) + scale)));
            string typed = shot.Card.ToUpperInvariant();
            typed = typed[..(int)Math.Min(typed.Length, typed.Length * Math.Clamp(into / Math.Max(0.1, shot.Real * 0.7), 0, 1))];
            var lines = Wrap(typed, chars).ToList();
            float lh = (o.Font.Height + 4) * scale;
            float y = height * 0.5f - lines.Count * lh / 2;
            o.Text(width * 0.15f, y - lh * 2, "THE CLERK, ON THE RADIO", Dim, scale);
            foreach (var l in lines)
            {
                o.Text(width * 0.15f, y, l, Ink, scale);
                y += lh;
            }
        }
    }

    /// <summary>
    /// The fortress on the radio (GDD §9; note 178): the dispatcher's manifest or the clerk's tally, a line at a time, typed
    /// out as it's read, flat, the last few on the card.
    /// </summary>
    public static void RadioCard(Overlay o, int width, int height, IReadOnlyList<string> lines, double seconds, RadioTuning t,
        IReadOnlyList<double>? times = null)
    {
        var (shown, typed) = Sim.Run.Radio.Reading(lines, seconds, t, times);
        if (shown == 0)
            return;
        int scale = Math.Max(1, height / 360);
        float lh = (o.Font.Height + 4) * scale, w = width * 0.56f;
        int keep = Math.Min(shown, 4);
        float x = (width - w) / 2, y = 34 * scale, h = lh * (keep + 1) + 8 * scale;
        UiStyle.Plate(o, x, y, w, h, UiStyle.Brass, 0.9f);
        o.Text(x + 6 * scale, y + 4 * scale, "RADIO: THE YARD", Dim, scale);
        float ly = y + 4 * scale + lh;
        int chars = Math.Max(12, (int)((w - 12 * scale) / (o.Font.Measure("M", scale) + scale)));
        for (int i = shown - keep; i < shown; i++)
        {
            string line = lines[i].ToUpperInvariant();
            if (i == shown - 1)
                line = line[..(int)Math.Round(line.Length * typed)];
            o.Text(x + 6 * scale, ly, line.Length > chars ? line[..chars] : line, i == shown - 1 ? Ink : Dim, scale);
            ly += lh;
        }
    }

    /// <summary>
    /// The skip (E.5, E.9), once it counts: hold the key. Each player's own (note 315), the hold filling under it; under the
    /// crew's vote, the votes so far of the crew's.
    /// </summary>
    static void Skip(Overlay o, int width, int height, IPlaySession s)
    {
        if (!s.Skippable)
            return;
        var (votes, of) = s.World.FilmVotes;
        string text = !s.World.WreckTuning.Skip.Own && of > 0 && votes > 0 ? $"HOLD [SPACE] TO SKIP   {votes}/{of}" : "HOLD [SPACE] TO SKIP";
        float w = UiStyle.MeasureKeyed(o, text), x = width - 12 - w;
        UiStyle.Keyed(o, x, height - 18, text, Dim);
        if (s.SkipHold > 0)
            o.Rect(x, height - 8, MathF.Round((float)(w * s.SkipHold)), 1, Ink);
    }

    static IEnumerable<string> Wrap(string text, int chars)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > chars)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }

    /// <summary>The reload's step under way (GDD v1.1 App. C.3: powder, ball, ram), for the prompt.</summary>
    static string LoadStep(in GunState gun, GunTuning t) =>
        (t.ReloadSteps - gun.ReloadNeeded) switch { 0 => "LOAD POWDER", 1 => "LOAD BALL", _ => "RAM IT" };

    /// <summary>
    /// The roof warning's two lines (note 260), or null: nothing coming, or it isn't for you (sight.json <c>roofWarning.roofOnly</c>:
    /// only up top). From the line and the train as this machine has them, so a client says it when the host would.
    /// </summary>
    public static (string Head, string Line)? RoofWarningLines(IPlaySession s)
    {
        var p = s.Player;
        if (s.World.Lineside is not { } lineside || !lineside.For(p, s.World) || lineside.Warning(s.Train) is not { } w)
            return null;
        string when = w.Metres <= 0 ? "NOW" : double.IsFinite(w.Seconds) ? $"IN {Math.Max(1, Math.Ceiling(w.Seconds)):0}S" : $"{w.Metres:0} M AHEAD";
        return w.Kind == SignKind.LowClearance
            ? ("LOW CLEARANCE", w.Metres <= 0 ? "IN THE TUNNEL: STAY OFF THE ROOF" : $"TUNNEL MOUTH {when}: GET OFF THE ROOF")
            : (w.Bridge ? "TOO FAST FOR THE BRIDGE" : "TOO FAST FOR THE BEND", $"{w.LimitKmh} KM/H BOARD {when}: GET OFF THE ROOF");
    }

    /// <summary>
    /// The cab's bend warning (note 265), or null: in the cab, a bend the speed now would derail the train on, under it or
    /// ahead within the distance to brake (LineGen.TrackRules.Assess, urgent); or the bend under it stressed past halfway
    /// from its board to its derailing speed. From the line and the train as this machine has them.
    /// </summary>
    public static (string Head, string Line, bool Urgent)? BendWarningLines(IPlaySession s)
    {
        var world = s.World;
        if (world.TrackPlan is not { } plan || world.Derailed || !s.Player.Alive || !PlayerMotor.InCab(s.Player, s.Train))
            return null;
        var t = s.Train.Dynamics.Tuning.Overspeed;
        var b = Sim.LineGen.TrackRules.Assess(s.Train, plan.Rules, t);
        int kmh = (int)Math.Round(s.Train.Dynamics.Speed * 3.6);
        // The board's figure as it's painted (in fives, down), and the speed it'd come off at.
        int posted = (int)(Math.Floor(b.PostedMs * 3.6 / 5) * 5), derails = (int)Math.Floor(b.DerailMs * 3.6);
        if (b.Warning)
            return b.OnIt
                ? ("FLANGES SCREAMING: YOU'RE COMING OFF", $"{kmh} KM/H ON A {posted} KM/H BEND. IT DERAILS OVER {derails}: BRAKE NOW", true)
                : ("TOO FAST FOR THE BEND AHEAD", $"{posted} KM/H BEND IN {b.AheadM:0} M, DERAILS OVER {derails}. YOU'RE AT {kmh}: BRAKE", true);
        if (b.Stress >= t.LurchAt)
            return ("THE BEND IS PULLING HARD", $"{kmh} KM/H, OVER ITS BOARD: EASE OFF", false);
        return null;
    }

    /// <summary>
    /// The cab's dead-line warning (note 286), or null: in the cab with the engine down a dead line (the Switchman's work, or
    /// a switch set wrong), what to do; and with its buffers coming up faster than it can stop under the speed that goes
    /// through them (Sim.Train.DeadEnds.Assess), urgent. From the line and the train as this machine has them.
    /// </summary>
    public static (string Head, string Line, bool Urgent)? DeadEndLines(IPlaySession s)
    {
        var world = s.World;
        if (world.Derailed || !s.Player.Alive || !PlayerMotor.InCab(s.Player, s.Train))
            return null;
        var d = Sim.Train.DeadEnds.Assess(s.Train, s.Train.Dynamics.Tuning.Overspeed);
        if (!d.OnDeadLine)
            return null;
        int kmh = (int)Math.Round(s.Train.Dynamics.Speed * 3.6), over = (int)Math.Round(d.DerailMs * 3.6);
        if (d.Warning)
            return ("BUFFERS AHEAD: THE LINE ENDS", $"END OF THE LINE IN {d.AheadM:0} M, OFF IT OVER {over} KM/H. YOU'RE AT {kmh}: BRAKE", true);
        return ("DOWN A DEAD LINE", $"THE LINE ENDS IN {d.AheadM:0} M. STOP, BACK UP, SET THE POINTS BACK BY HAND", false);
    }

    /// <summary>
    /// What the death screen says killed you. Every cause has its line (T115 playtest: "the death screen doesn't show me
    /// anything": the v1.1 creatures' causes had none); an unknown one says its name.
    /// </summary>
    public static string DeathLine(DeathCause cause) => cause switch
    {
        DeathCause.Cold => "FROZE",
        DeathCause.JumpedAtSpeed => "JUMPED AT SPEED",
        DeathCause.Derailed => "THE TRAIN LEFT THE RAILS",
        DeathCause.Mauled => "MAULED. SOMETHING GOT HOLD OF YOU AND NOBODY PULLED IT OFF",
        DeathCause.Hollow => "THE HOLLOW",
        DeathCause.Choir => "THE CHOIR",
        DeathCause.Taken => "TAKEN. IT WASN'T THEM OUTSIDE",
        DeathCause.Dragged => "DRAGGED OFF THE EDGE",
        DeathCause.Crushed => "CRUSHED UNDER A DROPPED LOAD",
        DeathCause.PulledUnder => "PULLED UNDER BETWEEN THE CARS",
        DeathCause.Lamplighter => "TORN DOWN AT THE LAMP",
        DeathCause.Deadman => "KILLED TAKING BACK THE CAB",
        DeathCause.Gaunt => "NOBODY WAS WATCHING IT",
        DeathCause.Replaced => "IT WASN'T ONE OF YOU. IT IS NOW",
        DeathCause.Nested => "SOMETHING CAME ABOARD ON SOMEONE'S BACK",
        DeathCause.Drift => "THE GROUND CAME UP. YOU KEPT MOVING",
        DeathCause.TornOff => "WENT OFF THE RAILS WITH THE REAR CAR",
        DeathCause.Climbed => "SOMETHING CAME IN OFF THE ROOF",
        DeathCause.Struck => "STRUCK BY THE TUNNEL MOUTH",
        DeathCause.Thrown => "THROWN OFF ON THE CURVE",
        DeathCause.Burned => "BURNED IN A BLAZING CAR",
        DeathCause.Exploded => "BLOWN UP WITH THE POWDER CAR",
        DeathCause.Poisoned => "GASSED BY THE CHEMICALS",
        DeathCause.Keg => "BLOWN UP BY THE POWDER",
        DeathCause.Leak => "GASSED BY THE LEAK",
        DeathCause.Wreckage => "CRUSHED WHEN THE WRECK SHIFTED",
        DeathCause.Gnawed => "EATEN BY THE GNAWERS",
        DeathCause.Ferryman => "SLOWED FOR THE LANTERN",
        DeathCause.Stoker => "BURNED OPENING THE FIREBOX ON THE STOKER",
        DeathCause.Waiting => "WAITING TO BE PICKED UP",
        DeathCause.Eaten => "SWALLOWED BY THE CAR HUGGER",
        DeathCause.Suffocated => "SMOTHERED. TIPPY TOESIE WAS IN THE CAR",
        DeathCause.Devoured => "EATEN BY THE RIBBITS, DOWN ON THE GROUND",
        DeathCause.Drained => "DRAINED BY A SOOT CHILD",
        DeathCause.Carried => "CARRIED OFF TO THE WHISTLER'S NEST",
        DeathCause.Seized => "SEIZED BY THE CHOIR. YOU WERE OUTSIDE, AND IT WAS LOUD",
        DeathCause.Uncoupled => "TAKEN WITH THE CABOOSE. THE PASSENGER CUT IT LOOSE",
        DeathCause.None => "",
        _ => cause.ToString().ToUpperInvariant(),
    };

    /// <summary>The player's keys (T80), for the prompts: the app sets them from the settings.</summary>
    public static Settings Keys { get; set; } = new();

    /// <summary>
    /// The prompt's print (App. F.1 on the prompts, "they're good but they are too big ... so they take up less space"):
    /// fine print a hand's breadth under the crosshair, where the eye already is, on a thin strip of iron rather than a
    /// riveted plate. Half the HUD's own wherever that still gives each of the font's pixels two of the screen's (1080p and
    /// up); in half steps bigger below that, so it's never mush.
    /// </summary>
    public static float PromptScaleAt(float pixels) => Math.Clamp(MathF.Ceiling(2 * 2 / MathF.Max(1, pixels)) / 2, 0.5f, 1);

    static float _promptScale = 0.5f;

    /// <summary>How far under the screen's middle (the crosshair) the prompt's strip sits, in canvas pixels.</summary>
    public const float PromptDrop = 16;

    /// <summary>The prompt, small, under the crosshair; a hold under way ("... (40%)") as a bar along its foot.</summary>
    static void PromptPlate(Overlay o, int width, int height, string prompt)
    {
        float k = _promptScale;
        float w = UiStyle.MeasureKeyed(o, prompt, k) + 8 * k, h = (o.Font.LineHeight + 6) * k;
        float px = MathF.Round((width - w) / 2), py = MathF.Round(height / 2f + PromptDrop);
        o.Rect(px, py, w, h, UiStyle.Iron with { W = 0.55f });
        o.Rect(px, py + h - k, w, k, UiStyle.Brass with { W = 0.35f });
        UiStyle.Keyed(o, px + 4 * k, py + 3.5f * k, prompt, Ink, k);
        if (System.Text.RegularExpressions.Regex.Match(prompt, @"\((\d+)%\)") is { Success: true } held
            && float.TryParse(held.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out float pct))
        {
            o.Rect(px, py + h, w, 1, Track);
            o.Rect(px, py + h, MathF.Round(w * Math.Clamp(pct / 100f, 0, 1)), 1, UiStyle.Lit);
        }
    }

    /// <summary>A prompt written with the default keys ([E], [RMB], [T]) as the player has them bound.</summary>
    public static string Bound(string prompt) =>
        // One pass, so a key bound where another default was isn't replaced twice (Use on F, the ladder's default).
        System.Text.RegularExpressions.Regex.Replace(prompt, @"\[(E|RMB|T|Z|F|R|B|X|L|H|I|VENT)\]", m => $"[{Controls.KeyLabel(Keys.KeyFor(m.Groups[1].Value switch
        {
            "E" => Control.Use,
            "X" => Control.Reverser,
            "L" => Control.Lamp,
            "H" => Control.Whistle,
            "I" => Control.Supplies,
            "VENT" => Control.Vent,
            "RMB" => Control.Throw,
            "T" => Control.Radio,
            "Z" => Control.Uncouple,
            "F" => Control.Ladder,
            "R" => Control.RegulatorOpen,
            _ => Control.Brake,
        }))}]");

    /// <summary>
    /// A healing find in your hands that you could use now (GDD App. F.1's rare healing loot; note 272): hurt, with nothing
    /// else Use works in reach. The corner offers "USE : HOLD [E]" (<see cref="Hints"/>); what it gives back is learned (note 285).
    /// </summary>
    public static bool CanHeal(IPlaySession s, Body carried) =>
        carried.Kind == BodyKind.Loot && s.World.Run is { Healing: not null } run && run.HealOf(carried) > 0
            && s.Player.Health < s.PlayerTuning.Health && CrewActions.NearestInteractable(s.Player, s.Train, s.World.Hand) is null;

    /// <summary>
    /// A healing find being used (note 272): how far it's got, from the body record, as plain state at the crosshair. Null
    /// otherwise: the find's name and keys are the corner's (note 285).
    /// </summary>
    public static string? HealPrompt(IPlaySession s, Body carried) =>
        CanHeal(s, carried) && carried.MendTicks > 0 && s.World.Run?.Healing is { } h
            ? $"USING IT ({Math.Min(1, carried.MendTicks * Sim.SimConstants.TickSeconds / h.UseSeconds) * 100:0}%)"
            : null;

    public static string? Prompt(IPlaySession s)
    {
        var p = s.Player;
        var train = s.Train;
        var world = s.World;
        if (!p.Alive)
            return null;
        // The Draggers (T46): grabbed at the edge, or near someone who is.
        if (world.Enemies is { } et)
            foreach (var e in world.ActiveEnemies)
                if (e is Sim.Enemies.Dragger { Phase: Sim.Enemies.SpinePhase.Punish } d && d.Target is { } held)
                {
                    if (held == s.PlayerId)
                        return "GRABBED AT THE EDGE";
                    if ((d.WorldPosition(train) - PlayerMotor.WorldPosition(p, train)).Length <= et.Draggers.FreeReach + 1)
                        return "PULL THEM FREE : HOLD [E]";
                }
        // The repair kit at a Holdout's door: held, Use works the lock (GDD App. D.7), not the hands; at a ruptured boiler's
        // firebox, it mends it (T109).
        if (world.Bodies.CarriedBy(s.PlayerId) is { Kind: BodyKind.RepairKit })
        {
            if (HoldoutPrompt(world, p, train, kit: true) is { } opening)
                return opening;
            if (CrewActions.AtTheRupture(p, train, world.Hand) && train.BoilerTuning is { } rt)
                return $"MEND THE BOILER : HOLD [E] ({p.ActionProgress / rt.RepairSeconds * 100:0}%)";
        }
        // A crew locker in front of you (note 173): its door, and its shelves.
        if (LockerPrompt(world, p, s.PlayerId) is { } locker)
            return locker;
        // Note 200: the kit in hand at a broken radio (your own, or one lying in reach): held, it's mended; a tap still puts the
        // kit down.
        if (world.Bodies.CarriedBy(s.PlayerId) is { Kind: BodyKind.RepairKit } && world.Bodies.MendableRadio(p, train, world.Hand, s.PlayerId) is { } radio)
        {
            double mend = train.Dynamics.Tuning.Kit.RadioMendSeconds;
            string whose = radio.Carrier == s.PlayerId ? "YOUR RADIO" : "THE RADIO";
            return radio.MendTicks > 0 ? $"MENDING {whose} ({radio.MendTicks * Sim.SimConstants.TickSeconds / mend * 100:0}%)"
                : $"MEND {whose} : HOLD [E]   PUT DOWN : [E]";
        }
        // Carried, Use puts it down: nothing else in reach is offered. What it is, and how to be rid of it, is the corner's;
        // here only a healing find's use under way (note 272, in note 285's form).
        if (world.Bodies.CarriedBy(s.PlayerId) is { } inHands)
            return HealPrompt(s, inHands);
        // T112: the gun's seat and its own controls.
        if (world.Combat is { } combat && Guns.MannedGun(p, train, combat.Guns) is { } manned)
        {
            var gun = train.Vehicles[manned].Gun;
            // Sat at it, fire and getting up are the corner's (Hints); here, only what's wrong with it.
            bool seated = p.Has(PlayerFlags.Seated);
            // GDD §23 (note 183): a shot's fouled it, and it's cleared by hand before anything else.
            return gun.Jammed ? $"CLEAR THE GUN : HOLD [E] ({Math.Min(1, gun.ReloadProgress / combat.Guns.ClearSeconds) * 100:0}%)"
                : gun.ReloadNeeded > 0 ? $"{LoadStep(gun, combat.Guns)} : HOLD [E]"
                : gun.Ammo <= 0 ? "NO SHOT"
                : train.BoilerTuning is not null && train.Boiler.Pressure < combat.Guns.MinPressure ? "NO STEAM"
                : seated ? null
                : "SIT : [E]   PUSH ALONG : [E] + WALK";
        }
        // A headset player's prompts follow their reaching hand (T29), as the sim's reach does.
        var hand = world.Hand;
        // A breach in the car's shell (decided 1 Oct): boarded up from inside, at the hole, before anything else there.
        if (Breaches.Within(p, train, hand) is not null)
            return $"BOARD IT UP : HOLD [E] ({Math.Min(1, p.ActionProgress / train.Dynamics.Tuning.Breach.BoardSeconds) * 100:0}%)";
        if (p.Parent > 0 && p.Parent < train.Frames.Count && train.Vehicles[p.Parent].Breached && PlayerMotor.Indoors(p, train))
            return "THE CAR'S BREACHED";
        var near = CrewActions.Nearest(p, train, hand);
        // T94: a ladder in reach, and the key that takes you onto it.
        if (PlayerMotor.LadderInReach(p, train, s.PlayerTuning))
            return "CLIMB : [F]";
        // T91: the coupling is cut with its own key, held, looking down at it.
        if (p.Surface == Surface.Coupler && near != InteractableKind.Door)
            return p.Hand != default ? "CUT THE COUPLING : GRIP"
                : p.Pitch <= -train.Dynamics.Tuning.Couplings.UncoupleLookDownDegrees * Math.PI / 180 ? "CUT THE COUPLING : HOLD [Z]"
                : "THE COUPLING : LOOK DOWN";
        // Note 267: the vent's feedback while it's held open (its key, or Use at the valve), from the cab: it's working.
        if (PlayerMotor.InCab(p, train) && train.BoilerTuning is not null && train.Boiler.Vented && !train.Boiler.Ruptured)
            return "VENTING";
        switch (near)
        {
            // GDD §12's whistle cord (note 264), looked at (the director, 7 Oct: "PULL CORD : [E]").
            case InteractableKind.Whistle when PlayerMotor.InCab(p, train):
                return "PULL CORD : [E]";
            // A ruptured boiler (T109): mended here with the repair kit in hand, and only so (the kit's prompt is above).
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train) && train.Boiler.Ruptured:
                return $"BOILER RUPTURED   {RepairKitWhere(world, s.PlayerId)}";
            // Note 275: coal goes on with the shovel, and there's the one (in note 285's form: a short state, nothing foretold).
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train) && !CrewActions.HasShovel(p, train):
                return Kit.Has(p.Kit, Tool.Shovel) || train.Boiler.ShovelOut ? "THE SHOVEL IS OUT" : "HANDS FULL";
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train):
                return p.Hand != default && !p.Has(PlayerFlags.Shovelful) ? "SHOVEL EMPTY" : "SHOVEL COAL : HOLD [E]";
            // Only a reaching hand finds the coal face (T29).
            case InteractableKind.Coal when PlayerMotor.InCab(p, train):
                return p.Has(PlayerFlags.Shovelful) ? "SHOVEL FULL" : "TAKE COAL : GRIP";
            // T97: venting is how the train's slowed (steam sets its speed); T109, in the cab.
            case InteractableKind.Vent when PlayerMotor.InCab(p, train):
                return "VENT STEAM : HOLD [E]";
            // T109: the engineering kit's rack.
            case InteractableKind.ToolRack when PlayerMotor.InCab(p, train):
                return Kit.Held(p) == Tool.Wrench ? "PUT THE WRENCH BACK : [E]" : Kit.Held(p) == Tool.Shovel ? "HANG THE SHOVEL BACK : [E]" : train.Boiler.WrenchOut ? "THE WRENCH IS OUT"
                    : "TAKE THE WRENCH : [E]";
            case InteractableKind.Handbrake when p.Surface == Surface.Roof:
                return "HANDBRAKE : HOLD [E]";
            // T99: a cargo car's roof hatch, for the crane to lower a casting in through.
            case InteractableKind.Hatch when p.Surface == Surface.Roof:
                return train.Vehicles[p.Parent].DoorOpen(CarShape.HatchBit)
                    ? train.HatchBlocked?.Invoke(p.Parent) == true ? "THE CASTING'S IN THE HATCH" : "SHUT THE HATCH : HOLD [E]"
                    : "OPEN THE HATCH : HOLD [E]";
            // Out on the running board (App. A.2).
            case InteractableKind.Sandbox when p.Parent == 0 && p.Surface == Surface.Deck:
                return "SAND THE RAIL : HOLD [E]";
            case InteractableKind.Door:
                return "DOOR : [E]";
            // Spec F.3's powered switch thrower (note 196): the next points ahead, from the cab, slowed for them.
            case InteractableKind.Points when world.Switches is { } stands && SwitchStands.CabLever(p, train, hand) is { } lever:
                {
                    var thrower = train.Dynamics.Tuning.Composition.Thrower;
                    if (lever.Branch is not { } ahead)
                        return "NO POINTS AHEAD";
                    double off = train.Line.Branches[ahead].Toe - train.Line.MainDistance(train.Dynamics.Path, train.Dynamics.Distance);
                    // The thrower's limit is a speed figure, which the director keeps (7 Oct): the lever works under it.
                    return !lever.Slow ? $"POINTS IN {off:0} M: UNDER {thrower.MaxSpeed * 3.6:0} KM/H"
                        : train.PointsOccupied(ahead, stands.Tuning.PointsLength) ? "POINTS HELD"
                        : $"THROW TO {SwitchTo(world, train, ahead)} : HOLD [E]";
                }
        }
        bool wearing = world.Bodies.RadiosCarried && world.Bodies.HasRadio(s.PlayerId);
        if (world.Bodies.InReach(p, train, hand, wearing, s.PlayerId) is { } thing)
            return thing.Kind switch
            {
                BodyKind.Ragdoll => "PICK UP THE BODY : [E]",
                BodyKind.Radio => "TAKE THE RADIO : [E]",
                BodyKind.RepairKit => "TAKE THE REPAIR KIT : [E]",
                BodyKind.Loot => $"TAKE {world.Run?.FindName(thing)?.ToUpperInvariant() ?? "IT"} : [E]",
                // A reaching hand takes its end with both hands on it (T43).
                BodyKind.Heavy when thing.Carrier >= 0 => p.Hand != default ? "TAKE THE OTHER END : GRIP" : "TAKE THE OTHER END : [E]",
                BodyKind.Heavy => p.Hand != default ? "TAKE AN END : GRIP" : "TAKE AN END : [E]",
                _ => "PICK UP : [E]",
            };
        // An open house's hiding spot (note 326), what it is said and the search under way, empty-handed only.
        if (world.Bodies.CarriedBy(s.PlayerId) is null && world.Run?.SpotInReach(p, train) is { } spot)
            return world.Run.SearchProgress(spot) is > 0 and < 1 and var searched
                ? $"SEARCHING THE {SpotName(spot.Container.Kind)} ({searched * 100:0}%)"
                : $"SEARCH THE {SpotName(spot.Container.Kind)} : HOLD [E]";
        if (world.Run?.LeverInReach(p, train, hand) == true)
            return "CHUTE LEVER : HOLD [E]";
        // GDD §18's set pieces (note 185). How full the car under the spout is is what you read to let go.
        if (world.Run?.SpoutLeverInReach(p, train, hand) is { } spout)
        {
            var under = world.Run.CarUnderSpout(train, spout);
            return spout.Pouring ? under is { } filling ? $"POURING   CAR {filling.Load * 100:0}% FULL" : "POURING"
                : under is { } car ? $"SPOUT : HOLD [E]   CAR {car.Load * 100:0}% FULL" : "NO CAR UNDER THE SPOUT";
        }
        if (world.Run?.InPen(p, train) is { } pen)
            return pen.Herding ? $"DRIVING THE HERD ({pen.Head} LEFT)"
                : world.Run.CarAtRamp(train, pen) is null ? "NO CAR AT THE RAMP" : "DRIVE THE HERD : HOLD [E]";
        if (world.Run?.AtHoseStand(p, train) is { } stand)
        {
            string held = world.Run.HoseHold(s.PlayerId) is > 0 and < 1 and var h ? $" ({h * 100:0}%)" : "";
            return stand.HoseCar >= 0
                ? $"HOSE ON{(stand.Leaking ? ", LEAKING" : "")}   TAKE IT OFF : HOLD [E]{held}"
                : world.Run.CarAtHose(train, stand) is null ? "NO CAR BY THE STAND" : $"PUT THE HOSE ON : HOLD [E]{held}";
        }
        // The wreck yard (note 187): a heap in the dark is a heap you can't see into. Its groan is heard, not read.
        if (world.Run?.HeapNear(p, train) is { Found: false, Salvage: > 0, Groan: <= 0 })
            return "TOO DARK TO SEE";
        if (world.Switches?.InReach(p, train, hand) is { } branch)
        {
            // Which way it'll go, and when it won't: the points don't move with a wheel on them.
            return train.PointsOccupied(branch, world.Switches.Tuning.PointsLength) ? "POINTS HELD"
                : $"THROW TO {SwitchTo(world, train, branch)} : HOLD [E]";
        }
        // A yard whose power's down (level-design D.2): restart it at the powerhouse.
        if (world.Run is { } powered && powered.PowerhouseInReach(p, train) && powered.CurrentSite is { } ps)
            return ps.Restart > 0 ? $"RESTARTING THE GENERATOR ({ps.Restart / powered.PowerTuning.RestartSeconds * 100:0}%)"
                : "RESTART THE GENERATOR : HOLD [E]";
        if (HoldoutPrompt(world, p, train, kit: false) is { } breach)
            return breach;
        // The crane (T48): at its controls, or at its hook on the ground.
        if (world.Run?.CurrentSite?.CraneNear(PlayerMotor.WorldPosition(p, train)) is { } crane)
        {
            // At its controls, they're the corner's (Hints).
            if (p.Has(PlayerFlags.Operating))
                return null;
            if (p.Parent == PlayerState.World && ((PlayerMotor.WorldPosition(p, train) - crane.Controls) with { Y = 0 }).Length <= crane.Tuning.ControlsReach)
                return "THE CRANE : HOLD [E]";
            if (p.Parent == PlayerState.World && crane.Riggable(PlayerMotor.WorldPosition(p, train)) is not null)
                return crane.Rigging > 0 ? $"RIGGING ({crane.Rigging * 100:0}%)" : "RIG THE CASTING : HOLD [E]";
        }
        if (world.Run?.HandleInReach(p, train, hand) is not null && world.Run.CurrentSite is { } site)
            // A headset turns the crank round with the hand (T43): which way is how it's worked, so it's said.
            return site.OutOfRhythm ? "OUT OF RHYTHM"
                : p.Hand != default ? "CRANK : OVER THE TOP, TOWARDS THE TRACK" : "CRANK : HOLD [E]";
        // At the controls, driving them is the corner's (Hints): here, only what you're looking at.
        // Note 266 (build 1121: "the lights are completely off"): in a car whose lamp is out (a Climber came in through it).
        if (p.Parent > 0 && p.Parent < train.Frames.Count && !train.Vehicles[p.Parent].LampLit && PlayerMotor.Indoors(p, train)
            && train.Frames[p.Parent].Shape.Interior is not null)
            return $"LIGHT THE LAMP : [{Controls.KeyLabel(Keys.KeyFor(Control.CarLamp))}]";
        return null;
    }

    /// <summary>What a Holdout is, for the dead player in it (App. D.4 "fiction and art").</summary>
    static string HoldoutName(Holdout h) => h.Layout.Kind switch
    {
        HoldoutKind.PrisonCar => "PRISON CAR",
        HoldoutKind.Lockup => "HALT'S LOCKUP",
        _ => "BARRICADED SHELTER",
    };

    /// <summary>
    /// Top left in fine print (note 285): what a stop's waiting on while the train's at one (the chute, the cranes, the spur,
    /// a Holdout lit), and alone in the yard, the way out. Not the route's name, the next place or the clock: the route card
    /// has the night, and the top centre says a place as it comes up and the dawn when it's near.
    /// </summary>
    static void Situation(Overlay o, IPlaySession s)
    {
        // Hosting or joining in the yard, the lobby says it.
        bool lobby = s.Link is { Lost: false } && s.World.Run is { Phase: Sim.Run.RunPhase.Yard };
        string status = PrototypeSession.RouteStatus(s.Route, s.World, s.Train);
        var parts = status.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // The night's result has its own place in the middle; the clock's at the top centre when it matters.
            .Where(t => !t.StartsWith("VIGIL", StringComparison.Ordinal) && !t.StartsWith("DELIVERED", StringComparison.Ordinal)
                && !t.StartsWith("RUN LOST", StringComparison.Ordinal) && !t.StartsWith("DAWN", StringComparison.OrdinalIgnoreCase)
                && t != "IN TUNNEL" && !(lobby && t.StartsWith("in the yard", StringComparison.OrdinalIgnoreCase)))
            .Where(t => s.Route is not { } r || !t.Equals(r.Name, StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith(PrototypeSession.NextPlace(r, s.Train.Dynamics.Distance), StringComparison.OrdinalIgnoreCase))
            // A stop's long list, a line to each thing it's waiting on.
            .SelectMany(t => t.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        float k = Fine, y = 6;
        int line = o.Font.LineHeight;
        foreach (var t in parts)
        {
            o.Text(6, y, t.ToUpperInvariant(), t.StartsWith("STOPPED", StringComparison.Ordinal) ? Amber : Dim, k);
            y += (line + 1) * k;
        }
    }
}
