-- Restore original rows before further edits. Duplicate IDs cause an error;
-- callers must ROLLBACK on error rather than overwrite newer records.
START TRANSACTION;
INSERT INTO base_api SELECT * FROM hwjy_20261102_api;
INSERT INTO base_view SELECT * FROM hwjy_20261102_view;
INSERT INTO base_permission SELECT * FROM hwjy_20261102_permission;
INSERT INTO base_permission_api SELECT * FROM hwjy_20261102_permission_api;
INSERT INTO base_role_permission SELECT * FROM hwjy_20261102_role_permission;
INSERT INTO base_pkg_permission SELECT * FROM hwjy_20261102_pkg_permission;
COMMIT;
