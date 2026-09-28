---
applyTo: "web/**"
---

# Web app (React + TypeScript + Vite PWA)

React 19, react-router 7, TypeScript strict, vite-plugin-pwa, Vitest, oxlint. No other runtime dependencies (UI kits, state libraries, axios, date libraries) without asking.

## Style
- Function components and hooks, named exports (`export function UploadPage()`); one page per file in `src/pages/`, shared bits in `components.tsx`.
- No semicolons, single quotes, 2-space indent, trailing commas. `import { type X }` for types (`verbatimModuleSyntax`).
- camelCase functions/variables, PascalCase components/types, `UPPER_SNAKE` for module constants (`POLL_MS`, `ACCEPTED`).
- Short JSDoc on exported helpers showing input → output: `/** 10495 -> "₱10,495.00"; null -> "—" */`.

## Data
- All HTTP goes through `src/api.ts`, with relative `/api/...` URLs. Its types mirror the C# DTOs — change both together.
- The demo build (`npm run build:demo`) swaps in `src/demo/demoApi.ts`, which mirrors the endpoints in the browser. New or changed endpoints need a demoApi version too. Check demo-only UI with `IS_DEMO`; the normal build must not contain demo code (keep demo module top-level code pure).
- The demo site shows only synthetic receipts (`public/demo/receipts`, made by `eval/make_demo_receipts.py`), never real receipt photos.
- Poll busy receipts (`Queued`/`Processing`) like `useReceipt.ts`: a `cancelled` flag and cleared timers in the effect cleanup.
- Keep state that should survive navigation in the URL (e.g. the list tab), not in global state.

## Money, dates, VAT
- Display with `formatPeso` / `formatDate` from `format.ts`; parse input with `parseMoney` (`null` = empty, `NaN` = invalid).
- Parse `YYYY-MM-DD` as a calendar date (never `new Date('2025-12-20')`, which shifts by timezone). "Today" is `todayIso()`.
- `vatWarnings` must match `ReceiptValidator.cs`. Warnings inform; the user has the final say on confirm.

## UX
- Mobile first: used from a phone on the same Wi-Fi, with camera capture. Tap targets and layout must work at phone width.
- Show errors with the API's own message (it's written for users). Never lose what the user typed on a failed save.
- The service worker must never cache `/api/` responses or receipt images.
- No external requests: no CDN assets, web fonts or analytics.

## Tests
Pure logic lives in `.ts` modules with a `*.test.ts` file next to it (`format.test.ts`, `imageTools.test.ts`). Use `describe`/`it`/`it.each` with realistic peso amounts. Components aren't unit-tested.

```bash
cd web
npm run lint && npm test && npm run build && npm run build:demo
```
