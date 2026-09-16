-- SQLite / P0 发布产物刷新前重插入的死 PermissionApi 关联清理回滚。
BEGIN IMMEDIATE;
INSERT INTO base_permission_api SELECT * FROM p0_004_backup_reinserted_dead_permission_api;
COMMIT;
