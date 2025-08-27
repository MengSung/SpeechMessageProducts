# 高鉅金流 PayPage 交易完成回傳資訊實作策略與部署指南

## 專案概述

本專案實現了高鉅金流 PayPage 的「交易完成回傳資訊」功能，當用戶完成付款後，高鉅金流會將交易結果回傳到指定的回調網址，系統會自動處理這些資訊並更新 Dynamics 365 CRM。

## 已實作的功能

### 1. 資料模型 (`MyPayReturnModel.cs`)
- 建立了完整的回傳資料模型，對應高鉅金流官方文檔的欄位規格
- 包含必要欄位：`state`, `msg`, `order_id`, `store_uid`, `transaction_id`, `hash`
- 包含可選欄位：`cost`, `user_name` 等
- 相容於 .NET Framework 4.7.1

### 2. 控制器 (`MyPayController.cs`)
- **POST /api/MyPay/return**: 接收高鉅金流的交易完成回傳資訊
- **GET /api/MyPay/success**: 付款成功頁面
- **GET /api/MyPay/failure**: 付款失敗頁面
- 包含完整的錯誤處理和日誌記錄
- 相容於 .NET Framework 4.7.1 的 ASP.NET Core

### 3. 業務邏輯處理 (`QPayProcessor.cs`)
新增了三個核心方法：

#### `VerifyMyPayHash(MyPayReturnModel returnModel)`
- 驗證高鉅金流回傳的 Hash 簽名
- 使用 SHA256 算法，組合規則：`KEY + transaction_id + order_id + state + IV`
- 確保資料來源的合法性

#### `ProcessMyPayReturn(MyPayReturnModel returnModel)`
- 主要的回傳處理邏輯
- 實作冪等性處理，避免重複處理同一筆交易
- 支援收費單和認獻單兩種實體類型
- 根據交易結果調用不同的處理方法

#### `ProcessSuccessfulMyPayReturn()` / `ProcessFailedMyPayReturn()`
- 分別處理成功和失敗的交易
- 更新 Dynamics 365 中的付款狀態、實收金額、備註等
- 發送 LINE 通知給付款人

### 4. 前端頁面 (`PaymentResult.cshtml`)
- 統一的付款結果顯示頁面
- 根據成功/失敗狀態顯示不同的內容和樣式
- 提供返回首頁和查看奉獻記錄的連結
- 相容於 .NET Framework 4.7.1 的 Razor 語法

### 5. 設定檔更新 (`appsettings.json`)
- 新增高鉅金流必要的 `Key` 和 `IV` 設定
- 更新 `SuccessReturl` 和 `FailureReturl` 指向新的控制器
- 在 `SetRawDataProperties` 中新增 `notify_url` 設定

## 部署前準備

### 1. Dynamics 365 CRM 設定

**重要：需要在 CRM 中新增以下欄位**

**收費單 (new_fee) 實體：**
- `new_mypay_transaction_id` (單行文字，長度：100) - 儲存高鉅金流交易單號
- 確認已存在欄位：
  - `new_pay_status` (選項組) - 付款狀態
  - `new_fee_really_paid` (貨幣) - 實收金額
  - `new_explain` (多行文字) - 備註
  - `new_pay_date` (日期時間) - 付款日期

**認獻單 (new_dedication_booking) 實體：**
- `new_mypay_transaction_id` (單行文字，長度：100) - 儲存高鉅金流交易單號
- 確認已存在欄位：
  - `new_dedication_booking_status` (選項組) - 認獻單狀態（需包含「已啟動」選項，值：100000001）
  - `new_explain` (多行文字) - 備註

**聯絡人 (contact) 實體：**
- 確認已存在欄位：
  - `new_lineid` (單行文字) - LINE ID（用於推播通知）

### 2. 高鉅金流後台設定

在高鉅金流商店後台設定中，需要配置以下 URL：

- **交易成功導向網址**: `https://sunnyvalech.speechmessage.com.tw:603/api/MyPay/success`
- **交易失敗導向網址**: `https://sunnyvalech.speechmessage.com.tw:603/api/MyPay/failure`
- **後端回調網址**: `https://sunnyvalech.speechmessage.com.tw:603/api/MyPay/return`

### 3. appsettings.json 設定

需要向高鉅金流申請並設定實際的金鑰：

```json
{
  "MyPay": {
    "Key": "實際的高鉅金流 API Key",
    "IV": "實際的高鉅金流 IV 值",
    "Store_Id": "實際的商店代號",
    "SuccessReturl": "https://sunnyvalech.speechmessage.com.tw:603/api/MyPay/success",
    "FailureReturl": "https://sunnyvalech.speechmessage.com.tw:603/api/MyPay/failure"
  }
}
```

