[CmdletBinding()]
param([ValidateSet('Full', 'Local')][string]$Mode = 'Full', [switch]$NoRestore,
    [string]$MySqlBinaryDirectory, [string]$MySqlWorkingDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'artifacts/quality'
New-Item -ItemType Directory -Force -Path $results | Out-Null
try {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-gate-report.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Gate report validation tests failed.' }
    $backend=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'run-p0-gate.ps1'),'-Mode',$Mode)
    if ($NoRestore) { $backend+='-NoRestore' }
    if ($MySqlBinaryDirectory) { $backend+=@('-MySqlBinaryDirectory',$MySqlBinaryDirectory) }
    if ($MySqlWorkingDirectory) { $backend+=@('-MySqlWorkingDirectory',$MySqlWorkingDirectory) }
    & pwsh @backend
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Push-Location (Join-Path $root 'ui/zhontai.ui.admin.vue3')
    try {
        foreach ($check in @('lint', 'typecheck', 'test', 'build')) {
            & pnpm run $check 2>&1 | Tee-Object -FilePath (Join-Path $results "frontend-$check.log") | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "Frontend $check failed." }
        }
    } finally { Pop-Location }
    Write-Host "Quality $Mode Gate PASSED. Evidence: $results"
    exit 0
} catch {
    [Console]::Error.WriteLine('Quality Gate FAILED: ' + $_.Exception.Message)
    exit 1
}
