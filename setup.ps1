# DocumentSearch one-time setup script
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

Write-Host "==> Creating folders..."
New-Item -ItemType Directory -Force -Path "$Root\tessdata", "$Root\..\Documents" | Out-Null

if (-not (Test-Path "$Root\tessdata\eng.traineddata")) {
    Write-Host "==> Downloading Tesseract eng.traineddata (~23 MB)..."
    Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" `
        -OutFile "$Root\tessdata\eng.traineddata" -UseBasicParsing
} else {
    Write-Host "==> Tesseract data already present."
}

Write-Host "==> Building solution..."
dotnet build "$Root\DocumentSearch.sln"

Write-Host "==> Starting Docker (OpenSearch + RabbitMQ)..."
Push-Location $Root
docker compose up -d
Pop-Location

Write-Host ""
Write-Host "Setup complete. Run in two terminals:"
Write-Host "  dotnet run --project src/DocumentSearch.Api"
Write-Host "  dotnet run --project src/DocumentSearch.Worker"
