# Extraction bake-off results

2026-10-01 · model `qwen3-vl:8b-instruct` · 18 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | vat_exempt_sales | discount | service_charge | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| vision | 100% | 67% | 89% | 89% | 83% | 94% | 94% | 100% | 85% ✅ | 9.5 | 17.9 ✅ | 4 |

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
| vision | 20260801_SRMembershipShopping | date | 2021-08-26 | 2026-08-01 |
| vision | 20260821_BarbecueHouse | date | 2026-03-21 | 2026-08-21 |
| vision | 20260821_BarbecueHouse | vat_amount | 233.21 | 466.43 |
| vision | 20260821_BarbecueHouse | vatable_sales | 1943.45 | 3886.92 |
| vision | 20260821_BarbecueHouse | vat_exempt_sales | 5441.67 | 1943.45 |
| vision | 20260821_BarbecueHouse | discount | 388.0 | 388.69 |
| vision | 20260910_CebuPSIHoseCenter | date | 2024-05-10 | 2026-09-10 |
| vision | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vat_amount | 27.81 | 32.15 |
| vision | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vatable_sales | 267.81 | 267.85 |
| vision | 20260928_TRI-JMarketing | total | 740.0 | 750.0 |

## Confidence score

From `python eval/calibrate_confidence.py` (2026-10-01, same 18 receipts). A field's confidence is the probability of its
least likely token; a receipt's is its lowest field (`ConfidenceScorer.cs`). 15 fields were misread, 4 of them on receipts
the VAT/total checks didn't flag; 10 receipts were read fully right.

| Threshold | Misreads caught | Missed by checks, caught | Receipts the checks pass, sent to review | Right receipts flagged |
|---|---|---|---|---|
| 0.50 | 2/15 | 0/4 | 0/4 | 0/10 |
| 0.60 | 3/15 | 0/4 | 0/4 | 0/10 |
| 0.70 | 4/15 | 0/4 | 0/4 | 1/10 |
| 0.80 | 5/15 | 0/4 | 0/4 | 1/10 |
| 0.85 | 6/15 | 1/4 | 1/4 | 1/10 |
| 0.90 | 6/15 | 1/4 | 3/4 | 1/10 |
| **0.95** | **8/15** | **2/4** | **4/4** | **1/10** |
| 0.97 | 12/15 | 3/4 | 4/4 | 1/10 |
| 0.99 | 12/15 | 3/4 | 4/4 | 5/10 |

Misread fields and their confidence:

| Confidence | Receipt | Field | Checks flagged it |
|---|---|---|---|
| 0.440 | 20240816_PETRONBASAKPARDOSERVICESTATION | date | yes |
| 0.495 | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vat_amount | yes |
| 0.595 | 20260821_BarbecueHouse | date | yes |
| 0.614 | 20260821_BarbecueHouse | discount | yes |
| 0.727 | 20260910_CebuPSIHoseCenter | date | yes |
| 0.831 | 20260928_TRI-JMarketing | total | no |
| 0.901 | 20240816_PETRONBASAKPARDOSERVICESTATION | total | yes |
| 0.920 | 20260703_JNTIULPGSTORE | date | no |
| 0.952 | 20260821_BarbecueHouse | vat_exempt_sales | yes |
| 0.959 | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | no |
| 0.962 | 20240816_PETRONBASAKPARDOSERVICESTATION | vatable_sales | yes |
| 0.969 | 20260919_CEBUSEALTRUSTAUTOSUPPLY | vatable_sales | yes |
| 0.999 | 20260801_SRMembershipShopping | date | no |
| 0.999 | 20260821_BarbecueHouse | vat_amount | yes |
| 0.999 | 20260821_BarbecueHouse | vatable_sales | yes |

**Chosen: `Ollama:MinConfidence` = 0.95** (the script's suggestion). The gate looks at a receipt's lowest field, so it
counts receipts: at 0.95 all 4 receipts with a misread the checks miss go to review. 0.95 and 0.97 send the same 9 of
18 receipts to review (the checks alone send 4), but correctly read receipts score 0.980 and up, and the app scores a little lower than the
eval (its prompt also asks for the category: one right receipt scored 0.954 in the app, 0.980 here), so 0.97 would send
right receipts to review. The one right receipt flagged at 0.95 scored 0.686. SR Membership's wrong date (0.999) is
confidently wrong and no threshold catches it.

Asking Ollama for token probabilities added about 0.2 s per receipt (measured with and without). The timings in
`model_outputs/` for this run are slower than usual because another app was using the GPU.
