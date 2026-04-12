param(
    [string]$RootPath = "."
)

$ErrorActionPreference = "Stop"

Write-Host "=== Recherche des fichiers AssemblyInfo manuels ===" -ForegroundColor Cyan

$files = Get-ChildItem -Path $RootPath -Recurse -File -Include *.cs |
    Where-Object {
        $_.FullName -notlike "*\bin\*" -and
        $_.FullName -notlike "*\obj\*"
    }

$assemblyFiles = @()
$attributeHits = @()

foreach ($file in $files) {
    if ($file.Name -match '^(AssemblyInfo|GlobalAssemblyInfo)\.cs$') {
        $assemblyFiles += $file.FullName
    }

    $matches = Select-String -Path $file.FullName -Pattern '^\s*\[assembly:\s*' -SimpleMatch:$false
    if ($matches) {
        foreach ($m in $matches) {
            $attributeHits += [PSCustomObject]@{
                File     = $file.FullName
                Line     = $m.LineNumber
                Content  = $m.Line.Trim()
            }
        }
    }
}

Write-Host ""
Write-Host "=== Fichiers AssemblyInfo.cs / GlobalAssemblyInfo.cs hors obj/bin ===" -ForegroundColor Yellow
if ($assemblyFiles.Count -eq 0) {
    Write-Host "Aucun." -ForegroundColor Green
} else {
    $assemblyFiles | Sort-Object | ForEach-Object { Write-Host $_ }
}

Write-Host ""
Write-Host "=== Attributs [assembly: ...] trouvés hors obj/bin ===" -ForegroundColor Yellow
if ($attributeHits.Count -eq 0) {
    Write-Host "Aucun." -ForegroundColor Green
} else {
    $attributeHits |
        Sort-Object File, Line |
        Format-Table -AutoSize
}

Write-Host ""
Write-Host "=== Fin ===" -ForegroundColor Green