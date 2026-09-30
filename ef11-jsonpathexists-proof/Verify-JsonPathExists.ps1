param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $SqlUser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$transcript = Join-Path $artifacts 'verification.txt'
$previousConnection = $env:DNC_SQL_CONNECTION
$transcriptStarted = $false
$exitCode = 1

function Invoke-DotNetChecked {
    param([string[]] $Arguments)
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 can turn native stderr into ErrorRecords.
        $ErrorActionPreference = 'Continue'
        & dotnet @Arguments
        $nativeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($nativeExitCode -ne 0) {
        throw "dotnet command failed with exit code $nativeExitCode."
    }
}

Push-Location $root
try {
    Start-Transcript -Path $transcript -Force | Out-Null
    $transcriptStarted = $true
    Write-Host '=== DNC EF11 JsonPathExists verification ==='
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^11\.') {
        throw 'This proof needs the .NET 11 SDK. Run dotnet --list-sdks and check global.json.'
    }
    Write-Host "Selected SDK: $sdk"

    Write-Host "`n--- Restore ---"
    Invoke-DotNetChecked -Arguments @('restore', '.\JsonPathProof.csproj', '--configfile', '.\NuGet.Config')
    Write-Host "`n--- Build ---"
    Invoke-DotNetChecked -Arguments @('build', '.\JsonPathProof.csproj', '--configuration', 'Release', '--no-restore')
    Write-Host 'PASS: restore and build completed'

    # Build a connection locally; never put the password in command arguments or files.
    Add-Type -AssemblyName System.Data
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    # PowerShell adapts this IDictionary as connection-string keys.
    # Use accepted key names rather than CLR property names.
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = 'tempdb'
    $builder['Connect Timeout'] = 15
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    if ([string]::IsNullOrWhiteSpace($SqlUser)) {
        $builder['Integrated Security'] = $true
        Write-Host 'Authentication: Windows'
    }
    else {
        $builder['Integrated Security'] = $false
        $builder['User ID'] = $SqlUser
        $securePassword = Read-Host 'SQL password (hidden; do not send it in chat)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        try {
            $builder['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
        Write-Host 'Authentication: SQL login'
    }
    $env:DNC_SQL_CONNECTION = $builder.get_ConnectionString()

    Write-Host "`n--- SQL Server execution ---"
    Invoke-DotNetChecked -Arguments @('run', '--project', '.\JsonPathProof.csproj', '--configuration', 'Release', '--no-build')
    Write-Host 'PASS: laptop verification completed'
    $exitCode = 0
}
catch {
    Write-Host ('FAIL: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'See README-AR.md. Send the error text, but never send credentials.'
}
finally {
    $env:DNC_SQL_CONNECTION = $previousConnection
    if ($builder) { $builder.Clear() }
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    Pop-Location
    Write-Host "Verification log: $transcript"
}
exit $exitCode
