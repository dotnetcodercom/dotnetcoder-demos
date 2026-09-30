$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Match a NuGet diagnostic code, not NU1510 in the demo folder name.
$nu1510DiagnosticPattern = '\bNU1510\s*:'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root 'artifacts'
$transcript = Join-Path $artifacts 'verification.txt'

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Path $artifacts | Out-Null

Start-Transcript -Path $transcript -Force | Out-Null

try {
    Write-Host '=== .NET 11 NU1510 proof ==='
    Write-Host "Demo folder: $root"

    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'The dotnet command failed. Install the .NET 11 SDK and open a new PowerShell window.'
    }

    Write-Host "Selected SDK: $sdkVersion"
    $sdkMajor = [int]($sdkVersion.Split('.')[0])
    if ($sdkMajor -ne 11) {
        throw "This proof requires the selected .NET 11 SDK, but dotnet selected $sdkVersion. Check dotnet --list-sdks and any global.json above this folder."
    }

    function Invoke-DotNetLogged {
        param(
            [Parameter(Mandatory = $true)]
            [string] $Name,

            [Parameter(Mandatory = $true)]
            [string[]] $Arguments
        )

        Write-Host "`n--- $Name ---"
        Write-Host ('dotnet ' + ($Arguments -join ' '))
        $output = & dotnet @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }

        if ($exitCode -ne 0) {
            throw "$Name failed with exit code $exitCode."
        }

        return ($output | Out-String)
    }

    $beforeProject = Join-Path $root 'Before\Before.csproj'
    $afterProject = Join-Path $root 'After\After.csproj'
    $multiProject = Join-Path $root 'MultiTargetSafe\MultiTargetSafe.csproj'

    $beforeOutput = Invoke-DotNetLogged -Name 'Before restore' -Arguments @(
        'restore', $beforeProject, '--force', '--no-cache', '--verbosity', 'minimal'
    )

    if ($beforeOutput -notmatch $nu1510DiagnosticPattern) {
        throw 'Expected NU1510 was not found in the Before restore output.'
    }
    Write-Host 'PASS: Before project emits NU1510'

    $afterRestoreOutput = Invoke-DotNetLogged -Name 'After restore' -Arguments @(
        'restore', $afterProject, '--force', '--no-cache', '--verbosity', 'minimal'
    )
    if ($afterRestoreOutput -match $nu1510DiagnosticPattern) {
        throw 'Unexpected NU1510 was found in the After restore output.'
    }

    $afterBuildOutput = Invoke-DotNetLogged -Name 'After build' -Arguments @(
        'build', $afterProject, '--configuration', 'Release', '--no-restore', '--verbosity', 'minimal'
    )
    if ($afterBuildOutput -match $nu1510DiagnosticPattern) {
        throw 'Unexpected NU1510 was found in the After build output.'
    }
    Write-Host 'PASS: After project restores and builds without NU1510'

    $afterRunOutput = Invoke-DotNetLogged -Name 'After run' -Arguments @(
        'run', '--project', $afterProject, '--configuration', 'Release', '--no-build'
    )
    if ($afterRunOutput -notmatch 'APP PASS: ILogger is available from the .NET 11 shared framework') {
        throw 'The After app did not print its expected success marker.'
    }
    Write-Host 'PASS: net11.0 app runs without the explicit package reference'

    $multiRestoreOutput = Invoke-DotNetLogged -Name 'MultiTargetSafe restore' -Arguments @(
        'restore', $multiProject, '--force', '--no-cache', '--verbosity', 'minimal'
    )
    if ($multiRestoreOutput -match $nu1510DiagnosticPattern) {
        throw 'Unexpected NU1510 was found in the MultiTargetSafe restore output.'
    }

    foreach ($framework in @('net10.0', 'net11.0')) {
        $multiBuildOutput = Invoke-DotNetLogged -Name "MultiTargetSafe build $framework" -Arguments @(
            'build', $multiProject, '--configuration', 'Release', '--framework', $framework,
            '--no-restore', '--verbosity', 'minimal'
        )
        if ($multiBuildOutput -match $nu1510DiagnosticPattern) {
            throw "Unexpected NU1510 was found while building MultiTargetSafe for $framework."
        }
    }
    Write-Host 'PASS: MultiTargetSafe builds net10.0 and net11.0 without NU1510'
    Write-Host 'PASS: NU1510 proof completed'
}
finally {
    Stop-Transcript | Out-Null
}
