using System.Text;
using System.Text.RegularExpressions;

namespace ReceiptExtractor.Api.Extraction;

/// <summary>One generated token as Ollama reports it (<c>logprobs: true</c>). <see cref="Bytes"/> is set when the token alone isn't valid UTF-8.</summary>
public sealed record TokenLogprob(string Token, double Logprob, int[]? Bytes = null);

/// <summary>How sure the model was: per scored field (model JSON names) and the lowest of them. Null overall = no score.</summary>
public sealed record Confidence(double? Overall, IReadOnlyDictionary<string, double> Fields)
{
    public static readonly Confidence None = new(null, new Dictionary<string, double>());
}

/// <summary>
/// Turns token probabilities into field confidence: the least likely token of each scored value. One shaky digit is
/// the misread we want to catch, and an average would hide it. Same rules as eval/confidence.py.
/// </summary>
public static class ConfidenceScorer
{
    /// <summary>Dates and amounts, in review-form order. Merchant and category are not scored.</summary>
    public static readonly string[] ScoredFields =
        ["date", "total", "vat_amount", "vatable_sales", "vat_exempt_sales", "discount", "service_charge"];

    public static Confidence Score(string json, IReadOnlyList<TokenLogprob>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
            return Confidence.None;

        // Work in UTF-8 bytes: a token can be half of "ñ", and a merchant like that shifts every offset after it.
        var pieces = tokens.Select(t => t.Bytes is { } b ? b.Select(x => (byte)x).ToArray() : Encoding.UTF8.GetBytes(t.Token)).ToList();
        var content = Encoding.UTF8.GetBytes(json);
        if (!pieces.SelectMany(p => p).SequenceEqual(content))
            return Confidence.None; // never guess which token belongs where

        var starts = new int[pieces.Count];
        for (int i = 1; i < pieces.Count; i++)
            starts[i] = starts[i - 1] + pieces[i - 1].Length;

        var fields = new Dictionary<string, double>();
        foreach (var name in ScoredFields)
        {
            if (ValueSpan(json, name) is not { } span)
                continue;
            var (from, to) = span;
            double? min = null;
            for (int i = 0; i < pieces.Count; i++)
            {
                var (start, end) = (starts[i], starts[i] + pieces[i].Length);
                if (end <= from || start >= to || pieces[i].All(b => b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'))
                    continue;
                min = Math.Min(min ?? 0, tokens[i].Logprob); // logprobs are <= 0
            }
            if (min is { } lp)
                fields[name] = Math.Round(Math.Exp(lp), 3);
        }

        return fields.Count == 0 ? Confidence.None : new(fields.Values.Min(), fields);
    }

    /// <summary>The value's byte range: inside the quotes for a string, up to the next , } or whitespace otherwise.</summary>
    private static (int From, int To)? ValueSpan(string json, string key)
    {
        var match = Regex.Match(json, $$"""[{,]\s*"{{key}}"\s*:\s*""");
        if (!match.Success)
            return null;
        var from = match.Index + match.Length;
        int to;
        if (from < json.Length && json[from] == '"')
        {
            from++;
            to = json.IndexOf('"', from);
            if (to < 0) return null;
        }
        else
        {
            to = from;
            while (to < json.Length && json[to] is not (',' or '}' or ' ' or '\n' or '\r' or '\t'))
                to++;
        }
        return to > from ? (Encoding.UTF8.GetByteCount(json.AsSpan(0, from)), Encoding.UTF8.GetByteCount(json.AsSpan(0, to))) : null;
    }
}
