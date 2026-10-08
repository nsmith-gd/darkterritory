using Ballast;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Sim.Tests;

/// <summary>The townsfolk's personality matrix and their names (ARCHITECTURE §8 note 453; tuning and world townsfolk.json).</summary>
public class TownFolkTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly TownFolkContent Folk = Towns.Folk!;

    static TownSite Site(int seed, string? name = null)
    {
        string[] industries = [.. Towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite(name ?? $"Fort {Towns.Surnames[seed % Towns.Surnames.Count]}", industries[seed % industries.Length], 1100, (ulong)seed, []);
    }

    [Fact]
    public void EveryoneIsSomebodyAndTheSameSomebodyEveryTime()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var a = TownGenerator.Generate(Towns, Site(seed));
            var b = TownGenerator.Generate(Towns, Site(seed));
            Assert.All(a.People, p => Assert.NotNull(p.Personality));
            Assert.Equal(a.People.Select(p => (p.Name, p.Personality)), b.People.Select(p => (p.Name, p.Personality)));
            Assert.All(a.People, p => Assert.Equal(p.Personality!.Name, p.Name));
        }
    }

    [Fact]
    public void ACustomLeansItsPeopleItsWayAndAJobLeansItsOwn()
    {
        // The Passenger's town counts strangers; the Tippy Toesie's holds everyone's hand (tuning "cultures").
        double Mean(string culture, string role, Func<TownTraits, double> trait)
        {
            double sum = 0;
            int n = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var folk = new TownFolk(Folk, Towns.Writing, Site(seed), culture, (ulong)seed);
                for (int i = 0; i < 20; i++, n++)
                    sum += trait(folk.Traits(i, role, "", -1, false));
            }
            return sum / n;
        }
        Assert.True(Mean("count", "hand", t => t.Welcome) < Mean("pairs", "hand", t => t.Welcome) - 0.5);
        Assert.True(Mean("talkers", "hand", t => t.Telling) > Mean("hush", "hand", t => t.Telling) + 0.5);
        Assert.True(Mean("hush", "keeper", t => t.Keeping) > Mean("hush", "fitter", t => t.Keeping) + 0.5);
        Assert.True(Mean("hush", "widow", t => t.Grief) > Mean("hush", "clerk", t => t.Grief) + 0.5);
    }

    [Fact]
    public void EveryTemperamentTurnsUpAndNoneIsATownsWholePeople()
    {
        var seen = new Dictionary<string, int>();
        int people = 0;
        for (int seed = 1; seed <= 80; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            foreach (var p in plan.People)
                seen[p.Personality!.Temperament] = seen.GetValueOrDefault(p.Personality.Temperament) + 1;
            people += plan.People.Count;
            // A town of any size is more than one kind of person.
            var most = plan.People.GroupBy(p => p.Personality!.Temperament).MaxBy(g => g.Count())!;
            Assert.True(most.Count() <= Math.Max(3, plan.People.Count * 0.6),
                $"seed {seed}: {most.Count()} of {plan.People.Count} in {plan.Name} ({plan.Culture}) are {most.Key}");
        }
        foreach (string temperament in Folk.Tuning.Temperaments.Keys.Append("plain"))
            Assert.True(seen.GetValueOrDefault(temperament) > 0, $"nobody is {temperament}");
        Assert.All(seen, kv => Assert.True(kv.Value < people / 2, $"{kv.Key}: {kv.Value} of {people}"));
        // Every temperament has its words.
        Assert.All(Folk.Tuning.Temperaments.Keys, t => Assert.True(Folk.Writing.Temperaments[t] is { Lines.Length: > 0, Bynames.Length: > 0 }, t));
    }

    [Fact]
    public void ANameIsTheirPeoplesTheirGenerationsAndTheirTemperaments()
    {
        var heritages = Folk.Writing.Heritages.ToDictionary(h => h.Id);
        var after = Folk.Writing.After;
        string[] afterNames = [.. after.Virtue, .. after.Daylight, .. after.Plain, .. after.Culture.Values.SelectMany(v => v), .. Towns.Writing.FirstNames];
        int bynamed = 0, people = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            foreach (var p in plan.People)
            {
                var m = p.Personality!;
                people++;
                Assert.Contains(m.Surname, heritages[m.Heritage].Surnames);
                if (m.Generation == "after")
                    Assert.Contains(m.Given, afterNames);
                if (m.Byname.Length == 0)
                    continue;
                bynamed++;
                Assert.Contains(m.Given, m.Byname);
                // Somebody strongly of a temperament is known by it.
                if (m.Strength >= Folk.Tuning.Names.Strong)
                    Assert.Contains(m.Byname, Folk.Writing.Temperaments[m.Temperament].Bynames.Select(f => f.Replace("{first}", m.Given)));
            }
            // A household shares its surname, and so its people.
            foreach (var house in plan.People.Where(p => p.House >= 0).GroupBy(p => p.House))
                Assert.Single(house.Select(p => (p.Personality!.Surname, p.Personality.Heritage)).Distinct());
        }
        // Bynames are for some, not all (tuning "names").
        Assert.InRange(bynamed / (double)people, 0.15, 0.5);
    }

    [Fact]
    public void AFortsNameLeadsItsPeople()
    {
        int acadian = 0;
        for (int seed = 1; seed <= 60; seed++)
            acadian += new TownFolk(Folk, Towns.Writing, Site(seed, "Fort Boudreau"), "hush", (ulong)seed).Mix[0].Heritage == "acadian" ? 1 : 0;
        Assert.True(acadian >= 60 * (Folk.Tuning.Names.FortName - 0.15), $"{acadian} of 60");
    }

    [Fact]
    public void TheCloseMouthedKeepTheCustomToThemselvesAndTheOpenSayTheMost()
    {
        int close = 0, open = 0;
        var telling = Folk.Tuning.Telling;
        for (int seed = 1; seed <= 60; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            var culture = Towns.Writing.Cultures.Single(c => c.Id == plan.Culture);
            var custom = culture.Lines.Where(l => !l.Contains('{')).ToHashSet();
            foreach (var p in plan.People)
            {
                var m = p.Personality!;
                if (m.Traits.Telling < telling.Close && p.Role != "keeper")
                {
                    close++;
                    Assert.DoesNotContain(p.Lines, custom.Contains);
                }
                else if (m.Traits.Telling > telling.Open && p.House < 0)
                {
                    open++;
                    Assert.True(p.Lines.Count >= Towns.Tuning.LinesPerPerson[1] - 1, $"seed {seed}, {p.Name}: {p.Lines.Count} lines");
                }
            }
        }
        Assert.True(close > 0 && open > 0, $"{close} close, {open} open");
    }
}
