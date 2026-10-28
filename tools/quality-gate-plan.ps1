[CmdletBinding()]
param(
    [string]$EventName = $env:GITHUB_EVENT_NAME,
    [string]$EventPath = $env:GITHUB_EVENT_PATH,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$LibraryOnly
)

function Invoke-QualityGateGit {
    param([string]$RepositoryRoot, [string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $RepositoryRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Git could not start.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdout.GetAwaiter().GetResult()
            Error = $stderr.GetAwaiter().GetResult()
        }
    } finally { $process.Dispose() }
}

function Test-QualityGateDocumentationPath {
    param([string]$Path)
    # Agent instructions and skills can alter execution and are never a documentation shortcut.
    if ($Path -match '(?i)(?:\A|/)(?:AGENTS|SKILL)\.md\z') { return $false }
    return $Path -cmatch '\A(?:README[^/]*\.md|CHANGELOG\.md|CONTRIBUTING\.md|SECURITY\.md|docs/(?:[^/]+/)*[^/]+\.md|ui/(?:[^/]+/)*README[^/]*\.md|tools/QUALITY-GATE\.md)\z'
}

function Get-QualityGatePlan {
    param(
        [string]$EventName,
        [object]$Event,
        [string]$RepositoryRoot,
        [string]$Repository,
        [scriptblock]$GitHubApi
    )
    $plan = [pscustomobject]@{ Mode = 'full'; Reason = 'Full verification is the default.'; Base = ''; Head = ''; SourceRun = ''; RoutingTests = $true }
    try {
        if ($EventName -notin @('pull_request', 'push')) {
            $plan.Reason = 'Manual and unrecognized events require full verification.'
            return $plan
        }
        if ($null -eq $Event) { $plan.Reason = 'Event data is unavailable.'; return $plan }

        if ($EventName -eq 'pull_request') {
            $base = [string]$Event.pull_request.base.sha
            $head = [string]$Event.pull_request.head.sha
        } else {
            if ([string]$Event.ref -cnotmatch '\Arefs/heads/(main|master|dev)\z' -or $Event.deleted -eq $true -or $Event.forced -eq $true) {
                $plan.Reason = 'Only an ordinary push to a supported branch can be classified.'
                return $plan
            }
            $base = [string]$Event.before
            $head = [string]$Event.after
        }
        foreach ($sha in @($base, $head)) {
            if ($sha -notmatch '\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z' -or $sha -match '\A0+\z') {
                $plan.Reason = 'A valid nonzero commit range is required.'
                return $plan
            }
            $commit = Invoke-QualityGateGit $RepositoryRoot @('rev-parse', '--verify', "$sha`^{commit}")
            if ($commit.ExitCode -ne 0) { $plan.Reason = 'A range commit is unavailable.'; return $plan }
        }
        if ($EventName -eq 'pull_request') {
            $ancestor = Invoke-QualityGateGit $RepositoryRoot @('merge-base', $base, $head)
            if ($ancestor.ExitCode -ne 0) { $plan.Reason = 'The pull request merge base is unavailable.'; return $plan }
            $base = $ancestor.Output.Trim()
        }
        $plan.Base = $base
        $plan.Head = $head
        # No rename detection: both the removed code path and its new name must be classified.
        # Read the native UTF-8 stream directly to preserve NUL, spaces, Unicode and newlines.
        $diff = Invoke-QualityGateGit $RepositoryRoot @('diff', '--name-only', '--no-renames', '-z', $base, $head, '--')
        if ($diff.ExitCode -ne 0) { $plan.Reason = 'The changed path list is unavailable.'; return $plan }
        $paths = @($diff.Output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
        if ($paths.Count -eq 0) { $plan.Reason = 'An empty change list does not justify skipping full verification.'; return $plan }
        $plan.RoutingTests = @($paths | Where-Object {
            $_ -in @('.github/workflows/quality-gate.yml', 'tools/quality-gate-plan.ps1', 'tools/test-quality-gate-plan.ps1')
        }).Count -gt 0
        $codePaths = @($paths | Where-Object { !(Test-QualityGateDocumentationPath $_) })
        if ($codePaths.Count -eq 0) {
            $plan.Mode = 'docs'
            $plan.RoutingTests = $false
            $plan.Reason = 'Every changed path is on the documentation whitelist.'
            return $plan
        }
        $plan.Reason = 'Code, configuration, automation or unrecognized paths require full verification.'
        if ($EventName -ne 'push' -or !$GitHubApi -or $Repository -notmatch '\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z') { return $plan }

        $parents = Invoke-QualityGateGit $RepositoryRoot @('rev-list', '--parents', '-n', '1', $head)
        if ($parents.ExitCode -ne 0) { return $plan }
        $commits = $parents.Output.Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
        if ($commits.Count -ne 3 -or $commits[1] -ine $base) { return $plan }
        $prHead = $commits[2]
        $containedBase = Invoke-QualityGateGit $RepositoryRoot @('merge-base', '--is-ancestor', $base, $prHead)
        if ($containedBase.ExitCode -ne 0) { return $plan }
        $mergeTree = Invoke-QualityGateGit $RepositoryRoot @('rev-parse', "$head`^{tree}")
        $headTree = Invoke-QualityGateGit $RepositoryRoot @('rev-parse', "$prHead`^{tree}")
        if ($mergeTree.ExitCode -ne 0 -or $headTree.ExitCode -ne 0 -or $mergeTree.Output -cne $headTree.Output) { return $plan }

        $associated = & $GitHubApi "/commits/$head/pulls?per_page=100"
        $prs = @($associated | Where-Object { $null -ne $_ })
        if ($prs.Count -ge 100) { $plan.Reason = 'The associated pull request list may be truncated.'; return $plan }
        $branch = ([string]$Event.ref).Substring('refs/heads/'.Length)
        $matchingPrs = @($prs | Where-Object {
            $merged = $_.merged -eq $true
            if ($null -eq $_.merged -and $_.merged_at) {
                $mergedAt = [DateTimeOffset]::MinValue
                $merged = [DateTimeOffset]::TryParse([string]$_.merged_at, [ref]$mergedAt)
            }
            if ($null -ne $_.state -and $_.state -cne 'closed') { $merged = $false }
            $merged -and $_.merge_commit_sha -ieq $head -and $_.head.sha -ieq $prHead -and
                $_.base.sha -ieq $base -and $_.base.ref -ceq $branch -and
                $_.head.repo.full_name -ieq $Repository -and $_.base.repo.full_name -ieq $Repository
        })
        if ($matchingPrs.Count -ne 1) { $plan.Reason = 'The merge does not identify exactly one matching merged repository pull request.'; return $plan }
        $pr = $matchingPrs[0]
        $prNumber = 0L
        if ([string]$pr.number -cnotmatch '\A[1-9][0-9]*\z' -or ![long]::TryParse([string]$pr.number, [ref]$prNumber)) {
            $plan.Reason = 'The merged pull request number is invalid.'
            return $plan
        }
        $runs = & $GitHubApi "/actions/workflows/quality-gate.yml/runs?event=pull_request&head_sha=$prHead&per_page=100"
        $runList = @($runs.workflow_runs | Where-Object { $null -ne $_ })
        if ($null -eq $runs.total_count -or [int]$runs.total_count -ne $runList.Count) {
            $plan.Reason = 'The workflow run list is incomplete.'
            return $plan
        }
        $matchingRuns = @($runList | Where-Object {
            $_.head_sha -ieq $prHead -and $_.event -ceq 'pull_request' -and
                $_.path -cmatch '\A\.github/workflows/quality-gate\.yml(?:@[^\r\n]+)?\z' -and
                $_.repository.full_name -ieq $Repository -and $_.head_repository.full_name -ieq $Repository
        })
        if ($matchingRuns.Count -eq 0) { $plan.Reason = 'No repository workflow run verifies this pull request head.'; return $plan }
        foreach ($candidate in $matchingRuns) {
            if ([long]$candidate.id -le 0) {
                $plan.Reason = 'The latest workflow run cannot be reliably identified.'
                return $plan
            }
        }
        # Never filter by success first: a newer failed, cancelled or pending run must win.
        $run = $matchingRuns | Sort-Object -Property @{ Expression = { [long]$_.id }; Descending = $true } | Select-Object -First 1
        $runPrs = @($run.pull_requests | Where-Object { $null -ne $_ })
        $associations = @($runPrs | Where-Object {
            $_.number -eq $pr.number -and $_.head.sha -ieq $prHead -and $_.base.sha -ieq $base -and $_.base.ref -ceq $branch
        })
        if ($run.status -cne 'completed' -or $run.conclusion -cne 'success' -or ($runPrs.Count -gt 0 -and $associations.Count -ne 1)) {
            $plan.Reason = 'The latest workflow run is not a successful verification of the same pull request base and head.'
            return $plan
        }
        $verifiedAt = [DateTimeOffset]::MinValue
        $now = [DateTimeOffset]::UtcNow
        $validTimestamp = $false
        if ($run.updated_at -is [DateTimeOffset]) {
            $verifiedAt = $run.updated_at
            $validTimestamp = $true
        } elseif ($run.updated_at -is [DateTime] -and $run.updated_at.Kind -eq [DateTimeKind]::Utc) {
            # Invoke-RestMethod may deserialize JSON timestamps as UTC DateTime values.
            $verifiedAt = [DateTimeOffset]::new($run.updated_at)
            $validTimestamp = $true
        } elseif ($run.updated_at -is [string]) {
            $validTimestamp = [DateTimeOffset]::TryParse($run.updated_at, [ref]$verifiedAt)
        }
        if (!$validTimestamp -or
            $verifiedAt.Offset -ne [TimeSpan]::Zero -or $verifiedAt -gt $now.AddMinutes(5) -or $verifiedAt -lt $now.AddHours(-24)) {
            $plan.Reason = 'The verification timestamp is missing, invalid or outside the 24-hour reuse window.'
            return $plan
        }
        $jobs = & $GitHubApi "/actions/runs/$($run.id)/jobs?filter=latest&per_page=100"
        $jobList = @($jobs.jobs | Where-Object { $null -ne $_ })
        if ($null -eq $jobs.total_count -or [int]$jobs.total_count -ne $jobList.Count) {
            $plan.Reason = 'The workflow job list is incomplete.'
            return $plan
        }
        $gateJobs = @($jobList | Where-Object { $_.name -ceq 'Full Quality Gate' })
        if ($gateJobs.Count -ne 1 -or $gateJobs[0].status -cne 'completed' -or $gateJobs[0].conclusion -cne 'success') { return $plan }
        foreach ($requiredStep in @('Run full quality gate', 'Audit high and critical dependencies')) {
            $steps = @($gateJobs[0].steps | Where-Object { $_.name -ceq $requiredStep })
            if ($steps.Count -ne 1 -or $steps[0].status -cne 'completed' -or $steps[0].conclusion -cne 'success') {
                $plan.Reason = 'The successful run did not actually complete the full gate and dependency audit.'
                return $plan
            }
        }
        # GitHub can remove pull_requests associations after a PR is merged. A successful
        # marker from this job proves which checked-out tree actually passed full verification.
        $treeMarkers = @($gateJobs[0].steps | Where-Object { $_.name -cmatch '\AVerified source tree: ' })
        $expectedMarker = 'Verified source tree: ' + $mergeTree.Output.Trim()
        if ($treeMarkers.Count -ne 1 -or $treeMarkers[0].name -cne $expectedMarker -or
            $treeMarkers[0].status -cne 'completed' -or $treeMarkers[0].conclusion -cne 'success') {
            $plan.Reason = 'The successful full run has no unambiguous verification marker for this exact source tree.'
            return $plan
        }
        $plan.Mode = 'reuse'
        $plan.RoutingTests = $false
        $plan.Reason = 'The same repository pull request already verified this exact merged tree.'
        $plan.SourceRun = "https://github.com/$Repository/actions/runs/$($run.id)"
        return $plan
    } catch {
        $plan.Mode = 'full'
        $plan.SourceRun = ''
        $plan.Reason = 'Change classification or merge verification evidence is unavailable; full verification is required.'
        return $plan
    }
}

if ($LibraryOnly) { return }

$ErrorActionPreference = 'Stop'
$payload = $null
try {
    if ($EventPath) { $payload = Get-Content -LiteralPath $EventPath -Raw | ConvertFrom-Json }
} catch { $payload = $null }
$repository = $env:GITHUB_REPOSITORY
$api = {
    param([string]$Path)
    if (!$env:GH_TOKEN -or $env:GITHUB_REPOSITORY -notmatch '\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z' -or !$Path.StartsWith('/') -or $Path.StartsWith('//')) {
        throw 'The GitHub read API is unavailable.'
    }
    $headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    $responseHeaders = $null
    $response = Invoke-RestMethod -Method Get -Uri ("https://api.github.com/repos/$env:GITHUB_REPOSITORY" + $Path) -Headers $headers -TimeoutSec 10 -ResponseHeadersVariable responseHeaders
    if ((@($responseHeaders['Link']) -join ',') -match '(?i)\brel\s*=\s*"?next\b') {
        throw 'The GitHub response has another page; full verification is required.'
    }
    $response
}
$plan = Get-QualityGatePlan -EventName $EventName -Event $payload -RepositoryRoot $RepositoryRoot -Repository $repository -GitHubApi $api
if ($env:GITHUB_OUTPUT) {
    $routingTests = $plan.RoutingTests.ToString().ToLowerInvariant()
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "mode=$($plan.Mode)`nrouting_tests=$routingTests`n", [Text.UTF8Encoding]::new($false))
}
Write-Host "Quality gate plan: $($plan.Mode). $($plan.Reason)"

if ($plan.Mode -eq 'docs') {
    $check = Invoke-QualityGateGit $RepositoryRoot @('diff', '--check', $plan.Base, $plan.Head, '--')
    if ($check.ExitCode -ne 0) { throw ('Documentation diff check failed: ' + $check.Output) }
    $summary = "### Documentation verification`n`nDocumentation diff checks passed. Full verification and dependency audit were not run for this documentation-only change.`n"
} elseif ($plan.Mode -eq 'reuse') {
    $summary = "### Reused verification`n`nThis merged tree was already verified by [the successful pull request run]($($plan.SourceRun)). Full verification and dependency audit were not rerun in this push job.`n"
} else {
    $summary = "### Verification plan`n`nFull verification is required. $($plan.Reason) Results are reported by the following steps.`n"
}
if ($env:GITHUB_STEP_SUMMARY) { [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $summary, [Text.UTF8Encoding]::new($false)) }
