# ConvertPayPageResponseToCreOrder 函數說明

## 目的
建立一個統一的轉換函數，將 `PayPageResponse` 轉換為 `CreOrder` 格式，以支援多種金流系統（台新 TSPG、永豐 QPay、高鉅 MyPay）的整合。

## 位置
- **檔案**: `ChurchReport/WebServiceConnector/QPayProcessor.cs`
- **區域**: `#region 永豐金流工具區`
- **方法名稱**: `ConvertPayPageResponseToCreOrder`

## 函數簽章

```csharp
private CreOrder ConvertPayPageResponseToCreOrder(
    PayPageResponse payPageResponse, 
    string payType = "C", 
    string orderNo = null
)
```

## 參數說明

| 參數名稱 | 類型 | 必填 | 說明 |
|---------|------|------|------|
| `payPageResponse` | `PayPageResponse` | 是 | 金流系統的回應物件 |
| `payType` | `string` | 否 | 付款類型，預設為 "C" (信用卡) |
| `orderNo` | `string` | 否 | 訂單編號（當回應中沒有提供時使用） |

### 付款類型 (payType) 值說明

| 值 | 說明 | 對應的參數物件 |
|----|------|---------------|
| `C` | 信用卡 | `CreOrderCardParamRes` |
| `A` | ATM 轉帳 | `CreOrderATMParamRes` |
| `M` | 行動支付 | `CreOrderMobileParamRes` |
| `L` | LinePay | `CreOrderMobileParamRes` |

## 回傳值

回傳 `CreOrder` 物件，包含以下主要欄位：

| 欄位 | 說明 |
|------|------|
| `OrderNo` | 訂單編號 |
| `Status` | 交易狀態 ("S"=成功, "F"=失敗) |
| `Description` | 交易描述或錯誤訊息 |
| `PayType` | 付款類型 |
| `CardParam` | 信用卡付款參數（當 payType="C" 時） |
| `ATMParam` | ATM 付款參數（當 payType="A" 時） |
| `MobileParam` | 行動支付參數（當 payType="M" 或 "L" 時） |

## 轉換邏輯

### 1. 成功判斷
```csharp
bool isSuccess = payPageResponse.code == "0000" || payPageResponse.code == "00";
```

- TSPG (台新): `code = "0000"` 表示成功
- 永豐 QPay: `Status = "S"` 表示成功

### 2. 訂單編號優先順序
```csharp
OrderNo = payPageResponse.order_no 
    ?? payPageResponse.uid 
    ?? orderNo 
    ?? string.Empty
```

### 3. 根據付款類型建立對應參數

#### 信用卡 (C)
```csharp
CardParam = new CreOrderCardParamRes
{
    CardPayURL = payPageResponse.url
};
```

#### ATM 轉帳 (A)
```csharp
ATMParam = new CreOrderATMParamRes
{
    AtmPayNo = payPageResponse.key
};
```

#### 行動支付/LinePay (M/L)
```csharp
MobileParam = new CreOrderMobileParamRes
{
    MobilePayURL = payPageResponse.url
};
```

## 使用範例

### 範例 1: 台新金流 (TSPG) 信用卡付款

```csharp
// 建立 TSPG 付款請求
var tspgRequest = GetTSPGPaymentRequestData(
    Amount: 1000,
    ProductName: "月定獻金",
    OrderDate: "20241225120000",
    FeeId: "fee-12345",
    PayType: "C",
    PayTypeSub: "ONE",
    LineLoginContact: loginContact
);

// 呼叫 TSPG API
var payPageResponse = TspgToolkit.OrderCreateTest(tspgRequest, enable3D: false);

// 轉換為 CreOrder
CreOrder creOrder = ConvertPayPageResponseToCreOrder(
    payPageResponse, 
    payType: "C", 
    orderNo: "C20241225120000"
);

// 使用結果
if (creOrder.Status == "S")
{
    // 成功：導向付款頁面
    string paymentUrl = creOrder.CardParam.CardPayURL;
    return paymentUrl;
}
else
{
    // 失敗：顯示錯誤訊息
    string errorMsg = creOrder.Description;
    return $"付款失敗: {errorMsg}";
}
```

### 範例 2: 處理 ATM 付款

```csharp
var payPageResponse = TspgToolkit.CreateATMOrder(...);

CreOrder creOrder = ConvertPayPageResponseToCreOrder(
    payPageResponse, 
    payType: "A", 
    orderNo: "A20241225120000"
);

if (creOrder.Status == "S")
{
    string atmPayNo = creOrder.ATMParam.AtmPayNo;
    // 顯示虛擬帳號資訊
}
```

### 範例 3: 處理行動支付

```csharp
var payPageResponse = TspgToolkit.CreateMobileOrder(...);

CreOrder creOrder = ConvertPayPageResponseToCreOrder(
    payPageResponse, 
    payType: "M", 
    orderNo: "M20241225120000"
);

if (creOrder.Status == "S")
{
    string mobilePayUrl = creOrder.MobileParam.MobilePayURL;
    // 導向行動支付頁面
}
```

## 錯誤處理

函數包含完整的錯誤處理機制：

### 1. Null 檢查
```csharp
if (payPageResponse == null)
{
    return new CreOrder
    {
        OrderNo = orderNo ?? string.Empty,
        Status = "F",
        Description = "PayPageResponse 為 null"
    };
}
```

