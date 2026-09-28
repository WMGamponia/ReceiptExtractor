using System.ComponentModel.DataAnnotations.Schema;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Data;

public enum ReceiptStatus
{
    Queued,       // uploaded, waiting for the extraction worker
    Processing,   // the model is reading it right now
    Extracted,    // fields extracted, all checks passed
    NeedsReview,  // fields extracted, but a check failed (see Flags)
    Confirmed,    // the user reviewed/corrected and saved it
    Failed,       // extraction crashed (see Error)
}

public sealed class ReceiptRecord
{
    public Guid Id { get; set; }
    public ReceiptStatus Status { get; set; }

    public required string OriginalFileName { get; set; }
    public required string ImagePath { get; set; }
    public required string ContentType { get; set; }
    public DateTime UploadedAtUtc { get; set; }

    // Extracted (and later user-corrected) fields.
    // Note: SQLite stores decimal as text, so money sums for summaries are done in C#, not SQL.
    public string? Merchant { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Total { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? VatableSales { get; set; }
    public decimal? VatExemptSales { get; set; }
    public decimal? Discount { get; set; }
    public string? Category { get; set; }

    /// <summary>Problems found by <see cref="ReceiptValidator"/>. Non-empty means "needs review".</summary>
    public List<string> Flags { get; set; } = [];

    /// <summary>Raw JSON the model returned. Compare with the confirmed fields to see what users corrected.</summary>
    public string? ModelOutput { get; set; }

    public double? ExtractionSeconds { get; set; }
    public string? Error { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }

    [NotMapped]
    public ReceiptFields Fields => new(Merchant, Date, Total, VatAmount, VatableSales, VatExemptSales, Discount);

    /// <summary>Record name from SCOPE.md: "&lt;Date&gt;_&lt;Merchant&gt;", "&lt;Date&gt;", or "Undated - &lt;upload time&gt;".</summary>
    [NotMapped]
    public string DisplayName => (Date, Merchant) switch
    {
        (null, _) => $"Undated - {UploadedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
        ({ } d, { Length: > 0 } m) => $"{d:yyyy-MM-dd}_{m}",
        ({ } d, _) => $"{d:yyyy-MM-dd}",
    };

    public void ApplyFields(ReceiptFields f, string? category)
    {
        Merchant = f.Merchant;
        Date = f.Date;
        Total = f.Total;
        VatAmount = f.VatAmount;
        VatableSales = f.VatableSales;
        VatExemptSales = f.VatExemptSales;
        Discount = f.Discount;
        Category = category;
    }
}
