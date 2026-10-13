using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Gaunt carries off what it took (note 505; GDD App. A.6 "it carries the body out at walking pace, in full view";
/// the art checklist's gaunt-anim): leaving with a load, it holds it in its mouth, the neck down under its body to where
/// the sim holds the load (Gaunt.Carry: <c>carryHigh</c> over its feet, aboard <c>carryLow</c>).
/// </summary>
public class GauntCarryTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CreatureArt Art = new(Look.Load(Content), Content);
    static readonly GauntTuning Tuning = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Gaunt;

    /// <summary>Where the jaw's hinge is over the clip's loop (model space: +Y up, −Z the way it faces).</summary>
    static List<Vector3> Jaw(string clip)
    {
        int jaw = Art.Get("gaunt")!.Skeleton.IndexOf("jaw");
        return Enumerable.Range(0, 8).Select(i => Art.Joints("gaunt", clip, Art.Get("gaunt")!.Clip(clip).Duration * i / 8, true).ElementAt(jaw)).ToList();
    }

    [Theory]
    [InlineData("carry", false)]
    [InlineData("carry_low", true)]
    public void ItsMouthIsOnTheLoadUnderItAsItGoes(string clip, bool aboard)
    {
        double load = aboard ? Tuning.CarryLow : Tuning.CarryHigh;
        foreach (var at in Jaw(clip))
        {
            // Down at the load, not up where its head is carried following. (The jaw's hinge is 0.3 m back from the mouth,
            // and the head's turned back onto the load, so the hinge is that much further out ahead of it; gaunt.py's build
            // checks the mouth's own tip, to a few centimetres: "[dt] gaunt carry ...".)
            var from = at - new Vector3(0, (float)load, 0);
            Assert.True(from.Length() < 0.85f, $"{clip}: the jaw at {at}, {from.Length():0.00} m from the load at {load} m");
            Assert.True(at.Y < (aboard ? 1.0f : 2.0f), $"{clip}: the jaw at {at.Y:0.00} m, up out of reach of the load");
        }
        // And not as the empty-handed walk has it: its head carried out ahead, well off the load.
        var on = new Vector3(0, (float)load, 0);
        float walking = Jaw(aboard ? "crawl" : "follow").Average(j => (j - on).Length()), carrying = Jaw(clip).Average(j => (j - on).Length());
        Assert.True(walking > carrying + 0.25f, $"{clip}: the jaw {carrying:0.00} m from the load, walking {walking:0.00}");
    }

    [Fact]
    public void LeavingWithALoadItCarriesItAndEmptyHandedItWalks()
    {
        // CreatureArt's pick: BreakOff with a body's id (extra) is carrying; talked down, extra −1, it walks off with nothing.
        Assert.Contains("carry", Art.Get("gaunt")!.ClipNames);
        Assert.Contains("carry_low", Art.Get("gaunt")!.ClipNames);
        Assert.Equal("carry", CreatureArt.GauntClip(SpinePhase.BreakOff, 7, 1.4f, low: false));
        Assert.Equal("carry_low", CreatureArt.GauntClip(SpinePhase.BreakOff, 7, 1.4f, low: true));
        Assert.Equal("follow", CreatureArt.GauntClip(SpinePhase.BreakOff, -1, 1.4f, low: false));
        Assert.Equal("crawl", CreatureArt.GauntClip(SpinePhase.BreakOff, -1, 1.4f, low: true));
    }
}
