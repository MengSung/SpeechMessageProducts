# Notifications

`MemberNotificationService` 是 GoodShepherd 跨領域單人通知的共用組合入口。它重讀會友與來源教會的正式關聯、驗證 LINE 綁定、解析語言、透過字典組訊息與 sender；Live 解密當次頻道封包後委派 `LineMessagingProcessor.Core`，Shadow 只記有界摘要。

## 活動與外送的主機代送

`MemberNotifyRelay` 對應第四種待辦 `100000003`。三個沒有安全設定的活動與外送 Plugin 在 Live 只入列 `kind`、`recordId`、`message`、`route`，不保存流程文字、個資或秘密，也不在沙箱推播；有安全設定的其他 Plugin 繼續原有直接通知路徑。

教會從學員、申請人、繳費會友或寄件者的正式關聯取得，不能由收件人反推。活動入列來源 ID 包含 OperationId（空時 CorrelationId），外送沿 activityId；資料庫替代鍵負責跨程序防重，不覆寫已存在待辦的狀態或 payload。

Host 重新解析封閉四欄並驗來源教會，server 組織 ID 取自同一 SDK 的 WhoAmI。規則在解密前再次核對來源，與原入口共用收件人、訊息及 Retry Key。永久拒絕結束；429、5xx、傳輸失敗以 `MemberNotifyRelayDeliveryException` 保留實際狀態及原始 inner，後續 Host adapter 接工作者重試契約。課程付款分支仍在查詢前拒絕。

SDK、push、key 由呼叫端擁有；單筆 await 排空後才釋放 scope 及清零 key。沒有跨教會快取、static 可變狀態或脫離呼叫的工作。雙目標合成 SDK／HTTP 及部分信任測試屬離線證據，實機送達須另於 C-b 與全流程演練驗收。
