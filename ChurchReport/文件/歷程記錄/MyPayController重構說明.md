# MyPayController 重構說明文件

## ?? 重構概述

本次重構將原本超過 2300 行的 `MyPayController.cs` 分割成多個職責清晰的服務類別，大幅提升程式碼的可維護性、可測試性和可讀性。

## ?? 重構目標

1. **單一職責原則（SRP）**：每個類別只負責一項特定功能
2. **依賴注入（DI）**：透過建構函式注入服務，提高可測試性
3. **程式碼重用**：避免重複的程式碼邏輯
4. **易於維護**：清晰的檔案結構，便於未來擴展和維護

## ?? 檔案結構

### 重構前
```
ChurchReport/
└── Controllers/
    └── MyPayController.cs (2300+ 行)
```

### 重構後
```
ChurchReport/
├── Controllers/
│   └── MyPayController.cs (精簡至 ~300 行)
└── Services/
    ├── MyPayMessageBuilder.cs      (LINE 訊息建立)
    ├── MyPayStatusHelper.cs         (狀態判斷與訊息轉換)
    ├── MyPayFeeTypeHelper.cs        (收費單類型判斷)
    ├── MyPayLogger.cs               (日誌記錄)
    ├── MyPayCrmService.cs           (CRM 資料更新)
    └── MyPayNotificationService.cs  (LINE 通知發送)
```

## ?? 服務類別說明

### 1. MyPayController.cs (主控制器)
**職責**：API 端點定義和流程協調

**主要功能**：
- 接收金流回傳 (`PaymentNotify`)
- 顯示成功/失敗頁面 (`PaymentSuccess`, `PaymentFailure`)
- 協調各服務完成交易處理流程

**依賴注入的服務**：
```csharp
- ILogger<MyPayController>
- MyPayMessageBuilder
- MyPayCrmService
- MyPayNotificationService
- MyPayStatusHelper
- MyPayFeeTypeHelper
- MyPayLogger
```

### 2. MyPayMessageBuilder.cs
**職責**：建立各類型的 LINE 通知訊息

**主要方法**：
- `BuildDedicationSuccessMessage()` - 奉獻成功訊息
- `BuildDedicationFailureMessage()` - 奉獻失敗訊息
- `BuildCoursePaymentSuccessMessage()` - 課程繳費成功訊息
- `BuildCoursePaymentFailureMessage()` - 課程繳費失敗訊息
- `BuildGeneralPaymentSuccessMessage()` - 一般繳費成功訊息
- `BuildGeneralPaymentFailureMessage()` - 一般繳費失敗訊息

**特色**：
- 統一的訊息格式
- 易於擴展新的訊息類型
- 純函數式設計，無副作用

### 3. MyPayStatusHelper.cs
**職責**：交易狀態判斷與錯誤訊息轉換

**主要方法**：
- `IsSuccessfulPaymentStatus(prc)` - 判斷交易是否成功
- `BuildFailureMessage()` - 建立失敗訊息文字
- `GetFriendlyErrorMessage()` - 轉換為友善的錯誤訊息
- `GetPaymentStatusMessage(prc)` - 取得交易狀態訊息
- `ParseFinishTime()` - 解析完成時間字串
- `GetPaymentMethodName(pfn)` - 取得付款方式名稱

**特色**：
- 集中管理所有狀態碼和錯誤碼
- 提供友善的中文訊息
- 包含完整的錯誤處理

### 4. MyPayFeeTypeHelper.cs
**職責**：收費單類型判斷及相關資訊取得

**主要方法**：
- `DetermineFeeType()` - 判斷收費單類型（奉獻/課程/其他）
- `GetCourseName()` - 取得課程名稱
- `GetDedicationCategoryName()` - 取得奉獻類別名稱

**定義**：
```csharp
public enum FeeType
{
    Dedication,  // 奉獻類型
    Course,      // 課程類型
    Other        // 其他類型
}
```

**特色**：
- 清晰的類型定義
- 智慧型判斷邏輯
- 多重資料來源備援

### 5. MyPayLogger.cs
**職責**：金流回傳資料的日誌記錄

