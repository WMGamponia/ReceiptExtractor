using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Seeding;

/// <summary>
/// Made-up receipts so the Spending page has a year of data to show on a fresh install: every category,
/// spread over the 12 months before today, with PH merchants and amounts. The same list every time for a
/// given day (fixed random seed). There are no photos; each gets a copy of sample-receipt.png.
/// </summary>
public static class SampleReceipts
{
    public const string FileName = "sample-receipt.png";

    private const int Count = 40;

    private sealed record Store(string Merchant, string Category, decimal Min, decimal Max, bool Vat = true);

    private static readonly Store[] Stores =
    [
        new("Jollibee", "Food", 150, 900),
        new("Mang Inasal", "Food", 180, 750),
        new("Chowking", "Food", 150, 800),
        new("Starbucks", "Food", 180, 650),
        new("SM Supermarket", "Groceries", 800, 4500),
        new("Puregold", "Groceries", 500, 3500),
        new("Aling Nena Sari-Sari Store", "Groceries", 60, 400, Vat: false), // no VAT breakdown on the receipt
        new("Grab", "Transport", 150, 650),
        new("Victory Liner", "Transport", 450, 900),
        new("Petron", "Fuel", 1000, 3000),
        new("Shell", "Fuel", 800, 2500),
        new("Mercury Drug", "Health", 200, 2500),
        new("Watsons", "Health", 150, 1200),
        new("Meralco", "Utilities", 1800, 5500),
        new("Maynilad", "Utilities", 400, 1200),
        new("PLDT Home", "Utilities", 1699, 1699),
        new("Lazada", "Shopping", 300, 4000),
        new("Shopee", "Shopping", 200, 2500),
        new("Uniqlo", "Shopping", 790, 2990),
        new("National Book Store", "Other", 150, 1200),
        new("LBC Express", "Other", 180, 650),
    ];

    public static IReadOnlyList<SeedReceipt> Generate(DateOnly today)
    {
        var random = new Random(20260928); // seeded Random gives the same sequence on every .NET version
        var receipts = new List<SeedReceipt>();

        for (var i = 0; i < Count; i++)
        {
            // One store per category first, so every category shows up; then any store.
            var store = i < Categories.All.Length
                ? Stores.First(s => s.Category == Categories.All[i])
                : Stores[random.Next(Stores.Length)];
            var date = today.AddDays(-random.Next(365));
            var price = Math.Round(store.Min + (decimal)random.NextDouble() * (store.Max - store.Min)) + random.Next(4) * 0.25m;

            receipts.Add(new SeedReceipt(Fields(store, date, price, seniorCitizen: store.Category == "Health" && random.Next(3) == 0),
                store.Category, FileName, ImagePath: null));
        }
        return receipts;
    }

    private static ReceiptFields Fields(Store store, DateOnly date, decimal price, bool seniorCitizen)
    {
        if (seniorCitizen)
        {
            // Senior citizen / PWD: VAT-exempt, then 20% off (VATable sales + VAT + exempt − discount = total).
            var discount = Math.Round(price * 0.20m, 2, MidpointRounding.AwayFromZero);
            return new ReceiptFields(store.Merchant, date, price - discount, null, null, VatExemptSales: price, Discount: discount);
        }
        if (!store.Vat)
            return new ReceiptFields(store.Merchant, date, price, null, null);

        // 12% VAT is included in PH totals: VAT = total × 12/112.
        var vat = Math.Round(price * 12m / 112m, 2, MidpointRounding.AwayFromZero);
        return new ReceiptFields(store.Merchant, date, price, vat, price - vat);
    }
}
