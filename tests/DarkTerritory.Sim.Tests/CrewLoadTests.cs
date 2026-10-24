using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Queue #723 (note 1020; the director's two-player Frontier night, 24 Oct): a crew size's own load (pressure.crewLoad) on the
/// pressure's build and λ alike, and a tier's own escalation (pressure.escalationTier). Shipped empty, today's night exactly;
/// the numbers are proposed on the Creature Tweaker.
/// </summary>
public class CrewLoadTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PressureTuning P = E.Director.Pressure;

    static EnemyTuning With(PressureTuning p) => E with { Director = E.Director with { Pressure = p } };

    [Fact]
    public void ShippedEmptyEveryCrewAndTierIsAsBefore()
    {
        Assert.Empty(P.CrewLoad);
        Assert.Empty(P.EscalationTier);
        for (int crew = 1; crew <= 8; crew++)
            Assert.Equal(1, P.Load(crew));
        Assert.Equal(P.Escalation, P.EscalationOn("frontier"));
        var p = P with { CrewLoad = new() { ["2"] = 0.6 }, EscalationTier = new() { ["frontier"] = 1.5 } };
        Assert.Equal(0.6, p.Load(2));
        Assert.Equal(1, p.Load(3));
        Assert.Equal(1.5, p.EscalationOn("frontier"));
        Assert.Equal(P.Escalation, p.EscalationOn("deadLines"));
    }

    [Fact]
    public void ACrewsLoadScalesThePressuresBuildAndTheEncounterRateAlike()
    {
        GuardNight night(PressureTuning p, int crew)
        {
            var n = new GuardNight(With(p), null, 6_000, speed: 0, cars: 4, crew: crew);
            n.Aboard(crew);
            // Just past the grace: the pressure has built and nothing's been spent, so the two nights match but for the load.
            n.Run(n.Director.Grace + 3);
            return n;
        }
        var load = P with { CrewLoad = new() { ["2"] = 0.5 } };
        var off = night(P, 2);
        var on = night(load, 2);
        Assert.True(off.Director.Terms.Rate > 0 && off.Director.EncounterRate > 0);
        Assert.Equal(off.Director.Terms.Rate * 0.5, on.Director.Terms.Rate, 9);
        Assert.Equal(off.Director.EncounterRate * 0.5, on.Director.EncounterRate, 9);
        // A crew not named is untouched.
        Assert.Equal(night(P, 3).Director.Terms.Rate, night(load, 3).Director.Terms.Rate, 12);
    }
}
