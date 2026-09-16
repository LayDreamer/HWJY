-- SQLite / P0 死 PermissionApi 关联清理。
-- 仅删除已不存在 Permission 或 API 的关联；ApiId = 0 为合法的无 API 绑定值。
BEGIN IMMEDIATE;
CREATE TABLE p0_003_backup_dead_permission_api AS
SELECT * FROM base_permission_api pa
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.Id = pa.PermissionId)
   OR (pa.ApiId <> 0 AND NOT EXISTS (SELECT 1 FROM base_api a WHERE a.Id = pa.ApiId));
DELETE FROM base_permission_api
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.Id = base_permission_api.PermissionId)
   OR (ApiId <> 0 AND NOT EXISTS (SELECT 1 FROM base_api a WHERE a.Id = base_permission_api.ApiId));
COMMIT;

SELECT COUNT(*) AS remaining_dead_permission_api
FROM base_permission_api pa
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.Id = pa.PermissionId)
   OR (pa.ApiId <> 0 AND NOT EXISTS (SELECT 1 FROM base_api a WHERE a.Id = pa.ApiId));
