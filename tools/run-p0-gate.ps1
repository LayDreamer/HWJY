[CmdletBinding()]
param([ValidateSet('Full', 'Local')][string]$Mode = 'Full', [switch]$NoRestore,
    [switch]$ManagedMySql, [string]$MySqlBinaryDirectory, [string]$MySqlWorkingDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Mode -eq 'Full' -and !$ManagedMySql) {
    $launcher=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'run-mysql-acceptance.ps1'))
    if ($NoRestore) { $launcher+='-NoRestore' }
    if ($MySqlBinaryDirectory) { $launcher+=@('-MySqlBinaryDirectory',$MySqlBinaryDirectory) }
    if ($MySqlWorkingDirectory) { $launcher+=@('-MySqlWorkingDirectory',$MySqlWorkingDirectory) }
    & pwsh @launcher
    exit $LASTEXITCODE
}
$required = @('ADMINCORE_T4_MYSQL', 'ADMINCORE_HOST_MYSQL', 'ADMINCORE_HOST_MYSQL_LOG', 'ADMINCORE_ACCEPTANCE_MYSQL')
if ($Mode -eq 'Full') {
    $required += @('ADMINCORE_ACCEPTANCE_SERVER_UUID','ADMINCORE_ACCEPTANCE_DATA_DIRECTORY','ADMINCORE_ACCEPTANCE_INSTANCE_STATE')
    $missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -gt 0) {
        [Console]::Error.WriteLine('P0 Gate BLOCKED: missing isolated MySQL configuration: ' + ($missing -join ', '))
        exit 2
    }
    $state = Get-Content -LiteralPath $env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE -Raw | ConvertFrom-Json
    if ($state.port -ne 33306 -or $state.serverUuid -ne $env:ADMINCORE_ACCEPTANCE_SERVER_UUID -or $state.dataDirectory -ne $env:ADMINCORE_ACCEPTANCE_DATA_DIRECTORY) {
        throw 'Managed MySQL instance proof is inconsistent.'
    }
}
$results = Join-Path $root 'artifacts/quality'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$reportName = "p0-$Mode-$runId.trx"
$report = Join-Path $results $reportName
try {
    $arguments = @('build', (Join-Path $root 'src/ZhonTai.sln'), '-c', 'Release', '-m:1', '-nodeReuse:false','--tl:off')
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
