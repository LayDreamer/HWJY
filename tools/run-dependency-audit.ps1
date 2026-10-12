[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root 'artifacts/quality'
New-Item -ItemType Directory -Force -Path $results | Out-Null
Push-Location $root
try {
    $nugetText = & dotnet list src/ZhonTai.sln package --vulnerable --include-transitive --format json --no-restore 2> (Join-Path $results 'nuget-audit.err.log') | Out-String
    $nugetExit = $LASTEXITCODE
    Set-Content -LiteralPath (Join-Path $results 'nuget-audit.json') -Value $nugetText -Encoding utf8
    Push-Location (Join-Path $root 'ui/zhontai.ui.admin.vue3')
    try {
        $npmText = & pnpm audit --json --audit-level=high --registry=https://registry.npmjs.org 2> (Join-Path $results 'npm-audit.err.log') | Out-String
        $npmExit = $LASTEXITCODE
        Set-Content -LiteralPath (Join-Path $results 'npm-audit.json') -Value $npmText -Encoding utf8
    } finally { Pop-Location }
    $nuget = $nugetText | ConvertFrom-Json
    $npm = $npmText | ConvertFrom-Json
    if ($nugetExit -ne 0 -or $nuget.problems -or @($nuget.projects).Count -eq 0 -or $npm.error -or !$npm.metadata.vulnerabilities) {
        throw 'Dependency audit could not obtain complete reports; see artifacts/quality.'
    }
    $blockedPackages = @($nuget.projects | ForEach-Object {
        foreach ($framework in $_.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if (@($package.vulnerabilities | Where-Object severity -in @('High', 'Critical')).Count -gt 0) { $package.id }
            }
        }
    } | Sort-Object -Unique)
    if ($blockedPackages.Count -gt 0 -or $npmExit -ne 0 -or [int]$npm.metadata.vulnerabilities.high -gt 0 -or [int]$npm.metadata.vulnerabilities.critical -gt 0) {
        throw "High/critical dependencies remain. NuGet packages: $($blockedPackages -join ', '); npm high=$($npm.metadata.vulnerabilities.high), critical=$($npm.metadata.vulnerabilities.critical)."
    }
    Write-Host "Dependency audit PASSED: no high/critical findings. npm moderate=$($npm.metadata.vulnerabilities.moderate), low=$($npm.metadata.vulnerabilities.low)."
} catch {
    [Console]::Error.WriteLine('Dependency audit FAILED: ' + $_.Exception.Message)
    exit 1
} finally { Pop-Location }
