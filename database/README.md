# Admin.Core P0 MySQL release material

`mysql/manifest.json` is the sole forward-apply allowlist.  The executor only selects entries whose provider is `MySql` and direction is `Forward`; rollback and historical SQLite cleanup files are intentionally excluded.

The release executor creates and maintains `p0_schema_migration`, acquires a MySQL named lock for the full apply operation, records `Running`, `Succeeded`, and `Failed`, and rejects checksum changes or unfinished history.  Apply and rollback are restricted to an explicitly supplied isolated database.
