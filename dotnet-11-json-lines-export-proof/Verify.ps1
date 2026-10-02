$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path evidence | Out-Null
& dotnet --info 2>&1 | Tee-Object -FilePath evidence/environment.txt
if ($LASTEXITCODE -ne 0) { throw 'Pinned SDK unavailable. Install the SDK specified in global.json.' }
& dotnet build -c Release --disable-build-servers -p:UseSharedCompilation=false 2>&1 | Tee-Object -FilePath evidence/build.txt
if ($LASTEXITCODE -ne 0) { throw 'Proof build failed.' }
& dotnet run -c Release --no-build 2>&1 | Tee-Object -FilePath evidence/console.txt
if ($LASTEXITCODE -ne 0) { throw 'Proof failed. Read evidence/receipt.json for the failing check.' }
Write-Host 'PASS: nine checks. Results are in evidence/receipt.json.' -ForegroundColor Green
