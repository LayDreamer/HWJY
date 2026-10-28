[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MySqlBinaryDirectory,
    [string]$EvidenceDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('admincore-mysql-verification-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Verification evidence requires a fresh directory.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$binary = (Resolve-Path -LiteralPath $MySqlBinaryDirectory).Path
$ci = Join-Path $repo 'tools/ci-mysql.ps1'
$launcher = Join-Path $repo 'tools/run-mysql-acceptance.ps1'
$names = @('ADMINCORE_HOST_MYSQL','ADMINCORE_HOST_MYSQL_LOG','ADMINCORE_T4_MYSQL','ADMINCORE_ACCEPTANCE_MYSQL',
    'ADMINCORE_ACCEPTANCE_SERVER_UUID','ADMINCORE_ACCEPTANCE_DATA_DIRECTORY','ADMINCORE_ACCEPTANCE_INSTANCE_STATE',
    'ADMINCORE_CONFIGCENTER_PATH','DbConfig__ConnectionString','DbConfig__Dbs__0__ConnectionString','MYSQL_PWD',
    'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','MSBuildSDKsPath')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$ownedRoots = [Collections.Generic.List[string]]::new()
$results = [ordered]@{}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function New-InstanceRoot([string]$Label) {
    $path = Join-Path ([IO.Path]::GetTempPath()) ('hwjy-mysql-' + $Label + '-' + [Guid]::NewGuid().ToString('N'))
    $ownedRoots.Add($path)
    return $path
}
function Assert-PortFree {
    $listeners=@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 33306)
    Require ($listeners.Count -eq 0) 'An active listener remains on port 33306.'
}
function Daily-Listeners {
    [ordered]@{
        listeners=@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 3306 | ForEach-Object { $_.ToString() } | Sort-Object)
        processes=@(Get-Process -Name mysqld -ErrorAction SilentlyContinue | Sort-Object Id | ForEach-Object { [ordered]@{id=$_.Id;path=$_.Path;startedAt=$_.StartTime.ToUniversalTime().ToString('o')} })
    } | ConvertTo-Json -Depth 5 -Compress
}
function Assert-Stopped([string]$Root) {
    Require (!(Test-Path -LiteralPath (Join-Path $Root 'process.json'))) 'Active ownership proof survived successful cleanup.'
    $proof = Get-Content -LiteralPath (Join-Path $Root 'process.stopped.json') -Raw | ConvertFrom-Json
    Require ($proof.port -eq 33306) 'Unexpected instance port in proof.'
    foreach ($entry in @(@{processId=$proof.processId;startedAt=$proof.startedAt}) + @($proof.children)) {
        if (!$entry.processId) { continue }
        $remaining = Get-Process -Id $entry.processId -ErrorAction SilentlyContinue
        if ($remaining -and $remaining.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$entry.startedAt).ToUniversalTime().Ticks) {
            throw 'An owned MySQL process survived cleanup.'
        }
    }
    Assert-PortFree
}
try {
    Assert-PortFree
    $dailyBefore = Daily-Listeners
    $results.dailyListenerBefore = $dailyBefore

    $invalidRoot = New-InstanceRoot 'invalid-port'
    $rejected = $false
    try { & $ci -Action Start -Port 3306 -WorkingDirectory $invalidRoot -BinaryDirectory $binary } catch { $rejected=$true }
    Require ($rejected -and !(Test-Path -LiteralPath $invalidRoot)) '3306 was not rejected before initialization.'
    $results.reject3306BeforeInitialization=$true
    Write-Output 'PASS: port 3306 rejected before initialization.'

    $occupiedRoot = New-InstanceRoot 'occupied'
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,33306)
    $listener.Start()
    try {
        $rejected=$false
        try { & $ci -Action Start -WorkingDirectory $occupiedRoot -BinaryDirectory $binary } catch { $rejected=$true }
        Require ($rejected -and !(Test-Path -LiteralPath $occupiedRoot)) 'An existing listener was not rejected safely.'
    } finally { $listener.Stop() }
    $results.rejectOccupiedPortBeforeInitialization=$true
    Write-Output 'PASS: occupied 33306 rejected before initialization.'

    $existingRoot=New-InstanceRoot 'existing'
    New-Item -ItemType Directory -Path $existingRoot | Out-Null
    $sentinel=Join-Path $existingRoot 'sentinel.txt'
    [IO.File]::WriteAllText($sentinel,'must-preserve')
    $rejected=$false
    try { & $ci -Action Start -WorkingDirectory $existingRoot -BinaryDirectory $binary } catch { $rejected=$true }
    Require ($rejected -and [IO.File]::ReadAllText($sentinel) -ceq 'must-preserve' -and !(Test-Path (Join-Path $existingRoot 'data'))) 'Existing directory was modified.'
    $results.rejectExistingDirectory=$true
    Write-Output 'PASS: existing directory preserved.'

    $smokeRoot=New-InstanceRoot 'lifecycle'
    & $ci -Action Start -WorkingDirectory $smokeRoot -BinaryDirectory $binary
    $statePath=Join-Path $smokeRoot 'process.json'
    $originalState=[IO.File]::ReadAllText($statePath)
    $state=$originalState | ConvertFrom-Json
    Require ($state.serverUuid -and $state.port -eq 33306) 'Live instance identity not recorded.'
    $results.startupIdentityRecorded=$true
    $state.port=3306
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath
    $rejected=$false
    try { & $ci -Action Stop -WorkingDirectory $smokeRoot } catch { $rejected=$true }
    Require $rejected 'Cleanup accepted altered instance identity.'
    [IO.File]::WriteAllText($statePath,$originalState)
    $state=$originalState | ConvertFrom-Json
    $state.startedAt=([DateTime]$state.startedAt).AddSeconds(-1).ToString('o')
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath
    $rejected=$false
    try { & $ci -Action Stop -WorkingDirectory $smokeRoot } catch { $rejected=$true }
    Require $rejected 'Cleanup accepted an altered process start time.'
    [IO.File]::WriteAllText($statePath,$originalState)
    & $ci -Action Stop -WorkingDirectory $smokeRoot
    Assert-Stopped $smokeRoot
    $stoppedPath=Join-Path $smokeRoot 'process.stopped.json'
    $stoppedState=[IO.File]::ReadAllText($stoppedPath)
    # A completed proof may now name a reused PID; it must never be used for cleanup again.
    $state=$stoppedState | ConvertFrom-Json
    $state.processId=$PID
    $state.startedAt=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stoppedPath
    & $ci -Action Stop -WorkingDirectory $smokeRoot
    [IO.File]::WriteAllText($stoppedPath,$stoppedState)
    Assert-Stopped $smokeRoot
    $results.rejectAlteredCleanupProof=$true
    $results.rejectAlteredProcessIdentity=$true
    $results.completedProofIgnoredOnRepeatedStop=$true
    $results.successCleanupAndRepeatedStop=$true
    foreach ($name in $names) {
        if ($null -eq $original[$name]) { Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name,$original[$name],'Process') }
    }
    Write-Output 'PASS: real startup, identity refusal, cleanup and repeated stop.'

    $fake='Server=127.0.0.1;Port=3306;Database=do_not_connect;User ID=sentinel;Password=sentinel;SslMode=Required'
    foreach ($name in @('ADMINCORE_HOST_MYSQL','ADMINCORE_HOST_MYSQL_LOG','ADMINCORE_T4_MYSQL','ADMINCORE_ACCEPTANCE_MYSQL','DbConfig__ConnectionString','DbConfig__Dbs__0__ConnectionString')) {
        [Environment]::SetEnvironmentVariable($name,$fake,'Process')
    }
    $env:ADMINCORE_CONFIGCENTER_PATH='must-not-load-daily-config'
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $env:DOTNET_ENVIRONMENT='Development'
    Remove-Item Env:ADMINCORE_ACCEPTANCE_SERVER_UUID,Env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable('ADMINCORE_ACCEPTANCE_DATA_DIRECTORY','','Process')
    $env:MSBuildSDKsPath=Join-Path $evidence 'intentionally-missing-sdk'
    $inherited=@{}
    foreach ($name in $names) { $inherited[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
    $failureRoot=New-InstanceRoot 'failure'
    & $launcher -NoRestore -MySqlBinaryDirectory $binary -MySqlWorkingDirectory $failureRoot *> (Join-Path $evidence 'injected-failure.log')
    $failureExit=$LASTEXITCODE
    Require ($failureExit -eq 1) 'Injected downstream build failure did not fail the launcher.'
    $failureText=[IO.File]::ReadAllText((Join-Path $evidence 'injected-failure.log'))
    Require ($failureText.Contains('MSB') -and $failureText.Contains('build')) 'Failure did not reach the intentionally broken build.'
    Assert-Stopped $failureRoot
    foreach ($name in $names) { Require ([Environment]::GetEnvironmentVariable($name,'Process') -ceq $inherited[$name]) ('Environment was not restored: '+$name) }
    Require (!(Test-Path Env:ADMINCORE_ACCEPTANCE_SERVER_UUID)) 'Originally missing environment key was not removed.'
    Require ((Test-Path Env:ADMINCORE_ACCEPTANCE_DATA_DIRECTORY) -and [Environment]::GetEnvironmentVariable('ADMINCORE_ACCEPTANCE_DATA_DIRECTORY','Process') -ceq '') 'Originally empty environment key was not retained.'
    $results.downstreamFailureExit=$failureExit
    $results.failureCleanup=$true
    $results.environmentRestoration=$true
    Write-Output 'PASS: downstream failure cleanup and caller environment restoration.'

    if ([string]::IsNullOrEmpty($original['MSBuildSDKsPath'])) { Remove-Item Env:MSBuildSDKsPath -ErrorAction SilentlyContinue }
    else { [Environment]::SetEnvironmentVariable('MSBuildSDKsPath',$original['MSBuildSDKsPath'],'Process') }
    $fullRoot=New-InstanceRoot 'full'
    & pwsh -NoProfile -File (Join-Path $repo 'tools/run-p0-gate.ps1') -Mode Full -NoRestore -MySqlBinaryDirectory $binary -MySqlWorkingDirectory $fullRoot *> (Join-Path $evidence 'full-gate.log')
    $fullExit=$LASTEXITCODE
    Require ($fullExit -eq 0) 'Full acceptance failed; inspect full-gate.log.'
    Assert-Stopped $fullRoot
    $trx=Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts/quality') -Filter 'p0-Full-*.trx' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    [xml]$report=Get-Content -LiteralPath $trx.FullName -Raw
    $counters=$report.TestRun.ResultSummary.Counters
    Require ([int]$counters.total -gt 0 -and $counters.total -eq $counters.passed -and [int]$counters.notExecuted -eq 0) 'Full TRX is incomplete.'
    $results.fullGateExit=$fullExit
    $results.fullTests=[ordered]@{total=[int]$counters.total;passed=[int]$counters.passed;failed=[int]$counters.failed;skipped=[int]$counters.notExecuted}
    $results.fullInstanceRoot=$fullRoot
    $results.fullInstanceStopped=$true
    $dailyAfter=Daily-Listeners
    Require ($dailyBefore -ceq $dailyAfter) 'Daily 3306 listener identity changed during verification.'
    $results.dailyListenerUnchanged=$true
    $results.completedAt=[DateTime]::UtcNow.ToString('o')
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'verification.json')
    Write-Output ('PASS: public Full gate '+$counters.passed+'/'+$counters.total+' with inherited unsafe config cleared; owned processes stopped; daily listener unchanged.')
    Write-Output ('Evidence: '+(Join-Path $evidence 'verification.json'))
} finally {
    foreach ($ownedRoot in $ownedRoots) {
        if (Test-Path -LiteralPath (Join-Path $ownedRoot 'process.json')) {
            & $ci -Action Stop -WorkingDirectory $ownedRoot
        }
    }
    foreach ($name in $names) {
        if ($null -eq $original[$name]) { Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name,$original[$name],'Process') }
    }
}
