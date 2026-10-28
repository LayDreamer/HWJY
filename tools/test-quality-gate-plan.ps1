$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'quality-gate-plan.ps1') -LibraryOnly

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$temporaryName = 'hwjy-gate-plan-' + [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $temporaryParent $temporaryName))
$repositoryRoot = Join-Path $temporaryRoot 'repository'
$hooksRoot = Join-Path $temporaryRoot 'empty-hooks'
$repository = 'example/hwjy'
$script:caseCount = 0
$script:branchCount = 0
$script:realQualityGateGit = (Get-Command Invoke-QualityGateGit).ScriptBlock
$script:immutableGitEvidence = @{}
$script:gitEvidenceReads = 0
$script:gitEvidenceReuses = 0
$elapsed = [Diagnostics.Stopwatch]::StartNew()

function Invoke-QualityGateGit {
    param([string]$RepositoryRoot, [string[]]$Arguments)
    # Reuse only actual read results for fully specified, immutable commits. Every new
    # diff/graph is read by real Git first; fixture writes and CLI children never use this cache.
    $sha = '(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})'
    $signature = $Arguments -join ' '
    $immutable = $signature -cmatch ("\A(?:rev-parse --verify $sha\^\{commit\}|rev-parse $sha\^\{tree\}|merge-base (?:--is-ancestor )?$sha $sha|diff --name-only --no-renames -z $sha $sha --|rev-list --parents -n 1 $sha)\z")
    $key = $RepositoryRoot + [char]0 + ($Arguments -join [char]0)
    if ($immutable -and $script:immutableGitEvidence.ContainsKey($key)) {
        $script:gitEvidenceReuses++
        return $script:immutableGitEvidence[$key]
    }
    $script:gitEvidenceReads++
    $result = & $script:realQualityGateGit -RepositoryRoot $RepositoryRoot -Arguments $Arguments
    if ($immutable) { $script:immutableGitEvidence[$key] = $result }
    return $result
}

function Invoke-TestGit([string[]]$GitArguments) {
    $output = @(& git -C $repositoryRoot -c core.autocrlf=false -c core.quotePath=false `
        -c "core.hooksPath=$hooksRoot" -c commit.gpgsign=false `
        -c user.name=GatePlanRegression -c user.email=gate-plan@example.invalid @GitArguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed ($($GitArguments -join ' ')): $($output -join ' ')" }
    return ($output -join "`n").Trim()
}

function Write-TestFile([string]$RelativePath, [string]$Content = "fixture`n") {
    $path = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $RelativePath))
    if (!$path.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture path escaped its repository.'
    }
    [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
}

function New-TestCommit([string]$Message = 'gate fixture') {
    Invoke-TestGit @('add', '--all') | Out-Null
    Invoke-TestGit @('commit', '--allow-empty', '-m', $Message) | Out-Null
    return Invoke-TestGit @('rev-parse', 'HEAD')
}

function New-TestBranch([string]$Start) {
    $script:branchCount++
    Invoke-TestGit @('switch', '-c', "case-$script:branchCount", $Start) | Out-Null
}

function New-PullRequestEvent([string]$Base, [string]$Head) {
    return [pscustomobject]@{
        number = 17
        pull_request = [pscustomobject]@{
            number = 17
            base = [pscustomobject]@{ sha = $Base; ref = 'main'; repo = [pscustomobject]@{ full_name = $repository } }
            head = [pscustomobject]@{ sha = $Head; repo = [pscustomobject]@{ full_name = $repository } }
        }
    }
}

function New-PushEvent([string]$Before, [string]$After) {
    return [pscustomobject]@{ before = $Before; after = $After; ref = 'refs/heads/main'; deleted = $false; forced = $false }
}

