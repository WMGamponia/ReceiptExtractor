using ReceiptExtractor.Api.Endpoints;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

public class UserHintsTests
{
    private static readonly ReceiptFields User =
        new("JOLLIBEE", new DateOnly(2026, 9, 24), 443.86m, 23.14m, 192.86m, 284.82m, 56.96m);

    [Fact]
    public void Describes_only_locked_fields_in_prompt_order()
    {
        var hints = UserHints.Describe(User, "Food", ["vat_amount", "total", "vat_amount"]);

        Assert.Equal(new[] { new FieldHint("total", "443.86"), new FieldHint("vat_amount", "23.14") }, hints);
    }

    [Fact]
    public void Unknown_field_names_are_ignored()
    {
        Assert.Empty(UserHints.Describe(User, "Food", ["tip"]));
    }

    [Fact]
    public void Missing_values_are_described_plainly()
    {
        var hints = UserHints.Describe(new ReceiptFields(null, null, null, null, null), null, ["merchant", "discount"]);

        Assert.Equal(new[] { new FieldHint("merchant", "(not on the receipt)"), new FieldHint("discount", "0.00") }, hints);
    }

    [Fact]
    public void Prompt_lists_the_values_and_is_empty_without_hints()
    {
        Assert.Equal("", UserHints.Prompt([]));

        var prompt = UserHints.Prompt([new FieldHint("total", "443.86")]);

        Assert.Contains("They are correct:", prompt);
        Assert.Contains("- total: 443.86", prompt);
    }

    [Fact]
    public void Locked_fields_keep_the_users_value()
    {
        // The model misread VAT as the "Less 12% VAT" line; the user fixed it and locked it.
        var model = new ReceiptFields("JOLLIBEE", new DateOnly(2026, 9, 24), 443.86m, 34.18m, 192.86m, 284.82m, 56.96m);
        var user = model with { VatAmount = 23.14m, Merchant = "Jollibee Foods" };

        var kept = UserHints.Keep(model, user, new HashSet<string> { "vat_amount" });

        Assert.Equal(23.14m, kept.VatAmount);
        Assert.Equal("JOLLIBEE", kept.Merchant); // not locked, so the model's answer stands
    }

    [Fact]
    public void Service_charge_can_be_locked()
    {
        var model = new ReceiptFields("X", new DateOnly(2026, 8, 8), 1220m, 120m, 1000m, ServiceCharge: 0m);
        var user = model with { ServiceCharge = 100m };

        Assert.Equal(new[] { new FieldHint("service_charge", "100.00") }, UserHints.Describe(user, null, ["service_charge"]));
        Assert.Equal(100m, UserHints.Keep(model, user, new HashSet<string> { "service_charge" }).ServiceCharge);
    }

    [Fact]
    public void Re_extract_rejects_a_negative_service_charge()
    {
        var update = new ReceiptUpdate("X", new DateOnly(2026, 8, 8), 1220m, 120m, 1000m, null, null, "Food", ServiceCharge: -100m);

        var (valid, errors) = new ReextractRequest(update, ["service_charge"]).Check();

        Assert.Null(valid);
        Assert.Equal(new[] { "Enter the service charge as a positive amount." }, errors["serviceCharge"]);
    }
}
