using System.Text.Json;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>Same cases as gate in web/src/confidence.test.ts: tests/confidence-cases.json.</summary>
public sealed class ConfidenceGateTests
{
    public static TheoryData<string> GateCaseNames() =>
        new(ConfidenceScorerTests.Cases("gate").EnumerateArray().Select(c => c.GetProperty("name").GetString()!));

    [Theory]
    [MemberData(nameof(GateCaseNames))]
    public void Gates_the_shared_cases(string name)
    {
        var c = ConfidenceScorerTests.Cases("gate").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var fields = c.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        var scored = fields.Count == 0 ? Confidence.None : new Confidence(fields.Values.Min(), fields);
        var locked = c.GetProperty("locked").EnumerateArray().Select(x => x.GetString()!).ToHashSet();

        var gated = ConfidenceGate.WithLocked(scored, locked);

        var overall = c.GetProperty("overall");
        Assert.Equal(overall.ValueKind == JsonValueKind.Null ? (double?)null : overall.GetDouble(), gated.Overall);
        Assert.Equal(c.GetProperty("flag").GetString(), ConfidenceGate.Flag(gated, c.GetProperty("min").GetDouble()));
    }
}
