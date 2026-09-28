# Receipt Tracker

Snap or upload a Philippine receipt, get a clean record, and see where your money went — **without receipt data ever leaving your machine.**

> 🚧 **Work in progress.** Extraction approach chosen (step 3 of the [roadmap](ROADMAP.md)); ASP.NET Core backend working end to end (step 4). Frontend not merged yet.
>
> _Last updated: 2026-09-27_

**Stack:** ASP.NET Core (.NET 10) · EF Core + SQLite · Ollama (`qwen3-vl:8b-instruct`) · xUnit · Python for the extraction eval · TypeScript PWA (planned)

## Why local-first

Receipts are personal data: names, addresses, TINs, what you bought and where. Everything here runs on your own computer — the AI model included — so nothing gets sent to a cloud API. That also means no per-receipt API bill, but privacy is the main reason, not cost.

## What it extracts

| Field | Example |
|---|---|
| Merchant | DataBlitz |
| Date | 2025-12-20 |
| Total | ₱10,495.00 |
| VAT amount | ₱1,124.46 |
| VATable sales | ₱9,370.54 |
| VAT-exempt sales | ₱500.00 (e.g. senior citizen / PWD purchases) |
| Discount | ₱100.00 (all discounts on the receipt, added up) |

Full scope and non-goals: [SCOPE.md](SCOPE.md).

## Extraction bake-off

Before building anything, I compared two ways to pull these fields out of real receipt photos (faded thermal paper, rotated Lazada waybills, BIR-format invoices):

- **OCR + LLM:** Tesseract reads the text, then a local LLM turns that text into fields.
- **Vision model:** a local vision-language model looks at the photo and returns the fields directly.

