# Confidence Score Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every extraction gets a read-only confidence score from the model's token probabilities; below a configured threshold the receipt gets a flag and so lands in Needs review instead of Ready to confirm.

**Architecture:** Ollama returns per-token logprobs for the same `/api/chat` call. A pure scorer (`ConfidenceScorer`, ported to `eval/confidence.py`) maps tokens to the scored fields' values and takes the least-likely token per field. A pure gate (`ConfidenceGate`, mirrored in `web/src/confidence.ts`) turns a low score into one flag, so the existing "flags ⇒ NeedsReview" rule does the rest. The threshold is calibrated on the eval dataset.

**Tech Stack:** ASP.NET Core (.NET 10) minimal APIs, EF Core + SQLite, xUnit; React + TypeScript + Vite, Vitest; Python 3 eval (`ollama` 0.6.2, unittest).

**Spec:** `docs/superpowers/specs/2026-10-01-confidence-score-design.md`

## Global Constraints

- Scored fields, in this order everywhere (it is also the review form's order): `date`, `total`, `vat_amount`, `vatable_sales`, `vat_exempt_sales`, `discount`, `service_charge`. Merchant and category are never scored.
- Field confidence = `exp(min logprob)` over the value's non-whitespace tokens, rounded to 3 decimals. Overall = minimum field confidence. No tokens, or tokens that don't rebuild the content exactly ⇒ no score (`Overall = null`, no fields), never a guess.
- Per-field keys are the model's JSON names (`vat_amount`, …) in the API, the DB, the demo and the eval.
- Gate: flag only when `Overall < MinConfidence` (strict). Locked (user-corrected) fields count as `1.0`.
- Flag text (C# and TS identical, pinned by `tests/confidence-cases.json`):
  - one field: `The model was unsure about Total (62%). Check it against the photo.`
  - several: `The model was unsure about Total, VAT (lowest 62%). Check them against the photo.`
  - labels: Date, Total, VAT, VATable sales, VAT-exempt sales, Discount, Service charge
  - percent = `floor(x * 100 + 1e-9)` (so 0.29 shows 29%, and 0.849 never shows as 85%)
- Threshold: `Ollama:MinConfidence`. It lives in `appsettings.json`, the `OllamaOptions` default and `MIN_CONFIDENCE` in `web/src/demo/demoApi.ts`, which must be equal (SyncTests). It is `0` (gate off) until Task 9 sets the calibrated value.
- Confidence is never accepted from the client (`ReceiptUpdate`, `ReextractRequest`, live demo `hints`).
- Live demo: nothing new is logged; confidence values are never logged anywhere.
- Commits: plain messages, **no** `Co-Authored-By` trailer (user's rule). Branch: `feature/confidence-score`.
- Build and test without fighting locked files: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"` (`$SCRATCH` = the session scratchpad); in `web/` use `npm test`, never `npm ci`. Python: `.venv/Scripts/python -m unittest discover -s eval -p "test_*.py"`.

## Review Focus

1. **Non-ASCII text before the scored fields** (merchant `"Niño's Bakery"`, tokens split mid-character): spans must be computed in UTF-8 bytes and still find the right digits. Pinned by the `multibyte merchant` case in `tests/confidence-cases.json` (Task 1, used by Tasks 2 and 6 too).
2. **Percent rounding** (0.29 × 100 = 28.999…): the flag must say 29%. Pinned by the `percent rounding` gate case (Task 3), which runs in C# and TS.
3. **Re-extract with every scored field locked:** overall becomes 1.0, no flag, and a receipt without a score stays without one. Pinned by the `all locked` and `no score stays none` gate cases (Task 3).
4. **Old data without confidence:** old `receipts.db` rows and old demo localStorage receipts must load, show no badge and not crash. Pinned by the SchemaPatches test (Task 4) and the `old saved receipt` demo test (Task 8).
5. **Confirming a low-confidence receipt:** the "unsure" flag drops away on confirm, the score stays, and the review page shows no markers or upload-date button for a confirmed receipt. Pinned by the endpoint test `Confirming_drops_the_unsure_flag_and_keeps_the_score` (Task 4) and `unsureFields` returning `[]` for Confirmed (Task 6).

---

### Task 1: C# scorer and the shared scorer cases

**Files:**
- Create: `tests/confidence-cases.json`
- Create: `src/ReceiptExtractor.Api/Extraction/ConfidenceScorer.cs`
- Create: `tests/ReceiptExtractor.Tests/ConfidenceScorerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TokenLogprob(string Token, double Logprob, int[]? Bytes = null);`
  - `public sealed record Confidence(double? Overall, IReadOnlyDictionary<string, double> Fields) { public static readonly Confidence None; }`
  - `public static class ConfidenceScorer { public static readonly string[] ScoredFields; public static Confidence Score(string json, IReadOnlyList<TokenLogprob>? tokens); }`
  - `tests/confidence-cases.json` with `"scorer"` cases (`name`, `content`, `tokens` [{`token`,`logprob`,`bytes`?}], `overall`, `fields`) and `"gate"` cases (added in Task 3).

- [x] **Step 1: Write the shared cases**

`tests/confidence-cases.json`. The `exp` values are rounded to 3 decimals: exp(-0.5)=0.607, exp(-0.1)=0.905, exp(-2)=0.135, exp(-0.05)=0.951, exp(-1)=0.368, exp(0)=1.

```json
{
  "_comment": "Shared by ConfidenceScorer/ConfidenceGate (C#), web/src/confidence.ts and eval/confidence.py. Logprobs are natural logs.",
  "scorer": [
    {
      "name": "one shaky digit",
      "content": "{\"date\": \"2026-08-29\", \"total\": 12.50}",
      "tokens": [
        {"token": "{\"", "logprob": 0}, {"token": "date", "logprob": 0}, {"token": "\": \"", "logprob": 0},
        {"token": "2026", "logprob": 0}, {"token": "-08-", "logprob": -0.05}, {"token": "2", "logprob": -0.5},
        {"token": "9", "logprob": 0}, {"token": "\", \"", "logprob": 0}, {"token": "total", "logprob": 0},
        {"token": "\": ", "logprob": -3}, {"token": "12", "logprob": 0}, {"token": ".5", "logprob": -0.1},
        {"token": "0", "logprob": 0}, {"token": "}", "logprob": -4}
      ],
      "overall": 0.607,
      "fields": {"date": 0.607, "total": 0.905}
    },
    {
      "name": "token straddling punctuation and value counts",
      "content": "{\"total\":100}",
      "tokens": [{"token": "{\"total", "logprob": 0}, {"token": "\":1", "logprob": -2}, {"token": "00}", "logprob": 0}],
      "overall": 0.135,
      "fields": {"total": 0.135}
    },
    {
      "name": "null is scored, whitespace-only tokens are not",
      "content": "{\"discount\": null,\n  \"service_charge\": 0}",
      "tokens": [
        {"token": "{\"discount\":", "logprob": 0}, {"token": " ", "logprob": -5}, {"token": "null", "logprob": -1},
        {"token": ",\n", "logprob": 0}, {"token": "  ", "logprob": -5}, {"token": "\"service_charge\":", "logprob": 0},
        {"token": " ", "logprob": -5}, {"token": "0", "logprob": 0}, {"token": "}", "logprob": 0}
      ],
      "overall": 0.368,
      "fields": {"discount": 0.368, "service_charge": 1.0}
    },
    {
      "name": "merchant and category are not scored",
      "content": "{\"merchant\":\"ABC\",\"total\":5,\"category\":\"Food\"}",
      "tokens": [
        {"token": "{\"merchant\":\"", "logprob": 0}, {"token": "ABC", "logprob": -6}, {"token": "\",\"total\":", "logprob": 0},
        {"token": "5", "logprob": -0.05}, {"token": ",\"category\":\"", "logprob": 0}, {"token": "Food", "logprob": -6},
        {"token": "\"}", "logprob": 0}
      ],
      "overall": 0.951,
      "fields": {"total": 0.951}
    },
    {
      "name": "multibyte merchant",
      "content": "{\"merchant\":\"Niño\",\"total\":7}",
      "tokens": [
        {"token": "{\"merchant\":\"Ni", "logprob": 0},
        {"token": "�", "logprob": -1, "bytes": [195]},
        {"token": "�", "logprob": -1, "bytes": [177]},
        {"token": "o\",\"total\":", "logprob": 0},
        {"token": "7", "logprob": -0.1},
        {"token": "}", "logprob": 0}
      ],
      "overall": 0.905,
      "fields": {"total": 0.905}
    },
    {
      "name": "no tokens means no score",
      "content": "{\"total\":5}",
      "tokens": [],
      "overall": null,
      "fields": {}
    },
    {
      "name": "tokens that don't rebuild the content mean no score",
      "content": "{\"total\":5}",
      "tokens": [{"token": "{\"total\":6}", "logprob": 0}],
      "overall": null,
      "fields": {}
    }
  ],
  "gate": []
}
```

- [x] **Step 2: Write the failing tests**

`tests/ReceiptExtractor.Tests/ConfidenceScorerTests.cs`:

```csharp
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
        Assert.Equal(overall.ValueKind == JsonValueKind.Null ? null : overall.GetDouble(), result.Overall);
        var expected = c.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        Assert.Equal(expected.OrderBy(p => p.Key), result.Fields.OrderBy(p => p.Key));
    }

    [Fact]
    public void Null_tokens_mean_no_score() =>
        Assert.Same(Confidence.None, ConfidenceScorer.Score("{\"total\":5}", null));
}
```

- [x] **Step 3: Run the tests to make sure they fail**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build" --filter ConfidenceScorerTests`
Expected: build error, `ConfidenceScorer` / `TokenLogprob` not found.

- [x] **Step 4: Implement the scorer**

`src/ReceiptExtractor.Api/Extraction/ConfidenceScorer.cs`:

```csharp
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
                min = Math.Min(min ?? 0, tokens[i].Logprob);
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
```

Note: `min = Math.Min(min ?? 0, …)` is right because logprobs are ≤ 0.

- [x] **Step 5: Run the tests to make sure they pass**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build" --filter ConfidenceScorerTests`
Expected: 8 passed.

- [x] **Step 6: Commit**

```bash
git add tests/confidence-cases.json src/ReceiptExtractor.Api/Extraction/ConfidenceScorer.cs tests/ReceiptExtractor.Tests/ConfidenceScorerTests.cs
git commit -m "Confidence scorer: least likely token per date/amount field, shared test cases"
```

---

### Task 2: Python scorer for the eval

**Files:**
- Create: `eval/confidence.py`
- Create: `eval/test_confidence.py`

**Interfaces:**
- Consumes: `tests/confidence-cases.json` `"scorer"` (Task 1).
- Produces: `SCORED_FIELDS: list[str]`; `score(content: str, tokens: list[dict]) -> dict` returning `{"overall": float | None, "fields": dict[str, float]}`. Each token dict has `token`, `logprob` and optionally `bytes`.

- [x] **Step 1: Write the failing test**

`eval/test_confidence.py`:

```python
"""The eval's copy of ConfidenceScorer.cs, checked against the same cases (tests/confidence-cases.json)."""

import json
import unittest
from pathlib import Path

from confidence import score

ROOT = Path(__file__).resolve().parent.parent


class ScorerTests(unittest.TestCase):
    def test_matches_the_shared_cases(self):
        cases = json.loads((ROOT / "tests" / "confidence-cases.json").read_text(encoding="utf-8"))["scorer"]
        for c in cases:
            with self.subTest(c["name"]):
                self.assertEqual(score(c["content"], c["tokens"]), {"overall": c["overall"], "fields": c["fields"]})

    def test_no_tokens(self):
        self.assertEqual(score('{"total":5}', None), {"overall": None, "fields": {}})


if __name__ == "__main__":
    unittest.main()
```

- [x] **Step 2: Run it to make sure it fails**

Run: `.venv/Scripts/python -m unittest discover -s eval -p "test_confidence.py"`
Expected: `ModuleNotFoundError: No module named 'confidence'`.

- [x] **Step 3: Implement**

`eval/confidence.py`:

```python
"""
Confidence from token probabilities: the least likely token of each scored value.
Same rules as src/ReceiptExtractor.Api/Extraction/ConfidenceScorer.cs; both pass tests/confidence-cases.json.
"""

import math
import re

# Dates and amounts, in review-form order. Merchant and category are not scored.
SCORED_FIELDS = ["date", "total", "vat_amount", "vatable_sales", "vat_exempt_sales", "discount", "service_charge"]

_WHITESPACE = set(b" \n\r\t")


def _value_span(content: str, key: str) -> tuple[int, int] | None:
    """The value's byte range: inside the quotes for a string, up to the next , } or whitespace otherwise."""
    m = re.search(r'[{,]\s*"' + key + r'"\s*:\s*', content)
    if not m:
        return None
    start = m.end()
    if start < len(content) and content[start] == '"':
        start += 1
        end = content.find('"', start)
        if end < 0:
            return None
    else:
        end = start
        while end < len(content) and content[end] not in ',} \n\r\t':
            end += 1
    if end <= start:
        return None
    return len(content[:start].encode("utf-8")), len(content[:end].encode("utf-8"))


def score(content: str, tokens: list[dict] | None) -> dict:
    """tokens: [{"token", "logprob", "bytes"?}] as Ollama returns them. No or mismatched tokens -> no score."""
    none = {"overall": None, "fields": {}}
    if not tokens:
        return none
    # UTF-8 bytes: a token can be half of "ñ", which shifts every offset after it.
    pieces = [bytes(t["bytes"]) if t.get("bytes") is not None else t["token"].encode("utf-8") for t in tokens]
    if b"".join(pieces) != content.encode("utf-8"):
        return none

    starts, pos = [], 0
    for p in pieces:
        starts.append(pos)
        pos += len(p)

    fields = {}
    for name in SCORED_FIELDS:
        span = _value_span(content, name)
        if span is None:
            continue
        lo, hi = span
        logprobs = [t["logprob"] for t, p, s in zip(tokens, pieces, starts)
                    if s < hi and s + len(p) > lo and not set(p) <= _WHITESPACE]
        if logprobs:
            fields[name] = round(math.exp(min(logprobs)), 3)
    return {"overall": min(fields.values()), "fields": fields} if fields else none
```

- [x] **Step 4: Run it to make sure it passes**

Run: `.venv/Scripts/python -m unittest discover -s eval -p "test_*.py"`
Expected: all OK, including the existing `test_scoring.py`.

Python's `round` uses banker's rounding and C# `Math.Round` defaults to it too (`MidpointRounding.ToEven`), so both agree on ties.

- [x] **Step 5: Commit**

```bash
git add eval/confidence.py eval/test_confidence.py
git commit -m "Eval: Python copy of the confidence scorer on the shared cases"
```

---

### Task 3: Gate (C#): locked fields and the flag

**Files:**
- Modify: `tests/confidence-cases.json` (fill `"gate"`)
- Create: `src/ReceiptExtractor.Api/Extraction/ConfidenceGate.cs`
- Create: `tests/ReceiptExtractor.Tests/ConfidenceGateTests.cs`

**Interfaces:**
- Consumes: `Confidence`, `ConfidenceScorer.ScoredFields` (Task 1).
- Produces:
  - `public static Confidence ConfidenceGate.WithLocked(Confidence c, IReadOnlySet<string> locked)`
  - `public static string? ConfidenceGate.Flag(Confidence c, double minConfidence)`
  - `public static string ConfidenceGate.Percent(double x)` returning e.g. `"62%"`

- [x] **Step 1: Fill the gate cases**

Replace `"gate": []` in `tests/confidence-cases.json` with:

```json
  "gate": [
    {"name": "above the threshold", "fields": {"date": 0.99, "total": 0.9}, "locked": [], "min": 0.85,
     "overall": 0.9, "flag": null},
    {"name": "exactly the threshold is not flagged", "fields": {"total": 0.85}, "locked": [], "min": 0.85,
     "overall": 0.85, "flag": null},
    {"name": "one field", "fields": {"date": 0.99, "total": 0.62}, "locked": [], "min": 0.85,
     "overall": 0.62, "flag": "The model was unsure about Total (62%). Check it against the photo."},
    {"name": "several fields in form order", "fields": {"vat_amount": 0.7, "date": 0.8, "total": 0.99, "service_charge": 0.5}, "locked": [], "min": 0.85,
     "overall": 0.5, "flag": "The model was unsure about Date, VAT, Service charge (lowest 50%). Check them against the photo."},
    {"name": "percent rounding", "fields": {"vatable_sales": 0.29, "vat_exempt_sales": 0.4, "discount": 0.3}, "locked": [], "min": 0.85,
     "overall": 0.29, "flag": "The model was unsure about VATable sales, VAT-exempt sales, Discount (lowest 29%). Check them against the photo."},
    {"name": "a locked field counts as sure", "fields": {"date": 0.99, "total": 0.5}, "locked": ["total", "merchant"], "min": 0.85,
     "overall": 0.99, "flag": null},
    {"name": "all locked", "fields": {"date": 0.4, "total": 0.5}, "locked": ["date", "total", "vat_amount", "vatable_sales", "vat_exempt_sales", "discount", "service_charge"], "min": 0.85,
     "overall": 1.0, "flag": null},
    {"name": "no score stays none", "fields": {}, "locked": ["total"], "min": 0.85,
     "overall": null, "flag": null},
    {"name": "threshold 0 turns the gate off", "fields": {"total": 0.01}, "locked": [], "min": 0,
     "overall": 0.01, "flag": null}
  ]
```

The input `overall` is always the minimum of `fields` (or null when `fields` is empty). The case's `overall` is the value expected **after** `WithLocked`.

- [x] **Step 2: Write the failing tests**

`tests/ReceiptExtractor.Tests/ConfidenceGateTests.cs`:

```csharp
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
        Assert.Equal(overall.ValueKind == JsonValueKind.Null ? null : overall.GetDouble(), gated.Overall);
        Assert.Equal(c.GetProperty("flag").GetString(), ConfidenceGate.Flag(gated, c.GetProperty("min").GetDouble()));
    }
}
```

- [x] **Step 3: Run them to make sure they fail**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build" --filter ConfidenceGateTests`
Expected: build error, `ConfidenceGate` not found.

- [x] **Step 4: Implement**

`src/ReceiptExtractor.Api/Extraction/ConfidenceGate.cs`:

```csharp
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
```

- [x] **Step 5: Run them to make sure they pass**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build" --filter "ConfidenceGateTests|ConfidenceScorerTests"`
Expected: all passed.

- [x] **Step 6: Commit**

```bash
git add tests/confidence-cases.json src/ReceiptExtractor.Api/Extraction/ConfidenceGate.cs tests/ReceiptExtractor.Tests/ConfidenceGateTests.cs
git commit -m "Confidence gate: locked fields count as sure, one flag below the threshold"
```

---

### Task 4: Extractor asks for logprobs; storage, API and worker

**Files:**
- Modify: `src/ReceiptExtractor.Api/Extraction/OllamaExtractor.cs` (options, `ExtractionResult`, request, `ChatResponse`)
- Modify: `src/ReceiptExtractor.Api/appsettings.json` (`"MinConfidence": 0`)
- Modify: `src/ReceiptExtractor.Api/Data/ReceiptRecord.cs`, `Data/AppDbContext.cs`, `Data/SchemaPatches.cs`
- Modify: `src/ReceiptExtractor.Api/Endpoints/ReceiptEndpoints.cs` (`ReceiptResponse`, every `ReceiptResponse.From` caller)
- Modify: `src/ReceiptExtractor.Api/Extraction/ExtractionWorker.cs`
- Test: `tests/ReceiptExtractor.Tests/OllamaRequestTests.cs`, `SchemaPatchesTests.cs`, `ReceiptEndpointsTests.cs`, `SyncTests.cs`
- Modify: `docs/superpowers/specs/2026-10-01-confidence-score-design.md` (keys are model JSON names)

**Interfaces:**
- Consumes: `ConfidenceScorer.Score`, `TokenLogprob`, `Confidence` (Task 1); `ConfidenceGate` (Task 3).
- Produces:
  - `OllamaOptions.MinConfidence` (`double`, default `0`)
  - `ExtractionResult(ReceiptFields Fields, string? Category, string ModelOutput, Confidence? Confidence = null)`
  - `ReceiptRecord.Confidence` (`double?`), `ReceiptRecord.FieldConfidence` (`Dictionary<string,double>`)
  - Response JSON: `confidence` (number|null), `fieldConfidence` (object keyed by model JSON names), `minConfidence` (number)
  - `ReceiptResponse.From(ReceiptRecord r, double minConfidence)`

- [ ] **Step 1: Write the failing tests**

In `OllamaRequestTests.cs`, add a second canned answer, a way to choose it, and two tests. Replace the `CapturingHandler` and `Create` with:

```csharp
    // Same answer with the token probabilities Ollama adds for logprobs: true (one shaky digit in the total).
    private const string AnswerWithLogprobs = """
        {"message":{"content":"{\"date\":\"2026-08-09\",\"total\":100}"},"done_reason":"stop","logprobs":[
          {"token":"{\"date\":\"","logprob":0},{"token":"2026-08-09","logprob":-0.01},{"token":"\",\"total\":","logprob":0},
          {"token":"1","logprob":-0.5},{"token":"00","logprob":0},{"token":"}","logprob":0}]}
        """;

    private sealed class CapturingHandler(string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static (OllamaExtractor Extractor, CapturingHandler Handler) Create(string answer = Answer)
    {
        var options = new OllamaOptions();
        var handler = new CapturingHandler(answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return (new OllamaExtractor(http, Options.Create(options)), handler);
    }

    [Fact]
    public async Task Asks_for_token_probabilities_and_scores_them()
    {
        var (extractor, handler) = Create(AnswerWithLogprobs);

        var result = await extractor.ExtractAsync(Image, [], CancellationToken.None);

        Assert.Contains("\"logprobs\":true", handler.Body);
        Assert.Equal(0.607, result.Confidence!.Overall);
        Assert.Equal(0.99, result.Confidence.Fields["date"]);
    }

    [Fact]
    public async Task An_answer_without_probabilities_has_no_score()
    {
        var (extractor, _) = Create();

        var result = await extractor.ExtractAsync(Image, [], CancellationToken.None);

        Assert.Null(result.Confidence!.Overall);
    }
```

In `SchemaPatchesTests.cs`, extend the list:

```csharp
    private static readonly string[] AddedSinceFirstRelease =
        ["VatExemptSales", "Discount", "Corrections", "LockedFields", "ServiceCharge", "Confidence", "FieldConfidence"];
```

In `ReceiptEndpointsTests.cs`, change `AddReceipt` to take a score, and add the tests:

```csharp
    private Guid AddReceipt(string? modelOutput = ModelOutput, double? confidence = null, ReceiptStatus status = ReceiptStatus.Extracted)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = new ReceiptRecord
        {
            Id = Guid.NewGuid(), Status = status, OriginalFileName = "r.jpg", ImagePath = "r.jpg",
            ContentType = "image/jpeg", ModelOutput = modelOutput,
        };
        if (modelOutput is not null)
            receipt.ApplyFields(OllamaExtractor.ParseModelOutput(modelOutput).Fields, "Food");
        if (confidence is { } c)
        {
            receipt.Confidence = c;
            receipt.FieldConfidence = new() { ["date"] = 0.99, ["total"] = c };
            receipt.Flags = ["The model was unsure about Total (62%). Check it against the photo."];
        }
        db.Receipts.Add(receipt);
        db.SaveChanges();
        return receipt.Id;
    }

    [Fact]
    public async Task Returns_the_score_and_the_threshold()
    {
        var id = AddReceipt(confidence: 0.62, status: ReceiptStatus.NeedsReview);

        var receipt = await Json(await _http.GetAsync($"/api/receipts/{id}"));

        Assert.Equal(0.62, receipt.GetProperty("confidence").GetDouble());
        Assert.Equal(0.62, receipt.GetProperty("fieldConfidence").GetProperty("total").GetDouble());
        Assert.Equal(0.85, receipt.GetProperty("minConfidence").GetDouble());
    }

    [Fact]
    public async Task A_receipt_without_a_score_says_so()
    {
        var receipt = await Json(await _http.GetAsync($"/api/receipts/{AddReceipt()}"));

        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("confidence").ValueKind);
        Assert.Empty(receipt.GetProperty("fieldConfidence").EnumerateObject());
    }

    [Fact]
    public async Task Confirming_drops_the_unsure_flag_and_keeps_the_score()
    {
        var id = AddReceipt(confidence: 0.62, status: ReceiptStatus.NeedsReview);

        // A client trying to set the score along with the fields: it isn't part of ReceiptUpdate, so it's ignored.
        var body = new Dictionary<string, object?>
        {
            ["merchant"] = "TEXAS ROADHOUSE", ["date"] = "2026-08-08", ["total"] = 6323.43m, ["vatAmount"] = 361.34m,
            ["vatableSales"] = 3011.16m, ["vatExemptSales"] = 3011.16m, ["discount"] = 602.23m, ["serviceCharge"] = 542.01m,
            ["category"] = "Food", ["confidence"] = 1.0,
        };
        var saved = await Json(await _http.PutAsJsonAsync($"/api/receipts/{id}", body));

        Assert.Equal("Confirmed", saved.GetProperty("status").GetString());
        Assert.Empty(saved.GetProperty("flags").EnumerateArray());
        Assert.Equal(0.62, saved.GetProperty("confidence").GetDouble());
    }
```

In `InitializeAsync`, register the threshold the tests expect (after `AddSingleton<ExtractionQueue>()`):

```csharp
        builder.Services.Configure<OllamaOptions>(o => o.MinConfidence = 0.85);
```

In `SyncTests.cs`, add (needs `using System.Text.Json;`, already imported):

```csharp
    [Fact]
    public void Confidence_threshold_is_the_same_everywhere()
    {
        // appsettings.json is what the app uses; the OllamaOptions default covers a missing setting; the demo site
        // (no server) has its own copy in demoApi.ts.
        var settings = JsonDocument.Parse(File.ReadAllText(RepoFile("src/ReceiptExtractor.Api/appsettings.json")))
            .RootElement.GetProperty("Ollama").GetProperty("MinConfidence").GetDouble();
        var demo = Regex.Match(File.ReadAllText(RepoFile("web/src/demo/demoApi.ts")), @"export const MIN_CONFIDENCE = ([0-9.]+)");

        Assert.Equal(settings, new OllamaOptions().MinConfidence);
        Assert.True(demo.Success, "MIN_CONFIDENCE not found in demoApi.ts");
        Assert.Equal(settings, double.Parse(demo.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
```

Add `export const MIN_CONFIDENCE = 0` to `web/src/demo/demoApi.ts` now, next to `PRELOADED`, with the comment `// Same as Ollama:MinConfidence in appsettings.json (SyncTests); the demo has no server to ask.`. Task 8 uses it.

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"`
Expected: build errors (`MinConfidence`, `Confidence` on the record and result not found).

- [ ] **Step 3: Extractor**

In `OllamaExtractor.cs`:

```csharp
public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3-vl:8b-instruct";
    public string KeepAlive { get; set; } = "10m";   // keep the model loaded between receipts
    public int ContextLength { get; set; } = 8192;   // the image alone can take ~4000 tokens
    /// <summary>Below this confidence a receipt goes to Needs review (eval/RESULTS.md). 0 = off. Same value in appsettings.json and demoApi.ts.</summary>
    public double MinConfidence { get; set; } = 0;
}

public sealed record ExtractionResult(ReceiptFields Fields, string? Category, string ModelOutput, Confidence? Confidence = null);
```

In the request object add `logprobs = true,` after `think = false,`, with the comment `// token probabilities, for the confidence score`. Replace the final `return ParseModelOutput(content);` with:

```csharp
        var tokens = chat.Logprobs?.Select(t => new TokenLogprob(t.Token ?? "", t.Logprob, t.Bytes)).ToList();
        return ParseModelOutput(content) with { Confidence = ConfidenceScorer.Score(content, tokens) };
```

Replace `ChatResponse` and add the token record:

```csharp
    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("logprobs")] List<ChatLogprob>? Logprobs);

    private sealed record ChatLogprob(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("logprob")] double Logprob,
        [property: JsonPropertyName("bytes")] int[]? Bytes);
```

Add `"MinConfidence": 0` to the `"Ollama"` section of `appsettings.json`, after `"ContextLength": 8192`.

- [ ] **Step 4: Storage**

`ReceiptRecord.cs`, after `Flags`:

```csharp
    /// <summary>
    /// How sure the model was of the dates and amounts it read: the lowest of <see cref="FieldConfidence"/>
    /// (see ConfidenceScorer). Null = no score (read before scores existed, or Ollama sent none). Never user-editable.
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>Per scored field, model JSON names as keys (e.g. "vat_amount"). Fields the user locked count as 1.0.</summary>
    public Dictionary<string, double> FieldConfidence { get; set; } = [];
```

`AppDbContext.OnModelCreating`, after the `Status` line (add `using System.Text.Json;` and `using Microsoft.EntityFrameworkCore.ChangeTracking;`):

```csharp
        // A dictionary isn't a primitive collection to EF: store it as JSON text like the string lists.
        receipt.Property(r => r.FieldConfidence).HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, double>>(v, (JsonSerializerOptions?)null) ?? new(),
            new ValueComparer<Dictionary<string, double>>(
                (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                d => d.Aggregate(0, (h, p) => HashCode.Combine(h, p.Key, p.Value)),
                d => new Dictionary<string, double>(d)));
```

`SchemaPatches.AddedColumns`, append:

```csharp
        (nameof(ReceiptRecord.Confidence), "REAL NULL"),
        (nameof(ReceiptRecord.FieldConfidence), "TEXT NOT NULL DEFAULT '{}'"),
```

- [ ] **Step 5: API response**

In `ReceiptEndpoints.cs`, append three members to `ReceiptResponse` after `string ImageUrl`:

```csharp
    string ImageUrl,
    double? Confidence,                                 // read-only: the model's, never the user's
    IReadOnlyDictionary<string, double> FieldConfidence,
    double MinConfidence)                               // Ollama:MinConfidence, so the web app has one source of truth
{
    public static ReceiptResponse From(ReceiptRecord r, double minConfidence) => new(
        r.Id, r.DisplayName, r.Status, r.Merchant, r.Date, r.Total, r.VatAmount, r.VatableSales,
        r.VatExemptSales, r.Discount, r.ServiceCharge, r.Category, r.Flags, r.Corrections, r.ExtractionSeconds, r.Error, r.OriginalFileName, r.UploadedAtUtc,
        r.ConfirmedAtUtc, $"/api/receipts/{r.Id}/image", r.Confidence, r.FieldConfidence, minConfidence);
}
```

Every handler that calls `ReceiptResponse.From` (upload, list, get, rotate, confirm, bulk confirm, retry, re-extract: `grep -n "ReceiptResponse.From" src/ReceiptExtractor.Api/Endpoints/ReceiptEndpoints.cs`) gets a parameter `IOptions<OllamaOptions> ollama` and passes `ollama.Value.MinConfidence`. In the list handler that is `receipts.Select(r => ReceiptResponse.From(r, ollama.Value.MinConfidence))`. `Microsoft.Extensions.Options` and `ReceiptExtractor.Api.Extraction` are already imported.

- [ ] **Step 6: Worker**

In `ExtractionWorker.ProcessAsync`, resolve the options next to the extractor:

```csharp
        var minConfidence = scope.ServiceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value.MinConfidence;
```

(add `using Microsoft.Extensions.Options;`). Replace the two lines that set `Flags` and `Status` with:

```csharp
            var confidence = ConfidenceGate.WithLocked(result.Confidence ?? Confidence.None, locked);
            receipt.Confidence = confidence.Overall;
            receipt.FieldConfidence = new(confidence.Fields);

            receipt.LockedFields = [];
            receipt.Flags = ReceiptValidator.Validate(fields, today);
            if (ConfidenceGate.Flag(confidence, minConfidence) is { } unsure)
                receipt.Flags.Add(unsure); // a shaky read is not "Ready to confirm"
            receipt.Status = receipt.Flags.Count > 0 ? ReceiptStatus.NeedsReview : ReceiptStatus.Extracted;
```

(the existing `receipt.LockedFields = [];` line moves into this block, so remove the original.) `Confidence` here is the record from `ReceiptExtractor.Api.Extraction`. If it clashes with the `ReceiptRecord.Confidence` property name in this file, write `Extraction.Confidence.None`.

- [ ] **Step 7: Spec correction**

In the spec's "Storage and API" section, replace `FieldConfidence` (camelCase keys to match the rest of the API, e.g. `vatAmount`) with `FieldConfidence` (model JSON names as keys, e.g. `vat_amount`, the same names the web app already uses for locked fields).

- [ ] **Step 8: Run all C# tests**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"`
Expected: all pass. If `Confidence_threshold_is_the_same_everywhere` fails, check that `MIN_CONFIDENCE = 0` was added to demoApi.ts.

- [ ] **Step 9: Commit**

```bash
git add src tests web/src/demo/demoApi.ts docs/superpowers/specs/2026-10-01-confidence-score-design.md
git commit -m "Confidence score: asked from Ollama, stored per receipt, low score sends it to Needs review"
```

---

### Task 5: Live demo returns the score

**Files:**
- Modify: `src/ReceiptExtractor.Api/LiveDemo/LiveDemoEndpoints.cs:20,107-112`
- Test: `tests/ReceiptExtractor.Tests/LiveDemoTests.cs`

**Interfaces:**
- Consumes: `ExtractionResult.Confidence`, `ConfidenceGate.WithLocked` (Tasks 3–4).
- Produces: `LiveReading(ReceiptFields Fields, string? Category, double ExtractionSeconds, Confidence? Confidence)`. JSON: `"confidence": {"overall": 0.62, "fields": {"total": 0.62}}` (dictionary keys are not camel-cased), or `null`.

- [ ] **Step 1: Write the failing test**

Look at how `LiveDemoTests.cs` fakes `IReceiptExtractor` (`grep -n "IReceiptExtractor\|ExtractionResult(" tests/ReceiptExtractor.Tests/LiveDemoTests.cs`). Make the fake return `Confidence: new Confidence(0.62, new Dictionary<string, double> { ["date"] = 0.99, ["total"] = 0.62 })`, then add the following tests, using the file's existing helpers for posting a photo (and hints):

```csharp
    [Fact]
    public async Task Returns_the_confidence_score()
    {
        var reading = await ReadSamplePhoto(); // the file's existing "post a photo, return the JSON" helper

        Assert.Equal(0.62, reading.GetProperty("confidence").GetProperty("overall").GetDouble());
        Assert.Equal(0.62, reading.GetProperty("confidence").GetProperty("fields").GetProperty("total").GetDouble());
    }

    [Fact]
    public async Task Fields_the_visitor_kept_count_as_sure()
    {
        var reading = await ReadSamplePhotoWithHints(locked: ["total"]); // existing re-extract helper

        Assert.Equal(0.99, reading.GetProperty("confidence").GetProperty("overall").GetDouble());
        Assert.Equal(1.0, reading.GetProperty("confidence").GetProperty("fields").GetProperty("total").GetDouble());
    }
```

If the helpers have other names, use those. The assertions are what matter.

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build" --filter LiveDemoTests`
Expected: FAIL, no `confidence` property.

- [ ] **Step 3: Implement**

```csharp
public sealed record LiveReading(ReceiptFields Fields, string? Category, double ExtractionSeconds, Confidence? Confidence);
```

and in `Extract`:

```csharp
            var confidence = ConfidenceGate.WithLocked(result.Confidence ?? Confidence.None, locked);
            return corrections is null
                ? Results.Ok(new LiveReading(result.Fields, result.Category, seconds, confidence))
                : Results.Ok(new LiveReading(
                    UserHints.Keep(result.Fields, corrections.Fields, locked),
                    locked.Contains(UserHints.Category) ? corrections.Category : result.Category,
                    seconds,
                    confidence));
```

`locked` is a `HashSet<string>` of model JSON names, which is what `WithLocked` expects. Don't add anything to the log line.

- [ ] **Step 4: Run them to make sure they pass**

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ReceiptExtractor.Api/LiveDemo/LiveDemoEndpoints.cs tests/ReceiptExtractor.Tests/LiveDemoTests.cs
git commit -m "Live demo: the reading carries the confidence score"
```

---

### Task 6: Web types and the TypeScript gate

**Files:**
- Modify: `web/src/api.ts` (`Receipt`)
- Create: `web/src/confidence.ts`
- Create: `web/src/confidence.test.ts`

**Interfaces:**
- Consumes: `tests/confidence-cases.json` `"gate"` (Task 3); `LockableField` from `api.ts`.
- Produces (`web/src/confidence.ts`):
  - `export type ScoredField = 'date' | 'total' | 'vat_amount' | 'vatable_sales' | 'vat_exempt_sales' | 'discount' | 'service_charge'`
  - `export type FieldConfidence = Partial<Record<ScoredField, number>>`
  - `export interface Scored { overall: number | null; fields: FieldConfidence }`
  - `export const SCORED_FIELDS: ScoredField[]`
  - `export function withLocked(c: Scored, locked: readonly string[]): Scored`
  - `export function confidenceFlag(c: Scored, min: number): string | null`
  - `export function formatConfidence(x: number): string` (`0.62` → `'62%'`)
  - `export function unsureFields(r: Pick<Receipt, 'status' | 'fieldConfidence' | 'minConfidence'>): ScoredField[]`
  - `Receipt` gains `confidence: number | null`, `fieldConfidence: FieldConfidence`, `minConfidence: number`

- [ ] **Step 1: Write the failing tests**

`web/src/confidence.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import cases from '../../tests/confidence-cases.json'
import { confidenceFlag, formatConfidence, unsureFields, withLocked, type FieldConfidence } from './confidence'

describe('confidence gate (same cases as ConfidenceGateTests.cs)', () => {
  it.each(cases.gate.map((c) => [c.name, c] as const))('%s', (_, c) => {
    const fields = c.fields as FieldConfidence
    const values = Object.values(fields) as number[]
    const scored = { overall: values.length ? Math.min(...values) : null, fields }

    const gated = withLocked(scored, c.locked)

    expect(gated.overall).toBe(c.overall)
    expect(confidenceFlag(gated, c.min)).toBe(c.flag)
  })
})

describe('formatConfidence', () => {
  it('rounds down', () => expect(formatConfidence(0.849)).toBe('84%'))
  it('survives float error', () => expect(formatConfidence(0.29)).toBe('29%'))
})

describe('unsureFields', () => {
  const r = { status: 'NeedsReview' as const, fieldConfidence: { date: 0.6, total: 0.99, discount: 0.5 }, minConfidence: 0.85 }

  it('lists fields under the threshold in form order', () => expect(unsureFields(r)).toEqual(['date', 'discount']))
  it('none once confirmed', () => expect(unsureFields({ ...r, status: 'Confirmed' })).toEqual([]))
  it('none for a receipt saved before scores existed', () =>
    expect(unsureFields({ status: 'Extracted', fieldConfidence: undefined as unknown as FieldConfidence, minConfidence: 0.85 })).toEqual([]))
})
```

- [ ] **Step 2: Run them to make sure they fail**

Run (in `web/`): `npm test -- confidence`
Expected: FAIL, cannot find module `./confidence`.

- [ ] **Step 3: Implement**

`web/src/api.ts`: add to `Receipt` after `imageUrl: string`:

```ts
  confidence: number | null // how sure the model was of the dates and amounts (lowest field); null = no score. Read-only.
  fieldConfidence: FieldConfidence // per field, model JSON names; may be missing on demo receipts saved before scores
  minConfidence: number // below this, the receipt was sent to review
```

with `import type { FieldConfidence } from './confidence'` at the top.

`web/src/confidence.ts`:

```ts
// The confidence gate: same rules and wording as ConfidenceGate.cs (tests/confidence-cases.json).
// The demo site uses it in place of the server; the review page uses unsureFields for its markers.
import type { Receipt } from './api'

export type ScoredField = 'date' | 'total' | 'vat_amount' | 'vatable_sales' | 'vat_exempt_sales' | 'discount' | 'service_charge'
export type FieldConfidence = Partial<Record<ScoredField, number>>
export interface Scored {
  overall: number | null
  fields: FieldConfidence
}

/** Dates and amounts, in review-form order. Merchant and category are not scored. */
export const SCORED_FIELDS: ScoredField[] = ['date', 'total', 'vat_amount', 'vatable_sales', 'vat_exempt_sales', 'discount', 'service_charge']

const LABELS: Record<ScoredField, string> = {
  date: 'Date',
  total: 'Total',
  vat_amount: 'VAT',
  vatable_sales: 'VATable sales',
  vat_exempt_sales: 'VAT-exempt sales',
  discount: 'Discount',
  service_charge: 'Service charge',
}

/** 0.62 -> "62%". Rounds down, so a score under the threshold never shows as the threshold. */
export const formatConfidence = (x: number) => `${Math.floor(x * 100 + 1e-9)}%`

/** Fields the user corrected and kept for a re-extraction are theirs, not the model's guess: 1.0. */
export function withLocked(c: Scored, locked: readonly string[]): Scored {
  if (c.overall == null) return c
  const fields: FieldConfidence = { ...c.fields }
  for (const f of SCORED_FIELDS) if (locked.includes(f)) fields[f] = 1
  return { overall: Math.min(...(Object.values(fields) as number[])), fields }
}

/** The flag to add, or null at or above the threshold (or with no score). */
export function confidenceFlag(c: Scored, min: number): string | null {
  if (c.overall == null || c.overall >= min) return null
  const unsure = SCORED_FIELDS.filter((f) => (c.fields[f] ?? 1) < min).map((f) => LABELS[f])
  const pct = formatConfidence(c.overall)
  return unsure.length === 1
    ? `The model was unsure about ${unsure[0]} (${pct}). Check it against the photo.`
    : `The model was unsure about ${unsure.join(', ')} (lowest ${pct}). Check them against the photo.`
}

/** Fields to mark on the review page: the model's shaky reads, until the receipt is confirmed. */
export function unsureFields(r: Pick<Receipt, 'status' | 'fieldConfidence' | 'minConfidence'>): ScoredField[] {
  if (r.status === 'Confirmed' || !r.fieldConfidence) return []
  return SCORED_FIELDS.filter((f) => (r.fieldConfidence[f] ?? 1) < r.minConfidence)
}
```

- [ ] **Step 4: Run them to make sure they pass, and type-check**

Run (in `web/`): `npm test` then `npx tsc -b --noEmit` (or the project's `npm run build` type-check step; see `package.json`).
Expected: the tests pass. TypeScript will complain wherever a `Receipt` literal is now missing the three fields (in `demoApi.ts` `blank()` and test fixtures). In `blank()`, add `confidence: null, fieldConfidence: {}, minConfidence: MIN_CONFIDENCE,`. In test fixtures, add the same three fields with `minConfidence: 0`.

- [ ] **Step 5: Commit**

```bash
git add web/src
git commit -m "Web: confidence on Receipt, the gate and its shared cases in TypeScript"
```

---

### Task 7: Review page and list show the score

**Files:**
- Modify: `web/src/pages/ReviewPage.tsx` (header ~line 46; `Field` ~line 478; date field ~line 363; money fields ~367-398)
- Modify: `web/src/pages/ReceiptsPage.tsx:~206`
- Modify: `web/src/index.css` (after `.badge-*`, ~line 110)

**Interfaces:**
- Consumes: `formatConfidence`, `unsureFields`, `ScoredField` (Task 6); `todayIso` from `format.ts`.

- [ ] **Step 1: Header badge**

In `ReviewPage`, next to `<StatusBadge status={receipt.status} />`:

```tsx
        {receipt.confidence != null && (
          <span className="badge badge-confidence" title="How sure the model was of the dates and amounts it read (its least sure one). You can't change it.">
            Confidence {formatConfidence(receipt.confidence)}
          </span>
        )}
```

- [ ] **Step 2: Field marker**

Extend `Field`:

```tsx
function Field(props: { label: string; hint?: string; required?: boolean; error?: string; unsure?: boolean; children: ReactNode }) {
  return (
    <label className={`field ${props.error ? 'field-invalid' : ''}`}>
      <span className="field-label">
        {props.label}
        {props.required && <span className="required"> *</span>}
        {props.unsure && (
          <span className="unsure" title="Model unsure — check against the photo">
            {' '}● unsure
          </span>
        )}
        {props.hint && <span className="muted"> · {props.hint}</span>}
      </span>
      {props.children}
      {props.error && <span className="text-error">{props.error}</span>}
    </label>
  )
}
```

In `ReviewForm`, compute `const unsure = new Set(unsureFields(receipt))` and pass `unsure={unsure.has('date')}` on Date, `'total'` on Total, `'vat_amount'` on VAT, `'vatable_sales'` on VATable sales, `'vat_exempt_sales'` on VAT-exempt sales, `'discount'` on Discount and `'service_charge'` on Service charge.

- [ ] **Step 3: "Use upload date"**

Directly after the Date `<Field>…</Field>`:

```tsx
      {unsure.has('date') && form.date !== todayIso(new Date(receipt.uploadedAtUtc)) && (
        <button type="button" className="link-button" onClick={() => set('date')(todayIso(new Date(receipt.uploadedAtUtc)))}>
          Use upload date ({todayIso(new Date(receipt.uploadedAtUtc))})
        </button>
      )}
```

If `link-button` isn't an existing class (`grep -n "link-button\|button-link" web/src/index.css`), use the class the page already uses for its small secondary buttons (e.g. the "12% included" fill button near line 385).

- [ ] **Step 4: List line**

In `ReceiptsPage.tsx`, after the issues count:

```tsx
                {r.confidence != null && (r.status === 'Extracted' || r.status === 'NeedsReview') && ` · ${formatConfidence(r.confidence)}`}
```

- [ ] **Step 5: Styles**

`web/src/index.css`, after the `.badge-*` rules:

```css
.badge-confidence { background: var(--surface-2, #eef0f3); color: inherit; margin-left: 0.5rem; }
.unsure { color: #9a6400; font-weight: 600; }
@media (prefers-color-scheme: dark) {
  .unsure { color: #f5c46b; }
}
```

Match the variable names `index.css` already uses: check the `:root` block, and replace `--surface-2` with the muted-surface token it defines.

- [ ] **Step 6: Verify in the browser**

Start the dev setup (`.claude/launch.json` or `npm run dev` through preview_start; the API on :5036). Seed a low-confidence receipt by uploading a dataset photo (with `Ollama:MinConfidence` temporarily set to `0.99` in `appsettings.Development.json`, not committed). Check:
- the badge shows "Confidence NN%",
- unsure fields have the marker,
- the date button fills the date,
- the list shows the percentage,
- a confirmed receipt shows the badge but no markers or button.

Also check dark mode (resize_window colorScheme dark). Take a screenshot as proof. Revert the temporary setting.

Run (in `web/`): `npm test` and the type-check.
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add web/src/pages/ReviewPage.tsx web/src/pages/ReceiptsPage.tsx web/src/index.css
git commit -m "Review page: confidence badge, unsure markers, Use upload date; percentage in the list"
```

---

### Task 8: Demo site applies the gate

**Files:**
- Modify: `web/src/demo/demoApi.ts` (`DemoReceipt`, `applyAnswer`, `initialReceipts`, `tick`, `startLiveRead`, `publicCopy`)
- Modify: `web/src/demo/liveReader.ts` (`Reading`, `LiveAnswer`, `send`)
- Modify: `eval/make_demo_receipts.py` (`read_all` keeps the score)
- Test: `web/src/demo/demo.test.ts`, `web/src/demo/liveReader.test.ts`

**Interfaces:**
- Consumes: `withLocked`, `confidenceFlag`, `Scored` (Task 6); `MIN_CONFIDENCE` (Task 4).
- Produces: `LiveAnswer.confidence?: Scored | null`; `model-answers.json` entries may have `confidence` (number|null) and `fieldConfidence` (object); `applyAnswer(r, fields, extractionSeconds, scored?: Scored | null)`.

- [ ] **Step 1: Write the failing tests**

In `demo.test.ts`, add:

```ts
describe('confidence (like ExtractionWorker)', () => {
  afterEach(() => {
    setLiveReader(undefined)
    setMinConfidence(MIN_CONFIDENCE)
    resetDemo()
  })

  const shaky: LiveAnswer = {
    fields: { merchant: 'X', date: '2026-08-01', total: 112, vatAmount: 12, vatableSales: 100, vatExemptSales: 0, discount: 0, serviceCharge: 0, category: 'Food' },
    extractionSeconds: 1,
    confidence: { overall: 0.5, fields: { date: 0.99, total: 0.5 } },
  }

  async function readLive(answer: LiveAnswer) {
    setLiveReader({ status: async () => ({ model: 'm', perVisitorPerHour: null }), read: async (_f, onStart) => (onStart() ? answer : null) })
    const r = await demoApi.uploadReceipt(new File(['x'], 'mine.jpg', { type: 'image/jpeg' }))
    await vi.waitFor(async () => expect((await demoApi.getReceipt(r.id)).status).not.toMatch(/Queued|Processing/))
    return demoApi.getReceipt(r.id)
  }

  it('a shaky live read needs review', async () => {
    setMinConfidence(0.85)
    const r = await readLive(shaky)
    expect(r).toMatchObject({ status: 'NeedsReview', confidence: 0.5, fieldConfidence: { total: 0.5 }, minConfidence: 0.85 })
    expect(r.flags).toEqual(['The model was unsure about Total (50%). Check it against the photo.'])
  })

  it('no score from an older PC: no flag', async () => {
    setMinConfidence(0.85)
    const r = await readLive({ ...shaky, confidence: undefined })
    expect(r).toMatchObject({ status: 'Extracted', confidence: null, fieldConfidence: {} })
  })

  it('old saved receipt without confidence still loads', async () => {
    const old = { ...(await demoApi.listReceipts())[0] } as Partial<Receipt>
    delete old.confidence
    delete old.fieldConfidence
    delete old.minConfidence
    localStorage.setItem('receipt-tracker-demo-v1', JSON.stringify([old]))
    resetDemoMemoryOnly()
    expect(await demoApi.getReceipt(old.id!)).toMatchObject({ confidence: null, fieldConfidence: {}, minConfidence: MIN_CONFIDENCE })
  })
})
```

Add `MIN_CONFIDENCE`, `setMinConfidence` and `resetDemoMemoryOnly` to the import from `./demoApi`. The test file already imports `vi`, `afterEach`, `LiveAnswer` and `Receipt`. If `localStorage` isn't available in the test environment, check how existing tests in `demo.test.ts` handle storage and follow that. If they never touch it, drop the `localStorage` lines and the `resetDemoMemoryOnly` test helper, and check `publicCopy` directly by making it exported for tests.

In `liveReader.test.ts`, add a case next to the existing successful-read test. Have the fake fetch return `{ fields: {...}, category: 'Food', extractionSeconds: 1, confidence: { overall: 0.5, fields: { total: 0.5 } } }` and assert `answer.confidence` equals `{ overall: 0.5, fields: { total: 0.5 } }`. Add a second case where the JSON has no `confidence` and assert `answer.confidence` is `null`.

- [ ] **Step 2: Run them to make sure they fail**

Run (in `web/`): `npm test -- demo liveReader`
Expected: FAIL (missing exports and fields).

- [ ] **Step 3: liveReader**

```ts
export interface LiveAnswer {
  fields: ReceiptUpdate
  extractionSeconds: number
  confidence?: Scored | null // null/missing: the PC runs a version without scores
}

interface Reading {
  fields: Omit<ReceiptUpdate, 'category'>
  category: Category | null
  extractionSeconds: number
  confidence?: Scored | null
}
```

In `send`, return `{ fields: { ...reading.fields, category: reading.category }, extractionSeconds: reading.extractionSeconds, confidence: reading.confidence ?? null }`. Import `type { Scored } from '../confidence'`.

- [ ] **Step 4: demoApi**

```ts
// Same as Ollama:MinConfidence in appsettings.json (SyncTests); the demo has no server to ask.
export const MIN_CONFIDENCE = 0
let minConfidence = MIN_CONFIDENCE

/** Tests: a threshold other than the shipped one. */
export function setMinConfidence(value: number) {
  minConfidence = value
}

/** Tests: forget the in-memory list so the next call loads what's in localStorage. */
export function resetDemoMemoryOnly() {
  receipts = undefined
}
```

(`MIN_CONFIDENCE` already exists from Task 4; keep its comment and replace the value only in Task 9.)

Update `applyAnswer`:

```ts
/** The model is done: its fields, the checks, the confidence gate, and Extracted / NeedsReview (like ExtractionWorker). */
function applyAnswer(r: DemoReceipt, fields: ReceiptUpdate, extractionSeconds: number, scored?: Scored | null) {
  Object.assign(r, fields, { extractionSeconds, readyAt: undefined })
  r.confidence = scored?.overall ?? null
  r.fieldConfidence = scored?.fields ?? {}
  r.flags = checks(r)
  const unsure = scored ? confidenceFlag(scored, minConfidence) : null
  if (unsure) r.flags.push(unsure)
  r.status = r.flags.length > 0 ? 'NeedsReview' : 'Extracted'
  finish(r)
}
```

Add `const answerScore = (a: Answer): Scored | null => ('confidence' in a && a.confidence != null ? { overall: a.confidence as number, fields: (a as { fieldConfidence?: FieldConfidence }).fieldConfidence ?? {} } : null)`. Pass `answerScore(p.answer)` / `answerScore(answer)` in `initialReceipts` and `tick`, and `answer.confidence` in `startLiveRead`. The PC already applied locked fields, so pass it as-is.

Update `publicCopy` so old saved receipts and the threshold come out right:

```ts
const publicCopy = ({ model: _model, readyAt: _readyAt, live: _live, ...r }: DemoReceipt): Receipt => ({
  ...r,
  confidence: r.confidence ?? null,
  fieldConfidence: r.fieldConfidence ?? {},
  minConfidence,
})
```

Imports: `import { confidenceFlag, type FieldConfidence, type Scored } from '../confidence'`.

- [ ] **Step 5: make_demo_receipts keeps the score**

In `eval/make_demo_receipts.py` `read_all`, add `"confidence", "fieldConfidence"` to `keep`. (The answers file is regenerated in Task 9, after the threshold is set.)

- [ ] **Step 6: Run the tests to make sure they pass**

Run (in `web/`): `npm test` and the type-check; `dotnet test … --filter SyncTests`.
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add web/src/demo eval/make_demo_receipts.py
git commit -m "Demo: same confidence gate as the app, for sample and live reads"
```

---

### Task 9: Eval scores, calibration, threshold

**Files:**
- Modify: `eval/try_vision.py` (`extract`), `eval/compare_approaches.py` (`run`), `eval/run_eval.py` (`cache_key`, `predict`, `save_model_output`)
- Create: `eval/calibrate_confidence.py`
- Modify: `eval/model_outputs/vision/*.json` (regenerated), `eval/RESULTS.md`, `README.md`
- Modify: `src/ReceiptExtractor.Api/appsettings.json`, `OllamaExtractor.cs` (`MinConfidence` default), `web/src/demo/demoApi.ts` (`MIN_CONFIDENCE`), `web/src/demo/model-answers.json`

**Interfaces:**
- Consumes: `eval/confidence.score`, `SCORED_FIELDS` (Task 2); `run_eval.load_dataset`, `field_ok`, `vat_check`.
- Produces: model output records gain `"confidence": {"overall", "fields"}`; `extract(image, model) -> tuple[Receipt, float, dict]`.

- [ ] **Step 1: Ask for logprobs in the eval**

`try_vision.extract` (add `from confidence import score` at the top):

```python
def extract(image_path: Path, model: str) -> tuple[Receipt, float, dict]:
    """The fields, the seconds the model took, and the confidence score (eval/confidence.py)."""
    start = time.perf_counter()
    resp = ollama.chat(
        model=model,
        messages=[{"role": "user", "content": PROMPT, "images": [upright(image_path)]}],
        format=Receipt.model_json_schema(),  # force the JSON shape
        think=False,                         # skip "thinking" mode; answer directly
        logprobs=True,                       # token probabilities, for the confidence score
        options={
            "temperature": 0,  # same input -> same output
            "num_ctx": 8192,   # context window; the image alone can take ~4000 tokens
        },
    )
    seconds = time.perf_counter() - start
    content = resp.message.content or ""
    # ... existing empty-answer check unchanged ...
    tokens = [{"token": t.token, "logprob": t.logprob, "bytes": getattr(t, "bytes", None)} for t in (resp.logprobs or [])]
    return Receipt.model_validate_json(content), seconds, score(content, tokens)
```

Update every caller of `extract` and of `run` (`grep -n "extract(\|run(" eval/*.py`):
- `try_vision.main` unpacks three values and prints `confidence['overall']`.
- `compare_approaches.run` returns a fifth element, `confidence` (`None` for the OCR approaches): `result, _, confidence = extract(image_path, model)`. Then `return result, None, 0.0, time.perf_counter() - start, confidence`, and the OCR path `return parse_text(text, model), text, ocr_seconds, time.perf_counter() - start, None`.

`run_eval.py`:
- `cache_key`: add `"confidence-v1"` to the hashed parts (`for part in (approach, model, PROMPT, TEXT_PROMPT, "confidence-v1"):`). Old caches then re-run once and gain scores.
- `predict`: `result, text, ocr_s, total_s, confidence = run(approach, image, model)` and add `"confidence": confidence,` to `record`.
- `save_model_output`: `slim = {k: record[k] for k in ("key", "model", "prediction", "seconds", "confidence") if k in record}`.

- [ ] **Step 2: Re-run the vision eval**

Ollama must be running with the model. Run: `.venv/Scripts/python eval/run_eval.py --only vision`
Expected: all receipts re-read (about 20 s each), the core accuracy table is printed and is unchanged or within noise (≥ 85%). Every `eval/model_outputs/vision/*.json` now has a `confidence` object with a non-null `overall`. If any `overall` is null, the scorer rejected the tokens: print `content` and the joined tokens for that receipt and fix the scorer (adding the case to `tests/confidence-cases.json`) before going on.

Then: `.venv/Scripts/python eval/run_eval.py --offline --only vision --check` (what CI runs).
Expected: passes.

- [ ] **Step 3: Calibration script**

`eval/calibrate_confidence.py`:

```python
"""
Picks Ollama:MinConfidence: for each threshold, how many misread dates/amounts the confidence score catches, and how
many correctly read receipts it would send to review for nothing. Reads the committed eval/model_outputs/vision.

Run (from the project root, after run_eval.py --only vision):
    python eval/calibrate_confidence.py
"""

import json
from pathlib import Path

from confidence import SCORED_FIELDS
from run_eval import MODEL_OUTPUTS, field_ok, load_dataset, vat_check

THRESHOLDS = [0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.97, 0.99]
MAX_FALSE_ALARMS = 0.20  # at most 1 in 5 correct receipts sent to review for nothing


def rows() -> list[dict]:
    out = []
    for image, label in load_dataset():
        path = MODEL_OUTPUTS / "vision" / f"{image.stem}.json"
        if not path.exists():
            continue
        record = json.loads(path.read_text(encoding="utf-8"))
        conf = record.get("confidence") or {}
        if conf.get("overall") is None:
            continue
        pred = record["prediction"]
        wrong = [f for f in SCORED_FIELDS if f in label and not field_ok(f, pred.get(f), label.get(f))]
        out.append({"name": image.stem, "fields": conf["fields"], "overall": conf["overall"], "wrong": wrong,
                    "checks_flag": vat_check(pred) is not None or pred.get("date") is None})
    return out


def main() -> None:
    data = rows()
    wrong_fields = [(r, f) for r in data for f in r["wrong"]]
    missed_by_checks = [(r, f) for r, f in wrong_fields if not r["checks_flag"]]
    all_right = [r for r in data if not r["wrong"]]
    print(f"{len(data)} receipts, {len(wrong_fields)} misread fields ({len(missed_by_checks)} not caught by the checks), "
          f"{len(all_right)} receipts fully right\n")

    print("| Threshold | Misreads caught | Missed by checks, caught | Right receipts flagged |")
    print("|---|---|---|---|")
    best = None
    for t in THRESHOLDS:
        caught = sum(r["fields"].get(f, 1) < t for r, f in wrong_fields)
        new = sum(r["fields"].get(f, 1) < t for r, f in missed_by_checks)
        false_alarms = sum(r["overall"] < t for r in all_right)
        fa_rate = false_alarms / len(all_right) if all_right else 0
        print(f"| {t:.2f} | {caught}/{len(wrong_fields)} | {new}/{len(missed_by_checks)} | {false_alarms}/{len(all_right)} |")
        if fa_rate <= MAX_FALSE_ALARMS and new > 0 and (best is None or new - false_alarms > best[1]):
            best = (t, new - false_alarms)

    print("\nMisread fields and their confidence:")
    for r, f in sorted(wrong_fields, key=lambda x: x[0]["fields"].get(x[1], 1)):
        print(f"  {r['fields'].get(f, 1):.3f}  {r['name']}  {f}{'' if r['checks_flag'] else '  (checks missed it)'}")

    print(f"\nSuggested MinConfidence: {best[0]}" if best else
          "\nNo threshold catches a misread the checks miss while flagging at most 20% of right receipts: "
          "the score doesn't separate right from wrong reads on this dataset. Stop and report.")


if __name__ == "__main__":
    main()
```

Make sure `MODEL_OUTPUTS` is a module-level name in `run_eval.py`. It's used by `save_model_output`, so it is.

- [ ] **Step 4: Run it, then STOP and show the user**

Run: `.venv/Scripts/python eval/calibrate_confidence.py`

**Checkpoint:** paste the table, the misread list and the suggestion to the user, and wait. If the script says "Stop and report", do not go on: explain what the numbers show and ask how to proceed (the spec's stop condition). Otherwise ask the user to confirm the suggested threshold or pick another one from the table.

- [ ] **Step 5: Set the agreed threshold everywhere**

Set the confirmed value (written below as `T`, e.g. `0.9`) in:
- `src/ReceiptExtractor.Api/appsettings.json`: `"MinConfidence": T`
- `OllamaOptions.MinConfidence` default: `= T;`
- `web/src/demo/demoApi.ts`: `export const MIN_CONFIDENCE = T`

Run: `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"` and (in `web/`) `npm test`.
Expected: all pass (SyncTests confirms the three agree).

- [ ] **Step 6: Regenerate the demo answers**

Build and run the API in Production from a scratch folder, so it uses its own data folder and not the real `receipts.db`:

```bash
dotnet build src/ReceiptExtractor.Api -c Release -o "$SCRATCH/demo-api"
cd "$SCRATCH/demo-api" && ASPNETCORE_ENVIRONMENT=Production dotnet ReceiptExtractor.Api.dll --urls http://localhost:5080
```

(run it in the background), then `.venv/Scripts/python eval/make_demo_receipts.py read --api http://localhost:5080`, then stop the API and delete `$SCRATCH/demo-api`.
Expected: `web/src/demo/model-answers.json` entries now have `confidence` and `fieldConfidence`. Run `npm test` in `web/` again: the existing tests that read the samples (`demo.test.ts`) must still pass. If a sample now lands in NeedsReview because of a low score, check whether a test expected `Extracted` for it, and tell the user about it rather than editing the expectation silently.

- [ ] **Step 7: Results and README**

- `eval/RESULTS.md`: add a "Confidence score" section with the calibration table, the misread list (field names and scores only, no OCR text), the chosen threshold and one sentence on why.
- `README.md`, per CLAUDE.md:
  - copy the table into the bake-off/results section,
  - add `eval/calibrate_confidence.py` to "Running" / "Project structure",
  - add a "What I learned" line on how well token probabilities separated misreads,
  - update "Last updated" to 2026-10-01.
- `ROADMAP.md`: `grep -n -i "confidence" ROADMAP.md`. Tick any matching item. If there is none, leave the file alone.

- [ ] **Step 8: Commit**

```bash
git add eval src/ReceiptExtractor.Api/appsettings.json src/ReceiptExtractor.Api/Extraction/OllamaExtractor.cs web/src/demo/demoApi.ts web/src/demo/model-answers.json README.md ROADMAP.md
git status --short eval/preds  # must print nothing staged: eval/preds/ is never committed
git commit -m "Eval: confidence scores on every read, calibrated MinConfidence"
```

---

### Task 10: Project notes

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add the sync rules**

Under "Project facts", after the VAT-checks line in the Frontend bullet, add:

```markdown
  Confidence score (spec `docs/superpowers/specs/2026-10-01-confidence-score-design.md`): token logprobs from Ollama →
  `ConfidenceScorer.cs` (copy in `eval/confidence.py`) → `ConfidenceGate.cs` (copy in `web/src/confidence.ts`); shared
  cases in `tests/confidence-cases.json`. `Ollama:MinConfidence` is in `appsettings.json`, the `OllamaOptions` default
  and `MIN_CONFIDENCE` in `demoApi.ts` (SyncTests); re-pick it with `eval/calibrate_confidence.py` after a model or
  prompt change.
```

- [ ] **Step 2: Full verification**

Run:
- `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/build"`
- in `web/`: `npm test`, the type-check and `npm run build:demo`
- `.venv/Scripts/python -m unittest discover -s eval -p "test_*.py"`
- `.venv/Scripts/python eval/run_eval.py --offline --only vision --check`

Expected: everything passes.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "Notes: where the confidence score lives and what to keep in sync"
```
