# DataEngine Hardening Notes

- The Codex brief requested placing `QuerySpec` in Infrastructure. To keep the `IDataEngine` contract in the Domain layer (and avoid Domain -> Infrastructure dependencies), `QuerySpec` lives in `Aion.Domain`. Infrastructure implements it without crossing boundaries.
- The `TableId`/`EntityTypeId` terminology is harmonised in code. Columns still use the legacy `EntityTypeId` name for schema compatibility via `[Column]` mappings.
- Soft-delete is respected when a table enables `SupportsSoftDelete`, but audit trail plumbing remains to be implemented; hooks can be added where deletions/updates occur.
