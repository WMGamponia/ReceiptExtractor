namespace ReceiptExtractor.Api.Extraction;

/// <summary>
/// Below <see cref="OllamaOptions.MinConfidence"/> a receipt gets one flag, which sends it to Needs review instead of
/// Ready to confirm. Same rules and wording as web/src/confidence.ts (tests/confidence-cases.json).
/// </summary>
public static class ConfidenceGate
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["date"] = "Date", ["total"] = "Total", ["vat_amount"] = "VAT", ["vatable_sales"] = "VATable sales",
        ["vat_exempt_sales"] = "VAT-exempt sales", ["discount"] = "Discount", ["service_charge"] = "Service charge",
    };

    /// <summary>Fields the user corrected and kept for a re-extraction are theirs, not the model's guess: 1.0.</summary>
    public static Confidence WithLocked(Confidence c, IReadOnlySet<string> locked)
    {
        if (c.Overall is null)
            return c;
        var fields = c.Fields.ToDictionary(p => p.Key, p => p.Value);
        foreach (var name in ConfidenceScorer.ScoredFields.Where(locked.Contains))
            fields[name] = 1.0;
        return new(fields.Values.Min(), fields);
    }

    /// <summary>The flag to add, or null when the score is at or above the threshold (or there is no score).</summary>
    public static string? Flag(Confidence c, double minConfidence)
    {
        if (c.Overall is not { } overall || overall >= minConfidence)
            return null;
        var unsure = ConfidenceScorer.ScoredFields
            .Where(f => c.Fields.TryGetValue(f, out var v) && v < minConfidence)
            .Select(f => Labels[f]).ToList();
        return unsure.Count == 1
            ? $"The model was unsure about {unsure[0]} ({Percent(overall)}). Check it against the photo."
            : $"The model was unsure about {string.Join(", ", unsure)} (lowest {Percent(overall)}). Check them against the photo.";
    }

    /// <summary>0.62 -> "62%". Rounds down, so a score under the threshold never shows as the threshold.</summary>
    public static string Percent(double x) => $"{Math.Floor(x * 100 + 1e-9):0}%";
}