## 技術相容性

### .NET Framework 4.7.1 相容性考量
- 移除了 null-conditional operator (`?.`) 在 Razor 視圖中的使用
- 使用傳統的 null 檢查語法
- Controller 繼承自 `Controller` 而非 `ControllerBase`
- 使用 `ViewBag.IsSuccess` 傳遞布林值，避免在視圖中處理中文字符比較

### Razor 語法調整
- 使用分離的 `@{}` 程式碼區塊
- 避免在條件判斷中直接使用包含中文字符的字串比較
- 改為在 Controller 中預處理邏輯

## 安全性考量

### 1. 簽名驗證
- 所有回調都必須通過 Hash 簽名驗證
- 驗證失敗的請求會被拒絕並記錄

### 2. 冪等性處理
- 使用 `transaction_id` 避免重複處理同一筆交易
- 確保系統的資料一致性

### 3. 錯誤處理
- 完整的異常捕獲和日誌記錄
- 失敗時不會影響系統穩定性

### 4. HTTPS 要求
- 所有通訊必須使用 HTTPS
- 確保資料傳輸安全

## 測試建議

### 1. 單元測試
- 測試 Hash 驗證功能的正確性
- 測試冪等性處理邏輯
- 測試不同交易狀態的處理

### 2. 整合測試
- 使用高鉅金流測試環境進行完整流程測試
- 驗證 CRM 資料更新的正確性
- 測試 LINE 通知功能

### 3. 負載測試
- 測試高併發回調處理能力
- 確保系統在高負載下的穩定性

## 監控與維運

### 1. 日誌監控
- 監控回調處理的成功率
- 追蹤驗證失敗的情況
- 記錄系統異常

### 2. 告警設定
- 設定回調失敗率過高的告警
- 監控 CRM 更新失敗的情況
- 設定系統異常告警

### 3. 備份與復原
- 定期備份交易資料
- 建立災難復原計畫

## 故障排除

### 常見問題

1. **驗證失敗**
   - 檢查 Key 和 IV 設定是否正確
   - 確認簽名計算邏輯是否符合官方文檔

2. **找不到對應實體**
   - 檢查 order_id 是否為有效的 Guid
   - 確認 CRM 中確實存在對應的記錄

3. **LINE 通知失敗**
   - 檢查聯絡人是否有設定 LINE ID
   - 確認 LINE Bot 設定是否正確

4. **Razor 視圖編譯錯誤**
   - 確保所有引號都是標準的 ASCII 引號
   - 避免在視圖中使用 null-conditional operator
   - 檢查中文字符的編碼問題

## 部署檢查清單

### 程式碼部署
- [x] 建置成功，無編譯錯誤
- [x] MyPayReturnModel.cs 已建立
- [x] MyPayController.cs 已建立並設定正確路由
- [x] QPayProcessor.cs 已新增回傳處理方法
- [x] PaymentResult.cshtml 已建立並相容於 .NET Framework 4.7.1
- [x] appsettings.json 已更新設定結構

### CRM 設定
- [ ] 在 new_fee 實體新增 new_mypay_transaction_id 欄位
- [ ] 在 new_dedication_booking 實體新增 new_mypay_transaction_id 欄位
- [ ] 確認付款狀態選項組包含所需的值
- [ ] 確認認獻單狀態選項組包含「已啟動」選項

### 金流平台設定
- [ ] 在高鉅金流後台設定回調 URL
- [ ] 申請並設定實際的 API Key 和 IV
- [ ] 測試環境驗證

### 網路設定
- [ ] 確認防火牆允許入站連線
- [ ] 確認 HTTPS 憑證正確設定
- [ ] 確認 URL 可從外部訪問

## 後續擴展

### 1. 功能擴展
- 支援更多付款方式
- 實作自動對帳功能
- 增加退款處理

### 2. 效能優化
- 實作快取機制
- 使用訊息佇列處理高併發
- 優化資料庫查詢

### 3. 管理功能
- 建立交易管理後台
- 增加報表統計功能
- 實作手動重試機制

## 總結

本實作提供了完整的高鉅金流 PayPage 交易回傳處理功能，並已解決 .NET Framework 4.7.1 的相容性問題。程式碼已成功建置，接下來需要完成 CRM 欄位設定和金流平台設定，即可投入生產使用。

**重要提醒：部署前請務必完成 CRM 欄位新增和金流平台設定，並進行充分的測試。**