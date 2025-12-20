[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-SolutionPath {
    Get-ChildItem -Path $repoRoot -File -Include *.sln, *.slnx | Select-Object -First 1
}

function Get-TestProjects {
    Get-ChildItem -Path $repoRoot -Recurse -Filter *.csproj | Where-Object { $_.Name -match 'Test' }
}

$testProjects = Get-TestProjects
if (-not $testProjects) {
    Write-Host 'No tests found'
    return
}

$solution = Get-SolutionPath
if ($null -ne $solution) {
    Write-Host "Running dotnet test on solution $([IO.Path]::GetFileName($solution.FullName)) (Release)"
    dotnet test $solution.FullName -c Release
}
else {
    foreach ($project in $testProjects) {
        Write-Host "Running dotnet test for $($project.FullName) (Release)"
        dotnet test $project.FullName -c Release
    }
}