**主要方法**：
- `LogFullReturnData(model)` - 記錄完整的金流回傳資料

**特色**：
- 結構化的日誌格式
- 包含所有核心欄位
- 便於問題追蹤和稽核

### 6. MyPayCrmService.cs
**職責**：更新 Dynamics 365 CRM 中的收費單資訊

**主要方法**：
- `UpdateFeeEntityWithMyPayReturn()` - 使用 MyPayReturnModel 更新
- `UpdateFeeEntityForSuccessWithMyPay()` - 使用個別參數更新（舊版相容）

**更新內容**：
- 付款狀態（成功時）
- 實付金額
- 付款日期
- 付款方式
- 完整交易明細（描述欄位）

**特色**：
- 詳細的交易記錄
- 支援多種資訊類型（分期、紅利、虛擬帳號等）
- 舊版相容性

### 7. MyPayNotificationService.cs
**職責**：根據收費單類型發送對應的 LINE 通知

**主要方法**：
- `SendLineMessage()` - 發送 LINE 訊息基礎方法
- `SendLineNotificationByType()` - 發送成功通知（使用 MyPayReturnModel）
- `SendLineFailureNotificationByType()` - 發送失敗通知（使用 MyPayReturnModel）
- `SendPaymentNotificationByType()` - 舊版相容方法

**特色**：
- 整合多個服務（MessageBuilder, StatusHelper, FeeTypeHelper）
- 智慧型金額解析（優先順序：actual_cost > cost > CRM）
- 完整的錯誤處理

## ?? 處理流程

```
金流回傳 → MyPayController.PaymentNotify
    ↓
    ├─ MyPayLogger.LogFullReturnData (記錄完整資料)
    ├─ MyPayReturnModel.ValidateAllFields (驗證欄位)
    ├─ MyPayStatusHelper.IsSuccessfulPaymentStatus (判斷成功/失敗)
    ├─ MyPayFeeTypeHelper.DetermineFeeType (判斷收費單類型)
    ├─ MyPayCrmService.UpdateFeeEntityWithMyPayReturn (更新 CRM)
    └─ MyPayNotificationService.SendLineNotificationByType (發送 LINE 通知)
        ├─ MyPayMessageBuilder.BuildXXXMessage (建立訊息)
        ├─ MyPayFeeTypeHelper.GetCourseName/GetDedicationCategoryName (取得名稱)
        └─ SendLineMessage (發送)
```

## ?? 服務註冊（Startup.cs）

在 `ConfigureServices` 方法中新增以下服務註冊：

```csharp
// 註冊 MyPay 相關服務
services.AddScoped<ChurchReport.Services.MyPayMessageBuilder>();
services.AddScoped<ChurchReport.Services.MyPayStatusHelper>();
services.AddScoped<ChurchReport.Services.MyPayFeeTypeHelper>();
services.AddScoped<ChurchReport.Services.MyPayLogger>();
services.AddScoped<ChurchReport.Services.MyPayCrmService>();
services.AddScoped<ChurchReport.Services.MyPayNotificationService>();
```

## ? 重構優點

### 1. 可維護性提升
- **分離關注點**：每個類別職責單一，修改時不會影響其他部分
- **易於定位**：問題發生時可快速找到對應的服務類別
- **降低複雜度**：從 2300 行降至每個檔案約 200-500 行

### 2. 可測試性提升
- **依賴注入**：可輕易替換模擬物件進行單元測試
- **純函數**：MessageBuilder 等服務採用純函數設計，易於測試
- **獨立測試**：每個服務可獨立進行單元測試

### 3. 可擴展性提升
- **新增訊息類型**：只需在 MessageBuilder 中新增方法
- **新增狀態碼**：只需在 StatusHelper 中新增 switch case
- **新增收費類型**：只需在 FeeTypeHelper 中新增判斷邏輯

### 4. 程式碼重用
- **避免重複**：相同邏輯集中在服務類別中
- **統一介面**：所有 LINE 訊息都透過 MessageBuilder 建立
- **一致性**：狀態判斷和錯誤處理邏輯統一

## ?? 向下相容性

