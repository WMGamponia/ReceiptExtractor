# Reading receipts on an AMD GPU

With an AMD Radeon card, the model reads a receipt in seconds instead of minutes (9s median on my RX 9070 XT). First get the app running with [Windows](windows.md) or [Linux](linux.md) steps 1–3.

**What you need:** a Radeon card that Ollama supports through AMD's ROCm (the RX 7000 and 9000 series; on Linux also many RX 6000 cards; see [Ollama's GPU list](https://docs.ollama.com/gpu)), ideally with **8 GB or more of video memory** for the 6 GB model. My RX 9070 XT works on Windows even though the list only shows it for Linux. Cards not on the list may still work through Vulkan, which Ollama also supports, but I haven't tried that.

## Windows

**Docker Desktop on Windows can't give containers an AMD GPU**, so Ollama has to run on Windows itself. This is the setup I use and test with.

1. Update your AMD driver (AMD Software: Adrenalin Edition, https://www.amd.com/en/support/download/drivers.html). Ollama needs a recent driver for ROCm.
2. Install Ollama and download the model:

   ```powershell
   winget install -e --id Ollama.Ollama
   ollama pull qwen3-vl:8b-instruct
   ```

   (Or download Ollama from https://ollama.com/download. Open a new PowerShell window after installing so it finds `ollama`.)
3. Start the app, pointed at that Ollama (from the `ReceiptExtractor` folder):

   ```powershell
   $env:OLLAMA_URL="http://host.docker.internal:11434"; docker compose up --build
   ```

Don't use `--profile ai` here: that runs a second Ollama inside Docker, which can only use the CPU.

## Linux

> Untested: I don't have AMD hardware on Linux. These steps follow AMD's and Ollama's instructions.

**Option A: Ollama inside Docker, with the GPU passed through.** Nothing else listens on your network.

1. Install AMD's GPU driver with ROCm support for your distribution: https://rocm.docs.amd.com/projects/install-on-linux/en/latest/ (the `amdgpu-install` tool). Check that `/dev/kfd` exists afterwards.
2. Make sure your user is in the `video` and `render` groups: `sudo usermod -aG video,render $USER`, then log out and back in.
3. Start the app with the AMD override file, which uses Ollama's ROCm image and passes `/dev/kfd` and `/dev/dri` into the container:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.amd.yml --profile ai up --build
   ```

   The model downloads once (6 GB); follow it with `docker compose logs -f ollama-pull`.

**Option B: Ollama installed on Linux.** Install it with `curl -fsSL https://ollama.com/install.sh | sh` (it sets up ROCm support for AMD cards) and run `ollama pull qwen3-vl:8b-instruct`. Ollama only listens on `127.0.0.1` by default, which containers can't reach, so let it listen on all addresses:

```bash
sudo systemctl edit ollama.service
# add these two lines, save, then:
#   [Service]
#   Environment="OLLAMA_HOST=0.0.0.0:11434"
sudo systemctl restart ollama
OLLAMA_URL=http://host.docker.internal:11434 docker compose up --build
```

This makes Ollama reachable from your network too, so block port 11434 in your firewall if the PC isn't on a trusted network.

## Check it's using the GPU

Upload a receipt, and while it's being read run `ollama ps` (with Ollama in Docker: `docker compose exec ollama ollama ps`). The `PROCESSOR` column should say **100% GPU**. Task Manager → Performance → GPU (Windows) or `rocm-smi` (Linux) should also show it busy.

| What you see | What it means |
|---|---|
| `100% CPU` | The GPU isn't being used: update the AMD driver, check your card is supported, and restart Ollama (on Windows: quit it from the system tray and start it again) |
| e.g. `40%/60% CPU/GPU` | The model doesn't fit in video memory, so part of it runs on the CPU. It works, just slower |
| Linux: `error gathering device information` or `/dev/kfd: permission denied` in `docker compose logs ollama` | The ROCm driver isn't installed, or the container can't open the GPU. Try adding `group_add: [video, render]` under `ollama:` in `docker-compose.amd.yml` |
