<#
  Downloads the receipt-reading model into Ollama on this PC, unless it's already there.
  For ReceiptTracker.exe and `dotnet run`; Docker uses scripts/pull-model.sh.

    powershell -ExecutionPolicy Bypass -File scripts\pull-model.ps1
    powershell -ExecutionPolicy Bypass -File scripts\pull-model.ps1 -Model qwen3-vl:4b-instruct

  Default model: Ollama:Model in src/ReceiptExtractor.Api/appsettings.json.
#>
param([string]$Model)

$ErrorActionPreference = 'Stop'

if (-not $Model) {
    $settings = Join-Path $PSScriptRoot '..\src\ReceiptExtractor.Api\appsettings.json'
    $Model = (Get-Content $settings -Raw | ConvertFrom-Json).Ollama.Model
}

if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
    Write-Host "Ollama isn't installed. Get it from https://ollama.com/download, then run this again."
    exit 1
}

$list = & ollama list 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Can't reach Ollama. Start the Ollama app, then run this again."
    exit 1
}

$installed = $list | Select-Object -Skip 1 | ForEach-Object { ($_ -split '\s+')[0] }
if ($installed -contains $Model) {
    Write-Host "$Model is already downloaded."
    exit 0
}

Write-Host "Downloading $Model (qwen3-vl:8b-instruct is about 6 GB)..."
& ollama pull $Model
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Done: $Model is ready. Receipts can be read now."