function Assert-Plan {
    param(
        [string]$Name,
        [string]$Expected,
        [string]$EventName,
        [object]$Event,
        [Nullable[bool]]$ExpectedRouting,
        [scriptblock]$GitHubApi = { param($Path) throw "No API fixture configured for $Path" }
    )
    $plan = Get-QualityGatePlan -EventName $EventName -Event $Event -RepositoryRoot $repositoryRoot `
        -Repository $repository -GitHubApi $GitHubApi
    if ($null -eq $plan -or $plan.Mode -ne $Expected) {
        throw "$Name expected '$Expected', got: $($plan | ConvertTo-Json -Depth 5 -Compress)"
    }
    if ([string]::IsNullOrWhiteSpace($plan.Reason)) { throw "$Name did not explain its decision." }
    if ($Expected -eq 'reuse' -and [string]::IsNullOrWhiteSpace([string]$plan.SourceRun)) {
        throw "$Name did not identify the reused run."
    }
    if ($null -ne $ExpectedRouting -and $plan.RoutingTests -ne $ExpectedRouting) {
        throw "$Name reported RoutingTests=$($plan.RoutingTests), expected $ExpectedRouting."
    }
    if ($Expected -in @('docs', 'reuse') -and $plan.RoutingTests -ne $false) {
        throw "$Name must not trigger routing regressions in a documentation or reuse job."
    }
    $script:caseCount++
    Write-Output "PASS: $Name ($Expected)"
}

function New-ApiFixture([string]$Before, [string]$Head, [string]$After) {
    $treeResult = Invoke-QualityGateGit -RepositoryRoot $repositoryRoot -Arguments @('rev-parse', "$After`^{tree}")
    if ($treeResult.ExitCode -ne 0) { throw 'Fixture merge tree is unavailable.' }
    $tree = $treeResult.Output.Trim()
    $pullRequest = [pscustomobject]@{
        number = 17; merged = $true; state = 'closed'; merge_commit_sha = $After
        base = [pscustomobject]@{ sha = $Before; ref = 'main'; repo = [pscustomobject]@{ full_name = $repository } }
        head = [pscustomobject]@{ sha = $Head; repo = [pscustomobject]@{ full_name = $repository } }
    }
    $run = [pscustomobject]@{
        id = 501; created_at = [DateTime]::UtcNow.AddMinutes(-5).ToString('o')
        updated_at = [DateTime]::UtcNow.AddMinutes(-1).ToString('o'); status = 'completed'; conclusion = 'success'
        head_sha = $Head; event = 'pull_request'; path = '.github/workflows/quality-gate.yml'
        repository = [pscustomobject]@{ full_name = $repository }
        head_repository = [pscustomobject]@{ full_name = $repository }
        html_url = 'https://github.com/example/hwjy/actions/runs/501'
        pull_requests = @([pscustomobject]@{
            number = 17
            base = [pscustomobject]@{ sha = $Before; ref = 'main' }
            head = [pscustomobject]@{ sha = $Head }
        })
    }
    $job = [pscustomobject]@{
        id = 601; name = 'Full Quality Gate'; status = 'completed'; conclusion = 'success'
        steps = @(
            [pscustomobject]@{ name = 'Run full quality gate'; status = 'completed'; conclusion = 'success' },
            [pscustomobject]@{ name = 'Audit high and critical dependencies'; status = 'completed'; conclusion = 'success' },
            [pscustomobject]@{ name = "Verified source tree: $tree"; status = 'completed'; conclusion = 'success' }
        )
    }
    return @{
        Pulls = @($pullRequest)
        Runs = [pscustomobject]@{ total_count = 1; workflow_runs = @($run) }
        Jobs = [pscustomobject]@{ total_count = 1; jobs = @($job) }
        Calls = [Collections.Generic.List[string]]::new()
        Failure = ''
    }
}

function New-TestApi([hashtable]$Fixture) {
    return {
        param([string]$Path)
        $Fixture.Calls.Add($Path)
        if ($Fixture.Failure) { throw $Fixture.Failure }
        if ($Path -match '^/commits/[0-9a-f]+/pulls\?per_page=100$') { return $Fixture.Pulls }
        if ($Path -match '^/actions/workflows/quality-gate\.yml/runs\?event=pull_request&head_sha=[0-9a-f]+&per_page=100$') { return $Fixture.Runs }
        if ($Path -match '^/actions/runs/501/jobs\?filter=latest&per_page=100$') { return $Fixture.Jobs }
        throw "Unexpected API fixture route: $Path"
    }.GetNewClosure()
}

