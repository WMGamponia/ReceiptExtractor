# Development

Everything for running and changing the code. What the app does and why: the [README](../README.md).

## How a receipt flows

upload → saved to disk + DB row `Queued` → background worker sends the image to Ollama with a JSON schema → model output parsed and cleaned (e.g. "LAZADA Edifier" → "LAZADA") → checks run (VAT breakdown: VATable + VAT + VAT-exempt = total, allowing for a senior citizen / PWD discount and a restaurant service charge, each either after the breakdown or inside it; VAT = 12% of VATable sales; date; total) → confidence score (how sure the model was of each date and amount, from its own token probabilities; below 0.95 the receipt gets a flag) → `Extracted`, or `NeedsReview` with reasons → user confirms/corrects → `Confirmed`. If a correction should change other fields too (e.g. the right VAT line), **Re-extract** keeps the corrected fields, tells the model they're correct, and re-reads the rest. The model's raw output is kept, so user corrections can later become new eval data.

## Run it as a desktop app (Windows)

Build once:

```bat
publish.cmd
```

That creates **`publish\ReceiptTracker.exe`**, a single file containing the API *and* the web app (no .NET or Node needed to run it). Start Ollama, double-click the exe, and your browser opens at http://localhost:5080. Close the console window to stop it.

- Your database and receipt images are saved in a `data` folder next to the exe, so the whole folder is portable. It starts empty: the sample receipts are Docker only.
- The exe is unsigned, so SmartScreen warns the first time (**More info → Run anyway**).
- To change settings (model, Ollama address, port) without rebuilding, put a `settings.json` next to the exe, e.g. `{ "Ollama": { "Model": "qwen3-vl:4b-instruct" } }`.
- To use it from your phone on the same Wi-Fi: `ReceiptTracker.exe --urls http://0.0.0.0:5080`, then open `http://<your-PC's-IP>:5080`. This makes it reachable by anyone on your network, since there are no logins.

## Run with Docker

