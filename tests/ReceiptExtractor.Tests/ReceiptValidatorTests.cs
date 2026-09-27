using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

public class ReceiptValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);

    [Fact]
    public void Real_DataBlitz_receipt_passes()
    {
        var fields = new ReceiptFields("DataBlitz", new DateOnly(2025, 12, 20), 10495.00m, 1124.46m, 9370.54m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Non_VAT_receipt_skips_the_VAT_check()
    {
        var fields = new ReceiptFields("FLYQUIP ENTERPRISES", new DateOnly(2024, 8, 31), 27840.00m, 0m, 0m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Dropped_centavos_are_flagged()
    {
        // What the vision model returned before the prompt fix: 1124 + 9370 = 10494, not 10495.
        var fields = new ReceiptFields("DataBlitz", new DateOnly(2025, 12, 20), 10495m, 1124m, 9370m);

        var flags = ReceiptValidator.Validate(fields, Today);

        Assert.Contains(flags, f => f.Contains("VATable sales + VAT"));
    }

    [Fact]
    public void Misread_VAT_digits_are_flagged()
    {
        // Tesseract read 1,627.50 as 827.50 and 13,562.50 as 6,552.50.
        var fields = new ReceiptFields("DataBlitz", new DateOnly(2025, 12, 21), 15190m, 827.50m, 6552.50m);

        var flags = ReceiptValidator.Validate(fields, Today);

        Assert.Equal(2, flags.Count);
    }

    [Fact]
    public void Missing_total_and_date_are_flagged()
    {
        var flags = ReceiptValidator.Validate(new ReceiptFields(null, null, null, null, null), Today);

        Assert.Contains(flags, f => f.Contains("Total is missing"));
        Assert.Contains(flags, f => f.Contains("Date is missing"));
    }

    [Fact]
    public void Future_date_is_flagged()
    {
        var fields = new ReceiptFields("WCSI", new DateOnly(2026, 11, 1), 5190m, 0m, 0m);

        var flags = ReceiptValidator.Validate(fields, Today);

        Assert.Contains(flags, f => f.Contains("in the future"));
    }

    [Theory]
    [InlineData(918.00, 98.36)]     // Robinsons
    [InlineData(14895.00, 1595.89)] // DataBlitz
    [InlineData(15190.00, 1627.50)] // DataBlitz
    public void VAT_is_12_percent_of_the_inclusive_total(decimal total, decimal vat)
    {
        var fields = new ReceiptFields("x", new DateOnly(2025, 1, 1), total, vat, total - vat);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }
}
