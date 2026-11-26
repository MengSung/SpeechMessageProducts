/*
檔案整體說明
目的與定位：F6 共用 LIFF 目前租戶選擇索引的明確部署腳本，只建立各產品平台資料庫中的 dbo.line_current_selection 四欄表，不建立或選擇資料庫。
主要功能與流程：部署者先連到核定的產品測試／平台資料庫，再執行同一交易內的缺表建立與精確 schema 驗證；三段 nvarchar(128) 使用二進位定序及資料庫唯一主鍵，唯一負載是 ContactId。既有不同 schema 明確 THROW，不能靜默遷移或丟資料。
使用邊界與維護：此腳本不授權主體、不存姓名或 token，也不保存任何跨使用者執行狀態；SQL caller 是交易唯一 owner，執行失敗或取消必須回滾／關閉連線。正式程式 InitializeAsync 與此腳本的 schema 必須一致，兩條路徑均由獨立 LocalDB 測試驗證；一般請求不能建表。
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.line_current_selection') IS NULL
BEGIN
    CREATE TABLE dbo.line_current_selection
    (
        product nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        channel_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        line_user_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        contact_id uniqueidentifier NOT NULL,
        CONSTRAINT PK_line_current_selection PRIMARY KEY CLUSTERED(product, channel_key, line_user_id)
    );
END;
DECLARE @table int = OBJECT_ID(N'dbo.line_current_selection', N'U');
IF @table IS NULL
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id=@table
    AND name COLLATE Latin1_General_100_BIN2=N'line_current_selection'
    AND temporal_type=0 AND is_memory_optimized=0)
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@table)<>4
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF (SELECT COUNT(*) FROM sys.columns AS c
    JOIN (VALUES (1,N'product',231,256), (2,N'channel_key',231,256),
                 (3,N'line_user_id',231,256), (4,N'contact_id',36,16)) AS e(ordinal,name,type,length)
      ON c.column_id=e.ordinal AND c.name COLLATE Latin1_General_100_BIN2=e.name COLLATE Latin1_General_100_BIN2
     AND c.system_type_id=e.type AND c.user_type_id=c.system_type_id AND c.max_length=e.length
    WHERE c.object_id=@table AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
      AND c.is_rowguidcol=0 AND c.is_sparse=0 AND c.generated_always_type=0
      AND c.encryption_type IS NULL AND c.default_object_id=0 AND c.rule_object_id=0
      AND (e.ordinal=4 OR c.collation_name COLLATE Latin1_General_100_BIN2=N'Latin1_General_100_BIN2'))<>4
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@table)<>1
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i WHERE i.object_id=@table
    AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
    AND i.has_filter=0 AND i.ignore_dup_key=0
    AND (SELECT COUNT(*) FROM sys.index_columns AS k WHERE k.object_id=i.object_id AND k.index_id=i.index_id)=3
    AND (SELECT COUNT(*) FROM sys.index_columns AS k WHERE k.object_id=i.object_id AND k.index_id=i.index_id
         AND k.key_ordinal=k.column_id AND k.key_ordinal BETWEEN 1 AND 3
         AND k.is_descending_key=0 AND k.is_included_column=0)=3)
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=@table)
    OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=@table)
    OR EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id=@table)
    THROW 51060, 'LIFF selection schema is incompatible.', 1;
COMMIT TRANSACTION;
