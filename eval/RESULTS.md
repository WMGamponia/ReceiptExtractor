# Extraction bake-off results

2026-10-01 · model `qwen3-vl:8b-instruct` · 18 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | vat_exempt_sales | discount | service_charge | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| vision | 100% | 72% | 89% | 89% | 83% | 94% | 94% | 100% | 87% ✅ | 9.6 | 19.5 ✅ | 4 |

Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.
VAT-check flags = results whose VAT breakdown doesn't add up (would be sent to review).

## Misses

| Approach | Receipt | Field | Got | Expected |
|---|---|---|---|---|
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | date | 2026-08-16 | 2024-08-16 |
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | total | 2259.89 | 2259.09 |
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | vatable_sales | 2017.0 | 2017.04 |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | 2026-11-01 | 2026-01-11 |
| vision | 20260703_JNTIULPGSTORE | date | 2025-09-29 | 2026-07-03 |
| vision | 20260821_BarbecueHouse | date | 2026-02-21 | 2026-08-21 |
| vision | 20260821_BarbecueHouse | vat_amount | 233.21 | 466.43 |
| vision | 20260821_BarbecueHouse | vatable_sales | 1943.45 | 3886.92 |
| vision | 20260821_BarbecueHouse | vat_exempt_sales | 5441.67 | 1943.45 |
| vision | 20260821_BarbecueHouse | discount | 388.81 | 388.69 |
| vision | 20260910_CebuPSIHoseCenter | date | 2024-05-10 | 2026-09-10 |
| vision | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vat_amount | 36.0 | 32.15 |
| vision | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vatable_sales | 26785.0 | 267.85 |
| vision | 20260928_TRI-JMarketing | total | 740.0 | 750.0 |

## Confidence score

From `python eval/calibrate_confidence.py` (2026-10-01, same 18 receipts, re-run after the dataset photos were redacted).
A field's confidence is the probability of its least likely token; a receipt's is its lowest field (`ConfidenceScorer.cs`).
14 fields were misread, 3 of them on receipts the VAT/total checks didn't flag; 11 receipts were read fully right.

| Threshold | Misreads caught | Missed by checks, caught | Receipts the checks pass, sent to review | Right receipts flagged |
|---|---|---|---|---|
| 0.50 | 2/14 | 0/3 | 0/3 | 0/11 |
| 0.60 | 4/14 | 1/3 | 1/3 | 0/11 |
| 0.70 | 5/14 | 1/3 | 1/3 | 1/11 |
| 0.80 | 7/14 | 2/3 | 2/3 | 1/11 |
| 0.85 | 7/14 | 2/3 | 2/3 | 1/11 |
| 0.90 | 8/14 | 2/3 | 3/3 | 1/11 |
| **0.95** | **9/14** | **2/3** | **3/3** | **2/11** |
| 0.97 | 11/14 | 3/3 | 3/3 | 2/11 |
| 0.99 | 13/14 | 3/3 | 3/3 | 6/11 |

Misread fields and their confidence:

| Confidence | Receipt | Field | Checks flagged it |
|---|---|---|---|
| 0.440 | 20240816_PETRONBASAKPARDOSERVICESTATION | date | yes |
| 0.478 | 20260821_BarbecueHouse | date | yes |
| 0.508 | 20260703_JNTIULPGSTORE | date | no |
| 0.595 | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vatable_sales | yes |
| 0.646 | 20260821_BarbecueHouse | discount | yes |
| 0.727 | 20260910_CebuPSIHoseCenter | date | yes |
| 0.799 | 20260928_TRI-JMarketing | total | no |
| 0.890 | 20260821_BarbecueHouse | vat_exempt_sales | yes |
| 0.901 | 20240816_PETRONBASAKPARDOSERVICESTATION | total | yes |
| 0.959 | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | no |
| 0.962 | 20240816_PETRONBASAKPARDOSERVICESTATION | vatable_sales | yes |
| 0.983 | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vat_amount | yes |
| 0.989 | 20260821_BarbecueHouse | vatable_sales | yes |
| 0.998 | 20260821_BarbecueHouse | vat_amount | yes |

**Chosen: `Ollama:MinConfidence` = 0.95**, picked on the first calibration run (before the redaction), where it was the
script's suggestion: at 0.95 all 4 receipts with a misread the checks missed went to review, and 0.97 would have sent right
receipts to review (the app scores a little lower than the eval: its prompt also asks for the category).

After the redaction the script suggests 0.90: 0.90 and 0.95 both send all 3 such receipts to review (the gate looks at a
receipt's lowest field, and WCSI's 0.959 date sits on the same receipt as an 0.876 field), but 0.95 also flags SR Membership,
read fully right at 0.948. The other flagged right receipt (Flyquip) scored 0.686. Kept at 0.95 for now; re-pick it when more
receipts are labeled. The confidently wrong SR Membership date (0.999) from the first run is read right now.

Asking Ollama for token probabilities added about 0.2 s per receipt (measured with and without). The timings in
`model_outputs/` are from a fresh run of all 18 receipts on an idle GPU.
