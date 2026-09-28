using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Seeding;

/// <summary>
/// Fills an empty database with demo receipts when Seed:Enabled is on (the Docker image):
/// the labeled receipts wait in "To review" with their photos, and the made-up ones are confirmed,
/// so the Spending page has a year of data. A database that has any receipts is never touched.
/// </summary>
public static class SeedData
{
    /// <returns>How many receipts were added.</returns>
    public static int Apply(AppDbContext db, SeedOptions options, string contentRoot, string receiptsPath,
        TimeProvider time, ILogger logger)
    {
        if (!options.Enabled)
            return 0;
        if (db.Receipts.Any())
        {
            logger.LogInformation("Seed data skipped: the database already has receipts");
            return 0;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

        var datasets = string.IsNullOrWhiteSpace(options.Path) ? null : Path.GetFullPath(Path.Combine(contentRoot, options.Path));
        var labeled = datasets is null ? [] : LabeledReceipts.Load(datasets);
        if (labeled.Count == 0)
            logger.LogWarning("No labeled receipts found in {Path}; seeding made-up receipts only", datasets ?? "(Seed:Path not set)");

        var folder = Path.GetFullPath(receiptsPath);
        Directory.CreateDirectory(folder);

        foreach (var seed in labeled)
        {
            var receipt = NewReceipt(seed, folder, uploadedAtUtc: now);
            receipt.Flags = ReceiptValidator.Validate(seed.Fields, today);
            receipt.Status = receipt.Flags.Count > 0 ? ReceiptStatus.NeedsReview : ReceiptStatus.Extracted;
            db.Receipts.Add(receipt);
        }

        var samples = SampleReceipts.Generate(today);
        foreach (var seed in samples)
        {
            // "Uploaded" and confirmed at noon Philippine time (04:00 UTC) on the receipt's day, as if added right away.
            var receipt = NewReceipt(seed, folder, uploadedAtUtc: seed.Fields.Date!.Value.ToDateTime(new TimeOnly(4, 0)));
            receipt.Status = ReceiptStatus.Confirmed;
            receipt.ConfirmedAtUtc = receipt.UploadedAtUtc;
            db.Receipts.Add(receipt);
        }

        db.SaveChanges();
        logger.LogInformation("Seeded {Labeled} labeled receipts (to review) and {Samples} made-up receipts (confirmed)",
            labeled.Count, samples.Count);
        return labeled.Count + samples.Count;
    }

    private static ReceiptRecord NewReceipt(SeedReceipt seed, string folder, DateTime uploadedAtUtc)
    {
        var id = Guid.NewGuid();
        var isPng = Path.GetExtension(seed.ImagePath ?? SampleReceipts.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase);
        var imagePath = Path.Combine(folder, $"{id}{(isPng ? ".png" : ".jpg")}");

        if (seed.ImagePath is null)
        {
            using var placeholder = typeof(SeedData).Assembly.GetManifestResourceStream(PlaceholderResource)!;
            using var file = File.Create(imagePath);
            placeholder.CopyTo(file);
        }
        else
        {
            File.Copy(seed.ImagePath, imagePath);
        }

        var receipt = new ReceiptRecord
        {
            Id = id,
            OriginalFileName = seed.FileName,
            ImagePath = imagePath,
            ContentType = isPng ? "image/png" : "image/jpeg",
            UploadedAtUtc = uploadedAtUtc,
        };
        receipt.ApplyFields(seed.Fields, seed.Category);
        return receipt;
    }

    // LogicalName in the csproj: publish.cmd renames the assembly, which would change the default resource name.
    private const string PlaceholderResource = "sample-receipt.png";
}