function Assert-PlanEntryPoint {
    param([string]$Name, [string]$EventName, [object]$Event, [string]$ExpectedMode, [bool]$ExpectSuccess = $true, [switch]$MalformedJson)
    $script:branchCount++
    $caseRoot = Join-Path $temporaryRoot "entry-$script:branchCount"
    [void][IO.Directory]::CreateDirectory($caseRoot)
    $eventPath = Join-Path $caseRoot 'event.json'
    $outputPath = Join-Path $caseRoot 'github-output.txt'
    $summaryPath = Join-Path $caseRoot 'summary.md'
    $json = if ($MalformedJson) { '{invalid json' } else { $Event | ConvertTo-Json -Depth 10 }
    [IO.File]::WriteAllText($eventPath, $json, [Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $repositoryRoot
    foreach ($argument in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'quality-gate-plan.ps1'),
        '-EventName', $EventName, '-EventPath', $eventPath, '-RepositoryRoot', $repositoryRoot)) {
        $start.ArgumentList.Add($argument)
    }
    # Only the child receives synthetic GitHub state. No caller environment or credentials are read or changed.
    $start.Environment['GITHUB_OUTPUT'] = $outputPath
    $start.Environment['GITHUB_STEP_SUMMARY'] = $summaryPath
    $start.Environment['GITHUB_REPOSITORY'] = $repository
    [void]$start.Environment.Remove('GH_TOKEN')
    [void]$start.Environment.Remove('GITHUB_TOKEN')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Regression child could not start.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) {
            $process.Kill($true)
            throw 'Regression entry-point child timed out.'
        }
        $console = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if (($process.ExitCode -eq 0) -ne $ExpectSuccess) { throw "$Name returned unexpected exit code $($process.ExitCode): $console" }
    } finally { $process.Dispose() }
    $output = [IO.File]::ReadAllText($outputPath)
    if ($output -notmatch "(?m)^mode=$ExpectedMode\r?$") { throw "$Name wrote unexpected GitHub output: $output" }
    $expectedRouting = if ($ExpectedMode -eq 'docs') { 'false' } else { 'true' }
    if ($output -notmatch "(?m)^routing_tests=$expectedRouting\r?$") { throw "$Name wrote unexpected routing test output: $output" }
    $summary = if (Test-Path -LiteralPath $summaryPath) { [IO.File]::ReadAllText($summaryPath) } else { '' }
    if ($ExpectedMode -eq 'docs' -and $ExpectSuccess) {
        if (!$summary.Contains('Documentation diff checks passed.') -or !$summary.Contains('were not run')) {
            throw "$Name did not accurately report documentation-only verification."
        }
    } elseif ($ExpectSuccess) {
        if (!$summary.Contains('Full verification is required.') -or $summary.Contains('checks passed')) {
            throw "$Name did not accurately report that full verification remains required."
        }
    } elseif ($summary.Contains('checks passed')) { throw "$Name reported success after the documentation check failed." }
    $script:caseCount++
    Write-Output "PASS: $Name (entry point, $ExpectedMode)"
}

