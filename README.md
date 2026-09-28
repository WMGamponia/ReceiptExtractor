# Receipt Tracker

Snap or upload a Philippine receipt, get a clean record, and see where your money went — **without receipt data ever leaving your machine.**

**Try the demo: https://receipt-tracker-demo.pages.dev** (made-up receipts, runs in your browser, no install; see [Demo site](#demo-site-no-install)).

![The real app on a PC: a receipt is uploaded, the local model reads it in a few seconds, the swapped date is fixed in the review screen, and the receipt counts toward August's spending](docs/demo.gif)

*The real app (Docker, `qwen3-vl:8b-instruct` on an AMD GPU) with a synthetic receipt: the model reads `08/09/2026` as September 8, the date is fixed in review ("You corrected"), and it shows up in August's spending.*

> 🚧 **Work in progress.** Extraction approach chosen (step 3 of the [roadmap](ROADMAP.md)); ASP.NET Core backend working end to end (step 4); React + TypeScript PWA built and tested end to end on laptop + phone (step 5); PH-specific touches done (step 6): local categories, peso formatting, VAT-inclusive math, CSV export. Packaged (step 7): a single Windows exe, or `docker compose up` with demo data. CI on every PR (step 8): tests, the eval scored from cached model outputs, the Docker build. Demo site live and demo GIF recorded (step 9), plus an on-demand live demo that reads visitors' own photos on my PC.
>
> _Last updated: 2026-09-29_

**Stack:** ASP.NET Core (.NET 10) · EF Core + SQLite · Ollama (`qwen3-vl:8b-instruct`) · xUnit · React + TypeScript PWA (Vite) · Python for the extraction eval

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
| Category | Shopping — one of Food, Groceries, Transport, Fuel, Health, Utilities, Shopping, Other (suggested by the model, you can change it) |

Full scope and non-goals: [SCOPE.md](SCOPE.md).

### A tracker, not a tax tool

This shows where your money went. It doesn't file anything with the BIR, compute your taxes or claim a receipt is valid for deductions. The VAT check only confirms that a receipt's own numbers add up (VATable sales + 12% VAT = total), to catch misreads.

## Extraction bake-off

Before building anything, I compared two ways to pull these fields out of real receipt photos (faded thermal paper, rotated Lazada waybills, BIR-format invoices):

- **OCR + LLM:** Tesseract reads the text, then a local LLM turns that text into fields.
- **Vision model:** a local vision-language model looks at the photo and returns the fields directly.

Both use the same model (`qwen3-vl:8b-instruct` via [Ollama](https://ollama.com)), prompt and JSON schema, so the only difference is *seeing the image* vs *reading OCR text*.

**Results on 7 hand-labeled receipts** (AMD Radeon RX 9070 XT):

| Approach | Merchant | Date | Total | VAT | Core accuracy¹ | Median | Slowest |
|---|---|---|---|---|---|---|---|
| **Vision model** | 71% | 86% | 100% | 100% | **86%** ✅ | 9.0s | 16.8s |
| Tesseract + LLM | 29% | 0% | 29% | 43% | 19% ❌ | 1.5s | 2.0s |

¹ Date + merchant + total. Target: ≥ 85%, under 30s per receipt. Full breakdown: [eval/RESULTS.md](eval/RESULTS.md).
The vision row is the latest run (2026-09-28, the current prompt: VAT-exempt sales, discount, tax tables and "Less VAT" lines; the original bake-off scored 95%). The Tesseract row is from the original bake-off. CI re-scores the vision row on every PR (see [Continuous integration](#continuous-integration)).

PaddleOCR + LLM was also tried by hand on one receipt: 4/5 fields after tuning, but ~85s per receipt, because PaddleOCR can't use an AMD GPU on Windows.

### What I learned

- **Plain OCR breaks on real-world receipts.** Tesseract returned almost nothing on 3 of 7 receipts (rotated, faded, low contrast), and it can't read stylized logos, so the store name often never reaches the LLM.
- **One misread digit ruins a field.** `15,190` became `73,190`; `Dec 21 2025` became `Dec 24 7025`.
- **Two kinds of error need two kinds of fix.** *Reading* errors (bad OCR) need a better OCR engine or a better photo. *Understanding* errors (e.g. picking the BIR "Date of ATP" printer-permit date instead of the transaction date) are fixed in the prompt.
- **PH receipts have a built-in checksum.** VAT is included in the total, so `vatable sales + VAT = total` and `VAT = total × 12/112`. Checking that flagged every bad Tesseract result and none of the vision results — it'll be used in the app to send suspicious receipts to review.
- **Prompts are suggestions; code is a rule.** Asking for "centavos exactly as printed" fixed dropped decimals, but the model still ignored a rule about marketplace names. Exact rules belong in code after extraction.

**Known issues (Docker):** reading receipts on CPU is slow — `qwen3-vl:2b-instruct` took 99s per receipt in Docker on a 24-thread CPU (and missed the date); the 8B model is slower and needs ~8 GB of RAM for Docker. Docker Desktop can't use AMD GPUs on Windows, so use Ollama on the host there. The NVIDIA and AMD override files are untested (no hardware to try them on).

**Known issues:** adding VAT-exempt sales and discount to the prompt cost some accuracy: the WCSI receipt's merchant and date are misread ("WAL TER-APM", 2026-11-01). The later tax-table and "Less VAT" notes fixed the FLYQUIP receipt's false ₱680 discount, but now its whole total comes back as VAT-exempt sales, and WCSI's total as VATable sales with no VAT (the VAT check catches that one). Core accuracy is 86%, one miss above the 85% target, so the next prompt change that costs a field will fail CI. None of the labeled receipts has a real discount yet. The Lazada receipt comes back as "LAZADA Edifier" instead of "LAZADA". Seven receipts is a small sample — the dataset is growing toward 30–50, including handwritten sari-sari receipts.

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
`eval/preds/` holds OCR text, so it's never committed; each run also saves the extracted fields and timing (no OCR text) to `eval/model_outputs/`, which **is** committed so CI can score the eval without a GPU. **After changing the prompt, re-run the eval and commit `eval/model_outputs/`**, or CI fails.

```bash
python eval/run_eval.py --offline --only vision --check   # score the committed outputs, like CI
python -m unittest discover -s eval -p "test_*.py"        # scoring rules
```

## Demo site (no install)

Live at **https://receipt-tracker-demo.pages.dev**. `npm run build:demo` builds the web app as a static site that needs no server and no AI model, for Cloudflare Pages:

- **Made-up data only.** 40 confirmed receipts over the past year fill the Spending page, dated relative to today so it never looks stale. Everything a visitor does stays in their browser (`localStorage`); **Reset demo** starts over.
- **Real model answers on synthetic receipts.** Real receipt photos are personal data, so they never go on the demo. `eval/make_demo_receipts.py` draws five made-up PH receipts (made-up stores, TIN `000-000-000-000`: a carinderia, a supermarket with a VAT tax table, a pharmacy with a senior citizen discount, a tilted and faded gas-station slip, a non-VAT sari-sari store) and has the real app read them with `qwen3-vl:8b-instruct`. On **Add**, picking one replays the model's actual answer after the time it really took, mistakes included: it reads the pharmacy's `08/09/2026` as September 8 (day and month swapped) and files the sari-sari store under Food.
- Uploading your own photo explains that reading receipts needs the model on your own computer, **unless the live demo is on** (below). **Re-extract** always does.

How it works: `src/api.ts` exports `demoApi` (`src/demo/`) instead of the HTTP client when built with `--mode demo`. It mirrors the API's rules (validation, checks, bulk confirm, corrections) and ports the summary periods and CSV export to TypeScript; the normal build contains none of it.

```bash
cd web
npm run build:demo                     # -> web/dist
npx vite preview --mode demo           # try it at http://localhost:4173

# after changing the prompt or the synthetic receipts (needs the app running with Ollama):
python eval/make_demo_receipts.py render
python eval/make_demo_receipts.py read --api http://localhost:5080
```

### Read on the author's PC (live demo, on demand)

While I run `scripts/live-demo.ps1`, visitors' **own** photos are read by the real model on my PC and the answer comes back to their browser; everything else still happens in the browser. It's off by default, and the site falls back to the samples when it is.

- **Nothing is kept.** The API starts in a separate, minimal mode (`--LiveDemo:Enabled=true`, `src/ReceiptExtractor.Api/LiveDemo/`) that registers no database and no storage: two endpoints (`GET /api/live`, `POST /api/extract`), the photo read in memory and dropped. Logs have size, time and outcome only. My real app (port 5080) and Ollama are never exposed.
- **Limits:** one photo at a time on the GPU (3 more may wait), 20 per visitor an hour, 100 per run, 2 minutes per photo, and it stops itself after 3 hours. Only `receipt-tracker-demo.pages.dev` may read the answers (CORS).
- **In the browser:** `web/src/demo/liveReader.ts` checks whether the PC is on (`VITE_LIVE_URL`), sends photos one at a time, and turns errors into plain messages. Samples always replay their recorded answers.

One-time setup:
1. Install [Tailscale](https://tailscale.com/download) and sign in.
2. In the Tailscale admin console, rename this PC (e.g. `receipt-demo`) so its real name isn't in the public address.
3. Run the script once: the first `tailscale funnel` prints a link to allow Funnel (and HTTPS) on the tailnet.
4. Cloudflare Pages → the project → Settings → Variables: `VITE_LIVE_URL` = the address the script prints (`https://receipt-demo.<tailnet>.ts.net`), then redeploy.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\live-demo.ps1   # needs Ollama with the model; Ctrl+C to stop
```

Trying it without the tunnel: run the API with `--urls http://localhost:5090 --LiveDemo:Enabled=true --LiveDemo:AllowedOrigin=http://localhost:4173`, then `VITE_LIVE_URL=http://localhost:5090 npm run build:demo` and `npx vite preview --mode demo`.

**Deploying to Cloudflare Pages:** Workers & Pages → Create → Pages → Connect to Git → this repo, then: root directory `web`, build command `npm run build:demo`, build output `dist`, environment variable `NODE_VERSION` = `24`. Every push to `main` redeploys. Page routes such as `/receipts/…` work without extra setup (Pages serves `index.html` for them).

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on `main`, with four jobs:

| Job | What it checks |
|---|---|
| API tests | `dotnet test` (parsing, VAT math, periods, CSV, seed data, and that the app's prompt matches the eval's) |
| Web app | `npm run lint`, `npm test`, `npm run build`, `npm run build:demo` |
| Extraction eval | Scores the committed `eval/model_outputs/` (no GPU, no Ollama). **Fails** if core accuracy drops under 85%, or if the prompt, model or dataset changed without re-running the eval. The results table is on the run's summary page |
| Docker | `docker build` |

The VAT check lives in three places (API, web app, eval), so all three are tested against the same cases in `tests/vat-cases.json`.

## Run it as a desktop app (Windows)

Build once:

```bat
publish.cmd
```

That creates **`publish\ReceiptTracker.exe`**, a single file containing the API *and* the web app (no .NET or Node needed to run it). Start Ollama, double-click the exe, and your browser opens at http://localhost:5080. Close the console window to stop it.

- Your database and receipt images are saved in a `data` folder next to the exe, so the whole folder is portable.
- To change settings (model, Ollama address, port) without rebuilding, put a `settings.json` next to the exe, e.g. `{ "Ollama": { "Model": "qwen3-vl:4b-instruct" } }`.
- To use it from your phone on the same Wi-Fi: `ReceiptTracker.exe --urls http://0.0.0.0:5080`, then open `http://<your-PC's-IP>:5080`. This makes it reachable by anyone on your network, since there are no logins.
- Windows may show a SmartScreen warning the first time because the exe isn't code-signed: **More info → Run anyway**.

## Run with Docker

Requirements: [Docker](https://www.docker.com/products/docker-desktop/) (Docker Desktop on Windows/macOS). Nothing else — no .NET, Node or Ollama.

```bash
docker compose up --build
```

Open http://localhost:5080. This runs the app (API + web app + SQLite database in the `app-data` volume) without the AI model, so it's a ~1 GB download: every screen works, but uploaded receipts fail with "Can't reach Ollama" until you add a model (below).

**Demo data:** on first start the Docker app fills its empty database with receipts to try things on:
- **To review:** the 7 hand-labeled receipts from the eval, with their photos and the correct values, ready to open and confirm.
- **Confirmed:** 40 made-up receipts over the past 12 months across all categories (Jollibee, Petron, Meralco, Mercury Drug with a senior citizen discount, a sari-sari store without VAT…), so the Spending page, the chart and **Export CSV** have data. They show a "Sample receipt" picture instead of a photo.

Delete them from the Receipts page whenever you like; a database that has receipts is never seeded again. The exe and `dotnet run` don't seed (`Seed:Enabled` is off).

Adding a model, one of two ways:

| | Command | Download | Speed per receipt |
|---|---|---|---|
| **Ollama already on your PC** (uses your GPU) | `OLLAMA_URL=http://host.docker.internal:11434 docker compose up --build` | nothing extra | ~10–20s on my RX 9070 XT |
| **Ollama in Docker** | `docker compose --profile ai up --build` | + Ollama image and the 6 GB model | minutes on CPU (see below) |

(PowerShell: `$env:OLLAMA_URL="http://host.docker.internal:11434"; docker compose up --build`.)

- With `--profile ai`, an `ollama-pull` container downloads the model once and exits; follow it with `docker compose logs -f ollama-pull`. Receipts uploaded before it finishes can be retried with **Try again**.
- GPU for the Ollama container: add `-f docker-compose.yml -f docker-compose.nvidia.yml` (NVIDIA) or `-f docker-compose.amd.yml` (AMD, Linux only). Docker Desktop on Windows can't pass an AMD GPU through, so on an AMD Windows PC use Ollama on the host instead.
- A different model: `MODEL=qwen3-vl:4b-instruct docker compose --profile ai up`. Smaller models are faster but less accurate (only the 8B was scored in the bake-off).
- Stop with `docker compose down`. Your receipts stay in the `app-data` volume; `docker compose down -v` deletes them (and the downloaded model).

## Downloading the model (without Docker)

`scripts\pull-model.ps1` (Windows) or `scripts/pull-model.sh` (Linux/macOS) downloads the model the app is set to use into your local Ollama, and does nothing if it's already there:

```bash
powershell -ExecutionPolicy Bypass -File scripts\pull-model.ps1        # model from appsettings.json
powershell -ExecutionPolicy Bypass -File scripts\pull-model.ps1 -Model qwen3-vl:4b-instruct
```

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
| `GET /api/receipts/export.csv?from=&to=` | Confirmed receipts as a CSV file, oldest first. Both dates optional; `to` is the day *after* the last one (like the summary's `end`) |
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

## Running the web app

Requirements: Node.js 20+, and the API running (above).

```bash
cd web
npm install
npm run dev          # http://localhost:5173 — /api calls are proxied to the API
npm test             # unit tests (formatting, VAT warnings)
```

- **Spending** — totals for any day, ISO week, month, calendar quarter or year, broken down by category (confirmed receipts only). **Export CSV** downloads the period's receipts for a spreadsheet: date (`20251220`), merchant, category, total, VAT, VATable / VAT-exempt sales, discount. Amounts are plain numbers (`1124.46`) so the spreadsheet can add them up
- **Receipts** — everything waiting for review, with status updating live while the model reads. Tick receipts (or **Select all ready to confirm**) and confirm them in one go; anything that failed a check is called out before you confirm
- **Review** — the photo next to the extracted fields; VAT math is re-checked as you type, and future dates are flagged (the model sometimes swaps day and month). The photo can be rotated, zoomed (buttons, scroll wheel, pinch, double-click) and dragged around; **Save rotation** stores the upright image without re-running extraction, and zoom is never saved. Amounts show as `10,495.00`; the **←.** / **.→** buttons next to each amount move the decimal point when the model misplaced it (`112446.00` → `1,124.46`), and if a receipt's VAT lines are missing, **Work out VAT from the total** splits it (12% VAT is included in PH totals)
- **Add** — take a photo (opens the back camera on a phone) or drop/pick files

**On your phone:** with both running, open `http://<your-PC's-IP>:5173` on the same Wi-Fi. "Take photo" opens the camera. Installing it as an app needs HTTPS, which comes with the hosted demo link (step 9).

## Project structure

```
ReceiptExtractor.sln
publish.cmd      builds publish\ReceiptTracker.exe (API + web app in one file)
Dockerfile       the same app as a Linux image (web app built in)
docker-compose.yml         app (+ Ollama and a model download with --profile ai)
docker-compose.nvidia.yml  / docker-compose.amd.yml: GPU for the Ollama container
scripts/         pull-model.ps1 / pull-model.sh: download the vision model into Ollama
                 live-demo.ps1: let demo-site visitors' photos be read on this PC for a few hours
src/ReceiptExtractor.Api/
  Endpoints/     HTTP endpoints (upload, status, confirm, retry, delete)
  Extraction/    Ollama client, background queue + worker
  Validation/    VAT math, date checks, merchant clean-up
  Data/          EF Core entity + DbContext (SQLite)
  Summaries/     period math (ISO weeks, calendar quarters) + GET /api/summary
  Hosting/       desktop mode: data folder next to the exe, opens the browser, checks Ollama
  Seeding/       demo data for an empty database (Docker): labeled receipts + made-up ones
  Export/        CSV export (Excel-friendly: UTF-8 BOM, formula-safe text)
  LiveDemo/      live demo mode: extract-only, no database or storage, limits (scripts/live-demo.ps1)
tests/ReceiptExtractor.Tests/   xUnit tests
tests/vat-cases.json            VAT check cases shared by the C#, TypeScript and Python tests
.github/workflows/ci.yml        CI: tests, eval from cached model outputs, Docker build
web/
  src/pages/     Dashboard, Receipts, Review, Upload
  src/api.ts     typed API client
  src/format.ts  peso/date formatting, VAT warnings (mirrors ReceiptValidator.cs)
  src/demo/      demo site (npm run build:demo): in-browser stand-in for the API, made-up data
  public/demo/   synthetic receipt images for the demo site
datasets/
  labels/        receipt images
  receipts/      hand-labeled ground truth (one JSON per image)
eval/
  try_vision.py            one receipt through the vision model
  compare_approaches.py    vision vs Tesseract vs PaddleOCR on one receipt
  run_eval.py              score every approach on every receipt (--offline: from model_outputs/, for CI)
  test_scoring.py          tests for the scoring rules
  model_outputs/           extracted fields per receipt (committed; no OCR text)
  make_demo_receipts.py    synthetic receipts for the demo site, read by the real app
  merchant_aliases.json    store name ↔ company name equivalents
  RESULTS.md               latest scores
SCOPE.md         what the MVP does and doesn't do
ROADMAP.md       steps to finish
```

## Privacy note

`eval/preds/` holds the full OCR text of each receipt (including TINs), so it's in `.gitignore`. `eval/model_outputs/` only has the extracted fields (merchant, date, amounts), the same values as the committed labels and `RESULTS.md`. Receipt images must have TINs and card digits redacted before they're committed.
