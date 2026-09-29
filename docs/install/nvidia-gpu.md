# Reading receipts on an NVIDIA GPU

With an NVIDIA graphics card, the model reads a receipt in seconds instead of minutes. First get the app running with [Windows](windows.md) or [Linux](linux.md) steps 1–3.

**What you need:** an NVIDIA card that Ollama supports (compute capability 5.0 or newer, which is most GeForce cards from the GTX 900 series on; see [Ollama's GPU list](https://docs.ollama.com/gpu)), ideally with **8 GB or more of video memory** for the 6 GB model. With less, Ollama splits the model between the GPU and the CPU, which is slower.

> I test on an AMD card, so these NVIDIA steps haven't been run end to end. They follow Docker's, NVIDIA's and Ollama's own instructions.

## Windows

**Recommended: Ollama on Windows.** It's the same setup I use with my AMD card.

1. Update your NVIDIA driver (GeForce Experience / NVIDIA App, or https://www.nvidia.com/drivers).
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

**Alternative: Ollama inside Docker.** Docker Desktop can pass an NVIDIA GPU into containers through WSL 2 (it needs a current NVIDIA driver; see [Docker's GPU page](https://docs.docker.com/desktop/features/gpu/)):

```powershell
docker compose -f docker-compose.yml -f docker-compose.nvidia.yml --profile ai up --build
```

## Linux

**Recommended: Ollama inside Docker, with the GPU passed through.** Nothing else listens on your network.

1. Install the NVIDIA driver for your distribution and check it: `nvidia-smi` should list your card.
2. Install the **NVIDIA Container Toolkit** and configure Docker for it: https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html (ends with `sudo nvidia-ctk runtime configure --runtime=docker` and `sudo systemctl restart docker`).
3. Start the app with the NVIDIA override file (from the `ReceiptExtractor` folder):

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.nvidia.yml --profile ai up --build
   ```

   The model downloads once (6 GB); follow it with `docker compose logs -f ollama-pull`.

**Alternative: Ollama installed on Linux.** Install it with `curl -fsSL https://ollama.com/install.sh | sh` and run `ollama pull qwen3-vl:8b-instruct`. Ollama only listens on `127.0.0.1` by default, which containers can't reach, so let it listen on all addresses:

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

Upload a receipt, and while it's being read run `ollama ps` (with Ollama in Docker: `docker compose exec ollama ollama ps`). The `PROCESSOR` column should say **100% GPU**. `nvidia-smi` should also show an `ollama` process using video memory.

| What you see | What it means |
|---|---|
| `100% CPU` | The GPU isn't being used: check the driver (and on Linux in Docker, the Container Toolkit), then restart Ollama |
| e.g. `40%/60% CPU/GPU` | The model doesn't fit in video memory, so part of it runs on the CPU. It works, just slower |
| `could not select device driver "nvidia"` | Linux: the NVIDIA Container Toolkit isn't installed or Docker wasn't restarted after configuring it |
