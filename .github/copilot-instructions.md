# Copilot Instructions — Receipt Tracker

Local-first tracker for Philippine receipts: **upload → extract (local vision model) → review/correct → confirm → summaries**.
Single user, runs on the owner's Windows PC. No accounts, no cloud. Scope and non-goals: `SCOPE.md`. Plan: `ROADMAP.md`.

These rules apply to every suggestion. Files in `.github/instructions/` add rules per area; if one conflicts with this file, the specific file wins unless it weakens privacy or correctness.

## Priorities (in order)
1. **Correct** — amounts, dates and VAT math must be right; never guess silently, flag for review instead.
2. **Private** — receipt data never leaves the machine.
3. **Simple** — smallest change that works, in the style of the surrounding code.
4. **Fast enough** — upload → extracted fields in under 30 s.
5. **Pleasant** — clear, plain-English messages; failures are recoverable (retry, re-extract, edit).

## Hard rules
- **No outbound calls** except Ollama at the configured `Ollama:BaseUrl`. No cloud APIs, telemetry/analytics SDKs, CDN scripts or web fonts.
- **Never log, print or commit receipt contents**: field values, model output, OCR text, user hints, TINs, card digits. Log IDs, statuses, counts and durations instead.
- **Never commit `eval/preds/`** (OCR text). `eval/model_outputs/` (extracted fields only) is committed for CI. Receipt images need TINs and card digits redacted before they're committed.
- **Don't build non-goals** unless asked: line items, multi-user/auth, bank/GCash/Maya sync, tax filing, native app, multi-currency.
- **Money is `decimal` (C#) / `number` rounded to centavos (TS), PHP only.** VAT is 12% and included in the total.
- **Exact rules belong in code, not only in the prompt** — the model ignores prompt rules sometimes (see `MerchantRules`).

## Where things live
| Area | Path |
|---|---|
| API (ASP.NET Core .NET 10 minimal APIs) | `src/ReceiptExtractor.Api/` — `Endpoints/`, `Extraction/`, `Validation/`, `Data/`, `Summaries/`, `Hosting/` |
| Tests (xUnit) | `tests/ReceiptExtractor.Tests/` |
| Web app (React + TS + Vite PWA) | `web/src/` — `api.ts` client, `format.ts` formatting + VAT warnings, `pages/` |
| Extraction eval (Python) | `eval/`, ground truth in `datasets/receipts/*.json`, images in `datasets/labels/` |
| Desktop exe | `publish.cmd` → `publish\ReceiptTracker.exe`; `Hosting/DesktopMode.cs` (data folder next to exe, port 5080) |

Dev ports: API `http://localhost:5036`, Vite `http://localhost:5173` (proxies `/api` to the API).

## Keep these in sync (most bugs come from here)
| When you change… | Also change… |
|---|---|
| Extraction prompt `OllamaExtractor.Prompt` | `PROMPT` in `eval/try_vision.py` (C# adds the category line); re-run the eval |
| VAT / review checks in `Validation/ReceiptValidator.cs` | `vatWarnings` in `web/src/format.ts`, plus tests on both sides |
| API DTOs in `Endpoints/`, `Summaries/` | Types in `web/src/api.ts` (enums serialize as strings, JSON is camelCase) |
| Categories (`Categories` in `ReceiptFields.cs`) | `CATEGORIES` in `api.ts`, JSON schema enum + prompt in `OllamaExtractor`, `try_vision.py` |
| Lockable fields (`UserHints`, snake_case model names) | `LockableField` in `api.ts`, `LOCKABLE` in `ReviewPage.tsx` |
| A column on `ReceiptRecord` | `Data/SchemaPatches.cs` (DB uses `EnsureCreated()`, no migrations) |
| Anything user-visible, a roadmap step, `eval/RESULTS.md` | `README.md` (status line, "Last updated", tables, Known issues) and `ROADMAP.md` checkboxes |

## How to work
- Match the surrounding code's patterns, naming and comment style; don't introduce new layers or abstractions.
- No new NuGet/npm/pip dependencies without asking.
- Preserve existing behavior unless asked to change it. No TODOs or placeholders unless asked.
- Behavior change → add or update tests. Bug fix → add a test that reproduces it, using the real receipt values when there are any.
- Comments explain *why* (often with a real receipt example or a `SCOPE.md` reference), not *what*.
- Be economical: edit locally rather than rewriting files, don't dump whole files, don't read `datasets/`, `bin/`, `obj/`, `web/node_modules/`, `web/dist/` or `wwwroot/`. For multi-file changes, give a short plan first. State assumptions when the request is ambiguous.

## Verify before calling it done
```bash
dotnet build
dotnet test
cd web && npm run lint && npm test && npm run build
```
Prompt or extraction changes: `python eval/run_eval.py --only vision` (needs Ollama + GPU, several minutes) and report the new numbers, including regressions.

Environment: Windows, PowerShell, Visual Studio, Python venv in `.venv`, AMD Radeon GPU (PaddleOCR can't use it).

## Area-specific instructions
| Files | Instructions |
|---|---|
| `**/*.cs` | `instructions/backend.instructions.md`, `instructions/logging.instructions.md` |
| `web/**` | `instructions/web.instructions.md` |
| `eval/**`, `datasets/**` | `instructions/eval.instructions.md` |
