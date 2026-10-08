using Ballast;
using Ballast.Assets;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A ball stops at the body you see (note 290; GDD App. F.1, build 1121: "rounds don't collide where they land"): each
/// creature's body in enemies.json <c>bodies</c> stands inside its model as drawn, up its height, and no wider than it.
/// </summary>
public class CreatureBodyTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly CreatureArt Art = new(Look.Load(Content), Content);
    static readonly EnemyTuning E = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));

    /// <summary>The model each kind is drawn with, and the scale it's drawn at (CreatureArt: the Ribbit's 1.4).</summary>
    public static TheoryData<EnemyKind, string, float> Drawn() => new()
    {
        { EnemyKind.CinderHound, "cinder_hound", 1 }, { EnemyKind.Switchman, "switchman", 1 }, { EnemyKind.Climber, "climber", 1 },
        { EnemyKind.TrackDoll, "track_doll", 1 }, { EnemyKind.Ribbit, "ribbit", 1.4f }, { EnemyKind.Gaunt, "gaunt", 1 },
        { EnemyKind.Grumbler, "grumbler", 1 }, { EnemyKind.Whistler, "whistler", 1 }, { EnemyKind.Dragger, "dragger", 1 },
        { EnemyKind.Follower, "follower", 1 }, { EnemyKind.SootChildren, "soot_child", 1 }, { EnemyKind.CarHugger, "car_hugger", 1 },
        { EnemyKind.Passenger, "passenger", 1 }, { EnemyKind.Stoker, "stoker", 1 }, { EnemyKind.TippyToesie, "tippy_toesie", 1 },
        { EnemyKind.Moose, "moose", 1 }, { EnemyKind.Gannet, "gannet", 1 }, { EnemyKind.Mourners, "mourner", 1 },
        { EnemyKind.FreightBeetle, "freight_beetle", 1 }, { EnemyKind.TowerJaw, "tower_jaw", 1 },
    };

    /// <summary>Kinds with a body in the sim but no model yet (CreatureArt.Outside.cs draws them as stand-ins till they're
    /// modelled: the Brakeman, the Knotter, Hotbox; notes 364, 365, 367).</summary>
    static readonly EnemyKind[] NotYetModelled = [EnemyKind.Brakeman, EnemyKind.Knotter, EnemyKind.Hotbox];

    [Theory]
    [MemberData(nameof(Drawn))]
    public void EveryBodyStandsInsideItsModel(EnemyKind kind, string model, float scale)
    {
        var m = Art.Get(model) ?? throw new FileNotFoundException($"content/{CreatureArt.Folder}/{model}.glb");
        var body = E.Body(kind);
        Assert.NotEmpty(body);
        float bottom = m.Min.Y * scale, top = m.Max.Y * scale;
        float wide = MathF.Max(m.Max.X - m.Min.X, m.Max.Z - m.Min.Z) * scale / 2;
        foreach (var (radius, height) in body)
        {
            // Each sphere's middle in the figure's height, and no fatter than the figure is across.
            Assert.InRange(height, bottom - 0.01, top + 0.01);
            Assert.InRange(radius, 0.1, wide + 0.15);
        }
        // And the stack goes up it: a ball over its shoulders doesn't pass through its head.
        Assert.True(body.Max(s => s.Height + s.Radius) >= 0.6 * top, $"{kind}'s body stops at {body.Max(s => s.Height + s.Radius):0.00} m of its {top:0.00}");
    }

    [Fact]
    public void EveryCreatureWithABodyIsDrawn()
    {
        var drawn = Drawn().Select(r => r.Data.Item1).OrderBy(k => k);
        Assert.Equal(drawn, E.Bodies.Keys.Select(k => Enum.Parse<EnemyKind>(k, true)).Except(NotYetModelled).OrderBy(k => k));
    }
}
