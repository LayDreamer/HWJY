#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Worker)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$hostDirectory = Join-Path $root 'src/modules/admin/ZhonTai.Admin.Host'
$project = Join-Path $hostDirectory 'ZhonTai.Admin.Host.csproj'
$environmentNames = @('ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_URLS')
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    if (![string]::IsNullOrWhiteSpace($env:ADMINCORE_CONFIGCENTER_PATH)) {
        throw '日常启动不接受 ADMINCORE_CONFIGCENTER_PATH；请在独立开发终端移除该覆盖后重试。'
    }
    if (!$Worker -and ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq 18010)) {
        throw '18010 已有服务监听。请先确认并停止原后端，再运行本入口；不会自动结束任何进程。'
    }
    $hasMainConnection = ![string]::IsNullOrWhiteSpace($env:DbConfig__ConnectionString)
    $hasLogConnection = ![string]::IsNullOrWhiteSpace($env:DbConfig__Dbs__0__ConnectionString)
    if ($hasMainConnection -ne $hasLogConnection) {
        throw '需要同时提供主库和日志库连接变量；也可在当前终端清空两项后使用交互凭据。'
    }

    Push-Location $hostDirectory
    try {
        & dotnet build $project -c Release -m:1 -nodeReuse:false --tl:off
        if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败，未启动后端。' }

        if (!$hasMainConnection) {
            $mysqlCredential = Get-Credential -Message '输入本机 MySQL 开发账号和密码（仅用于当前终端）'
            if (!$mysqlCredential) { throw '未提供 MySQL 开发凭据，未启动后端。' }
            $mysqlConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
            try {
                $mysqlConnection['Server'] = '127.0.0.1'
                $mysqlConnection['Port'] = 3306
                $mysqlConnection['User ID'] = $mysqlCredential.UserName
                $mysqlConnection['Password'] = $mysqlCredential.GetNetworkCredential().Password
                $mysqlConnection['Character Set'] = 'utf8mb4'
                $mysqlConnection['Database'] = 'admincore_host'
                $env:DbConfig__ConnectionString = $mysqlConnection.ConnectionString
                $mysqlConnection['Database'] = 'admincore_host_log'
                $env:DbConfig__Dbs__0__ConnectionString = $mysqlConnection.ConnectionString
            } finally {
                $mysqlConnection.Clear()
                Remove-Variable mysqlCredential, mysqlConnection
            }
        }

        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:DOTNET_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = 'http://localhost:18010'
        Write-Host '当前工程 Release 构建已完成，使用 Development 配置启动。Ctrl+C 停止后可在同一终端再次运行。'
        $arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-launch-profile')
        if ($Worker) { $arguments += @('--', '--worker') }
        & dotnet @arguments
        exit $LASTEXITCODE
    } finally { Pop-Location }
} catch {
    [Console]::Error.WriteLine('开发后端启动失败：' + $_.Exception.Message)
    exit 1
} finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
