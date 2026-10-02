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
}
