-- Generated from the read-only current platform schemas.
-- Test fixtures (acc_*), migration metadata (p0_*), and all non-base tables are excluded.

CREATE TABLE `base_api` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint NOT NULL COMMENT '所属模块',
  `Name` varchar(50) DEFAULT NULL COMMENT '接口命名',
  `Label` varchar(500) DEFAULT NULL COMMENT '接口名称',
  `Path` varchar(500) DEFAULT NULL COMMENT '接口地址',
  `HttpMethods` varchar(50) DEFAULT NULL COMMENT '接口提交方法',
  `EnabledLog` bit(1) NOT NULL COMMENT '启用接口日志',
  `EnabledParams` bit(1) NOT NULL COMMENT '启用请求参数',
  `EnabledResult` bit(1) NOT NULL COMMENT '启用响应结果',
  `Description` varchar(500) DEFAULT NULL COMMENT '说明',
  `Sort` int NOT NULL COMMENT '排序',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_api_01` (`ParentId`,`Path`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='接口管理';

CREATE TABLE `base_app_log` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Logged` datetime(3) NOT NULL,
  `Level` varchar(5) DEFAULT NULL,
  `Message` text,
  `Logger` varchar(300) DEFAULT NULL,
  `Properties` text,
  `Callsite` varchar(300) DEFAULT NULL,
  `Exception` text,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='应用程序日志';

