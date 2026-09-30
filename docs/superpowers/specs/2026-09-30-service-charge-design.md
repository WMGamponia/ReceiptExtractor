# Service charge: extract it and count it in the VAT check

Status: approved design, 2026-09-30. Implementation plan: to follow.

## Goal

Restaurant receipts that add a service charge on top of the VAT breakdown stop being flagged as "doesn't add up".
Two new dataset receipts show the problem:

| Receipt | VATable + VAT + exempt − discount | Service charge | Total |
|---|---|---|---|
| Texas Roadhouse (2026-08-08) | 3,011.16 + 361.34 + 3,011.16 − 602.23 = 5,781.43 | 542.01 | 6,323.43 |
| Barbecue house (2026-08-21) | 3,886.92 + 466.43 + 1,943.45 − 388.69 = 5,908.11 | 590.81 | 6,498.91 |

In both, the gap between the breakdown and the total is exactly the printed service charge (10% of the pre-VAT amount).

Success:
- Both receipts pass the VAT check in C#, TS and Python, from their labels and from a correct model answer.
- A service charge that doesn't account for the gap is still flagged.
- The model reads the service charge, the user sees and can correct it on the review page, and it's in the CSV.
- Eval core accuracy stays ≥ 85%.

## Decisions

- **A real field, not a tolerance.** Guessing "a 5–10% gap is probably a service charge" would also let through a
  misread that happens to be off by that much, which is the kind of error the check exists to catch.
- **Name:** `service_charge` (eval, labels, model JSON), `ServiceCharge` (C#), `serviceCharge` (TS). Positive amount,
  0 or null when there is none, exactly like `discount`.
- **Inside or on top, like the discount.** Some PH receipts put the service charge inside VATable sales, others (both
  of ours) add it after the breakdown. The check accepts either.
- **Tips are not service charges.** The prompt says so; tips stay out of every field.

## The check

Today: `breakdown = vatable + vat + exempt`; the receipt adds up if `breakdown = total` or
`breakdown − discount = total` (within 2 centavos).

New: it adds up if `breakdown − d + s = total` for some `d ∈ {0, discount}` and `s ∈ {0, service charge}`
(duplicates skipped when either is 0). The VAT = 12% of VATable rule is unchanged.

The flag message names what was tried, e.g.
"VATable sales + VAT + VAT-exempt sales = ₱6,383.66 (₱5,781.43 after the ₱602.23 discount, ₱6,323.44 with the
₱542.01 service charge), but total is ₱6,300.00." The exact wording is settled in the plan; it must stay identical
between C# and TS (the shared cases compare flag counts, not text).

Where it lives (all three must agree; `tests/vat-cases.json` enforces it):
- `ReceiptValidator.VatFlags` (C#)
- `vatWarnings` in `web/src/format.ts` (TS), and `solveVat` there (the review page's auto-fill of VAT / VATable)
  also considers the service charge
- `vat_check` in `eval/run_eval.py` (Python)

New shared cases in `tests/vat-cases.json` (every case gains a `service_charge` key, null where it doesn't apply):
- Texas Roadhouse: exempt + discount + service charge on top → 0 problems
- Barbecue house: same shape → 0 problems
- Service charge inside the breakdown (VATable includes it) → 0 problems
- Service charge that doesn't cover the gap → 1 problem

## Where the field goes (following `discount`)

Extraction:
- `OllamaExtractor.Prompt` and `eval/try_vision.py` (kept identical; `SyncTests` checks): one line, roughly
  "service_charge: the service charge (often 10% at restaurants), positive; not tips; 0 if none". Also the JSON
  schema (`required` list) and the model DTO, stored as a positive amount like the discount.
- `UserHints`: a lockable `service_charge` field, and the "must add up" hint line mentions it.

Storage and API:
- `ReceiptRecord.ServiceCharge` + `SchemaPatches` entry (`TEXT NULL`), `ReceiptFields.ServiceCharge = null`.
- Receipt response, update request and re-extract request carry it; a negative value is a validation error
  ("Enter the service charge as a positive amount."), same as the discount.
- Corrections: compared like the discount.
- CSV: a "Service charge" column after "Discount" (API `ReceiptCsv` and demo `csv.ts`).

Web:
- `api.ts` types, review page: a "Service charge (₱)" money input next to the discount, which keeps a filled-in VAT
  field in step the same way the discount does.
- Demo: `demoApi.ts` (checks, corrections, update/re-extract validation), `liveReader.ts` types. Made-up sample
  receipts stay without a service charge.

Seeding: `LabeledReceipts` reads the field from the label JSON.

## Data and eval

- Every label JSON in `datasets/receipts/` gets `"service_charge"`: 542.01 (Texas Roadhouse), 590.81 (barbecue house),
  0.00 for the rest. The Python `Receipt` model gains the field, so it's scored like the others.
- The prompt changes, so: re-run `python eval/run_eval.py --only vision`, commit `eval/model_outputs/` and
  `RESULTS.md`; re-run the `read` step of `eval/make_demo_receipts.py` for the demo's model answers.
- The 11 new dataset receipts (verified 2026-09-30) and `merchant_aliases.json` additions go in the same PR.

## Testing

- Shared VAT cases above, run by the C#, TS and Python tests.
- Extractor: the service charge is read from the model JSON and made positive; missing counts as null.
- API: update with a negative service charge → 400; CSV has the column; correction recorded when changed.
- Schema patch: an old DB gets the column.
- Seed test: `Labeled_receipts_wait_in_review_with_their_photos` currently assumes every label passes the check. The
  PSI Hose receipt legitimately doesn't (its handwritten VAT + VATable = 230, total 250), so the test asserts each
  labeled receipt's status follows its flags (NeedsReview when flagged, Extracted otherwise) instead.
- Web: review page shows and saves the service charge; demo checks use it.

## Docs

README: bake-off table from the new RESULTS.md, dataset size, "What I learned" (service charges on restaurant
receipts). ROADMAP checkboxes if one applies. CLAUDE.md is unchanged (the sync rules already cover this).

## Out of scope

- Tips.
- Service charge in the made-up sample receipts (API `SampleReceipts`, demo `sampleReceipts.ts`).
- Any other charge (delivery fee, packaging): same pattern later if a receipt needs it.
