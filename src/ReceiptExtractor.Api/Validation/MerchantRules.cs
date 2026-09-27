using System.Text.RegularExpressions;

namespace ReceiptExtractor.Api.Validation;

/// <summary>Clean-up applied to the merchant name the model returns.</summary>
public static partial class MerchantRules
{
    // Online marketplaces: the merchant is the marketplace, not the seller's shop
    // (e.g. "LAZADA Edifier" -> "LAZADA"). The model kept ignoring this in the prompt, so it's a rule here.
    private static readonly string[] Marketplaces = ["LAZADA", "SHOPEE", "TIKTOK SHOP"];

    public static string? Normalize(string? merchant)
    {
        if (string.IsNullOrWhiteSpace(merchant))
            return null;

        var cleaned = Whitespace().Replace(merchant.Trim(), " ");

        foreach (var marketplace in Marketplaces)
        {
            if (cleaned.Contains(marketplace, StringComparison.OrdinalIgnoreCase))
                return marketplace;
        }

        return cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
