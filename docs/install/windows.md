# Install on Windows

Step by step for Windows 10 or 11. For the short version, see [Try it](../../README.md#try-it) in the README.

**Have an NVIDIA or AMD graphics card?** Do steps 1–3 here, then follow [NVIDIA GPU](nvidia-gpu.md) or [AMD GPU](amd-gpu.md) to have receipts read in seconds instead of minutes.

## 1. Install Docker Desktop and Git

Open **PowerShell** and run:

```powershell
winget install -e --id Docker.DockerDesktop
winget install -e --id Git.Git
```

Or download them: [Docker Desktop](https://www.docker.com/products/docker-desktop/), [Git](https://git-scm.com/download/win) (or skip Git and use **Code → Download ZIP** on GitHub).

Then **start Docker Desktop** from the Start menu and wait until it says it's running. The first start may install WSL 2 and ask you to restart Windows.

## 2. Get the code

Open a **new** PowerShell window (so it finds `git`):

```powershell
git clone https://github.com/WMGamponia/ReceiptExtractor.git
cd ReceiptExtractor
```

Every `docker compose` command below runs from this `ReceiptExtractor` folder.

## 3. Start the app

```powershell
docker compose up --build
```

The first build takes a few minutes (~1 GB download). When the log settles, open **http://localhost:5080**. You'll see 7 real receipts waiting for review and 40 made-up ones on the Spending page.

Uploading your own photo fails with "Can't reach Ollama" at this point: the AI model isn't running yet (step 4).

Stop the app with **Ctrl+C**, or `docker compose down`.

## 4. Read your own receipts

Pick one:

- **You have an NVIDIA or AMD graphics card:** follow [NVIDIA GPU](nvidia-gpu.md) or [AMD GPU](amd-gpu.md). ~10–20s per receipt.
- **You don't (or aren't sure):** run the model inside Docker, on the CPU:

  ```powershell
  docker compose --profile ai up --build
  ```

  This downloads Ollama and the 6 GB model once. Follow the download with `docker compose logs -f ollama-pull` in a second window. Each receipt then takes **minutes** to read, and Docker needs about 8 GB of RAM for the model (Docker Desktop → Settings → Resources, if it's limited).

Not sure which graphics card you have? **Task Manager → Performance → GPU** shows its name.

## Troubleshooting

| Problem | Fix |
|---|---|
| `no configuration file provided: not found` | You're not in the `ReceiptExtractor` folder. `cd` into it |
| `error during connect` / `docker daemon is not running` | Start Docker Desktop and wait until it's running |
| `port is already allocated` (5080) | Something else uses port 5080. Stop it, or change `"5080:5080"` to e.g. `"5081:5080"` in `docker-compose.yml` and open http://localhost:5081 |
| Upload says "Can't reach Ollama" | The model isn't running: see step 4. Then press **Try again** on the receipt |
| Want to start over with fresh sample data | `docker compose down -v` (deletes all receipts and the downloaded model), then `docker compose up --build` |
