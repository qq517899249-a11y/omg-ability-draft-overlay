$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force research | Out-Null
# A public mirror snapshot is used until an authorized direct Windrun feed is available.
# Do not attempt to bypass Windrun's verification page.
$candidate = Join-Path (Get-Location) 'research/ad_data.candidate.js'
Invoke-WebRequest -Uri 'http://43.130.62.185/ad/ad_data.js' -OutFile $candidate -TimeoutSec 30
node tools/prepare-data.mjs $candidate
if ($LASTEXITCODE -ne 0) { throw 'Invalid source; previous statistics were retained.' }
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Statistics were updated but the application copy was not refreshed.' }
Write-Host 'Snapshot refreshed. Restart OMG AD to load it.'