try {
    if (Test-Path -LiteralPath $temporaryRoot) { throw 'A fresh regression directory is required.' }
    [void][IO.Directory]::CreateDirectory($repositoryRoot)
    [void][IO.Directory]::CreateDirectory($hooksRoot)
    Invoke-TestGit @('init', '--object-format=sha1', '-b', 'main') | Out-Null
    Write-TestFile 'README.md'
    Write-TestFile 'docs/existing.md'
    Write-TestFile 'src/Auth.cs' "class Auth {}`n"
    Write-TestFile 'database/001.sql' "select 1;`n"
    $base = New-TestCommit 'baseline'

    $documentationPaths = @(
        'README.md', 'README.en.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md',
        'docs/guide.md', 'docs/nested/setup.md', 'docs/架构 图.md',
        'ui/admin/README.md', 'ui/admin/mobile/README.en.md', 'tools/QUALITY-GATE.md'
    )
    foreach ($path in $documentationPaths) {
        New-TestBranch $base
        Write-TestFile $path "changed documentation`n"
        $head = New-TestCommit
        Assert-Plan -Name "documentation: $path" -Expected docs -EventName pull_request -Event (New-PullRequestEvent $base $head)
    }

    $codePaths = @(
        'src/Auth.cs', 'src/Permissions.cs', 'database/002.sql', 'tools/run-quality-gate.ps1',
        'tools/quality-gate-plan.ps1', 'tools/test-quality-gate-plan.ps1',
        '.github/workflows/quality-gate.yml', 'config.json', 'pnpm-lock.yaml',
        'AGENTS.md', 'SKILL.md', 'docs/AGENTS.md', 'docs/Agents.md', 'docs/nested/skill.md',
        'ui/admin/SKILL.md', '.github/README.md', 'other/notes.md', 'notes.txt'
    )
    foreach ($path in $codePaths) {
        New-TestBranch $base
        Write-TestFile $path "critical or unclassified change`n"
        $head = New-TestCommit
        $routing = $path -in @('.github/workflows/quality-gate.yml', 'tools/quality-gate-plan.ps1', 'tools/test-quality-gate-plan.ps1')
        Assert-Plan -Name "full coverage: $path" -Expected full -EventName pull_request -Event (New-PullRequestEvent $base $head) -ExpectedRouting $routing
    }

    New-TestBranch $base
    Write-TestFile 'README.md' "documentation and code`n"
    Write-TestFile 'src/Auth.cs' "class UpdatedAuth {}`n"
    $head = New-TestCommit
    Assert-Plan -Name 'mixed documentation and authentication code' -Expected full -EventName pull_request -Event (New-PullRequestEvent $base $head)

    foreach ($path in @('README.md', 'docs/existing.md', 'src/Auth.cs', 'database/001.sql')) {
        New-TestBranch $base
        Invoke-TestGit @('rm', '--', $path) | Out-Null
        $head = New-TestCommit
        $expected = if ($path -like '*.md') { 'docs' } else { 'full' }
        Assert-Plan -Name "deletion: $path" -Expected $expected -EventName pull_request -Event (New-PullRequestEvent $base $head)
    }

    foreach ($rename in @(
        @('docs/existing.md', 'docs/renamed guide.md', 'docs'),
        @('src/Auth.cs', 'docs/Auth.md', 'full'),
        @('docs/existing.md', 'src/FormerDoc.cs', 'full')
    )) {
        New-TestBranch $base
        Invoke-TestGit @('mv', '--', $rename[0], $rename[1]) | Out-Null
        $head = New-TestCommit
        Assert-Plan -Name "rename: $($rename[0]) -> $($rename[1])" -Expected $rename[2] -EventName pull_request -Event (New-PullRequestEvent $base $head)
    }

    Assert-Plan -Name 'empty pull request diff' -Expected full -EventName pull_request -Event (New-PullRequestEvent $base $base)
    Assert-Plan -Name 'missing base commit' -Expected full -EventName pull_request -Event (New-PullRequestEvent ('f' * 40) $base)
    Assert-Plan -Name 'manual dispatch always executes full' -Expected full -EventName workflow_dispatch -Event ([pscustomobject]@{}) -ExpectedRouting $true
    Assert-Plan -Name 'unknown event executes full' -Expected full -EventName unexpected_event -Event ([pscustomobject]@{})
    Assert-Plan -Name 'malformed PR event executes full' -Expected full -EventName pull_request -Event ([pscustomobject]@{})

    New-TestBranch $base
    Write-TestFile 'docs/push.md'
    $docsHead = New-TestCommit
    Assert-Plan -Name 'documentation-only direct push' -Expected docs -EventName push -Event (New-PushEvent $base $docsHead)

    New-TestBranch $base
    Write-TestFile 'src/Auth.cs' "class NewAuth {}`n"
    $featureHead = New-TestCommit 'Merge pull request #17 from fake/title-only'
    Assert-Plan -Name 'merge-like commit title is not evidence' -Expected full -EventName push -Event (New-PushEvent $base $featureHead)

    New-TestBranch $base
    Invoke-TestGit @('merge', '--no-ff', $featureHead, '-m', 'real merge') | Out-Null
    $merge = Invoke-TestGit @('rev-parse', 'HEAD')
    $push = New-PushEvent $base $merge
    $fixture = New-ApiFixture $base $featureHead $merge
    Assert-Plan -Name 'matching successful PR full gate is reused' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)
    if ($fixture.Calls.Count -ne 3) { throw 'Successful reuse must query PR, workflow run and job evidence.' }

    $negativeFixtures = [ordered]@{
        'no associated PR' = { param($f) $f.Pulls = @() }
        'invalid associated PR number' = { param($f) $f.Pulls[0].number = 'not-a-number' }
        'PR not merged' = { param($f) $f.Pulls[0].merged = $false }
        'foreign head repository' = { param($f) $f.Pulls[0].head.repo.full_name = 'foreign/hwjy' }
        'foreign base repository' = { param($f) $f.Pulls[0].base.repo.full_name = 'foreign/hwjy' }
        'wrong merge commit' = { param($f) $f.Pulls[0].merge_commit_sha = 'a' * 40 }
        'wrong PR head' = { param($f) $f.Pulls[0].head.sha = 'a' * 40 }
        'wrong PR base' = { param($f) $f.Pulls[0].base.sha = 'a' * 40 }
        'different target branch' = { param($f) $f.Pulls[0].base.ref = 'release' }
        'incomplete associated PR page' = { param($f) $f.Pulls = @($f.Pulls[0]) * 100 }
        'no workflow run' = { param($f) $f.Runs.total_count = 0; $f.Runs.workflow_runs = @() }
        'incomplete workflow run page' = { param($f) $f.Runs.total_count = 2 }
        'foreign workflow' = { param($f) $f.Runs.workflow_runs[0].path = '.github/workflows/other.yml' }
        'foreign run repository' = { param($f) $f.Runs.workflow_runs[0].repository.full_name = 'foreign/hwjy' }
        'foreign run head repository' = { param($f) $f.Runs.workflow_runs[0].head_repository.full_name = 'foreign/hwjy' }
        'non-PR workflow event' = { param($f) $f.Runs.workflow_runs[0].event = 'workflow_dispatch' }
        'different run head' = { param($f) $f.Runs.workflow_runs[0].head_sha = 'a' * 40 }
        'wrong PR in run' = { param($f) $f.Runs.workflow_runs[0].pull_requests[0].number = 99 }
        'base advanced after old check' = { param($f) $f.Runs.workflow_runs[0].pull_requests[0].base.sha = 'a' * 40 }
        'wrong run PR head' = { param($f) $f.Runs.workflow_runs[0].pull_requests[0].head.sha = 'a' * 40 }
        'invalid run identity' = { param($f) $f.Runs.workflow_runs[0].id = 0 }
        'missing completion freshness timestamp' = { param($f) $f.Runs.workflow_runs[0].PSObject.Properties.Remove('updated_at') }
        'invalid completion freshness timestamp' = { param($f) $f.Runs.workflow_runs[0].updated_at = 'not-a-date' }
        'expired evidence cannot skip a fresh dependency audit' = { param($f) $f.Runs.workflow_runs[0].updated_at = [DateTime]::UtcNow.AddHours(-25).ToString('o') }
        'future evidence timestamp is rejected' = { param($f) $f.Runs.workflow_runs[0].updated_at = [DateTime]::UtcNow.AddMinutes(6).ToString('o') }
        'non-UTC evidence timestamp is rejected' = { param($f) $f.Runs.workflow_runs[0].updated_at = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(1)).ToString('o') }
        'unspecified DateTime cannot claim UTC freshness' = { param($f) $f.Runs.workflow_runs[0].updated_at = [DateTime]::SpecifyKind([DateTime]::UtcNow, [DateTimeKind]::Unspecified) }
        'local DateTime cannot claim UTC freshness' = { param($f) $f.Runs.workflow_runs[0].updated_at = [DateTime]::SpecifyKind([DateTime]::UtcNow, [DateTimeKind]::Local) }
        'latest workflow failed' = { param($f) $f.Runs.workflow_runs[0].conclusion = 'failure' }
        'latest workflow still running' = { param($f) $f.Runs.workflow_runs[0].status = 'in_progress'; $f.Runs.workflow_runs[0].conclusion = $null }
        'workflow skipped' = { param($f) $f.Runs.workflow_runs[0].conclusion = 'skipped' }
        'no matching full gate job' = { param($f) $f.Jobs.jobs[0].name = 'Documentation checks' }
        'failed job despite successful workflow fixture' = { param($f) $f.Jobs.jobs[0].conclusion = 'failure' }
        'gate job still running' = { param($f) $f.Jobs.jobs[0].status = 'in_progress' }
        'incomplete jobs page' = { param($f) $f.Jobs.total_count = 2 }
        'green docs-only job' = { param($f) $f.Jobs.jobs[0].steps[0].conclusion = 'skipped' }
        'audit skipped' = { param($f) $f.Jobs.jobs[0].steps[1].conclusion = 'skipped' }
        'audit failed' = { param($f) $f.Jobs.jobs[0].steps[1].conclusion = 'failure' }
        'legacy success without tree marker' = { param($f) $f.Jobs.jobs[0].steps = @($f.Jobs.jobs[0].steps | Where-Object name -NotLike 'Verified source tree:*') }
        'wrong verified source tree' = { param($f) $f.Jobs.jobs[0].steps[2].name = 'Verified source tree: ' + ('a' * 40) }
        'tree marker skipped' = { param($f) $f.Jobs.jobs[0].steps[2].conclusion = 'skipped' }
        'tree marker still running' = { param($f) $f.Jobs.jobs[0].steps[2].status = 'in_progress' }
        'duplicate tree marker' = { param($f) $f.Jobs.jobs[0].steps += $f.Jobs.jobs[0].steps[2] }
        'wrong and correct tree markers together' = { param($f) $f.Jobs.jobs[0].steps += [pscustomobject]@{ name = 'Verified source tree: ' + ('a' * 40); status = 'completed'; conclusion = 'success' } }
        'empty associations with wrong tree marker' = { param($f) $f.Runs.workflow_runs[0].pull_requests = @(); $f.Jobs.jobs[0].steps[2].name = 'Verified source tree: ' + ('a' * 40) }
        'API permission denied' = { param($f) $f.Failure = 'HTTP 403' }
        'API timeout' = { param($f) $f.Failure = 'Controlled request timeout' }
    }
    foreach ($item in $negativeFixtures.GetEnumerator()) {
        $fixture = New-ApiFixture $base $featureHead $merge
        & $item.Value $fixture
        Assert-Plan -Name "reuse fallback: $($item.Key)" -Expected full -EventName push -Event $push -GitHubApi (New-TestApi $fixture)
    }

    $fixture = New-ApiFixture $base $featureHead $merge
    $latest = $fixture.Runs.workflow_runs[0] | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $latest.id = 502
    $latest.created_at = [DateTime]::UtcNow.AddMinutes(-2).ToString('o')
    $latest.conclusion = 'failure'
    $fixture.Runs.workflow_runs += $latest
    $fixture.Runs.total_count = 2
    Assert-Plan -Name 'old successful run cannot mask a newer failed run' -Expected full -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $fixture = New-ApiFixture $base $featureHead $merge
    $fixture.Runs.workflow_runs[0].path += '@refs/pull/17/merge'
    Assert-Plan -Name 'workflow path with explicit ref is accepted' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $fixture = New-ApiFixture $base $featureHead $merge
    $fixture.Runs.workflow_runs[0].pull_requests = @()
    Assert-Plan -Name 'real merged-run empty associations reuse only with verified tree marker' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $fixture = New-ApiFixture $base $featureHead $merge
    $fixture.Pulls[0].PSObject.Properties.Remove('merged')
    $fixture.Pulls[0] | Add-Member -NotePropertyName merged_at -NotePropertyValue '2026-10-28T06:00:00Z'
    Assert-Plan -Name 'raw associated-PR API merged_at shape is accepted' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $fixture = New-ApiFixture $base $featureHead $merge
    # GitHub returns a Z suffix. An explicit +00:00 is deserialized as Local by
    # PowerShell on some hosts and is covered by the conservative rejection case.
    $jsonTimestamp = '{"updated_at":"' + [DateTime]::UtcNow.ToString('o') + '"}'
    $fixture.Runs.workflow_runs[0].updated_at = ($jsonTimestamp | ConvertFrom-Json).updated_at
    Assert-Plan -Name 'JSON-deserialized UTC DateTime evidence is accepted' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $fixture = New-ApiFixture $base $featureHead $merge
    $fixture.Runs.workflow_runs[0].updated_at = [DateTimeOffset]::UtcNow
    Assert-Plan -Name 'UTC DateTimeOffset evidence is accepted' -Expected reuse -EventName push -Event $push -GitHubApi (New-TestApi $fixture)

    $badPush = New-PushEvent ('0' * 40) $merge
    Assert-Plan -Name 'new branch push has no trusted before commit' -Expected full -EventName push -Event $badPush
    $badPush = New-PushEvent $base $merge
    $badPush.deleted = $true
    Assert-Plan -Name 'deleted ref cannot reuse a gate' -Expected full -EventName push -Event $badPush
    $badPush = New-PushEvent $base $merge
    $badPush.ref = 'refs/heads/feature'
    Assert-Plan -Name 'unconfigured branch cannot reuse a main gate' -Expected full -EventName push -Event $badPush

    # A real merge of diverged histories produces an untested combined tree.
    New-TestBranch $base
    Write-TestFile 'src/Other.cs' "class BaseAdvanced {}`n"
    $advancedBase = New-TestCommit
    Invoke-TestGit @('merge', '--no-ff', $featureHead, '-m', 'merge after target advanced') | Out-Null
    $combinedMerge = Invoke-TestGit @('rev-parse', 'HEAD')
    $fixture = New-ApiFixture $advancedBase $featureHead $combinedMerge
    Assert-Plan -Name 'diverged target creates a different merge tree' -Expected full -EventName push `
        -Event (New-PushEvent $advancedBase $combinedMerge) -GitHubApi (New-TestApi $fixture)

    Assert-PlanEntryPoint -Name 'documentation CLI writes actual successful check and truthful summary' `
        -EventName pull_request -Event (New-PullRequestEvent $base $docsHead) -ExpectedMode docs
    New-TestBranch $base
    Write-TestFile 'docs/whitespace.md' "bad trailing whitespace   `n"
    $whitespaceHead = New-TestCommit
    Assert-PlanEntryPoint -Name 'documentation CLI fails when real diff check fails' -EventName pull_request `
        -Event (New-PullRequestEvent $base $whitespaceHead) -ExpectedMode docs -ExpectSuccess $false
    Assert-PlanEntryPoint -Name 'manual CLI reports full verification still required' -EventName workflow_dispatch `
        -Event ([pscustomobject]@{}) -ExpectedMode full
    Assert-PlanEntryPoint -Name 'malformed event JSON defaults to full verification' -EventName pull_request `
        -Event ([pscustomobject]@{}) -ExpectedMode full -MalformedJson

    Write-Output "Quality gate routing regression PASSED: $script:caseCount cases; real Git diffs and merge graphs, controlled API boundary."
    Write-Output ("Immutable Git evidence: $script:gitEvidenceReads actual reads, $script:gitEvidenceReuses reuses. Elapsed: {0:N2}s." -f $elapsed.Elapsed.TotalSeconds)
} finally {
    Set-Item -Path Function:\Invoke-QualityGateGit -Value $script:realQualityGateGit
    $elapsed.Stop()
    $resolved = [IO.Path]::GetFullPath($temporaryRoot)
    if ((Split-Path $resolved -Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) -ne $temporaryParent -or
        (Split-Path $resolved -Leaf) -ne $temporaryName -or !$temporaryName.StartsWith('hwjy-gate-plan-')) {
        throw 'Refusing to clean an unexpected regression directory.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
