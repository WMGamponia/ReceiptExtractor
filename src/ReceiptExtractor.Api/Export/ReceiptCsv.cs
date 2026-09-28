using System.Globalization;
using System.Text;
using ReceiptExtractor.Api.Data;

namespace ReceiptExtractor.Api.Export;

/// <summary>
/// Receipts as a CSV file for a spreadsheet (e.g. a freelancer's expense sheet). Amounts are plain numbers
/// (1124.46, no peso sign or commas) so the spreadsheet can add them up.
/// </summary>
public static class ReceiptCsv
{
    private static readonly string[] Header =
        ["Date", "Merchant", "Category", "Total", "VAT", "VATable sales", "VAT-exempt sales", "Discount", "Receipt"];

    public static string Write(IEnumerable<ReceiptRecord> receipts)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var r in receipts)
        {
            AppendRow(csv,
            [
                r.Date?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "",
                Text(r.Merchant),
                Text(r.Category),
                Money(r.Total),
                Money(r.VatAmount),
                Money(r.VatableSales),
                Money(r.VatExemptSales),
                Money(r.Discount),
                Text(r.DisplayName),
            ]);
        }
        return csv.ToString();
    }

    /// <summary>UTF-8 with a byte-order mark: without it Excel reads the file as ANSI and "Peñafrancia" comes out garbled.</summary>
    public static byte[] Encode(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    /// <summary>"receipts_2026-09-01_to_2026-09-30.csv" for from = Sep 1, to = Oct 1 (<paramref name="to"/> is exclusive).</summary>
    public static string FileName(DateOnly? from, DateOnly? to)
    {
        static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = to?.AddDays(-1);
        return (from, last) switch
        {
            ({ } f, { } l) when f == l => $"receipts_{Day(f)}.csv",
            ({ } f, { } l) => $"receipts_{Day(f)}_to_{Day(l)}.csv",
            ({ } f, null) => $"receipts_from_{Day(f)}.csv",
            (null, { } l) => $"receipts_to_{Day(l)}.csv",
            _ => "receipts.csv",
        };
    }

    private static string Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// A cell starting with = + - @ would be run as a formula by Excel / Google Sheets ("CSV injection"), and merchant
    /// names come from the model reading a photo. A leading apostrophe makes the spreadsheet show it as plain text.
    /// </summary>
    private static string Text(string? value) =>
        value is { Length: > 0 } && "=+-@\t\r".Contains(value[0]) ? "'" + value : value ?? "";

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells)
    {
        csv.AppendJoin(',', cells.Select(Quote));
        csv.Append("\r\n"); // RFC 4180 line ending
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
