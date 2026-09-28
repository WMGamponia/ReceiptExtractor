using System.Globalization;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Extraction;

/// <summary>A value the user checked against the receipt, passed to the model as known-correct.</summary>
public sealed record FieldHint(string Field, string Value);

/// <summary>
/// "Re-extract with my changes": fields the user corrected are locked. The model is told their values
/// (so it can use them to read the rest, e.g. a correct total fixes the VAT breakdown), and whatever it
/// returns for a locked field is replaced by the user's value.
/// Field names are the same as the model's JSON (merchant, date, total, vat_amount, ...).
/// </summary>
public static class UserHints
{
    public const string Merchant = "merchant";
    public const string Date = "date";
    public const string Total = "total";
    public const string VatAmount = "vat_amount";
    public const string VatableSales = "vatable_sales";
    public const string VatExemptSales = "vat_exempt_sales";
    public const string Discount = "discount";
    public const string Category = "category";

    public static readonly string[] Fields =
        [Merchant, Date, Total, VatAmount, VatableSales, VatExemptSales, Discount, Category];

    public static bool IsKnown(string field) => Fields.Contains(field);

    /// <summary>The locked fields with the user's values, in prompt order.</summary>
    public static IReadOnlyList<FieldHint> Describe(ReceiptFields user, string? category, IEnumerable<string> locked) =>
        locked.Where(IsKnown).Distinct()
            .OrderBy(f => Array.IndexOf(Fields, f))
            .Select(f => new FieldHint(f, Format(f, user, category)))
            .ToList();

    /// <summary>Text appended to the extraction prompt. Empty when there are no hints.</summary>
    public static string Prompt(IReadOnlyList<FieldHint> hints)
    {
        if (hints.Count == 0)
            return "";

        var lines = new List<string>
        {
            "",
            "The owner of this receipt has checked these values against it. They are correct:",
        };
        lines.AddRange(hints.Select(h => $"- {h.Field}: {h.Value}"));
        lines.Add("Return them exactly as given, and use them to read the other fields correctly");
        lines.Add("(VATable sales + VAT + VAT-exempt sales must add up to the total, before or after the discount).");
        return string.Join('\n', lines);
    }

    /// <summary>What the model returned, with the user's value for every locked field.</summary>
    public static ReceiptFields Keep(ReceiptFields model, ReceiptFields user, IReadOnlySet<string> locked) => new(
        locked.Contains(Merchant) ? user.Merchant : model.Merchant,
        locked.Contains(Date) ? user.Date : model.Date,
        locked.Contains(Total) ? user.Total : model.Total,
        locked.Contains(VatAmount) ? user.VatAmount : model.VatAmount,
        locked.Contains(VatableSales) ? user.VatableSales : model.VatableSales,
        locked.Contains(VatExemptSales) ? user.VatExemptSales : model.VatExemptSales,
        locked.Contains(Discount) ? user.Discount : model.Discount);

    private const string NotPrinted = "(not on the receipt)";

    private static string Format(string field, ReceiptFields f, string? category) => field switch
    {
        Merchant => string.IsNullOrWhiteSpace(f.Merchant) ? NotPrinted : f.Merchant,
        Date => f.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? NotPrinted,
        Total => f.Total is { } total ? Money(total) : NotPrinted,
        VatAmount => Money(f.VatAmount ?? 0),
        VatableSales => Money(f.VatableSales ?? 0),
        VatExemptSales => Money(f.VatExemptSales ?? 0),
        Discount => Money(f.Discount ?? 0),
        Category => category ?? "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field."),
    };

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
