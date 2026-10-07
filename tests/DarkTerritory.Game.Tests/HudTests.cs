using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>The pixel font, the overlay, and what the HUD says (T23).</summary>
public class HudTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheFontCoversWhatTheHudWrites()
    {
        var font = BitmapFont.Default;
        Assert.Equal(5, font.Width);
        Assert.Equal(7, font.Height);
        var unknown = font.Glyph('?');
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:;!?%/()[]-+=<>#'\"_|°abcxyz")
            Assert.True(c == '?' || !ReferenceEquals(font.Glyph(c), unknown), $"no glyph for '{c}'");
        // Lower case is drawn as capitals, and typography falls back to something close.
        Assert.Same(font.Glyph('A'), font.Glyph('a'));
        Assert.Same(font.Glyph('-'), font.Glyph('—'));
        Assert.Same(unknown, font.Glyph('€'));
        Assert.Equal(17, font.Measure("ABC"));
    }

    [Fact]
    public void TextIsQuadsForRunsOfInk()
    {
        var o = new Overlay();
        o.Text(0, 0, "I", Vector4.One, shadow: false);
        // 'I': a bar across the top and bottom, and the stem between: seven runs, two triangles each.
        Assert.Equal(7 * 6, o.Count);
        o.Clear();
        o.Text(0, 0, "I", Vector4.One);
        Assert.Equal(2 * 7 * 6, o.Count);
    }

    [Fact]
    public void AFouledGunAndABreachedCarSayHowToPutThemRight()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        // GDD §23: at a fouled gun, clear it by hand.
        var mount = train.Frames[0].Shape.Gun!.Value;
        s.Player = PlayerMotor.SpawnOnRoof(train, 0, mount.Position.Z - mount.Facing.Z * 0.7, s.PlayerTuning);
        Assert.Equal("[E] SIT AT THE GUN   [E] + WALK: PUSH IT ALONG THE RAIL", Hud.Prompt(s));
        train.Vehicles[0].Gun.Jammed = true;
        Assert.Equal("GUN FOULED: [E] HOLD: CLEAR IT (0%)", Hud.Prompt(s));
        // Decided 1 Oct: in a breached car, board up the hole; at it, hold Use.
        var room = train.Frames[2].Shape.Interior!.Value;
        train.Vehicles[2].Breach(Breaches.EndWall(train.Frames[2].Shape)!.Value);
        s.Player = new PlayerState { Parent = 2, Surface = Surface.Deck, Health = 100, Position = new Double3(-0.45, room.Min.Y, room.Min.Z + 1) };
        Assert.Equal("THE CAR'S BREACHED: BOARD UP THE HOLE", Hud.Prompt(s));
        s.Player = s.Player with { Position = Breaches.StandAt(train, 2) };
        Assert.Equal("[E] HOLD: BOARD UP THE BREACH (0%)", Hud.Prompt(s));
    }

    [Fact]
    public void ThePromptSaysWhatYourHandsCanDoHere()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        PlayerState At(InteractableKind kind)
        {
            var p = PlayerMotor.SpawnInCab(train, s.PlayerTuning);
            var thing = train.Frames[0].Shape.Interactables.First(i => i.Kind == kind).Position;
            // On the cab's side of it (cab forward, note 276: the firebox is in the back wall, the cab ahead of it).
            double into = Math.Sign(train.Frames[0].Shape.Cab!.Value.Centre.Z - thing.Z);
            p.Position = thing with { Y = p.Position.Y, Z = thing.Z + 0.4 * into };
            return p;
        }
        s.Player = At(InteractableKind.Firebox);
        Assert.Equal("[E] HOLD: SHOVEL COAL (FASTER)", Hud.Prompt(s));
        s.Player = At(InteractableKind.Vent);
        Assert.Equal("[E] HOLD: VENT STEAM (SLOWER)   OR [VENT] ANYWHERE IN THE CAB", Hud.Prompt(s));
        s.Player = PlayerMotor.SpawnInCab(train, s.PlayerTuning);
        Assert.StartsWith(s.Train.BoilerTuning?.SteamDrive == true ? "[R] RELEASE BRAKE" : "[R/F] REGULATOR", Hud.Prompt(s));
        s.Player = s.Player with { Health = 0, Death = DeathCause.Cold };
        Assert.Null(Hud.Prompt(s));
    }

    /// <summary>Stood in the cab at <paramref name="at"/>, looking at <paramref name="kind"/>'s handle.</summary>
    static PlayerState LookingAt(PrototypeSession s, Double3 at, InteractableKind kind)
    {
        var thing = s.Train.Frames[0].Shape.Interactables.First(i => i.Kind == kind);
        var p = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        p.Position = at with { Y = p.Position.Y };
        var to = thing.Position + Double3.Up * thing.Aim - (p.Position + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight);
        p.Yaw = Math.Atan2(-to.X, -to.Z);
        p.Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
        return p;
    }

    [Fact]
    public void TheCabSaysTheCordTheVentAndTheBrakeAndLittleElse()
    {
        // Note 267 (the director's notes on build 1121): the whistle cord, looked at, says what it is and that it's loud;
        // the vent's one key is on the driving prompt with the brake's, and held, the prompt says it's working.
        var s = new PrototypeSession(Content, "test-loop", 4);
        // (Cab forward, note 276: the cord at the driver's end, the firebox at the fireman's; each read where it's worked.)
        var shape = s.Train.Frames[0].Shape;
        var firebox = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        var cord = shape.Interactables.First(i => i.Kind == InteractableKind.Whistle);
        s.Player = LookingAt(s, cord.Position + new Double3(-0.3, 0, 0.4), InteractableKind.Whistle);
        Assert.Equal("[E] HOLD: WHISTLE (LOUD: THE CHOIR HEARS IT)   OR [H]", Hud.Prompt(s));
        s.Player = LookingAt(s, firebox.Position + new Double3(0.35, 0, -0.45), InteractableKind.Firebox);
        Assert.Equal("[E] HOLD: SHOVEL COAL (FASTER)", Hud.Prompt(s));
        s.Player = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        Assert.Contains("[VENT] HOLD: VENT", Hud.Prompt(s));
        Assert.DoesNotContain("REVERSER", Hud.Prompt(s));
        Assert.Contains("[LEFT CTRL]", Hud.Bound(Hud.Prompt(s)!));
        s.Train.Boiler.Vented = true;
        Assert.StartsWith("VENTING STEAM: PRESSURE", Hud.Prompt(s));
        s.Train.Boiler.Vented = false;

        // The engine's panel in the cab is the speed and the levers, two lines; out of the cab, nothing (hud-look: "too much
        // UI ... not enough in world"): the driver's gauges are in the cab.
        static int TopLeft(Overlay o) => o.Vertices.Count(v => v.Position.X < 160 && v.Position.Y < 46);
        var hud = new Overlay();
        Hud.Build(hud, 480, 270, s);
        int cab = TopLeft(hud);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 1, 0, s.PlayerTuning);
        Hud.Build(hud, 480, 270, s);
        int roof = TopLeft(hud);
        Assert.True(cab > 0, "the cab has its panel");
        Assert.Equal(0, roof);
    }

    [Fact]
    public void ALockersDoorSaysWhatsInItAndTheHotbarWhatsInYourHands()
    {
        // Note 267: "there needs to be some telegraphing that there's a repair kit inside one of the lockers", and "I don't
        // seem to understand how to hold it in my inventory".
        var s = new PrototypeSession(Content, "test-loop", 4);
        var (car, bay) = DarkTerritory.Sim.World.KitLocker(s.Train)!.Value;
        var front = bay.Front;
        s.Player = new PlayerState
        {
            Parent = car,
            Surface = Surface.Deck,
            Health = 100,
            Position = new Double3(front.X + bay.Facing * 0.42, front.Y, front.Z),
            Yaw = bay.Facing * Math.PI / 2,
        };
        Assert.False(s.Train.Vehicles[car].LockerOpen(bay.Index));
        Assert.Equal("THE FITTER'S LOCKER: THE REPAIR KIT   [E] OPEN", Hud.Prompt(s));
        s.Train.Vehicles[car].ToggleLocker(bay.Index);
        Assert.Equal("[E] TAKE THE REPAIR KIT INTO YOUR HANDS   HOLD: SHUT", Hud.Prompt(s));
        var lamp = Assert.Single(Lockers.Contents(s.World.Bodies, car, s.Train.Frames[car].Shape.Lockers.First(b => b.Name == "DRIVER").Index));
        Assert.Equal(DarkTerritory.Sim.Physics.BodyKind.Lamp, lamp.Kind);
        Assert.Equal("THE LAMP", Hud.Holding(s.World, car, s.Train.Frames[car].Shape.Lockers.First(b => b.Name == "DRIVER").Index));
    }

    [Fact]
    public void TheSuppliesPanelListsWhatsAboard()
    {
        // The director's decision of 2026-10-06 (note 264): one panel, toggled on, of the supplies aboard.
        var s = new PrototypeSession(Content, "test-loop", 4);
        var rows = Hud.SuppliesLines(s.World, ((IPlaySession)s).PlayerId);
        Assert.Equal(["COAL", "REPAIR KIT", "EXTINGUISHERS", "CARGO", "STORES"], rows.Select(r => r.Item).Take(5));
        Assert.Equal("THE FITTER'S LOCKER, CAR 1", rows.Single(r => r.Item == "REPAIR KIT").Value);
        Assert.Contains("TOYS", rows.Single(r => r.Item == "STORES").Value);
        var hud = new Overlay();
        Hud.Supplies(hud, 480, 270, s);
        Assert.True(hud.Count > 0);
        Assert.Equal("I", Controls.Defaults[Control.Supplies]);
    }

    [Fact]
    public void AtASwitchStandThePromptSaysWhichWayAndHoldingThrowsIt()
    {
        // A generated night with a junction on it; the player walks up to its stand (T27).
        var route = Enumerable.Range(1, 20).Select(seed => DarkTerritory.Sim.Route.RouteGenerator.Generate(
            DarkTerritory.Sim.Route.RouteTuning.Load(Content),
            DarkTerritory.Sim.Route.RouteTier.Frontier, (ulong)seed)).First(r => r.Branches.Count > 0);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var stands = s.World.Switches!;
        var line = s.Train.Line;
        var lever = stands.LeverAt(line, 0);
        var toe = line.Sample(line.Branches[0].Toe);
        var right = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
        s.Player = PlayerMotor.SpawnOnGround(lever - Double3.Up * 0.9 + right * (line.Branches[0].Side * 0.8), line, line.Branches[0].Toe, s.PlayerTuning);
        Assert.Equal("[E] HOLD: THROW THE SWITCH TO THE DEAD LINE", Hud.Prompt(s));

        for (int i = 0; i < (stands.Tuning.ThrowSeconds + 0.2) * DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(s.Train.Diverging(0));
        Assert.Equal("[E] HOLD: THROW THE SWITCH TO THE MAIN LINE", Hud.Prompt(s));
    }

    [Fact]
    public void PulledUpByAFacilityOnASpurTheHudSaysHowMuchFits()
    {
        var route = DarkTerritory.Sim.Route.RouteGenerator.Generate(
            DarkTerritory.Sim.Route.RouteTuning.Load(Content),
            DarkTerritory.Sim.Route.RouteTier.Frontier, 7);
        var s = new PrototypeSession(Content, route, 12, enemies: false);
        int facility = Enumerable.Range(0, s.World.Run!.FacilityCount).First(i => s.World.Run.SpurOf(i) >= 0);
        var spur = s.Train.Line.Branches[s.World.Run.SpurOf(facility)];
        // Stopped on the main line short of its points, as the drill does (T28).
        var state = s.Train.Capture();
        s.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = spur.Toe - 14, Velocity = 0 }] });
        // The siding's length is generated with the yard (level-design P16): the HUD says what this one takes.
        int fit = DarkTerritory.Sim.Run.SpurDrill.Capacity(s.Train.Dynamics.Tuning.Geometry, spur, DarkTerritory.Sim.Route.RouteTuning.Load(Content).Junctions.PointsLength);
        Assert.True(fit < 12, $"the spur takes the whole train ({fit} cars)");
        Assert.Contains($"IS DOWN THE SPUR: ENGINE + {fit} CARS FIT, CUT THE REST", PrototypeSession.RouteStatus(route, s.World, s.Train));
    }

    [Fact]
    public void TheHudDrawsOverTheFrame()
    {
        GpuContext gpu;
        try { gpu = new GpuContext("tests"); }
        catch (GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
        using (gpu)
        {
            using var renderer = new GreyboxRenderer(gpu, 64, 36);
            var o = new Overlay();
            o.Rect(0, 0, 8, 8, new Vector4(1, 1, 1, 1));
            o.Rect(56, 28, 8, 8, new Vector4(1, 0, 0, 0.5f));
            var fog = new Vector3(0, 0, 0);
            var px = renderer.Render(new MeshBuilder(), Camera.LookAt(Double3.Zero, new Double3(0, 0, -1)), FrameLighting.Night, fog, o);
            (int R, int G, int B) At(int x, int y) => (px[(y * 64 + x) * 4], px[(y * 64 + x) * 4 + 1], px[(y * 64 + x) * 4 + 2]);
            Assert.Equal((255, 255, 255), At(4, 4));
            // Half-transparent red over black: half red.
            var (r, g, b) = At(60, 32);
            Assert.InRange(r, 120, 135);
            Assert.True(g < 5 && b < 5);
            // And nothing elsewhere (but the grain and the dither's step: a level or two).
            var (er, eg, eb) = At(32, 18);
            Assert.True(er <= 3 && eg <= 3 && eb <= 3, $"({er}, {eg}, {eb})");
        }
    }

    [Fact]
    public void TheRosterNamesTheCrewAndGivesNothingAway()
    {
        // GDD v1.4 open question 2: roll call is verbal. The roster is the session's crew by name and who's speaking: no
        // locations, nobody marked dead, and the Passenger aboard wearing crew 2's face isn't on it (its tell is silence).
        var s = new PrototypeSession(Content, "test-loop", 4);
        var (lines, heard) = Staging.Roster(s.Train, Content);
        Assert.Equal([1, 2, 3, 4], lines.Select(l => (int)l.Id));
        Assert.Equal(["DUNMORE", "OKAFOR", "REYES", "DAVE"], lines.Select(l => l.Name));
        Assert.All(lines, l => Assert.Equal("", l.Where));
        Assert.All(lines, l => Assert.True(l.Alive));
        Assert.Single(lines, l => l.You);
        var o = new Overlay();
        Hud.Roster(o, 480, 270, lines, heard);
        Assert.True(o.Count > 0);
    }

    [Fact]
    public void TheHudSaysHowDeepTheColdIs()
    {
        // GDD §22 deep cold (note 201): from the line's conditions, which every machine builds from the night's seed.
        var s = new PrototypeSession(Content, "test-loop", 4);
        Assert.Null(Hud.ColdLine(s.Player, s.Train, s.PlayerTuning));
        DarkTerritory.Sim.Net.HazardConditions.Apply(s.Train.Line, DarkTerritory.Sim.Net.HazardSet.Clear with { Name = "cold", ColdStep = 2 });
        Assert.Equal("BITTER COLD: OUTSIDE, IT COMES ON 1.5X FASTER", Hud.ColdLine(s.Player, s.Train, s.PlayerTuning));
        var deeper = new PrototypeSession(Content, "test-loop", 4);
        DarkTerritory.Sim.Net.HazardConditions.Apply(deeper.Train.Line, DarkTerritory.Sim.Net.HazardSet.Clear with { Name = "deep", ColdStep = 1 });
        Assert.StartsWith("DEEP COLD", Hud.ColdLine(deeper.Player, deeper.Train, deeper.PlayerTuning));
        Assert.True(BitmapFont.Default.Measure(Hud.ColdLine(s.Player, s.Train, s.PlayerTuning)!) < 480 - 12);
    }

    [Fact]
    public void TheKitInHandOffersToMendABrokenRadio()
    {
        // GDD §23 "radio breaks" (note 201): the repair kit mends it, held; how far it's got from the body record.
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        var bodies = s.World.Bodies;
        var radio = bodies.All.First(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.Radio);
        var kit = bodies.All.First(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.RepairKit);
        (radio.Carrier, radio.Broken, kit.Carrier, kit.Locker) = (1, true, 1, -1);
        Assert.Equal($"[E] HOLD: MEND YOUR RADIO WITH THE KIT ({s.TrainTuning.Kit.RadioMendSeconds:0}S)   [E] PUT DOWN", Hud.Prompt(s));
        radio.MendTicks = (int)(s.TrainTuning.Kit.RadioMendSeconds * DarkTerritory.Sim.SimConstants.TickRate / 2);
        Assert.Equal("[E] HOLD: MENDING YOUR RADIO WITH THE KIT (50%)", Hud.Prompt(s));
        radio.Broken = false;
        Assert.StartsWith("THE REPAIR KIT:", Hud.Prompt(s));
    }

    [Fact]
    public void TheBallotIsPickedThenCastAndOnlyCastOnce()
    {
        // GDD v1.4 App. D.11 "locked on submit" (note 202): a number picks, the same again (or Enter) casts; a headset's
        // stick steps and its click casts. What's sent is the option's number, till the host's ballot says it's locked.
        EnemyKind[] options = [EnemyKind.Whistler, EnemyKind.FireFlies, EnemyKind.TrackDoll];
        var picker = new BallotPicker();
        Assert.Equal(0, picker.Select((options, null)));
        picker.Cast(); // nothing picked: nothing cast
        picker.Key(4, 3); // not on the ballot
        Assert.Equal((-1, false), (picker.Pick, picker.Sent));
        picker.Key(2, 3);
        picker.Key(3, 3); // a change of mind is a pick, not a cast
        Assert.Equal((2, false), (picker.Pick, picker.Sent));
        Assert.Equal(0, picker.Select((options, null)));
        picker.Key(3, 3);
        Assert.True(picker.Sent);
        Assert.Equal(3, picker.Select((options, null)));
        Assert.Equal(3, picker.Select((options, null))); // every tick till it's locked
        picker.Key(1, 3); // sent: no taking it back
        Assert.Equal(2, picker.Pick);
        Assert.Equal(0, picker.Select((options, EnemyKind.TrackDoll)));
        Assert.Equal((2, false), (picker.Pick, picker.Sent));
        // In a headset: down from nothing is the top, up wraps to the foot, the click casts.
        var vr = new BallotPicker();
        vr.Headset(VrMenuPress.Click, 3);
        Assert.False(vr.Sent);
        vr.Headset(VrMenuPress.Down, 3);
        Assert.Equal(0, vr.Pick);
        vr.Headset(VrMenuPress.Up, 3);
        Assert.Equal(2, vr.Pick);
        vr.Headset(VrMenuPress.Down, 3);
        vr.Headset(VrMenuPress.Click, 3);
        Assert.Equal(1, vr.Select((options, null)));
        // The stick's click is a press of its own, once, and the menus don't use it.
        var keys = new VrMenuInput();
        Assert.Equal(VrMenuPress.Click, keys.Read(new Ballast.Xr.XrControllerState { Run = true }));
        Assert.Equal(VrMenuPress.None, keys.Read(new Ballast.Xr.XrControllerState { Run = true }));
    }

    [Fact]
    public void TheDeadSeeTheirBallotPickItAndSeeItLocked()
    {
        // D.11 as the dead player's screen (note 202), over a real host: the creatures with their keys and wants, the pick
        // lit, casting, then cast and locked by the host; nothing for the living.
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0);
        using var a = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        void Step(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                host.Step(default);
                a.Step(default);
                Thread.Sleep(1);
            }
        }
        Step(DarkTerritory.Sim.SimConstants.TickRate);
        Assert.Null(Hud.BallotRows(a));
        host.Host!.SetPlayerState((byte)a.PlayerId, a.Player with { Health = 0, Death = DeathCause.Mauled });
        Step(DarkTerritory.Sim.SimConstants.TickRate);
        Assert.True(a.Voting);
        Assert.Null(Hud.BallotRows(host));
        var ballot = a.Ballot!.Value.Options;
        int n = ballot.Count;
        var rows = Hud.BallotRows(a)!;
        Assert.Equal(n + 1, rows.Count);
        Assert.Equal($"[1] {DarkTerritory.Sim.Run.IncidentLog.Spoken(ballot[0].ToString()).ToUpperInvariant()}", rows[0].Text);
        Assert.Equal(DarkTerritory.Sim.Enemies.Director.WantOf(ballot[0]).ToString().ToUpperInvariant(), rows[0].Note);
        Assert.DoesNotContain(rows, r => r.Picked);
        Assert.Equal($"[1]-[{n}] PICK ONE", rows[^1].Text);
        Hud.Headset = true;
        try { Assert.Equal("[STICK UP/DOWN] PICK ONE", Hud.BallotRows(a)![^1].Text); }
        finally { Hud.Headset = false; }

        a.Picker.Key(n, n);
        rows = Hud.BallotRows(a)!;
        Assert.True(rows[n - 1].Picked);
        Assert.Equal($"[{n}] AGAIN OR [ENTER] CAST IT", rows[n].Text);
        Assert.Equal("IT'S LOCKED ONCE CAST", rows[^1].Text);
        Assert.All(rows, r => Assert.True(BitmapFont.Default.Measure(r.Text.Replace("[", "").Replace("]", "")) < 240, r.Text));
        a.Picker.Key(n, n);
        Assert.Equal("CASTING ...", Hud.BallotRows(a)![^1].Text);
        Step(DarkTerritory.Sim.SimConstants.TickRate / 2);
        Assert.Equal(ballot[n - 1], host.Host.World.Director!.VoteOf(a.PlayerId));
        Assert.False(a.Voting);
        rows = Hud.BallotRows(a)!;
        Assert.Equal("CAST, AND LOCKED", rows[^1].Text);
        Assert.True(rows[n - 1].Picked);
        // The HUD draws it (the plate, its rows) without falling over.
        var o = new Overlay();
        Hud.Build(o, 480, 270, a);
        Assert.True(o.Count > 0);
    }

    [Fact]
    public void EveryDeathSaysWhatKilledYou()
    {
        // T115 playtest: "the death screen doesn't show me anything": the v1.1 creatures' causes had no line.
        foreach (var cause in Enum.GetValues<DeathCause>().Where(c => c != DeathCause.None))
        {
            string line = Hud.DeathLine(cause);
            Assert.False(string.IsNullOrWhiteSpace(line) || line == cause.ToString().ToUpperInvariant(), $"{cause} has no line of its own");
            Assert.True(BitmapFont.Default.Measure(line) > 0);
        }
    }

    [Fact]
    public void AHitFlashesTheEdgeByItsSizeAndFades()
    {
        // GDD App. F.1 (note 272): "damage feedback is minimal: an edge flash and a sound".
        var s = new PrototypeSession(Content, "test-loop", 4);
        Assert.Equal(0, Hud.HurtStrength(s));
        s.Player = s.Player with { Health = s.Player.Health - (int)Hud.HurtFlashFullAt };
        Assert.Equal(1, Hud.HurtStrength(s), 6);
        for (int i = 0; i < Hud.HurtFlashSeconds / 2 * DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(default);
        double half = Hud.HurtStrength(s);
        Assert.InRange(half, 0.05, 0.5);
        for (int i = 0; i < Hud.HurtFlashSeconds * DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(default);
        Assert.Equal(0, Hud.HurtStrength(s));
        // A smaller hit, a smaller flash; healing none.
        s.Player = s.Player with { Health = s.Player.Health - 20 };
        double small = Hud.HurtStrength(s);
        Assert.InRange(small, Hud.HurtFlashLeast, 0.5);
        var other = new PrototypeSession(Content, "test-loop", 4) { };
        other.Player = other.Player with { Health = 40 };
        Hud.HurtStrength(other);
        other.Player = other.Player with { Health = 90 };
        Assert.Equal(0, Hud.HurtStrength(other));
        // Drawn round the rim only, and nothing at all unhurt.
        var o = new Overlay();
        Hud.EdgeFlash(o, 480, 270, 0);
        Assert.Equal(0, o.Count);
        Hud.EdgeFlash(o, 480, 270, 1);
        Assert.True(o.Count > 0);
    }

    [Fact]
    public void AHealingFindInHandSaysHoldUseWhenYoureHurt()
    {
        var route = DarkTerritory.Sim.Route.RouteGenerator.Generate(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.RouteTier.Frontier, 1);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var run = s.World.Run!;
        for (int k = 0; k < run.Stops.Count; k++)
            run.Stock(s.World.Bodies, k);
        var find = s.World.Bodies.All.First(b => run.HealOf(b) > 0);
        int heals = run.HealOf(find);
        string name = run.FindName(find)!.ToUpperInvariant();
        find.Carrier = ((IPlaySession)s).PlayerId;
        Assert.Equal($"{name} (+{heals} WHEN HURT): INTO ANY CAR TO KEEP IT   [E] PUT DOWN   [RMB] THROW", Hud.HealPrompt(s, find));
        // Hurt, out on the ballast beside the train (at nothing Use works).
        double along = s.Train.Dynamics.Distance - 20, hint = along;
        var t = s.Train.Line.Sample(along);
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 8;
        s.Player = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, s.Train.Line, ref hint) }, s.Train.Line, along, s.PlayerTuning) with { Health = 40 };
        Assert.Null(CrewActions.NearestInteractable(s.Player, s.Train, s.World.Hand));
        Assert.Equal($"{name}: [E] HOLD: USE IT (+{heals})   [E] PUT DOWN   [RMB] THROW", Hud.HealPrompt(s, find));
        Assert.Equal(Hud.HealPrompt(s, find), Hud.Prompt(s));
        find.MendTicks = (int)(DarkTerritory.Sim.SimConstants.TickRate * run.Healing!.UseSeconds / 2);
        Assert.Equal($"[E] HOLD: USING THE {name} (50%)", Hud.HealPrompt(s, find));
        // Anything else isn't medicine.
        Assert.Null(Hud.HealPrompt(s, s.World.Bodies.All.First(b => run.HealOf(b) == 0)));
    }

}
