using System.Globalization;

namespace ReceiptExtractor.Api.Validation;

public static class Peso
{
    /// <summary>10495 -> "₱10,495.00", the same on every PC (N2 alone follows the Windows region, e.g. "10.495,00").</summary>
    public static string Format(decimal amount) =>
        (amount < 0 ? "-₱" : "₱") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);
}
