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
/// The flat-screen HUD (T23), drawn in the low-res frame's own pixels with the pixel font. Sparse on purpose
/// (GDD §32: the screen is the night, not a dashboard):
/// <list type="bullet">
/// <item>top left: the engine (speed, regulator, the pressure gauge with its working band, fire and coal); in the cab only
/// the speed and the levers' state, the gauges on the backhead being the boiler's read-out (note 264, GDD §32);</item>
/// <item>top right: the link, ping to host first and big (spec E: "shown prominently", non-optional);</item>
/// <item>centre: what's happening to you (dead, waiting at a Holdout, cold, the night's result);</item>
/// <item>bottom centre: what your hands can do right here;</item>
/// <item>bottom left: the night (dawn clock, next landmark, a stop).</item>
/// </list>
/// </summary>
public static class Hud
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
        if (s.StrandedOutro)
            Skip(o, width, height, s);
        // GDD §9: the fortress on the radio (the manifest leaving, the tally home) has the top of the screen while it reads.
        if (s.RadioReading is { } reading)
            RadioCard(o, width, height, reading, s.RadioSeconds, s.World.Run?.Tuning.Radio ?? new(), s.RadioTimes);
        Engine(o, s, line);
        RouteStrip(o, width, s, line);
        if (s.Link is { } link)
            Link(o, width, link, line);
        Radio(o, width, s, line);
        Cold(o, width, s, line);
        Alerts(o, width, height, s, line);
        // (Not while the link is lost: who's aboard is stale, and the reconnecting message has the screen.)
        if (s.Link is { Lost: false } lobby && s.World.Run is { Phase: Sim.Run.RunPhase.Yard })
            Lobby(o, height, s, lobby, line);
        // (The night over, its report has the screen: no prompts over it.)
        if (s.World.Run?.Report is null && Prompt(s) is { } written)
            PromptPlate(o, width, height, Bound(written));
        Night(o, height, s, line);
        if (p.Alive)
        {
            Hotbar(o, width, height, p, line, s.World.Bodies.CarriedBy(s.PlayerId) is { } inHands ? Called(s.World, inHands) : null);
            if (s.World.Run?.Report is null)
                Noise(o, width, height, s.World, line);
        }
        // (Not over the run-end screen's report, where it sat in the middle of a line.)
        if (p.Alive && crosshair && s.World.Run?.Report is null)
        {
            // A small cross, for aiming and for "what am I looking at".
            float cx = width / 2f, cy = height / 2f;
            o.Rect(cx - 2, cy, 5, 1, Ink with { W = 0.55f });
            o.Rect(cx, cy - 2, 1, 5, Ink with { W = 0.55f });
            HitMarker(o, cx, cy, s);
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
    /// The hotbar (T108), bottom right: each slot with a tool in it, by its number key, the one in hand lit; an empty slot
    /// picked shows as hands. The wheel steps through the tools.
    /// </summary>
    /// <param name="inHands">
    /// What's carried in both hands (a lamp, the repair kit, a crate), by name: it's what's held, lit at the hotbar's left
    /// (note 264, the director's notes on build 1121: "I don't seem to understand how to hold it in my inventory").
    /// </param>
    static void Hotbar(Overlay o, int width, int height, PlayerState p, int line, string? inHands = null)
    {
        var slots = Enumerable.Range(0, Kit.Slots).Where(i => Kit.At(p.Kit, i) != Tool.None || i == p.HeldSlot).ToList();
        float x = width - 4, y = height - line - 8;
        if (inHands is not null)
            slots.RemoveAll(i => Kit.At(p.Kit, i) == Tool.None);
        for (int k = slots.Count - 1; k >= 0; k--)
        {
            int i = slots[k];
            var tool = Kit.At(p.Kit, i);
            string label = $"{i + 1} {(tool == Tool.None ? "HANDS" : tool.ToString().ToUpperInvariant())}";
            float w = o.Font.Measure(label) + 8;
            x -= w + 2;
            bool held = i == p.HeldSlot;
            UiStyle.Plate(o, x, y - 2, w, line + 6, held ? UiStyle.Lit : null);
            if (held)
                o.Rect(x + 3, y + line + 1, w - 6, 1, UiStyle.Lit);
            o.Text(x + 4, y + 1, label, held && inHands is null ? Amber : Dim);
        }
        if (inHands is not null)
        {
            string label = $"IN HANDS: {inHands}";
            float w = o.Font.Measure(label) + 8;
            x -= w + 2;
            UiStyle.Plate(o, x, y - 2, w, line + 6, UiStyle.Lit);
            o.Rect(x + 3, y + line + 1, w - 6, 1, UiStyle.Lit);
            o.Text(x + 4, y + 1, label, Amber);
        }
    }

    /// <summary>
    /// The lobby (T116, the co-op games' way: Lethal Company's ship, PEAK's airport): while the train's in the yard, who's
    /// aboard, how friends get in, and how the night starts. Drop-in is open here; once the train's out the gate, only at a
    /// facility (spec E).
    /// </summary>
    static void Lobby(Overlay o, int height, IPlaySession s, LinkInfo link, int line)
    {
        var crew = s.Roster();
        float x = 6, y = MathF.Round(height * 0.22f), w = 250;
        // Of the crew cap (note 254), when there is one.
        var lines = new List<(string Text, Vector4 Colour)> { ($"THE LOBBY: {crew.Count} ABOARD{(link.Cap > 0 ? $" OF {link.Cap}" : "")}", Amber) };
        lines.AddRange(crew.Select(c => ($"  {c.Name}{(c.You && c.Name != "YOU" ? " (YOU)" : "")}", c.You ? Ink : Dim)));
        // Hosting at the cap: the lobby's shut (listed FULL, the platform lobby closed) till a place frees. The places taken
        // can be more than the names above: a dropped player's held place, someone waiting to board.
        if (link.Full)
            lines.Add(($"CREW FULL ({link.Places}/{link.Cap}): NOBODY ELSE CAN JOIN", Red));
        else if (link.JoinAt is { } at)
        {
            lines.Add((link.Listed ? "FRIENDS: JOIN, YOUR GAME'S LISTED" : "A PRIVATE LOBBY: FRIENDS JOIN BY INVITE", Dim));
            lines.Add(($"  (OR THEY TYPE {at})", Dim));
        }
        else if (link.PingMs is null && !link.Lost)
            lines.Add(("A PRIVATE NIGHT: NOBODY ELSE CAN JOIN", Dim));
        // The host starts the night; a joiner waits for it (the 4 Oct rehearsal: a joiner was told to drive out).
        bool hosting = link.PingMs is null && !link.Lost;
        lines.Add((hosting ? "EVERYONE IN? DRIVE OUT OF THE YARD" : "THE HOST DRIVES OUT WHEN EVERYONE'S IN", Ink));
        w = lines.Max(l => o.Font.Measure(l.Text)) + 10;
        UiStyle.Plate(o, x - 2, y - 3, w, lines.Count * line + 6);
        foreach (var (text, colour) in lines)
        {
            o.Text(x + 2, y, text, colour);
            y += line;
        }
    }

    /// <summary>
    /// The crew's loudness meter (T113 playtest: "no counterplay" for the Choir), above the hotbar: how loud the crew's been
    /// over the meter's window against the Choir's threshold (the tick), and how far it's gathered. Seeing it climb is the
    /// counterplay: go quiet before it fills.
    /// </summary>
    static void Noise(Overlay o, int width, int height, World world, int line)
    {
        if (world.Combat is not { } c || world.Choir.Spent)
            return;
        var ch = world.Choir;
        float w = 120, h = 5, x = width - 4 - w, y = height - 2 * line - 20;
        double loud = Math.Clamp(ch.Loudness / (c.Choir.Threshold * 2), 0, 1);
        bool over = ch.Loudness >= c.Choir.Threshold;
        o.TextRight(width - 4, y - line - 1, ch.Present ? "THE CHOIR IS HERE: SILENCE"
            : ch.Rest > 0 ? "NOISE  (THE CHOIR'S DRIVEN OFF)" : over ? "NOISE: TOO LOUD" : "NOISE", ch.Present || over ? Amber : Dim);
        o.Rect(x, y, w, h, Dim with { W = 0.35f });
        o.Rect(x, y, (float)(w * loud), h, over ? Amber : Ink with { W = 0.6f });
        o.Rect(x + w / 2, y - 1, 1, h + 2, Ink); // the threshold
        if (ch.Build > 0 && !ch.Present)
            o.Rect(x, y + h + 1, (float)(w * ch.Build), 2, Red);
    }

    /// <summary>
    /// The crew roster (T69, held Q): the session's crew by name, and who's speaking now. Not who's aboard, or where, or
    /// alive: GDD v1.4 made roll call verbal (open question 2). <paramref name="heard"/>: seconds since a crewmate's voice
    /// last came in, null for never.
    /// </summary>
    public static void Roster(Overlay o, int width, int height, IReadOnlyList<RosterLine> lines, Func<byte, double?>? heard)
    {
        int line = o.Font.LineHeight;
        float w = 260, h = (lines.Count + 2) * line + 8;
        float x = MathF.Round((width - w) / 2), y = MathF.Round(height * 0.2f);
        UiStyle.Plate(o, x, y, w, h);
        o.Text(x + 6, y + 4, "THE CREW. ROLL CALL IS SHOUTED", Ink);
        y += 4 + 2 * line;
        foreach (var l in lines)
        {
            o.Text(x + 6, y, l.Name, Ink);
            bool speaking = !l.You && heard?.Invoke(l.Id) is < 2;
            o.TextRight(x + w - 6, y, speaking ? "SPEAKING" : "", Green);
            y += line;
        }
    }

    /// <summary>
    /// The supplies aboard (the director's decision of 2026-10-06, GDD §10; note 264): one panel, toggled on (its key, I),
    /// never always on: the coal, the repair kit and where it is, the extinguishers, the cargo and crates, the stores (lamps,
    /// radios, toys, finds) and the powder and shot. Like the manifest board in a guard van, read when it's wanted.
    /// </summary>
    public static void Supplies(Overlay o, int width, int height, IPlaySession s)
    {
        var lines = SuppliesLines(s.World, s.PlayerId);
        int line = o.Font.LineHeight;
        float col = lines.Max(l => o.Font.Measure(l.Item)) + 12;
        float w = Math.Max(240, col + lines.Max(l => o.Font.Measure(l.Value))) + 12;
        float h = (lines.Count + 2) * line + 8;
        float x = MathF.Round((width - w) / 2), y = MathF.Round(height * 0.2f);
        UiStyle.Plate(o, x, y, w, h);
        o.Text(x + 6, y + 4, "SUPPLIES ABOARD", Amber);
        UiStyle.Keyed(o, x + w - 6 - UiStyle.MeasureKeyed(o, Bound("[I] CLOSE")), y + 4, Bound("[I] CLOSE"), Dim);
        y += 4 + 2 * line;
        foreach (var (item, value, warn) in lines)
        {
            o.Text(x + 6, y, item, Dim);
            o.Text(x + 6 + col, y, value, warn ? Amber : Ink);
            y += line;
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

    static void Engine(Overlay o, IPlaySession s, int line)
    {
        var train = s.Train;
        var d = train.Dynamics;
        var c = s.Controls;
        float x = 6, y = 5;
        var band = SpeedBands.Classify(d.Tuning, d.Speed);
        // In the cab (note 264, the director's notes on build 1121: "way too much UI for what's on the screen"): the gauges on
        // the backhead and the firebox are the boiler's read-out (GDD §32), so the panel's the speed and the levers' state.
        if (PlayerMotor.InCab(s.Player, train) && train.BoilerTuning is { SteamDrive: true })
        {
            string levers = Bound($"BRAKE {(c.Brake > 0 ? "ON " : "OFF")}  [X] {(c.Reverser > 0 ? "FWD" : "REV")}");
            float cw = Math.Max(o.Font.Measure($"{0,3:0} KM/H  ") + o.Font.Measure("CRUISE"), UiStyle.MeasureKeyed(o, levers)) + 10;
            UiStyle.Plate(o, 2, 2, cw, 2 * line + 6, train.Boiler.Ruptured ? Red : null);
            o.Text(x, y, $"{Math.Abs(d.Speed) * 3.6,3:0} KM/H", Ink);
            o.Text(x + 64, y, band.ToString().ToUpperInvariant(), band >= SpeedBand.Cruise ? Amber : Dim);
            UiStyle.Keyed(o, x, y + line, levers, Dim);
            return;
        }
        // Out of the cab, nothing (the director's "too much UI ... not enough in world or embodied", hud-look): the train's
        // going is felt and heard, the driver's gauges are in the cab, and what's aboard is the supplies view's (I). A
        // rupture or a bend taken too fast has the screen's middle (Alerts) wherever you are.
        if (train.BoilerTuning is { SteamDrive: true })
            return;
        UiStyle.Plate(o, 2, 2, 150, 4 * line + 6);
        o.Text(x, y, $"{Math.Abs(d.Speed) * 3.6,3:0} KM/H", Ink);
        o.Text(x + 64, y, band.ToString().ToUpperInvariant(), band >= SpeedBand.Cruise ? Amber : Dim);
        y += line;
        // T97: with steam driving there's no regulator; what the pressure will make is the thing to read.
        string drive = s.Train.BoilerTuning is { SteamDrive: true } steam
            ? $"STEAM {s.Train.Boiler.SteamSpeed(steam, s.Train.Dynamics.Tuning.MaxSpeed) * 3.6,3:0} KM/H"
            : $"REG {c.Throttle * 100,3:0}%";
        o.Text(x, y, $"{drive}  BRAKE {(c.Brake > 0 ? "ON " : "OFF")}  {(c.Reverser > 0 ? "FWD" : "REV")}", Dim);
        y += line;
        var b = train.Boiler;
        if (train.BoilerTuning is not { } bt)
            return;
        if (b.Ruptured)
        {
            o.Text(x, y, "BOILER RUPTURED", Red);
            return;
        }
        // The gauge: the working band marked, the fill coloured by where the needle is.
        float gx = x + 12, gw = 100;
        o.Text(x, y, "P", Dim);
        o.Rect(gx, y + 1, gw, 5, Track);
        float Mark(double pressure) => gx + (float)(pressure / bt.PressureMax) * gw;
        o.Rect(Mark(bt.WorkingBandMin), y + 1, Mark(bt.WorkingBandMax) - Mark(bt.WorkingBandMin), 5, Green with { W = 0.25f });
        var fill = b.Pressure >= bt.Redline ? Red : b.Pressure >= bt.WorkingBandMin ? Green : Amber;
        o.Rect(gx, y + 2, Mark(b.Pressure) - gx, 3, fill);
        // Ticks at the working band's edges, over the fill, so the band reads whatever the needle does.
        o.Rect(Mark(bt.WorkingBandMin), y, 1, 7, Ink);
        o.Rect(Mark(bt.WorkingBandMax), y, 1, 7, Ink);
        o.Rect(Mark(bt.Redline), y, 1, 7, Red);
        o.Text(gx + gw + 4, y, $"{b.Pressure:0}", b.SafetyValveLifting ? Red : Ink);
        y += line;
        var fire = b.LowFire(bt) ? Amber : Dim;
        o.Text(x, y, $"FIRE {b.Firebox:0.0}", fire);
        o.Text(x + 64, y, $"COAL {b.Tender:0}", b.Tender < 40 ? Amber : Dim);
    }

    static void Link(Overlay o, int width, LinkInfo link, int line)
    {
        float right = width - 6;
        if (link.PingMs is { } ping)
        {
            var colour = ping < 80 ? Green : ping < 150 ? Amber : Red;
            // Just the milliseconds at the big size: "PING 100 MS" ran into the route strip at 1280 wide (the 4 Oct rehearsal).
            o.TextRight(right, 5, $"{ping:0} MS", colour, scale: 2);
        }
        else
        {
            // No ping: this is the host, or a joiner whose link has gone (it says so below).
            o.TextRight(right, 5, link.Lost ? "NO LINK" : "HOST", link.Lost ? Red : Ink, scale: 2);
        }
        o.TextRight(right, 5 + 2 * line, $"CREW OF {link.Aboard}", Dim);
        o.TextRight(right, 5 + 3 * line, link.Role, Dim);
        // Note 253: a joiner whose link went tries to get back, and says how it's going; out of tries, F5 tries again.
        if (link.Lost && link.Attempt > 0)
            o.TextRight(right, 5 + 4 * line, $"RECONNECTING: TRY {link.Attempt} OF {link.Attempts}", Amber);
        // Note 254: turned away on the way back (the place ran out, and the crew's full).
        else if (link.Lost && link.Refused is { } refused)
            o.TextRight(right, 5 + 4 * line, $"{refused}: [F5] TRY AGAIN", Red);
        else if (link.Lost && link.CanReconnect)
            o.TextRight(right, 5 + 4 * line, "CONNECTION LOST: [F5] RECONNECT", Red);
        else if (link.Lost)
            o.TextRight(right, 5 + 4 * line, "CONNECTION LOST", Red);
        else if (link.JoinAt is { } at)
            o.TextRight(right, 5 + 4 * line, $"FRIENDS JOIN AT {at}", Dim);
    }

    /// <summary>Whether you've a radio on you (T41), under the link: without one, T does nothing and nobody's on it for you.</summary>
    /// <summary>Where the repair kit is, for a ruptured boiler (T109): it's what mends it, and somebody has to go and get it.</summary>
    /// <summary>Which way a switch goes when it's thrown: back to the main line, or over for its branch.</summary>
    static string SwitchTo(TrainOnLine train, int branch) =>
        train.Diverging(branch) ? "THE MAIN LINE" : $"THE {(train.Line.Branches[branch].Kind == BranchKind.Spur ? "SPUR" : "DEAD LINE")}";

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
            return world.Run?.Kit is { Place: not KitPlace.None } far ? RepairKitFar(far, consist) : "THE REPAIR KIT MENDS IT, AND THE TRAIN HAS NONE";
        if (kit.Carrier == playerId)
            return "THE REPAIR KIT MENDS IT: TO THE FIREBOX WITH IT";
        if (kit.Carrier >= 0)
            return "A CREWMATE HAS THE REPAIR KIT: IT MENDS IT, AT THE FIREBOX";
        int car = consist.IndexOf(kit.Parent);
        // In its locker (note 173): the crew learn which.
        if (car > 0 && kit.Stowed && kit.Locker < world.Train.Frames[kit.Parent].Shape.Lockers.Count)
            return $"THE REPAIR KIT MENDS IT. IT'S IN THE {world.Train.Frames[kit.Parent].Shape.Lockers[kit.Locker].Name}'S LOCKER, CAR {car}";
        return car > 0 ? $"THE REPAIR KIT MENDS IT. IT'S IN CAR {car}" : car == 0 ? "THE REPAIR KIT MENDS IT. IT'S HERE ON THE ENGINE"
            : "THE REPAIR KIT MENDS IT. IT'S OFF THE TRAIN";
    }

    /// <summary>Where the host says the kit is, when it's beyond what this machine is sent (note 263).</summary>
    static string RepairKitFar(KitWhere kit, Consist consist)
    {
        if (kit.Place == KitPlace.Carried)
            return "A CREWMATE HAS THE REPAIR KIT: IT MENDS IT, AT THE FIREBOX";
        if (kit.Place == KitPlace.Lost)
            return "THE REPAIR KIT IS GONE";
        int car = kit.Vehicle >= 0 ? consist.IndexOf(kit.Vehicle) : -1;
        return car > 0 ? $"THE REPAIR KIT MENDS IT. IT'S IN CAR {car}" : car == 0 ? "THE REPAIR KIT MENDS IT. IT'S ON THE ENGINE"
            : "THE REPAIR KIT MENDS IT. IT'S OFF THE TRAIN";
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
            return carried is not null && !full ? $"{name}   [E] PUT {Called(world, carried)} IN" : $"{name}: {Holding(world, at.Car, at.Bay.Index)}   [E] OPEN";
        if (carried is not null)
            return !full ? $"[E] PUT {Called(world, carried)} IN {name}   HOLD: SHUT" : $"{name} IS FULL   [E] HOLD: SHUT";
        return Lockers.Contents(world.Bodies, at.Car, at.Bay.Index).LastOrDefault() is { } top
            ? $"[E] TAKE {Called(world, top)} INTO YOUR HANDS   HOLD: SHUT" : $"{name}: EMPTY   [E] HOLD: SHUT";
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
                return h.Quiet ? $"OPENING THE LOCK {h.Progress / h.Breach(ho.Tuning).Seconds * 100:0}%. QUIETLY. KEEP AT IT"
                    : $"{(h.Layout.Kind == HoldoutKind.Shelter ? "PRYING" : "SMASHING")} IT OPEN {h.Progress / h.Breach(ho.Tuning).Seconds * 100:0}%. LOUD. KEEP AT IT";
            return kit && h.Lockable ? $"[E] HOLD: OPEN THE LOCK WITH THE KIT ({ho.Tuning.Open.Seconds:0}S, SILENT)"
                : $"[E] HOLD: {(h.Layout.Kind == HoldoutKind.Shelter ? "PRY THE BARRICADE" : "SMASH THE LOCK")} ({h.Breach(ho.Tuning).Seconds:0}S, LOUD)";
        }
        return null;
    }

    static void Radio(Overlay o, int width, IPlaySession s, int line)
    {
        var bodies = s.World.Bodies;
        if (!bodies.RadiosCarried || !s.Player.Alive)
            return;
        // Right mouse throws what's in your hands; with them empty, it sets the radio down to pass on.
        string wearing = Bound(bodies.CarriedBy(s.PlayerId) is null ? "RADIO [T]  [RMB] SET IT DOWN" : "RADIO [T]");
        // GDD §23 "radio breaks" (note 183): carried, but smashed.
        bool broken = bodies.All.Any(b => b.Kind == BodyKind.Radio && b.Carrier == s.PlayerId && b.Broken);
        o.TextRight(width - 6, 5 + 5 * line, bodies.HasRadio(s.PlayerId) ? wearing : broken ? "RADIO BROKEN: THE REPAIR KIT MENDS IT" : "NO RADIO", bodies.HasRadio(s.PlayerId) ? Dim : Amber);
    }

    /// <summary>
    /// GDD §22 deep cold on the HUD (note 201): under the radio, while the line where you are is in a cold step, how deep and
    /// what it does: the cold comes on that much faster outside. Amber out in it, dim in the warm.
    /// </summary>
    static void Cold(Overlay o, int width, IPlaySession s, int line)
    {
        if (!s.Player.Alive || ColdLine(s.Player, s.Train, s.PlayerTuning) is not { } cold)
            return;
        o.TextRight(width - 6, 5 + 6 * line, cold, PlayerMotor.NearHeat(s.Player, s.Train) ? Dim : Amber);
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

    /// <summary>
    /// The night's line across the top (T95 playtest): how far there's left to go, and where the stops are, facilities and
    /// villages, ticked along it; the train's the bright mark. Stops behind it dim.
    /// </summary>
    static void RouteStrip(Overlay o, int width, IPlaySession s, int line)
    {
        if (s.Route is not { } route || route.Length <= 0)
            return;
        // Clear of the engine's panel top left and the link's top right.
        float w = MathF.Round(width * 0.34f), x = MathF.Round(width * 0.41f), y = 6, h = 5;
        double at = Math.Clamp(s.Train.Dynamics.Distance / route.Length, 0, 1);
        double left = Math.Max(0, route.Length - s.Train.Dynamics.Distance) / 1000;
        // Note 267: the next place by name, which the bottom-left block said again under it; it says it here only, the
        // plate as wide as it needs.
        string ahead = PrototypeSession.NextPlace(route, s.Train.Dynamics.Distance).ToUpperInvariant();
        string caption = $"{ahead}   {left:0.0} KM LEFT";
        float pw = Math.Max(w + 8, o.Font.Measure(caption) + 10);
        UiStyle.Plate(o, x + w / 2 - pw / 2, y - 3, pw, h + line + 8);
        o.Rect(x, y, w, h, Track);
        o.Rect(x, y, MathF.Round(w * (float)at), h, Dim);
        foreach (var f in route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village))
        {
            float fx = x + MathF.Round(w * (float)Math.Clamp(f.Start / route.Length, 0, 1));
            bool passed = f.Start < s.Train.Dynamics.Distance;
            var colour = passed ? Dim : f.Kind == FeatureKind.Facility ? Amber : Ink;
            o.Rect(fx - 1, y - 2, 3, h + 4, colour);
        }
        o.Rect(x + MathF.Round(w * (float)at) - 2, y - 3, 5, h + 6, Green);
        o.TextCentred(x + w / 2, y + h + 2, caption, Ink);
    }

    static void Alerts(Overlay o, int width, int height, IPlaySession s, int line)
    {
        var p = s.Player;
        var world = s.World;
        float y = height * 0.28f;
        void Big(string text, Vector4 colour)
        {
            o.TextCentred(width / 2f, y, text, colour, scale: 2);
            y += 2 * line + 2;
        }
        void Small(string text, Vector4 colour)
        {
            o.TextCentred(width / 2f, y, text, colour);
            y += line;
        }
        if (s.Link is { Waiting: { } waiting })
        {
            Big("WAITING", Amber);
            Small(waiting, Ink);
        }
        // Note 253: the link's gone. Back in time, the crewmate's still theirs: limp where they stood till then.
        if (s.Link is { Lost: true } lost && world.Run is not { Over: true })
        {
            if (lost.Attempt > 0)
            {
                Big("RECONNECTING", Amber);
                Small($"TRY {lost.Attempt} OF {lost.Attempts}: YOUR BODY LIES WHERE YOU STOOD TILL YOU'RE BACK", Ink);
            }
            // Note 254: back too late, the place had gone, and the crew had filled it.
            else if (lost.Refused is { } refused)
            {
                Big(refused, Red);
                Small("YOUR PLACE WAS HELD TILL IT RAN OUT, AND THE CREW'S FULL: [F5] TRY AGAIN", Ink);
            }
            else if (lost.CanReconnect)
            {
                Big("CONNECTION LOST", Red);
                Small("[F5] RECONNECT", Ink);
            }
        }
        // The derailment's cinematic plays out first (T117): no run's end or death screen over it. Over the replay (T121),
        // what did it: "TOOK THE 45 KM/H BEND AT 68 KM/H, 23 KM/H TOO FAST".
        if (s.WreckCinematic)
        {
            if (DerailSequence.Beat(s.SequenceTuning, s.WreckSeconds) == DerailBeat.Replay)
            {
                Big("REPLAY", Ink);
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
                Small($"{r.CarsDelivered} CARS, {r.CarsLost} LOST. {r.Net:0} SCRIP. CREW HOME {r.CrewHome}", Ink);
            }
            else if (r.End == RunEnd.Stranded)
            {
                // GDD v1.4 §23.2: nobody died of it, and nobody much cares.
                Big("STRANDED", Amber);
                Small(EngineeringKit.Line(r.KitLoss), Ink);
                Small($"RECOVERY AT FIRST LIGHT. RECOVERY IS CHARGEABLE: {r.Recovery:0} SCRIP", Dim);
            }
            else
            {
                Big("RUN LOST", Red);
                Small(r.End switch { RunEnd.Derailed => "DERAILED", RunEnd.CrewLost => "THE WHOLE CREW IS DEAD", _ => "STILL OUT WHEN THE LINE WENT LIVE" }, Ink);
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
            Small($"COLD: {Math.Max(0, s.PlayerTuning.Cold.DeathSeconds - p.Cold):0}S. GET INSIDE", p.Cold > s.PlayerTuning.Cold.DeathSeconds - 30 ? Red : Amber);
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
                    Small("THE REPAIR KIT IS GONE: STRANDED WHEN SHE STOPS", Red);
                else
                    Small(RepairKitWhere(world, s.PlayerId), Ink);
            }
            else if (b.AtMaxSeconds > 0)
            {
                Big($"VENT! RUPTURE IN {Math.Max(0, bt.RuptureHoldSeconds - b.AtMaxSeconds):0}S", flash ? Red : Amber);
                Small(Bound("[VENT] HOLD IN THE CAB"), Ink);
            }
            else if (b.Pressure >= bt.Redline)
                Small(Bound("PRESSURE IN THE RED: [VENT] HOLD TO VENT, OR LET THE FIRE BURN DOWN"), flash ? Red : Amber);
        }
        // Note 267: a crewmate's whistle names whose hand is on the cord (GDD §12). The Whistler's has no hand on it, and no
        // line (App. A.4: "the whistle sounds with no hand on the cord" is its tell), so one with no name is the Whistler.
        if (p.Alive && world.WhistleSeconds > 0 && world.WhistleBy >= 0)
            Small(world.WhistleBy == s.PlayerId ? "THE WHISTLE: YOUR HAND'S ON THE CORD (LOUD)" : $"THE WHISTLE: {IncidentLog.NameOf(world, world.WhistleBy).ToUpperInvariant()} ON THE CORD", Dim);
        // T115 playtest ("suddenly I can't move and then a few seconds later I die"): held, say so, and what to do. Alone
        // (the solo rule) Use held struggles free; with a crew, a friend has to pull it off or hit it.
        if (p.Alive && p.Has(PlayerFlags.Held))
        {
            Big("SOMETHING HAS YOU", world.Tick / 10 % 2 == 0 ? Red : Amber);
            bool alone = s.Roster().Count(l => l.Alive) <= 1;
            Small(alone ? Bound("HOLD [E] TO STRUGGLE FREE") : "SHOUT FOR HELP: A CREWMATE CAN PULL IT OFF, OR HIT IT", Ink);
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
            Big(bend.Head, bend.Urgent ? (world.Tick / 6 % 2 == 0 ? Red : Amber) : Amber);
            Small(bend.Line, Ink);
        }
        // T113: the Choir's long telegraph, said plainly once it's well along, and what to do about it.
        if (p.Alive && world.Combat is not null && !world.Choir.Present && world.Choir.Build > 0.25)
            Small("THE CHOIR IS GATHERING: GO QUIET", world.Tick / 15 % 2 == 0 ? Red : Amber);
    }

    /// <summary>
    /// The incident report (GDD v1.4 App. D.12): deaths with who, where and the cause line (C.9) beside the fee and refund,
    /// rescues, the boiler, cars lost, and how it ended; then the night's money. The clerk's flat voice, top to bottom; what
    /// won't fit says how many more.
    /// </summary>
    /// <summary>
    /// The dead's card (GDD App. D.6-D.10), on a plate of its own in the lower middle, clear of what they're watching: DEAD
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
            rows.Add(($"WATCHING CREW {s.Watching}   [{Controls.KeyLabel(Keys.KeyFor(Control.Fire))}] OR [{Controls.KeyLabel(Keys.KeyFor(Control.Right))}] NEXT   " +
                $"[{Controls.KeyLabel(Keys.KeyFor(Control.Left))}] BACK", Ink));
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
                rows.Add(($"[{Controls.KeyLabel(Keys.KeyFor(Control.Bookmark))}] BOOKMARK THIS ({left} LEFT)", Dim));
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
                    rows.Add(("[E] CALL OUT   [RMB] LET SOMEONE ELSE GO FIRST", Dim));
                }
                // D.7 Live Mic: theirs alone, off by default; on, the rescuer at the door hears what they say to the dead.
                rows.Add((mine.LiveMic ? "[SPACE] LIVE MIC: ON. THEY HEAR YOU AT THE DOOR" : "[SPACE] LIVE MIC: OFF", mine.LiveMic ? Green : Dim));
            }
            else
            {
                rows.Add(("YOU'LL WAIT AT THE NEXT HALT OR YARD, IF THEY STOP FOR YOU", Dim));
                rows.Add(("[RMB] LET SOMEONE ELSE GO FIRST", Dim));
            }
            // D.11: the creature vote has a plate of its own (BallotPlate, note 202); once cast, the card keeps a line of it.
            if (s.Ballot is { Cast: { } cast })
                rows.Add(($"YOU CALLED THE {Creature(cast)}. THE LIVING WON'T KNOW TILL THE END", Dim));
            if (s.VoteCue is { } cue)
                rows.Add((cue, Red));
            // D.6: the dead and lobbied see the whole queue, and where they are in it (the living see nothing).
            if (QueueLine(world, holdouts, s.PlayerId) is { } queue)
                rows.Add((queue, Ink));
        }
        float big = o.Font.Measure("DEAD", 2);
        float w = Math.Max(big, rows.Max(r => UiStyle.MeasureKeyed(o, r.Text))) + 20, rowH = line + 3;
        float h = 2 * line + 8 + rows.Count * rowH + 8;
        // Low in the frame, clear of what they're watching, but never off its foot (the vote and the queue make it tall).
        float x = MathF.Round((width - w) / 2), y = MathF.Round(Math.Min(height * 0.56f, height - h - 30));
        UiStyle.Plate(o, x, y, w, h);
        o.TextCentred(width / 2f, y + 6, "DEAD", Red, scale: 2);
        y += 2 * line + 10;
        foreach (var (text, colour) in rows)
        {
            UiStyle.Keyed(o, MathF.Round((width - UiStyle.MeasureKeyed(o, text)) / 2), y, text, colour);
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
        float rowH = line + 4;
        float notes = rows.Max(r => r.Note is null ? 0 : o.Font.Measure(r.Note) + 12);
        float w = Math.Max(o.Font.Measure(BallotTitle), rows.Max(r => UiStyle.MeasureKeyed(o, r.Text) + (r.Note is null ? 0 : notes))) + 16;
        float h = line + 10 + rows.Count * rowH + 2;
        float x = MathF.Round(width - w - 6), y = 44;
        UiStyle.Plate(o, x, y, w, h, s.Ballot is { Cast: null } ? Amber with { W = 0.7f } : null);
        o.Text(x + 8, y + 5, BallotTitle, Amber);
        y += line + 10;
        foreach (var (text, colour, picked, note) in rows)
        {
            if (picked)
                o.Rect(x + 4, y - 3, w - 8, rowH, colour with { W = 0.18f });
            UiStyle.Keyed(o, x + 8, y, text, colour);
            if (note is not null)
                o.Text(x + w - 8 - o.Font.Measure(note), y, note, picked ? colour : Dim);
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

    /// <summary>The skip vote (E.5, E.9), once it counts: hold the key; the votes so far of the crew's.</summary>
    static void Skip(Overlay o, int width, int height, IPlaySession s)
    {
        if (!s.Skippable)
            return;
        var (votes, of) = s.World.FilmVotes;
        string text = of > 0 && votes > 0 ? $"HOLD [SPACE] TO SKIP   {votes}/{of}" : "HOLD [SPACE] TO SKIP";
        UiStyle.Keyed(o, width - 12 - UiStyle.MeasureKeyed(o, text), height - 18, text, Dim);
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
        (t.ReloadSteps - gun.ReloadNeeded) switch { 0 => "POWDER", 1 => "BALL", _ => "RAM" };

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

    /// <summary>What your hands can do right here, with the key that does it.</summary>
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
                        return "GRABBED AT THE EDGE! SOMEONE PULL YOU FREE";
                    if ((d.WorldPosition(train) - PlayerMotor.WorldPosition(p, train)).Length <= et.Draggers.FreeReach + 1)
                        return "[E] HOLD: PULL THEM FREE";
                }
        // The repair kit at a Holdout's door: held, Use works the lock (quietly, GDD App. D.7), not the hands; at a ruptured
        // boiler's firebox, it mends it (T109).
        if (world.Bodies.CarriedBy(s.PlayerId) is { Kind: BodyKind.RepairKit })
        {
            if (HoldoutPrompt(world, p, train, kit: true) is { } opening)
                return opening;
            if (CrewActions.AtTheRupture(p, train, world.Hand) && train.BoilerTuning is { } rt)
                return $"[E] HOLD: MEND THE BOILER WITH THE KIT ({p.ActionProgress / rt.RepairSeconds * 100:0}%)";
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
            return radio.MendTicks > 0
                ? $"[E] HOLD: MENDING {whose} WITH THE KIT ({radio.MendTicks * Sim.SimConstants.TickSeconds / mend * 100:0}%)"
                : $"[E] HOLD: MEND {whose} WITH THE KIT ({mend:0}S)   [E] PUT DOWN";
        }
        if (world.Bodies.CarriedBy(s.PlayerId) is { } carried)
            return carried.Kind switch
            {
                // Spec D.2 "heavy items need two" (T43).
                BodyKind.Heavy when !carried.Lifted => "HOLDING AN END: IT NEEDS TWO   [E] LET GO",
                BodyKind.Heavy => "TOGETHER, INTO A CAR: [E] PUT IT DOWN",
                BodyKind.Cargo => "INTO A CAR TO LOAD IT: [E] PUT DOWN   [RMB] THROW",
                // A village find (level-design P12): it pays once it's put down aboard, in any car.
                BodyKind.Loot => $"{world.Run?.FindName(carried)?.ToUpperInvariant() ?? "A FIND"}: INTO ANY CAR TO KEEP IT   [E] PUT DOWN   [RMB] THROW",
                BodyKind.RepairKit => world.Train.Boiler.Ruptured ? "THE REPAIR KIT: TO THE FIREBOX WITH IT   [E] PUT DOWN"
                    : "THE REPAIR KIT: IT MENDS THE BOILER, AND OPENS A LOCK QUIETLY   [E] PUT DOWN   [RMB] THROW",
                _ => "[E] PUT DOWN   [RMB] THROW",
            };
        // T112: the gun's seat and its own controls.
        if (world.Combat is { } combat && Guns.MannedGun(p, train, combat.Guns) is { } manned)
        {
            var gun = train.Vehicles[manned].Gun;
            bool seated = p.Has(PlayerFlags.Seated);
            string up = seated ? "   [SPACE] GET UP" : "";
            // GDD §23 (note 183): a shot's fouled it, and it's cleared by hand before anything else.
            return gun.Jammed ? $"GUN FOULED: [E] HOLD: CLEAR IT ({Math.Min(1, gun.ReloadProgress / combat.Guns.ClearSeconds) * 100:0}%){up}"
                : gun.ReloadNeeded > 0 ? $"[E] HOLD: LOAD IT ({LoadStep(gun, combat.Guns)}){up}"
                : gun.Ammo <= 0 ? $"NO SHOT LEFT{up}"
                : train.BoilerTuning is not null && train.Boiler.Pressure < combat.Guns.MinPressure ? $"NO STEAM TO TURN THE GUN{up}"
                : seated ? $"[LMB] FIRE   AIM WITH THE MOUSE{up}"
                : "[E] SIT AT THE GUN   [E] + WALK: PUSH IT ALONG THE RAIL";
        }
        // A headset player's prompts follow their reaching hand (T29), as the sim's reach does.
        var hand = world.Hand;
        // A breach in the car's shell (decided 1 Oct): boarded up from inside, at the hole, before anything else there.
        if (Breaches.Within(p, train, hand) is not null)
            return $"[E] HOLD: BOARD UP THE BREACH ({Math.Min(1, p.ActionProgress / train.Dynamics.Tuning.Breach.BoardSeconds) * 100:0}%)";
        if (p.Parent > 0 && p.Parent < train.Frames.Count && train.Vehicles[p.Parent].Breached && PlayerMotor.Indoors(p, train))
            return train.Dynamics.Tuning.Breach.NeedsKit && !p.Has(PlayerFlags.RepairKit) ? "THE CAR'S BREACHED: BRING THE REPAIR KIT TO BOARD IT UP"
                : "THE CAR'S BREACHED: BOARD UP THE HOLE";
        var near = CrewActions.Nearest(p, train, hand);
        // T94: a ladder in reach, and the key that takes you onto it.
        if (PlayerMotor.LadderInReach(p, train, s.PlayerTuning))
            return "[F] GRAB LADDER";
        // T91: the coupling is cut with its own key, held, looking down at it.
        if (p.Surface == Surface.Coupler && near != InteractableKind.Door)
            return p.Hand != default ? "REACH DOWN AND GRIP: CUT THE COUPLING"
                : p.Pitch <= -train.Dynamics.Tuning.Couplings.UncoupleLookDownDegrees * Math.PI / 180 ? "[Z] HOLD: CUT THE COUPLING"
                : "LOOK DOWN AT THE COUPLER TO CUT IT";
        // Note 267: the vent's feedback while it's held open (its key, or Use at the valve), from the cab: it's working.
        if (PlayerMotor.InCab(p, train) && train.BoilerTuning is not null && train.Boiler.Vented && !train.Boiler.Ruptured)
            return $"VENTING STEAM: PRESSURE {train.Boiler.Pressure:0}, FALLING. LET GO TO SHUT IT";
        switch (near)
        {
            // GDD §12's whistle cord (note 264), looked at: loud, the meter and the Choir hear it, in your name.
            case InteractableKind.Whistle when PlayerMotor.InCab(p, train):
                return "[E] HOLD: WHISTLE (LOUD: THE CHOIR HEARS IT)   OR [H]";
            // A ruptured boiler (T109): mended here with the repair kit in hand, and only so (the kit's prompt is above).
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train) && train.Boiler.Ruptured:
                return $"BOILER RUPTURED: {RepairKitWhere(world, s.PlayerId)}";
            // Note 275: coal goes on with the shovel, and there's the one.
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train) && !CrewActions.HasShovel(p, train):
                return Kit.Has(p.Kit, Tool.Shovel) || train.Boiler.ShovelOut ? "THE SHOVEL IS OUT: WHOEVER HAS IT FIRES" : "HANDS FULL: NO ROOM FOR THE SHOVEL";
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train):
                return p.Hand != default && !p.Has(PlayerFlags.Shovelful) ? "SHOVEL COAL: FILL IT AT THE TENDER FIRST" : "[E] HOLD: SHOVEL COAL (FASTER)";
            // Only a reaching hand finds the coal face (T29).
            case InteractableKind.Coal when PlayerMotor.InCab(p, train):
                return p.Has(PlayerFlags.Shovelful) ? "SHOVEL FULL: INTO THE FIREBOX" : "GRIP: COAL ON THE SHOVEL";
            // T97: venting is how the train's slowed (steam sets its speed); T109, in the cab.
            case InteractableKind.Vent when PlayerMotor.InCab(p, train):
                return "[E] HOLD: VENT STEAM (SLOWER)   OR [VENT] ANYWHERE IN THE CAB";
            // T109: the engineering kit's rack.
            case InteractableKind.ToolRack when PlayerMotor.InCab(p, train):
                return Kit.Held(p) == Tool.Wrench ? "[E] PUT THE WRENCH BACK" : Kit.Held(p) == Tool.Shovel ? "[E] HANG THE SHOVEL BACK" : train.Boiler.WrenchOut ? "THE WRENCH IS OUT"
                    : "[E] TAKE THE WRENCH";
            case InteractableKind.Handbrake when p.Surface == Surface.Roof:
                return "[E] HOLD: HANDBRAKE";
            // T99: a cargo car's roof hatch, for the crane to lower a casting in through.
            case InteractableKind.Hatch when p.Surface == Surface.Roof:
                return train.Vehicles[p.Parent].DoorOpen(CarShape.HatchBit)
                    ? train.HatchBlocked?.Invoke(p.Parent) == true ? "THE CASTING'S IN THE HATCH" : "[E] HOLD: SHUT THE HATCH"
                    : "[E] HOLD: OPEN THE HATCH (CRANE LOADING)";
            // Out on the running board (App. A.2): what the sand does is only worth it on greased rail.
            case InteractableKind.Sandbox when p.Parent == 0 && p.Surface == Surface.Deck:
                return train.Traction < 1 || train.Sand > 0 ? $"[E] HOLD: SAND THE RAIL ({train.Traction * 100:0}% GRIP)" : "[E] HOLD: SAND";
            case InteractableKind.Door:
                return "[E] DOOR";
            // Spec F.3's powered switch thrower (note 196): the next points ahead, from the cab, slowed for them.
            case InteractableKind.Points when world.Switches is { } stands && SwitchStands.CabLever(p, train, hand) is { } lever:
                {
                    var thrower = train.Dynamics.Tuning.Composition.Thrower;
                    if (lever.Branch is not { } ahead)
                        return $"POWERED POINTS: NONE WITHIN {thrower.Reach:0} M AHEAD";
                    double off = train.Line.Branches[ahead].Toe - train.Line.MainDistance(train.Dynamics.Path, train.Dynamics.Distance);
                    return !lever.Slow ? $"POWERED POINTS {off:0} M AHEAD: SLOW TO {thrower.MaxSpeed * 3.6:0} KM/H TO THROW THEM"
                        : train.PointsOccupied(ahead, stands.Tuning.PointsLength) ? "POWERED POINTS: HELD, A WHEEL IS ON THEM"
                        : $"[E] HOLD: THROW THE POINTS {off:0} M AHEAD TO {SwitchTo(train, ahead)}";
                }
        }
        bool wearing = world.Bodies.RadiosCarried && world.Bodies.HasRadio(s.PlayerId);
        if (world.Bodies.InReach(p, train, hand, wearing, s.PlayerId) is { } thing)
            return thing.Kind switch
            {
                BodyKind.Ragdoll => "[E] PICK UP THE BODY",
                BodyKind.Radio => "[E] TAKE THE RADIO",
                BodyKind.RepairKit => "[E] TAKE THE REPAIR KIT INTO YOUR HANDS",
                BodyKind.Loot => $"[E] TAKE {world.Run?.FindName(thing)?.ToUpperInvariant() ?? "IT"}",
                // A reaching hand takes its end with both hands on it (T43).
                BodyKind.Heavy when thing.Carrier >= 0 => p.Hand != default ? "BOTH HANDS ON IT: TAKE THE OTHER END" : "[E] TAKE THE OTHER END",
                BodyKind.Heavy => p.Hand != default ? "HEAVY: BOTH HANDS ON AN END (IT NEEDS TWO)" : "[E] TAKE AN END (IT NEEDS TWO)",
                _ => "[E] PICK UP",
            };
        if (world.Run?.LeverInReach(p, train, hand) == true)
            return "[E] HOLD: CHUTE LEVER";
        // GDD §18's set pieces (note 185).
        if (world.Run?.SpoutLeverInReach(p, train, hand) is { } spout)
            return spout.Pouring ? $"POURING: LET GO WHEN THE CAR'S FULL ({spout.Bin:0.0} LOADS IN THE BIN)"
                : world.Run.CarUnderSpout(train, spout) is { } under ? $"[E] HOLD: SPOUT (CAR UNDER IT {under.Load * 100:0}% FULL)"
                : "[E] HOLD: SPOUT. NO CAR UNDER IT: WALK ONE UNDER";
        if (world.Run?.InPen(p, train) is { } pen)
            return pen.Herding ? $"DRIVING THE HERD ({pen.Head} LEFT). KEEP AT IT"
                : world.Run.CarAtRamp(train, pen) is null ? "THE HERD: NO CAR WITH ROOM AT THE RAMP"
                : $"[E] HOLD: DRIVE THE HERD UP THE RAMP (IT NEEDS {world.Run.FacilityTuning?.Ramp.Herders ?? 2}, LOUD)";
        if (world.Run?.AtHoseStand(p, train) is { } stand)
        {
            string held = world.Run.HoseHold(s.PlayerId) is > 0 and < 1 and var h ? $" {h * 100:0}%" : "";
            return stand.HoseCar >= 0
                ? $"HOSE ON: PRESSURE {stand.Pressure * 100:0}%{(stand.Leaking ? " LEAKING" : "")}. STAY BY IT   [E] HOLD: TAKE IT OFF{held}"
                : world.Run.CarAtHose(train, stand) is null ? "HOSE: NO CAR WITH ROOM BY THE STAND" : $"[E] HOLD: PUT THE HOSE ON THE CAR{held}";
        }
        // The wreck yard (note 187): its tell, and what a lamp's for.
        if (world.Run?.HeapNear(p, train) is { } heap)
            return heap.Groan > 0 ? "IT'S GOING: GET CLEAR OF THE WRECK"
                : !heap.Found && heap.Salvage > 0 ? "THE WRECK: TOO DARK TO SEE WHAT'S IN IT. BRING A LAMP"
                : heap.Stability <= (world.Run.FacilityTuning?.Wreck.StrainPerPiece ?? 0.4) ? "THE WRECK CREAKS: ONE MORE PIECE AND IT GOES"
                : "THE WRECK: CARRY THE SALVAGE OUT TO A CAR";
        if (world.Switches?.InReach(p, train, hand) is { } branch)
        {
            // Say which way it'll go, and when it won't: the points don't move with a wheel on them.
            return train.PointsOccupied(branch, world.Switches.Tuning.PointsLength)
                ? "SWITCH: POINTS HELD, A WHEEL IS ON THEM"
                : $"[E] HOLD: THROW THE SWITCH TO {SwitchTo(train, branch)}";
        }
        // A yard whose power's down (level-design D.2): restart it at the powerhouse.
        if (world.Run is { } powered && powered.PowerhouseInReach(p, train) && powered.CurrentSite is { } ps)
            return ps.Restart > 0 ? $"RESTARTING THE GENERATOR {ps.Restart / powered.PowerTuning.RestartSeconds * 100:0}%. KEEP HOLDING"
                : $"[E] HOLD: RESTART THE GENERATOR ({powered.PowerTuning.RestartSeconds:0}S, LOUD)";
        if (HoldoutPrompt(world, p, train, kit: false) is { } breach)
            return breach;
        // The crane (T48): at its controls, or at its hook on the ground.
        if (world.Run?.CurrentSite?.CraneNear(PlayerMotor.WorldPosition(p, train)) is { } crane)
        {
            if (p.Has(PlayerFlags.Operating))
                return crane.Hooked is null ? "CRANE: WASD BRIDGE AND TROLLEY   SPACE/B HOOK   LET GO OF E TO STEP DOWN"
                    : "CRANE: WASD BRIDGE AND TROLLEY   SPACE/B HOOK   [LMB] LET GO (SET IT DOWN FIRST)";
            if (p.Parent == PlayerState.World && ((PlayerMotor.WorldPosition(p, train) - crane.Controls) with { Y = 0 }).Length <= crane.Tuning.ControlsReach)
                return "[E] HOLD: THE CRANE'S CONTROLS (UP IN THE CAB)";
            if (p.Parent == PlayerState.World && crane.Riggable(PlayerMotor.WorldPosition(p, train)) is not null)
                return crane.Rigging > 0 ? $"RIGGING THE CASTING {crane.Rigging * 100:0}%" : "[E] HOLD: RIG THE CASTING TO THE HOOK";
        }
        if (world.Run?.HandleInReach(p, train, hand) is not null && world.Run.CurrentSite is { } site)
            // A headset turns the crank round with the hand (T43); out of rhythm, the drum stalls (spec D.2).
            return site.OutOfRhythm ? "OUT OF RHYTHM: MATCH THE OTHER CRANK"
                : p.Hand != default ? site.Turning ? "CRANK: OVER THE TOP, TOWARDS THE TRACK. KEEP TOGETHER" : "CRANK: OVER THE TOP, TOWARDS THE TRACK (IT NEEDS TWO)"
                : site.Turning ? "[E] HOLD: CRANK. KEEP TOGETHER" : "[E] HOLD: CRANK (IT NEEDS TWO)";
        if (CabControls.CanDrive(p, train))
        {
            // T97: steam drives it. At a stand on the brake, R lets it off; otherwise B brakes (coal and the vent do the rest).
            string drive = train.BoilerTuning?.SteamDrive != true ? "[R/F] REGULATOR   [B] BRAKE"
                : s.Controls.Brake > 0 && train.Dynamics.Speed < CabControls.StandingBelow ? "[R] RELEASE BRAKE" : "[B] BRAKE";
            // Note 267 (the director's notes on build 1121: "way too much UI", and the vent's control "a bit off"): the brake
            // and the vent, one key each, held; the reverser's key is on the engine panel by its state, the lamp's on the lamp.
            string vent = train.BoilerTuning is null ? "" : "   [VENT] HOLD: VENT";
            return world.LampOutSeconds > 0 ? $"{drive}{vent}   LAMP SMASHED ({world.LampOutSeconds:0}s)"
                : $"{drive}{vent}   [L] LAMP {(world.LampLit ? "OFF" : "ON")}";
        }
        // Note 266 (build 1121: "the lights are completely off"): in a car whose lamp is out (a Climber came in through it),
        // how to light it.
        if (p.Parent > 0 && p.Parent < train.Frames.Count && !train.Vehicles[p.Parent].LampLit && PlayerMotor.Indoors(p, train)
            && train.Frames[p.Parent].Shape.Interior is not null)
            return $"THE CAR'S LAMP IS OUT: [{Controls.KeyLabel(Keys.KeyFor(Control.CarLamp))}] LIGHT IT";
        return null;
    }

    /// <summary>What a Holdout is, for the dead player in it (App. D.4 "fiction and art").</summary>
    static string HoldoutName(Holdout h) => h.Layout.Kind switch
    {
        HoldoutKind.PrisonCar => "PRISON CAR",
        HoldoutKind.Lockup => "HALT'S LOCKUP",
        _ => "BARRICADED SHELTER",
    };

    static void Night(Overlay o, int height, IPlaySession s, int line)
    {
        string status = PrototypeSession.RouteStatus(s.Route, s.World, s.Train);
        var parts = status.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // The night's result has its own place in the middle.
            .Where(t => !t.StartsWith("VIGIL", StringComparison.Ordinal) && !t.StartsWith("DELIVERED", StringComparison.Ordinal) && !t.StartsWith("RUN LOST", StringComparison.Ordinal))
            // Note 267: the route's name (the route card has it) and the next place (the strip across the top has it).
            .Where(t => s.Route is not { } r || !t.Equals(r.Name, StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith(PrototypeSession.NextPlace(r, s.Train.Dynamics.Distance), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (parts.Count == 0)
            return;
        float y = height - 4 - parts.Count * line;
        UiStyle.Plate(o, 2, y - 3, parts.Max(t => o.Font.Measure(t)) + 8, parts.Count * line + 4);
        foreach (var t in parts)
        {
            o.Text(6, y, t, t.Contains("DAWN", StringComparison.Ordinal) || t.StartsWith("STOPPED", StringComparison.Ordinal) ? Amber : Dim);
            y += line;
        }
    }
}
