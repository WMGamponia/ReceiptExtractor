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
- [ ] TypeScript PWA with camera capture
- [ ] Review-and-correct screen
- [ ] Dashboard
- [ ] Log every user correction (free eval data)

## 6. Add PH-specific touches
- [ ] PHP formatting
- [ ] VAT-inclusive math (12% baked into the total)
- [x] VAT-exempt sales and discounts (senior citizen / PWD): extracted, editable, included in the VAT check
- [ ] Sensible local categories
- [ ] CSV export for freelancers tracking expenses
- [ ] Keep it a tracker, not a tax tool

## 7. Package it
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
