param(
    [string]$CaseId = 'vayne-project-r-hud',
    [Parameter(Mandatory)][string]$Step,
    [ValidateSet('pending', 'passed', 'failed')][string]$Observation = 'pending',
    [string]$EvidencePath,
    [string]$Note,
    [string]$HostPath = (Join-Path $PSScriptRoot '..\LOL-GameAssistant\Tools\SkinCore\league-skin-core-host.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.artifacts\skin-effects')
)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\native\LeagueSkinCore\skin-effects-cases.json') -Raw | ConvertFrom-Json
$case = @($plan.cases | Where-Object id -eq $CaseId)
if ($case.Count -ne 1) { throw 'Unknown effects case' }
$case = $case[0]
$selectedStep = @($case.steps | Where-Object id -eq $Step)
if ($selectedStep.Count -ne 1) { throw 'Unknown effects step' }
$selectedStep = $selectedStep[0]
if ($Observation -ne 'pending' -and [string]::IsNullOrWhiteSpace($Note)) {
    throw 'Describe the actual visual observation in -Note'
}
if ($Observation -eq 'passed' -and -not $EvidencePath) { throw 'Visual pass requires a screenshot or recording in -EvidencePath' }
$evidence = $null
if ($EvidencePath) {
    $file = Get-Item -LiteralPath $EvidencePath
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw 'Evidence must be a nonempty file' }
    $evidence = [ordered]@{ path=$file.FullName; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
$autoHost = (Resolve-Path -LiteralPath $HostPath).Path
$state = & $autoHost status | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $state.ok -or -not $state.session) { throw 'Cannot read current skin state' }
$matches = $state.model -eq $case.model -and $state.active_model -eq $case.model
if ($selectedStep.target_skin) { $matches = $matches -and $state.skin -eq $case.skin -and $state.active_skin -eq $case.skin }
else { $matches = $matches -and $state.skin -eq 0 -and $state.active_skin -eq 0 }
if (-not $matches) { throw "Current model/skin does not match step $Step; no visual result recorded" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$reportPath = Join-Path $OutputDirectory "$CaseId.json"
$records = @()
if (Test-Path -LiteralPath $reportPath) {
    $previous = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    # Results from another game or build must not complete this game's acceptance.
    if ($previous.session -eq $state.session -and $previous.reference_sha256 -eq $state.reference_sha256) {
        $records = @($previous.steps | Where-Object id -ne $Step)
    }
}
$records += [pscustomobject][ordered]@{
    id=$Step; expected=$selectedStep.expect; observation=$Observation; note=$Note
    evidence=$evidence; time_utc=[DateTimeOffset]::UtcNow.ToString('o'); state=$state
}
$overall = 'pending'
if (@($records | Where-Object observation -eq 'failed').Count) { $overall = 'failed' }
elseif (@($records | Where-Object observation -eq 'passed').Count -eq $case.steps.Count) { $overall = 'passed' }
[ordered]@{
    case_id=$CaseId; session=$state.session; reference_sha256=$state.reference_sha256
    overall=$overall; steps=$records
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
[ordered]@{ case_id=$CaseId; step=$Step; overall=$overall; report=$reportPath } | ConvertTo-Json -Compress
if ($overall -eq 'failed') { exit 1 }
if ($overall -eq 'pending') { exit 2 }
