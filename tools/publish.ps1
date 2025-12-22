[CmdletBinding()]
param(
    [ValidateSet('windows')]
    [string[]]$Targets = @('windows'),

    [string]$Configuration = 'Release',

    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts' 'publish'),

    [switch]$SkipClean
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appHostProject = Join-Path $repoRoot 'src' 'Aion.AppHost' 'Aion.AppHost.csproj'

if (-not (Test-Path $appHostProject)) {
    throw "Projet AppHost introuvable : $appHostProject"
}

$frameworks = @{
    windows = 'net10.0-windows10.0.19041.0'
}

$publishRoot = Join-Path $OutputRoot 'Aion.AppHost'
if (-not $SkipClean) {
    Remove-Item -Path $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

function Test-SecretFreePayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $nonExampleFiles = Get-ChildItem -Path $Path -Recurse -File -Filter 'appsettings*.json' | Where-Object { $_.Name -notmatch '\.example\.json$' }
    if ($nonExampleFiles) {
        $fileList = $nonExampleFiles | ForEach-Object { $_.FullName } | Sort-Object
        throw "Fichiers appsettings non-exemple détectés dans l'artefact :`n$($fileList -join "`n")"
    }
}

foreach ($target in $Targets) {
    if (-not $frameworks.ContainsKey($target)) {
        throw "Cible inconnue '$target'. Cibles supportées : $($frameworks.Keys -join ', ')"
    }

    $framework = $frameworks[$target]
    $targetOutput = Join-Path $publishRoot $target
    New-Item -ItemType Directory -Force -Path $targetOutput | Out-Null

    Write-Host "Publication Aion.AppHost pour $target ($framework)"
    dotnet publish $appHostProject -c $Configuration -f $framework -p:WindowsPackageType=None -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false -o $targetOutput

    Test-SecretFreePayload -Path $targetOutput

    $zipPath = Join-Path $publishRoot "Aion.AppHost-$target.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $targetOutput '*') -DestinationPath $zipPath -Force
    Write-Host "Archive générée : $zipPath"
}

Write-Host "Artefacts disponibles dans : $publishRoot"
