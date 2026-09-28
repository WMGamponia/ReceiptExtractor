using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Seeding;

/// <summary>
/// Settings under "Seed". Off by default: only the Docker image turns it on (docker-compose.yml),
/// so a reviewer's fresh install has something to look at. Your own database is never seeded.
/// </summary>
public sealed class SeedOptions
{
    public bool Enabled { get; set; }

    /// <summary>The datasets folder (labels/ + receipts/). Relative paths are relative to the content root.</summary>
    public string? Path { get; set; }
}

/// <summary>One receipt to seed. <paramref name="ImagePath"/> null means "use the sample-receipt placeholder".</summary>
public sealed record SeedReceipt(ReceiptFields Fields, string Category, string FileName, string? ImagePath);
