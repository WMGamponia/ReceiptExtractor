using SkiaSharp;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>
/// Phones save photos sideways with an EXIF "rotate me" tag. Browsers obey it; Ollama doesn't, so the model read
/// the receipt on its side and dropped decimal points (89286 for 892.86). The fixtures are 40x20 with the left half
/// black; sideways-exif6.jpg also carries orientation 6 (turn 90° clockwise), so upright it's 20x40, black on top.
/// </summary>
public sealed class PhotoOrientationTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Turns_a_sideways_phone_photo_upright()
    {
        var upright = PhotoOrientation.Upright(Fixture("sideways-exif6.jpg"));

        using var bitmap = SKBitmap.Decode(upright);
        Assert.Equal(20, bitmap.Width);
        Assert.Equal(40, bitmap.Height);
        Assert.True(bitmap.GetPixel(10, 5).Red < 50, "top should be black");
        Assert.True(bitmap.GetPixel(10, 35).Red > 200, "bottom should be white");
    }

    [Fact]
    public void Leaves_an_upright_photo_as_it_is()
    {
        var photo = Fixture("upright.jpg");

        Assert.Same(photo, PhotoOrientation.Upright(photo));
    }

    [Fact]
    public void Leaves_bytes_it_cannot_read_as_they_are()
    {
        byte[] notAnImage = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        Assert.Same(notAnImage, PhotoOrientation.Upright(notAnImage));
    }
}
