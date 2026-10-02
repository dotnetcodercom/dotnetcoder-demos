[CmdletBinding()]
param([ValidateSet('Isolated','Shared')][string]$Mode = 'Isolated')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'Proof-Common.ps1')
Push-Location $root
try {
    $runFolder = New-ProofFolder -Root $root -Label ('sdk-' + $Mode.ToLowerInvariant())
    $log = Join-Path $runFolder 'console.txt'
    Write-ProofLine -Log $log -Text '=== DNC parallel .NET publish verification ==='
    Write-ProofLine -Log $log -Text 'Scope: local SDK artifact isolation only. No Azure provisioning or deployment.'
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('--version') -Log $log
    if ($code -ne 0) { throw 'No suitable .NET SDK. Read README.md for prerequisites.' }
    $code = Invoke-ProofCommand -File $dotnet -Arguments @('run','--project',(Join-Path $root 'Runner\Runner.csproj'),'--configuration','Release','--',$root,$Mode.ToLowerInvariant()) -Log $log
    if ($code -ne 0) { throw 'Verification failed. Share console.txt and receipt.json; do not call the Demo verified.' }
    Write-ProofLine -Log $log -Text ('SAVED CONSOLE: ' + $log)
    if ($Mode -eq 'Shared') {
        Write-ProofLine -Log $log -Text 'Shared mode is diagnostic only. NO_RACE_OBSERVED is an honest possible result.'
    }
    exit 0
}
catch {
    $message = 'FAIL: ' + $_.Exception.Message
    if (Get-Variable log -ErrorAction SilentlyContinue) { Write-ProofLine -Log $log -Text $message } else { Write-Host $message -ForegroundColor Red }
    exit 1
}
finally { Pop-Location }
