namespace ReceiptExtractor.Api.Validation;

/// <summary>
/// Sanity checks on extracted fields. Any flag sends the receipt to review.
/// These are deterministic rules in code, not prompt instructions: the model can't ignore them.
/// </summary>
public static class ReceiptValidator
{
    /// <summary>PH receipts are VAT-inclusive at 12%, so VAT = total × 12/112.</summary>
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

        var vat = f.VatAmount ?? 0;
        var vatable = f.VatableSales ?? 0;
        var hasVat = vat != 0 || vatable != 0;

        if (hasVat && f.Total is { } total)
        {
            if (Math.Abs(vatable + vat - total) > Tolerance)
                flags.Add($"VATable sales + VAT = {vatable + vat:N2}, but total is {total:N2}.");

            var expectedVat = Math.Round(total * VatRate / (1 + VatRate), 2);
            if (Math.Abs(vat - expectedVat) > Tolerance)
                flags.Add($"VAT {vat:N2} isn't 12% of the total (expected {expectedVat:N2}). " +
                          "Could be a misread, or a discount / VAT-exempt items.");
        }

        return flags;
    }
}
