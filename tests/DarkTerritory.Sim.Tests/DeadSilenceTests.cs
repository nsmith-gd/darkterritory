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
            $"choir {w.Choir.Aggro:R} {w.Choir.SecondsSinceShot:R}",
            $"loud {w.Loudness.Level:R} " + string.Join(",", w.Loudness.Totals.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value:R}")),
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
        Assert.DoesNotContain(loud.World.Loudness.Totals.Keys, k => k != "gun" && k != "breach");
    }

    [Fact]
    public void ABreachIsTheLivingsAndCountsTowardCrewLoudness()
    {
        var n = Night(out var h);
        n.World.Choir = DarkTerritory.Sim.Combat.ChoirState.Quiet;
        n.World.BeginTick();
        double before = n.World.Choir.Aggro;
        // A melee tool: smash (prison car) or pry (the rest), both loud.
        var tool = n.World.Bodies.SpawnCrate(n.Train, 1, Ballast.Double3.Zero, BodyKind.Crowbar);
        tool.Carrier = 1;
        n.Hold(1, true);
        var method = n.Holdouts.MethodFor(h, BodyKind.Crowbar)!.Value;
        var step = Tuning.Holdouts.Step(method);
        n.Step(step.Seconds - 0.5);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        double level = Tuning.Combat.Loudness.Levels[step.Loudness];
        Assert.InRange(n.World.Loudness.Totals["breach"], level * (step.Seconds - 0.7), level * (step.Seconds - 0.3));
        Assert.True(n.World.Choir.Aggro >= before + level * (step.Seconds - 1), $"{n.World.Choir.Aggro} from {before}");
        Assert.InRange(n.World.Loudness.Level, level * 0.9, level * 1.1);
    }

    [Fact]
    public void TheRepairKitsOpenIsSilent()
    {
        var site = Routes().Plan!.Holdouts.FirstOrDefault(x => x.Type == HoldoutType.PrisonCar);
        if (site is null)
            return;
        var n = new HoldoutTests.Night(front: site.Zone.S0 + 150);
        n.DeadAtTheFortress(2);
        n.Step(0.2);
        var h = n.Holdouts.Of(site.Id)!;
        n.AtTheDoor(1, h, BodyKind.RepairKit);
        n.Hold(1, true);
        n.Step(3);
        Assert.Equal(HoldoutPhase.Breaching, h.Phase);
        Assert.Equal(BreachMethod.Open, h.Method);
        Assert.False(n.World.Loudness.Totals.ContainsKey("breach"));
    }
}
