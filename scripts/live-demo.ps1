<#
  Live demo: for a few hours, visitors of the demo site (receipt-tracker-demo.pages.dev) can have their own receipt
  photos read by the model on this PC. Nothing is stored. See docs/superpowers/specs/2026-09-28-live-demo-design.md.

    powershell -ExecutionPolicy Bypass -File scripts\live-demo.ps1

  Stops by itself after 3 hours (LiveDemo:StopAfter), or press Ctrl+C. Either way, Funnel is turned off again.
  To try a Cloudflare preview of a branch instead of the live site (only one site may read the answers):
    ... live-demo.ps1 -AllowedOrigin https://<branch>.receipt-tracker-demo.pages.dev
  One-time setup (Tailscale, Funnel, VITE_LIVE_URL in Cloudflare Pages): README, "Read on the author's PC".
#>
param([int]$Port = 5090, [string]$AllowedOrigin)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\ReceiptExtractor.Api'
$model = (Get-Content (Join-Path $project 'appsettings.json') -Raw | ConvertFrom-Json).Ollama.Model

# 1. Ollama is running and has the model.
try {
    $tags = Invoke-RestMethod 'http://localhost:11434/api/tags' -TimeoutSec 5
} catch {
    Write-Host "Can't reach Ollama on this PC. Start the Ollama app, then run this again."
    exit 1
}
if (-not ($tags.models.name -contains $model)) {
    Write-Host "The model $model isn't downloaded. Run scripts\pull-model.ps1, then run this again."
    exit 1
}

# 2. Tailscale is installed and signed in.
if (-not (Get-Command tailscale -ErrorAction SilentlyContinue)) {
    Write-Host "Tailscale isn't installed. Get it from https://tailscale.com/download, sign in, then run this again."
    exit 1
}
$tailscale = & tailscale status --json | ConvertFrom-Json
if ($tailscale.BackendState -ne 'Running') {
    Write-Host "Tailscale isn't signed in. Open Tailscale and sign in, then run this again."
    exit 1
}
$address = 'https://' + $tailscale.Self.DNSName.TrimEnd('.')

# 3. Build first, so the tunnel never opens onto nothing.
dotnet build $project -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit 1 }

# 4. Open the tunnel. The first time, this prints a link to allow Funnel on your tailnet.
& tailscale funnel --bg $Port
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
    & tailscale funnel --https=443 off | Out-Null
    Write-Host "Live demo stopped; Funnel is off."
}
