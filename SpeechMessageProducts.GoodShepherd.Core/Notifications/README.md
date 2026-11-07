# Notifications

本模組是 GoodShepherd 跨領域通知組合的唯一預定入口；P2-02 僅建立服務型別骨架，沒有通知能力。

## 後續工作與邊界

- P2-04：由 `MemberNotificationService` 組合會友／教會／Channel 最小查詢、教會一致性檢查、語言選擇、資源訊息組裝與單人推播。
- P5-04～P5-12 的通知規則可依正式契約呼叫此服務；送訊協定、Retry Key 與 Channel 教會檢查由 `LineMessagingProcessor.Core` 負責。
- P2-02 不預設 `NotifyAsync` 參數、不查 D365、不解密或保存 Token，也不連 LINE。

實際實作須以雙目標契約測試證明未綁定拒絕、教會隔離、Retry Key、語言與平行請求不串流；資源 client 的唯一 owner、取消／逾時與 Dispose 順序須由具體實作文件說明。
