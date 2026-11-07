# Messaging

本模組是外送訊息及群發規則的預定位置；P2-02 尚未移植舊 Plugin 或建立訊息發布行為。

## 階段 5 搬移範圍

- P5-12：盤點舊 `LineMessagePlugIn`（含多份副本），新外殼為 `OutboundLetterPlugin`。
- `OutboundLetterRule` 僅處理 letter 的「外送」方向，直接單人推播或依設計支援快速行銷活動群發。
- P2-06 合併的 `PushUtility`、`ReplyUtility`、`LineUtilityClass` 只保留規劃允許的相容轉接，不能在此重建第二套傳輸實作。

須先保存各公開方法的 HTTP 黃金資料，再逐項驗證相容差異。舊程式中以姓名加手機綁定身分的流程不移植；通知傳輸委派共用核心，不保存跨請求收件者或憑證狀態。
