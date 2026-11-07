# Members

本模組是會友領域規則的預定位置；P2-02 尚未查詢、快取或保存任何會友資料。

## 階段 5 搬移範圍

- P5-04：新增 `MemberLanguageChangedPlugin` 的規則協作者 `LanguageRule`，處理 `new_preferredlanguage` 更新後的語言規則與宮格切換契約。
- 會友及 LINE 綁定資料的最小欄位查詢由 P2-04 的 `ToolUtility.Core` 查詢介面負責；通知組合由 `Notifications/MemberNotificationService` 負責。

語言與授權必須由伺服器可信資料決定，不接受呼叫端提供的身分或教會作為授權依據。任何快取都須在完整隔離鍵、有界期限及確定清理契約明確後才可加入；目前沒有快取。
