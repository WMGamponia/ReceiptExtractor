using System.Globalization;
using System.Text;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Export;

namespace ReceiptExtractor.Tests;

public class ReceiptCsvTests
{
    private static ReceiptRecord Receipt(string? merchant = "DataBlitz", decimal? total = 10495m, decimal? vat = 1124.46m,
        decimal? vatable = 9370.54m, decimal? exempt = null, decimal? discount = null, string? category = "Shopping",
        decimal? serviceCharge = null) => new()
    {
        OriginalFileName = "IMG_0001.jpg",
        ImagePath = "x.jpg",
        ContentType = "image/jpeg",
        Status = ReceiptStatus.Confirmed,
        Merchant = merchant,
        Date = new DateOnly(2025, 12, 20),
        Total = total,
        VatAmount = vat,
        VatableSales = vatable,
        VatExemptSales = exempt,
        Discount = discount,
        ServiceCharge = serviceCharge,
        Category = category,
    };

    private static string[] Lines(string csv) => csv.Split("\r\n");

    [Fact]
    public void Has_a_header_and_one_row_per_receipt()
    {
        var lines = Lines(ReceiptCsv.Write([Receipt(), Receipt()]));

        Assert.Equal("Date,Merchant,Category,Total,VAT,VATable sales,VAT-exempt sales,Discount,Service charge,Receipt", lines[0]);
        Assert.Equal("20251220,DataBlitz,Shopping,10495.00,1124.46,9370.54,,,,2025-12-20_DataBlitz", lines[1]);
        Assert.Equal(lines[1], lines[2]);
        Assert.Equal(4, lines.Length);
        Assert.Equal("", lines[3]); // ends with a line break
    }

    [Fact]
    public void Amounts_are_plain_numbers_whatever_the_PC_region()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // would write 10495,00 and break the columns
        try
        {
            var row = Lines(ReceiptCsv.Write([Receipt(total: 1234567.8m, exempt: 500m, discount: 100m, serviceCharge: 50m)]))[1];

            Assert.Contains(",1234567.80,1124.46,9370.54,500.00,100.00,50.00,", row);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Missing_values_are_empty_cells()
    {
        var row = Lines(ReceiptCsv.Write([Receipt(merchant: null, vat: null, vatable: null, category: null)]))[1];

        Assert.Equal("20251220,,,10495.00,,,,,,2025-12-20", row);
    }

    [Theory]
    [InlineData("S&R, Inc.", "\"S&R, Inc.\"")]
    [InlineData("Jollibee \"Ayala\"", "\"Jollibee \"\"Ayala\"\"\"")]
    [InlineData("Line\nbreak", "\"Line\nbreak\"")]
    [InlineData("Peñafrancia Store", "Peñafrancia Store")]
    public void Text_is_quoted_when_needed(string merchant, string expected) =>
        Assert.StartsWith($"20251220,{expected},", Lines(ReceiptCsv.Write([Receipt(merchant: merchant)]))[1]);

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+63 STORE", "'+63 STORE")]
    [InlineData("-SALE-", "'-SALE-")]
    [InlineData("@home", "'@home")]
    public void Text_that_a_spreadsheet_would_run_as_a_formula_is_defused(string merchant, string expected) =>
        Assert.StartsWith($"20251220,{expected},", Lines(ReceiptCsv.Write([Receipt(merchant: merchant)]))[1]);

    [Fact]
    public void Encodes_as_UTF8_with_a_BOM_so_Excel_shows_ñ()
    {
        var bytes = ReceiptCsv.Encode("Peñafrancia");

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("Peñafrancia", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("2026-09-01", "2026-10-01", "receipts_2026-09-01_to_2026-09-30.csv")] // "to" is exclusive
    [InlineData("2026-09-28", "2026-09-29", "receipts_2026-09-28.csv")]
    [InlineData("2026-01-01", null, "receipts_from_2026-01-01.csv")]
    [InlineData(null, "2026-01-01", "receipts_to_2025-12-31.csv")]
    [InlineData(null, null, "receipts.csv")]
    public void File_name_shows_the_dates_covered(string? from, string? to, string expected) =>
        Assert.Equal(expected, ReceiptCsv.FileName(Parse(from), Parse(to)));

    private static DateOnly? Parse(string? s) => s is null ? null : DateOnly.Parse(s, CultureInfo.InvariantCulture);
}
