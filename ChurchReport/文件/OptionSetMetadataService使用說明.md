# OptionSetMetadataService 使用說明

## 概述

`OptionSetMetadataService` 是一個專門用於動態查詢 Dynamics 365 OptionSet 的服務類別，取代了原本硬編碼的 `Dictionary` 方式，提供更靈活、更易維護的解決方案。

## 主要優勢

### ? 1. **不再寫死程式碼**
- **原本**：每次新增奉獻類別都要修改程式碼
- **現在**：直接從 Dynamics 365 取得最新的 OptionSet 清單

### ? 2. **自動同步**
- 當 Dynamics 365 的 OptionSet 更新時，程式自動取得最新資料
- 不需要重新部署程式碼

### ? 3. **快取機制**
- 預設快取 24 小時，減少對 Dynamics 365 的查詢次數
- 提升效能，降低 API 呼叫成本

### ? 4. **支援多語言**
- 優先使用繁體中文（zh-TW）
- 支援簡體中文（zh-CN）、英文（en-US）

## 架構說明

```
┌─────────────────────────────────────────────────────────┐
│  QPayProcessor (業務邏輯層)                                │
│  - 呼叫 GetCategoryValueByDisplayText()                   │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│  OptionSetMetadataService (服務層)                        │
│  - GetOptionSetMapping()     (取得完整對應表)              │
│  - GetOptionSetValue()       (顯示文字 → 值)              │
│  - GetOptionSetText()        (值 → 顯示文字)              │
│  - ClearCache()              (清除快取)                   │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│  Dynamics 365 Metadata API                               │
│  - RetrieveAttributeRequest                              │
│  - PicklistAttributeMetadata                             │
└─────────────────────────────────────────────────────────┘
```

## 重構前後對比

### ? **重構前** (硬編碼方式)

```csharp
private int GetCategoryValueByDisplayText(string displayText)
{
    // 使用 Dictionary 建立顯示文字與代碼的對應表
    var categoryMapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "主日奉獻", 100000010 },
        { "十一奉獻", 100000000 },
        { "感恩奉獻", 100000002 },
        { "建堂奉獻", 100000006 },
        { "宣教奉獻", 100000007 },
        { "愛心奉獻", 100000019 },
        { "特別獻金", 100000008 }
    };

    if (categoryMapping.TryGetValue(displayText.Trim(), out int categoryValue))
    {
        return categoryValue;
    }

    return 100000000; // 預設為十一奉獻
}
```

**缺點**：
- ? 新增類別需要修改程式碼
- ? 無法自動同步 Dynamics 365 的變更
- ? 維護成本高

### ? **重構後** (動態查詢方式)

```csharp
private int GetCategoryValueByDisplayText(string displayText)
{
    try
    {
        // ? 使用 OptionSetMetadataService 動態查詢
        int categoryValue = _optionSetMetadataService.GetOptionSetValue(
            entityName: "new_fee",
            attributeName: "new_category",
            displayText: displayText.Trim(),
            defaultValue: 100000000 // 預設為十一奉獻
        );

        return categoryValue;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"錯誤: {ex.Message}，使用預設值");
        return 100000000;
    }
}
```

**優點**：
- ? 自動從 Dynamics 365 取得最新清單
- ? 支援多語言
- ? 快取機制提升效能
- ? 維護成本低

## 使用範例

### 1. 基本用法 - 取得 OptionSet 值

```csharp
// 從顯示文字取得 OptionSet 值
int categoryValue = _optionSetMetadataService.GetOptionSetValue(
    entityName: "new_fee",
    attributeName: "new_category",
    displayText: "十一奉獻",
    defaultValue: 100000000
);

// 輸出: categoryValue = 100000000
```

### 2. 反向查詢 - 取得顯示文字

```csharp
// 從 OptionSet 值取得顯示文字
string displayText = _optionSetMetadataService.GetOptionSetText(
    entityName: "new_fee",
    attributeName: "new_category",
    optionSetValue: 100000000
);

// 輸出: displayText = "十一奉獻"
```

### 3. 取得完整對應表

```csharp
// 取得完整的 OptionSet 對應表
Dictionary<string, int> mapping = _optionSetMetadataService.GetOptionSetMapping(
    entityName: "new_fee",
    attributeName: "new_category"
);

// 輸出範例:
// {
//   "主日奉獻" => 100000010,
//   "十一奉獻" => 100000000,
//   "感恩奉獻" => 100000002,
//   "建堂奉獻" => 100000006,
//   ...
// }
```

### 4. 清除快取

```csharp
// 當 Dynamics 365 的 OptionSet 有更新時，可以手動清除快取
_optionSetMetadataService.ClearCache("new_fee", "new_category");
```

## 快取機制

### 快取鍵格式
```
OptionSet_{entityName}_{attributeName}
```

例如：`OptionSet_new_fee_new_category`

### 快取過期時間
- **預設**: 24 小時
- **可調整**: 修改 `CACHE_DURATION_HOURS` 常數

