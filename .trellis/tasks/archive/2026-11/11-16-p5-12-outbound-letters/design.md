# P5-12 設計

## 資料與傳輸邊界
OutboundLetterPlugin 只接 letter Create／Update 的非同步 PostOperation；沿用 DepthPolicy(2, Create)。Live 在任何 SDK 查詢前依 P5-PROP-006 拒絕；Shadow 交 OutboundLetterRule 讀正式資料並記意圖。規則與傳輸分類可離線注入既有 MemberNotificationService 驗證，不另造 HTTP 管道。活動來自資料庫的正式 ID，來源教會由正式關聯核對，不能以收件者自身教會當來源授權。多收件者明確拒絕，QuickCampaign 的各活動獨立執行，不能以顯示姓名去重。

狀態／失敗欄位先從現有設計與 metadata 確認，再實作最小回寫，不猜欄位；ShadowRecorded 不是成功，不把影子結果寫成已送達。重試必須保留原傳輸例外，呼叫端取消與內部逾時分開。

## Hosting 邊界
FailureSummary 預設停用且不註冊 timer 或 consumer；啟用後每輪獨立 DI scope，以受信任設定的組織／教會讀取封閉時間窗筆數，不讀內文。共用 ExceptionDiagnostics 唯一負責落檔與通知順序。每窗單次通知、bounded 範圍數與逾時，停止先取消、等待 work 排空，再 Dispose scope／timer／CTS；無使用者 Session、憑證或跨窗實體快取。

## 驗證與發布
先紅燈後產品實作，小段綠燈才提交，整包驗收前不 push。包內主會話 inline 檢查，不派實作子代理或外審；階段末一次外審。失敗彙總來源必須真正接入而非註冊 no-op。若資料模型已有缺口，如實登錄不假稱完成。

## 已查證的狀態及來源契約

2026-11-16 14:01 Asia/Taipei (UTC+08:00)：goodshepherdback 唯讀 metadata 確認 letter.activityid、完成1/4、取消2/5及兩個250字分類欄位。Shadow 以固定 SMV category 加字典 subcategory 區分失敗，不覆寫 description、不搶用外部既有類別。來源只接受 from account 或 from contact.parentcustomerid；systemuser 沒有正式教會關聯，拒絕而不從 to 反推。其他媒體／群組及任意行銷來源列入階段末缺口，不能宣稱全部舊功能相容。詳細逐檔表見 wp-reports/P5-12-inventory.md。

## 失敗彙總實作決策

2026-11-16 14:32 Asia/Taipei (UTC+08:00)：Enabled 缺省 false，不發布 source／worker；IntervalMinutes 缺省5、限制1～60，Threshold 缺省10、限制0～50000，嚴格大於才通知。LineWebhook 使用 Channels 啟動快照，限64間、單組織、每教會單頻道，WhoAmI 及 account→channel 每輪重驗。來源只聚合活動主鍵，不讀個資或本文；方向外送、取消2/5、固定三個 SMV 失敗 category，sender account 或 contact.parentcustomerid 分教會，modifiedon 為半開 UTC 窗。

共用 diagnostics 新增 GUID／long／UTC 白名單摘要入口，重用原有 flush→bounded LINE queue 與四個輸出組合，不傳 exception.Message 或動態 operation。排程抑制 ExecutionContext，每輪 scope＋30秒合作預算，等待 SDK 真返回才清理；同步 Gateway 的傳輸硬期限沿既有 owner，不用 WaitAsync 遺棄查詢。每次啟動每窗最多嘗試一次、固定容量最後窗記號；沒有跨程序／重啟持久去重帳本，不宣稱 exactly-once，階段末如實列出。

唯讀 goodshepherdback 確認 activityparty.partyobjecttypecode 為 EntityName，account=1／contact=2。初版跨 link alias filter 被9.1拒絕，移到根 filter 後實際合成來源聚合回0、Int32、letter.activityid；此只證明查詢可接受，沒有推播、沒有執行舊程式，也不是實機影子比對。
