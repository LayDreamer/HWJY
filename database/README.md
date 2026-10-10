# Admin.Core P0 MySQL release material

`mysql/manifest.json` is the sole forward-apply allowlist.  The executor only selects entries whose provider is `MySql` and direction is `Forward`; rollback and historical SQLite cleanup files are intentionally excluded.

The release executor creates and maintains `p0_schema_migration`, acquires a MySQL named lock for the full apply operation, records `Running`, `Succeeded`, and `Failed`, and rejects checksum changes or unfinished history.  Apply and rollback are restricted to an explicitly supplied isolated database.

Use the audited release command from the repository root. It defaults to help, and `plan`/`verify` are read-only. Supply a dedicated isolated database through `ADMINCORE_RELEASE_MYSQL`; no command prints its value.

```powershell
dotnet run --project tools/AdminCore.P0.Release -- help
dotnet run --project tools/AdminCore.P0.Release -- plan
dotnet run --project tools/AdminCore.P0.Release -- verify
dotnet run --project tools/AdminCore.P0.Release -- apply --confirm
dotnet run --project tools/AdminCore.P0.Release -- rollback --version 20260916.002 --confirm
```

Production configuration starts from `deploy/admincore/templates/dbconfig.Production.template.json`. Deploy it outside the Host's automatic ConfigCenter, inject connection strings and manifest path with the deployment system, and keep every automatic database/structure/seed switch disabled.
