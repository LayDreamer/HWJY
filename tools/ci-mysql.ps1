[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop')][string]$Action = 'Start',
    [string]$WorkingDirectory,
    [string]$BinaryDirectory,
    [ValidateSet(33306)][int]$Port = 33306
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'This acceptance environment requires Windows.' }
if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    if ($Action -eq 'Stop') {
        if (!$env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE) { return }
        $WorkingDirectory = Split-Path $env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE -Parent
    } else {
        $runtimeBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $WorkingDirectory = Join-Path $runtimeBase ('admincore-ci-mysql-' + [Guid]::NewGuid().ToString('N'))
    }
}
$root = [IO.Path]::GetFullPath($WorkingDirectory)
$statePath = Join-Path $root 'process.json'
$data = Join-Path $root 'data'
$pidFile = Join-Path $root 'server.pid'
if ($Action -eq 'Stop') {
    if (!(Test-Path -LiteralPath $statePath)) { return }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($state.port -ne 33306 -or [IO.Path]::GetFullPath($state.dataDirectory).TrimEnd('\','/') -ne $data.TrimEnd('\','/')) {
        throw 'State is not an owned 33306 acceptance instance; refusing to stop it.'
    }
    foreach ($entry in @(@{ processId=$state.processId; startedAt=$state.startedAt }) + @($state.children)) {
        if (!$entry.processId) { continue }
        $ownedProcess = Get-Process -Id $entry.processId -ErrorAction SilentlyContinue
        if (!$ownedProcess) { continue }
        if ($ownedProcess.Path -ne $state.executable -or $ownedProcess.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$entry.startedAt).ToUniversalTime().Ticks) {
            throw 'MySQL process identity changed; refusing to stop another process.'
        }
        $ownedProcess.Kill($true)
        if (!$ownedProcess.WaitForExit(15000)) { throw 'Owned MySQL process did not stop within 15 seconds.' }
    }
    for ($attempt=0; $attempt -lt 150; $attempt++) {
        if (!([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 33306)) { break }
        Start-Sleep -Milliseconds 100
    }
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 33306) {
        throw 'Port 33306 is still listening after owned process cleanup; no other process will be stopped.'
    }
    # Retire active ownership proof only after cleanup succeeds; old PIDs may be reused.
    Move-Item -LiteralPath $statePath -Destination (Join-Path $root 'process.stopped.json')
    Write-Host 'Owned 33306 acceptance MySQL stopped.'
    return
}
# Refuse any existing listener before initialization or SQL, even if it uses a familiar schema name.
if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 33306) {
    throw 'Port 33306 is already in use; no existing MySQL will be reused or modified.'
}
if (Test-Path -LiteralPath $root) { throw 'Acceptance MySQL needs a fresh working directory.' }
New-Item -ItemType Directory -Force -Path $root | Out-Null
if ([string]::IsNullOrWhiteSpace($BinaryDirectory)) {
    if ($env:ADMINCORE_MYSQL_BIN) { $BinaryDirectory = $env:ADMINCORE_MYSQL_BIN }
    else {
        $archive = Join-Path $root 'mysql.zip'
        Invoke-WebRequest 'https://cdn.mysql.com/Downloads/MySQL-8.4/mysql-8.4.11-winx64.zip' -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $root
        $BinaryDirectory = Join-Path $root 'mysql-8.4.11-winx64/bin'
    }
}
$server = (Resolve-Path -LiteralPath (Join-Path $BinaryDirectory 'mysqld.exe')).Path
$client = (Resolve-Path -LiteralPath (Join-Path $BinaryDirectory 'mysql.exe')).Path
$base = Split-Path $server -Parent | Split-Path -Parent
& $server --no-defaults --initialize-insecure "--basedir=$base" "--datadir=$data" --console *> (Join-Path $root 'initialize.log')
if ($LASTEXITCODE -ne 0) { throw 'Isolated MySQL initialization failed; see initialize.log.' }
$startInfo = [Diagnostics.ProcessStartInfo]::new($server)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in @('--no-defaults', "--basedir=$base", "--datadir=$data", '--port=33306', '--bind-address=127.0.0.1', '--mysqlx=OFF', "--pid-file=$pidFile", "--log-error=$(Join-Path $root 'mysql.log')")) { $startInfo.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($startInfo)
$state = [ordered]@{ port=33306; dataDirectory=$data; processId=$process.Id; executable=$server; startedAt=$process.StartTime.ToUniversalTime().ToString('o'); children=@() }
$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath
$originalPassword = $env:MYSQL_PWD
try {
    $env:MYSQL_PWD = $null
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) {
            $startupError=$process.StandardError.ReadToEnd()
            $startupError | Set-Content -LiteralPath (Join-Path $root 'startup.err.log')
            throw "Owned MySQL exited before readiness (exit=$($process.ExitCode)); see startup.err.log."
        }
        & $client --no-defaults --protocol=TCP --host=127.0.0.1 --port=33306 --user=root --connect-timeout=2 -e 'SELECT 1' *> $null
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $pidFile)) { $ready=$true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'Isolated MySQL did not become ready.' }
    $actual = & $client --no-defaults --default-character-set=utf8mb4 --protocol=TCP --host=127.0.0.1 --port=33306 --user=root --batch --skip-column-names -e "SELECT JSON_OBJECT('port',@@port,'uuid',@@server_uuid,'directory',@@datadir)"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read bootstrap instance identity.' }
    $identity = $actual | ConvertFrom-Json
    if ($identity.port -ne 33306 -or [IO.Path]::GetFullPath($identity.directory).TrimEnd('\','/') -ne $data.TrimEnd('\','/')) {
        throw 'Listener does not belong to this new data directory; no bootstrap SQL executed.'
    }
    $serverPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
    $childProcess = Get-Process -Id $serverPid
    if ($childProcess.Path -ne $server) { throw 'PID file is not owned by the expected executable.' }
    if ($serverPid -ne $process.Id) { $state.children=@(@{processId=$serverPid;startedAt=$childProcess.StartTime.ToUniversalTime().ToString('o')}) }
    $state['serverUuid']=$identity.uuid
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath
    $rootPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::add-mask::$rootPassword"; Write-Host "::add-mask::$password" }
    "ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword';" | & $client --no-defaults --protocol=TCP --host=127.0.0.1 --port=33306 --user=root
    if ($LASTEXITCODE -ne 0) { throw 'Could not secure bootstrap account.' }
    $env:MYSQL_PWD=$rootPassword
    $schemas=[ordered]@{
        ADMINCORE_T4_MYSQL='admincore_p0_t4_acceptance'
        ADMINCORE_HOST_MYSQL='admincore_host'
        ADMINCORE_HOST_MYSQL_LOG='admincore_log'
        ADMINCORE_ACCEPTANCE_MYSQL='admincore_acceptance'
    }
    $sql=[Collections.Generic.List[string]]::new()
    foreach ($hostName in @('127.0.0.1','localhost')) { $sql.Add("CREATE USER 'admincore_acceptance'@'$hostName' IDENTIFIED BY '$password';") }
    foreach ($schema in $schemas.Values) {
        $sql.Add(('CREATE DATABASE `'+$schema+'` CHARACTER SET utf8mb4;'))
        $grantName=$schema.Replace('_','\_')
        foreach ($hostName in @('127.0.0.1','localhost')) { $sql.Add(('GRANT ALL PRIVILEGES ON `'+$grantName+'`.* TO '+"'admincore_acceptance'@'$hostName';")) }
    }
    ($sql -join "`n") | & $client --no-defaults --protocol=TCP --host=127.0.0.1 --port=33306 --user=root
    if ($LASTEXITCODE -ne 0) { throw 'Could not provision scoped acceptance databases/account.' }
    foreach ($entry in $schemas.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key,"Server=127.0.0.1;Port=33306;Database=$($entry.Value);User ID=admincore_acceptance;Password=$password;SslMode=Required;AllowPublicKeyRetrieval=true",'Process')
    }
    $env:ADMINCORE_ACCEPTANCE_SERVER_UUID=$identity.uuid
    $env:ADMINCORE_ACCEPTANCE_DATA_DIRECTORY=$data
    $env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE=$statePath
    Write-Host 'Owned MySQL ready on 127.0.0.1:33306; identity recorded, four schemas granted to dedicated acceptance account.'
} catch {
    & $PSCommandPath -Action Stop -WorkingDirectory $root
    throw
} finally {
    $env:MYSQL_PWD=$originalPassword
    $rootPassword=$null; $password=$null
}
