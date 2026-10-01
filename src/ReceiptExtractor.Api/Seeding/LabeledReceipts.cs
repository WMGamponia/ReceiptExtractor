using System.Text.Json;
using System.Text.Json.Serialization;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Seeding;

/// <summary>
/// The hand-labeled receipts from the eval (datasets/receipts/*.json + the photo with the same name in
/// datasets/labels/). Seeded as "waiting for review" so a reviewer can open a real photo and confirm it.
/// </summary>
public static class LabeledReceipts
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Same shape as the eval's ground truth (and the model's JSON).
    private sealed record Label(
        string? Merchant, DateOnly? Date, decimal? Total,
        [property: JsonPropertyName("vat_amount")] decimal? VatAmount,
        [property: JsonPropertyName("vatable_sales")] decimal? VatableSales,
        [property: JsonPropertyName("vat_exempt_sales")] decimal? VatExemptSales,
        decimal? Discount,
        [property: JsonPropertyName("service_charge")] decimal? ServiceCharge,
        string? Category);

    public static IReadOnlyList<SeedReceipt> Load(string datasetsFolder)
    {
        var labels = Path.Combine(datasetsFolder, "receipts");
        if (!Directory.Exists(labels))
            return [];

        var receipts = new List<SeedReceipt>();
        foreach (var file in Directory.GetFiles(labels, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var image = ImageExtensions
                .Select(ext => Path.Combine(datasetsFolder, "labels", name + ext))
                .FirstOrDefault(File.Exists);
            if (image is null)
                continue; // a label without its photo can't be reviewed

            var label = JsonSerializer.Deserialize<Label>(File.ReadAllText(file), Json)!;
            var fields = new ReceiptFields(label.Merchant, label.Date, label.Total, label.VatAmount, label.VatableSales,
                label.VatExemptSales, label.Discount, label.ServiceCharge);
            receipts.Add(new SeedReceipt(fields, Categories.Normalize(label.Category) ?? "Other", Path.GetFileName(image), image));
        }
        return receipts;
    }
}