The basics are in [Try it](../README.md#try-it). More options:

- **What's in it:** the API + web app in one image, with the SQLite database and images in the `app-data` volume. On first start, an empty database is filled with the sample receipts; delete them from the Receipts page whenever you like, and a database that has receipts is never seeded again (`Seed:Enabled`).
- **GPU for the Ollama container:** add `-f docker-compose.yml -f docker-compose.nvidia.yml` (NVIDIA) or `-f docker-compose.amd.yml` (AMD, Linux only). Neither file has been tested.
- **A different model:** `MODEL=qwen3-vl:4b-instruct docker compose --profile ai up`. Smaller models are faster but less accurate (only the 8B was scored in the bake-off), and still slow on a CPU: the 2B took 99s per receipt on a 24-thread CPU and missed the date.
- `docker compose down -v` also deletes the downloaded model.

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

## Running the web app

Requirements: Node.js 20+, and the API running (above).

```bash
cd web
npm install
npm run dev          # http://localhost:5173 — /api calls are proxied to the API
npm test             # unit tests (formatting, VAT warnings)
```

**On your phone:** with both running, open `http://<your-PC's-IP>:5173` on the same Wi-Fi. "Take photo" opens the camera.

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

# pick Ollama:MinConfidence from the committed vision outputs (after a model or prompt change)
python eval/calibrate_confidence.py
```

Predictions are cached in `eval/preds/` and re-run automatically when the model or prompt changes (`--fresh` forces a re-run).
`eval/preds/` holds OCR text, so it's never committed; each run also saves the extracted fields and timing (no OCR text) to `eval/model_outputs/`, which **is** committed so CI can score the eval without a GPU. **After changing the prompt, re-run the eval and commit `eval/model_outputs/`**, or CI fails.

```bash
python eval/run_eval.py --offline --only vision --check   # score the committed outputs, like CI
python -m unittest discover -s eval -p "test_*.py"        # scoring rules
```

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on `main`, with four jobs:

| Job | What it checks |
|---|---|
| API tests | `dotnet test` (parsing, VAT math, periods, CSV, seed data, and that the app's prompt matches the eval's) |
| Web app | `npm run lint`, `npm test`, `npm run build`, `npm run build:demo` |
| Extraction eval | Scores the committed `eval/model_outputs/` (no GPU, no Ollama). **Fails** if core accuracy drops under 85%, or if the prompt, model or dataset changed without re-running the eval. The results table is on the run's summary page |
| Docker | `docker build` |

The VAT check lives in three places (API, web app, eval), so all three are tested against the same cases in `tests/vat-cases.json`.

## Demo site and live demo

How the demo site is built and deployed to Cloudflare Pages, how its sample receipts are made, and how the on-demand live demo works (Tailscale Funnel, limits, setup): [docs/live-demo.md](live-demo.md).

## Project structure

```
ReceiptExtractor.sln
publish.cmd      builds publish\ReceiptTracker.exe (API + web app in one file)
Dockerfile       the same app as a Linux image (web app built in)
docker-compose.yml         app (+ Ollama and a model download with --profile ai)
docker-compose.nvidia.yml  / docker-compose.amd.yml: GPU for the Ollama container
scripts/         pull-model.ps1 / pull-model.sh: download the vision model into Ollama
                 live-demo.ps1: let demo-site visitors' photos be read on this PC for a few hours
                 LiveDemoLauncher/: LiveDemo.exe, a double-click launcher for live-demo.ps1 (not in the solution)
src/ReceiptExtractor.Api/
  Endpoints/     HTTP endpoints (upload, status, confirm, retry, delete)
  Extraction/    Ollama client (turns sideways photos upright first), background queue + worker, confidence score + gate
  Validation/    VAT math, date checks, merchant clean-up
  Data/          EF Core entity + DbContext (SQLite)
  Summaries/     period math (ISO weeks, calendar quarters) + GET /api/summary
  Hosting/       desktop mode: data folder next to the exe, opens the browser, checks Ollama
  Seeding/       demo data for an empty database (Docker): labeled receipts + made-up ones
  Export/        CSV export (Excel-friendly: UTF-8 BOM, formula-safe text)
  LiveDemo/      live demo mode: extract-only, no database or storage, limits (scripts/live-demo.ps1)
tests/ReceiptExtractor.Tests/   xUnit tests
tests/vat-cases.json            VAT check cases shared by the C#, TypeScript and Python tests
tests/confidence-cases.json     confidence score and gate cases shared by the C#, TypeScript and Python tests
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
docs/            demo GIF, live-demo.md (demo site + live demo), design specs
  install/       step-by-step install guides: Windows, Linux, NVIDIA GPU, AMD GPU
eval/
  try_vision.py            one receipt through the vision model
  compare_approaches.py    vision vs Tesseract vs PaddleOCR on one receipt
  run_eval.py              score every approach on every receipt (--offline: from model_outputs/, for CI)
  confidence.py            confidence score from token probabilities (same rules as ConfidenceScorer.cs)
  calibrate_confidence.py  how well each MinConfidence threshold separates right reads from misreads
  test_scoring.py          tests for the scoring rules
  test_upright.py          tests for turning sideways photos upright (needs Pillow)
  photo_metadata.py        strips GPS and other metadata from dataset photos (keeps the orientation)
  test_photo_metadata.py   fails if a dataset photo has a GPS location
  model_outputs/           extracted fields per receipt (committed; no OCR text)
  make_demo_receipts.py    synthetic receipts for the demo site, read by the real app
  merchant_aliases.json    store name ↔ company name equivalents
  RESULTS.md               latest scores
SCOPE.md         what the MVP does and doesn't do
ROADMAP.md       steps to finish
```

## Privacy note

`eval/preds/` holds the full OCR text of each receipt (including TINs), so it's in `.gitignore`. `eval/model_outputs/` only has the extracted fields (merchant, date, amounts), the same values as the committed labels and `RESULTS.md`. Receipt images must have TINs and card digits redacted before they're committed.

## More things I learned

The rest of the notes from building and tuning it (the main ones are in the [README](../README.md#what-i-learned)).

- **One misread digit ruins a field.** `15,190` became `73,190`; `Dec 21 2025` became `Dec 24 7025`.
- **Two kinds of error need two kinds of fix.** *Reading* errors (bad OCR) need a better OCR engine or a better photo. *Understanding* errors (e.g. picking the BIR "Date of ATP" printer-permit date instead of the transaction date) are fixed in the prompt.
- **Restaurants add a service charge after the VAT breakdown.** Two of the new receipts (Texas Roadhouse, a barbecue house) only add up once the 10% service charge is counted, so it's now its own field and the VAT check allows it on top of the breakdown or inside it, like a discount. Guessing "a 5–10% gap is probably a service charge" would also have hidden real misreads.
- **Handwritten receipts have more than one date.** The printed box at the bottom (printer's accreditation, ATP, "Date Issued") has dates too, and the model often took one of those. One prompt line fixed some of them, but the first wording also made the model read the gas-station demo receipt's CASH line as the total. A prompt change can break a field it doesn't mention, so the demo receipts are re-read and checked after every prompt change.
- **A label in the prompt can match the wrong line.** The prompt asked for the "12% VAT" line, and the model took the pharmacy demo receipt's "Less 12% VAT" line, even though the next sentence said not to. Pointing at the breakdown instead ("the VAT Amount line, next to VATable Sales") fixed it, and also fixed a VAT-exempt misread in the eval.
- **Image size didn't matter.** After fixing the sideways photos, the eval scored the same at 1536, 2048 and 4080 px.
