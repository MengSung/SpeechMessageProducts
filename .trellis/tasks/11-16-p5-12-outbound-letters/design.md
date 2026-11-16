# P5-12 設計

## 資料與傳輸邊界
OutboundLetterPlugin 只接 letter Create／Update 的非同步 PostOperation；沿用 DepthPolicy(2, Create)。Live 在任何 SDK 查詢前依 P5-PROP-006 拒絕；Shadow 交 OutboundLetterRule 讀正式資料並記意圖。規則與傳輸分類可離線注入既有 MemberNotificationService 驗證，不另造 HTTP 管道。活動來自資料庫的正式 ID，來源教會由正式關聯核對，不能以收件者自身教會當來源授權。多收件者明確拒絕，QuickCampaign 的各活動獨立執行，不能以顯示姓名去重。

狀態／失敗欄位先從現有設計與 metadata 確認，再實作最小回寫，不猜欄位；ShadowRecorded 不是成功，不把影子結果寫成已送達。重試必須保留原傳輸例外，呼叫端取消與內部逾時分開。

## Hosting 邊界
FailureSummary 預設停用且不註冊 timer 或 consumer；啟用後每輪獨立 DI scope，以受信任設定的組織／教會讀取封閉時間窗筆數，不讀內文。共用 ExceptionDiagnostics 唯一負責落檔與通知順序。每窗單次通知、bounded 範圍數與逾時，停止先取消、等待 work 排空，再 Dispose scope／timer／CTS；無使用者 Session、憑證或跨窗實體快取。

## 驗證與發布
先紅燈後產品實作，小段綠燈才提交，整包驗收前不 push。包內主會話 inline 檢查，不派實作子代理或外審；階段末一次外審。失敗彙總來源必須真正接入而非註冊 no-op。若資料模型已有缺口，如實登錄不假稱完成。
