# Security Notes

- API keys and provider settings belong in user secrets or environment variables. The repository includes `appsettings.OpenAI.example.json`, `appsettings.Mistral.example.json` and `appsettings.Development.example.json` as templates; real `appsettings*.json` files are git-ignored to avoid leaking credentials.
- See `docs/SECURITY.md` for end-to-end guidance (user-secrets in dev, environment variables in CI/prod, offline defaults).
- The CI pipeline runs a `gitleaks` scan on every push/PR to catch accidental secret commits. To run locally, install gitleaks and execute `gitleaks detect --source .`.
- When adding new configuration files, prefer `*.example.json` templates and keep sensitive values out of version control.
