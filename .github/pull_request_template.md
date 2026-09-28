## What
<!-- What changes for the user, in a sentence or two. Screenshots for UI changes. -->

## Why
<!-- The problem or roadmap step. -->
Closes #

## Changes
<!-- By file path, grouped by area (API, web, eval, docs). -->
- `path/to/file` — what changed

## Root cause
<!-- Fixes only: what caused it. Delete this section for features. -->

## Testing
- [ ] `dotnet test` passes (tests added/updated for changed behavior)
- [ ] `npm run lint && npm test && npm run build` passes in `web/`
- [ ] Tried it in the app: <!-- what you did -->
- Eval (prompt/extraction changes only): core accuracy <!-- before --> → <!-- after -->

## Checklist
- [ ] No receipt data, TINs, card digits or `eval/preds/` in the diff
- [ ] Kept in sync: prompt ↔ `eval/try_vision.py`, `ReceiptValidator.cs` ↔ `format.ts`, DTOs ↔ `api.ts`, new columns ↔ `SchemaPatches.cs`
- [ ] README and ROADMAP updated
- [ ] No new build warnings or lint errors
- [ ] No new dependencies (or explained above)