Both use the same model (`qwen3-vl:8b-instruct` via [Ollama](https://ollama.com)), prompt and JSON schema, so the only difference is *seeing the image* vs *reading OCR text*.

**Results on 7 hand-labeled receipts** (AMD Radeon RX 9070 XT):

| Approach | Merchant | Date | Total | VAT | Core accuracy¹ | Median | Slowest |
|---|---|---|---|---|---|---|---|
| **Vision model** | 71% | 86% | 100% | 100% | **86%** ✅ | 9.3s | 18.4s |
| Tesseract + LLM | 29% | 0% | 29% | 43% | 19% ❌ | 1.5s | 2.0s |

¹ Date + merchant + total. Target: ≥ 85%, under 30s per receipt. Full breakdown: [eval/RESULTS.md](eval/RESULTS.md).
The vision row is the latest run (2026-09-27, with VAT-exempt sales and discount in the prompt; the original bake-off scored 95%). The Tesseract row is from the original bake-off.

PaddleOCR + LLM was also tried by hand on one receipt: 4/5 fields after tuning, but ~85s per receipt, because PaddleOCR can't use an AMD GPU on Windows.

### What I learned

- **Plain OCR breaks on real-world receipts.** Tesseract returned almost nothing on 3 of 7 receipts (rotated, faded, low contrast), and it can't read stylized logos, so the store name often never reaches the LLM.
- **One misread digit ruins a field.** `15,190` became `73,190`; `Dec 21 2025` became `Dec 24 7025`.
- **Two kinds of error need two kinds of fix.** *Reading* errors (bad OCR) need a better OCR engine or a better photo. *Understanding* errors (e.g. picking the BIR "Date of ATP" printer-permit date instead of the transaction date) are fixed in the prompt.
- **PH receipts have a built-in checksum.** VAT is included in the total, so `vatable sales + VAT = total` and `VAT = total × 12/112`. Checking that flagged every bad Tesseract result and none of the vision results — it'll be used in the app to send suspicious receipts to review.
- **Prompts are suggestions; code is a rule.** Asking for "centavos exactly as printed" fixed dropped decimals, but the model still ignored a rule about marketplace names. Exact rules belong in code after extraction.

**Known issues:** adding VAT-exempt sales and discount to the prompt cost some accuracy: the WCSI receipt's merchant and date are now misread ("WAL TER-APM", 2026-11-01), and on the handwritten FLYQUIP receipt a ₱680 line item next to the empty SC/PWD discount box was read as a discount. The tax-table and "Less VAT" prompt notes came after this run, so re-run `python eval/run_eval.py --only vision` to measure the current prompt. None of the labeled receipts has a real discount yet. The Lazada receipt comes back as "LAZADA Edifier" instead of "LAZADA". Seven receipts is a small sample — the dataset is growing toward 30–50, including handwritten sari-sari receipts.

## Running the evaluation

Requirements: Windows/macOS/Linux, Python 3.12, [Ollama](https://ollama.com), and optionally [Tesseract](https://github.com/UB-Mannheim/tesseract/wiki) (Windows: `winget install -e --id UB-Mannheim.TesseractOCR`).

```bash
python -m venv .venv
.venv\Scripts\activate            # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
ollama pull qwen3-vl:8b-instruct

# one receipt, one approach
python eval/try_vision.py datasets/labels/20251220_DataBlitz.jpg

# one receipt, all approaches side by side (with raw OCR text)
python eval/compare_approaches.py datasets/labels/20251220_DataBlitz.jpg

# every labeled receipt, scored -> eval/RESULTS.md
python eval/run_eval.py --only vision tesseract
```

Predictions are cached in `eval/preds/` and re-run automatically when the model or prompt changes (`--fresh` forces a re-run).

## Running the API

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and Ollama running with `qwen3-vl:8b-instruct` pulled.

Open `ReceiptExtractor.sln` in Visual Studio and run the **http** profile, or:

```bash
dotnet run --project src/ReceiptExtractor.Api     # http://localhost:5036
dotnet test                                       # unit tests
```

| Endpoint | What it does |
|---|---|
| `POST /api/receipts` | Upload a JPG/PNG (form field `file`). Returns **202** right away; extraction runs in the background |
| `GET /api/receipts/{id}` | Status (`Queued` → `Processing` → `Extracted` / `NeedsReview` / `Failed`), fields and review flags |
| `GET /api/receipts` | All receipts, newest first |
| `GET /api/receipts/{id}/image` | The receipt image |
| `PUT /api/receipts/{id}/image` | Replace the image (e.g. rotated). Fields are kept, extraction is **not** re-run; the first version is backed up as `{id}.original.jpg` |
| `PUT /api/receipts/{id}` | Save reviewed/corrected fields → `Confirmed` |
| `POST /api/receipts/confirm` | Bulk confirm `{ "ids": [...] }` with the extracted fields as-is. Receipts still being read, failed, or missing date/total/category are skipped with a reason |
| `POST /api/receipts/{id}/retry` | Re-run a failed extraction |
| `POST /api/receipts/{id}/reextract` | Re-extract with corrections: `{ "fields": {...}, "locked": ["total", "vat_amount"] }`. Locked fields are kept and given to the model as known-correct; it re-reads the rest |
| `DELETE /api/receipts/{id}` | Delete the receipt and its image files (incl. the `.original` backup). **409** while the model is reading it |
| `POST /api/receipts/delete` | Bulk delete `{ "ids": [...] }` (records + image files). Receipts the model is reading are skipped with a reason |

`src/ReceiptExtractor.Api/ReceiptExtractor.Api.http` has ready-made requests you can click in Visual Studio. The database (`receipts.db`) and uploaded images (`storage/`) are created on first run; columns added later are patched into an existing database on startup (`Data/SchemaPatches.cs`).

**How a receipt flows:** upload → saved to disk + DB row `Queued` → background worker sends the image to Ollama with a JSON schema → model output parsed and cleaned (e.g. "LAZADA Edifier" → "LAZADA") → checks run (VAT breakdown: VATable + VAT + VAT-exempt = total, or = total + discount for senior citizen / PWD discounts; VAT = 12% of VATable sales; date; total) → `Extracted`, or `NeedsReview` with reasons → user confirms/corrects → `Confirmed`. If a correction should change other fields too (e.g. the right VAT line), **Re-extract** keeps the corrected fields, tells the model they're correct, and re-reads the rest. The model's raw output is kept, so user corrections can later become new eval data.

## Project structure

```
ReceiptExtractor.sln
src/ReceiptExtractor.Api/
  Endpoints/     HTTP endpoints (upload, status, confirm, retry, delete)
  Extraction/    Ollama client, background queue + worker
  Validation/    VAT math, date checks, merchant clean-up
  Data/          EF Core entity + DbContext (SQLite)
tests/ReceiptExtractor.Tests/   xUnit tests
datasets/
  labels/        receipt images
  receipts/      hand-labeled ground truth (one JSON per image)
eval/
  try_vision.py            one receipt through the vision model
  compare_approaches.py    vision vs Tesseract vs PaddleOCR on one receipt
  run_eval.py              score every approach on every receipt
  merchant_aliases.json    store name ↔ company name equivalents
  RESULTS.md               latest scores
SCOPE.md         what the MVP does and doesn't do
ROADMAP.md       steps to finish
```

## Privacy note

`eval/preds/` holds the full OCR text of each receipt (including TINs), so it's in `.gitignore`. Receipt images must have TINs and card digits redacted before they're committed.
