[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$required = @('ADMINCORE_T4_MYSQL', 'ADMINCORE_HOST_MYSQL', 'ADMINCORE_HOST_MYSQL_LOG')
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    Write-Error ('P0 Gate BLOCKED: missing required environment variables: ' + ($missing -join ', '))
    exit 2
}

$project = Join-Path $PSScriptRoot '..\src\modules\admin\ZhonTai.Admin.Tests\ZhonTai.Admin.Tests.csproj'
$output = & dotnet test $project -c Release --no-build --no-restore --filter 'P0Gate=Required' '--logger:console;verbosity=normal' 2>&1 | Tee-Object -Variable testOutput
$exitCode = $LASTEXITCODE
$summary = @($testOutput | Select-String -Pattern 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)' | Select-Object -Last 1)

if ($summary.Count -ne 1) {
    Write-Error 'P0 Gate FAILED: test summary was not produced.'
    exit 1
}

$match = [regex]::Match($summary[0].ToString(), 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+)')
$failed = [int]$match.Groups[1].Value
$passed = [int]$match.Groups[2].Value
$skipped = [int]$match.Groups[3].Value
Write-Host "P0 Gate totals: Passed=$passed Failed=$failed Skipped=$skipped"

if ($exitCode -ne 0 -or $failed -ne 0 -or $skipped -ne 0 -or $passed -eq 0) {
    Write-Error 'P0 Gate FAILED: every required test must execute and pass.'
    exit 1
}

exit 0
