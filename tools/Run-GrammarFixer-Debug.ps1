# Run GrammarFixer from a terminal with live crash + log output.
# Usage: powershell -File tools/Run-GrammarFixer-Debug.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot "src\GrammarFixer\GrammarFixer.csproj"
$logDir   = Join-Path $env:LOCALAPPDATA "GrammarFixer\logs"
$logFile  = Join-Path $logDir ("grammerfixer_{0:yyyy-MM-dd}.log" -f (Get-Date))

Write-Host "=== GrammarFixer debug launcher ===" -ForegroundColor Cyan
Write-Host "Project : $project"
Write-Host "Log file: $logFile"
Write-Host ""
Write-Host "Tip: reproduce the crash in this window; errors print here and append to the log." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Tail log file in background so LT + pipeline lines appear live.
$tailJob = Start-Job -ScriptBlock {
    param($Path)
    if (-not (Test-Path $Path)) { New-Item -ItemType File -Force -Path $Path | Out-Null }
    Get-Content -LiteralPath $Path -Wait -Tail 0 | ForEach-Object { Write-Host "[log] $_" -ForegroundColor DarkCyan }
} -ArgumentList $logFile

try {
    Push-Location $repoRoot
    dotnet run --project $project -c Debug
    $exit = $LASTEXITCODE
}
finally {
    Stop-Job $tailJob -ErrorAction SilentlyContinue
    Remove-Job $tailJob -Force -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host ""
Write-Host "Exited with code $exit" -ForegroundColor $(if ($exit -eq 0) { "Green" } else { "Red" })
exit $exit
