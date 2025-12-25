#!/usr/bin/env pwsh
param(
    [switch]$SkipWorkloadInstall
)

$ErrorActionPreference = "Stop"

function Require-DotNet {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet SDK introuvable. Installez .NET via https://dotnet.microsoft.com/download puis relancez ce script."
    }
}

function Ensure-MauiWorkload {
    param([switch]$SkipInstall)

    if ($SkipInstall) {
        Write-Host "Installation du workload MAUI ignorée."
        return
    }

    $workloads = dotnet workload list
    if ($workloads -match "maui") {
        Write-Host "Workload MAUI déjà installé."
        return
    }

    Write-Host "Installation du workload MAUI..."
    dotnet workload install maui
}

Require-DotNet
Ensure-MauiWorkload -SkipInstall:$SkipWorkloadInstall

Write-Host "Restauration des dépendances..."
dotnet restore

Write-Host "Compilation de l'AppHost..."
dotnet build ./src/Aion.AppHost/Aion.AppHost.csproj

Write-Host "Installation locale terminée. Lancez l'application avec Visual Studio ou 'dotnet build -t:Run' selon votre plateforme."
