#Requires -Version 7.3
<#
.SYNOPSIS
Sets up GitHub for deploying the knowledge base to the VM.

.DESCRIPTION
Walks through each step and asks before changing anything:

  1. Requires approval before workflows from outside contributors run, because the repository is public and has a
     self-hosted runner.
  2. Creates the production environment, limited to the main branch.
  3. Gives the CD workflow read-only access to the private configuration repository with a deploy key.
  4. Fills the environment's secrets and variables with the AppHost parameters, offering your local AppHost user
     secrets as defaults.
  5. Creates a runner registration token and, given an SSH target, copies scripts/setup-vm.sh to the VM and runs it.
  6. Lists the sign-in redirect addresses to add to the Entra app registration.

It is safe to run again: values already in GitHub are kept unless you replace them.

.EXAMPLE
./scripts/setup-github.ps1 -SshTarget ubuntu@203.0.113.10
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Environment = 'production',

    # user@host of the VM. Asked for when omitted; leave empty to print the VM commands instead of running them.
    [Parameter(Position = 0)]
    [string] $SshTarget
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$appHostProject = Join-Path $repoRoot 'src/KnowledgeBase.AppHost'
$runnerLabel = 'knowledge-base'

function Write-Step([string] $Text) { Write-Host "`n== $Text" -ForegroundColor Cyan }
function Write-Ok([string] $Text) { Write-Host "✓ $Text" -ForegroundColor Green }
function Write-Note([string] $Text) { Write-Host "! $Text" -ForegroundColor Yellow }

function Confirm-Step([string] $Question) {
    $reply = Read-Host "$Question [Y/n]"
    return [string]::IsNullOrWhiteSpace($reply) -or $reply -match '^[Yy]'
}

function Invoke-Gh {
    $output = & gh @args
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($args[0]) $($args[1]) failed (exit code $LASTEXITCODE)."
    }
    return $output
}

# Passes the value on standard input exactly as given. Piping a string to a native command in PowerShell appends a
# newline, which would end up in the secret.
function Set-GhSecret([string] $Name, [string] $Value) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('gh')
    foreach ($argument in 'secret', 'set', $Name, '--env', $Environment, '--repo', $repo) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.StandardInput.Write($Value)
    $process.StandardInput.Close()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Setting secret $Name failed: $errorOutput"
    }
}

function Get-LocalUserSecrets {
    $output = dotnet user-secrets list --json --project $appHostProject 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @{}
    }

    $text = $output -join "`n"
    if (-not $text.Contains('//BEGIN')) {
        return @{}
    }
    $json = $text.Substring($text.IndexOf('//BEGIN') + 7)
    $json = $json.Substring(0, $json.LastIndexOf('//END'))
    return $json | ConvertFrom-Json -AsHashtable
}

function Read-ParameterValue([pscustomobject] $Parameter, [string] $Offer, [string] $OfferSource, [bool] $InGitHub) {
    $kind = $Parameter.Secret ? 'secret' : 'variable'
    Write-Host "`n$($Parameter.Name)" -ForegroundColor White -NoNewline
    Write-Host " → $kind $($Parameter.GitHub)" -ForegroundColor DarkGray
    Write-Host "  $($Parameter.Help)" -ForegroundColor DarkGray

    if ($InGitHub) {
        $current = $Parameter.Secret ? '(set)' : $existingVariables[$Parameter.GitHub]
        Write-Host "  In GitHub: $current"
        $enter = 'keep it'
        if ($Offer) {
            Write-Host "  Type + to replace it with $OfferSource"
        }
    }
    elseif ($Offer) {
        $enter = "use $OfferSource"
    }
    else {
        $enter = 'skip'
    }

    $prompt = "  Enter to $enter; or type a value, @path to read a file, - to skip"
    $answer = $Parameter.Secret ? (Read-Host -Prompt $prompt -MaskInput) : (Read-Host -Prompt $prompt)

    switch -Regex ($answer) {
        '^$' { return $InGitHub ? $null : $Offer }
        '^-$' { return $null }
        '^\+$' { return $Offer }
        '^@' { return (Get-Content -Raw -LiteralPath $answer.Substring(1)).TrimEnd("`r", "`n") }
        default { return $answer }
    }
}

