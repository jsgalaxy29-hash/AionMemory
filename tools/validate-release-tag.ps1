[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,
    [string]$ReleaseNotesRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs' 'release-notes')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$tagPattern = '^v\d+\.\d+\.\d+(-beta\.\d+|-rc\.\d+)?$'
if ($Tag -notmatch $tagPattern) {
    throw "Le tag '$Tag' ne respecte pas le format vMAJOR.MINOR.PATCH (suffixes -beta.N/-rc.N autorisés)."
}

$notesPath = Join-Path $ReleaseNotesRoot "$Tag.md"
if (-not (Test-Path $notesPath)) {
    throw "Notes de release manquantes : $notesPath"
}

$requiredSections = @('## Nouveautés', '## Corrections', '## Points de vigilance')
foreach ($section in $requiredSections) {
    $sectionFound = Select-String -Path $notesPath -Pattern "^$([regex]::Escape($section))" -SimpleMatch -Quiet
    if (-not $sectionFound) {
        throw "La section obligatoire '$section' est absente de $notesPath"
    }
}

Write-Host "Tag $Tag validé avec release notes $notesPath"
