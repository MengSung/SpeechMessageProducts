# 會友細節性別與生日設計

## 目標

在會友細節彈窗的基本資料區顯示「性別」與「生日」，讓使用者不必回到會友列表查閱。

## 方案比較

1. **沿用 Detail 的 contact Retrieve（採用）**：在既有欄位集合加入 `gendercode` 與 `birthdate`，同一次 CRM 查詢完成，沒有額外網路往返。
2. 從列表資料傳入彈窗：會讓其他共用會友細節入口缺少資料，而且列表快取可能過期。
3. 另外新增基本資料 API：責任清楚但會增加一次請求及載入時間，本需求不需要。

## 資料與顯示

- `MemberInfoDetailViewModel` 新增 `Gender`（OptionSet 顯示文字）與 nullable `BirthDate`。
- `MemberInfoController.Detail` 在既有 `GetContactDetailColumns()` 加入 `gendercode`、`birthdate`。
- 性別沿用現有 OptionSet metadata 解析，避免硬編碼男女性別代碼。
- 生日只保留有效年份；畫面格式固定為 `yyyy/MM/dd`，不做時區轉換。
- 空值顯示「（未設定）」。

## 版面

在地址／會員身分之後、關係目標之前新增一列唯讀欄位：左側「性別」、右側「生日」。沿用 `.member-info-field`、`.member-info-label` 與 `.member-info-value`；手機版依既有 media query 自動改為單欄。

## 邊界與驗證

- 本次只顯示，不加入性別與生日編輯／上傳行為。
- 不新增 API，不改 Detail 授權判斷。
- 契約測試涵蓋 ViewModel、CRM 欄位、映射與 Razor 顯示；完成後執行全部 MemberInfo 測試與方案建置。