### 手動清除快取
```csharp
// 清除指定 OptionSet 的快取
_optionSetMetadataService.ClearCache("new_fee", "new_category");
```

## 錯誤處理

### 1. 找不到對應的顯示文字

```csharp
int value = _optionSetMetadataService.GetOptionSetValue(
    "new_fee",
    "new_category",
    "不存在的類別",
    defaultValue: 100000000 // 使用預設值
);
// 回傳: 100000000
```

### 2. Metadata 查詢失敗

```csharp
try
{
    var mapping = _optionSetMetadataService.GetOptionSetMapping("new_fee", "new_category");
}
catch (Exception ex)
{
    _logger.LogError(ex, "查詢 Metadata 失敗");
    // 自動回傳空的 Dictionary
}
```

## 效能考量

### 第一次查詢
- **時間**: 約 100-300ms（查詢 Metadata API）
- **後續**: 從快取取得，< 1ms

### 快取效益
```
第一次查詢:  300ms ─────────────┐
                                 │
後續 1000 次查詢: < 1ms ?─────────┘ (使用快取)
                                     
總節省時間: 約 299 秒
```

## 整合到現有程式碼

### 步驟 1: 加入命名空間

```csharp
using ChurchReport.Services; // 新增命名空間
```

### 步驟 2: 宣告服務實例

```csharp
private readonly OptionSetMetadataService _optionSetMetadataService;
```

### 步驟 3: 初始化服務

```csharp
public QPayProcessor(IPayment aPaymentService)
{
    // ...其他初始化...

    _optionSetMetadataService = new OptionSetMetadataService(
        m_ToolUtilityClass.m_Crm2011OrganizationService,
        logger, // 注入 ILogger
        cache   // 注入 IMemoryCache
    );
}
```

### 步驟 4: 使用服務

```csharp
// 原本的方法
public void SetFeePayCategory(String Value, ref Entity aFeeEntity)
{
    int categoryValue = GetCategoryValueByDisplayText(Value);
    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_category", categoryValue);
}

// GetCategoryValueByDisplayText 內部已改為動態查詢
```

## 未來擴展

### 1. 支援其他 OptionSet

```csharp
// 付款方式 (new_pay_way)
int payWayValue = _optionSetMetadataService.GetOptionSetValue(
    "new_fee",
    "new_pay_way",
    "信用卡"
);

// 付款狀態 (new_pay_status)
int payStatusValue = _optionSetMetadataService.GetOptionSetValue(
    "new_fee",
    "new_pay_status",
    "信用卡已繳費"
);
```

### 2. 與 MyPayFeeTypeHelper 整合

```csharp
// MyPayFeeTypeHelper 也可以使用相同服務
public class MyPayFeeTypeHelper
{
    private readonly OptionSetMetadataService _optionSetService;

    public string GetDedicationCategoryName(int categoryValue)
    {
        return _optionSetService.GetOptionSetText(
            "new_fee",
            "new_category",
            categoryValue
        );
    }
}
```

## 注意事項

### ?? 1. 快取過期
- 如果 Dynamics 365 的 OptionSet 更新，需要等待快取過期（24 小時）
- 或手動呼叫 `ClearCache()` 清除快取

### ?? 2. 權限要求
- 執行 Metadata 查詢需要適當的 CRM 權限
- 確保連接的帳號有 `Read Metadata` 權限

### ?? 3. 效能影響
- 第一次查詢會有輕微延遲（約 100-300ms）
- 建議在應用程式啟動時預先載入常用的 OptionSet

## 測試建議

### 單元測試

```csharp
[TestMethod]
public void TestGetOptionSetValue_Success()
{
    // Arrange
    var service = new OptionSetMetadataService(...);

    // Act
    int result = service.GetOptionSetValue("new_fee", "new_category", "十一奉獻");

    // Assert
    Assert.AreEqual(100000000, result);
}

[TestMethod]
public void TestGetOptionSetValue_NotFound_UseDefault()
{
    // Arrange
    var service = new OptionSetMetadataService(...);

    // Act
    int result = service.GetOptionSetValue("new_fee", "new_category", "不存在的類別", 999);

    // Assert
    Assert.AreEqual(999, result);
}
```

## 總結

使用 `OptionSetMetadataService` 的主要好處：

1. ? **不再寫死程式碼** - 動態從 Dynamics 365 取得
2. ? **自動同步** - CRM 更新後自動生效
3. ? **易於維護** - 集中管理 OptionSet 查詢邏輯
4. ? **效能優化** - 快取機制減少 API 呼叫
5. ? **支援多語言** - 自動處理本地化標籤
6. ? **錯誤處理** - 完整的例外處理與預設值機制

## 相關文件

- [Dynamics 365 Metadata API 文件](https://docs.microsoft.com/dynamics365/customerengagement/on-premises/developer/webapi/retrieve-metadata-name-metadataid)
- [PicklistAttributeMetadata 類別](https://docs.microsoft.com/dotnet/api/microsoft.xrm.sdk.metadata.picklistattributemetadata)
