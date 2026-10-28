function Assert-GateReport {
    param([string]$Path, [ValidateSet('Full', 'Local')][string]$Mode, [int]$TestExitCode)
    if ($TestExitCode -ne 0) { throw "Test runner exited with $TestExitCode." }
    if (!(Test-Path -LiteralPath $Path)) { throw 'Fresh TRX report is missing.' }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counter = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counter -or [int]$counter.total -eq 0 -or [int]$counter.passed -ne [int]$counter.total -or [int]$counter.executed -ne [int]$counter.total) {
        throw 'Every selected test must execute and pass; failures, skips and empty selections are rejected.'
    }
    $executions = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($executions.Count -ne [int]$counter.total -or @($executions | Where-Object outcome -ne 'Passed').Count -gt 0) {
        throw 'TRX outcomes do not match the successful counter summary.'
    }
    $executedIds = @($executions | ForEach-Object testId)
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']") | Where-Object { $_.id -in $executedIds })
    $methods = @($definitions | ForEach-Object { $_.SelectSingleNode("./*[local-name()='TestMethod']") })
    $requiredSuites = @('AdminCoreAdmissionTests', 'P0SchemaGuardProductionTests', 'P0T2ModuleAndTransactionAcceptanceTests',
        'PermissionHandlerTests', 'TransactionInterceptorTests', 'DependencyHealthTests', 'DependencySecurityCompatibilityTests', 'RepositoryBoundaryTests', 'GateCoverageTests', 'P0MySqlIsolationGuardTests', 'AcceptanceFixtureCleanupTests')
    if ($Mode -eq 'Full') { $requiredSuites += @('HostMySqlAcceptanceTests', 'P0MigrationAcceptanceTests', 'P0T4HostIsolationTests', 'P0DataPermissionAcceptanceTests') }
    foreach ($suite in $requiredSuites) {
        if (!($methods | Where-Object { $_.className -match ('\.' + [regex]::Escape($suite) + '(,|$)') })) {
            throw "Required suite did not execute: $suite"
        }
    }
    if ($Mode -eq 'Full') {
        foreach ($method in @('DP01_to_DP06_RealJwtRbacAndOrgDataPermissionAreEnforced', 'LOG01_to_LOG08_RealHttpTraceErrorsAndStructuredLogsAreEnforced')) {
            if (!($methods | Where-Object name -eq $method)) { throw "Required acceptance did not execute: $method" }
        }
    }
    Write-Host "TRX totals: Passed=$($counter.passed) Total=$($counter.total) Executed=$($counter.executed)"
}
