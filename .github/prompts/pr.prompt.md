---
description: 'Write the PR title and description for the current branch'
agent: 'agent'
---

Write a pull request title and description for my current changes.

1. Gather the changes: `git branch --show-current`, `git log --oneline main..HEAD`, `git diff main...HEAD --stat`, then `git diff main...HEAD` and `git status` for uncommitted work. Read only the files the diff touches.
2. Pick the type from the branch prefix: `feature/` → Feature, `fix/` → Fix. Otherwise infer it from the diff.
3. Take the issue number from the branch name if it has one (`feature/12-bulk-delete` → 12).
4. Title: `[Feature-12] Bulk delete receipts`, or `[Feature] Bulk delete receipts` when there's no issue number. Imperative, under 72 characters, says what the user gets.
5. Body: fill in `.github/pull_request_template.md`.
   - Describe changes by file path, grouped by area (API, web, eval, docs).
   - Keep "Root cause" only for fixes; drop any section that doesn't apply rather than writing "N/A".
   - Tick a checklist item only if the diff shows it's true; leave it unticked and add a short note otherwise.
   - For prompt or extraction changes, include the eval numbers from `eval/RESULTS.md` (before → after).
   - `Closes #<n>` only when there's an issue number.
6. The repository is public: never include receipt contents, amounts from real receipts, TINs, card digits, or anything from `eval/preds/`.

Output: the whole result inside **one** fenced code block tagged `markdown` — the title as the first line (`# <title>`), then the body. Under 4000 characters. Nothing before or after the code block.