Push-Location $repoRoot
try {
    Write-Step 'GitHub CLI'
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'The GitHub CLI is not signed in. Run: gh auth login'
    }
    $repo = Invoke-Gh repo view --json nameWithOwner --jq .nameWithOwner
    Write-Ok "Signed in; repository $repo"

    Write-Step 'Workflows from outside contributors'
    $policy = Invoke-Gh api "repos/$repo/actions/permissions/fork-pr-contributor-approval" --jq .approval_policy
    if ($policy -eq 'all_external_contributors') {
        Write-Ok 'Every outside contributor already needs approval before workflows run'
    }
    else {
        Write-Host 'The repository is public and the VM runs a self-hosted runner. A pull request from a fork could'
        Write-Host 'change a workflow to run on that runner, so its workflows should wait for your approval.'
        Write-Host "Current policy: $policy"
        if (Confirm-Step 'Require approval for all outside contributors?') {
            Invoke-Gh api -X PUT "repos/$repo/actions/permissions/fork-pr-contributor-approval" -f approval_policy=all_external_contributors | Out-Null
            Write-Ok 'Approval is now required for all outside contributors'
        }
        else {
            Write-Note 'Left unchanged. Set it later under Settings → Actions → General.'
        }
    }

    Write-Step "Environment '$Environment'"
    gh api "repos/$repo/environments/$Environment" *> $null
    if ($LASTEXITCODE -ne 0) {
        if (-not (Confirm-Step "Create the '$Environment' environment, deployable only from main?")) {
            throw "The CD workflow needs the '$Environment' environment."
        }
        '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' |
            gh api -X PUT "repos/$repo/environments/$Environment" --input - | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Creating the '$Environment' environment failed."
        }
        Write-Ok "Created '$Environment'"
    }
    else {
        Write-Ok "'$Environment' exists"
    }

    $branchPolicies = Invoke-Gh api "repos/$repo/environments/$Environment/deployment-branch-policies" --jq '.branch_policies[].name'
    if (@($branchPolicies) -contains 'main') {
        Write-Ok 'Deployments are limited to main'
    }
    else {
        Invoke-Gh api -X POST "repos/$repo/environments/$Environment/deployment-branch-policies" -f name=main -f type=branch | Out-Null
        Write-Ok 'Limited deployments to main'
    }

    Write-Step 'Configuration repository access'
    $configRepo = "$repo-config"
    gh repo view $configRepo --json name *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "The private configuration repository $configRepo does not exist yet. Create it first (see the README)."
    }
    $hasDeployKey = @(Invoke-Gh secret list --env $Environment --json name --jq '.[].name') -contains 'CONFIG_DEPLOY_KEY'
    if ($hasDeployKey -and -not (Confirm-Step "CONFIG_DEPLOY_KEY is already set. Replace it with a new deploy key?")) {
        Write-Ok 'Kept CONFIG_DEPLOY_KEY'
    }
    else {
        # A read-only key for that one repository; the private half goes straight into the environment secret and the
        # local copy is deleted.
        $keyPath = Join-Path ([System.IO.Path]::GetTempPath()) "knowledge-base-config-$([guid]::NewGuid().ToString('N'))"
        try {
            ssh-keygen -q -t ed25519 -N '' -C "$repo CD" -f $keyPath
            if ($LASTEXITCODE -ne 0) {
                throw 'Generating the deploy key failed.'
            }
            Invoke-Gh repo deploy-key add "$keyPath.pub" --repo $configRepo --title "$repo CD ($Environment)" | Out-Null
            Set-GhSecret 'CONFIG_DEPLOY_KEY' (Get-Content -Raw -LiteralPath $keyPath)
            Write-Ok "Added a read-only deploy key to $configRepo and saved it as CONFIG_DEPLOY_KEY"
        }
        finally {
            Remove-Item -LiteralPath $keyPath, "$keyPath.pub" -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Step 'AppHost parameters'
    Write-Host 'Keys are stored as secrets because the logs of a public repository are public. The instructions and wiki'
    Write-Host 'rules are not set here: they live in the private config repository.'

    $agents = 'FactFinder', 'ThreadWeaver', 'Reviewer', 'Editor', 'Librarian'
    $parameters = @(
        [pscustomobject]@{ Name = 'WikiRootDirectory'; GitHub = 'WIKI_ROOT_DIRECTORY'; Secret = $false; Required = $true; OnVm = $true; Default = '/srv/knowledge-base'; Help = 'Folder on the VM for the wiki and pipeline state.' }
        [pscustomobject]@{ Name = 'AnthropicApiKey'; GitHub = 'ANTHROPIC_API_KEY'; Secret = $true; Required = $true; OnVm = $false; Default = $null; Help = 'Anthropic API key. Required by the AppHost even when no agent uses Anthropic (any placeholder then).' }
        [pscustomobject]@{ Name = 'EntraTenantId'; GitHub = 'ENTRA_TENANT_ID'; Secret = $false; Required = $true; OnVm = $false; Default = $null; Help = 'Directory (tenant) id of the Entra app registration.' }
        [pscustomobject]@{ Name = 'EntraClientId'; GitHub = 'ENTRA_CLIENT_ID'; Secret = $false; Required = $true; OnVm = $false; Default = $null; Help = 'Application (client) id of the Entra app registration.' }
        [pscustomobject]@{ Name = 'EntraClientSecret'; GitHub = 'ENTRA_CLIENT_SECRET'; Secret = $true; Required = $true; OnVm = $false; Default = $null; Help = 'A client secret of the Entra app registration.' }
        [pscustomobject]@{ Name = 'OpenAICompatibleEndpoint'; GitHub = 'OPENAI_COMPATIBLE_ENDPOINT'; Secret = $false; Required = $true; OnVm = $true; Default = 'http://127.0.0.1:9931/v1'; Help = 'The llama-server API on the VM, as the API container sees it (it shares the host network).' }
        [pscustomobject]@{ Name = 'OpenAICompatibleApiKey'; GitHub = 'OPENAI_COMPATIBLE_API_KEY'; Secret = $true; Required = $false; OnVm = $true; Default = $null; Help = 'Only if llama-server was started with --api-key; skip otherwise.' }
        [pscustomobject]@{ Name = 'WikiBackupRemote'; GitHub = 'WIKI_BACKUP_REMOTE'; Secret = $false; Required = $false; OnVm = $true; Default = $null; Help = 'SSH URL of the private wiki backup repository (git@github.com:<owner>/<repo>.git); skip to leave backup off. See "Wiki backup" in the README.' }
        [pscustomobject]@{ Name = 'WikiBackupKeyDirectory'; GitHub = 'WIKI_BACKUP_KEY_DIRECTORY'; Secret = $false; Required = $false; OnVm = $true; Default = $null; Help = 'Folder on the VM holding the backup deploy key (id_ed25519) and known_hosts, e.g. /srv/knowledge-base-backup-key. It must exist; skip to leave backup off.' }
    )
    foreach ($agent in $agents) {
        $upper = $agent.ToUpperInvariant()
        $parameters += [pscustomobject]@{ Name = "${agent}Provider"; GitHub = "${upper}_PROVIDER"; Secret = $false; Required = $true; OnVm = $true; Default = 'Anthropic'; Help = "Anthropic or OpenAICompatible (the local model) for the $agent." }
        $parameters += [pscustomobject]@{ Name = "${agent}Model"; GitHub = "${upper}_MODEL"; Secret = $false; Required = $true; OnVm = $false; Default = $null; Help = "Model for the $agent, e.g. claude-sonnet-5-5, or any label (such as qwen) for the local model." }
        $parameters += [pscustomobject]@{ Name = "${agent}EnableThinking"; GitHub = "${upper}_ENABLE_THINKING"; Secret = $false; Required = $true; OnVm = $true; Default = 'true'; Help = "true, or false to stop the local model reasoning before it answers for the $agent (Anthropic ignores it)." }
    }

    $localSecrets = Get-LocalUserSecrets
    if ($localSecrets.Count -gt 0) {
        Write-Ok "Found $($localSecrets.Count) local AppHost user secrets to offer as defaults"
    }
    $existingSecrets = @(Invoke-Gh secret list --env $Environment --json name --jq '.[].name')
    $existingVariables = @{}
    foreach ($variable in (Invoke-Gh variable list --env $Environment --json 'name,value' | ConvertFrom-Json)) {
        $existingVariables[$variable.name] = $variable.value
    }

    $chosen = @{}
    $missing = @()
    foreach ($parameter in $parameters) {
        $localValue = $localSecrets["Parameters:$($parameter.Name)"]
        # Values that differ on the VM (paths, addresses) default to the VM value rather than the local one.
        if ($parameter.OnVm -or -not $localValue) {
            $offer = $parameter.Default
            $offerSource = $offer ? $offer : $null
        }
        else {
            $offer = $localValue
            $offerSource = ($parameter.Secret -or $localValue.Length -gt 60) ? "the local user secret ($($localValue.Length) characters)" : "the local user secret: $localValue"
        }

        $inGitHub = $parameter.Secret ? ($existingSecrets -contains $parameter.GitHub) : $existingVariables.ContainsKey($parameter.GitHub)
        $value = Read-ParameterValue $parameter $offer $offerSource $inGitHub

        if ($null -ne $value -and $value -ne '') {
            if ($parameter.Secret) {
                Set-GhSecret $parameter.GitHub $value
            }
            else {
                Invoke-Gh variable set $parameter.GitHub --env $Environment --repo $repo --body $value | Out-Null
            }
            Write-Ok "Saved $($parameter.GitHub)"
            $chosen[$parameter.Name] = $value
        }
        elseif ($inGitHub) {
            Write-Ok "Kept $($parameter.GitHub)"
            if (-not $parameter.Secret) {
                $chosen[$parameter.Name] = $existingVariables[$parameter.GitHub]
            }
        }
        elseif ($parameter.Required) {
            $missing += $parameter.GitHub
        }
    }

    if ($missing.Count -gt 0) {
        Write-Note "Not set, and deployments fail without them: $($missing -join ', ')"
    }

    Write-Step 'Self-hosted runner'
    $runners = @(Invoke-Gh api "repos/$repo/actions/runners" --jq ".runners[] | select(any(.labels[]; .name == `"$runnerLabel`")) | `"\(.name) (\(.status))`"")
    $runAnyway = $true
    if ($runners.Count -gt 0) {
        Write-Ok "Runner already registered: $($runners -join ', ')"
        $runAnyway = Confirm-Step 'Run the VM setup script anyway (it also checks Docker, the data directory and llama-server)?'
    }

    if ($runAnyway) {
        $token = Invoke-Gh api -X POST "repos/$repo/actions/runners/registration-token" --jq .token
        $dataDirectory = $chosen['WikiRootDirectory'] ?? '/srv/knowledge-base'
        $vmArguments = "--token $token --data-dir '$dataDirectory' --repo-url 'https://github.com/$repo'"

        if (-not $PSBoundParameters.ContainsKey('SshTarget')) {
            $SshTarget = Read-Host 'SSH target for the VM (e.g. ubuntu@203.0.113.10), or Enter to print the commands instead'
        }

        if ($SshTarget) {
            scp (Join-Path $PSScriptRoot 'setup-vm.sh') "${SshTarget}:setup-knowledge-base-vm.sh"
            if ($LASTEXITCODE -ne 0) {
                throw "Copying the setup script to $SshTarget failed."
            }
            ssh -t $SshTarget "bash ~/setup-knowledge-base-vm.sh $vmArguments"
            if ($LASTEXITCODE -ne 0) {
                throw 'The VM setup script failed; see its output above.'
            }
        }
        else {
            Write-Host 'Copy scripts/setup-vm.sh to the VM and run it there within the hour (the token expires):'
            Write-Host "  bash setup-vm.sh $vmArguments" -ForegroundColor White
        }
    }

    Write-Step 'Entra sign-in'
    $hostName = Read-Host 'Public host name Cloudflare serves the app on (e.g. kb.example.com), or Enter to skip'
    if ($hostName) {
        Write-Host 'In the Entra admin centre, open the app registration → Authentication → Web, and add both redirect URIs:'
        Write-Host "  https://$hostName/signin-oidc" -ForegroundColor White
        Write-Host "  https://$hostName/signout-callback-oidc" -ForegroundColor White
    }

    Write-Step 'Done'
    Write-Host 'Each successful CI run on main now deploys. To deploy without a new commit:'
    Write-Host '  gh workflow run CD --ref main' -ForegroundColor White
}
finally {
    Pop-Location
}
