using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Ferryman (T56, App. A.2, B.2): a lantern on the line ahead, waving you down. Rule: do not slow down.</summary>
public class FerrymanTests
{
    static readonly FerrymanTuning F = Tuning.Enemies.Ferryman;
    static readonly LineDefinition Straight = new("t", [new TrackSegment(80_000)]);
    static readonly RailLine Line = new(Straight);

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        /// <summary>Every spine transition so far (the world's list is only this tick's).</summary>
        public readonly List<EnemyEvent> Events = [];
        /// <summary>Held at this speed every tick; null lets the train run on its own controls.</summary>
        public double? Speed;
        public TrainControls Controls = new() { Reverser = 1 };

        public Night(double? speed, double at = 2_000, EnemyTuning? enemies = null, Route.Route? route = null)
        {
            Speed = speed;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, at);
            if (speed is { } v)
                train.Dynamics.Velocity = v;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(enemies ?? quiet, route, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;

        public int InCab()
        {
            Crew.Add(PlayerMotor.SpawnInCab(Train, Tuning.Player));
            Intents.Add(default);
            return Crew.Count;
        }

        public Ferryman Ahead(int side = 1) => World.AddEnemy(id => Ferryman.Ahead(id, Train, side, F));

        public void Run(double seconds, Action? each = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                each?.Invoke();
                if (Speed is { } v)
                    Train.Dynamics.Velocity = v;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(Controls);
                Events.AddRange(World.EnemyEvents);
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    PlayerMotor.Step(ref s, Intents[c], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = s;
                }
            }
        }
    }

    [Fact]
    public void TheLanternIsTheTellFromTheMomentItsThere()
    {
        var night = new Night(speed: 14);
        var f = night.Ahead();
        night.Run(SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Telegraph, f.Phase);
        Assert.True(f.Lantern);
        Assert.True(f.Waving);
        // Far up the line: the lantern is seen long before it matters.
        Assert.True(f.LineDistance - night.Train.Dynamics.Distance > F.SpawnAhead - 5);
    }

    [Fact]
    public void HoldYourSpeedAndItStepsAsideAtTheLastMomentAndIsGone()
    {
        var night = new Night(speed: 14);
        int driver = night.InCab();
        var f = night.Ahead();
        bool steppedOnto = false;
        night.Run(F.SpawnAhead / 14 - 5, () => steppedOnto |= Math.Abs(f.Lateral) < 0.1);
        // It stepped out onto the line to wave the train down.
        Assert.True(steppedOnto);
        night.Run(8);
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.BreakOff);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
        // Aside, off the line, with the train going by.
        Assert.True(Math.Abs(f.Lateral) > 1.5 || f.Gone);
        night.Run(20);
        Assert.True(f.Gone);
        Assert.Equal(Tuning.Player.Health, night.Crew[driver - 1].Health);
    }

    [Fact]
    public void SlowForItAndItComesDownTheLineBoardsAndTakesTheConductor()
    {
        var night = new Night(speed: 14);
        int driver = night.InCab();
        var f = night.Ahead();
        night.Run(10);
        // Every instinct says brake.
        night.Speed = 4;
        night.Run(SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        Assert.False(f.Waving);
        night.Run(60, () =>
        {
            if (f.Phase == SpinePhase.Punish)
                night.Speed = 4;
        });
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Punish);
        Assert.False(night.Crew[driver - 1].Alive);
        Assert.Equal(DeathCause.Ferryman, night.Crew[driver - 1].Death);
        Assert.True(f.Gone);
    }

    [Fact]
    public void SlowingInTheFirstInstantStillGetsTheReactionWindow()
    {
        var night = new Night(speed: 14);
        var f = night.Ahead();
        night.Run(SimConstants.TickSeconds);
        night.Speed = 2;
        night.Run(Tuning.Enemies.MinReactionSeconds - 0.2);
        Assert.Equal(SpinePhase.Telegraph, f.Phase);
        night.Run(0.4);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        var commit = Assert.Single(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
        Assert.True(commit.SecondsInFrom >= Tuning.Enemies.MinReactionSeconds);
    }

    [Fact]
    public void ANotchingWobbleIsntSlowing()
    {
        var night = new Night(speed: 14);
        var f = night.Ahead();
        int tick = 0;
        night.Run(20, () => night.Speed = 14 + Math.Sin(tick++ * 0.05) * (F.SlowTolerance * 0.4));
        Assert.Equal(SpinePhase.Telegraph, f.Phase);
    }

    [Fact]
    public void OnceItsSteppedAsideSlowingDoesNothing()
    {
        var night = new Night(speed: 14);
        int driver = night.InCab();
        var f = night.Ahead();
        night.Run(F.SpawnAhead / 14);
        Assert.Equal(SpinePhase.BreakOff, f.Phase);
        night.Speed = 0;
        night.Run(30);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
        Assert.Equal(Tuning.Player.Health, night.Crew[driver - 1].Health);
    }

    static Route.Route Frontier(params RouteFeature[] features) =>
        new("t", RouteTier.Frontier, 1, Straight, features, new RouteWeather(0.01, false, 0, 0), 3600);

    static EnemyTuning Only()
    {
        var d = Tuning.Enemies.Director;
        return Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "ferryman" ? c.Value : 1e9) },
        };
    }

    [Fact]
    public void TheDirectorSendsOneOnAClearStraightMidRunOnlyOnce()
    {
        // Early in the run: none.
        var early = new Night(speed: 14, at: 5_000, Only(), Frontier());
        early.Run(30);
        Assert.DoesNotContain(early.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);

        var night = new Night(speed: 14, at: 40_000, Only(), Frontier());
        night.Run(200);
        var sent = night.World.Director!.Log.Where(l => l.Kind == EnemyKind.Ferryman).ToList();
        Assert.Single(sent);
        Assert.Equal(Tuning.Enemies.Director.Costs["ferryman"], sent[0].Cost);
    }

    [Fact]
    public void NotWithAReasonToSlowAheadNorOnALocalLineNorWithTheLampSmashed()
    {
        // Sleepers up the line: a crew that brakes for them is right to. It isn't placed where it's a trap you can only lose.
        var sleepers = new Night(speed: 14, at: 40_000, Only(), Frontier(new RouteFeature(FeatureKind.Sleepers, 40_400, 40_420)));
        sleepers.Run(10);
        Assert.DoesNotContain(sleepers.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);

        var local = new Night(speed: 14, at: 40_000, Only(), Frontier() with { Tier = RouteTier.Local });
        local.Run(10);
        Assert.DoesNotContain(local.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);

        var smashed = new Night(speed: 14, at: 40_000, Only(), Frontier());
        smashed.World.SmashLamp(60);
        smashed.Run(10);
        Assert.DoesNotContain(smashed.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);

        // A weak bridge up the line, posted well under the train's speed (T74): the driver has to slow for the board, so it's
        // no place for a lantern that punishes slowing.
        var sight = Ballast.DataFile.Load<SightTuning>(Path.Combine(Ballast.DataFile.FindContentRoot(), SightTuning.File));
        var posted = Frontier(new RouteFeature(FeatureKind.Bridge, 40_600, 40_650, MaxCars: 3));
        var bridge = new Night(speed: 14, at: 40_000, Only(), posted);
        bridge.World.EnableLineside(sight, posted);
        bridge.Run(10);
        Assert.DoesNotContain(bridge.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);
        // (The same line without the bridge, boards and all: it comes.)
        var clear = new Night(speed: 14, at: 40_000, Only(), Frontier());
        clear.World.EnableLineside(sight, Frontier());
        clear.Run(10);
        Assert.Contains(clear.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);

        var crawling = new Night(speed: F.MinSpeed - 2, at: 40_000, Only(), Frontier());
        crawling.Run(10);
        Assert.DoesNotContain(crawling.World.Director!.Log, l => l.Kind == EnemyKind.Ferryman);
    }

    [Fact]
    public void AClientSeesTheLanternAndItsPlaceOnTheLine()
    {
        var night = new Night(speed: 14);
        var f = night.Ahead(side: -1);
        night.Run(2);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(night.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Ferryman>(Assert.Single(client.ActiveEnemies));
        Assert.True(seen.Lantern);
        Assert.Equal(f.LineDistance, seen.LineDistance, 1);
        Assert.Equal(f.Lateral, seen.Lateral, 2);
    }

    [Fact]
    public void TheDriverBotHoldsItsSpeedPastTheLanternEvenWithTheLampDown()
    {
        var night = new Night(speed: null, at: 2_000);
        int driver = night.InCab();
        var bot = new ConductorBot();
        // Up to cruise first.
        night.Run(90, () => night.Intents[driver - 1] = Drive(night, bot, driver));
        Assert.InRange(night.Train.Dynamics.Speed, bot.CruiseSpeed - 1.5, bot.CruiseSpeed + 1.5);
        var f = night.Ahead();
        // Lamps down (for the Lamplighters, say) would drop it to the dark cruise: past a lantern, it mustn't.
        night.World.LampLit = false;
        night.Run(90, () => night.Intents[driver - 1] = Drive(night, bot, driver) with { Lamp = LampSwitch.None });
        Assert.Contains(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.BreakOff);
        Assert.DoesNotContain(night.Events, e => e.EnemyId == f.Id && e.To == SpinePhase.Commit);
        Assert.True(night.Crew[driver - 1].Alive);

        static PlayerIntent Drive(Night night, ConductorBot bot, int driver)
        {
            var intent = bot.Decide(night.Crew[driver - 1], night.World, night.World.Tick, out _);
            // The cab's controls from the bot's intent, as the host applies a driver's.
            night.Controls.Brake = 0;
            CabControls.Apply(ref night.Controls, intent, night.Crew[driver - 1], night.Train);
            return intent;
        }
    }
}
