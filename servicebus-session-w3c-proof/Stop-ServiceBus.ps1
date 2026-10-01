$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
  $env:DNC_OUTPUT_DIR = Join-Path $PSScriptRoot 'artifacts/latest'
  $env:MSSQL_SA_PASSWORD = 'UnusedPlaceholder!123'
  $env:ACCEPT_EULA = 'N'
  docker compose -p dnc-servicebus-w3c-proof -f docker/compose.yaml down --remove-orphans
  if ($LASTEXITCODE -ne 0) { throw 'Docker cleanup failed' }
} finally { Remove-Item Env:ACCEPT_EULA -ErrorAction SilentlyContinue; Remove-Item Env:MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue; Remove-Item Env:DNC_OUTPUT_DIR -ErrorAction SilentlyContinue; Pop-Location }
