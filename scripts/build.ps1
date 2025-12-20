[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-SolutionPath {
    Get-ChildItem -Path $repoRoot -File -Include *.sln, *.slnx | Select-Object -First 1
}

function Restore-And-BuildSolution {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionPath
    )

    Write-Host "Restoring solution $([IO.Path]::GetFileName($SolutionPath))"
    dotnet restore $SolutionPath

    Write-Host "Building solution $([IO.Path]::GetFileName($SolutionPath)) (Release)"
    dotnet build $SolutionPath -c Release
}

function Restore-And-BuildProjects {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo[]]$Projects
    )

    foreach ($project in $Projects) {
        Write-Host "Restoring project $($project.FullName)"
        dotnet restore $project.FullName

        Write-Host "Building project $($project.FullName) (Release)"
        dotnet build $project.FullName -c Release
    }
}

Push-Location $repoRoot
try {
    $solution = Get-SolutionPath
    if ($null -ne $solution) {
        Restore-And-BuildSolution -SolutionPath $solution.FullName
    }
    else {
        $projects = Get-ChildItem -Path $repoRoot -Recurse -Filter *.csproj | Sort-Object FullName
        if (-not $projects) {
            throw 'No projects found to build.'
        }

        Restore-And-BuildProjects -Projects $projects
    }
}
finally {
    Pop-Location
}
