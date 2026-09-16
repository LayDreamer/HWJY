-- SQLite / P0 死 PermissionApi 关联清理回滚。
BEGIN IMMEDIATE;
INSERT INTO base_permission_api SELECT * FROM p0_003_backup_dead_permission_api;
COMMIT;
