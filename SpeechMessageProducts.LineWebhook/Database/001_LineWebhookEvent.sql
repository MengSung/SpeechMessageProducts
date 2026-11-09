/*
檔案整體說明
目的與定位：由部署者明確執行實作計畫 3.5 附錄 A.8 的平台 Webhook 去重結構，應用程式不自動建表。
主要功能與流程：建立四欄事件表、頻道與事件複合主鍵及接收時間索引；未提交 INSERT 由單一請求連線持有，提交後供其他程序判重，七天前資料由有界清理服務刪除。
使用邊界與維護：只對部署者選定資料庫執行，不含連線或密碼、不擅加 ownership 欄位。不含存在就略過的條件，既有結構衝突必須交部署者診斷；LocalDB 案例實際執行同一檔案並核對欄位及清理結果。交易與連線生命週期由執行端負責，本檔不作授權判定。
*/
CREATE TABLE dbo.LineWebhookEvent
(
    ChannelId       varchar(20)  NOT NULL,
    WebhookEventId  varchar(64)  NOT NULL,
    ReceivedUtc     datetime2(3) NOT NULL CONSTRAINT DF_LineWebhookEvent_ReceivedUtc DEFAULT (sysutcdatetime()),
    IsRedelivery    bit          NOT NULL,
    CONSTRAINT PK_LineWebhookEvent PRIMARY KEY CLUSTERED (ChannelId, WebhookEventId)
);
CREATE INDEX IX_LineWebhookEvent_ReceivedUtc ON dbo.LineWebhookEvent (ReceivedUtc);
