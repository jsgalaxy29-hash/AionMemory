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

Assert-NoUiInfrastructureDependency
Assert-NoSecretsInRepo
Assert-NoDangerousConfig

dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true
dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
