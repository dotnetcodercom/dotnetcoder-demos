[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path '.\artifacts' | Out-Null
Remove-Item '.\artifacts\result.json' -ErrorAction SilentlyContinue
Remove-Item '.\artifacts\http-cases.txt' -ErrorAction SilentlyContinue
$transcribing = $false
$failed = $false
try {
    Start-Transcript -Path '.\artifacts\verification.txt' -Force | Out-Null
    $transcribing = $true
    Write-Host '=== DNC Native C# Union verification ==='
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet was not found. Install the .NET 11 SDK, then open a new PowerShell window.'
    }
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'SDK selection failed. See global.json and README-AR.md.' }
    Write-Host "Selected SDK: $sdk"
    if ($sdk -notmatch '^11\.') { throw 'This project requires a .NET 11 SDK.' }
    Write-Host "`n--- Restore ---"
    & dotnet restore .\UnionProof.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    Write-Host "`n--- Build ---"
    & dotnet build .\UnionProof.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host "`n--- Real HTTP + OpenAPI verification ---"
    & dotnet .\bin\Release\net11.0\UnionProof.dll --verify
    if ($LASTEXITCODE -ne 0) { throw 'HTTP/OpenAPI verification failed.' }
    if (-not (Test-Path '.\artifacts\result.json')) { throw 'Verification did not create a success result.' }
    Write-Host 'VERIFICATION COMPLETE: native union, HTTP JSON, and generated OpenAPI checked.'
}
catch {
    $failed = $true
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Send artifacts\verification.txt or the error text.'
}
finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    Write-Host "Verification log: $PSScriptRoot\artifacts\verification.txt"
}
if ($failed) { exit 1 }
