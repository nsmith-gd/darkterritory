using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.14 "dead silence": Call Out and the Live Mic never feed the crew loudness meter, the Gaunt's silence check
/// (what the host keeps of who's been talking) or the director's state. Two nights, the same in every way but that one
/// has its dead calling out and opening the mic the whole time: they must come out the same. And the breach, which is
/// the living's, does count (D.7).
/// </summary>
[Collection(nameof(LineGenTests))]
public class DeadSilenceTests
{
    static HoldoutTests.Night Night(out Holdout h)
    {
        var site = Routes().Plan!.Holdouts.First(x => x.SiteKind == HoldoutSiteKind.Facility && !x.Second);
        var n = new HoldoutTests.Night(front: site.Zone.S0 + 150, enemies: true);
        n.DeadAtTheFortress(2);
        n.DeadAtTheFortress(3);
        n.Step(0.2);
        h = n.Holdouts.Of(site.Id)!;
        n.AtTheDoor(1, h, null);
        return n;
    }

    static Route.Route Routes() => LineGen.Routes.Generate(Ballast.DataFile.FindContentRoot(), "frontier:7", 6);

    static string Fingerprint(World w)
    {
        var d = w.Director!;
        var rng = d.Rng;
        return string.Join("|",
            $"choir {w.Choir.Loudness:R} {w.Choir.Build:R} {w.Choir.Present} {w.Choir.Spent}",
            $"loud {w.Loudness(Tuning.Combat.Choir):R}",
            $"director {d.Spent:R} {d.HeldBecause} {rng.NextUInt()} " + string.Join(",", d.Log.Select(l => $"{l.Tick}:{l.Kind}")),
            "enemies " + string.Join(",", w.ActiveEnemies.Select(e => $"{e.Id}:{e.Kind}:{e.Phase}")),
            $"voices {string.Join(",", Enumerable.Range(0, 5).Select(i => w.Voices.LastSpoke(i)?.ToString() ?? "-"))}");
    }

    [Fact]
    public void CallOutAndTheLiveMicChangeNothingTheRunListensTo()
    {
        var quiet = Night(out var hq);
        var loud = Night(out var hl);
        Assert.Equal(Fingerprint(quiet.World), Fingerprint(loud.World));
        int allowed = 0;
        for (int tick = 0; tick < 90 * SimConstants.TickRate; tick++)
        {
            // The dead ask for everything they can, every tick: a Call Out, and the mic on and off.
            var everyone = loud.Crew.Select(c => (c.Key, c.Value)).ToList();
            foreach (int dead in new[] { 2, 3 })
            {
                if (loud.World.Request(dead, new Request(RequestKind.CallOut, hl.Index), everyone))
                    allowed++;
                loud.World.Request(dead, new Request(RequestKind.LiveMic, tick / 30 % 2), everyone);
            }
            quiet.Step();
            loud.Step();
        }
        Assert.True(hl.CallOuts >= 90 / Tuning.Holdouts.CallOut.CooldownSeconds - 1, $"{hl.CallOuts} call outs");
        Assert.Equal(hl.CallOuts, allowed);
        Assert.Equal(0, hq.CallOuts);
        // The run heard none of it.
        Assert.Equal(Fingerprint(quiet.World), Fingerprint(loud.World));
    }

    [Fact]
    public void ABreachIsTheLivingsAndGoesOnTheMeter()
    {
        // Smashing a lock is cannon-loud (a burst a blow, a blow a second); prying a barricade is machinery (D.7, D.13).
        // Against the same night with the tool held and nobody breaching, the meter's higher, and the Choir hears it.
        var quiet = Night(out var hq);
        var loud = Night(out var h);
        foreach (var n in new[] { quiet, loud })
            n.World.Bodies.SpawnCrate(n.Train, 1, Ballast.Double3.Zero, BodyKind.Crowbar).Carrier = 1;
        loud.Hold(1, true);
        var method = loud.Holdouts.MethodFor(h, BodyKind.Crowbar)!.Value;
        var step = Tuning.Holdouts.Step(method);
        var t = Tuning.Combat.Choir;
        double during = 0;
        for (int i = 0; i < (step.Seconds - 0.5) * SimConstants.TickRate; i++)
        {
            quiet.Step();
            loud.Step();
            during = Math.Max(during, loud.World.Loudness(t) - quiet.World.Loudness(t));
        }
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        Assert.Equal(HoldoutPhase.Occupied, hq.Phase);
        if (step.Loudness == "machinery")
            Assert.Equal(t.MachineryLoudness, during, 9);
        Assert.True(loud.World.Choir.Loudness > quiet.World.Choir.Loudness + 0.1, $"{loud.World.Choir.Loudness} against {quiet.World.Choir.Loudness}");
    }

    [Fact]
    public void TheRepairKitsOpenIsSilent()
    {
        var site = Routes().Plan!.Holdouts.FirstOrDefault(x => x.Type == HoldoutType.PrisonCar);
        if (site is null)
            return;
        HoldoutTests.Night At(out Holdout h)
        {
            var n = new HoldoutTests.Night(front: site.Zone.S0 + 150, enemies: true);
            n.DeadAtTheFortress(2);
            n.Step(0.2);
            h = n.Holdouts.Of(site.Id)!;
            n.AtTheDoor(1, h, BodyKind.RepairKit);
            return n;
        }
        var quiet = At(out _);
        var n = At(out var h);
        n.Hold(1, true);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            quiet.Step();
            n.Step();
        }
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        Assert.Equal(BreachMethod.Open, h.Method);
        Assert.Equal(quiet.World.Choir.Loudness, n.World.Choir.Loudness, 12);
    }
}
