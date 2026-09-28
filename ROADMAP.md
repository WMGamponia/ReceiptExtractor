# Receipt Tracker — Roadmap

Steps to take the project from scope to finished.

## 1. Lock the MVP scope
- [ ] Core loop: snap receipt → extract merchant, date, total, VAT → confirm/fix → summaries (daily, weekly, monthly, quarterly, yearly)
- [ ] Write down non-goals (multi-user, bank sync, etc.) so the project doesn't balloon

## 2. Build the dataset first
- [ ] Collect 30–50 of my own receipts: faded thermal paper, handwritten sari-sari store receipts, crumpled Grab and Mercury Drug receipts
- [ ] Hand-label each one as JSON (ground truth)
- [ ] Redact TINs and card digits before committing anything to the repo

## 3. Bake off extraction approaches ✅
- [x] **A:** Traditional OCR (PaddleOCR or Tesseract) + local LLM for parsing
- [x] **B:** Local vision model via Ollama reading the image directly
- [x] Score field-level accuracy against the labels and pick the winner
- [x] Save the comparison table for the README

**Winner: vision (`qwen3-vl:8b-instruct`)** — 95% core accuracy, slowest receipt 17.5s, on 7 receipts. Tesseract + LLM: 19%. See `eval/RESULTS.md`.

## 4. Build the backend ✅
- [x] ASP.NET Core (.NET 10) + SQLite via EF Core — chosen to match C#/.NET job postings
- [x] Run extraction as a background job with a status endpoint (`POST` returns 202, poll `GET /api/receipts/{id}`)
- [x] Validate output against a strict schema (Ollama structured output + parsing)
- [x] Flag suspicious fields for review (VAT math, missing/future date, missing total)
- [x] Unit tests for VAT math, merchant rules, parsing, record naming
- [x] First end-to-end run against Ollama on my machine (tested with Postman)

## 5. Build the frontend
- [x] TypeScript PWA with camera capture (React + Vite, installable, `web/`)
- [x] Review-and-correct screen (image beside the form, live VAT check, future-date warning, fills in VAT / VATable sales from the total)
- [x] Image viewer: rotate, zoom, pan; save the rotation without re-running extraction (zoom is view-only)
- [x] Bulk confirm from the To-review list (checkboxes + "select all ready to confirm")
- [x] Delete receipts — one from the review screen, or bulk from the Confirmed / All tabs (removes records and image files)
- [x] List tab kept in the URL, so saving or cancelling a receipt returns to the tab you came from
- [x] Dashboard (daily → yearly, per-category totals, recent-period chart) + `GET /api/summary`
- [x] Re-extract with corrections: fields the user fixed are locked and given to the model as known-correct; it re-reads the rest
- [x] Log every user correction (free eval data) — stored per receipt as "field: model → you"
- [x] First end-to-end run on my machine (laptop + phone)

## 6. Add PH-specific touches
- [x] PHP formatting: ₱10,495.00 everywhere (also in backend messages, whatever the PC's region); amounts in the form with commas; buttons to move a misplaced decimal point
- [x] VAT-inclusive math (12% baked into the total): "Work out VAT from the total" splits a total into VATable sales + VAT
- [x] VAT-exempt sales and discounts (senior citizen / PWD): extracted, editable, included in the VAT check
- [x] Sensible local categories: Food, Groceries, Transport, Fuel, Health, Utilities, Shopping, Other, with PH examples in the prompt (Mercury Drug → Health, Petron → Fuel, Lazada → Shopping)
- [ ] CSV export for freelancers tracking expenses
- [x] Keep it a tracker, not a tax tool (README: "A tracker, not a tax tool")

## 7. Package it
- [x] Windows: single `ReceiptTracker.exe` (API + web app) via `publish.cmd`; data stored next to the exe
- [ ] Single `docker compose up` starts Ollama, API, database, and web app
- [ ] Model-pull script
- [ ] Seed data

## 8. Tests and CI
- [ ] Unit tests for parsing and VAT math
- [ ] Cache model outputs so the eval runs in GitHub Actions without a GPU

## 9. Deploy a demo
- [ ] Frontend on Cloudflare Pages in "demo mode" with pre-parsed sample receipts (no hosted model, no bill)
- [ ] Record a 30-second GIF of the real local flow

## 10. Write the README
- [ ] Architecture diagram
- [ ] Eval results table
- [ ] Tradeoffs made
- [ ] "Why local-first" section — receipts are personal data, so on-device is a real feature, not just a cost hack
