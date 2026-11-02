-- Explicit MySQL maintenance only; never loaded at application startup.
-- Backups preserve original rows. Run the companion rollback only before making
-- further changes to these records. No users, roles or packages are deleted.
CREATE TABLE IF NOT EXISTS hwjy_20261102_permission LIKE base_permission;
CREATE TABLE IF NOT EXISTS hwjy_20261102_permission_api LIKE base_permission_api;
CREATE TABLE IF NOT EXISTS hwjy_20261102_role_permission LIKE base_role_permission;
CREATE TABLE IF NOT EXISTS hwjy_20261102_pkg_permission LIKE base_pkg_permission;
CREATE TABLE IF NOT EXISTS hwjy_20261102_view LIKE base_view;
CREATE TABLE IF NOT EXISTS hwjy_20261102_api LIKE base_api;
START TRANSACTION;
DROP TEMPORARY TABLE IF EXISTS hwjy_retired_permission_ids;
CREATE TEMPORARY TABLE hwjy_retired_permission_ids (Id BIGINT PRIMARY KEY);
INSERT INTO hwjy_retired_permission_ids
WITH RECURSIVE retired AS (
    SELECT Id FROM base_permission
    WHERE (Path = '/dev' AND Id = 563552009130053)
       OR (Path = '/help' AND Id = 161227168497734)
       OR (Name = 'admin/doc' AND Path = '/platform/system/document')
       OR (Name = 'admin/region' AND Path = '/platform/system/region')
       OR (Name = 'admin/msg-type' AND Path = '/platform/system/msg-type')
       OR (Name = 'admin/msg' AND Path = '/platform/system/msg')
       OR (Name IN ('dev/codegen','dev/dev-group','dev/dev-template','dev/dev-project',
                    'dev/dev-project-model','dev/dev-project-model-field',
                    'dev/dev-project-gen','dev/dev-project-gen/preview') AND Path LIKE '/dev/%')
    UNION ALL
    SELECT child.Id FROM base_permission child JOIN retired parent ON child.ParentId = parent.Id
)
SELECT DISTINCT Id FROM retired;
INSERT IGNORE INTO hwjy_20261102_permission SELECT p.* FROM base_permission p JOIN hwjy_retired_permission_ids r ON r.Id = p.Id;
INSERT IGNORE INTO hwjy_20261102_permission_api SELECT a.* FROM base_permission_api a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
INSERT IGNORE INTO hwjy_20261102_role_permission SELECT a.* FROM base_role_permission a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
INSERT IGNORE INTO hwjy_20261102_pkg_permission SELECT a.* FROM base_pkg_permission a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
DELETE a FROM base_permission_api a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
DELETE a FROM base_role_permission a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
DELETE a FROM base_pkg_permission a JOIN hwjy_retired_permission_ids r ON r.Id = a.PermissionId;
DELETE p FROM base_permission p JOIN hwjy_retired_permission_ids r ON r.Id = p.Id;
-- A view shared with any surviving permission is preserved.
INSERT IGNORE INTO hwjy_20261102_view
SELECT v.* FROM base_view v
WHERE v.Id IN (SELECT ViewId FROM hwjy_20261102_permission WHERE ViewId IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.ViewId = v.Id);
DELETE v FROM base_view v JOIN hwjy_20261102_view b ON b.Id = v.Id
WHERE NOT EXISTS (SELECT 1 FROM base_permission p WHERE p.ViewId = v.Id);
-- Only retired API namespaces are removed; implemented document APIs remain.
INSERT IGNORE INTO hwjy_20261102_api
SELECT a.* FROM base_api a
WHERE (a.Path LIKE '/api/dev/%' OR a.Path LIKE '/api/admin/region/%'
    OR a.Path LIKE '/api/admin/msg/%' OR a.Path LIKE '/api/admin/msg-type/%')
  AND NOT EXISTS (SELECT 1 FROM base_permission_api p WHERE p.ApiId = a.Id);
DELETE a FROM base_api a JOIN hwjy_20261102_api b ON b.Id = a.Id
WHERE NOT EXISTS (SELECT 1 FROM base_permission_api p WHERE p.ApiId = a.Id);
COMMIT;
