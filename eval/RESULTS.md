# Extraction bake-off results

2026-09-27 · model `qwen3-vl:8b-instruct` · 7 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | vat_exempt_sales | discount | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|---|---|
| vision | 71% | 86% | 100% | 100% | 100% | 100% | 86% | 86% ✅ | 9.3 | 18.4 ✅ | 0 |

Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.
VAT-check flags = results whose VAT breakdown doesn't add up (would be sent to review).

## Misses

| Approach | Receipt | Field | Got | Expected |
|---|---|---|---|---|
| vision | 20231209_LAZADA_Edifier | merchant | LAZADA Edifier | LAZADA |
| vision | 20240831_FLYQUIPENTERPRISES | discount | 680.0 | 0.0 |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | merchant | WAL TER-APM CENTRALLE CEBU | WCSI-APMC CENTRALLE CEBU |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | 2026-11-01 | 2026-01-11 |
