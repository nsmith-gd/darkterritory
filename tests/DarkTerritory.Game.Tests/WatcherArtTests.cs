using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The sign shown a crewmate afoot (World.Watcher, note 327), as something there and not eyes on their own (note 761; the
/// director, 14 Oct, `docs/playtest/2026-10-14.md` line 22: "The floating eyes that are watching me in the dark, they don't
/// really make much sense"): its kind's shape, dark, its feet on the ground under where the sim put its eyes, its eyes in its
/// head at their own size, and as the sign ends it backs off. The Whistler has none to show.
/// </summary>
public class WatcherArtTests
{
    static readonly string Content = Environment.GetEnvironmentVariable("DT_CONTENT") is { Length: > 0 } c ? c : DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly AfootTuning Afoot = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Director.Afoot;

    // Out at the lamp's edge (enemies.json afoot.signOut), off to one side, looked at from a crewmate's eye height.
    static readonly Double3 Eye = new(3, 1.65, 7);

    static Double3 Spot(EnemyKind kind, double out_ = 18) =>
        Eye with { Y = Afoot.SignHeight.GetValueOrDefault(Director.Key(kind), Afoot.SignHeightDefault) } + new Double3(0.6, 0, -out_);

    static MeshBuilder Watched(EnemyKind kind, double left, double out_ = 18)
    {
        var mesh = new MeshBuilder();
        Look.Art.Creatures!.Watching(mesh, Look.Art.Effects, kind, Spot(kind, out_), Eye, left, 3.5);
        return mesh;
    }

    [Theory]
    [InlineData(EnemyKind.Gaunt)]
    [InlineData(EnemyKind.Ribbit)]
    [InlineData(EnemyKind.Grumbler)]
    [InlineData(EnemyKind.SootChildren)]
    public void ItsShapeIsThereDarkUnderItsEyes(EnemyKind kind)
    {
        var mesh = Watched(kind, 2.5);
        // Its shape: one model, all of it dark.
        var body = Assert.Single(mesh.Instances, i => i.Asset.Skin is not null);
        float lum = body.Asset.Vertices.Average(v => (v.Color.X + v.Color.Y + v.Color.Z) / 3);
        Assert.True(lum <= CreatureArt.WatcherShade * 1.6f, $"{kind}'s shape {lum:0.000} bright");
        Assert.All(body.Asset.Vertices, v => Assert.True(v.Emissive <= CreatureArt.WatcherShade + 1e-3f));
        // On the ground under where the sim put its eyes (the eye's relative: the ground is the spot less its kind's height).
        var spot = Spot(kind) - Eye;
        double height = Afoot.SignHeight.GetValueOrDefault(Director.Key(kind), Afoot.SignHeightDefault);
        Assert.Equal(spot.Y - height, body.Model.Translation.Y, 2);
        Assert.True(Vector2.Distance(new(body.Model.Translation.X, body.Model.Translation.Z), new((float)spot.X, (float)spot.Z)) < 0.3f);
        // Its eyes: two pairs of billboards (a core and a halo each), up in its head, toward whoever's looking.
        var eyes = mesh.AdditiveFx;
        Assert.Equal(4 * 6, eyes.Count);
        float top = eyes.Average(v => v.Position.Y);
        Assert.True(top > body.Model.Translation.Y + 0.15f, $"{kind}'s eyes {top - body.Model.Translation.Y:0.00} m off the ground");
        Assert.True(top < body.Model.Translation.Y + height + 0.8f, $"{kind}'s eyes {top - body.Model.Translation.Y:0.00} m off the ground");
    }

    [Fact]
    public void ItsEyesAreTheirOwnSizeAndNoLongerSwellWithDistance()
    {
        // Each billboard's six corners: across it, at 12 m and at 20 m, the core's the same size.
        static float Core(MeshBuilder mesh)
        {
            var quad = mesh.AdditiveFx.Take(6).Select(v => v.Position).ToList();
            return quad.Max(a => quad.Max(b => Vector3.Distance(a, b)));
        }
        float near = Core(Watched(EnemyKind.Gaunt, 2.5, 12)), far = Core(Watched(EnemyKind.Gaunt, 2.5, 20));
        Assert.Equal(near, far, 2);
        Assert.InRange(near, Effects.EyeCore, Effects.EyeCore * 1.5f);
    }

    [Fact]
    public void AsTheSignEndsItBacksOffIntoTheDarkAndItsEyesGoOut()
    {
        float From(MeshBuilder mesh)
        {
            var feet = mesh.Instances.Single(i => i.Asset.Skin is not null).Model.Translation;
            return new Vector2(feet.X, feet.Z).Length();
        }
        float watching = From(Watched(EnemyKind.Gaunt, 2.5)), going = From(Watched(EnemyKind.Gaunt, 0.05));
        Assert.True(going - watching > 1.2f, $"backed off {going - watching:0.00} m");
        Assert.Empty(Watched(EnemyKind.Gaunt, 0.01).AdditiveFx);
        // Shut for the blink, a little after the middle.
        Assert.Empty(Watched(EnemyKind.Gaunt, 3.5 * 0.45).AdditiveFx);
        Assert.NotEmpty(Watched(EnemyKind.Gaunt, 2.5).AdditiveFx);
    }

    [Fact]
    public void TheWhistlerHasNoEyesToShowOnlyItsShape()
    {
        var mesh = Watched(EnemyKind.Whistler, 2.5);
        Assert.Single(mesh.Instances, i => i.Asset.Skin is not null);
        Assert.Empty(mesh.AdditiveFx);
    }

    [Fact]
    public void WhatHasNoShapeHereIsStillItsEyes()
    {
        // A kind without a watching shape (a Follower's, its own eyes on a carrier): the eyes alone, where the sim put them.
        var mesh = Watched(EnemyKind.Follower, 2.5);
        Assert.DoesNotContain(mesh.Instances, i => i.Asset.Skin is not null);
        Assert.Equal(4 * 6, mesh.AdditiveFx.Count);
        var centre = mesh.AdditiveFx.Aggregate(Vector3.Zero, (a, v) => a + v.Position) / mesh.AdditiveFx.Count;
        var spot = Spot(EnemyKind.Follower) - Eye;
        Assert.True(Vector3.Distance(centre, new Vector3((float)spot.X, (float)spot.Y, (float)spot.Z)) < 0.05f);
    }
}
