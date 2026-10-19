using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The tools heard working (queue #505, note 775; GameAudio.Tools.cs; upgrades.md §8.1, the director: "all of these are going
/// to need hard signs and feedback for the player to understand what the consequence is"): each at its moment, only with the
/// upgrade bought (campaign.json, through <see cref="Campaign.Apply"/> as every machine builds the night), and beside the
/// sound of what it changes, never instead of a tell. Each cue is installed as a short tone and counted off the mixer.
/// </summary>
public class ToolSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CampaignTuning Shop = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly Loadout Base = new(
        DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)),
        DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File)),
        DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File)),
        DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    const double Dt = SimConstants.TickSeconds;

    sealed class Bench
    {
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<string> Heard = [];
        readonly HashSet<int> _seen = [];
        readonly string[] _cues;
        /// <summary>Run after the world's step, before what's heard: the host's record as a client has it.</summary>
        public Action? Mirror;

        /// <summary>A train bought <paramref name="upgrades"/> (none: as it comes), the audio with only <paramref name="cues"/> installed.</summary>
        public Bench(string[] upgrades, params string[] cues)
        {
            var night = Campaign.Apply(Shop, upgrades, Base);
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(night.Train, 4, 1)), line, 5_000), night.Combat);
            _cues = cues;
            foreach (var cue in cues)
                Audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
            Audio.PlayerTuning = P;
            Audio.OwnId = 1;
        }

        public TrainOnLine Train => World.Train;

        public void Step(TrainControls controls = default, PlayerIntent intent = default)
        {
            World.BeginTick();
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, intent, id);
                Crew[id] = s;
            }
            World.Step(controls);
            Mirror?.Invoke();
            Audio.CrewStates = [.. Crew.Select(c => (c.Key, c.Value))];
            Audio.OwnIntent = intent;
            Audio.Update(World, controls, Listener.At(Train.Frames[1].Origin + Double3.Up * 3, 0), exposed: true, Dt);
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && _cues.Contains(v.Name))
                    Heard.Add(v.Name);
        }

        public void Run(double seconds, TrainControls controls = default, PlayerIntent intent = default)
        {
            for (int i = 0; i < seconds / Dt; i++)
                Step(controls, intent);
        }

        public int Count(string name) => Heard.Count(h => h == name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRammersClackHomeInTheWoodenRammersPlace(bool fitted)
    {
        // The reload's last stroke (note 746): with the gun rammers bought, the sprung rod's clack; without, the rammer's own.
        var b = new Bench(fitted ? ["gunRammers"] : [], "crew-cannon-ram.ram-home", "up-tells.rammer");
        var guard = b.Train.Vehicles.Last(v => v.HasGun);
        int steps = b.World.Combat!.Guns.ReloadSteps;
        b.Step();
        guard.Gun.LastShotTick = 99;
        guard.Gun.ReloadNeeded = steps;
        b.Step();
        for (int step = steps; step >= 1; step--)
        {
            for (int i = 0; i < 10; i++)
            {
                guard.Gun.ReloadProgress += Dt;
                b.Step();
            }
            guard.Gun.ReloadProgress = 0;
            guard.Gun.ReloadNeeded = step - 1;
            b.Step();
        }
        Assert.Equal(fitted ? 1 : 0, b.Count("up-tells.rammer"));
        Assert.Equal(fitted ? 0 : 1, b.Count("crew-cannon-ram.ram-home"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBoosterCutsInOnceAsTheTrainPullsAwayWithSteamOn(bool fitted)
    {
        var b = new Bench(fitted ? ["boosterEngine"] : [], "up-tells.booster");
        var steam = new TrainControls { Reverser = 1, Throttle = 1 };
        // Standing, steam on or not: nothing.
        b.Run(1, steam with { Throttle = 0 });
        b.Mirror = () => b.Train.Dynamics.Velocity = -Math.Min(8, b.Train.Dynamics.Speed + 2 * Dt);
        // Pulling away and on up to speed: once, as it gets going.
        b.Run(6, steam);
        Assert.Equal(fitted ? 1 : 0, b.Count("up-tells.booster"));
        // Coasting down to a stand and away again: once more.
        b.Mirror = () => b.Train.Dynamics.Velocity = -Math.Max(0, b.Train.Dynamics.Speed - 4 * Dt);
        b.Run(3, steam with { Throttle = 0 });
        b.Mirror = () => b.Train.Dynamics.Velocity = -Math.Min(8, b.Train.Dynamics.Speed + 2 * Dt);
        b.Run(3, steam);
        Assert.Equal(fitted ? 2 : 0, b.Count("up-tells.booster"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCompositionShoesMoanOnceAsTheyHeatOnADescent(bool fitted)
    {
        // BrakeFade (TrainDynamics.BrakeEfficiency): the shoes heating as the brakes are held down a long descent, to where the
        // iron ones go hot; the composition shoes' moan once there, not again till they've cooled.
        var b = new Bench(fitted ? ["compositionShoes"] : [], "up-tells.composition");
        var rake = b.Train.Dynamics;
        double min = rake.Tuning.BrakeFade.MinEfficiency;
        double efficiency = 1;
        b.Mirror = () => rake.Restore(rake.Distance, -10, efficiency);
        var braking = new TrainControls { Reverser = 1, Brake = 0.6 };
        foreach (double heat in new[] { 0.1, 0.3, 0.55, 0.8, 0.6, 0.9 })
        {
            efficiency = 1 - heat * (1 - min);
            b.Run(0.5, braking);
        }
        Assert.Equal(fitted ? 1 : 0, b.Count("up-tells.composition"));
        // Cooled right down, and hot again: once more.
        efficiency = 1;
        b.Run(0.5, braking);
        efficiency = 1 - 0.7 * (1 - min);
        b.Run(0.5, braking);
        Assert.Equal(fitted ? 2 : 0, b.Count("up-tells.composition"));
    }

    [Fact]
    public void TheLuckyHorseshoeGlintsAtAShotThatWouldHaveFouledWithoutItAndOnlyThen()
    {
        // The foul's the tick's and the gun's (Guns.Fouls), the same on every machine: a shot whose tick fouls at the chance
        // without the horseshoe and not at the chance with it is a foul it saved; any other shot isn't.
        var b = new Bench(["luckyHorseshoe"], "up-tells.horseshoe");
        var guns = b.World.Combat!.Guns;
        Assert.Equal(0.85, guns.HorseshoeFoul, 9);
        Assert.Equal(Base.Combat.Guns.FoulChance * 0.85, guns.FoulChance, 9);
        var gun = b.Train.Vehicles.Last(v => v.HasGun);
        uint Find(Func<bool, bool, bool> want) =>
            Enumerable.Range(1000, 1_000_000).Select(t => (uint)t)
                .First(t => want(Guns.Fouls(t, gun.Id, guns.FoulChance), Guns.Fouls(t, gun.Id, guns.FoulChance / guns.HorseshoeFoul)));
        uint saved = Find((now, without) => !now && without), clean = Find((now, without) => !now && !without);
        b.Step();
        gun.Gun.LastShotTick = clean;
        b.Run(1.5);
        Assert.Equal(0, b.Count("up-tells.horseshoe"));
        gun.Gun.LastShotTick = saved;
        b.Run(1.5);
        Assert.Equal(1, b.Count("up-tells.horseshoe"));

        // Without the horseshoe the same shot is only a shot.
        var plain = new Bench([], "up-tells.horseshoe");
        var g = plain.Train.Vehicles.Last(v => v.HasGun);
        plain.Step();
        g.Gun.LastShotTick = saved;
        plain.Run(1.5);
        Assert.Equal(0, plain.Count("up-tells.horseshoe"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOilersEaseAHotboxsFirstKnockAndTheTellKnocksOn(bool fitted)
    {
        // A Hotbox knocking in its truck (note 367; the oilers make its knock last longer, enemies.json hotbox.knockSeconds):
        // with the axle oilers, at its first knock the oil's eased knock and drip, once; its own knock, the tell, every turn.
        var b = new Bench(fitted ? ["axleOilers"] : [], "tell-hotbox.knock", "up-tells.oilers");
        var hotbox = new Hotbox(70);
        hotbox.Restore(SpinePhase.Dormant, 1, 6, 2, new Double3(0.7, 0.5, 4), 0, 1, (double)HotboxMode.Knock, 10, 0, -1, 0);
        b.Mirror = () =>
        {
            b.World.MirrorEnemies([hotbox]);
            b.Train.Dynamics.Velocity = -10;
        };
        b.Run(4);
        Assert.True(b.Count("tell-hotbox.knock") >= 3, $"{b.Count("tell-hotbox.knock")} knocks");
        Assert.Equal(fitted ? 1 : 0, b.Count("up-tells.oilers"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreCutBoardsGoUpQuickAndLightAndTheHammeringFollows(bool fitted)
    {
        // A breach boarded up (note 301, the wrench at the hole): the board put to it as ever; with the pre-cut boards, their
        // run of quick blows at once, and the hammer's own blows after it rather than over it.
        var b = new Bench(fitted ? ["precutBoards"] : [], "crew-repair.board-place", "crew-repair.hammer", "up-tells.precut");
        const int car = 2;
        b.Train.Vehicles[car].Breach(Breaches.EndWall(b.Train.Frames[car].Shape)!.Value);
        b.Crew[1] = new PlayerState
        {
            Parent = car,
            Position = Breaches.StandAt(b.Train, car),
            Surface = Surface.Deck,
            Health = P.Health,
            Yaw = Math.PI,
            LineHint = b.Train.Cars[car].FrontDistance,
            Kit = P.StartingKit,
            HeldSlot = 1,
        };
        b.Step();
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        b.Run(1.2, intent: use);
        Assert.Equal(1, b.Count("crew-repair.board-place"));
        Assert.Equal(fitted ? 1 : 0, b.Count("up-tells.precut"));
        // Within the run, the hammer's quiet; without the boards, it's at it already.
        if (fitted)
            Assert.Equal(0, b.Count("crew-repair.hammer"));
        else
            Assert.InRange(b.Count("crew-repair.hammer"), 1, 3);
        b.Run(1.5, intent: use);
        Assert.True(b.Count("crew-repair.hammer") >= 1);
    }
}
