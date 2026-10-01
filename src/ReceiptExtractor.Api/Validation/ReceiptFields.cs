namespace ReceiptExtractor.Api.Validation;

/// <summary>The fields we extract from a receipt. Same shape as the Python eval's schema.</summary>
public sealed record ReceiptFields(
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? VatAmount,
    decimal? VatableSales,
    decimal? VatExemptSales = null, // e.g. senior citizen / PWD purchases
    decimal? Discount = null,       // all discounts on the receipt added up, as a positive amount
    decimal? ServiceCharge = null); // e.g. a restaurant's 10% service charge, as a positive amount

/// <summary>Categories from SCOPE.md.</summary>
public static class Categories
{
    // Only ever add: receipts store the category as text, so renaming one would orphan existing receipts.
    // Keep in sync with the schema enum in OllamaExtractor and CATEGORIES in web/src/api.ts.
    public static readonly string[] All = ["Food", "Groceries", "Transport", "Fuel", "Health", "Utilities", "Shopping", "Other"];

    /// <summary>Case-insensitive match to a known category; anything else becomes null.</summary>
    public static string? Normalize(string? value) =>
        All.FirstOrDefault(c => string.Equals(c, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
