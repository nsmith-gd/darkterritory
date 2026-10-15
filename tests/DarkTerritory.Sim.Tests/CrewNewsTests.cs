using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 750 (GDD open question 2, App. D.6; the social collision audit's R1): a crewmate held or killed out of voice reach is
/// a line for the living, who and where, never what. Within reach, nothing (you hear it); the dead, nothing.
/// </summary>
public class CrewNewsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly CrewNewsTuning T = Tuning.Player.CrewNews;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));
    const int Me = 1, Priya = 2, Tom = 3;

    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 8, 1)), Line, 2_000);

    static string Name(int id) => id switch { Priya => "Priya", Tom => "Tom", _ => "Me" };

    static PlayerState Held(in PlayerState s) => s with { Flags = s.Flags | PlayerFlags.Held };

    static PlayerState Dead(in PlayerState s) => s with { Health = 0, Death = DeathCause.Mauled };

    static double Apart(TrainOnLine train, in PlayerState a, in PlayerState b) =>
        (PlayerMotor.WorldPosition(a, train) - PlayerMotor.WorldPosition(b, train)).Length;

    [Fact]
    public void TheGamesTuningIsOnAtVoicesReach()
    {
        Assert.True(T.On);
        Assert.Equal(VoiceRouting.ProximityCutoff, T.BeyondMetres);
        Assert.Equal(4, T.DeathSeconds);
    }

    [Fact]
    public void AHeldCrewmateBeyondVoiceReachIsALineForTheLivingAndNotWithinIt()
    {
        var train = Train();
        var news = new CrewNews();
        var me = PlayerMotor.SpawnOnRoof(train, 1, 0, P);
        var far = PlayerMotor.SpawnOnRoof(train, 6, 0, P);
        var near = PlayerMotor.SpawnOnRoof(train, 1, 3, P);
        Assert.True(Apart(train, me, far) > T.BeyondMetres);
        Assert.True(Apart(train, me, near) < T.BeyondMetres);
        Assert.Null(news.Update(T, train, Me, me, 0, [(Me, me), (Priya, far)], Name));
        var line = news.Update(T, train, Me, me, 1, [(Me, me), (Priya, Held(far))], Name);
        Assert.Equal("PRIYA - CAR 6 ROOF", line?.Text);
        Assert.False(line!.Value.Dead);
        // As long as the hold lasts.
        Assert.Equal("PRIYA - CAR 6 ROOF", news.Update(T, train, Me, me, 400, [(Me, me), (Priya, Held(far))], Name)?.Text);
        // Let go: gone.
        Assert.Null(news.Update(T, train, Me, me, 401, [(Me, me), (Priya, far)], Name));
        // Within 26 m: heard, so nothing's shown.
        Assert.Null(news.Update(T, train, Me, me, 402, [(Me, me), (Priya, Held(near))], Name));
        // Nor is your own hold: you have "SOMETHING HAS YOU".
        Assert.Null(news.Update(T, train, Me, Held(me), 403, [(Me, Held(me))], Name));
    }

    [Fact]
    public void TheDeadAreShownNothing()
    {
        var train = Train();
        var news = new CrewNews();
        var me = Dead(PlayerMotor.SpawnOnRoof(train, 1, 0, P));
        var far = PlayerMotor.SpawnOnRoof(train, 6, 0, P);
        news.Update(T, train, Me, me, 0, [(Me, me), (Priya, far), (Tom, far)], Name);
        Assert.Null(news.Update(T, train, Me, me, 1, [(Me, me), (Priya, Held(far)), (Tom, Dead(far))], Name));
    }

    [Fact]
    public void ADeathOutOfReachIsALineForItsSecondsAndNotWithinReach()
    {
        var train = Train();
        var news = new CrewNews();
        var me = PlayerMotor.SpawnOnRoof(train, 1, 0, P);
        var far = PlayerMotor.SpawnOnRoof(train, 6, 0, P);
        var near = PlayerMotor.SpawnOnRoof(train, 1, 3, P);
        news.Update(T, train, Me, me, 10, [(Me, me), (Priya, far), (Tom, near)], Name);
        var line = news.Update(T, train, Me, me, 11, [(Me, me), (Priya, Dead(far)), (Tom, Dead(near))], Name);
        Assert.Equal("PRIYA IS GONE - CAR 6 ROOF", line?.Text);
        Assert.True(line!.Value.Dead);
        long last = 11 + (long)(T.DeathSeconds * SimConstants.TickRate);
        Assert.NotNull(news.Update(T, train, Me, me, last, [(Me, me), (Priya, Dead(far)), (Tom, Dead(near))], Name));
        Assert.Null(news.Update(T, train, Me, me, last + 1, [(Me, me), (Priya, Dead(far)), (Tom, Dead(near))], Name));
        // Tom died beside you: you heard it.
        var tomOnly = new CrewNews();
        tomOnly.Update(T, train, Me, me, 0, [(Me, me), (Tom, near)], Name);
        Assert.Null(tomOnly.Update(T, train, Me, me, 1, [(Me, me), (Tom, Dead(near))], Name));
        // Seen dead from the first (a joiner's first snapshot): not news.
        Assert.Null(new CrewNews().Update(T, train, Me, me, 1, [(Me, me), (Priya, Dead(far))], Name));
    }

    [Fact]
    public void TheNewestWins()
    {
        var train = Train();
        var news = new CrewNews();
        var me = PlayerMotor.SpawnOnRoof(train, 1, 0, P);
        var six = PlayerMotor.SpawnOnRoof(train, 6, 0, P);
        var seven = PlayerMotor.SpawnOnRoof(train, 7, 0, P);
        news.Update(T, train, Me, me, 0, [(Me, me), (Priya, Held(six)), (Tom, seven)], Name);
        Assert.Equal("TOM - CAR 7 ROOF", news.Update(T, train, Me, me, 5, [(Me, me), (Priya, Held(six)), (Tom, Held(seven))], Name)?.Text);
        // Tom's let go: Priya's still held.
        Assert.Equal("PRIYA - CAR 6 ROOF", news.Update(T, train, Me, me, 6, [(Me, me), (Priya, Held(six)), (Tom, seven)], Name)?.Text);
        // Tom's killed: the death's the newest.
        news.Update(T, train, Me, me, 7, [(Me, me), (Priya, Held(six)), (Tom, seven)], Name);
        Assert.Equal("TOM IS GONE - CAR 7 ROOF", news.Update(T, train, Me, me, 8, [(Me, me), (Priya, Held(six)), (Tom, Dead(seven))], Name)?.Text);
    }

    [Fact]
    public void OffNothingsShown()
    {
        var train = Train();
        var news = new CrewNews();
        var off = T with { On = false };
        var me = PlayerMotor.SpawnOnRoof(train, 1, 0, P);
        var far = PlayerMotor.SpawnOnRoof(train, 6, 0, P);
        news.Update(off, train, Me, me, 0, [(Me, me), (Priya, far), (Tom, far)], Name);
        Assert.Null(news.Update(off, train, Me, me, 1, [(Me, me), (Priya, Held(far)), (Tom, Dead(far))], Name));
    }

    [Fact]
    public void WhereIsSaidAsTheCrewWouldSayIt()
    {
        var train = Train();
        Assert.Equal("CAR 4 ROOF", CrewNews.Place(train, PlayerMotor.SpawnOnRoof(train, 4, 0, P)));
        Assert.Equal("THE CAB", CrewNews.Place(train, PlayerMotor.SpawnInCab(train, P)));
        var room = train.Frames[4].Shape.Interior!.Value;
        var inside = PlayerMotor.SpawnOnRoof(train, 4, 0, P) with { Position = room.Centre with { Y = room.Min.Y + 0.1 }, Surface = Surface.Deck };
        Assert.Equal("INSIDE CAR 4", CrewNews.Place(train, inside));
        // The coupling gap behind car 3, in its frame.
        var gap = inside with { Parent = 3, Position = CrewSense.GapLocal(train, 3), Surface = Surface.Coupler };
        Assert.Equal("CAR 3 GAP", CrewNews.Place(train, gap));
        // Left behind on the line, and at a stop.
        double behind = train.RearDistance - 120;
        var left = PlayerMotor.SpawnOnGround(Line.Sample(behind).Position, Line, behind, P);
        Assert.Equal("OFF THE TRAIN, 120 M BACK", CrewNews.Place(train, left));
        Assert.Equal("AT THE STOP", CrewNews.Place(train, left, stop: true));
    }
}