CREATE TABLE `base_dict` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint DEFAULT NULL COMMENT '上级Id',
  `DictTypeId` bigint NOT NULL COMMENT '字典类型Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '字典名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '字典编码',
  `Value` varchar(50) DEFAULT NULL COMMENT '字典值',
  `Description` varchar(500) DEFAULT NULL COMMENT '说明',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Sort` int NOT NULL COMMENT '排序',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_dict_01` (`DictTypeId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据字典';

CREATE TABLE `base_dict_type` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint NOT NULL COMMENT '上级Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Description` varchar(500) DEFAULT NULL COMMENT '描述',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `IsTree` bit(1) NOT NULL COMMENT '是否树形',
  `Sort` int NOT NULL COMMENT '排序',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_dict_type_01` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='数据字典类型';

CREATE TABLE `base_doc` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `ParentId` bigint NOT NULL COMMENT '父级节点',
  `Label` varchar(50) DEFAULT NULL COMMENT '名称',
  `Type` int NOT NULL COMMENT '类型',
  `Name` varchar(500) DEFAULT NULL COMMENT '命名',
  `Content` text COMMENT '内容',
  `Html` text COMMENT 'Html',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Opened` bit(1) DEFAULT NULL COMMENT '打开组',
  `Sort` int DEFAULT NULL COMMENT '排序',
  `Description` varchar(100) DEFAULT NULL COMMENT '描述',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_doc_01` (`ParentId`,`Label`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='文档';

CREATE TABLE `base_doc_image` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `DocumentId` bigint NOT NULL COMMENT '文档Id',
  `Url` varchar(255) DEFAULT NULL COMMENT '请求路径',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_doc_image_01` (`DocumentId`,`Url`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='文档图片';

CREATE TABLE `base_file` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Provider` varchar(50) DEFAULT NULL COMMENT 'OSS供应商',
  `BucketName` varchar(200) DEFAULT NULL COMMENT '存储桶名称',
  `FileDirectory` varchar(500) DEFAULT NULL COMMENT '文件目录',
  `FileGuid` char(36) NOT NULL COMMENT '文件Guid',
  `SaveFileName` varchar(200) DEFAULT NULL COMMENT '保存文件名',
  `FileName` varchar(200) DEFAULT NULL COMMENT '文件名',
  `Extension` varchar(20) DEFAULT NULL COMMENT '文件扩展名',
  `Size` bigint NOT NULL COMMENT '文件字节长度',
  `SizeFormat` varchar(50) DEFAULT NULL COMMENT '文件大小格式化',
  `LinkUrl` varchar(500) DEFAULT NULL COMMENT '链接地址',
  `Md5` varchar(50) DEFAULT NULL COMMENT 'md5码，防止上传重复文件',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='文件';

CREATE TABLE `base_msg` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Title` varchar(255) DEFAULT NULL COMMENT '标题',
  `Content` text COMMENT '内容',
  `TypeId` bigint NOT NULL COMMENT '类型Id',
  `TypeName` varchar(255) DEFAULT NULL COMMENT '类型名称',
  `Status` enum('Draft','Published','Scheduled','Revoked','Archived') NOT NULL COMMENT '消息状态',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='消息';

CREATE TABLE `base_msg_type` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint NOT NULL COMMENT '父级Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Sort` int NOT NULL COMMENT '排序',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_msg_type_01` (`ParentId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='消息分类';

CREATE TABLE `base_msg_user` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `MsgId` bigint NOT NULL COMMENT '消息Id',
  `UserId` bigint NOT NULL COMMENT '用户Id',
  `IsRead` bit(1) NOT NULL COMMENT '是否已读',
  `ReadTime` datetime(3) DEFAULT NULL COMMENT '已读时间',
  `IsImportant` bit(1) NOT NULL COMMENT '是否重要',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='消息用户';

CREATE TABLE `base_org` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `ParentId` bigint NOT NULL COMMENT '父级',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Value` varchar(50) DEFAULT NULL COMMENT '值',
  `MemberCount` int NOT NULL COMMENT '成员数',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Sort` int NOT NULL COMMENT '排序',
  `Description` varchar(500) DEFAULT NULL COMMENT '描述',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_org_01` (`ParentId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='组织架构';

CREATE TABLE `base_permission` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Platform` varchar(20) DEFAULT NULL COMMENT '平台',
  `ParentId` bigint NOT NULL COMMENT '父级节点',
  `Label` varchar(50) DEFAULT NULL COMMENT '权限名称',
  `Code` varchar(500) DEFAULT NULL COMMENT '权限编码',
  `Type` int NOT NULL COMMENT '权限类型',
  `ViewId` bigint DEFAULT NULL COMMENT '视图Id',
  `Name` varchar(100) DEFAULT NULL COMMENT '路由命名',
  `Path` varchar(500) DEFAULT NULL COMMENT '路由地址',
  `Redirect` varchar(500) DEFAULT NULL COMMENT '重定向地址',
  `Icon` varchar(100) DEFAULT NULL COMMENT '图标',
  `Hidden` bit(1) NOT NULL COMMENT '隐藏',
  `Opened` bit(1) NOT NULL COMMENT '展开分组',
  `NewWindow` bit(1) NOT NULL COMMENT '打开新窗口',
  `External` bit(1) NOT NULL COMMENT '链接外显',
  `IsKeepAlive` bit(1) NOT NULL COMMENT '是否缓存',
  `IsAffix` bit(1) NOT NULL COMMENT '是否固定',
  `Link` varchar(500) DEFAULT NULL COMMENT '链接地址',
  `IsIframe` bit(1) NOT NULL COMMENT '是否内嵌窗口',
  `IsSystem` bit(1) NOT NULL COMMENT '是否系统权限',
  `Sort` int NOT NULL COMMENT '排序',
  `Description` varchar(200) DEFAULT NULL COMMENT '描述',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_permission_01` (`Platform`,`ParentId`,`Label`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='权限';

CREATE TABLE `base_permission_api` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `PermissionId` bigint NOT NULL COMMENT '权限Id',
  `ApiId` bigint NOT NULL COMMENT '接口Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`PermissionId`,`ApiId`),
  UNIQUE KEY `idx_base_permission_api_01` (`PermissionId`,`ApiId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='权限接口';

CREATE TABLE `base_pkg` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint NOT NULL COMMENT '父级Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Description` varchar(200) DEFAULT NULL COMMENT '说明',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Sort` int NOT NULL COMMENT '排序',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_pkg_01` (`ParentId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='套餐';

CREATE TABLE `base_pkg_permission` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Platform` varchar(255) DEFAULT NULL COMMENT '平台',
  `PkgId` bigint NOT NULL COMMENT '套餐Id',
  `PermissionId` bigint NOT NULL COMMENT '权限Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`PkgId`,`PermissionId`),
  UNIQUE KEY `idx_base_pkg_permission_01` (`Platform`,`PkgId`,`PermissionId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='套餐权限';

CREATE TABLE `base_print_template` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Template` text COMMENT '模板',
  `PrintData` text COMMENT '打印数据',
  `Description` varchar(200) DEFAULT NULL COMMENT '说明',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Sort` int NOT NULL COMMENT '排序',
  `Version` bigint NOT NULL COMMENT '版本',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_print_template_01` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='打印模板';

CREATE TABLE `base_region` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ParentId` bigint NOT NULL COMMENT '上级Id',
  `Name` varchar(100) DEFAULT NULL COMMENT '名称',
  `ShortName` varchar(100) DEFAULT NULL COMMENT '简称',
  `Level` int NOT NULL COMMENT '级别',
  `Code` varchar(20) DEFAULT NULL COMMENT '代码',
  `Pinyin` varchar(200) DEFAULT NULL COMMENT '拼音',
  `PinyinFirst` varchar(20) DEFAULT NULL COMMENT '拼音首字母',
  `Capital` varchar(100) DEFAULT NULL COMMENT '行政中心/政府驻地',
  `Population` int DEFAULT NULL COMMENT '人口（单位：万人）',
  `Area` int DEFAULT NULL COMMENT '面积（单位：平方千米）',
  `AreaCode` varchar(20) DEFAULT NULL COMMENT '区号',
  `ZipCode` varchar(20) DEFAULT NULL COMMENT '邮编',
  `Sort` int DEFAULT NULL COMMENT '排序',
  `Hot` bit(1) NOT NULL COMMENT '热门',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_region_01` (`ParentId`,`Name`),
  UNIQUE KEY `idx_base_region_02` (`ParentId`,`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='地区';

CREATE TABLE `base_role` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `ParentId` bigint NOT NULL COMMENT '父级Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Code` varchar(50) DEFAULT NULL COMMENT '编码',
  `Type` int NOT NULL COMMENT '角色类型',
  `DataScope` int NOT NULL COMMENT '数据范围',
  `Description` varchar(200) DEFAULT NULL COMMENT '说明',
  `Hidden` bit(1) NOT NULL COMMENT '隐藏',
  `Sort` int NOT NULL COMMENT '排序',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_role_01` (`ParentId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='角色';

CREATE TABLE `base_role_org` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `RoleId` bigint NOT NULL COMMENT '角色Id',
  `OrgId` bigint NOT NULL COMMENT '部门Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`RoleId`,`OrgId`),
  UNIQUE KEY `idx_base_role_org_01` (`RoleId`,`OrgId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='角色部门';

CREATE TABLE `base_role_permission` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Platform` varchar(255) DEFAULT NULL COMMENT '平台',
  `RoleId` bigint NOT NULL COMMENT '角色Id',
  `PermissionId` bigint NOT NULL COMMENT '权限Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`RoleId`),
  UNIQUE KEY `idx_base_role_permission_01` (`Platform`,`RoleId`,`PermissionId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='角色权限';

CREATE TABLE `base_search_template` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `ModuleId` bigint NOT NULL COMMENT '模块Id',
  `Name` varchar(50) DEFAULT NULL COMMENT '名称',
  `Template` text COMMENT '模板',
  `Version` bigint NOT NULL COMMENT '版本',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_search_template_01` (`CreatedUserId`,`ModuleId`,`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='查询模板';

CREATE TABLE `base_task` (
  `Id` varchar(255) NOT NULL,
  `Topic` varchar(255) DEFAULT NULL,
  `Body` text,
  `Round` int NOT NULL,
  `Interval` int NOT NULL,
  `IntervalArgument` varchar(1024) DEFAULT NULL,
  `CreateTime` datetime(3) NOT NULL,
  `LastRunTime` datetime(3) NOT NULL,
  `CurrentRound` int NOT NULL,
  `ErrorTimes` int NOT NULL,
  `Status` int NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `base_task_ext` (
  `TaskId` varchar(255) NOT NULL COMMENT '任务Id',
  `AlarmEmail` varchar(500) DEFAULT NULL COMMENT '报警邮件，多个邮件地址则逗号分隔',
  `FailRetryCount` int DEFAULT NULL COMMENT '失败重试次数',
  `FailRetryInterval` int DEFAULT NULL COMMENT '失败重试间隔（秒）',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '添加时间',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '添加用户Id',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改用户Id',
  PRIMARY KEY (`TaskId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='任务邮件';

CREATE TABLE `base_task_log` (
  `TaskId` varchar(255) DEFAULT NULL,
  `Round` int NOT NULL,
  `ElapsedMilliseconds` bigint NOT NULL,
  `Success` bit(1) NOT NULL,
  `Exception` text,
  `Remark` text,
  `CreateTime` datetime(3) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `base_tenant` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `UserId` bigint NOT NULL COMMENT '授权用户',
  `OrgId` bigint NOT NULL COMMENT '授权部门',
  `TenantType` enum('Platform','Tenant') DEFAULT NULL COMMENT '租户类型',
  `Domain` varchar(300) DEFAULT NULL COMMENT '域名',
  `DbKey` varchar(50) DEFAULT NULL COMMENT '数据库注册键',
  `DbType` int DEFAULT NULL COMMENT '数据库',
  `ConnectionString` varchar(500) DEFAULT NULL COMMENT '连接字符串',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `Description` varchar(500) DEFAULT NULL COMMENT '说明',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='租户';

CREATE TABLE `base_tenant_pkg` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint NOT NULL COMMENT '租户Id',
  `PkgId` bigint NOT NULL COMMENT '套餐Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`TenantId`,`PkgId`),
  UNIQUE KEY `idx_base_tenant_pkg_01` (`TenantId`,`PkgId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='租户套餐';

CREATE TABLE `base_user` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `UserName` varchar(60) DEFAULT NULL COMMENT '账号',
  `Password` varchar(200) DEFAULT NULL COMMENT '密码',
  `PasswordEncryptType` int DEFAULT NULL COMMENT '密码加密类型',
  `Name` varchar(60) DEFAULT NULL COMMENT '姓名',
  `Mobile` varchar(20) DEFAULT NULL COMMENT '手机号',
  `Email` varchar(100) DEFAULT NULL COMMENT '邮箱',
  `OrgId` bigint NOT NULL COMMENT '主属部门Id',
  `ManagerUserId` bigint DEFAULT NULL COMMENT '直属主管Id',
  `NickName` varchar(60) DEFAULT NULL COMMENT '昵称',
  `Avatar` varchar(500) DEFAULT NULL COMMENT '头像',
  `Status` int DEFAULT NULL COMMENT '用户状态',
  `Type` int NOT NULL COMMENT '用户类型',
  `LastLoginTime` datetime(3) DEFAULT NULL COMMENT '最后登录时间',
  `LastLoginIP` varchar(100) DEFAULT NULL COMMENT '最后登录IP',
  `LastLoginCountry` varchar(100) DEFAULT NULL COMMENT '最后登录国家',
  `LastLoginProvince` varchar(100) DEFAULT NULL COMMENT '最后登录省份',
  `LastLoginCity` varchar(100) DEFAULT NULL COMMENT '最后登录城市',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_user_01` (`UserName`),
  KEY `idx_base_user_02` (`Mobile`),
  KEY `idx_base_user_03` (`Email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='用户';

CREATE TABLE `base_user_org` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `UserId` bigint NOT NULL COMMENT '用户Id',
  `OrgId` bigint NOT NULL COMMENT '部门Id',
  `IsManager` bit(1) NOT NULL COMMENT '是否主管',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  PRIMARY KEY (`Id`,`UserId`,`OrgId`),
  UNIQUE KEY `idx_base_user_org_01` (`UserId`,`OrgId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='用户所属部门';

CREATE TABLE `base_user_role` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `UserId` bigint NOT NULL COMMENT '用户Id',
  `RoleId` bigint NOT NULL COMMENT '角色Id',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`,`UserId`,`RoleId`),
  UNIQUE KEY `idx_base_user_role_01` (`UserId`,`RoleId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='用户角色';

CREATE TABLE `base_user_staff` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `Position` varchar(255) DEFAULT NULL COMMENT '职位',
  `JobNumber` varchar(20) DEFAULT NULL COMMENT '工号',
  `Sex` int DEFAULT NULL COMMENT '性别',
  `EntryTime` datetime(3) DEFAULT NULL COMMENT '入职时间',
  `WorkWeChatCard` varchar(500) DEFAULT NULL COMMENT '企业微信名片',
  `Introduce` varchar(500) DEFAULT NULL COMMENT '个人简介',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='用户员工';

CREATE TABLE `base_view` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `Platform` varchar(20) DEFAULT NULL COMMENT '平台',
  `ParentId` bigint NOT NULL COMMENT '所属节点',
  `Name` varchar(50) DEFAULT NULL COMMENT '视图命名',
  `Label` varchar(500) DEFAULT NULL COMMENT '视图名称',
  `Path` varchar(500) DEFAULT NULL COMMENT '视图路径',
  `Description` varchar(500) DEFAULT NULL COMMENT '说明',
  `Cache` bit(1) NOT NULL COMMENT '缓存',
  `Sort` int NOT NULL COMMENT '排序',
  `Enabled` bit(1) NOT NULL COMMENT '启用',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  `ModifiedUserId` bigint DEFAULT NULL COMMENT '修改者用户Id',
  `ModifiedUserName` varchar(60) DEFAULT NULL COMMENT '修改者用户名',
  `ModifiedUserRealName` varchar(60) DEFAULT NULL COMMENT '修改者姓名',
  `ModifiedTime` datetime(3) DEFAULT NULL COMMENT '修改时间',
  `IsDeleted` bit(1) NOT NULL COMMENT '是否删除',
  PRIMARY KEY (`Id`),
  UNIQUE KEY `idx_base_view_01` (`Platform`,`ParentId`,`Label`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='视图管理';

CREATE TABLE `base_login_log` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `Name` varchar(60) DEFAULT NULL COMMENT '姓名',
  `IP` varchar(100) DEFAULT NULL COMMENT 'IP',
  `Country` varchar(100) DEFAULT NULL COMMENT '国家',
  `Province` varchar(100) DEFAULT NULL COMMENT '省份',
  `City` varchar(100) DEFAULT NULL COMMENT '城市',
  `Isp` varchar(100) DEFAULT NULL COMMENT '网络服务商',
  `Browser` varchar(100) DEFAULT NULL COMMENT '浏览器',
  `Os` varchar(100) DEFAULT NULL COMMENT '操作系统',
  `Device` varchar(50) DEFAULT NULL COMMENT '设备',
  `BrowserInfo` text COMMENT '浏览器信息',
  `ElapsedMilliseconds` bigint NOT NULL COMMENT '耗时（毫秒）',
  `Status` bit(1) NOT NULL COMMENT '操作状态',
  `Msg` text COMMENT '操作消息',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='登录日志';

CREATE TABLE `base_operation_log` (
  `Id` bigint NOT NULL COMMENT '主键Id',
  `TenantId` bigint DEFAULT NULL COMMENT '租户Id',
  `ApiLabel` varchar(50) DEFAULT NULL COMMENT '接口名称',
  `ApiPath` varchar(500) DEFAULT NULL COMMENT '接口地址',
  `ApiMethod` varchar(50) DEFAULT NULL COMMENT '接口方法',
  `Name` varchar(60) DEFAULT NULL COMMENT '姓名',
  `IP` varchar(100) DEFAULT NULL COMMENT 'IP',
  `Country` varchar(100) DEFAULT NULL COMMENT '国家',
  `Province` varchar(100) DEFAULT NULL COMMENT '省份',
  `City` varchar(100) DEFAULT NULL COMMENT '城市',
  `Isp` varchar(100) DEFAULT NULL COMMENT '网络服务商',
  `Browser` varchar(100) DEFAULT NULL COMMENT '浏览器',
  `Os` varchar(100) DEFAULT NULL COMMENT '操作系统',
  `Device` varchar(50) DEFAULT NULL COMMENT '设备',
  `BrowserInfo` text COMMENT '浏览器信息',
  `ElapsedMilliseconds` bigint NOT NULL COMMENT '耗时（毫秒）',
  `Status` bit(1) NOT NULL COMMENT '操作状态',
  `Msg` text COMMENT '操作消息',
  `Params` text COMMENT '请求参数',
  `StatusCode` int DEFAULT NULL COMMENT '状态码',
  `Result` text COMMENT '响应结果',
  `CreatedUserId` bigint DEFAULT NULL COMMENT '创建者用户Id',
  `CreatedUserName` varchar(60) DEFAULT NULL COMMENT '创建者用户名',
  `CreatedUserRealName` varchar(60) DEFAULT NULL COMMENT '创建者姓名',
  `CreatedTime` datetime(3) DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`Id`),
  KEY `idx_base_operation_log_01` (`ApiPath`,`CreatedTime`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='操作日志';