### 2. 例外處理
```csharp
catch (Exception ex)
{
    return new CreOrder
    {
        OrderNo = orderNo ?? string.Empty,
        Status = "F",
        Description = $"轉換失敗: {ex.Message}"
    };
}
```

## 日誌記錄

函數會自動記錄轉換過程的詳細資訊：

```csharp
System.Diagnostics.Trace.WriteLine($"[QPayProcessor] ConvertPayPageResponseToCreOrder:");
System.Diagnostics.Trace.WriteLine($"  - PayType: {payType}");
System.Diagnostics.Trace.WriteLine($"  - OrderNo: {creOrder.OrderNo}");
System.Diagnostics.Trace.WriteLine($"  - Status: {creOrder.Status}");
System.Diagnostics.Trace.WriteLine($"  - Code: {payPageResponse.code}");
System.Diagnostics.Trace.WriteLine($"  - Message: {payPageResponse.msg}");
```

### 日誌輸出範例

```
[QPayProcessor] ConvertPayPageResponseToCreOrder:
  - PayType: C
  - OrderNo: C20241225120000
  - Status: S
  - Code: 0000
  - Message: 交易成功
  - PayURL: https://tspg-t.taishinbank.com.tw/payment/...
```

## 整合點

此函數目前整合在以下方法中：

### 1. CreOrderCard (QPayProcessor.cs)

```csharp
if (m_Configuration["PAY_PROVIDER"] == "台新金流")
{
    var tspgRequest = GetTSPGPaymentRequestData(...);
    var payPageResponse = TspgToolkit.OrderCreateTest(tspgRequest, enable3D);
    
    // 使用轉換函數
    return ConvertPayPageResponseToCreOrder(payPageResponse, PayType, PayType + OrderDate);
}
```

## 擴充性

### 未來可能的擴充方向

1. **支援更多付款方式**
   - 超商代碼繳費
   - 銀聯卡
   - 國際信用卡

2. **增強欄位映射**
   - 手續費資訊
   - 交易時間
   - 付款期限

3. **支援批次轉換**
   ```csharp
   public List<CreOrder> ConvertPayPageResponseListToCreOrderList(
       List<PayPageResponse> responses, 
       string payType = "C"
   )
   ```

4. **支援反向轉換**
   ```csharp
   public PayPageResponse ConvertCreOrderToPayPageResponse(CreOrder creOrder)
   ```

## 相關檔案

| 檔案 | 說明 |
|------|------|
| `PayPageResponse.cs` | 輸入模型定義 |
| `CreOrder.cs` (QPay.Domain) | 輸出模型定義 |
| `TspgToolkit.cs` | 台新金流工具類別 |
| `QPayProcessor.cs` | 主要處理器（本函數所在位置） |

## 版本資訊

- **建立日期**: 2024-12-25
- **版本**: 1.0.0
- **作者**: Copilot AI Assistant
- **最後更新**: 2024-12-25

## 測試建議

### 單元測試範例

```csharp
[TestMethod]
public void TestConvertPayPageResponseToCreOrder_Success()
{
    // Arrange
    var payPageResponse = new PayPageResponse
    {
        code = "0000",
        msg = "交易成功",
        uid = "TEST12345",
        url = "https://example.com/pay"
    };

    // Act
    var creOrder = ConvertPayPageResponseToCreOrder(payPageResponse, "C", "ORDER001");

    // Assert
    Assert.AreEqual("S", creOrder.Status);
    Assert.AreEqual("TEST12345", creOrder.OrderNo);
    Assert.IsNotNull(creOrder.CardParam);
    Assert.AreEqual("https://example.com/pay", creOrder.CardParam.CardPayURL);
}

[TestMethod]
public void TestConvertPayPageResponseToCreOrder_Failure()
{
    // Arrange
    var payPageResponse = new PayPageResponse
    {
        code = "9999",
        msg = "交易失敗",
        uid = "TEST12345"
    };

    // Act
    var creOrder = ConvertPayPageResponseToCreOrder(payPageResponse, "C", "ORDER001");

    // Assert
    Assert.AreEqual("F", creOrder.Status);
    Assert.AreEqual("交易失敗", creOrder.Description);
}

[TestMethod]
public void TestConvertPayPageResponseToCreOrder_Null()
{
    // Arrange
    PayPageResponse payPageResponse = null;

    // Act
    var creOrder = ConvertPayPageResponseToCreOrder(payPageResponse, "C", "ORDER001");

    // Assert
    Assert.AreEqual("F", creOrder.Status);
    Assert.AreEqual("ORDER001", creOrder.OrderNo);
    Assert.IsTrue(creOrder.Description.Contains("null"));
}
```

## 注意事項

1. **執行緒安全**: 此函數為實例方法，不是靜態方法，每次呼叫都會建立新的 `CreOrder` 物件，因此是執行緒安全的。

2. **效能考量**: 轉換過程非常輕量，主要是簡單的物件映射，不涉及網路請求或資料庫操作。

3. **相容性**: 此函數設計為向下相容，即使 `PayPageResponse` 新增欄位，現有程式碼仍可正常運作。

4. **錯誤恢復**: 所有錯誤都會被捕捉並轉換為失敗的 `CreOrder` 物件，不會拋出例外。

## 總結

`ConvertPayPageResponseToCreOrder` 函數提供了一個統一的介面，讓不同金流系統的回應都能轉換為標準的 `CreOrder` 格式，簡化了金流整合的複雜度，並提供了良好的可維護性和擴充性。
