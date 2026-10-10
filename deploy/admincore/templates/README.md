# Admin.Core Production database template

`dbconfig.Production.template.json` is a deployment-only template and must not be placed in the Host's automatically loaded `ConfigCenter` directory. Inject `DbConfig__ConnectionString`, `DbConfig__Dbs__0__ConnectionString`, and `P0SchemaGuard__ManifestPath` through the deployment system. Do not place credentials in the template or repository.

The root `admindb` is the Host's Admin and persistent task database (`DbKeys.TaskDb`); nested `logdb` is the log database. Every configured database has automatic create, schema sync, data sync, update sync, and seed generation disabled. Production startup verifies the P0 manifest history and required tables before any database registration or task executor startup.
