[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop')][string]$Action = 'Start',
    [string]$WorkingDirectory = (Join-Path $env:RUNNER_TEMP 'admincore-ci-mysql'),
    [string]$BinaryDirectory,
    [ValidateRange(1024, 65535)][int]$Port = 33306
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'This acceptance environment requires Windows.' }
$root = [IO.Path]::GetFullPath($WorkingDirectory)
$statePath = Join-Path $root 'process.json'
if ($Action -eq 'Stop') {
    if (!(Test-Path -LiteralPath $statePath)) { return }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $process = Get-Process -Id $state.processId -ErrorAction SilentlyContinue
    if ($process) {
        if ($process.Path -ne $state.executable -or $process.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$state.startedAt).ToUniversalTime().Ticks) {
            throw 'MySQL process identity changed; refusing to stop another process.'
        }
        # MySQL on Windows has a supervising process and a server child.
        # Stopping only the supervisor leaves the server holding the port.
        $process.Kill($true)
        $process.WaitForExit(15000) | Out-Null
    }
    foreach ($child in @($state.children | Where-Object { $_ })) {
        $childProcess = Get-Process -Id $child.processId -ErrorAction SilentlyContinue
        if ($childProcess -and $childProcess.Path -eq $state.executable -and
            $childProcess.StartTime.ToUniversalTime().Ticks -eq ([DateTime]$child.startedAt).ToUniversalTime().Ticks) {
            $childProcess.Kill($true)
            $childProcess.WaitForExit(15000) | Out-Null
        }
    }
    Write-Host 'Isolated CI MySQL stopped.'
    return
}
if (Test-Path -LiteralPath $root) { throw 'CI MySQL needs a fresh working directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
if ([string]::IsNullOrWhiteSpace($BinaryDirectory)) {
    $archive = Join-Path $root 'mysql.zip'
    Invoke-WebRequest 'https://cdn.mysql.com/Downloads/MySQL-8.4/mysql-8.4.11-winx64.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $root
    $BinaryDirectory = Join-Path $root 'mysql-8.4.11-winx64/bin'
}
$server = (Resolve-Path -LiteralPath (Join-Path $BinaryDirectory 'mysqld.exe')).Path
$client = (Resolve-Path -LiteralPath (Join-Path $BinaryDirectory 'mysql.exe')).Path
$data = Join-Path $root 'data'
$base = Split-Path $server -Parent | Split-Path -Parent
& $server --no-defaults --initialize-insecure "--basedir=$base" "--datadir=$data" --console *> (Join-Path $root 'initialize.log')
if ($LASTEXITCODE -ne 0) { throw 'Isolated MySQL initialization failed; see initialize.log.' }
$startInfo = [Diagnostics.ProcessStartInfo]::new($server)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($argument in @('--no-defaults', "--basedir=$base", "--datadir=$data", "--port=$Port", '--bind-address=127.0.0.1', '--mysqlx=OFF', "--log-error=$(Join-Path $root 'mysql.log')")) { $startInfo.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($startInfo)
@{ processId = $process.Id; executable = $server; startedAt = $process.StartTime.ToUniversalTime().ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statePath
$originalPassword = $env:MYSQL_PWD
try {
    $env:MYSQL_PWD = $null
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "Isolated MySQL exited during startup (exit=$($process.ExitCode)); see mysql.log." }
        & $client --no-defaults --host=127.0.0.1 "--port=$Port" --user=root --connect-timeout=2 --execute='SELECT 1' *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'Isolated MySQL did not become ready.' }
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" | Where-Object ExecutablePath -eq $server | ForEach-Object {
        $childProcess = Get-Process -Id $_.ProcessId
        @{ processId = $childProcess.Id; startedAt = $childProcess.StartTime.ToUniversalTime().ToString('o') }
    })
    @{ processId = $process.Id; executable = $server; startedAt = $process.StartTime.ToUniversalTime().ToString('o'); children = $children } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath
    $password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::add-mask::$password" }
    "ALTER USER 'root'@'localhost' IDENTIFIED BY '$password';" | & $client --no-defaults --host=127.0.0.1 "--port=$Port" --user=root
    if ($LASTEXITCODE -ne 0) { throw 'Could not secure isolated MySQL account.' }
    $env:MYSQL_PWD = $password
    $schemas = [ordered]@{
        ADMINCORE_T4_MYSQL = 'admincore_p0_t4_acceptance'
        ADMINCORE_HOST_MYSQL = 'admincore_host'
        ADMINCORE_HOST_MYSQL_LOG = 'admincore_log'
        ADMINCORE_ACCEPTANCE_MYSQL = 'admincore_acceptance'
    }
    ($schemas.Values | ForEach-Object { "CREATE DATABASE $_ CHARACTER SET utf8mb4;" }) | & $client --no-defaults --host=127.0.0.1 "--port=$Port" --user=root
    if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated acceptance schemas.' }
    foreach ($entry in $schemas.GetEnumerator()) {
        $connection = "Server=127.0.0.1;Port=$Port;Database=$($entry.Value);User ID=root;Password=$password;SslMode=Required;AllowPublicKeyRetrieval=true"
        [Environment]::SetEnvironmentVariable($entry.Key, $connection, 'Process')
        if ($env:GITHUB_ACTIONS -eq 'true') { "$($entry.Key)=$connection" | Add-Content -LiteralPath $env:GITHUB_ENV }
    }
    Write-Host 'Isolated Windows MySQL 8.4 ready; four acceptance schemas created.'
} catch {
    & $PSCommandPath -Action Stop -WorkingDirectory $root
    throw
} finally {
    $env:MYSQL_PWD = $originalPassword
    $password = $null
}
