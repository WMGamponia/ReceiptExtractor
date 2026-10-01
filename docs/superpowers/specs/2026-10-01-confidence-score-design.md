# Confidence score: keep shaky reads out of "Ready to confirm"

Status: approved design, 2026-10-01. Implementation plan: to follow.

## Goal

Every extraction gets a read-only confidence score built from the model's own token probabilities. A receipt whose
score is below a configured threshold is not "Ready to confirm": it goes to Needs review, so "Select all ready to
confirm" skips it.

Success:
- Wrong reads that pass the VAT/total checks are caught more often than today.
- Correct receipts aren't sent to review much more often than today (numbers in `eval/RESULTS.md`).
- The user sees the overall score and which fields the model was unsure about, and can't edit any of it.

## Decisions

- **Token probabilities, not self-reported confidence.** Ollama (≥ 0.12, we run 0.34) returns per-token logprobs for
  the same `/api/chat` request when `logprobs: true` is sent. A model asked to rate itself mostly answers "0.95";
  a score built from our own checks would only repeat the flags.
- **Scored fields: `date` and the six money fields** (`total`, `vat_amount`, `vatable_sales`, `vat_exempt_sales`,
  `discount`, `service_charge`). Merchant and category are not scored: merchant is a name with many acceptable
  spellings, category is a judgement call.
- **Field confidence = probability of the value's least-likely token.** One shaky digit is the error we want to
  catch; an average would hide it. A `null`/`0` answer is scored too ("not present" can be the shaky part).
- **Overall confidence = the lowest field confidence.**
- **The low-confidence gate is a flag**, so the existing rule (flags ⇒ NeedsReview) does the rest: status, bulk
  confirm, list badges and the demo all work unchanged.
- **Threshold is config, calibrated once:** `Ollama:MinConfidence` (next to the model settings) in `appsettings.json`,
  default chosen from the eval sweep below. Not adjustable in the UI.
- **A low-confidence date is never replaced automatically** (e.g. with the upload date: receipts are often
  photographed days later, and a silent replacement would hide the problem and skew summaries). The review page
  offers a one-click "Use upload date" next to a shaky date instead.

## Scoring (`Extraction/ConfidenceScorer.cs`)

A pure static class, unit-tested without Ollama:

```
Score(string json, IReadOnlyList<TokenLogprob> tokens) -> Confidence(double? Overall, IReadOnlyDictionary<string, double> Fields)
```

- Rebuild the JSON text from the tokens, tracking each token's character span. Ollama's `bytes` are used when a
  token isn't valid UTF-8 on its own.
- Find each scored key's value span in that text (after `"key":`, up to the next `,` / `}` at the same depth,
  quotes excluded for the date string). Tokens overlapping the value span, ignoring whitespace-only ones, count
  for that field. A token straddling key punctuation and the value (e.g. `": 1`) counts.
- Field confidence = `exp(min logprob)` over those tokens, rounded to 3 decimals. Fields missing from the JSON are
  left out.
- If the tokens don't reproduce the content exactly, or no tokens are returned, the result is "no score"
  (`Overall = null`, empty fields) — never a guess.

`OllamaExtractor` sends `logprobs = true` and passes the response's `logprobs` to the scorer. `ExtractionResult` gains
`Confidence Confidence`. `ParseModelOutput` (used by tests and seeding) returns "no score".

## Storage and API

- `ReceiptRecord`: `double? Confidence`, `Dictionary<string, double> FieldConfidence` (JSON text column, model JSON
  names as keys). Both go in `Data/SchemaPatches.cs`.
- `ReceiptResponse`: `Confidence`, `FieldConfidence` (camelCase keys to match the rest of the API, e.g.
  `vatAmount`), and `MinConfidence` (the configured threshold, so the web app has one source of truth).
- `ReceiptUpdate` / `ReextractRequest` don't accept them: they're not editable.
- Older and seeded receipts: `null` / empty. No score is shown and nothing is flagged.

## The gate (`ExtractionWorker`)

After `ReceiptValidator.Validate`:

- Fields the user locked for a re-extraction count as 1.0 (their correction shouldn't trigger "model unsure").
  Overall is recomputed as the minimum after that.
- If `Overall < MinConfidence`, add one flag, listing fields in form order:
  `The model was unsure about Total, Date (lowest 62%). Check them against the photo.`
- The score kept on the record is the latest extraction's (it describes the fields currently shown).

On confirm the flags are recomputed by `ReceiptValidator` as today, so the "unsure" flag drops away; the score stays
on the record as the model's read (useful for later calibration). Bulk confirm already skips NeedsReview.

## Web app

- `api.ts`: `confidence`, `fieldConfidence`, `minConfidence` on `Receipt`.
- Review page: a read-only "Confidence 71%" badge next to the status. Each scored input below `minConfidence` gets
  an amber marker, tooltip "Model unsure — check against the photo". A shaky date also shows a
  "Use upload date (2026-09-30)" button that fills the date input (the user still confirms).
- Receipts list: the percentage in the existing status line, e.g. "Needs review · 1 issue · 62%".

## Demo site and live demo

- `make_demo_receipts.py read` stores `confidence` and `fieldConfidence` in `model-answers.json`; `demoApi.ts`
  applies the same gate as `ExtractionWorker` with the same default threshold (one constant, checked by a test
  against `appsettings.json`).
- Live demo: `LiveReading` gains `Confidence`; `liveReader.ts` passes it through, and demoApi gates it. Nothing new is
  logged or stored.

## Eval and calibration

- `try_vision.py` asks for logprobs and scores them with a Python port of the scorer. The cached
  `eval/model_outputs/vision` records gain `confidence` and `field_confidence` (numbers only, nothing sensitive).
- New `eval/calibrate_confidence.py`: for thresholds 0.50–0.99 it reports, over the dataset,
  - share of wrong scored fields that fall below the threshold (caught),
  - share of receipts with all scored fields right that would be flagged (false alarms),
  - how many of the caught ones the VAT/total checks didn't already flag (what the score adds).
- The default threshold is picked from that table; table and choice go in `eval/RESULTS.md` and the README.
- **Stop condition:** if the scores don't separate right from wrong reads (no threshold catches meaningfully more
  than it falsely flags), stop and report before shipping a threshold.
- Scorer cases shared by C# and Python live in `tests/confidence-cases.json` (same idea as `tests/vat-cases.json`).

## Testing

- xUnit: scorer cases (split tokens like `"1"`, `"2"`, `".5"`; `null` values; nested whitespace; mismatched tokens
  ⇒ no score); worker adds the flag below the threshold and not above; locked fields count as 1.0; no logprobs ⇒
  no flag; endpoint returns the score and an update can't set it; schema patch adds the columns to an old DB.
- Vitest: demoApi gate, review badge/markers, "Use upload date".
- pytest: Python scorer against `tests/confidence-cases.json`.

## Out of scope

- Confidence for merchant or category.
- A UI to change the threshold.
- Re-scoring receipts extracted before this change.
