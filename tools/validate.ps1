#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $repoRoot

function Assert-NoUiInfrastructureDependency {
    $uiProjects = Get-ChildItem -Path (Join-Path $repoRoot "src") -Filter *.csproj -Recurse |
        Where-Object { $_.FullName -match "Aion\\.AppHost" }

    foreach ($project in $uiProjects) {
        [xml]$projectXml = Get-Content $project.FullName
        $references = @()
        foreach ($itemGroup in $projectXml.Project.ItemGroup) {
            if ($null -ne $itemGroup.ProjectReference) {
                $references += $itemGroup.ProjectReference | ForEach-Object { $_.Include }
            }
        }

        if ($references -match "Aion\\.Infrastructure\\.csproj") {
            throw "UI project '$($project.FullName)' must not reference Aion.Infrastructure directly."
        }
    }
}

function Assert-NoSecretsInRepo {
    $secretCandidates = Get-ChildItem -Path $repoRoot -Recurse -File -Include "appsettings*.json" |
        Where-Object { $_.Name -notmatch "\\.example\\.json$" } |
        Where-Object { $_.FullName -notmatch "[/\\\\](bin|obj)[/\\\\]" }

    if ($secretCandidates) {
        $paths = $secretCandidates | ForEach-Object { $_.FullName }
        throw "Potential secret files detected:`n$($paths -join [Environment]::NewLine)"
    }
}

function Assert-NoDangerousConfig {
    $configFiles = Get-ChildItem -Path $repoRoot -Recurse -File -Include "appsettings*.json" |
        Where-Object { $_.Name -notmatch "\\.example\\.json$" } |
        Where-Object { $_.FullName -notmatch "[/\\\\](bin|obj)[/\\\\]" }

    if (-not $configFiles) {
        return
    }

    $devKey = "aion-dev-sqlcipher-key-change-me-32chars"
    foreach ($file in $configFiles) {
        try {
            $json = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json
        }
        catch {
            throw "Invalid JSON configuration detected in $($file.FullName)."
        }

        if ($null -ne $json.Aion.Storage -and $json.Aion.Storage.EncryptPayloads -eq $false) {
            throw "Storage encryption is disabled in $($file.FullName)."
        }

        if ($null -ne $json.Aion.Ai -and $json.Aion.Ai.EnablePromptTracing -eq $true) {
            throw "Prompt tracing is enabled in $($file.FullName)."
        }

        if ($null -ne $json.Aion.Database -and $json.Aion.Database.EncryptionKey -eq $devKey) {
            throw "Development database encryption key detected in $($file.FullName)."
        }

        if ($null -ne $json.Aion.Storage -and $json.Aion.Storage.EncryptionKey -eq $devKey) {
            throw "Development storage encryption key detected in $($file.FullName)."
        }
    }
}

function Assert-ContractVersionBump {
    $contractRoot = "src/Aion.Domain/"
    $versionFile = "docs/ARCHITECTURE_FREEZE_V1.md"

    $baseBranch = if ($env:GITHUB_BASE_REF) { $env:GITHUB_BASE_REF } else { "main" }
    $baseRef = "origin/$baseBranch"

    try {
        git fetch origin $baseBranch --depth=1 | Out-Null
    }
    catch {
        Write-Warning "Unable to fetch $baseRef for contract validation; falling back to HEAD~1."
    }

    $mergeBase = git merge-base HEAD $baseRef 2>$null
    if (-not $mergeBase) {
        $mergeBase = "HEAD~1"
    }

    $changedFiles = git diff --name-only $mergeBase HEAD
    if (-not $changedFiles) {
        return
    }

    $contractChanges = $changedFiles | Where-Object { $_ -like "$contractRoot*" }
    if (-not $contractChanges) {
        return
    }

    if (-not ($changedFiles -contains $versionFile)) {
        $contractList = $contractChanges -join [Environment]::NewLine
        throw "Contract changes detected in '$contractRoot' without version bump in '$versionFile'.`n$contractList"
    }
}

Assert-NoUiInfrastructureDependency
Assert-NoSecretsInRepo
Assert-NoDangerousConfig
Assert-ContractVersionBump

dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true
dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
