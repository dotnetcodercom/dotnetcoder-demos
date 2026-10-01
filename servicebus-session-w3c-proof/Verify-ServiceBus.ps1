param([ValidateSet('Component','Integration','All')][string]$Mode = 'Component')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Install Node.js 22 or newer.' }
    if ($Mode -ne 'Component') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install and start Docker Desktop with Linux containers.' }
        Write-Host 'This test runs the Microsoft Service Bus emulator and SQL Server locally.'
        Write-Host 'Emulator license: https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt'
        Write-Host 'SQL Server license: https://go.microsoft.com/fwlink/?LinkId=746388'
        $answer = Read-Host 'After reading both licenses, type Y only if you accept them'
        if ($answer -cne 'Y') { throw 'Docker integration was not started. Component mode does not require these licenses.' }
        $env:ACCEPT_EULA = 'Y'
    }
    node tools/run.mjs --mode $Mode.ToLowerInvariant()
    if ($LASTEXITCODE -ne 0) { throw "Proof failed. See artifacts/latest/report.html and console.log (exit $LASTEXITCODE)." }
    Write-Host 'Open artifacts/latest/report.html. Send artifacts/latest and three screenshots.'
} finally { Remove-Item Env:ACCEPT_EULA -ErrorAction SilentlyContinue; Pop-Location }
