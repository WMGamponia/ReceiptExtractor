using System.Runtime.CompilerServices;
using System.Text.Json;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>Same cases as web/src/confidence.test.ts and eval/test_confidence.py: tests/confidence-cases.json.</summary>
public sealed class ConfidenceScorerTests
{
    internal static JsonElement Cases(string section, [CallerFilePath] string thisFile = "") =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "confidence-cases.json")))
            .RootElement.GetProperty(section);

    public static TheoryData<string> ScorerCaseNames() =>
        new(Cases("scorer").EnumerateArray().Select(c => c.GetProperty("name").GetString()!));

    [Theory]
    [MemberData(nameof(ScorerCaseNames))]
    public void Scores_the_shared_cases(string name)
    {
        var c = Cases("scorer").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var tokens = c.GetProperty("tokens").EnumerateArray().Select(t => new TokenLogprob(
            t.GetProperty("token").GetString()!,
            t.GetProperty("logprob").GetDouble(),
            t.TryGetProperty("bytes", out var b) ? b.EnumerateArray().Select(x => x.GetInt32()).ToArray() : null)).ToList();

        var result = ConfidenceScorer.Score(c.GetProperty("content").GetString()!, tokens);

        var overall = c.GetProperty("overall");
        Assert.Equal(overall.ValueKind == JsonValueKind.Null ? (double?)null : overall.GetDouble(), result.Overall);
        var expected = c.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        Assert.Equal(expected.OrderBy(p => p.Key), result.Fields.OrderBy(p => p.Key));
    }

    [Fact]
    public void Null_tokens_mean_no_score() =>
        Assert.Same(Confidence.None, ConfidenceScorer.Score("{\"total\":5}", null));
}
