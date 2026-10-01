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
            flags.AddRange(VatFlags(total, f.VatAmount ?? 0, f.VatableSales ?? 0, f.VatExemptSales ?? 0, f.Discount ?? 0,
                f.ServiceCharge ?? 0));

        return flags;
    }

    /// <summary>
    /// The VAT breakdown on a PH receipt adds up to the total:
    ///   VATable sales + VAT + VAT-exempt sales = total, or
    ///   ... − discount = total   (senior citizen / PWD discounts are taken off after the breakdown), and/or
    ///   ... + service charge = total   (restaurants often add it after the breakdown).
    /// Promo discounts and some service charges are already inside the breakdown, so every combination is accepted.
    /// </summary>
    public static List<string> VatFlags(decimal total, decimal vat, decimal vatable, decimal exempt, decimal discount,
        decimal serviceCharge = 0)
    {
        var flags = new List<string>();
        if (vat == 0 && vatable == 0 && exempt == 0)
            return flags; // no breakdown printed (non-VAT receipt): nothing to check

        var breakdown = vatable + vat + exempt;
        decimal[] discounts = discount != 0 ? [0, discount] : [0];
        decimal[] charges = serviceCharge != 0 ? [0, serviceCharge] : [0];
        var adds = discounts.Any(d => charges.Any(s => Math.Abs(breakdown - d + s - total) <= Tolerance));
        if (!adds)
        {
            var parts = exempt != 0 ? "VATable sales + VAT + VAT-exempt sales" : "VATable sales + VAT";
            var tried = new List<string>();
            if (discount != 0)
                tried.Add($"{Peso.Format(breakdown - discount)} after the {Peso.Format(discount)} discount");
            if (serviceCharge != 0)
                tried.Add($"{Peso.Format(breakdown - discount + serviceCharge)} with the {Peso.Format(serviceCharge)} service charge");
            var less = tried.Count > 0 ? $" ({string.Join(", ", tried)})" : "";
            flags.Add($"{parts} = {Peso.Format(breakdown)}{less}, but total is {Peso.Format(total)}.");
        }

        var expectedVat = Math.Round(vatable * VatRate, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(vat - expectedVat) > Tolerance)
            flags.Add($"VAT {Peso.Format(vat)} isn't 12% of VATable sales (expected {Peso.Format(expectedVat)}). Could be a misread.");

        return flags;
    }
}
