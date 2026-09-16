-- This technical table is intentionally bootstrap material, not part of the
-- platform baseline.  The executor creates it before recording this first
-- bootstrap entry, and this statement makes the ownership explicit in the
-- release artifact as well.
create table if not exists p0_schema_migration (
    Id bigint not null auto_increment primary key,
    Version varchar(100) not null,
    Name varchar(200) not null,
    Checksum varchar(128) not null,
    Status varchar(20) not null,
    StartedAt datetime not null,
    FinishedAt datetime null,
    AppliedBy varchar(200) null,
    Error text null,
    unique key UX_p0_schema_migration_version (Version)
) engine=InnoDB default charset=utf8mb4;

create table if not exists p0_release_metadata (
    `Key` varchar(100) not null primary key,
    `Value` varchar(500) not null,
    UpdatedAt datetime not null
) engine=InnoDB default charset=utf8mb4;
