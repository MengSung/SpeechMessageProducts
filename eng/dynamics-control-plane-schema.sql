SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SpeechMessageDynamicsControlPlane'
    THROW 51001, 'This schema may run only in SpeechMessageDynamicsControlPlane.', 1;
IF DB_NAME() IN (N'MSCRM_CONFIG', N'Jesus_MSCRM')
    THROW 51002, 'Dynamics CRM databases are forbidden targets.', 1;

IF OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') IS NULL
    EXEC(N'CREATE SEQUENCE dbo.RuntimeHostFencingSequence AS bigint START WITH 1 INCREMENT BY 1 CACHE 1000;');

IF OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RuntimeHostAdmissionEpoch
    (
        LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AdmissionEpoch bigint NOT NULL,
        ConfigurationDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MaximumRuntimeHosts int NOT NULL,
        LastUpdatedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_RuntimeHostAdmissionEpoch_LastUpdated DEFAULT (SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_RuntimeHostAdmissionEpoch PRIMARY KEY (LeaseNamespaceId),
        CONSTRAINT CK_RuntimeHostAdmissionEpoch_Epoch CHECK (AdmissionEpoch >= 1),
        CONSTRAINT CK_RuntimeHostAdmissionEpoch_MaxHosts CHECK (MaximumRuntimeHosts >= 1)
    );
END;

IF OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RuntimeHostSlotLease
    (
        LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SlotOrdinal int NOT NULL,
        AdmissionEpoch bigint NOT NULL,
        ConfigurationDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        HostInstanceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        FencingToken bigint NOT NULL CONSTRAINT DF_RuntimeHostSlotLease_Fencing DEFAULT (0),
        LeaseExpiresAtUtc datetime2(3) NULL,
        QuarantineUntilUtc datetime2(3) NULL,
        LastTouchedAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_RuntimeHostSlotLease_LastTouched DEFAULT (SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_RuntimeHostSlotLease PRIMARY KEY (LeaseNamespaceId, SlotOrdinal),
        CONSTRAINT CK_RuntimeHostSlotLease_SlotOrdinal CHECK (SlotOrdinal >= 0)
    );
END;

IF COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'AdmissionEpoch') IS NULL
    ALTER TABLE dbo.RuntimeHostSlotLease ADD AdmissionEpoch bigint NOT NULL
        CONSTRAINT DF_RuntimeHostSlotLease_AdmissionEpoch DEFAULT (1) WITH VALUES;
IF COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'ConfigurationDigest') IS NULL
    ALTER TABLE dbo.RuntimeHostSlotLease ADD ConfigurationDigest char(64) NOT NULL
        CONSTRAINT DF_RuntimeHostSlotLease_ConfigurationDigest DEFAULT (REPLICATE('0', 64)) WITH VALUES;

-- 既有 LocalDB 可能在預設大小寫不敏感定序下建立過租約表；provisioning 必須先重建主鍵再改成 BIN2，
-- 讓資料庫的 namespace／host／digest 相等規則與 C# 的 ordinal 結構化 key 完全一致，避免不同租約互相誤認。
DECLARE @epochPrimaryKey sysname;
DECLARE @epochDropConstraintSql nvarchar(max);
SELECT @epochPrimaryKey = keyConstraint.name
FROM sys.key_constraints AS keyConstraint
WHERE keyConstraint.parent_object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
  AND keyConstraint.type = N'PK';

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
      AND name = N'LeaseNamespaceId'
      AND collation_name <> N'Latin1_General_100_BIN2'
)
BEGIN
    IF @epochPrimaryKey IS NOT NULL
    BEGIN
        SET @epochDropConstraintSql = N'ALTER TABLE dbo.RuntimeHostAdmissionEpoch DROP CONSTRAINT ' + QUOTENAME(@epochPrimaryKey) + N';';
        EXEC(@epochDropConstraintSql);
    END;

    ALTER TABLE dbo.RuntimeHostAdmissionEpoch
        ALTER COLUMN LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL;
    ALTER TABLE dbo.RuntimeHostAdmissionEpoch
        ADD CONSTRAINT PK_RuntimeHostAdmissionEpoch PRIMARY KEY (LeaseNamespaceId);
END
ELSE IF @epochPrimaryKey IS NULL
    ALTER TABLE dbo.RuntimeHostAdmissionEpoch
        ADD CONSTRAINT PK_RuntimeHostAdmissionEpoch PRIMARY KEY (LeaseNamespaceId);

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
      AND name = N'ConfigurationDigest'
      AND collation_name <> N'Latin1_General_100_BIN2'
)
    ALTER TABLE dbo.RuntimeHostAdmissionEpoch
        ALTER COLUMN ConfigurationDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL;

