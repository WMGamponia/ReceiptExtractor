using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReceiptExtractor.Api.Data;

namespace ReceiptExtractor.Tests;

public sealed class SchemaPatchesTests : IDisposable
{
    // Columns added to ReceiptRecord after the first receipts.db files were created.
    private static readonly string[] AddedSinceFirstRelease =
        ["VatExemptSales", "Discount", "Corrections", "LockedFields", "ServiceCharge"];

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SchemaPatchesTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>A database as EnsureCreated() made it before the newer columns existed.</summary>
    private void CreateOldDatabase()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        foreach (var column in AddedSinceFirstRelease)
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"Receipts\" DROP COLUMN \"{column}\"");
    }

    [Fact]
    public void Old_database_gets_every_new_column()
    {
        CreateOldDatabase();

        using var db = NewContext();
        SchemaPatches.Apply(db);

        var columns = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Receipts')").ToList();
        Assert.All(AddedSinceFirstRelease, c => Assert.Contains(c, columns));
    }

    [Fact]
    public void Old_rows_can_be_read_and_saved_after_patching()
    {
        CreateOldDatabase();
        var id = Guid.NewGuid();
        using (var db = NewContext())
        {
            db.Database.ExecuteSql($"""
                INSERT INTO "Receipts" ("Id", "Status", "OriginalFileName", "ImagePath", "ContentType", "UploadedAtUtc", "Flags")
                VALUES ({id.ToString().ToUpperInvariant()}, 'Extracted', 'r.jpg', 'r.jpg', 'image/jpeg', '2026-01-01 00:00:00', '[]')
                """);
            SchemaPatches.Apply(db);
        }

        using (var db = NewContext())
        {
            var receipt = db.Receipts.Single(r => r.Id == id);
            Assert.Empty(receipt.Corrections);
            Assert.Empty(receipt.LockedFields);

            receipt.Corrections = ["total: 100.00 → 110.00"];
            db.SaveChanges();
        }

        using (var db = NewContext())
            Assert.Equal(["total: 100.00 → 110.00"], db.Receipts.Single(r => r.Id == id).Corrections);
    }

    [Fact]
    public void Running_twice_is_harmless()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();

        SchemaPatches.Apply(db);
        SchemaPatches.Apply(db);
    }
}
