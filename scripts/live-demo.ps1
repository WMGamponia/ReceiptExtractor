<#
  Live demo: for a few hours, visitors of the demo site (receipt-tracker-demo.pages.dev) can have their own receipt
  photos read by the model on this PC. Nothing is stored. See docs/superpowers/specs/2026-09-28-live-demo-design.md.

    powershell -ExecutionPolicy Bypass -File scripts\live-demo.ps1

  Stops by itself after 3 hours (LiveDemo:StopAfter), or press Ctrl+C. Either way, Funnel is turned off again.
  To try a Cloudflare preview of a branch instead of the live site (only one site may read the answers):
    ... live-demo.ps1 -AllowedOrigin https://<branch>.receipt-tracker-demo.pages.dev
  One-time setup (Tailscale, Funnel, VITE_LIVE_URL in Cloudflare Pages): docs/live-demo.md.
#>
param([int]$Port = 5090, [string]$AllowedOrigin)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\ReceiptExtractor.Api'
$model = (Get-Content (Join-Path $project 'appsettings.json') -Raw | ConvertFrom-Json).Ollama.Model

# 1. Ollama is running (started if it isn't) and has the model.
function Get-OllamaModels {
    try { Invoke-RestMethod 'http://localhost:11434/api/tags' -TimeoutSec 5 } catch { $null }
}
$tags = Get-OllamaModels
$ollamaApp = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama app.exe'
if (-not $tags -and (Test-Path $ollamaApp)) {
    Write-Host 'Starting Ollama...'
    Start-Process $ollamaApp
    for ($i = 0; $i -lt 30 -and -not $tags; $i++) {
        Start-Sleep -Seconds 1
        $tags = Get-OllamaModels
    }
}
if (-not $tags) {
    Write-Host "Can't reach Ollama on this PC. Start the Ollama app, then run this again."
    exit 1
}
if (-not ($tags.models.name -contains $model)) {
    Write-Host "The model $model isn't downloaded. Run scripts\pull-model.ps1, then run this again."
    exit 1
}

# 2. Tailscale is installed and connected (the Tailscale app is started if it isn't; signing in is up to you).
$tailscaleExe = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
$installed = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe' # not always on PATH
if (-not $tailscaleExe -and (Test-Path $installed)) {
    $tailscaleExe = $installed
}
if (-not $tailscaleExe) {
    Write-Host "Tailscale isn't installed. Get it from https://tailscale.com/download, sign in, then run this again."
    exit 1
}
function Get-TailscaleStatus {
    # Fails (no JSON) while the Tailscale service is still starting.
    try { & $tailscaleExe status --json 2>$null | ConvertFrom-Json } catch { $null }
}
$tailscale = Get-TailscaleStatus
$tailscaleApp = Join-Path (Split-Path $tailscaleExe) 'tailscale-ipn.exe'
if ($tailscale.BackendState -ne 'Running' -and (Test-Path $tailscaleApp)) {
    Write-Host 'Starting Tailscale...'
    Start-Process $tailscaleApp
    for ($i = 0; $i -lt 20 -and $tailscale.BackendState -ne 'Running'; $i++) {
        Start-Sleep -Seconds 1
        $tailscale = Get-TailscaleStatus
    }
}
if ($tailscale.BackendState -ne 'Running') {
    Write-Host "Tailscale isn't connected. Open Tailscale, sign in or connect, then run this again."
    exit 1
}
$address = 'https://' + $tailscale.Self.DNSName.TrimEnd('.')

# 3. Build first, so the tunnel never opens onto nothing.
dotnet build $project -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit 1 }

# 4. Open the tunnel. The first time, this prints a link to allow Funnel on your tailnet.
& $tailscaleExe funnel --bg $Port
if ($LASTEXITCODE -ne 0) {
    Write-Host "Funnel didn't start. If it printed a link, open it to allow Funnel, then run this again."
    exit 1
}

Write-Host ""
Write-Host "  Live demo is on at $address"
Write-Host "  (VITE_LIVE_URL in Cloudflare Pages must be this address.) Press Ctrl+C to stop early."
Write-Host ""
try {
    $settings = @('--urls', "http://localhost:$Port", '--LiveDemo:Enabled=true')
    if ($AllowedOrigin) { $settings += "--LiveDemo:AllowedOrigin=$AllowedOrigin" }
    dotnet run --project $project -c Release --no-build --no-launch-profile -- @settings
} finally {
    & $tailscaleExe funnel --https=443 off | Out-Null
    Write-Host "Live demo stopped; Funnel is off."
}
