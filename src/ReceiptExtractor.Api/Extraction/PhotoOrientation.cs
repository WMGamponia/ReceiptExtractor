using SkiaSharp;

namespace ReceiptExtractor.Api.Extraction;

/// <summary>
/// Phones often save a photo sideways plus an EXIF tag saying how to turn it. Browsers obey the tag, so the photo
/// looks fine in the app, but Ollama sends the pixels to the model as stored: it read the receipt on its side and
/// dropped decimal points (89286 for 892.86). This turns such photos upright before they're sent.
/// </summary>
public static class PhotoOrientation
{
    /// <summary>The photo turned upright as a JPEG; the same array if it's already upright or can't be read.</summary>
    public static byte[] Upright(byte[] image)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(image));
        if (codec is null || codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            return image;

        var origin = codec.EncodedOrigin;
        using var stored = SKBitmap.Decode(codec);
        if (stored is null)
            return image;

        float w = stored.Width, h = stored.Height;
        // Maps a stored pixel (x, y) to where it goes upright: x' = scaleX*x + skewX*y + transX, y' = skewY*x + scaleY*y + transY.
        var (matrix, turned) = origin switch
        {
            SKEncodedOrigin.TopRight => (new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1), false),     // mirrored
            SKEncodedOrigin.BottomRight => (new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), false), // upside down
            SKEncodedOrigin.BottomLeft => (new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1), false),   // flipped
            SKEncodedOrigin.LeftTop => (new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1), true),        // mirrored, on its side
            SKEncodedOrigin.RightTop => (new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1), true),      // on its side (most phones)
            SKEncodedOrigin.RightBottom => (new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), true),  // mirrored, on its other side
            SKEncodedOrigin.LeftBottom => (new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1), true),    // on its other side
            _ => (SKMatrix.Identity, false),
        };

        using var upright = turned ? new SKBitmap(stored.Height, stored.Width) : new SKBitmap(stored.Width, stored.Height);
        using (var canvas = new SKCanvas(upright))
        {
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(stored, 0, 0, SKSamplingOptions.Default);
        }

        using var jpeg = upright.Encode(SKEncodedImageFormat.Jpeg, 92);
        return jpeg?.ToArray() ?? image;
    }
}
