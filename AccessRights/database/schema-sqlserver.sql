-- =====================================================================
--  Access Rights Management System - SQL Server schema
--  ใช้เมื่อเลือก Database:Provider = "SqlServer"
--  รันด้วย:  sqlcmd -S localhost -d AccessRights -E -i schema-sqlserver.sql
--  (สร้าง Database ก่อน:  CREATE DATABASE AccessRights COLLATE Thai_CI_AS;)
-- =====================================================================

-- ---------------------------------------------------------------------
-- Users และบทบาท
-- ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.users', N'U') IS NULL
CREATE TABLE dbo.users (
    id             INT IDENTITY(1,1) NOT NULL CONSTRAINT pk_users PRIMARY KEY,
    username       NVARCHAR(50)   NOT NULL CONSTRAINT uq_users_username UNIQUE,
    full_name      NVARCHAR(150)  NOT NULL,
    email          NVARCHAR(150)  NOT NULL CONSTRAINT uq_users_email UNIQUE,
    password_hash  NVARCHAR(500)  NOT NULL,
    is_active      BIT            NOT NULL CONSTRAINT df_users_is_active DEFAULT (1),
    created_at     DATETIME2      NOT NULL CONSTRAINT df_users_created_at DEFAULT (SYSUTCDATETIME())
);
GO

IF OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
CREATE TABLE dbo.user_roles (
    user_id  INT          NOT NULL CONSTRAINT fk_user_roles_user REFERENCES dbo.users (id) ON DELETE CASCADE,
    role     NVARCHAR(20) NOT NULL CONSTRAINT ck_user_roles_role CHECK (role IN (N'Admin', N'Requester', N'Approver')),
    CONSTRAINT pk_user_roles PRIMARY KEY (user_id, role)
);
GO

-- ---------------------------------------------------------------------
-- Rights
-- ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.rights', N'U') IS NULL
CREATE TABLE dbo.rights (
    id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT pk_rights PRIMARY KEY,
    name               NVARCHAR(150)  NOT NULL CONSTRAINT uq_rights_name UNIQUE,
    description        NVARCHAR(1000) NULL,
    approver_id        INT            NOT NULL CONSTRAINT fk_rights_approver REFERENCES dbo.users (id),
    max_duration_days  INT            NULL CONSTRAINT ck_rights_max_duration CHECK (max_duration_days BETWEEN 1 AND 3650),
    is_active          BIT            NOT NULL CONSTRAINT df_rights_is_active DEFAULT (1),
    created_at         DATETIME2      NOT NULL CONSTRAINT df_rights_created_at DEFAULT (SYSUTCDATETIME())
);
GO

-- ---------------------------------------------------------------------
-- Access Requests
-- ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.access_requests', N'U') IS NULL
CREATE TABLE dbo.access_requests (
    id                INT IDENTITY(1,1) NOT NULL CONSTRAINT pk_access_requests PRIMARY KEY,
    requester_id      INT            NOT NULL CONSTRAINT fk_requests_requester REFERENCES dbo.users (id),
    right_id          INT            NOT NULL CONSTRAINT fk_requests_right REFERENCES dbo.rights (id),
    approver_id       INT            NOT NULL CONSTRAINT fk_requests_approver REFERENCES dbo.users (id),
    reason            NVARCHAR(1000) NOT NULL,
    end_date          DATE           NULL,
    status            NVARCHAR(20)   NOT NULL,
    decided_by_id     INT            NULL CONSTRAINT fk_requests_decided_by REFERENCES dbo.users (id),
    decided_at        DATETIME2      NULL,
    decision_comment  NVARCHAR(1000) NULL,
    created_at        DATETIME2      NOT NULL CONSTRAINT df_requests_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at        DATETIME2      NULL,
    version           INT            NOT NULL CONSTRAINT df_requests_version DEFAULT (0),
    CONSTRAINT ck_requests_status CHECK (status IN (N'Pending', N'Approved', N'Rejected', N'Cancelled')),
    CONSTRAINT ck_requests_reject_reason CHECK (
        status <> N'Rejected' OR (decision_comment IS NOT NULL AND LEN(LTRIM(RTRIM(decision_comment))) > 0)),
    CONSTRAINT ck_requests_not_self_decided CHECK (decided_by_id IS NULL OR decided_by_id <> requester_id)
);
GO

