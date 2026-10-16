using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A Soot Child's telegraph (note 823; the director, 15 Oct: "We need a telegraph for when it's about to kill us as well so
/// the player can pay attention to that telegraph as a last minute save measure"): as someone afoot comes within a step or
/// two of its lunge it turns to them and rises, poised (soot_child.py's rise), all of it by the lunge itself.
/// </summary>
public class SootChildTelegraphTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly SootChildrenTuning Tuning = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).SootChildren;

    [Fact]
    public void ItRisesAsSomeoneComesNearItsLungeAndIsPoisedAtIt()
    {
        double lunge = Tuning.LungeWithin;
        Assert.Equal(0f, CreatureArt.SootPoise(lunge + CreatureArt.SootRiseFrom + 0.5, lunge));
        Assert.Equal(0f, CreatureArt.SootPoise(lunge + CreatureArt.SootRiseFrom, lunge));
        Assert.Equal(1f, CreatureArt.SootPoise(lunge, lunge));
        float was = 0;
        for (double d = lunge + CreatureArt.SootRiseFrom; d >= lunge; d -= 0.1)
        {
            float poise = CreatureArt.SootPoise(d, lunge);
            Assert.True(poise >= was, $"at {d:0.0} m it sank back ({poise:0.00} after {was:0.00})");
            was = poise;
        }
        // A step or two: long enough to see it and back off at a walk, short of giving it away from the dark.
        Assert.InRange(CreatureArt.SootRiseFrom, 1.5, 3.5);
    }

    [Fact]
    public void ItsModelHasTheTelegraphAndTheHoldInYourArms()
    {
        var art = new CreatureArt(Look.Load(Content), Content);
        var clips = art.Get("soot_child")!.Clips;
        Assert.Contains("rise", clips.Keys);
        Assert.Contains("held", clips.Keys);
        Assert.InRange(clips["rise"].Duration, CreatureArt.SootRiseSeconds - 0.05, CreatureArt.SootRiseSeconds + 0.05);
    }
}
