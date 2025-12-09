# MyPayController 重構 - 快速參考指南

## ?? 快速開始

### 檔案位置
```
ChurchReport/
├── Controllers/
│   └── MyPayController.cs          ← 主控制器（精簡版）
├── Services/                        ← 新增的服務資料夾
│   ├── MyPayMessageBuilder.cs      ← LINE 訊息建立
│   ├── MyPayStatusHelper.cs        ← 狀態判斷
│   ├── MyPayFeeTypeHelper.cs       ← 收費單類型判斷
│   ├── MyPayLogger.cs              ← 日誌記錄
│   ├── MyPayCrmService.cs          ← CRM 更新
│   └── MyPayNotificationService.cs ← LINE 通知發送
└── Startup.cs                       ← 服務註冊（已更新）
```

## ?? 各服務職責一覽表

| 服務類別 | 主要職責 | 核心方法數 | 依賴服務 |
|---------|---------|-----------|---------|
| **MyPayController** | API 端點與流程協調 | 3 | 全部服務 |
| **MyPayMessageBuilder** | 建立 LINE 訊息 | 6 | 無 |
| **MyPayStatusHelper** | 狀態判斷與轉換 | 6 | ILogger |
| **MyPayFeeTypeHelper** | 收費單類型判斷 | 4 | ILogger |
| **MyPayLogger** | 日誌記錄 | 1 | ILogger |
| **MyPayCrmService** | CRM 資料更新 | 2 | ILogger, StatusHelper |
| **MyPayNotificationService** | LINE 通知發送 | 4 | ILogger, MessageBuilder, StatusHelper, FeeTypeHelper |

## ?? 常見使用場景

### 1. 新增一種新的訊息類型

**步驟**：
1. 在 `MyPayMessageBuilder.cs` 新增方法
2. 在 `MyPayNotificationService.cs` 的發送方法中呼叫
3. 在 `MyPayFeeTypeHelper.cs` 的 `FeeType` 列舉新增類型（如需要）

**範例**：
```csharp
// Step 1: MyPayMessageBuilder.cs
public string BuildActivityPaymentSuccessMessage(
    string fullName, string orderId, decimal amount,
    string activityName, DateTime paymentTime)
{
    var msg = $"【活動報名成功】{Environment.NewLine}";
    msg += $"親愛的 {fullName}，您好！{Environment.NewLine}";
    // ... 更多內容
    return msg;
}

// Step 2: MyPayNotificationService.cs
if (feeType == FeeType.Activity)
{
    message = _messageBuilder.BuildActivityPaymentSuccessMessage(...);
}

// Step 3: MyPayFeeTypeHelper.cs
public enum FeeType
{
    Dedication,
    Course,
    Activity,  // 新增
    Other
}
```

### 2. 新增狀態碼支援

**位置**：`MyPayStatusHelper.cs`

```csharp
public string GetPaymentStatusMessage(string prc)
{
    // ...existing cases...
    case "999": return "您的新狀態說明";
    default: return $"未知狀態碼：{prc}";
}
```

### 3. 新增錯誤代碼對應

**位置**：`MyPayStatusHelper.cs`

```csharp
public string GetFriendlyErrorMessage(string errorCode, string retCode)
{
    string code = errorCode ?? retCode ?? "";
    switch (code.ToUpper())
    {
        // ...existing cases...
        case "NEW_ERROR_CODE":
            return "友善的錯誤說明";
        default:
            return null;
    }
}
```

### 4. 修改 LINE 訊息格式

**位置**：`MyPayMessageBuilder.cs`

直接修改對應的方法即可，不影響其他部分。

### 5. 調整 CRM 更新邏輯

**位置**：`MyPayCrmService.cs`

修改 `UpdateFeeEntityWithMyPayReturn` 方法。

## ?? 資料流程圖

```
┌─────────────────┐
│  金流平台回傳    │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────┐
│  MyPayController             │
│  .PaymentNotify()            │
└────────┬────────────────────┘
         │
         ├─? MyPayLogger.LogFullReturnData()
         │   (記錄完整資料)
         │
         ├─? MyPayReturnModel.ValidateAllFields()
         │   (驗證欄位)
         │
         ├─? MyPayStatusHelper.IsSuccessfulPaymentStatus()
         │   (判斷成功/失敗)
         │
         ├─? MyPayFeeTypeHelper.DetermineFeeType()
         │   (判斷收費單類型)
         │
         ├─? MyPayCrmService.UpdateFeeEntityWithMyPayReturn()
         │   (更新 CRM)
         │   │
         │   └─? StatusHelper.ParseFinishTime()
         │       StatusHelper.GetPaymentMethodName()
         │       StatusHelper.GetPaymentStatusMessage()
         │
         └─? MyPayNotificationService.SendLineNotificationByType()
             (發送 LINE 通知)
             │
             ├─? MessageBuilder.BuildXXXMessage()
             │   (建立訊息內容)
             │
             ├─? FeeTypeHelper.GetCourseName()
             │   FeeTypeHelper.GetDedicationCategoryName()
             │   (取得名稱)
             │
             └─? SendLineMessage()
                 (實際發送)
```