DECLARE @slotPrimaryKey sysname;
DECLARE @slotDropConstraintSql nvarchar(max);
SELECT @slotPrimaryKey = keyConstraint.name
FROM sys.key_constraints AS keyConstraint
WHERE keyConstraint.parent_object_id = OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U')
  AND keyConstraint.type = N'PK';

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U')
      AND name = N'LeaseNamespaceId'
      AND collation_name <> N'Latin1_General_100_BIN2'
)
BEGIN
    IF @slotPrimaryKey IS NOT NULL
    BEGIN
        SET @slotDropConstraintSql = N'ALTER TABLE dbo.RuntimeHostSlotLease DROP CONSTRAINT ' + QUOTENAME(@slotPrimaryKey) + N';';
        EXEC(@slotDropConstraintSql);
    END;

    ALTER TABLE dbo.RuntimeHostSlotLease
        ALTER COLUMN LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL;
    ALTER TABLE dbo.RuntimeHostSlotLease
        ADD CONSTRAINT PK_RuntimeHostSlotLease PRIMARY KEY (LeaseNamespaceId, SlotOrdinal);
END
ELSE IF @slotPrimaryKey IS NULL
    ALTER TABLE dbo.RuntimeHostSlotLease
        ADD CONSTRAINT PK_RuntimeHostSlotLease PRIMARY KEY (LeaseNamespaceId, SlotOrdinal);

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U')
      AND name = N'ConfigurationDigest'
      AND collation_name <> N'Latin1_General_100_BIN2'
)
    ALTER TABLE dbo.RuntimeHostSlotLease
        ALTER COLUMN ConfigurationDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U')
      AND name = N'HostInstanceId'
      AND collation_name <> N'Latin1_General_100_BIN2'
)
    ALTER TABLE dbo.RuntimeHostSlotLease
        ALTER COLUMN HostInstanceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL;

-- 此表保留 namespace 到實體 Organization 的長期、非機密繫結；slot 釋放後不可刪除，
-- 否則下一個程序可用另一個 namespace 重建同一個組織的容量預算。
IF OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RuntimeHostOrganizationBinding
    (
        LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ExpectedOrganizationId uniqueidentifier NOT NULL,
        NormalizedOrganizationBaseUri nvarchar(450) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BoundAtUtc datetime2(3) NOT NULL
            CONSTRAINT DF_RuntimeHostOrganizationBinding_BoundAtUtc DEFAULT (SYSUTCDATETIME()),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_RuntimeHostOrganizationBinding PRIMARY KEY (LeaseNamespaceId),
        CONSTRAINT UQ_RuntimeHostOrganizationBinding_ExpectedOrganizationId UNIQUE (ExpectedOrganizationId),
        CONSTRAINT UQ_RuntimeHostOrganizationBinding_NormalizedOrganizationBaseUri UNIQUE (NormalizedOrganizationBaseUri)
    );
END;

-- 舊版 coordinator 不知道 canonical binding，若還有 epoch row 便不能安全推回它實際指向哪一個 Organization。
-- provisioning 在此明確停下來要求先 drain/清理或由受控作業人工建立映射，而不是猜測後繼續 rollout。
IF EXISTS
(
    SELECT 1
    FROM dbo.RuntimeHostAdmissionEpoch AS epochRow
    LEFT JOIN dbo.RuntimeHostOrganizationBinding AS bindingRow
        ON bindingRow.LeaseNamespaceId = epochRow.LeaseNamespaceId
    WHERE bindingRow.LeaseNamespaceId IS NULL
)
    THROW 51006, 'Existing admission epochs are missing canonical organization bindings; drain and migrate them before enabling the durable coordinator.', 1;

-- FK 讓尚未升級的 binary 無法先寫入沒有 canonical binding 的 epoch；
-- 新 acquire transaction 會先插入/驗證 binding，接著才建立 epoch，因此舊新版本混跑只會安全 fail-closed。
IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U')
      AND referenced_object_id = OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U')
      AND name = N'FK_RuntimeHostAdmissionEpoch_OrganizationBinding'
)
    ALTER TABLE dbo.RuntimeHostAdmissionEpoch
        ADD CONSTRAINT FK_RuntimeHostAdmissionEpoch_OrganizationBinding
        FOREIGN KEY (LeaseNamespaceId)
        REFERENCES dbo.RuntimeHostOrganizationBinding (LeaseNamespaceId);

SELECT DB_NAME() AS DatabaseName,
       OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') AS LeaseTableObjectId,
       OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') AS AdmissionEpochTableObjectId,
       OBJECT_ID(N'dbo.RuntimeHostOrganizationBinding', N'U') AS OrganizationBindingTableObjectId,
       OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') AS FencingSequenceObjectId,
       SYSUTCDATETIME() AS ServerUtc;
