# Windows PowerShell 5.1 and PowerShell 7 compatible.
function New-ProofFolder {
    param([string]$Root,[string]$Label)
    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
    $folder = Join-Path (Join-Path $Root 'evidence') ($stamp + '-' + $Label)
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    return $folder
}
function Write-ProofLine {
    param([string]$Log,[string]$Text)
    Write-Host $Text
    Add-Content -LiteralPath $Log -Value $Text -Encoding UTF8
}
function Invoke-ProofCommand {
    param([string]$File,[string[]]$Arguments,[string]$Log)
    Write-ProofLine -Log $Log -Text ('COMMAND: ' + [IO.Path]::GetFileName($File) + ' ' + ($Arguments -join ' '))
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $File @Arguments 2>&1 | ForEach-Object {
            Write-ProofLine -Log $Log -Text ($_.ToString())
        }
        $nativeExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    Write-ProofLine -Log $Log -Text ('EXIT CODE: ' + $nativeExit)
    return [int]$nativeExit
}
