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

    [Fact]
    public void Senior_citizen_receipt_passes()
    {
        // ₱560 of medicine: VAT removed (500 VAT-exempt), then 20% SC discount (100) -> ₱400 due.
        var fields = new ReceiptFields("MERCURY DRUG", new DateOnly(2025, 1, 1), 400m, 0m, 0m, 500m, 100m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Mixed_VATable_and_senior_citizen_items_pass()
    {
        // 112 of regular items (100 + 12 VAT) + 560 of SC items (500 exempt − 100 discount).
        var fields = new ReceiptFields("x", new DateOnly(2025, 1, 1), 512m, 12m, 100m, 500m, 100m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Promo_discount_already_inside_the_breakdown_passes()
    {
        // Discount applied before the VAT lines, so the breakdown already adds up to the total.
        var fields = new ReceiptFields("x", new DateOnly(2025, 1, 1), 1000m, 107.14m, 892.86m, 0m, 50m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Breakdown_that_does_not_add_up_is_flagged_even_with_a_discount()
    {
        var fields = new ReceiptFields("x", new DateOnly(2025, 1, 1), 400m, 0m, 0m, 500m, 80m);

        var flags = ReceiptValidator.Validate(fields, Today);

        Assert.Contains(flags, f => f.Contains("VAT-exempt sales") && f.Contains("420.00 after the 80.00 discount"));
    }
}
