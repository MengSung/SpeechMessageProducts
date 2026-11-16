# Fees

本模組提供收費、撥款純規則、受教會範圍約束的資料讀寫，以及 P5-11 觸發與通知組合。

## 階段 5 搬移範圍

- P5-11：盤點舊 `AllotPlugIn`、`AllotWorkflow`、`AllotTlsWorkflow`、`FeePushLineWorkflow`。
- `FeeAllotPlugin`／`FeeAllotActivity` 建立每日 `AllotQuery` 待辦，sourceId 由正式 feeId 與教會當地查詢日決定，payload 精確只有 feeId／queryDate；未來繳費日不入列。真正撥款查詢及四欄條件式回寫仍由 P4 Host Handler 負責。
- `FeeNotifyActivity` 只在舊可達 ExecuteWorkflow 分支記錄通知，Retry Key=feeId；`FeeNotificationRule` 保留舊文字與捨入，`FeeNotificationService` 使用共用通知入口重驗正式收件者／教會，讀語系與頻道身分但完全不讀 token。
- 依 P5-PROP-006，通知 Live 在任何查詢前拒絕；Shadow 完整組合訊息，只輸出安全摘要，不代表送達。課程付款 fallback 不由本模組補做。
- `Golden/Legacy/fees/` 六案為 reading／simulation，未經舊程式實跑；`live=deferred`、`differences=null`，不能稱實機差異零。每日待辦另由兩目標固定向量及服務／外殼測試保護。

金額與幣別依階段 4 的 `Money` 契約；測試須涵蓋冪等、重試及不跨教會使用設定。此目錄不持有金流憑證，也不代表已連接永豐測試環境。
