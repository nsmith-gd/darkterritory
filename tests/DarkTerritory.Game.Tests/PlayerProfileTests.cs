namespace DarkTerritory.Game.Tests;

/// <summary>GDD v1.4 App. D.12 (note 180): the commendations a player is given are tallied in their profile, night after night.</summary>
public class PlayerProfileTests
{
    [Fact]
    public void WhatTheCrewCommendedYouForIsKept()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-profile-{Guid.NewGuid():N}.json");
        try
        {
            var profile = new PlayerProfile(path);
            Assert.Empty(profile.Load().Commendations);
            profile.Record([(1, 0, 2), (2, 0, 2), (2, 3, 0)], me: 0);
            var after = profile.Record([(4, 0, 0)], me: 0);
            Assert.Equal(2, after.Commendations["Kept the Fire"]);
            Assert.Equal(1, after.Commendations["Came Back For Me"]);
            Assert.Equal(after.Commendations, new PlayerProfile(path).Load().Commendations);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void AHostsFirstChildCallIsRealUntilANightTheyHostedHasHadOne()
    {
        // GDD App. B.6 (note 182): kept in the profile, so it's the host's first-ever call, not each night's or each campaign's.
        var path = Path.Combine(Path.GetTempPath(), $"dt-profile-{Guid.NewGuid():N}.json");
        try
        {
            var profile = new PlayerProfile(path);
            Assert.True(profile.FirstChildReal);
            profile.Record([(1, 0, 2)], me: 0);
            Assert.True(profile.FirstChildReal);
            profile.MarkChildCalled();
            Assert.False(new PlayerProfile(path).FirstChildReal);
            // Commendations are kept beside it.
            Assert.Equal(1, new PlayerProfile(path).Load().Commendations["Kept the Fire"]);
            var setup = new SessionSetup(Route: "frontier:7") { FirstChildReal = true, Cargo = DarkTerritory.Sim.Train.CargoKind.Comet };
            // The host's own: never sent to a joiner. The freight is, so every machine builds the same consist.
            var sent = SessionSetup.Decode(setup.Encode());
            Assert.False(sent.FirstChildReal);
            Assert.Equal(DarkTerritory.Sim.Train.CargoKind.Comet, sent.Cargo);
            Assert.DoesNotContain("powder", new SessionSetup().Encode(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheRecordIsTheFurthestAnyCrewHasGone()
    {
        // Note 535: what a run its host lists is said with. A crew going further raises it; one that hasn't, doesn't.
        string path = Path.Combine(Path.GetTempPath(), $"dt-profile-{Guid.NewGuid():N}.json");
        try
        {
            var profile = new PlayerProfile(path);
            Assert.Equal(0, profile.Load().RecordKm);
            profile.Reach(212.4);
            profile.Reach(40);
            Assert.Equal(212.4, new PlayerProfile(path).Load().RecordKm);
            profile.CountNight();
            Assert.Equal(212.4, new PlayerProfile(path).Load().RecordKm);
            Assert.Equal("212", NetPlaySession.Record(new PlayerProfile(path).Load().RecordKm));
            Assert.Equal("", NetPlaySession.Record(0.4));
            Assert.Equal((int?)212, ListedGame.ParseRecord("212"));
            Assert.Null(ListedGame.ParseRecord(""));
            Assert.Null(ListedGame.ParseRecord("far"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
