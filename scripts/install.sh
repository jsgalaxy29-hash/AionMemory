#!/usr/bin/env bash
set -euo pipefail

skip_workload_install=false
if [[ "${1:-}" == "--skip-workload-install" ]]; then
  skip_workload_install=true
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK introuvable. Installez .NET via https://dotnet.microsoft.com/download puis relancez ce script." >&2
  exit 1
fi

if [[ "${skip_workload_install}" == "true" ]]; then
  echo "Installation du workload MAUI ignorée."
else
  if dotnet workload list | grep -q "maui"; then
    echo "Workload MAUI déjà installé."
  else
    echo "Installation du workload MAUI..."
    dotnet workload install maui
  fi
fi

echo "Restauration des dépendances..."
dotnet restore

echo "Compilation de l'AppHost..."
dotnet build ./src/Aion.AppHost/Aion.AppHost.csproj

echo "Installation locale terminée. Lancez l'application avec Visual Studio, Rider ou 'dotnet build -t:Run' selon votre plateforme."
