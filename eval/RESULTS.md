# Extraction bake-off results

2026-09-30 · model `qwen3-vl:8b-instruct` · 8 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | vat_exempt_sales | discount | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|---|---|
| vision | 75% | 88% | 100% | 100% | 88% | 88% | 100% | 88% ✅ | 8.9 | 17.7 ✅ | 1 |

Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.
VAT-check flags = results whose VAT breakdown doesn't add up (would be sent to review).

## Misses

| Approach | Receipt | Field | Got | Expected |
|---|---|---|---|---|
| vision | 20231209_LAZADA_Edifier | merchant | LAZADA Edifier | LAZADA |
| vision | 20240831_FLYQUIPENTERPRISES | vat_exempt_sales | 27840.0 | 0.0 |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | merchant | WAL TER-APM CENTRALLE CEBU | WCSI-APMC CENTRALLE CEBU |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | 2026-11-01 | 2026-01-11 |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | vatable_sales | 5190.0 | 0.0 |
