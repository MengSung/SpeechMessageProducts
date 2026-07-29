SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SpeechMessageDynamicsControlPlane'
    THROW 51001, 'This schema may run only in SpeechMessageDynamicsControlPlane.', 1;
IF DB_NAME() IN (N'MSCRM_CONFIG', N'Jesus_MSCRM')
    THROW 51002, 'Dynamics CRM databases are forbidden targets.', 1;

IF OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') IS NULL
    EXEC(N'CREATE SEQUENCE dbo.RuntimeHostFencingSequence AS bigint START WITH 1 INCREMENT BY 1 CACHE 1000;');

IF OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RuntimeHostSlotLease
    (
        LeaseNamespaceId nvarchar(128) NOT NULL,
        SlotOrdinal int NOT NULL,
        HostInstanceId nvarchar(128) NULL,
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

SELECT DB_NAME() AS DatabaseName,
       OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') AS LeaseTableObjectId,
       OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') AS FencingSequenceObjectId,
       SYSUTCDATETIME() AS ServerUtc;
