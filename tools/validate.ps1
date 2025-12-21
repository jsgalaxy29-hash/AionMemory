#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $repoRoot

dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true
dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
