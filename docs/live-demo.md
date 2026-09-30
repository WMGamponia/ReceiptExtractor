# Demo site and live demo

How the demo site at **https://receipt-tracker-demo.pages.dev** is built and deployed, and how the on-demand live demo works. For trying the app, see [Try it](../README.md#try-it) in the README.

## Demo site (no install)

`npm run build:demo` builds the web app as a static site that needs no server and no AI model, for Cloudflare Pages:

- **Made-up data only.** 40 confirmed receipts over the past year fill the Spending page, dated relative to today so it never looks stale. Everything a visitor does stays in their browser (`localStorage`); **Reset demo** starts over.
- **Real model answers on synthetic receipts.** Real receipt photos are personal data, so they never go on the demo. `eval/make_demo_receipts.py` draws five made-up PH receipts (made-up stores, TIN `000-000-000-000`: a carinderia, a supermarket with a VAT tax table, a pharmacy with a senior citizen discount, a tilted and faded gas-station slip, a non-VAT sari-sari store) and has the real app read them with `qwen3-vl:8b-instruct`. On **Add**, picking one replays the model's actual answer after the time it really took, mistakes included: it reads the pharmacy's `08/09/2026` as September 8 (day and month swapped) and files the sari-sari store under Food.
- Uploading your own photo explains that reading receipts needs the model on your own computer, **unless the live demo is on** (below); so does **Re-extract**.

How it works: `src/api.ts` exports `demoApi` (`src/demo/`) instead of the HTTP client when built with `--mode demo`. It mirrors the API's rules (validation, checks, bulk confirm, corrections) and ports the summary periods and CSV export to TypeScript; the normal build contains none of it.

```bash
cd web
npm run build:demo                     # -> web/dist
npx vite preview --mode demo           # try it at http://localhost:4173

# after changing the prompt or the synthetic receipts (needs the app running with Ollama):
python eval/make_demo_receipts.py render
python eval/make_demo_receipts.py read --api http://localhost:5080
```

**Deploying to Cloudflare Pages:** Workers & Pages → Create → Pages → Connect to Git → this repo, then: root directory `web`, build command `npm run build:demo`, build output `dist`, environment variable `NODE_VERSION` = `24`. Every push to `main` redeploys. Page routes such as `/receipts/…` work without extra setup (Pages serves `index.html` for them).

## Read on the author's PC (live demo, on demand)

While I run `scripts/live-demo.ps1`, visitors' **own** photos are read by the real model on my PC and the answer comes back to their browser; everything else still happens in the browser. It's off by default, and the site falls back to the samples when it is.

- **Nothing is kept.** The API starts in a separate, minimal mode (`--LiveDemo:Enabled=true`, `src/ReceiptExtractor.Api/LiveDemo/`) that registers no database and no storage: two endpoints (`GET /api/live`, `POST /api/extract`), the photo read in memory and dropped. Logs have size, time and outcome only. My real app (port 5080) and Ollama are never exposed.
- **Limits:** photos up to 15 MB (bigger uploads are cut off unread), one photo at a time on the GPU (3 more may wait), 20 per visitor an hour, 100 per run, 2 minutes per photo, and it stops itself after 3 hours. Only `receipt-tracker-demo.pages.dev` may read the answers (CORS).
- **In the browser:** `web/src/demo/liveReader.ts` checks whether the PC is on (`VITE_LIVE_URL`), sends photos one at a time, and turns errors into plain messages. Samples always replay their recorded answers.
- **Re-extract** works on a visitor's own photo while it's still open in the page: it's sent again with the corrected fields (an optional `hints` form field, the same `{ "fields", "locked" }` as `/api/receipts/{id}/reextract`, checked by the same rules). The model is told they're correct and they come back unchanged. Sample receipts still need the model on your own computer.

One-time setup:
1. Install [Tailscale](https://tailscale.com/download) and sign in.
2. In the Tailscale admin console, rename this PC (e.g. `receipt-demo`) so its real name isn't in the public address.
3. Run the script once: the first `tailscale funnel` prints a link to allow Funnel (and HTTPS) on the tailnet.
4. Cloudflare Pages → the project → Settings → Variables: `VITE_LIVE_URL` = the address the script prints (`https://receipt-demo.<tailnet>.ts.net`), then redeploy.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\live-demo.ps1   # needs Ollama with the model; Ctrl+C to stop
```

It starts Ollama and Tailscale if they aren't running. Or double-click `LiveDemo.exe`, which runs the script in its own window: build it once with `dotnet publish scripts/LiveDemoLauncher -o publish/live-demo` (it remembers this checkout's folder, so build it on the PC that runs the demo).

To try a Cloudflare preview of a branch (only one site may read the answers): set `VITE_LIVE_URL` for Preview builds too, and add `-AllowedOrigin https://<branch>.receipt-tracker-demo.pages.dev`.

Trying it without the tunnel: run the API with `--urls http://localhost:5090 --LiveDemo:Enabled=true --LiveDemo:AllowedOrigin=http://localhost:4173`, then `VITE_LIVE_URL=http://localhost:5090 npm run build:demo` and `npx vite preview --mode demo`.

Design spec: [docs/superpowers/specs/2026-09-28-live-demo-design.md](superpowers/specs/2026-09-28-live-demo-design.md).
