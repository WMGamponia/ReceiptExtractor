using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ReceiptExtractor.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReceiptRecord> Receipts => Set<ReceiptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<ReceiptRecord>();
        receipt.Property(r => r.Status).HasConversion<string>(); // readable in the DB file
        // A dictionary isn't a primitive collection to EF: store it as JSON text like the string lists.
        receipt.Property(r => r.FieldConfidence).HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, double>>(v, (JsonSerializerOptions?)null) ?? new(),
            new ValueComparer<Dictionary<string, double>>(
                (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                d => d.Aggregate(0, (h, p) => HashCode.Combine(h, p.Key, p.Value)),
                d => new Dictionary<string, double>(d)));
        receipt.HasIndex(r => r.UploadedAtUtc);
        receipt.HasIndex(r => r.Date);
    }
}
