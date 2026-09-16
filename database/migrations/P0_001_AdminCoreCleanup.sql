-- SQLite / Admin.Core P0 历史数据清理
-- 前置条件：应用已停止，且已完成完整库备份。
-- 本脚本只处理已裁剪的 A 类功能，以及租户/套餐/打印模板的 B 类菜单入口。
-- 每张 p0_001_backup_* 表只能由本脚本首次创建；重复执行会失败并回滚，以避免覆盖恢复依据。

BEGIN IMMEDIATE;

CREATE TEMP TABLE p0_001_target_permission AS
WITH RECURSIVE targets(Id) AS (
  SELECT Id FROM base_permission
  WHERE Label IN ('开发管理', '帮助文档', '参考样例', '地区管理', '消息管理', '消息分类', '站内信', '租户管理', '套餐管理', '打印模板')
  UNION ALL
  SELECT p.Id FROM base_permission p JOIN targets t ON p.ParentId = t.Id
)
SELECT DISTINCT Id FROM targets;

CREATE TEMP TABLE p0_001_target_api AS
WITH RECURSIVE targets(Id) AS (
  SELECT Id FROM base_api
  WHERE Path IN ('dev', 'region', 'msg', 'msg-type', 'site-msg') OR Path LIKE '/api/dev/%'
  UNION ALL
  SELECT a.Id FROM base_api a JOIN targets t ON a.ParentId = t.Id
)
SELECT DISTINCT Id FROM targets;

CREATE TEMP TABLE p0_001_target_view AS
SELECT Id FROM base_view
WHERE Label IN ('开发管理', '帮助文档', '参考样例', '地区管理', '消息管理', '消息分类', '站内信', '租户管理', '套餐管理', '打印模板');

-- 为回滚保留被删记录。若这些表已经存在，SQLite 会报错并使本事务回滚。
CREATE TABLE p0_001_backup_role_permission AS
SELECT * FROM base_role_permission WHERE PermissionId IN (SELECT Id FROM p0_001_target_permission);
CREATE TABLE p0_001_backup_permission_api AS
SELECT * FROM base_permission_api
WHERE PermissionId IN (SELECT Id FROM p0_001_target_permission)
   OR ApiId IN (SELECT Id FROM p0_001_target_api);
CREATE TABLE p0_001_backup_permission AS
SELECT * FROM base_permission WHERE Id IN (SELECT Id FROM p0_001_target_permission);
CREATE TABLE p0_001_backup_api AS
SELECT * FROM base_api WHERE Id IN (SELECT Id FROM p0_001_target_api);
CREATE TABLE p0_001_backup_view AS
SELECT * FROM base_view WHERE Id IN (SELECT Id FROM p0_001_target_view);
CREATE TABLE p0_001_backup_region AS SELECT * FROM base_region;
CREATE TABLE p0_001_backup_msg_user AS SELECT * FROM base_msg_user;
CREATE TABLE p0_001_backup_msg AS SELECT * FROM base_msg;
CREATE TABLE p0_001_backup_msg_type AS SELECT * FROM base_msg_type;
CREATE TABLE p0_001_backup_dev_code_gen_field AS SELECT * FROM dev_code_gen_field;
CREATE TABLE p0_001_backup_dev_code_gen AS SELECT * FROM dev_code_gen;
CREATE TABLE p0_001_backup_dev_project_gen AS SELECT * FROM dev_project_gen;
CREATE TABLE p0_001_backup_dev_project_model_field AS SELECT * FROM dev_project_model_field;
CREATE TABLE p0_001_backup_dev_project_model AS SELECT * FROM dev_project_model;
CREATE TABLE p0_001_backup_dev_project AS SELECT * FROM dev_project;
CREATE TABLE p0_001_backup_dev_group AS SELECT * FROM dev_group;
CREATE TABLE p0_001_backup_dev_template AS SELECT * FROM dev_template;

-- 先删关联，再删主数据。
DELETE FROM base_role_permission WHERE PermissionId IN (SELECT Id FROM p0_001_target_permission);
DELETE FROM base_permission_api
WHERE PermissionId IN (SELECT Id FROM p0_001_target_permission)
   OR ApiId IN (SELECT Id FROM p0_001_target_api);
DELETE FROM base_permission WHERE Id IN (SELECT Id FROM p0_001_target_permission);
DELETE FROM base_api WHERE Id IN (SELECT Id FROM p0_001_target_api);
DELETE FROM base_view WHERE Id IN (SELECT Id FROM p0_001_target_view);

DELETE FROM base_msg_user;
DELETE FROM base_msg;
DELETE FROM base_msg_type;
DELETE FROM base_region;
DELETE FROM dev_code_gen_field;
DELETE FROM dev_code_gen;
DELETE FROM dev_project_gen;
DELETE FROM dev_project_model_field;
DELETE FROM dev_project_model;
DELETE FROM dev_project;
DELETE FROM dev_group;
DELETE FROM dev_template;

COMMIT;

-- 执行后只读核对：以下查询均应返回 0。
SELECT 'Permission' AS item, COUNT(*) AS remaining FROM base_permission
WHERE Id IN (SELECT Id FROM p0_001_backup_permission)
UNION ALL SELECT 'View', COUNT(*) FROM base_view
WHERE Id IN (SELECT Id FROM p0_001_backup_view)
UNION ALL SELECT 'API', COUNT(*) FROM base_api
WHERE Id IN (SELECT Id FROM p0_001_backup_api)
UNION ALL SELECT 'RolePermission', COUNT(*) FROM base_role_permission
WHERE PermissionId IN (SELECT Id FROM p0_001_backup_permission)
UNION ALL SELECT 'PermissionApi', COUNT(*) FROM base_permission_api
WHERE PermissionId IN (SELECT Id FROM p0_001_backup_permission)
   OR ApiId IN (SELECT Id FROM p0_001_backup_api)
UNION ALL SELECT 'RegionData', COUNT(*) FROM base_region;
