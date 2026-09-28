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
- DB is created with `EnsureCreated()` (no migrations yet): when adding a column to the entity, also add it to
  `Data/SchemaPatches.cs` so existing `receipts.db` files get it (other schema changes still need the DB deleted).
- Windows dev machine, Visual Studio, Python venv in `.venv`, AMD Radeon GPU (PaddleOCR can't use it).
- Never commit `eval/preds/` (contains receipt OCR text incl. TINs).
