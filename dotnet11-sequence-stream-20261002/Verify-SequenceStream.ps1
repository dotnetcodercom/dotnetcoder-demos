[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'Proof-Common.ps1')
Push-Location $root
try {
    $runFolder = New-ProofFolder -Root $root -Label 'sequence-stream'
    $log = Join-Path $runFolder 'console.txt'
    Write-ProofLine -Log $log -Text '=== DNC .NET 11 sequence stream verification ==='
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('--version') -Log $log
    if ($code -ne 0) { throw 'The pinned .NET 11 RC1 SDK is not installed. See global.json and README.md.' }
    $project = Join-Path $root 'Proof\Proof.csproj'
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('restore',$project,'--force','--verbosity','minimal') -Log $log
    if ($code -ne 0) { throw 'Restore failed. No package feed is configured; required framework reference packs must be installed.' }
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('build',$project,'--configuration','Release','--no-restore','--verbosity','minimal') -Log $log
    if ($code -ne 0) { throw 'Build failed. Share console.txt instead of changing the test.' }
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('run','--project',$project,'--configuration','Release','--no-build','--',$runFolder) -Log $log
    if ($code -ne 0) { throw 'Proof failed. Share console.txt and receipt.json.' }
    $receiptPath = Join-Path $runFolder 'receipt.json'
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.outcome -ne 'PASS') { throw 'The application did not record PASS.' }
    Write-ProofLine -Log $log -Text 'PASS: actual .NET 11 API checks completed'
    Write-ProofLine -Log $log -Text ('SAVED EVIDENCE: ' + $runFolder)
    exit 0
}
catch {
    $message = 'FAIL: ' + $_.Exception.Message
    if (Get-Variable log -ErrorAction SilentlyContinue) { Write-ProofLine -Log $log -Text $message } else { Write-Host $message -ForegroundColor Red }
    exit 1
}
finally { Pop-Location }
