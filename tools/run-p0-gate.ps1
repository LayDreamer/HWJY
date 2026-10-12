[CmdletBinding()]
param([ValidateSet('Full', 'Local')][string]$Mode = 'Full', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$required = @('ADMINCORE_T4_MYSQL', 'ADMINCORE_HOST_MYSQL', 'ADMINCORE_HOST_MYSQL_LOG', 'ADMINCORE_ACCEPTANCE_MYSQL')
if ($Mode -eq 'Full') {
    $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -gt 0) {
        [Console]::Error.WriteLine('P0 Gate BLOCKED: missing isolated MySQL configuration: ' + ($missing -join ', '))
        exit 2
    }
}
$results = Join-Path $root 'artifacts/quality'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$reportName = "p0-$Mode-$runId.trx"
$report = Join-Path $results $reportName
try {
    $arguments = @('build', (Join-Path $root 'src/ZhonTai.sln'), '-c', 'Release', '-m:1', '-nodeReuse:false')
    if ($NoRestore) { $arguments += '--no-restore' }
    & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $results 'backend-build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    $filter = 'P0Gate=Required'
    if ($Mode -eq 'Local') { $filter += '&P0Dependency!=MySql' }
    $project = Join-Path $root 'src/modules/admin/ZhonTai.Admin.Tests/ZhonTai.Admin.Tests.csproj'
    & dotnet test $project -c Release --no-build --no-restore --filter $filter '--logger:console;verbosity=normal' "--logger:trx;LogFileName=$reportName" --results-directory $results 2>&1 |
        Tee-Object -FilePath (Join-Path $results "backend-$Mode.log") | Out-Host
    $testExit = $LASTEXITCODE
    . (Join-Path $PSScriptRoot 'assert-gate-report.ps1')
    Assert-GateReport -Path $report -Mode $Mode -TestExitCode $testExit
    Write-Host "P0 $Mode Gate PASSED. Report: $report"
    if ($Mode -eq 'Local') { Write-Host 'Local Gate excludes MySQL acceptance and is not a release approval.' }
    exit 0
} catch {
    [Console]::Error.WriteLine('P0 Gate FAILED: ' + $_.Exception.Message)
    exit 1
}
