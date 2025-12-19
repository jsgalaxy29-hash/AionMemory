param(
    [Parameter(Mandatory = $false, Position = 0)]
    [ValidateSet('build', 'test')]
    [string]$Command = 'build'
)

$ErrorActionPreference = 'Stop'

function Invoke-RestoreSolution {
    Write-Host 'Restoring solution AionMemory.slnx'
    dotnet restore ./AionMemory.slnx
}

function Invoke-BuildSolution {
    Invoke-RestoreSolution
    Write-Host 'Building solution AionMemory.slnx'
    dotnet build ./AionMemory.slnx
}

function Invoke-TestSolution {
    Invoke-RestoreSolution
    Write-Host 'Running tests for solution AionMemory.slnx'
    dotnet test ./AionMemory.slnx
}

switch ($Command) {
    'build' {
        Invoke-BuildSolution
    }
    'test' {
        Invoke-TestSolution
    }
}
