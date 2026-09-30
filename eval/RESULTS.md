# Extraction bake-off results

2026-09-30 · model `qwen3-vl:8b-instruct` · 18 receipts

| Approach | merchant | date | total | vat_amount | vatable_sales | vat_exempt_sales | discount | service_charge | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| vision | 100% | 67% | 89% | 89% | 83% | 89% | 94% | 100% | 85% ✅ | 9.5 | 18.6 ✅ | 4 |

Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.
VAT-check flags = results whose VAT breakdown doesn't add up (would be sent to review).

## Misses

| Approach | Receipt | Field | Got | Expected |
|---|---|---|---|---|
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | date | 2026-08-16 | 2024-08-16 |
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | total | 2259.89 | 2259.09 |
| vision | 20240816_PETRONBASAKPARDOSERVICESTATION | vatable_sales | 2017.0 | 2017.04 |
| vision | 20240831_FLYQUIPENTERPRISES | vat_exempt_sales | 27840.0 | 0.0 |
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
