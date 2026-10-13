using Ballast.Dev;
using Ballast.Render;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 517: a review packet's comparison, and the faster texture load that makes rendering the gallery for
/// every pull request affordable. The load is the same bytes as before: the mip chain against the loop it replaced, and the
/// layers worked out side by side against one at a time.
/// </summary>
public class ReviewPacketTests
{
    static Image Noise(int w, int h, int seed)
    {
        var rng = new Random(seed);
        var px = new byte[w * h * 4];
        rng.NextBytes(px);
        return new Image(w, h, px);
    }

    /// <summary>The mip chain as it was before note 517, kept here to hold the new one to it.</summary>
    static List<byte[]> OldMipChain(Image image)
    {
        var chain = new List<byte[]> { image.Rgba };
        int w = image.Width, h = image.Height;
        var level = image.Rgba;
        while (w > 1 || h > 1)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1), y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                        int sum = level[(y0 * w + x0) * 4 + c] + level[(y0 * w + x1) * 4 + c] + level[(y1 * w + x0) * 4 + c] + level[(y1 * w + x1) * 4 + c];
                        next[(y * nw + x) * 4 + c] = (byte)((sum + 2) / 4);
                    }
            chain.Add(next);
            (w, h, level) = (nw, nh, next);
        }
        return chain;
    }

    [Theory]
    [InlineData(256, 256)]
    [InlineData(7, 5)]
    [InlineData(1, 64)]
    [InlineData(300, 2)]
    public void TheMipChainIsTheSameBytesAsBefore(int w, int h)
    {
        var image = Noise(w, h, w * 31 + h);
        var old = OldMipChain(image);
        var now = GpuTexture.MipChain(image);
        Assert.Equal(old.Count, now.Count);
        for (int i = 0; i < old.Count; i++)
            Assert.Equal(old[i], now[i]);
    }

    [Fact]
    public void LayersSideBySideAreTheLayersOneAtATime()
    {
        var images = Enumerable.Range(0, 24).Select(i => Noise(32 + i * 8, 32 + i * 4, i)).ToList();
        var parallel = GpuTexture.MipChains(images, 64);
        Assert.Equal(images.Count, parallel.Count);
        for (int i = 0; i < images.Count; i++)
        {
            var one = GpuTexture.MipChain(images[i].Resized(64, 64));
            Assert.Equal(one.Count, parallel[i].Count);
            for (int m = 0; m < one.Count; m++)
                Assert.Equal(one[m], parallel[i][m]);
        }
    }

    [Fact]
    public void ADiffFindsWhatChangedAndWhere()
    {
        var before = Noise(64, 36, 1);
        var after = new Image(64, 36, (byte[])before.Rgba.Clone());
        Assert.Equal("same", ImageDiff.Compare(before, after).Verdict);
        // Under the tolerance everywhere: noise the eye wouldn't see.
        for (int i = 0; i < after.Rgba.Length; i += 4)
            after.Rgba[i] = (byte)Math.Clamp(after.Rgba[i] + 3, 0, 255);
        Assert.Equal("same", ImageDiff.Compare(before, after).Verdict);
        // A lamp gone out: a 10x6 block from (20, 10) turned black.
        for (int y = 10; y < 16; y++)
            for (int x = 20; x < 30; x++)
            {
                int p = (y * 64 + x) * 4;
                after.Rgba[p] = after.Rgba[p + 1] = after.Rgba[p + 2] = 0;
                before.Rgba[p] = before.Rgba[p + 1] = before.Rgba[p + 2] = 200;
            }
        var r = ImageDiff.Compare(before, after);
        Assert.Equal("changed", r.Verdict);
        Assert.Equal([20, 10, 10, 6], r.Box);
        Assert.Equal(60.0 / (64 * 36), r.Changed, 6);
        Assert.Equal(200, r.Max);
        var strip = ImageDiff.Strip(before, after, r);
        Assert.Equal(32 * 3 + 8, strip.Width);
        Assert.Equal(18, strip.Height);
        // The change is marked in magenta in the third panel.
        int third = (6 * strip.Width + (2 * (32 + 4) + 12)) * 4;
        Assert.Equal([255, 0, 255], strip.Rgba[third..(third + 3)]);
    }

    [Fact]
    public void AShotOfAnotherSizeIsAllChanged()
    {
        var r = ImageDiff.Compare(Noise(8, 8, 1), Noise(16, 8, 1));
        Assert.Equal("changed", r.Verdict);
        Assert.Equal(1, r.Changed);
    }
}
