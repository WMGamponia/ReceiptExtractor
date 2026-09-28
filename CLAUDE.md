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
  (User hints for "re-extract with my changes" are appended at runtime by `Extraction/UserHints.cs`; API only.)
- Frontend: React + TypeScript + Vite PWA in `web/`; dev server proxies `/api` to the API on :5036.
  VAT checks exist in both `ReceiptValidator.cs` and `web/src/format.ts` — keep them in sync.
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
- Never commit `eval/preds/` (contains receipt OCR text incl. TINs).
