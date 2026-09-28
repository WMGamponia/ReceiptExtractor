namespace ReceiptExtractor.Api.Endpoints;

/// <summary>What counts as a receipt photo: for uploads, replacing an image, and the live demo.</summary>
public static class UploadRules
{
    public const long MaxBytes = 15 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, (string Ext, string ContentType)> AllowedFiles =
        new Dictionary<string, (string Ext, string ContentType)>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = (".jpg", "image/jpeg"),
            [".jpeg"] = (".jpg", "image/jpeg"),
            [".png"] = (".png", "image/png"),
        };

    /// <summary>Checks the file's first bytes: JPEG starts with FF D8 FF, PNG with 89 50 4E 47.</summary>
    public static async Task<bool> LooksLikeImageAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[4];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 3) return false;
        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = read == 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        return isJpeg || isPng;
    }
}
