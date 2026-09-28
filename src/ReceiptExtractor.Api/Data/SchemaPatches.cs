using Microsoft.EntityFrameworkCore;

namespace ReceiptExtractor.Api.Data;

/// <summary>
/// EnsureCreated() only creates missing tables; it never adds columns to an existing one.
/// Until the project moves to EF Core migrations, columns added later are patched in here,
/// so an existing receipts.db keeps its data instead of having to be deleted.
/// </summary>
public static class SchemaPatches
{
    // SQLite stores decimal as TEXT (see ReceiptRecord); lists of strings are stored as JSON text.
    private static readonly (string Column, string Type)[] AddedColumns =
    [
        (nameof(ReceiptRecord.VatExemptSales), "TEXT NULL"),
        (nameof(ReceiptRecord.Discount), "TEXT NULL"),
        (nameof(ReceiptRecord.LockedFields), "TEXT NOT NULL DEFAULT '[]'"),
    ];

    public static void Apply(AppDbContext db)
    {
        db.Database.OpenConnection();
        try
        {
            var connection = db.Database.GetDbConnection();

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "PRAGMA table_info(\"Receipts\")";
                using var reader = read.ExecuteReader();
                while (reader.Read())
                    existing.Add(reader.GetString(1)); // column 1 = name
            }

            foreach (var (column, type) in AddedColumns.Where(c => !existing.Contains(c.Column)))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"Receipts\" ADD COLUMN \"{column}\" {type}";
                alter.ExecuteNonQuery();
            }
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }
}
