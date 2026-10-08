using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
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
    public void ARackRunDryAndThePowderLockerSayWhereThePowderIs()
    {
        // Note 374: the gun's rack empty, the prompt says where its powder is; at the guard van's locker, take a charge; with
        // one in hand at the gun, hold Use to fill the rack.
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        var g = s.World.Combat!.Guns;
        int van = train.Dynamics.Consist.Vehicles[^1].Id;
        var mount = Guns.Mount(train, van)!.Value;
        s.Player = PlayerMotor.SpawnOnRoof(train, van, mount.Position.Z - mount.Facing.Z * 0.8, s.PlayerTuning);
        train.Vehicles[van].Gun.Rack = 0;
        Assert.Equal("THE RACK'S EMPTY: POWDER FROM THE GUARD VAN", Hud.Prompt(s));
        var locker = Guns.Locker(train.Frames[van].Shape)!.Value;
        s.Player = new PlayerState { Parent = van, Surface = Surface.Deck, Health = 100, Position = locker + new Double3(0.6, 0.05, 0.4) };
        Assert.Equal($"TAKE A CHARGE : [E]   {Guns.Stowed(train, g)} ROUNDS", Hud.Prompt(s));
        foreach (var v in train.Vehicles.Where(v => v.HasGun))
            v.Gun.Ammo = Guns.Ready(v.Gun, g);
        Assert.Equal("THE POWDER LOCKER'S EMPTY", Hud.Prompt(s));
    }

    [Fact]
    public void AFouledGunAndABreachedCarSayHowToPutThemRight()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var train = s.Train;
        // GDD §23: at a fouled gun, clear it by hand.
        var mount = train.Frames[0].Shape.Gun!.Value;
        s.Player = PlayerMotor.SpawnOnRoof(train, 0, mount.Position.Z - mount.Facing.Z * 0.7, s.PlayerTuning);
        Assert.Equal("SIT : [E]   PUSH ALONG : [E] + WALK", Hud.Prompt(s));
        train.Vehicles[0].Gun.Jammed = true;
        // Note 344: nothing done yet, no percentage; part done, how far.
        Assert.Equal("CLEAR THE GUN : HOLD [E]", Hud.Prompt(s));
        train.Vehicles[0].Gun.ReloadProgress = s.World.Combat!.Guns.ClearSeconds / 4;
        Assert.Equal("CLEAR THE GUN : HOLD [E] (25%)", Hud.Prompt(s));
        // Decided 1 Oct: in a breached car, board up the hole; at it, hold Use.
        var room = train.Frames[2].Shape.Interior!.Value;
        train.Vehicles[2].Breach(Breaches.EndWall(train.Frames[2].Shape)!.Value);
        s.Player = new PlayerState { Parent = 2, Surface = Surface.Deck, Health = 100, Position = new Double3(-0.45, room.Min.Y, room.Min.Z + 1), Kit = s.PlayerTuning.StartingKit };
        Assert.Equal("THE CAR'S BREACHED", Hud.Prompt(s));
        // At it with the crowbar in hand: the key that puts the wrench in hand (note 301); with the wrench, hold Use.
        s.Player = s.Player with { Position = Breaches.StandAt(train, 2) };
        Assert.Equal("THE CAR'S BREACHED   WRENCH : [2]", Hud.Prompt(s));
        s.Player = s.Player with { HeldSlot = 1 };
        Assert.Equal("BOARD IT UP : HOLD [E]", Hud.Prompt(s));
        // A battered car: mended at its dent (note 301).
        train.Vehicles[3].Integrity = 0.5;
        var dent = Repairs.DentAt(train.Frames[3].Shape)!.Value;
        s.Player = s.Player with { Parent = 3, Position = new Double3(-0.45, room.Min.Y, dent.Z) };
        Assert.Equal("MEND THE CAR : HOLD [E] (50%)", Hud.Prompt(s));
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
            // On the cab's side of it (note 280: the firebox at the front, the cab behind it).
            double into = Math.Sign(train.Frames[0].Shape.Cab!.Value.Centre.Z - thing.Z);
            p.Position = thing with { Y = p.Position.Y, Z = thing.Z + 0.4 * into };
            return p;
        }
        s.Player = At(InteractableKind.Firebox);
        Assert.Equal("SHOVEL COAL : HOLD [E]", Hud.Prompt(s));
        s.Player = At(InteractableKind.Vent);
        Assert.Equal("VENT STEAM : HOLD [E]", Hud.Prompt(s));
        // At the controls looking at nothing (note 285): nothing at the crosshair; driving them is the corner's.
        s.Player = PlayerMotor.SpawnInCab(train, s.PlayerTuning);
        Assert.Null(Hud.Prompt(s));
        Assert.Equal(s.Train.BoilerTuning?.SteamDrive == true ? "RELEASE BRAKE : [R]" : "REGULATOR UP : [R]", Hud.Hints(s).Lines[0]);
        s.Player = s.Player with { Health = 0, Death = DeathCause.Cold };
        Assert.Null(Hud.Prompt(s));
        Assert.Null(Hud.Hints(s).Head);
        Assert.Empty(Hud.Hints(s).Lines);
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
        // Note 267 (the director's notes on build 1121): the whistle cord, looked at, says to pull it (note 285: "PULL CORD :
        // [E]", and not that it's loud: that's learned); the vent's one key is in the corner with the brake's, and held, the
        // prompt says it's venting.
        var s = new PrototypeSession(Content, "test-loop", 4);
        // (Note 280: the cord in the driver's front corner, the firebox at the front beside it; each read where it's worked.)
        var shape = s.Train.Frames[0].Shape;
        var firebox = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        var cord = shape.Interactables.First(i => i.Kind == InteractableKind.Whistle);
        s.Player = LookingAt(s, cord.Position + new Double3(-0.3, 0, 0.4), InteractableKind.Whistle);
        Assert.Equal("PULL CORD : [E]", Hud.Prompt(s));
        s.Player = LookingAt(s, firebox.Position + new Double3(0.15, 0, 0.45), InteractableKind.Firebox);
        Assert.Equal("SHOVEL COAL : HOLD [E]", Hud.Prompt(s));
        s.Player = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        var (head, lines) = Hud.Hints(s);
        Assert.Contains("VENT : HOLD [VENT]", lines);
        // Forward goes without saying; the reverser's said only when it's back.
        Assert.DoesNotContain(lines, l => l.Contains("REVERSE"));
        s.Controls.Reverser = -1;
        Assert.Contains("IN REVERSE : [X]", Hud.Hints(s).Lines);
        s.Controls.Reverser = 1;
        Assert.Contains("[LEFT CTRL]", Hud.Bound(string.Join("   ", lines)));
        // The corner's head is the speed, for the driver to read against the boards.
        Assert.Equal($"{Math.Abs(s.Train.Dynamics.Speed) * 3.6:0} KM/H", head?.Split("  ")[0]);
        s.Train.Boiler.Vented = true;
        Assert.Equal("VENTING", Hud.Prompt(s));
        s.Train.Boiler.Vented = false;

        // The corner is the cab's alone (hud-look: "too much UI ... not enough in world"): on a roof with empty hands, it
        // says nothing, and nothing's drawn at the bottom right.
        static int BottomRight(Overlay o) => o.Vertices.Count(v => v.Position.X > 360 && v.Position.Y > 200);
        var hud = new Overlay();
        Hud.Build(hud, 480, 270, s);
        int cab = BottomRight(hud);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 1, 0, s.PlayerTuning);
        Assert.Null(Hud.Hints(s).Head);
        Assert.Empty(Hud.Hints(s).Lines);
        Hud.Build(hud, 480, 270, s);
        int roof = BottomRight(hud);
        Assert.True(cab > 0, "the cab's controls are in the corner");
        Assert.Equal(0, roof);
    }

    /// <summary>
    /// The director, 7 Oct (note 285): prompts are short, Lethal Company's "PULL CORD : [E]", and never foretell what an
    /// action does: "consequences need to be learned". Every key is the action's (after " : "), and nothing says loud, quiet,
    /// faster, slower, what mends what, or who hears.
    /// </summary>
    static void AssertShort(string? prompt) => AssertForm(prompt, 48);

    /// <summary>Note 285's form: every key an action's (or a choice's) and nothing foretold; at most <paramref name="longest"/> characters.</summary>
    static void AssertForm(string? prompt, int longest)
    {
        if (prompt is null)
            return;
        foreach (System.Text.RegularExpressions.Match key in System.Text.RegularExpressions.Regex.Matches(prompt, @"\["))
        {
            string before = prompt[..key.Index];
            Assert.True(before.EndsWith(" : ", StringComparison.Ordinal) || before.EndsWith(" : HOLD ", StringComparison.Ordinal)
                || before.EndsWith(" OR ", StringComparison.Ordinal) || before.EndsWith("LET GO OF ", StringComparison.Ordinal), $"a key that isn't an action's: {prompt}");
        }
        foreach (string foretold in new[] { "LOUD", "QUIET", "SILENT", "FASTER", "SLOWER", "NEEDS", "MENDS", "HEARS", "KEEP AT IT", "GET CLEAR", "BRING", "WHEN THE" })
            Assert.DoesNotContain(foretold, prompt);
        Assert.True(prompt.Length <= longest, $"too long ({prompt.Length}): {prompt}");
    }

    [Fact]
    public void TheCommendationPickerAndTheSkipSayTheActionAndItsKey()
    {
        // Note 369: the last two lines that put the key first, in note 285's form.
        var pick = ("ADA", "CAME BACK FOR ME", false);
        Assert.Equal("ADA : [LEFT/RIGHT]   CAME BACK FOR ME : [UP/DOWN]   COMMEND : [SPACE]", Hud.CommendLine(pick, headset: false));
        Assert.Equal("ADA : [STICK LEFT/RIGHT]   CAME BACK FOR ME : [STICK UP/DOWN]   COMMEND : [CLICK STICK]", Hud.CommendLine(pick, headset: true));
        Assert.Equal("YOU COMMENDED ADA: CAME BACK FOR ME", Hud.CommendLine(pick with { Item3 = true }, headset: false));
        foreach (bool headset in new[] { false, true })
            AssertForm(Hud.CommendLine(pick, headset), 96);
        // Along the foot of the report in fine print, at every TEXT SIZE's canvas (480, 384, 320 wide) in a 720p and a 1080p
        // window, and on a headset's panel (480 wide at two pixels, whatever the TEXT SIZE): every line inside the frame, a
        // name with the most letters a name can have (Messages.NameLength) cut short or on two lines, given or not.
        var o = new Overlay();
        var bart = ("BARTHOLOMEW FENWICKE", "LAST ONE STANDING", false);
        Assert.Equal(DarkTerritory.Sim.Net.Messages.NameLength, bart.Item1.Length);
        var screens = new[] { 480, 384, 320 }.SelectMany(c => new[] { (c, 1280f / c, false), (c, 1920f / c, false) }).Append((480, 2f, true));
        foreach (var (canvas, pixels, headset) in screens)
            foreach (var who in new[] { pick, bart, bart with { Item3 = true } })
            {
                float fine = Hud.PromptScaleAt(pixels);
                var lines = Hud.CommendLines(o, who, headset, canvas, fine);
                Assert.InRange(lines.Count, 1, 2);
                foreach (string line in lines)
                {
                    Assert.True(UiStyle.MeasureKeyed(o, line, fine) <= canvas - 12, $"{canvas} wide at {fine}: {line}");
                    AssertForm(line, 128);
                }
            }
        // A short name whole on one line at 1080p, and at 720p; the longest cut short at 720p, on one line at 100% (the
        // report's room kept) and over COMMEND at 125%, where no name would go on one.
        Assert.Equal([Hud.CommendLine(pick, false)], Hud.CommendLines(o, pick, false, 480, Hud.PromptScaleAt(4)));
        Assert.Equal([Hud.CommendLine(pick, false)], Hud.CommendLines(o, pick, false, 480, Hud.PromptScaleAt(1280f / 480)));
        Assert.Equal(["BARTHOLOMEW. : [LEFT/RIGHT]   LAST ONE STANDING : [UP/DOWN]   COMMEND : [SPACE]"],
            Hud.CommendLines(o, bart, false, 480, Hud.PromptScaleAt(1280f / 480)));
        var narrow = Hud.CommendLines(o, bart, false, 384, Hud.PromptScaleAt(1280f / 384));
        Assert.Equal(2, narrow.Count);
        Assert.StartsWith("BARTHOLOMEW", narrow[0]);
        Assert.Equal("COMMEND : [SPACE]", narrow[1]);

        var s = new PrototypeSession(Content, "test-loop", 4);
        Assert.Equal("SKIP : HOLD [SPACE]", Hud.SkipLine(s));
        AssertShort(Hud.SkipLine(s));
        // Under the crew's vote (wreck.json skip.own false), the votes so far.
        s.World.WreckTuning = s.World.WreckTuning with { Skip = s.World.WreckTuning.Skip with { Own = false } };
        s.World.FilmVotes = (2, 5);
        Assert.Equal("SKIP : HOLD [SPACE]   2/5", Hud.SkipLine(s));
        AssertShort(Hud.SkipLine(s));
    }

    [Fact]
    public void PromptsNameTheActionAndItsKeyAndNeverWhatItDoes()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        // Every control in the cab, looked at from where it's worked.
        foreach (var thing in s.Train.Frames[0].Shape.Interactables.DistinctBy(i => i.Kind))
        {
            s.Player = LookingAt(s, thing.Position + new Double3(0.35, 0, 0.45), thing.Kind);
            AssertShort(Hud.Prompt(s));
            foreach (string line in Hud.Hints(s).Lines)
                AssertShort(line);
        }
        s.Train.Boiler.Vented = true;
        AssertShort(Hud.Prompt(s));
        s.Train.Boiler.Vented = false;
        // The kit's locker, shut and open, and the kit carried.
        var (car, bay) = DarkTerritory.Sim.World.KitLocker(s.Train)!.Value;
        s.Player = new PlayerState
        {
            Parent = car,
            Surface = Surface.Deck,
            Health = 100,
            Position = new Double3(bay.Front.X + bay.Facing * 0.42, bay.Front.Y, bay.Front.Z),
            Yaw = bay.Facing * Math.PI / 2,
        };
        AssertShort(Hud.Prompt(s));
        s.Train.Vehicles[car].ToggleLocker(bay.Index);
        AssertShort(Hud.Prompt(s));
        // The wrench in hand (note 301: the kit's gone).
        s.Player = s.Player with { Kit = s.PlayerTuning.StartingKit, HeldSlot = 1 };
        AssertShort(Hud.Prompt(s));
        Assert.All(Hud.Hints(s).Lines, AssertShort);
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
        // Note 301: the kit's gone, so locker 8 stands empty.
        Assert.Equal("LOCKER 8: EMPTY   OPEN : [E]", Hud.Prompt(s));
        var lamp = Assert.Single(Lockers.Contents(s.World.Bodies, car, s.Train.Frames[car].Shape.Lockers.First(b => b.Name == "1").Index));
        Assert.Equal(DarkTerritory.Sim.Physics.BodyKind.Lamp, lamp.Kind);
        Assert.Equal("THE LAMP", Hud.Holding(s.World, car, s.Train.Frames[car].Shape.Lockers.First(b => b.Name == "1").Index));
    }

    [Fact]
    public void TheSuppliesPanelListsWhatsAboard()
    {
        // The director's decision of 2026-10-06 (note 264): one panel, toggled on, of the supplies aboard.
        var s = new PrototypeSession(Content, "test-loop", 4);
        var rows = Hud.SuppliesLines(s.World, ((IPlaySession)s).PlayerId);
        // Note 301: no repair kit row; the wrench everyone carries is the repair tool.
        Assert.Equal(["COAL", "EXTINGUISHERS", "CARGO", "STORES"], rows.Select(r => r.Item).Take(4));
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
        Assert.Equal("THROW TO THE DEAD LINE : HOLD [E]", Hud.Prompt(s));

        for (int i = 0; i < (stands.Tuning.ThrowSeconds + 0.2) * DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(s.Train.Diverging(0));
        Assert.Equal("THROW TO THE MAIN LINE : HOLD [E]", Hud.Prompt(s));
    }

    [Fact]
    public void AtAStandThePromptIsTheLeversWhateversInYourHandsOrLyingByIt()
    {
        // Queue #94 (note 357): Use is the lever's at a stand, so the prompt was wrong to offer the crate lying by it, and
        // to say nothing while a lamp was in hand.
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
        int me = ((IPlaySession)s).PlayerId;
        var feet = PlayerMotor.WorldPosition(s.Player, s.Train);

        var crate = s.World.Bodies.SpawnCargo(feet + new Double3(0, 0, -0.6), line.Branches[0].Toe);
        Assert.Same(crate, s.World.Bodies.InReach(s.Player, s.Train));
        Assert.Equal("THROW TO THE DEAD LINE : HOLD [E]", Hud.Prompt(s));

        crate.Pbd.Particles[0].Position = feet + right * 40;
        var lamp = s.World.Bodies.SpawnItem(feet, line.Branches[0].Toe, BodyKind.Lamp);
        lamp.Carrier = me;
        Assert.Equal("THROW TO THE DEAD LINE : HOLD [E]", Hud.Prompt(s));
        for (int i = 0; i < (stands.Tuning.ThrowSeconds + 0.2) * DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.True(s.Train.Diverging(0));
        Assert.Equal(me, lamp.Carrier);
    }

    [Fact]
    public void AtTheCranesControlsTheCornerNamesTheHookBothWaysAndTheBrakeKeyLowersIt()
    {
        // The director, 8 Oct: "Crane hooks only go up with space, no obvious way for them to go down". The brake key lowers
        // the hook (Crane.Drive); the corner said only "HOOK : [SPACE]", and the solo session's brake key never reached the
        // crane (the app took it off to the cab alone).
        var (route, facility) = DarkTerritory.Sim.Bots.FacilityWork.Find(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.FacilityKind.Foundry)!.Value;
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var site = s.World.Run!.Sites[facility]!;
        var crane = site.Crane!;
        // Stood down the foundry's spur, the engine up at the buffer stop: the stop the crane works at.
        var spur = s.Train.Line.Branches[site.Spur];
        var state = s.Train.Capture();
        s.Train.Restore(state with { Rakes = [state.Rakes[0] with { Path = site.Spur, Distance = spur.End - 0.5, Velocity = 0 }] });
        s.Train.RefreshFrames();
        s.Player = PlayerMotor.SpawnOnGround(crane.Controls, s.Train.Line, site.MainDistance, s.PlayerTuning);
        for (int i = 0; i < 10; i++)
            s.Step(default);
        Assert.Same(site, s.World.Run.CurrentSite);
        // Not a hold (the director, 8 Oct): Use's press at the stand (sent as the seat's) takes the controls.
        Assert.Equal("THE CRANE : [E]", Hud.Prompt(s));
        s.Step(new PlayerIntent { Actions = PlayerActions.Seat });
        Assert.True(s.Player.Has(PlayerFlags.Operating));
        var use = new PlayerIntent();
        var lines = Hud.Hints(s).Lines;
        Assert.Contains("HOOK UP : [SPACE]", lines);
        Assert.Contains("HOOK DOWN : [B]", lines);
        Assert.Contains("STEP DOWN : [E]", lines);
        // Held, the brake key brings the hook down, as the app now sends it in a solo session too.
        double hook = crane.Hook;
        for (int i = 0; i < DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(use with { Buttons = PlayerButtons.Use | PlayerButtons.Brake });
        Assert.True(crane.Hook < hook - 0.5, $"the hook went from {hook:0.00} to {crane.Hook:0.00}");
        hook = crane.Hook;
        for (int i = 0; i < DarkTerritory.Sim.SimConstants.TickRate; i++)
            s.Step(use with { Buttons = PlayerButtons.Use | PlayerButtons.Jump });
        Assert.True(crane.Hook > hook + 0.5, $"the hook went from {hook:0.00} to {crane.Hook:0.00}");
        // Pressed again, the controls are let go.
        s.Step(new PlayerIntent { Actions = PlayerActions.Seat });
        Assert.False(s.Player.Has(PlayerFlags.Operating));
    }

    [Fact]
    public void AtTheSteamLiftsLeverThePromptSaysTheCarUnderTheChuteAndTheCabSaysWhereTheSteamsGoing()
    {
        // Queue #105 (note 368): the lever, the car under the chute, the skip winding while the engine vents into it.
        var (route, facility) = DarkTerritory.Sim.Bots.FacilityWork.Find(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.FacilityKind.MineHead)!.Value;
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var site = s.World.Run!.Sites[facility]!;
        var spur = s.Train.Line.Branches[site.Spur];
        var consist = s.Train.Dynamics.Consist;
        var state = s.Train.Capture();
        s.Train.Restore(state with { Rakes = [state.Rakes[0] with { Path = site.Spur, Distance = spur.Toe + site.LiftAlong + consist.OffsetOf(1) + consist.Vehicles[1].Length(s.TrainTuning) / 2, Velocity = 0 }] });
        s.Train.RefreshFrames();
        consist.Vehicles[1].Load = 0.25;
        s.Player = PlayerMotor.SpawnOnGround(site.LiftLever - Double3.Up * 0.9, s.Train.Line, site.MainDistance, s.PlayerTuning);
        Assert.Equal("LIFT : HOLD [E]   CAR 25% FULL", Hud.Prompt(s));
        site.Mirror(site.State with { Winding = true });
        Assert.Equal("WINDING   CAR 25% FULL", Hud.Prompt(s));
        // In the cab with the vent open, the steam's said to be going to the lift, not into the air.
        s.World.Run.Mirror(DarkTerritory.Sim.Run.RunPhase.AtFacility, DarkTerritory.Sim.Run.RunEnd.None, 900, facility, false,
            [.. Enumerable.Repeat(0.0, s.World.Run.FacilityCount)], [.. s.World.Run.Sites.Select(x => x?.State ?? default)]);
        s.Player = PlayerMotor.SpawnInCab(s.Train, s.PlayerTuning);
        s.Train.Boiler.Vented = true;
        Assert.Equal("STEAM TO THE LIFT", Hud.Prompt(s));
    }

    [Fact]
    public void AtTheConveyorThePromptsSayStartItClearTheJamAndWhereTheJamIs()
    {
        // Queue #136 (note 400): the drive house's starter, a jam beside you, and along the belt where the jam is or that it's
        // stalled (spec D.3: "someone has to roam").
        var (route, facility) = DarkTerritory.Sim.Bots.FacilityWork.Find(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.FacilityKind.GrainElevator)!.Value;
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var site = s.World.Run!.Sites[facility]!;
        var c = s.World.Run.FacilityTuning!.Conveyor;
        PlayerState At(Double3 p) => PlayerMotor.SpawnOnGround(p, s.Train.Line, site.MainDistance, s.PlayerTuning);
        s.Player = At(site.ConveyorStarter - Double3.Up * 0.9);
        Assert.Equal("START THE BELT : HOLD [E]", Hud.Prompt(s));
        site.Mirror(site.State with { Start = c.StartSeconds / 2 });
        Assert.Equal("STARTING THE BELT (50%)", Hud.Prompt(s));
        // Running and jammed halfway along its low run: beside the jam, clearing it.
        site.Mirror(site.State with { Start = 0, Running = true, Jam = 0.5 });
        var toTrack = ((site.ConveyorKnee - site.ConveyorTail) with { Y = 0 }).Normalized;
        var across = new Double3(-toTrack.Z, 0, toTrack.X);
        s.Player = At(site.JamAt - Double3.Up * c.BeltHeight + across * 0.6);
        Assert.Equal("BELT JAMMED : HOLD [E]", Hud.Prompt(s));
        site.Mirror(site.State with { Clear = c.ClearSeconds / 2 });
        Assert.Equal("CLEARING THE JAM (50%)", Hud.Prompt(s));
        // Down at the knee, out of reach of it: how far off it is.
        s.Player = At(site.ConveyorKnee - Double3.Up * c.BeltHeight);
        double away = ((site.JamAt - PlayerMotor.WorldPosition(s.Player, s.Train)) with { Y = 0 }).Length;
        Assert.Equal($"THE BELT'S JAMMED, {away:0} M AWAY", Hud.Prompt(s));
        // Stalled with grain still to carry: back to the drive house.
        site.Mirror(site.State with { Running = false, Jam = -1, Clear = 0, Grain = c.Grain - 0.5 });
        Assert.Equal("THE BELT'S STALLED : START IT AT THE DRIVE HOUSE", Hud.Prompt(s));
    }

    [Fact]
    public void AtTheTippleThePromptsSayHowTrueTheCarStandsAndPutItBackOnItsRails()
    {
        // Queue #159 (note 423): at the lever, how far the car in the cradle stands off its mark before the clamp (spec D.3: "a
        // sloppy clamp costs you"), the roll once it's clamped; beside a car off its rails, the wrench that puts it back.
        var (route, facility) = DarkTerritory.Sim.Bots.FacilityWork.Find(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.FacilityKind.MineHead,
            // Its whole list: the night's draw (note 449) may have left the tipple out of this one.
            modules: DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)).ModulesOf(DarkTerritory.Sim.Route.FacilityKind.MineHead))!.Value;
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var site = s.World.Run!.Sites[facility]!;
        var t = s.World.Run.FacilityTuning!.Tipple;
        PlayerState At(Double3 p) => PlayerMotor.SpawnOnGround(p, s.Train.Line, site.MainDistance, s.PlayerTuning);
        s.Player = At(site.TippleLever - Double3.Up * 0.9);
        Assert.Equal("NO CAR IN THE CRADLE", Hud.Prompt(s));
        // A cargo car stood in the cradle, a little off its mark and then further.
        var car = s.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var spur = s.Train.Line.Branches[site.Spur];
        void Stand(double off)
        {
            var consist = s.Train.Dynamics.Consist;
            int i = consist.IndexOf(car.Id);
            var state = s.Train.Capture();
            s.Train.Restore(state with { Rakes = [state.Rakes[0] with { Path = spur.Index, Distance = spur.Toe + site.TippleAlong + consist.OffsetOf(i) + car.Length(consist.Tuning) / 2 + off, Velocity = 0 }, .. state.Rakes.Skip(1)] });
            s.Train.RefreshFrames();
        }
        car.Load = 0;
        Stand(0);
        Assert.Equal("CLAMP : HOLD [E]   CAR STOOD TRUE", Hud.Prompt(s));
        Stand(1.0);
        Assert.Equal("CLAMP : HOLD [E]   CAR 1.0 M OFF ITS MARK", Hud.Prompt(s));
        Stand(0);
        site.Mirror(site.State with { Clamp = t.ClampSeconds / 2 });
        Assert.Equal("CLAMP : HOLD [E] (50%)   CAR STOOD TRUE", Hud.Prompt(s));
        site.Mirror(site.State with { Clamp = 0, Clamped = car.Id, GoodClamp = true, Roll = 0.25 });
        Assert.Equal("TIP IT : HOLD [E] (25%)", Hud.Prompt(s));
        site.Mirror(site.State with { RollingBack = true });
        Assert.Equal("ROLLING BACK", Hud.Prompt(s));
        // Off its rails: beside it, the wrench's key; with it in hand, the hold and how far it's got.
        site.Mirror(site.State with { Clamped = -1, Roll = 0, RollingBack = false, Rerail = t.RerailSeconds / 4 });
        car.OffRails = true;
        Assert.Equal("THE CAR'S OFF ITS RAILS : WRENCH IT BACK ON", Hud.Prompt(s));
        var across = ((site.TippleBin - site.Cradle) with { Y = 0 }).Normalized;
        s.Player = At(s.Train.Frames[car.Id].Origin with { Y = site.Cradle.Y } + across * 1.6);
        Assert.Equal($"THE CAR'S OFF ITS RAILS   WRENCH : [{Repairs.WrenchKey(s.Player)}]", Hud.Prompt(s));
        s.Player = s.Player with { HeldSlot = (byte)(Repairs.WrenchKey(s.Player) - 1) };
        Assert.Equal("PUT IT BACK ON ITS RAILS : HOLD [E] (25%)", Hud.Prompt(s));
    }

    [Fact]
    public void AtAnAlternatesStandThePromptNamesTheRouteCardsLineNotADeadLine()
    {
        // Note 289: on the line generator's nights (the ones the game plays) every branch that wasn't a spur was "the dead
        // line", so the crew choosing the route card's high line at its junction was told it was throwing the train away.
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(Content, "frontier:7", 4);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var line = s.Train.Line;
        var alt = line.Branches.First(b => b.Kind == DarkTerritory.Sim.Rail.BranchKind.Alternate);
        var lever = s.World.Switches!.LeverAt(line, alt.Index);
        var toe = line.Sample(alt.Toe);
        var right = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
        s.Player = PlayerMotor.SpawnOnGround(lever - Double3.Up * 0.9 + right * (alt.Side * 0.8), line, alt.Toe, s.PlayerTuning);
        string name = route.Plan!.RouteCard.KnownGrades.Single(k => k.Route.EndsWith(")", StringComparison.Ordinal) && k.Route != "main line").Route;
        name = name[..name.LastIndexOf(" (", StringComparison.Ordinal)].ToUpperInvariant();
        Assert.Equal($"THROW TO THE {name} : HOLD [E]", Hud.Prompt(s));
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
        Assert.Null(Hud.ColdLine(s.Player, s.Train));
        DarkTerritory.Sim.Net.HazardConditions.Apply(s.Train.Line, DarkTerritory.Sim.Net.HazardSet.Clear with { Name = "cold", ColdStep = 2 });
        // Its name only (note 369): how much faster it comes on outside is learned out in it.
        Assert.Equal("BITTER COLD", Hud.ColdLine(s.Player, s.Train));
        AssertShort(Hud.ColdLine(s.Player, s.Train));
        var deeper = new PrototypeSession(Content, "test-loop", 4);
        DarkTerritory.Sim.Net.HazardConditions.Apply(deeper.Train.Line, DarkTerritory.Sim.Net.HazardSet.Clear with { Name = "deep", ColdStep = 1 });
        Assert.Equal("DEEP COLD", Hud.ColdLine(deeper.Player, deeper.Train));
    }

    [Fact]
    public void TheWrenchInHandOffersToMendABrokenRadio()
    {
        // GDD §23 "radio breaks" (note 201): mended, held; how far it's got from the body record. Note 301: with the wrench in
        // hand (the kit's gone), and there's nothing to put down.
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        var bodies = s.World.Bodies;
        var radio = bodies.All.First(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.Radio);
        (radio.Carrier, radio.Broken) = (1, true);
        Assert.Null(Hud.Prompt(s)); // the crowbar in hand
        s.Player = s.Player with { HeldSlot = 1 };
        Assert.Equal("MEND YOUR RADIO : HOLD [E]", Hud.Prompt(s));
        radio.MendTicks = (int)(s.TrainTuning.Kit.RadioMendSeconds * DarkTerritory.Sim.SimConstants.TickRate / 2);
        Assert.Equal("MENDING YOUR RADIO (50%)", Hud.Prompt(s));
        radio.Broken = false;
        Assert.Null(Hud.Prompt(s));
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
    public void ACrateCarriedIntoAFullCarSaysTheCarsFull()
    {
        // Note 324: a crate put down in a full car never loads (Run: Load < 1), which stops you; said as a short state in the
        // corner, the car by its stencilled number. Room to spare, and nothing's said.
        var route = DarkTerritory.Sim.Route.RouteGenerator.Generate(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.RouteTier.Frontier, 1);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var run = s.World.Run!;
        for (int k = 0; k < run.Stops.Count; k++)
            run.Stock(s.World.Bodies, k, searched: true);
        var crate = s.World.Bodies.All.First(b => b.Kind == DarkTerritory.Sim.Physics.BodyKind.Cargo);
        crate.Carrier = ((IPlaySession)s).PlayerId;
        int car = Enumerable.Range(1, s.Train.Vehicles.Count - 1).First(i => s.Train.Vehicles[i].Kind == VehicleKind.Cargo);
        var room = s.Train.Frames[car].Shape.Interior!.Value;
        var middle = (room.Min + room.Max) * 0.5;
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, car, middle.Z, s.PlayerTuning) with { Position = middle with { Y = room.Min.Y + 0.1 }, Surface = Surface.Deck };
        Assert.True(PlayerMotor.Indoors(s.Player, s.Train));
        s.Train.Vehicles[car].Load = 0.5;
        Assert.DoesNotContain(Hud.Hints(s).Lines, l => l.Contains("FULL"));
        s.Train.Vehicles[car].Load = 1;
        Assert.Equal($"CAR {car} IS FULL", Hud.Hints(s).Lines[0]);
        // Empty-handed, or carrying what isn't freight, a full car is nothing to say.
        crate.Carrier = -1;
        Assert.DoesNotContain(Hud.Hints(s).Lines, l => l.Contains("FULL"));
    }

    [Fact]
    public void AHealingFindInHandSaysHoldUseWhenYoureHurt()
    {
        // Note 272 in note 285's form: the find's name and keys are the corner's, "USE : HOLD [E]" only when hurt; what it
        // gives back isn't said (learned). At the crosshair, only the use under way.
        var route = DarkTerritory.Sim.Route.RouteGenerator.Generate(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.RouteTier.Frontier, 1);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var run = s.World.Run!;
        for (int k = 0; k < run.Stops.Count; k++)
            run.Stock(s.World.Bodies, k, searched: true);
        var find = s.World.Bodies.All.First(b => run.HealOf(b) > 0);
        find.Carrier = ((IPlaySession)s).PlayerId;
        // Whole: a find like any other.
        Assert.False(Hud.CanHeal(s, find));
        Assert.Null(Hud.Prompt(s));
        Assert.Equal(["PUT DOWN : [E]", "THROW : [RMB]"], Hud.Hints(s).Lines);
        // Hurt, out on the ballast beside the train (at nothing Use works).
        double along = s.Train.Dynamics.Distance - 20, hint = along;
        var t = s.Train.Line.Sample(along);
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 8;
        s.Player = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, s.Train.Line, ref hint) }, s.Train.Line, along, s.PlayerTuning) with { Health = 40 };
        Assert.Null(CrewActions.NearestInteractable(s.Player, s.Train, s.World.Hand));
        Assert.True(Hud.CanHeal(s, find));
        Assert.Equal(["USE : HOLD [E]", "PUT DOWN : [E]", "THROW : [RMB]"], Hud.Hints(s).Lines);
        Assert.Null(Hud.Prompt(s));
        find.MendTicks = (int)(DarkTerritory.Sim.SimConstants.TickRate * run.Healing!.UseSeconds / 2);
        Assert.Equal("USING IT (50%)", Hud.Prompt(s));
        Assert.DoesNotContain(Hud.Hints(s).Lines.Concat([Hud.Prompt(s)!]), l => l.Contains('+') || l.Contains("WHEN HURT"));
        // Anything else isn't medicine.
        Assert.Null(Hud.HealPrompt(s, s.World.Bodies.All.First(b => run.HealOf(b) == 0)));
    }

    [Fact]
    public void AnOpenHousesCupboardSaysSearchItAndHowFarThrough()
    {
        // Note 326: at a hiding spot empty-handed, the prompt is the search; under way, how far through; searched, nothing.
        var route = DarkTerritory.Sim.Route.RouteGenerator.Generate(DarkTerritory.Sim.Route.RouteTuning.Load(Content), DarkTerritory.Sim.Route.RouteTier.Frontier, 1);
        var s = new PrototypeSession(Content, route, 4, enemies: false);
        var run = s.World.Run!;
        var spot = run.HidingSpots[0];
        run.Stock(s.World.Bodies, spot.Stop);
        double hint = run.Stops[spot.Stop].Start;
        s.Player = PlayerMotor.SpawnOnGround(spot.At with { Y = PlayerMotor.GroundAt(spot.At, s.Train.Line, ref hint) }, s.Train.Line, hint, s.PlayerTuning);
        string name = spot.Container.Kind switch
        {
            DarkTerritory.Sim.Stops.ContainerKind.Cupboard => "CUPBOARD",
            DarkTerritory.Sim.Stops.ContainerKind.Cabinet => "CABINET",
            DarkTerritory.Sim.Stops.ContainerKind.Cellar => "CELLAR",
            _ => "LOOSE BOARDS",
        };
        Assert.Equal($"SEARCH THE {name} : HOLD [E]", Hud.Prompt(s));
        var use = new DarkTerritory.Sim.Player.PlayerIntent { Buttons = DarkTerritory.Sim.Player.PlayerButtons.Use };
        var me = s.Player;
        for (int i = 0; i < Math.Round(spot.Seconds / 2 * DarkTerritory.Sim.SimConstants.TickRate); i++)
        {
            s.World.BeginTick();
            s.World.CrewAct(ref me, use, ((IPlaySession)s).PlayerId);
        }
        s.Player = me;
        Assert.Equal($"SEARCHING THE {name} (50%)", Hud.Prompt(s));
        // Something in hand: not a search.
        var toy = s.World.Bodies.SpawnItem(spot.At, hint, DarkTerritory.Sim.Physics.BodyKind.Toy);
        toy.Carrier = ((IPlaySession)s).PlayerId;
        Assert.DoesNotContain("SEARCH", Hud.Prompt(s) ?? "");
    }
}
