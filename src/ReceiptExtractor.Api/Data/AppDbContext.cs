using Microsoft.EntityFrameworkCore;

namespace ReceiptExtractor.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReceiptRecord> Receipts => Set<ReceiptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var receipt = modelBuilder.Entity<ReceiptRecord>();
        receipt.Property(r => r.Status).HasConversion<string>(); // readable in the DB file
        receipt.HasIndex(r => r.UploadedAtUtc);
        receipt.HasIndex(r => r.Date);
    }
}
