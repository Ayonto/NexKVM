# Builds a single, self-contained NexKVM.exe (no .NET install needed to run it) and zips it.
# Usage:  powershell -ExecutionPolicy Bypass -File build-release.ps1
$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "NexKVM.csproj"
$outDir = Join-Path $PSScriptRoot "publish"

Write-Host "Publishing self-contained single-file exe..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outDir

$exe = Join-Path $outDir "NexKVM.exe"
if (-not (Test-Path $exe)) { throw "Build failed: NexKVM.exe not found." }

$zip = Join-Path $PSScriptRoot "NexKVM-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe -DestinationPath $zip

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done." -ForegroundColor Green
Write-Host "  exe: $exe  ($size MB)"
Write-Host "  zip: $zip"
