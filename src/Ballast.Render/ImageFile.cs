using StbImageSharp;

namespace Ballast.Render;

/// <summary>Reads PNG (and the other formats stb_image knows) as RGBA8, for textures cooked into content.</summary>
public static class ImageFile
{
    public static Image Load(string path)
    {
        using var stream = File.OpenRead(path);
        var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new Image(result.Width, result.Height, result.Data);
    }
}
