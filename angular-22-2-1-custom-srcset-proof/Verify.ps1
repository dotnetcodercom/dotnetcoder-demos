$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function CheckExit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed." } }
& npm ci --no-audit --no-fund
CheckExit 'Verifier dependencies'
foreach ($version in @('22.2.0', '22.2.1')) {
    Push-Location (Join-Path $PSScriptRoot "versions/$version")
    try {
        & npm ci --no-audit --no-fund
        CheckExit "Angular $version dependencies"
        & npm run build
        CheckExit "Angular $version build"
    } finally { Pop-Location }
}
& npx playwright install chromium --only-shell
CheckExit 'Chromium installation'
& npm run verify
CheckExit 'Browser verification'
Write-Host 'PASS: evidence/receipt.json and four screenshots are ready.' -ForegroundColor Green
