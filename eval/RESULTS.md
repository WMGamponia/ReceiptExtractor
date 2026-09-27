# Extraction bake-off results

2026-09-27 · model `qwen3-vl:8b-instruct` · 7 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|
| vision | 71% | 86% | 100% | 100% | 100% | 86% ✅ | 8.6 | 18.7 ✅ | 0 |

Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.
VAT-check flags = results where vatable_sales + vat ≠ total (would be sent to review).

## Misses

| Approach | Receipt | Field | Got | Expected |
|---|---|---|---|---|
| vision | 20231209_LAZADA_Edifier | merchant | LAZADA Edifier | LAZADA |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | merchant | WAL TER-APM CENTRALLE CEBU | WCSI-APMC CENTRALLE CEBU |
| vision | 20260111_WCSI-APMC_CENTRALLE_CEBU | date | 2026-11-01 | 2026-01-11 |
| tesseract | 20231209_LAZADA_Edifier | merchant | None | LAZADA |
| tesseract | 20231209_LAZADA_Edifier | date | None | 2023-12-09 |
| tesseract | 20231209_LAZADA_Edifier | total | None | 8443.0 |
| tesseract | 20240831_FLYQUIPENTERPRISES | date | 2023-09-14 | 2024-08-31 |
| tesseract | 20240831_FLYQUIPENTERPRISES | total | None | 27840.0 |
| tesseract | 20251220_DataBlitz | merchant | Orion 4 : spheid, Inc. | DataBlitz |
| tesseract | 20251220_DataBlitz | date | 2025-04-10 | 2025-12-20 |
| tesseract | 20251220_DataBlitz | vat_amount | 1200.0 | 1124.46 |
| tesseract | 20251220_DataBlitz | vatable_sales | 8700.0 | 9370.54 |
| tesseract | 20251221_DataBlitz | merchant | COM Nears | DataBlitz |
| tesseract | 20251221_DataBlitz | date | 2025-12-24 | 2025-12-21 |
| tesseract | 20251221_DataBlitz | total | 73190.0 | 15190.0 |
| tesseract | 20251221_DataBlitz | vat_amount | 827.5 | 1627.5 |
| tesseract | 20251221_DataBlitz | vatable_sales | 6552.5 | 13562.5 |
| tesseract | 20251229_DataBlitz | date | 2024-04-09 | 2025-12-29 |
| tesseract | 20251229_DataBlitz | vat_amount | 14895.0 | 1595.89 |
| tesseract | 20251229_DataBlitz | vatable_sales | 0.0 | 13299.11 |
