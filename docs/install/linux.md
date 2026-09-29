# Install on Linux

Step by step for a desktop or server Linux (Ubuntu, Debian, Fedora and similar). For the short version, see [Try it](../../README.md#try-it) in the README.

> I develop and test on Windows. The Linux steps follow Docker's and Ollama's own instructions, but I haven't run them end to end on a Linux machine.

**Have an NVIDIA or AMD graphics card?** Do steps 1–3 here, then follow [NVIDIA GPU](nvidia-gpu.md) or [AMD GPU](amd-gpu.md) to have receipts read in seconds instead of minutes.

## 1. Install Docker and Git

Install **Docker Engine with the Compose plugin** by following Docker's guide for your distribution: https://docs.docker.com/engine/install/ (Docker Desktop for Linux works too).

Then let your user run Docker without `sudo`, and install Git:

```bash
sudo usermod -aG docker $USER      # log out and back in afterwards
sudo apt install git               # Fedora: sudo dnf install git
```

Check both work:

```bash
docker compose version
git --version
```

## 2. Get the code

```bash
git clone https://github.com/WMGamponia/ReceiptExtractor.git
cd ReceiptExtractor
```

Every `docker compose` command below runs from this `ReceiptExtractor` folder.

## 3. Start the app

```bash
docker compose up --build
```

The first build takes a few minutes (~1 GB download). When the log settles, open **http://localhost:5080**. You'll see 7 real receipts waiting for review and 40 made-up ones on the Spending page.

Uploading your own photo fails with "Can't reach Ollama" at this point: the AI model isn't running yet (step 4).

Stop the app with **Ctrl+C**, or `docker compose down`.

## 4. Read your own receipts

Pick one:

- **You have an NVIDIA or AMD graphics card:** follow [NVIDIA GPU](nvidia-gpu.md) or [AMD GPU](amd-gpu.md). ~10–20s per receipt.
- **You don't (or aren't sure):** run the model inside Docker, on the CPU:

  ```bash
  docker compose --profile ai up --build
  ```

  This downloads Ollama and the 6 GB model once. Follow the download with `docker compose logs -f ollama-pull` in a second terminal. Each receipt then takes **minutes** to read, and the model needs about 8 GB of free RAM.

Not sure which graphics card you have? `lspci | grep -iE 'vga|3d'` shows it.

## Troubleshooting

| Problem | Fix |
|---|---|
| `no configuration file provided: not found` | You're not in the `ReceiptExtractor` folder. `cd` into it |
| `permission denied ... /var/run/docker.sock` | Your user isn't in the `docker` group yet: run the `usermod` line above, then log out and back in |
| `docker: 'compose' is not a docker command` | Install the Compose plugin (`docker-compose-plugin` in Docker's repository) |
| `port is already allocated` (5080) | Something else uses port 5080. Stop it, or change `"5080:5080"` to e.g. `"5081:5080"` in `docker-compose.yml` and open http://localhost:5081 |
| Upload says "Can't reach Ollama" | The model isn't running: see step 4. Then press **Try again** on the receipt |
| Want to start over with fresh sample data | `docker compose down -v` (deletes all receipts and the downloaded model), then `docker compose up --build` |
