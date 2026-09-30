using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Seeding;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

public class SampleReceiptsTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Every_sample_passes_the_receipt_checks()
    {
        // Made-up receipts must look like clean, confirmed ones: no review flags.
        Assert.All(SampleReceipts.Generate(Today), r => Assert.Empty(ReceiptValidator.Validate(r.Fields, Today)));
    }

    [Fact]
    public void Covers_every_category()
    {
        var categories = SampleReceipts.Generate(Today).Select(r => r.Category).Distinct();
        Assert.Equal(Categories.All.Order(), categories.Order());
    }

    [Fact]
    public void Spread_over_the_last_12_months()
    {
        var dates = SampleReceipts.Generate(Today).Select(r => r.Fields.Date!.Value).ToList();

        Assert.All(dates, d => Assert.InRange(d, Today.AddDays(-364), Today));
        Assert.True(dates.Select(d => (d.Year, d.Month)).Distinct().Count() >= 10, "most months should have receipts");
    }

    [Fact]
    public void Same_receipts_every_time()
    {
        Assert.Equal(SampleReceipts.Generate(Today), SampleReceipts.Generate(Today));
    }

    [Fact]
    public void Includes_a_non_VAT_receipt_and_a_senior_citizen_discount()
    {
        var samples = SampleReceipts.Generate(Today);

        Assert.Contains(samples, r => r.Fields.VatAmount is null); // sari-sari store
        Assert.Contains(samples, r => r.Fields.Discount > 0 && r.Fields.VatExemptSales > 0);
    }
}

public class LabeledReceiptsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Datasets([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "datasets"));

    [Fact]
    public void Loads_every_labeled_receipt_in_the_repo_with_its_image()
    {
        var receipts = LabeledReceipts.Load(Datasets());

        Assert.Equal(Directory.GetFiles(Path.Combine(Datasets(), "receipts"), "*.json").Length, receipts.Count);
        Assert.All(receipts, r =>
        {
            Assert.True(File.Exists(r.ImagePath), r.ImagePath);
            Assert.Contains(r.Category, Categories.All);
            Assert.Empty(ReceiptValidator.Validate(r.Fields, new DateOnly(2026, 9, 30)));
        });
    }

    [Fact]
    public void Reads_the_label_fields()
    {
        var dataBlitz = LabeledReceipts.Load(Datasets()).Single(r => r.FileName == "20251220_DataBlitz.jpg");

        Assert.Equal(new ReceiptFields("DataBlitz", new DateOnly(2025, 12, 20), 10495.00m, 1124.46m, 9370.54m, 0m, 0m), dataBlitz.Fields);
        Assert.Equal("Shopping", dataBlitz.Category);
    }

    [Fact]
    public void Skips_labels_without_an_image()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "receipts"));
        File.WriteAllText(Path.Combine(_folder, "receipts", "20250101_Lost.json"), """{ "merchant": "Lost", "total": 1 }""");

        Assert.Empty(LabeledReceipts.Load(_folder));
    }

    [Fact]
    public void Missing_folder_gives_nothing()
    {
        Assert.Empty(LabeledReceipts.Load(Path.Combine(_folder, "nope")));
    }
}

public sealed class SeedDataTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 4, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _storage = Directory.CreateTempSubdirectory().FullName;

    public SeedDataTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        Directory.Delete(_storage, recursive: true);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private static string Datasets([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "datasets"));

    private int Seed(bool enabled = true, string? path = null)
    {
        using var db = NewContext();
        return SeedData.Apply(db, new SeedOptions { Enabled = enabled, Path = path ?? Datasets() },
            contentRoot: _storage, receiptsPath: _storage, new FixedTime(Now), NullLogger.Instance);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void Does_nothing_unless_enabled()
    {
        Assert.Equal(0, Seed(enabled: false));

        using var db = NewContext();
        Assert.Empty(db.Receipts);
    }

    [Fact]
    public void Labeled_receipts_wait_in_review_with_their_photos()
    {
        Seed();

        using var db = NewContext();
        var labeled = db.Receipts.Where(r => r.OriginalFileName != SampleReceipts.FileName).ToList();
        Assert.Equal(LabeledReceipts.Load(Datasets()).Count, labeled.Count);
        Assert.All(labeled, r =>
        {
            Assert.Equal(ReceiptStatus.Extracted, r.Status);
            Assert.Equal(Path.Combine(_storage, $"{r.Id}.jpg"), r.ImagePath);
            Assert.True(File.Exists(r.ImagePath));
            Assert.Equal("image/jpeg", r.ContentType);
        });
    }

    [Fact]
    public void Sample_receipts_are_confirmed_with_a_placeholder_image()
    {
        Seed();

        using var db = NewContext();
        var samples = db.Receipts.Where(r => r.OriginalFileName == SampleReceipts.FileName).ToList();
        Assert.Equal(SampleReceipts.Generate(DateOnly.FromDateTime(Now.UtcDateTime)).Count, samples.Count);
        Assert.All(samples, r =>
        {
            Assert.Equal(ReceiptStatus.Confirmed, r.Status);
            Assert.NotNull(r.ConfirmedAtUtc);
            Assert.Equal("image/png", r.ContentType);
            Assert.True(new FileInfo(r.ImagePath).Length > 0);
        });
    }

    [Fact]
    public void Never_touches_a_database_that_has_receipts()
    {
        Assert.True(Seed() > 0);

        Assert.Equal(0, Seed());
        using var db = NewContext();
        Assert.Equal(SampleReceipts.Generate(DateOnly.FromDateTime(Now.UtcDateTime)).Count + LabeledReceipts.Load(Datasets()).Count,
            db.Receipts.Count());
    }

    [Fact]
    public void Without_the_datasets_folder_seeds_only_samples()
    {
        var seeded = Seed(path: Path.Combine(_storage, "missing"));

        Assert.Equal(SampleReceipts.Generate(DateOnly.FromDateTime(Now.UtcDateTime)).Count, seeded);
    }
}
