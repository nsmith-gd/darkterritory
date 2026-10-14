using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The townsfolk lookbook (queue #426, ARCHITECTURE §8 note 694): its ten characters dressed on the survivors' figure, what
/// they wear made to that figure's measured surface and kept cheap, and who in a town is drawn as which.
/// </summary>
public class LookbookArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);

    [Fact]
    public void EachCharacterIsDressedOnTheSurvivorsRigAndPlaysItsClips()
    {
        var figure = Art.Get("survivor_prisoner")!;
        int own = figure.Parts.Where(p => p.Clip is null).Sum(p => p.Triangles);
        Assert.Equal(10, LookbookKit.Characters.Count);
        foreach (var c in LookbookKit.Characters)
        {
            var model = Art.Get(c.Figure);
            Assert.NotNull(model);
            Assert.Equal(figure.Skeleton.Names, model.Skeleton.Names);
            // The coat kept, dyed, for those who wear one; left off for the rest.
            Assert.Equal(c.Clothes.ContainsKey(Cloth.Coat), model.Parts.Any(p => p.Name.EndsWith(".coat", StringComparison.Ordinal)));
            // A character's budget (GDD §27: 4-10k): the figure's triangles less what's left off, the few that close its
            // frame's hole (Redress.Capped), and the hair and beards grown off its face's (Redress's growths).
            int tris = model.Parts.Where(p => p.Clip is null).Sum(p => p.Triangles);
            int grown = model.Parts.Where(p => c.Growths?.Any(g => p.Name.EndsWith("." + g.Name, StringComparison.Ordinal)) == true).Sum(p => p.Triangles);
            Assert.InRange(tris - grown, 2500, own + 32);
            Assert.InRange(tris, 2500, 10000);
            foreach (string clip in (string[])["idle", "walk", "gunner", "crouch_idle", "lantern_walk"])
                Assert.True(Art.Joints(c.Figure, clip, 0.5, true).Any(), $"{c.Id} can't play {clip}");
        }
    }

    [Fact]
    public void EachIsModelledAndBakedOnTheSurvivorsRig()
    {
        // tools/models/recipes/townsfolk_*.py: the ten, their clothes modelled over the survivors' figure and baked with it.
        var figure = Art.Get("survivor_prisoner")!;
        foreach (var c in LookbookKit.Characters)
        {
            var model = Art.Get(c.Modelled);
            Assert.True(model is not null, $"{c.Modelled}.glb isn't built (tools/models/build.sh {c.Modelled})");
            Assert.Equal(c.Modelled, Art.FigureFor(c));
            Assert.Equal(figure.Skeleton.Names, model.Skeleton.Names);
            // A character's budget (GDD §27: 4-10k triangles).
            Assert.InRange(model.Parts.Where(p => p.Clip is null).Sum(p => p.Triangles), 4000, 10000);
            // Nothing modelled flies off the figure (a cloth's thickness thrown out where it folds back on itself, as the
            // lamplighter's cowl once was): in its bind pose, within the T-pose's reach and under two metres.
            foreach (var part in model.Parts.Where(p => p.Clip is null))
                foreach (var v in part.Positions)
                    Assert.True(v.Y < 2.05f && MathF.Sqrt(v.X * v.X + v.Z * v.Z) < 0.95f, $"{c.Modelled} {part.Name}: a vertex at {v}");
            foreach (string clip in (string[])["idle", "walk", "gunner", "crouch_idle", "lantern_walk"])
                Assert.True(Art.Joints(c.Modelled, clip, 0.5, true).Any(), $"{c.Modelled} can't play {clip}");
        }
    }

    [Fact]
    public void WhatTheyWearIsCheapAndSitsOnTheFigure()
    {
        var kit = new LookbookKit(Look);
        var all = kit.All().ToList();
        Assert.NotEmpty(all);
        foreach (var (key, piece) in all)
        {
            Assert.InRange(piece.Triangles, 20, 6000);
            // Worn or held, it's within arm's reach of the figure in its bind pose (the held ones from the fist's origin).
            var (min, max) = ArtCatalog.Bounds(piece);
            Assert.True(min.X > -0.9f && max.X < 0.9f && min.Z > -0.9f && max.Z < 0.9f && max.Y < 2.2f && min.Y > -1.0f, $"{key}: {min} {max}");
        }
        // Each character's all together, less than their figure again.
        foreach (var c in LookbookKit.Characters)
            Assert.InRange(all.Where(p => p.Key.StartsWith(c.Id + ".", StringComparison.Ordinal)).Sum(p => p.Piece.Triangles), 100, 9000);
        // Hats are on the head: above the eyes' line, round the skull (wider than it).
        foreach (string hat in (string[])["trapper.bucket", "watchman.ushanka", "linesman.capgoggles", "preacher.widehat", "butcher.beanie",
            "harbourmaster.captcap", "drifter.flatcap"])
        {
            var (min, max) = ArtCatalog.Bounds(all.Single(p => p.Key == hat).Piece);
            Assert.True(max.Y > 1.78f && max.Y < 1.95f && max.X - min.X > 0.2f, $"{hat}: {min} {max}");
        }
    }

    [Fact]
    public void TheFiguresSurfaceIsMeasuredWhereItIs()
    {
        var figure = Art.Get("survivor_prisoner")!;
        var head = FigureSurface.Head(figure);
        var body = FigureSurface.Body(figure, coat: false, shape: null);
        // The head: about a hand and a half round at the eyes (the face's front 0.12 m out), closing over its crown.
        foreach (float bearing in (float[])[0, MathF.PI / 2, MathF.PI, -MathF.PI / 2])
        {
            Assert.InRange(head.Radius(1.655f, bearing), 0.07f, 0.14f);
            Assert.True(head.Radius(1.8f, bearing) < head.Radius(1.7f, bearing) * 0.6f);
        }
        // The trunk at the waist and at the chest, the shoulders' tops but not the arms out in the T-pose.
        Assert.InRange(body.Radius(0.98f, MathF.PI / 2), 0.14f, 0.26f);
        Assert.InRange(body.Radius(1.3f, MathF.PI), 0.12f, 0.22f);
        Assert.InRange(body.Radius(1.45f, MathF.PI / 2), 0.12f, 0.27f);
        // Over the coat it's wider below the waist.
        Assert.True(FigureSurface.Body(figure, coat: true, shape: null).Radius(0.7f, MathF.PI) > body.Radius(0.7f, MathF.PI));
    }

    [Fact]
    public void ATownsJobsAreDrawnAsTheirCharacters()
    {
        var towns = TownContent.Load(Content)!;
        // Each character's job is one of the towns'; "anyone" is the town's other people.
        foreach (var c in LookbookKit.Characters)
            Assert.All(c.Roles, r => Assert.True(r == "anyone" || towns.Writing.Roles.ContainsKey(r), $"{c.Id}: {r}"));
        Assert.Equal(LookbookKit.Characters.Count, LookbookKit.Characters.Select(c => c.Roles[0]).Distinct().Count());
        Assert.Equal("watchman", LookbookKit.For("guard", 3)?.Id);
        Assert.Equal("lamplighter", LookbookKit.For("lampman", 3)?.Id);
        // A job the lookbook has no one for: the town's own drab figure.
        Assert.Null(LookbookKit.For("widow", 3));
        // The town's other people: one in two dressed, the same every night, spread over those who go about the town.
        var dressed = Enumerable.Range(0, 400).Select(who => LookbookKit.For("anyone", who)).ToList();
        Assert.InRange(dressed.Count(d => d is not null), 140, 260);
        Assert.Equal(dressed, Enumerable.Range(0, 400).Select(who => LookbookKit.For("anyone", who)).ToList());
        Assert.True(dressed.Where(d => d is not null).Select(d => d!.Id).Distinct().Count() >= 4);
        Assert.All(dressed.Where(d => d is not null), d => Assert.Contains("anyone", d!.Roles));
        // What's in the right fist gives way to a hand lamp; the lamplighter's lamp is his pole's.
        Assert.True(LookbookKit.RightHandFull("lamplighter"));
        Assert.False(LookbookKit.RightHandFull("market"));
    }
}
