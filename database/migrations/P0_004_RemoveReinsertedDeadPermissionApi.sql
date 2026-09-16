-- SQLite / P0 发布产物刷新前重插入的死 PermissionApi 关联清理。
-- 前置条件：已重新构建并确认 Release Seed 的关联数为 116。
BEGIN IMMEDIATE;
CREATE TABLE p0_004_backup_reinserted_dead_permission_api AS
SELECT * FROM base_permission_api pa
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.Id = pa.PermissionId)
   OR (pa.ApiId <> 0 AND NOT EXISTS (SELECT 1 FROM base_api a WHERE a.Id = pa.ApiId));
DELETE FROM base_permission_api
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.Id = base_permission_api.PermissionId)
   OR (ApiId <> 0 AND NOT EXISTS (SELECT 1 FROM base_api a WHERE a.Id = base_permission_api.ApiId));
COMMIT;
