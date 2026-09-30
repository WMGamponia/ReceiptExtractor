# Live demo: read visitors' photos on my PC (on demand)

Status: approved design, 2026-09-28. Implementation plan: to follow.

## Goal

Visitors to the demo site (https://receipt-tracker-demo.pages.dev) can add **their own** receipt photo and get the real
model's answer, read on my PC, while I have it switched on. Everything else about the demo stays as it is: review,
confirm, Spending and CSV live in the visitor's browser.

Audience: portfolio visitors (recruiters, reviewers), a handful of reads a day.

Success:
- With the live demo on, a visitor adds a photo and sees it go Queued → Processing → To review in about 15–30 s,
  with the same fields, checks and flags the real app would give.
- With it off (the default), the site behaves exactly as it does today.
- My PC keeps nothing: no photo, no fields, no database row, no log line with receipt contents.

## Why not the earlier "live link"

The dropped live link was a full demo copy of the app behind a tunnel: shared state between visitors, strangers' photos
stored on my disk, a whole app exposed without a login. This design avoids all three:

| Risk | This design |
|---|---|
| Visitors see each other's receipts | Receipts only exist in each visitor's browser |
| Strangers' photos stored on my PC | Photo read in memory, answered, dropped; no storage is registered |
| Public door into my app and data | A separate, stateless process with two endpoints; my real app (port 5080) and Ollama are never tunneled |
| Always exposed | Off by default; on only while I run a script, and it stops itself after 3 hours |

## Decisions

- **Availability: on demand.** I run `scripts/live-demo.ps1` when I want it; it stops itself after 3 hours.
- **Tunnel: Tailscale Funnel.** Free, fixed HTTPS address (`https://<machine>.<tailnet>.ts.net`), no domain needed.
  A fixed address lets the site have it built in; a Cloudflare quick tunnel's random address would need to be passed in
  links, which anyone could forge to point at their own server.
- **Approach: an extract-only mode of the same API project** (not a separate project, not a full demo copy).
  Same extractor, prompt and upload checks as the real app.

## Part 1: the service on my PC (`LiveDemo` mode)

Started with `LiveDemo:Enabled=true` on `http://localhost:5090`. `Program.cs` then takes a separate, minimal startup
path that registers **only**:

- `OllamaExtractor` (typed `HttpClient`, same `Ollama` settings), `TimeProvider`, the rate limiter, CORS;
- `GET /api/live` → `200 { "model": "<Ollama:Model>", "perVisitorPerHour": 20 }` (the site's "is it on?" check; the limit is null when off, so the Add page shows the real number);
- `POST /api/extract` (multipart, one `file`) → `200 { "fields": {…}, "category": "…", "extractionSeconds": 17.3 }`.

It does **not** register `AppDbContext`, storage options, static files, the fallback to `index.html`, seeding, the
extraction queue/worker, or the desktop behaviour (`DesktopMode`: opening a browser, the `data` folder). Every other
path gives 404. Not registering storage is what guarantees nothing can be written.

### Extract endpoint

1. Same checks as a normal upload (`ReceiptEndpoints`): size 1 byte – 15 MB, and the first bytes must be JPEG
   (`FF D8 FF`) or PNG (`89 50 4E 47`). Otherwise 400 with the same messages. The shared checks move to a small helper
   used by both endpoints.
2. The photo is copied into memory and passed to a new overload
   `IReceiptExtractor.ExtractAsync(byte[] image, IReadOnlyList<FieldHint> hints, CancellationToken ct)`.
   The existing path overload reads the file and calls it, so the real app's behaviour is unchanged. Hints are always
   empty here (no re-extract on the live demo). _Changed 2026-09-30: an optional `hints` form field (a
   `ReextractRequest` as JSON) makes Re-extract work on the live demo; see docs/live-demo.md._
3. Response: the extracted fields and category, plus how long the read took. Checks/flags are computed in the browser
   (`demoApi` already mirrors `ReceiptValidator`). `ExtractionException` → 502 with a generic "The model couldn't read
   this photo; try another." Its real message (which names local addresses and setup commands) is logged, not sent.
4. Logs: size, duration and outcome only. Never fields, model output or image data.
5. `Cache-Control: no-store` on every response. No cookies, no auth.

### Limits (`LiveDemo` settings section, defaults below)

| Setting | Default | When exceeded |
|---|---|---|
| One read at a time on the GPU; `MaxWaiting` | 3 waiting | 503 "The author's PC is busy; try again in a minute." |
| `PerVisitorPerHour` (fixed window, keyed by client IP) | 20 | 429 "You've reached the demo's limit of 20 receipts an hour; try again later." |
| `Budget` (total reads per run, in memory) | 100 | 429 "The live demo has read all it will for now; try the sample receipts." |
| `ReadTimeout` | 2 min | 504 "Reading took too long; try again." |
| `StopAfter` (the service shuts itself down) | 3 h | the site sees the PC as offline |

- The budget and the stop timer together mean at most 100 reads per 3-hour run. Counts live in memory, so a new run
  starts a new budget.
- A visitor leaving the page cancels their request (`RequestAborted`), freeing the slot.
- **Client IP through Funnel is unverified.** If Funnel passes the visitor's address (`X-Forwarded-For` from the
  loopback proxy), use it via forwarded headers restricted to loopback. If it doesn't, the per-visitor limit is
  disabled (it would otherwise be one shared bucket), the global limits still apply, and the docs say so.

### CORS

Only `LiveDemo:AllowedOrigin` (default `https://receipt-tracker-demo.pages.dev`) gets `Access-Control-Allow-Origin`.
Methods GET/POST; no credentials. Other origins' browsers can't read the responses.

## Part 2: the demo site

### Switching it on

Build variable `VITE_LIVE_URL` (set in Cloudflare Pages, e.g. `https://receipt-demo.tail1234.ts.net`). When it is
unset (local builds, CI) the feature does nothing: no network calls, today's behaviour and text.

### `web/src/demo/liveReader.ts` (new)

- `liveStatus()`: `GET {VITE_LIVE_URL}/api/live` with a 3 s timeout; result cached for 30 s. Online = 200.
- A browser-side queue that sends photos **one at a time**, so one visitor's batch uses one place in my queue.
- `read(file)`: `POST {VITE_LIVE_URL}/api/extract`, returns the fields or throws with a visitor-facing message:
  the server's message for 400/429/502/503/504; "The author's PC went offline; try again later." for network errors.

### `demoApi` changes

- `uploadReceipt(file)`:
  - A sample receipt: unchanged. It always replays the recorded answer, even when online, which keeps samples fast and
    doesn't spend the budget.
  - The visitor's own photo, PC online: a **Queued** receipt with a `blob:` image URL is returned at once (like the real
    API); the queue moves it to Processing, then applies the answer exactly like a sample (`applyAnswer`: checks, flags,
    Extracted / NeedsReview, `model` kept for the corrections list). On failure: Failed with the message above.
  - PC offline: Failed with `NEEDS_MODEL` (today's behaviour).
- `retryReceipt(id)`: for a failed live read whose photo is still available in this tab (the `File` is kept in memory,
  never in `localStorage`), queue it again. Otherwise (after a reload) 409 "Upload the photo again."
  Samples and pre-made receipts keep today's message.
- `reextractReceipt`: unchanged (`NEEDS_MODEL`). _Changed 2026-09-30: sends a visitor's open photo with the hints._
- On load: receipts left Queued or Processing by a live read are marked Failed with "The page was closed before the
  author's PC finished reading it; upload the photo again."
- Photos are still never saved to `localStorage` (replaced by the placeholder, as today).

### Wording (`DemoParts.tsx`)

- Add page, when online, above the sample receipts: "The author's PC is online. Photos you add are read there by
  qwen3-vl, then deleted; nothing is kept. Don't upload receipts with personal details. Up to 20 an hour."
- Demo banner, when online: adds "Your own photos can be read on the author's PC right now."
- When offline or `VITE_LIVE_URL` is unset: today's text.

The normal (non-demo) build must contain none of this, like the rest of `src/demo/`.

## Part 3: `scripts/live-demo.ps1` and one-time setup

The script:
1. Checks Ollama answers on `localhost:11434` and has the model (`Ollama:Model` from `appsettings.json`); otherwise
   points to `scripts/pull-model`. Checks Tailscale is installed and signed in.
2. Starts the service: `dotnet run --project src/ReceiptExtractor.Api -c Release --no-launch-profile --
   --urls http://localhost:5090 --LiveDemo:Enabled=true`.
3. `tailscale funnel --bg 5090`.
4. Prints the public address, the stop time, and one line per read (time, duration, outcome, budget left); the
   service logs these, the script shows them.
5. In `finally` (Ctrl+C, or the service stopping itself): turns Funnel off. If the window is closed abruptly Funnel may
   stay on pointing at a closed port; harmless (the site sees "offline"), and the next run resets it.

One-time setup (README):
1. Install Tailscale, sign in.
2. In the Tailscale admin console, rename the machine (e.g. `receipt-demo`) so the PC's hostname isn't public.
3. Run the script once; the first `tailscale funnel` prints a link to enable HTTPS + Funnel for the tailnet.
4. Cloudflare Pages → Settings → Environment variables: `VITE_LIVE_URL` = the Funnel address; redeploy.

## Testing

API (xUnit, `WebApplicationFactory` in `LiveDemo` mode with a fake `IReceiptExtractor`):
- Only `/api/live` and `/api/extract` answer; `/api/receipts` and `/` give 404.
- Extract returns fields and category; non-image and oversized files give 400; extractor failure gives 502.
- CORS header only for the allowed origin.
- 21st read from one client in an hour → 429; budget exhausted → 429; a 4th waiting read → 503.
- No database file and no files appear under the content root / a temp data folder.
- Existing extractor tests pass through the bytes overload.

Web (vitest, `fetch` mocked):
- `VITE_LIVE_URL` unset → `fetch` never called; own photo → `NEEDS_MODEL`.
- Offline → `NEEDS_MODEL`. Online → Queued → Processing → Extracted/NeedsReview with flags and `model`.
- Photos sent one at a time; error messages mapped per status; retry works before reload, 409 after.
- Stale Queued/Processing live receipts → Failed on load. Samples never call `fetch`.

Manual end-to-end: run the script; from outside the home network (phone on mobile data) open the demo site and read a
synthetic receipt (`web/public/demo/receipts/*.jpg`). This also settles the client-IP question.

## Docs

- README: "Read on the author's PC" in the Demo site section (how it works, the limits, the one-time setup, the script).
- ROADMAP: the live item returns as the on-demand version, checked when done.
- CLAUDE.md: `LiveDemo` mode must never register the database or storage; `liveReader.ts` note.
- `.github/instructions/web.instructions.md`: `liveReader.ts` and `VITE_LIVE_URL`.

## Out of scope

- ~~Re-extract with hints on the live demo.~~ Added 2026-09-30.
- An always-on or invite-only mode; a domain or Cloudflare Access.
- A bash version of the script (only I run it, on Windows).
- Using the live PC for the sample receipts.
