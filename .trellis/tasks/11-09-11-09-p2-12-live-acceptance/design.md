# P2-12 設計

## 邊界

P2-12 只在 `goodshepherdback` 驗收已完成的 P2-11 member Plugin、P2-07 Worker、P2-08 RichMenu 與既有 P2-04 Channel 能力。實機資料全部加固定測試標記，清理以本次建立的正式 ID 為準，不依名稱猜測或刪除他人資料。

## Deployment member 契約

`StepRegistration.UnsecureConfig` 的 member 形狀為：

```json
{"mode":"member","culture":"qps-ploc","sourceChurchId":"<D格式GUID>","allowPseudoLocale":true}
```

`SecureConfig` 只接受完整的 `${secret:TokenKey}` 佔位。`sourceChurchId` 是部署者核准的測試教會正式 ID，不是由 task 或 Contact 輸入推導的授權；Plugin 執行時仍以 D365 組織、task 關聯 Contact、Contact 所屬教會與該教會 Channel 做完整核對。

既有 `trace`／`push` 契約與 `verify` 的秘密未比對回傳碼保留不變。member 不新增 smoke 情境，實機結果以 task 的 Plugin Trace 與 LINE HTTP 受理資料判定。

## 實機資料流

1. export 現況並保存本機備份索引。
2. 以測試 Channel 的 TokenKey 解密材料執行 `protect-token`，再以 `assign-channel` 指派測試教會 A。
3. 建立測試教會 A、B、各自測試 Contact；A 的 Contact 綁定測試 `TestLineUserId`，偏好語言設為 `qps-ploc`。
4. 以 task 的 `regardingobjectid` 觸發 member Plugin，追蹤只檢查固定欄位與 template key，不保存訊息本文或 token。
5. 以 B 的 Contact 觸發錯教會案例，確認 `ChurchMismatch` 且沒有 HTTP 請求；重跑同一 task 確認 Retry Key 不增加第二次受理。
6. 建立一筆 RichMenu 待辦，以兩個獨立 Worker 同時執行，查詢正式 task ID 與狀態版本，確認只成功認領一次、只產生一次候選。
7. 停用步驟、刪除本次測試資料與指派，恢復追蹤設定，重新 export／唯讀盤點，殘留必須為 0。

## 失敗與清理

任何步驟失敗都先保存不含秘密的摘要，再依既有 ID 執行清理；不以清單空白或名稱搜尋冒充殘留 0。Token、密碼、Channel Secret、TokenKey、LINE userId 與訊息本文不寫入證據。
