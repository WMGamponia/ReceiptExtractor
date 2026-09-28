namespace ReceiptExtractor.Api.Validation;

/// <summary>The fields we extract from a receipt. Same shape as the Python eval's schema.</summary>
public sealed record ReceiptFields(
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? VatAmount,
    decimal? VatableSales,
    decimal? VatExemptSales = null, // e.g. senior citizen / PWD purchases
    decimal? Discount = null);      // all discounts on the receipt added up, as a positive amount

/// <summary>Categories from SCOPE.md.</summary>
public static class Categories
{
    public static readonly string[] All = ["Food", "Transport", "Groceries", "Health", "Utilities", "Other"];

    /// <summary>Case-insensitive match to a known category; anything else becomes null.</summary>
    public static string? Normalize(string? value) =>
        All.FirstOrDefault(c => string.Equals(c, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
