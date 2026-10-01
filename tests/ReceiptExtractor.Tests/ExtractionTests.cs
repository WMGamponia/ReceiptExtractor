using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

public class MerchantRulesTests
{
    [Theory]
    [InlineData("LAZADA Edifier", "LAZADA")]
    [InlineData("Lazada", "LAZADA")]
    [InlineData("Shopee - GadgetHub PH", "SHOPEE")]
    [InlineData("  DATA   BLITZ ", "DATA BLITZ")]
    [InlineData("THE MARKETPLACE", "THE MARKETPLACE")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalizes_merchant_names(string? input, string? expected) =>
        Assert.Equal(expected, MerchantRules.Normalize(input));
}

public class ParseModelOutputTests
{
    [Fact]
    public void Parses_a_normal_answer()
    {
        const string json = """
            {"merchant":"LAZADA Edifier","date":"2023-12-09","total":8443,"vat_amount":0,"vatable_sales":0,"category":"other"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Equal("LAZADA", result.Fields.Merchant);
        Assert.Equal(new DateOnly(2023, 12, 9), result.Fields.Date);
        Assert.Equal(8443m, result.Fields.Total);
        Assert.Equal("Other", result.Category);
        Assert.Equal(json, result.ModelOutput);
    }

    [Fact]
    public void Unreadable_values_become_null()
    {
        const string json = """
            {"merchant":null,"date":"20/12/2025","total":null,"vat_amount":null,"vatable_sales":null,"category":"Gadgets"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Null(result.Fields.Merchant);
        Assert.Null(result.Fields.Date);   // wrong format -> null, so the validator flags it
        Assert.Null(result.Fields.Total);
        Assert.Null(result.Category);      // not one of the SCOPE.md categories
    }

    [Fact]
    public void Numbers_as_strings_are_accepted_and_rounded()
    {
        const string json = """
            {"merchant":"X","date":"2025-01-01","total":"10495.004","vat_amount":1124.456,"vatable_sales":9370.54,"category":"Food"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Equal(10495.00m, result.Fields.Total);
        Assert.Equal(1124.46m, result.Fields.VatAmount);
    }

    [Fact]
    public void Reads_VAT_exempt_sales_and_makes_discounts_positive()
    {
        const string json = """
            {"merchant":"Mercury Drug","date":"2025-01-01","total":400,"vat_amount":0,"vatable_sales":0,"vat_exempt_sales":500,"discount":-100,"category":"Health"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Equal(500m, result.Fields.VatExemptSales);
        Assert.Equal(100m, result.Fields.Discount);
    }

    [Fact]
    public void Older_answers_without_the_new_fields_still_parse()
    {
        const string json = """
            {"merchant":"X","date":"2025-01-01","total":100,"vat_amount":0,"vatable_sales":0,"category":"Food"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Null(result.Fields.VatExemptSales);
        Assert.Null(result.Fields.Discount);
        Assert.Null(result.Fields.ServiceCharge);
    }

    [Fact]
    public void Reads_the_service_charge_as_a_positive_amount()
    {
        const string json = """
            {"merchant":"TEXAS ROADHOUSE","date":"2026-08-08","total":6323.43,"vat_amount":361.34,"vatable_sales":3011.16,"vat_exempt_sales":3011.16,"discount":602.23,"service_charge":-542.01,"category":"Food"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Equal(542.01m, result.Fields.ServiceCharge);
    }

    [Fact]
    public void Invalid_json_throws_a_clear_error()
    {
        var ex = Assert.Throws<ExtractionException>(() => OllamaExtractor.ParseModelOutput("Sure! Here is the receipt"));

        Assert.Contains("invalid JSON", ex.Message);
    }
}

public class DisplayNameTests
{
    private static ReceiptRecord Receipt(string? merchant, DateOnly? date) => new()
    {
        OriginalFileName = "r.jpg",
        ImagePath = "r.jpg",
        ContentType = "image/jpeg",
        UploadedAtUtc = new DateTime(2026, 9, 27, 1, 30, 0, DateTimeKind.Utc),
        Merchant = merchant,
        Date = date,
    };

    [Fact]
    public void Date_and_merchant() =>
        Assert.Equal("2025-12-20_DataBlitz", Receipt("DataBlitz", new DateOnly(2025, 12, 20)).DisplayName);

    [Fact]
    public void Date_only_when_merchant_is_missing() =>
        Assert.Equal("2025-12-20", Receipt(null, new DateOnly(2025, 12, 20)).DisplayName);

    [Fact]
    public void Undated_when_date_is_missing()
    {
        var receipt = Receipt("DataBlitz", null);
        var expected = $"Undated - {receipt.UploadedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"; // shown in local time

        Assert.Equal(expected, receipt.DisplayName);
    }
}
