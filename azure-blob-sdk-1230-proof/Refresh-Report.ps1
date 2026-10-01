[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js is required.' }
& node (Join-Path $PSScriptRoot 'tools/refresh-report.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Report refresh failed. Send the error above.' }
Invoke-Item (Join-Path $PSScriptRoot 'artifacts/latest/report.html')
