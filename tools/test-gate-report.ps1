$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-gate-report.ps1')
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root ('artifacts/quality/gate-parser-' + [Guid]::NewGuid().ToString('N') + '.trx')
New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
$suites = @('AdminCoreAdmissionTests', 'P0SchemaGuardProductionTests', 'P0T2ModuleAndTransactionAcceptanceTests',
    'PermissionHandlerTests', 'TransactionInterceptorTests', 'DependencyHealthTests', 'DependencySecurityCompatibilityTests', 'RepositoryBoundaryTests', 'GateCoverageTests', 'P0MySqlIsolationGuardTests', 'AcceptanceFixtureCleanupTests')
$methods = ($suites | ForEach-Object { '<UnitTest id="' + $_ + '"><TestMethod className="ZhonTai.Admin.Tests.Acceptance.' + $_ + '" name="probe"/></UnitTest>' }) -join ''
$executions = '<Results>' + (($suites | ForEach-Object { '<UnitTestResult testId="' + $_ + '" outcome="Passed"/>' }) -join '') + '</Results>'
function Write-Probe([int]$Total, [int]$Passed, [int]$Executed, [string]$Definitions = $methods) {
    '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>' + $Definitions +
        '</TestDefinitions>' + $executions + '<ResultSummary><Counters total="' + $Total + '" passed="' + $Passed + '" executed="' + $Executed + '"/></ResultSummary></TestRun>' |
        Set-Content -LiteralPath $path
}
function Must-Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (!$rejected) { throw 'Gate accepted an invalid report.' }
}
try {
    Write-Probe 11 11 11
    Assert-GateReport -Path $path -Mode Local -TestExitCode 0
    Must-Reject { Assert-GateReport -Path $path -Mode Full -TestExitCode 0 }
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 1 }
    Write-Probe 11 10 10
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    Write-Probe 11 10 11
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    Write-Probe 0 0 0
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    $executions = $executions.Replace('outcome="Passed"', 'outcome="NotExecuted"')
    Write-Probe 11 11 11
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    $executions = $executions.Replace('outcome="NotExecuted"', 'outcome="Passed"')
    Write-Probe 11 11 11 ($methods -replace '<UnitTest id="GateCoverageTests">.*?</UnitTest>', '')
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    Write-Probe 11 11 11 ($methods -replace '<UnitTest id="P0MySqlIsolationGuardTests">.*?</UnitTest>', '')
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    Write-Probe 11 11 11 ($methods -replace '<UnitTest id="AcceptanceFixtureCleanupTests">.*?</UnitTest>', '')
    Must-Reject { Assert-GateReport -Path $path -Mode Local -TestExitCode 0 }
    Must-Reject { Assert-GateReport -Path ($path + '.missing') -Mode Local -TestExitCode 0 }
    $fullSuites = $suites + @('HostMySqlAcceptanceTests', 'P0MigrationAcceptanceTests', 'P0T4HostIsolationTests', 'P0DataPermissionAcceptanceTests')
    $fullMethods = ($fullSuites | ForEach-Object { '<UnitTest id="' + $_ + '"><TestMethod className="ZhonTai.Admin.Tests.Acceptance.' + $_ + '" name="probe"/></UnitTest>' }) -join ''
    $acceptances = @('DP01_to_DP06_RealJwtRbacAndOrgDataPermissionAreEnforced', 'LOG01_to_LOG08_RealHttpTraceErrorsAndStructuredLogsAreEnforced')
    $fullMethods += ($acceptances | ForEach-Object { '<UnitTest id="' + $_ + '"><TestMethod className="ZhonTai.Admin.Tests.Acceptance.P0DataPermissionAcceptanceTests" name="' + $_ + '"/></UnitTest>' }) -join ''
    $executions = '<Results>' + (($fullSuites + $acceptances | ForEach-Object { '<UnitTestResult testId="' + $_ + '" outcome="Passed"/>' }) -join '') + '</Results>'
    Write-Probe 17 17 17 $fullMethods
    Assert-GateReport -Path $path -Mode Full -TestExitCode 0
    foreach ($acceptance in $acceptances) {
        Write-Probe 17 17 17 ($fullMethods -replace ('<UnitTest id="' + $acceptance + '">.*?</UnitTest>'), '')
        Must-Reject { Assert-GateReport -Path $path -Mode Full -TestExitCode 0 }
    }
    Write-Host 'Gate parser: valid report accepted; skipped, failed, empty, incomplete, missing and failed-run reports rejected.'
} finally {
    Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
}
