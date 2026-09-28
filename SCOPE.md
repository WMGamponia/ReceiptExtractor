# Receipt Tracker — Scope

## Goal
Snap or upload a receipt, get a clean record, and see where money went over any period — without receipt data leaving my machine.

## Core loop
Upload → extract → review/correct → save → summaries

## Extracted fields
| Field       | Required | Notes                                                    |
|-------------|----------|----------------------------------------------------------|
| Date        | Yes      | Normalized to YYYY-MM-DD                                 |
| Merchant    | No       | As printed on receipt                                    |
| Total       | Yes      | PHP, 2 decimals                                          |
| VAT         | No       | PHP, 2 decimals; 12% included in total; blank if non-VAT receipt |
| VATable / VAT-exempt sales | No | PHP, 2 decimals; VAT-exempt covers senior citizen / PWD purchases |
| Discount    | No       | PHP, 2 decimals; all discounts on the receipt added up (SC, PWD, promo) |
| Category    | Yes      | Auto-suggested, user can change (Food, Transport, Groceries, Health, Utilities, Other) |

## Record naming
- `<Date>_<Merchant>`, or `<Date>` if merchant is unavailable
- If date is also unreadable: `Undated - <upload timestamp>`, flagged for review

## Summaries
- Periods: daily, weekly, monthly, quarterly, yearly
- Week: Monday–Sunday (ISO weeks)
- Quarter: calendar quarters (Q1 = Jan–Mar)
- Each summary shows total spend, per-category breakdown, and receipt count

## Done when
- Upload → extracted fields in < 30s on my laptop (spec: CPU / RAM / GPU)
- ≥ 85% field accuracy on my labeled set (date, merchant, total)
- Can correct any field before saving
- Summaries by category work for all five periods
- `docker compose up` works on a fresh machine

## Constraints
- Runs fully local; no receipt data sent to external services
- Original receipt image stored alongside the record
- Inputs: JPG, PNG, and PDF (e-receipts)
- PHP only

## Non-goals (for MVP)
- Line-item extraction
- Multi-user / accounts
- Bank, GCash, or Maya sync
- Tax filing or BIR compliance features
- Native mobile app (PWA only)
- Multi-currency
