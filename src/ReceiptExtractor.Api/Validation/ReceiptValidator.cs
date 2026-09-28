namespace ReceiptExtractor.Api.Validation;

/// <summary>
/// Sanity checks on extracted fields. Any flag sends the receipt to review.
/// These are deterministic rules in code, not prompt instructions: the model can't ignore them.
/// Mirrored in web/src/format.ts (vatWarnings) — keep them in sync.
/// </summary>
public static class ReceiptValidator
{
    /// <summary>PH VAT is 12% on top of VATable sales (so VAT = VATable sales × 12%).</summary>
    public const decimal VatRate = 0.12m;

    private const decimal Tolerance = 0.02m; // allow 2 centavos of rounding

    public static List<string> Validate(ReceiptFields f, DateOnly today)
    {
        var flags = new List<string>();

        if (f.Total is null)
            flags.Add("Total is missing.");
        else if (f.Total <= 0)
            flags.Add("Total must be more than zero.");

        if (f.Date is null)
            flags.Add("Date is missing or unreadable.");
        else if (f.Date > today)
            flags.Add($"Date {f.Date:yyyy-MM-dd} is in the future.");

        if (f.Total is { } total)
            flags.AddRange(VatFlags(total, f.VatAmount ?? 0, f.VatableSales ?? 0, f.VatExemptSales ?? 0, f.Discount ?? 0));

        return flags;
    }

    /// <summary>
    /// The VAT breakdown on a PH receipt adds up to the total:
    ///   VATable sales + VAT + VAT-exempt sales = total, or
    ///   ... − discount = total   (senior citizen / PWD discounts are taken off after the breakdown).
    /// Promo discounts are usually applied before the breakdown, so either form is accepted.
    /// </summary>
    public static List<string> VatFlags(decimal total, decimal vat, decimal vatable, decimal exempt, decimal discount)
    {
        var flags = new List<string>();
        if (vat == 0 && vatable == 0 && exempt == 0)
            return flags; // no breakdown printed (non-VAT receipt): nothing to check

        var breakdown = vatable + vat + exempt;
        var adds = Math.Abs(breakdown - total) <= Tolerance
                   || (discount != 0 && Math.Abs(breakdown - discount - total) <= Tolerance);
        if (!adds)
        {
            var parts = exempt != 0 ? "VATable sales + VAT + VAT-exempt sales" : "VATable sales + VAT";
            var less = discount != 0 ? $" ({breakdown - discount:N2} after the {discount:N2} discount)" : "";
            flags.Add($"{parts} = {breakdown:N2}{less}, but total is {total:N2}.");
        }

        var expectedVat = Math.Round(vatable * VatRate, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(vat - expectedVat) > Tolerance)
            flags.Add($"VAT {vat:N2} isn't 12% of VATable sales (expected {expectedVat:N2}). Could be a misread.");

        return flags;
    }
}
