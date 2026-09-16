-- SQLite / Admin.Core P0 历史数据清理回滚
-- 仅在 P0_001_AdminCoreCleanup.sql 已成功创建 p0_001_backup_* 表后执行。
-- 如清理事务本身失败，请勿执行本脚本；SQLite 已自动回滚，请直接核对原数据。

BEGIN IMMEDIATE;

INSERT INTO base_permission SELECT * FROM p0_001_backup_permission;
INSERT INTO base_api SELECT * FROM p0_001_backup_api;
INSERT INTO base_view SELECT * FROM p0_001_backup_view;
INSERT INTO base_region SELECT * FROM p0_001_backup_region;
INSERT INTO base_msg_type SELECT * FROM p0_001_backup_msg_type;
INSERT INTO base_msg SELECT * FROM p0_001_backup_msg;
INSERT INTO base_msg_user SELECT * FROM p0_001_backup_msg_user;
INSERT INTO dev_group SELECT * FROM p0_001_backup_dev_group;
INSERT INTO dev_template SELECT * FROM p0_001_backup_dev_template;
INSERT INTO dev_project SELECT * FROM p0_001_backup_dev_project;
INSERT INTO dev_project_model SELECT * FROM p0_001_backup_dev_project_model;
INSERT INTO dev_project_model_field SELECT * FROM p0_001_backup_dev_project_model_field;
INSERT INTO dev_project_gen SELECT * FROM p0_001_backup_dev_project_gen;
INSERT INTO dev_code_gen SELECT * FROM p0_001_backup_dev_code_gen;
INSERT INTO dev_code_gen_field SELECT * FROM p0_001_backup_dev_code_gen_field;
INSERT INTO base_role_permission SELECT * FROM p0_001_backup_role_permission;
INSERT INTO base_permission_api SELECT * FROM p0_001_backup_permission_api;

COMMIT;
