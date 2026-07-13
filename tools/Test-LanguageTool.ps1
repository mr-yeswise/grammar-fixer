# Quick LanguageTool server test script
param([string]$Text = "This are a test sentance with erors.")

$baseUrl = "http://localhost:8081"

Write-Host "=== Health check: GET /v2/languages ===" -ForegroundColor Cyan
try {
    $langs = Invoke-RestMethod -Uri "$baseUrl/v2/languages" -Method GET -TimeoutSec 5
    Write-Host "OK - $($langs.Count) languages available" -ForegroundColor Green
} catch {
    Write-Host "FAILED: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Grammar check: POST /v2/check ===" -ForegroundColor Cyan
Write-Host "Input: $Text"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $body = @{ language = "en-US"; text = $Text; enabledOnly = "false" }
    $result = Invoke-RestMethod -Uri "$baseUrl/v2/check" -Method POST -Body $body -TimeoutSec 15
    $sw.Stop()
    Write-Host "OK in $($sw.ElapsedMilliseconds)ms - $($result.matches.Count) issue(s) found" -ForegroundColor Green
    foreach ($m in $result.matches) {
        $fix = if ($m.replacements.Count -gt 0) { $m.replacements[0].value } else { "(no fix)" }
        Write-Host "  [$($m.offset):$($m.length)] $($m.message) -> $fix"
    }
} catch {
    $sw.Stop()
    Write-Host "FAILED after $($sw.ElapsedMilliseconds)ms: $_" -ForegroundColor Red
    exit 1
}
