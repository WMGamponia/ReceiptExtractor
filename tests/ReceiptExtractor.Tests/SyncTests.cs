using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

/// <summary>
/// Rules that exist in more than one place (see "Keep these in sync" in .github/copilot-instructions.md).
/// These fail when one copy changes without the other.
/// </summary>
public class SyncTests
{
    private static string RepoFile(string relativePath, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", relativePath));

    public static TheoryData<string> VatCaseNames() =>
        new(VatCases().Select(c => c.GetProperty("name").GetString()!));

    private static IEnumerable<JsonElement> VatCases() =>
        JsonDocument.Parse(File.ReadAllText(RepoFile("tests/vat-cases.json"))).RootElement.GetProperty("cases").EnumerateArray();

    private static decimal Amount(JsonElement c, string name) =>
        c.GetProperty(name).ValueKind == JsonValueKind.Null ? 0 : c.GetProperty(name).GetDecimal();

    /// <summary>Same cases as vatWarnings in web/src/format.test.ts and vat_check in eval/test_scoring.py.</summary>
    [Theory]
    [MemberData(nameof(VatCaseNames))]
    public void Vat_check_matches_the_shared_cases(string name)
    {
        var c = VatCases().Single(x => x.GetProperty("name").GetString() == name);

        var flags = ReceiptValidator.VatFlags(Amount(c, "total"), Amount(c, "vat"), Amount(c, "vatable"),
            Amount(c, "exempt"), Amount(c, "discount"), Amount(c, "service_charge"));

        Assert.Equal(c.GetProperty("problems").GetInt32(), flags.Count);
    }

    [Fact]
    public void Prompt_matches_the_eval_prompt()
    {
        // The eval scores eval/try_vision.py's PROMPT; the app sends OllamaExtractor.Prompt. They must be the same
        // prompt, or the eval numbers (and the CI accuracy check) say nothing about the app. The app adds the category.
        var python = Regex.Match(File.ReadAllText(RepoFile("eval/try_vision.py")), "PROMPT = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline)
            .Groups[1].Value.ReplaceLineEndings("\n");
        var app = Regex.Replace(OllamaExtractor.Prompt.ReplaceLineEndings("\n"), @"- category:.*?\n  Other\n", "", RegexOptions.Singleline);

        Assert.NotEmpty(python);
        Assert.Equal(python, app);
    }
}