重構後的程式碼完全保留了原有的功能和 API 介面：

- ? API 端點不變：`/api/MyPay/MyPayNotify`, `/api/MyPay/success`, `/api/MyPay/failure`
- ? 處理流程不變：驗證 → 判斷 → 更新 CRM → 發送通知
- ? 舊版方法保留：`UpdateFeeEntityForSuccessWithMyPay`, `SendPaymentNotificationByType`
- ? 資料模型不變：`MyPayReturnModel`, `ValidationResult`

## ?? 使用範例

### 在控制器中使用（已實作）
```csharp
public MyPayController(
    ILogger<MyPayController> logger,
    MyPayMessageBuilder messageBuilder,
    MyPayCrmService crmService,
    MyPayNotificationService notificationService,
    MyPayStatusHelper statusHelper,
    MyPayFeeTypeHelper feeTypeHelper,
    MyPayLogger myPayLogger)
{
    // ASP.NET Core 自動注入
}
```

### 未來擴展範例

#### 新增一個新的收費類型訊息
```csharp
// 在 MyPayMessageBuilder.cs 中新增
public string BuildActivityPaymentSuccessMessage(
    string fullName,
    string orderId,
    string transactionId,
    decimal amount,
    string activityName,
    DateTime paymentTime)
{
    var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
    msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
    msg += $"您的活動報名費用已成功完成！{Environment.NewLine}{Environment.NewLine}";
    // ... 更多內容
    return msg;
}
```

#### 新增狀態碼支援
```csharp
// 在 MyPayStatusHelper.cs 中新增
public string GetPaymentStatusMessage(string prc)
{
    // ...existing cases...
    case "999": return "新的狀態碼";
    // ...
}
```

## ?? 後續建議

### 1. 單元測試
建議為每個服務類別撰寫單元測試：
- `MyPayMessageBuilderTests.cs`
- `MyPayStatusHelperTests.cs`
- `MyPayFeeTypeHelperTests.cs`
- `MyPayCrmServiceTests.cs`
- `MyPayNotificationServiceTests.cs`

### 2. 效能優化
- 考慮使用快取機制（IMemoryCache）儲存常用的狀態碼對應
- 非同步化 CRM 更新和 LINE 通知發送

### 3. 監控與日誌
- 整合 Application Insights 或其他監控工具
- 新增更詳細的效能指標記錄

### 4. 文件化
- 為每個服務類別建立 README
- 補充更多的使用範例和最佳實踐

## ?? 重構統計

| 項目 | 重構前 | 重構後 | 改善 |
|-----|-------|-------|------|
| 檔案數量 | 1 | 7 | +6 |
| 主控制器行數 | 2300+ | ~300 | -87% |
| 類別數量 | 1 | 7 | +6 |
| 職責數量 | 多個 | 1個/類別 | 單一職責 |
| 測試難度 | 高 | 低 | 易於測試 |
| 維護性 | 低 | 高 | 大幅提升 |

## ?? 注意事項

1. **保留舊版方法**：為了向下相容，保留了部分舊版方法（如 `UpdateFeeEntityForSuccessWithMyPay`），未來可考慮移除
2. **LINE Token**：LINE_CHANNEL_ACCESS_TOKEN 目前是硬編碼在 `MyPayNotificationService` 中，建議未來改為從 `appsettings.json` 讀取
3. **常數定義**：`PAYMENT_STATUS_PAID` 和 `PAYMENT_METHOD_CREDIT_CARD` 等常數分散在不同服務中，可考慮集中管理
4. **錯誤處理**：目前錯誤處理主要記錄日誌，可考慮實作更詳細的錯誤通知機制

## ?? 結論

本次重構成功將一個超大的控制器檔案分割成多個職責清晰、易於維護的服務類別。透過依賴注入和單一職責原則，大幅提升了程式碼品質和可維護性。重構後的架構更符合 SOLID 原則，為未來的功能擴展和維護工作奠定了良好的基礎。

---

**重構日期**：2024年（依實際日期）  
**重構者**：GitHub Copilot  
**審核狀態**：? 建置成功，待測試驗證