## ?? 關鍵常數與設定

### 控制器常數
```csharp
// MyPayController.cs
private const string DYNAMICS_CONNECTION_NAME = "DYNAMICS365";
```

### CRM 狀態值
```csharp
// MyPayCrmService.cs
private const int PAYMENT_STATUS_PAID = 100000001;      // 已繳費
private const int PAYMENT_METHOD_CREDIT_CARD = 100000001; // 信用卡
```

### LINE Token
```csharp
// MyPayNotificationService.cs
private const string LINE_CHANNEL_ACCESS_TOKEN = "...";
```
> ?? 建議：未來改為從 appsettings.json 讀取

### 奉獻類別代碼
```csharp
// MyPayFeeTypeHelper.cs
100000010: 主日奉獻
100000000: 十一奉獻
100000002: 感恩奉獻
100000006: 建堂奉獻
100000007: 宣教奉獻
100000019: 愛心奉獻
100000008: 特別奉獻
```

### 交易狀態碼 (PRC)
```csharp
// MyPayStatusHelper.cs
250: 付款成功
290: 交易成功但資訊不符
600: 結帳完成
260: 交易成功，尚未付款完成(超商代碼)
270: 交易成功，尚未付款完成(虛擬帳號)
280: 交易成功，尚未付款完成(WebATM)
300: 交易失敗
400: 系統錯誤
```

## ?? 常見問題排查

### Q1: 建置錯誤 - 找不到服務類別
**原因**：服務未註冊到 DI 容器  
**解決**：檢查 `Startup.cs` 中的 `ConfigureServices` 方法

```csharp
services.AddScoped<ChurchReport.Services.MyPayMessageBuilder>();
services.AddScoped<ChurchReport.Services.MyPayStatusHelper>();
// ... 其他服務
```

### Q2: 執行時錯誤 - 無法解析服務
**原因**：控制器建構函式參數與註冊的服務不一致  
**解決**：確認控制器建構函式參數與 Startup 中註冊的服務一致

### Q3: LINE 訊息未發送
**檢查項目**：
1. 連絡人是否有 LINE ID？
2. LINE Token 是否正確？
3. 檢查日誌中的錯誤訊息
4. 確認 `MyPayNotificationService` 的異常處理邏輯

### Q4: CRM 未更新
**檢查項目**：
1. 訂單編號是否正確？（`new_q_pay_order_number`）
2. CRM 連線是否正常？
3. 檢查日誌中的錯誤訊息
4. 確認 `ToolUtilityClass` 是否正確 Dispose

### Q5: 訊息格式不正確
**位置**：`MyPayMessageBuilder.cs`  
**解決**：直接修改對應的訊息建立方法

## ?? 日誌查看指引

### 關鍵日誌標籤
```
[MyPay回傳]              - 所有 MyPay 相關日誌
[MyPay完整回傳資料]      - 完整的金流回傳資料
SendLineMessage:        - LINE 發送相關
```

### 查看特定訂單的完整處理流程
```bash
# 搜尋包含訂單編號的所有日誌
grep "ORDER123" Logs/Trace.log

# 或使用 Windows PowerShell
Select-String -Path "Logs\Trace.log" -Pattern "ORDER123"
```

### 查看錯誤日誌
```bash
# 搜尋錯誤和警告
grep -E "LogError|LogWarning" Logs/Trace.log

# PowerShell
Select-String -Path "Logs\Trace.log" -Pattern "LogError|LogWarning"
```

## ?? 相關文件

- [完整重構說明文件](./MyPayController重構說明.md)
- [測試檢查清單](./MyPayController測試檢查清單.md)
- [高鋸金流規格](./高鋸金流規格.txt)

## ?? 聯絡資訊

如有問題或建議，請聯繫：
- 專案維護者：[姓名]
- Email：[email@example.com]
- 內部文件：[連結]

---

**最後更新**：2024年（依實際日期）  
**版本**：2.0（重構版）
