# Notes for Claude

## Keep README.md current
Update README.md in the same change whenever any of these happen:
- A roadmap step is completed (update the status line and "Last updated" date)
- `eval/RESULTS.md` changes (copy the new numbers into the bake-off table)
- A new script, folder, or setup step is added (update "Running" / "Project structure")
- A notable finding or known issue appears (add it to "What I learned" / "Known issues")

Keep ROADMAP.md checkboxes in sync too.

## Project facts
- Scope: SCOPE.md. Roadmap: ROADMAP.md.
- Extraction: vision model `qwen3-vl:8b-instruct` via Ollama (won the step 3 bake-off).
- Merchant: store name or company name both count; equivalents in `eval/merchant_aliases.json`.
- Backend: ASP.NET Core (.NET 10) minimal APIs in `src/ReceiptExtractor.Api`, EF Core + SQLite, xUnit tests in `tests/`.
  The extraction prompt lives in both `OllamaExtractor.Prompt` and `eval/try_vision.py` — keep them in sync.
  (User hints for "re-extract with my changes" are appended at runtime by `Extraction/UserHints.cs`; API and the live
  demo's `hints` form field only. `ReextractRequest.Check` holds the rules for both.)
- Frontend: React + TypeScript + Vite PWA in `web/`; dev server proxies `/api` to the API on :5036.
  VAT checks exist in both `ReceiptValidator.cs` and `web/src/format.ts` — keep them in sync.
  Demo site (`npm run build:demo`, Cloudflare Pages): `web/src/demo/demoApi.ts` stands in for the API in the browser;
  when an endpoint's rules change, change demoApi too. No real receipt photos on the demo, ever: only the synthetic ones
  from `eval/make_demo_receipts.py` (re-run its `read` step after a prompt change).
  Live demo (`scripts/live-demo.ps1`, spec in `docs/superpowers/specs/2026-09-28-live-demo-design.md`): `--LiveDemo:Enabled=true`
  starts `LiveDemo/LiveDemoApp.cs` instead of the app. It must never register the database, storage or static files, and
  never log fields or images. Uploads stay in memory only because of `WithFormOptions` on `/api/extract` (ASP.NET
  spools anything over 64 KB to %TEMP% otherwise). The web side is `web/src/demo/liveReader.ts` (`VITE_LIVE_URL`); visitor-facing messages
  live in `LiveDemoEndpoints` and `liveReader.ts`/`demoApi.ts`.
- Packaged exe: `publish.cmd` builds web/dist into the API's wwwroot, then a self-contained single-file
  win-x64 publish. Non-Development runs use a `data` folder next to the exe and port 5080 (`Hosting/DesktopMode.cs`).
- Docker: `Dockerfile` (same app, Linux) + `docker-compose.yml` (app only; `--profile ai` adds Ollama + `ollama-pull`).
  The default model is in `appsettings.json`, `docker-compose.yml` (twice) and `scripts/pull-model.sh` — keep them in sync.
  Docker seeds an empty DB (`Seeding/`, `Seed:Enabled`): labeled receipts from `datasets/` + made-up ones. Label JSON
  categories must be one of the app's categories.
  Docker can't use the AMD GPU on Windows: test with `OLLAMA_URL=http://host.docker.internal:11434 docker compose up`.
- DB is created with `EnsureCreated()` (no migrations yet): when adding a column to the entity, also add it to
  `Data/SchemaPatches.cs` so existing `receipts.db` files get it (other schema changes still need the DB deleted).
- Windows dev machine, Visual Studio, Python venv in `.venv`, AMD Radeon GPU (PaddleOCR can't use it).
- New dataset photos: run `python eval/photo_metadata.py strip <photo>` first (phone photos carry GPS;
  `eval/test_photo_metadata.py` fails otherwise), then re-run the eval.
- Never commit `eval/preds/` (contains receipt OCR text incl. TINs). `eval/model_outputs/` (fields only) IS committed:
  after changing the prompt, re-run `python eval/run_eval.py --only vision` and commit it, or CI fails.
- CI (`.github/workflows/ci.yml`) fails if eval core accuracy < 85%. VAT check cases shared by C#/TS/Python: `tests/vat-cases.json`;
  `SyncTests.Prompt_matches_the_eval_prompt` enforces the prompt sync.
