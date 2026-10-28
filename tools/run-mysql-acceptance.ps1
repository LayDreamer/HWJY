[CmdletBinding()]
param([switch]$NoRestore, [string]$MySqlBinaryDirectory, [string]$MySqlWorkingDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$variables = @('ADMINCORE_HOST_MYSQL','ADMINCORE_HOST_MYSQL_LOG','ADMINCORE_T4_MYSQL','ADMINCORE_ACCEPTANCE_MYSQL',
    'ADMINCORE_ACCEPTANCE_SERVER_UUID','ADMINCORE_ACCEPTANCE_DATA_DIRECTORY','ADMINCORE_ACCEPTANCE_INSTANCE_STATE',
    'ADMINCORE_CONFIGCENTER_PATH','DbConfig__ConnectionString','DbConfig__Dbs__0__ConnectionString','MYSQL_PWD',
    'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')
$original = @{}
foreach ($name in $variables) {
    $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')
}
$result = 1
$statePath = $null
try {
    $start = @{Action='Start'}
    if ($MySqlBinaryDirectory) { $start.BinaryDirectory=$MySqlBinaryDirectory }
    if ($MySqlWorkingDirectory) { $start.WorkingDirectory=$MySqlWorkingDirectory }
    & (Join-Path $PSScriptRoot 'ci-mysql.ps1') @start
    $statePath = $env:ADMINCORE_ACCEPTANCE_INSTANCE_STATE
    if (!$statePath) { throw 'Managed instance proof was not created.' }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run-p0-gate.ps1') -Mode Full -ManagedMySql -NoRestore:$NoRestore
    $result=$LASTEXITCODE
} catch {
    [Console]::Error.WriteLine('Isolated acceptance failed: '+$_.Exception.Message)
    $result=1
} finally {
    try {
        if ($statePath) { & (Join-Path $PSScriptRoot 'ci-mysql.ps1') -Action Stop -WorkingDirectory (Split-Path $statePath -Parent) }
    } catch { [Console]::Error.WriteLine('Owned acceptance instance cleanup failed: '+$_.Exception.Message); $result=1 }
    foreach ($name in $variables) {
        if ($null -eq $original[$name]) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') }
        else { [Environment]::SetEnvironmentVariable($name,$original[$name],'Process') }
    }
}
exit $result
