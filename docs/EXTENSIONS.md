# Extensions v1 (assemblies internes)

## Objectif
AionMemory peut être étendu via des assemblies internes déclarées localement. Le cœur reste stable : aucune marketplace, aucun chargement distant arbitraire.

## Modèle de contrat
Une extension expose un descripteur dans `Aion.Domain` :

- `ExtensionDescriptor` : `id`, `version`, `capabilities`.
- `[assembly: ExtensionDescriptorAttribute(...)]` (optionnel) pour déclarer le descripteur directement dans l’assembly.

Si l’attribut n’est pas présent, le chargeur utilise le nom et la version de l’assembly, avec une liste de capacités vide.

## Configuration (assemblies connues)
Les assemblies sont explicitement listées dans la configuration :

```json
{
  "Aion": {
    "Extensions": {
      "RootPath": "./",
      "KnownAssemblies": [
        "Aion.Extensions.Example.dll"
      ]
    }
  }
}
```

- `RootPath` est la racine autorisée pour charger des assemblies.
- `KnownAssemblies` contient des chemins relatifs à `RootPath` (ou absolus si déjà contenus dans `RootPath`).

## Activation / désactivation
L’UI (AppHost) permet d’activer/désactiver une extension. L’état est stocké localement dans les préférences de l’application.

## Sécurité & limites de sandbox
Cette v1 applique un **contrôle minimal** :

- seules les assemblies listées sont chargées ;
- le chemin est contraint à `RootPath` ;
- aucune source distante n’est utilisée.

Limites importantes :

- **pas d’isolation** : l’extension est chargée dans le même processus .NET ;
- **pas de permissions restreintes** : le code a accès au même contexte que le reste de l’app ;
- **risque d’exécution de code** si une assembly non fiable est déclarée.

### Recommandations
- utilisez des assemblies internes signées et auditées ;
- gardez la liste `KnownAssemblies` minimale ;
- privilégiez la revue de code plutôt qu’un chargement dynamique.
