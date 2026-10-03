using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
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
        Assert.Equal("[LMB] FIRE   [E] + WALK: PUSH IT ALONG THE RAIL", Hud.Prompt(s));
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
            p.Position = thing with { Y = p.Position.Y, Z = thing.Z + 0.4 };
            return p;
        }
        s.Player = At(InteractableKind.Firebox);
        Assert.Equal("[E] HOLD: SHOVEL COAL (FASTER)", Hud.Prompt(s));
        s.Player = At(InteractableKind.Vent);
        Assert.Equal("[E] HOLD: VENT STEAM (SLOWER)", Hud.Prompt(s));
        s.Player = PlayerMotor.SpawnInCab(train, s.PlayerTuning);
        Assert.StartsWith(s.Train.BoilerTuning?.SteamDrive == true ? "[R] RELEASE BRAKE" : "[R/F] REGULATOR", Hud.Prompt(s));
        s.Player = s.Player with { Health = 0, Death = DeathCause.Cold };
        Assert.Null(Hud.Prompt(s));
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
    public void TheRosterCountsThePassengerAsOneTooManyAndNeverHearsIt()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        var (lines, heard) = Staging.Roster(s.Train, Content);
        // You, three crew, and the thing wearing crew 2's face beside the real crew 2 (App. A.7).
        Assert.Equal(5, lines.Count);
        Assert.Equal([1, 2, 2, 3, 4], lines.Select(l => (int)l.Id));
        var twos = lines.Where(l => l.Id == 2).ToList();
        Assert.All(twos, l => Assert.Equal("CREW 2", l.Name));
        Assert.Single(twos, l => !l.Voiced);
        Assert.Single(lines, l => l.You);
        // The voice heard under id 2 is the real crewmate's: the roster never puts it on the Passenger's line.
        var o = new Overlay();
        Hud.Roster(o, 480, 270, lines, heard);
        Assert.True(o.Count > 0);
        Assert.NotNull(heard(2));
    }

    [Fact]
    public void TheReportListsTheNightsDeathsWhoWhatAndWhere()
    {
        // GDD App. D.12 "deaths, with who and where", under the report's own lines.
        var r = new RunReport(RunEnd.Derailed, 900, 12.3, 0, 4, 0, 0, 0, 0, 0, 0, 0, 3, Fatalities: new DeathRoll(
        [
            new Fatality(2, DeathCause.Struck, 3, DeathSpot.Roof, 4.2),
            new Fatality(1, DeathCause.Thrown, -1, DeathSpot.Ground, 8),
            new Fatality(3, DeathCause.Choir, 4, DeathSpot.Inside, 12.3),
            new Fatality(4, DeathCause.Derailed, 0, DeathSpot.Cab, 12.3),
        ]));
        Assert.Equal(
        [
            "CREW 2: STRUCK BY A TUNNEL'S MOUTH. ON CAR 3'S ROOF, KM 4.2",
            "YOU: THROWN OFF ON A CURVE. BY THE LINE, KM 8.0",
            "CREW 3: TAKEN BY THE CHOIR. IN CAR 4, KM 12.3",
            "CREW 4: WENT DOWN WITH THE TRAIN. IN THE CAB, KM 12.3",
        ], Hud.DeathLines(r, me: 1));
        // The headline and its line are as they were (the run-end sounds tally those, and stamp these).
        Assert.Equal(["RUN LOST", "DERAILED"], Hud.ReportLines(r));
        Assert.Empty(Hud.DeathLines(r with { Fatalities = DeathRoll.Empty }));

        // A massacre: the first few, and the rest as a count (the DEAD panel still has to fit under it).
        var many = r with { Fatalities = new DeathRoll(Enumerable.Range(1, 9).Select(i => new Fatality(i, DeathCause.Derailed, 1, DeathSpot.Aboard, 9.9))) };
        var lines = Hud.DeathLines(many);
        int shown = Hud.DeathsShown(many).Count;
        Assert.InRange(shown, 3, 8);
        Assert.Equal(shown + 1, lines.Count);
        Assert.Equal("CREW 1: WENT DOWN WITH THE TRAIN. ON CAR 1, KM 9.9", lines[0]);
        Assert.Equal($"AND {9 - shown} MORE", lines[^1]);

        // Every way to die has its words, and they fit the HUD's width with the longest who and where.
        var font = BitmapFont.Default;
        foreach (var cause in Enum.GetValues<DeathCause>().Where(c => c is not (DeathCause.None or DeathCause.Waiting)))
        {
            Assert.NotEqual("DIED", Hud.DiedOf(cause));
            var line = Hud.DeathLines(r with { Fatalities = new DeathRoll([new Fatality(12, cause, 20, DeathSpot.Coupling, 123.4)]) })[0];
            Assert.True(font.Measure(line) <= 480, $"{line}: {font.Measure(line)} px");
        }
    }
}
