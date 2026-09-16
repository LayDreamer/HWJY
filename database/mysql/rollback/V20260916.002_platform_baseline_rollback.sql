-- Controlled rollback for the isolated P0 baseline acceptance database.
-- Version history remains so the executor records RolledBack and blocks a
-- silent re-apply until a verified recovery is performed.
drop table if exists `base_operation_log`;
drop table if exists `base_login_log`;
drop table if exists `base_view`;
drop table if exists `base_user_staff`;
drop table if exists `base_user_role`;
drop table if exists `base_user_org`;
drop table if exists `base_user`;
drop table if exists `base_tenant_pkg`;
drop table if exists `base_tenant`;
drop table if exists `base_task_log`;
drop table if exists `base_task_ext`;
drop table if exists `base_task`;
drop table if exists `base_search_template`;
drop table if exists `base_role_permission`;
drop table if exists `base_role_org`;
drop table if exists `base_role`;
drop table if exists `base_region`;
drop table if exists `base_print_template`;
drop table if exists `base_pkg_permission`;
drop table if exists `base_pkg`;
drop table if exists `base_permission_api`;
drop table if exists `base_permission`;
drop table if exists `base_org`;
drop table if exists `base_msg_user`;
drop table if exists `base_msg_type`;
drop table if exists `base_msg`;
drop table if exists `base_file`;
drop table if exists `base_doc_image`;
drop table if exists `base_doc`;
drop table if exists `base_dict_type`;
drop table if exists `base_dict`;
drop table if exists `base_app_log`;
drop table if exists `base_api`;