-- BR-04: คำขอ Pending ของผู้ใช้ + สิทธิเดียวกันได้ครั้งละ 1 รายการ
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_access_requests_one_pending')
CREATE UNIQUE INDEX ux_access_requests_one_pending
    ON dbo.access_requests (requester_id, right_id) WHERE status = N'Pending';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_access_requests_requester')
CREATE INDEX ix_access_requests_requester ON dbo.access_requests (requester_id, status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_access_requests_approver')
CREATE INDEX ix_access_requests_approver ON dbo.access_requests (approver_id, status);
GO

-- ---------------------------------------------------------------------
-- ประวัติ (BR-10) - เพิ่มได้อย่างเดียว
-- ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.request_histories', N'U') IS NULL
CREATE TABLE dbo.request_histories (
    id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_request_histories PRIMARY KEY,
    access_request_id  INT            NOT NULL CONSTRAINT fk_histories_request REFERENCES dbo.access_requests (id),
    action             NVARCHAR(20)   NOT NULL CONSTRAINT ck_histories_action
                       CHECK (action IN (N'Created', N'Edited', N'Cancelled', N'Approved', N'Rejected')),
    actor_id           INT            NOT NULL CONSTRAINT fk_histories_actor REFERENCES dbo.users (id),
    comment            NVARCHAR(1000) NULL,
    created_at         DATETIME2      NOT NULL CONSTRAINT df_histories_created_at DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_request_histories_request')
CREATE INDEX ix_request_histories_request ON dbo.request_histories (access_request_id, created_at);
GO

CREATE OR ALTER TRIGGER dbo.trg_request_histories_readonly
ON dbo.request_histories
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 50010, N'request_histories เป็นข้อมูลประวัติ แก้ไขหรือลบไม่ได้ (BR-10)', 1;
END;
GO

-- ---------------------------------------------------------------------
-- การแจ้งเตือน
-- ---------------------------------------------------------------------
IF OBJECT_ID(N'dbo.notifications', N'U') IS NULL
CREATE TABLE dbo.notifications (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_notifications PRIMARY KEY,
    user_id     INT            NOT NULL CONSTRAINT fk_notifications_user REFERENCES dbo.users (id) ON DELETE CASCADE,
    title       NVARCHAR(200)  NOT NULL,
    message     NVARCHAR(1000) NOT NULL,
    link        NVARCHAR(300)  NULL,
    is_read     BIT            NOT NULL CONSTRAINT df_notifications_is_read DEFAULT (0),
    created_at  DATETIME2      NOT NULL CONSTRAINT df_notifications_created_at DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_notifications_user')
CREATE INDEX ix_notifications_user ON dbo.notifications (user_id, is_read, created_at DESC);
GO

-- ---------------------------------------------------------------------
-- Views
-- ---------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.v_active_rights AS
SELECT r.id          AS request_id,
       u.username,
       u.full_name,
       rt.name       AS right_name,
       r.decided_at  AS approved_at,
       r.end_date
FROM dbo.access_requests r
JOIN dbo.users  u  ON u.id  = r.requester_id
JOIN dbo.rights rt ON rt.id = r.right_id
WHERE r.status = N'Approved'
  AND (r.end_date IS NULL OR r.end_date >= CAST(GETDATE() AS DATE));
GO

CREATE OR ALTER VIEW dbo.v_decision_log AS
SELECT h.created_at  AS decided_at,
       h.action,
       a.full_name   AS decided_by,
       u.full_name   AS requester,
       rt.name       AS right_name,
       h.comment,
       r.id          AS request_id
FROM dbo.request_histories h
JOIN dbo.access_requests r ON r.id  = h.access_request_id
JOIN dbo.users  a  ON a.id  = h.actor_id
JOIN dbo.users  u  ON u.id  = r.requester_id
JOIN dbo.rights rt ON rt.id = r.right_id
WHERE h.action IN (N'Approved', N'Rejected');
GO
