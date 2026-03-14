param(
    [string]$RootPath = "."
)

$ErrorActionPreference = "Stop"

Write-Host "=== Analyse des attributs d'assembly ===" -ForegroundColor Cyan
Write-Host "Racine analysée : $RootPath"
Write-Host ""

$patterns = @(
    '\[assembly:\s*AssemblyCompany\s*\(',
    '\[assembly:\s*AssemblyConfiguration\s*\(',
    '\[assembly:\s*AssemblyFileVersion\s*\(',
    '\[assembly:\s*AssemblyInformationalVersion\s*\(',
    '\[assembly:\s*AssemblyProduct\s*\(',
    '\[assembly:\s*AssemblyTitle\s*\(',
    '\[assembly:\s*AssemblyVersion\s*\(',
    '\[assembly:\s*TargetFramework\s*\(',
    '\[assembly:\s*AssemblyDescription\s*\(',
    '\[assembly:\s*AssemblyCopyright\s*\(',
    '\[assembly:\s*NeutralResourcesLanguage\s*\(',
    '\[assembly:\s*ComVisible\s*\(',
    '\[assembly:\s*Guid\s*\(',
    '\[assembly:\s*RequiresPreviewFeatures\s*\('
)

$allCsFiles = Get-ChildItem -Path $RootPath -Recurse -File -Include *.cs |
    Where-Object {
        $_.FullName -notmatch '\\bin\\' -and
        $_.FullName -notmatch '\\obj\\'
    }

$matches = foreach ($file in $allCsFiles) {
    $content = Get-Content -Path $file.FullName -Raw -ErrorAction Stop

    foreach ($pattern in $patterns) {
        if ($content -match $pattern) {
            [PSCustomObject]@{
                File    = $file.FullName
                Pattern = $pattern
            }
        }
    }
}

$assemblyInfoFiles = Get-ChildItem -Path $RootPath -Recurse -File |
    Where-Object {
        $_.Name -match 'AssemblyInfo\.cs$|GlobalAssemblyInfo\.cs$'
    }

if ((-not $matches) -and (-not $assemblyInfoFiles)) {
    Write-Host "Aucun attribut d'assembly manuel détecté hors bin/obj." -ForegroundColor Green
}
else {
    Write-Host "=== Fichiers suspects contenant des attributs d'assembly ===" -ForegroundColor Yellow

    if ($matches) {
        $matches |
            Sort-Object File, Pattern |
            Format-Table -AutoSize
    }
    else {
        Write-Host "Aucun attribut [assembly: ...] manuel détecté." -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "=== Fichiers nommés AssemblyInfo.cs / GlobalAssemblyInfo.cs ===" -ForegroundColor Yellow

    if ($assemblyInfoFiles) {
        $assemblyInfoFiles |
            Select-Object FullName |
            Format-Table -AutoSize
    }
    else {
        Write-Host "Aucun fichier AssemblyInfo.cs / GlobalAssemblyInfo.cs trouvé." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Projets .csproj trouvés ===" -ForegroundColor Cyan

$csprojFiles = Get-ChildItem -Path $RootPath -Recurse -File -Include *.csproj

if ($csprojFiles) {
    $csprojFiles |
        Select-Object FullName |
        Format-Table -AutoSize
}
else {
    Write-Host "Aucun fichier .csproj trouvé." -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Vérification GenerateAssemblyInfo / GenerateTargetFrameworkAttribute ===" -ForegroundColor Cyan

$projectSettings = foreach ($proj in $csprojFiles) {
    $projContent = Get-Content -Path $proj.FullName -Raw -ErrorAction Stop

    $generateAssemblyInfoMatch = [regex]::Match(
        $projContent,
        '<GenerateAssemblyInfo>\s*(true|false)\s*</GenerateAssemblyInfo>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    $generateTargetFrameworkMatch = [regex]::Match(
        $projContent,
        '<GenerateTargetFrameworkAttribute>\s*(true|false)\s*</GenerateTargetFrameworkAttribute>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    [PSCustomObject]@{
        Project = $proj.FullName
        GenerateAssemblyInfo = if ($generateAssemblyInfoMatch.Success) {
            $generateAssemblyInfoMatch.Groups[1].Value
        }
        else {
            "(défaut SDK = true)"
        }
        GenerateTargetFrameworkAttribute = if ($generateTargetFrameworkMatch.Success) {
            $generateTargetFrameworkMatch.Groups[1].Value
        }
        else {
            "(défaut SDK = true)"
        }
    }
}

if ($projectSettings) {
    $projectSettings | Format-Table -Wrap -AutoSize
}
else {
    Write-Host "Impossible de lire les paramètres des projets." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Fin diagnostic ===" -ForegroundColor Green