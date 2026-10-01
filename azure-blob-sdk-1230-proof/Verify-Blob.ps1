[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw 'Node.js 22 or newer is required. Install Node.js LTS, reopen PowerShell, and run again.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 10 or 11 is required. Reopen PowerShell after installing the SDK.'
}
& node (Join-Path $PSScriptRoot 'tools/run.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Blob proof failed. Send the newest artifacts folder or the console output.' }
Write-Host ''
Write-Host 'Open artifacts/latest/report.html and capture the three sections listed in README-AR.md.' -ForegroundColor Green
