# QPayReturnUrl 疑難排解指南

## 快速檢查清單

### 1. 檢查日誌輸出
在 Visual Studio 的 **Output** 視窗中選擇 **Debug** 輸出，查看：
- `[QPayCardController] QPayReturnUrl called` - 確認請求已到達
- HTTP Method (GET/POST) - 確認金流使用的方法
- ShopNo 和 PayToken 值 - 確認參數正確傳遞

### 2. 常見問題與解決方案

#### 問題：仍然出現 HTTP 500
**可能原因**：
- DisplayErrorView.cshtml 視圖檔案遺失或路徑錯誤
- QPayFeeProcessor 或 QPayDedicationBookingProcessor 內部錯誤

**解決方法**：
1. 檢查日誌中的詳細錯誤訊息
2. 確認 `Views/Home/DisplayErrorView.cshtml` 存在
3. 檢查 QPayFeeProcessor 相關類別

#### 問題：顯示「缺少必要的付款資訊」
**可能原因**：
- 金流系統未傳送 ShopNo 或 PayToken 參數
- 參數名稱大小寫不符

**解決方法**：
1. 查看日誌中的 QueryString 或 Form 參數內容
2. 確認金流系統傳送的參數名稱
3. 必要時修改控制器參數綁定：
```csharp
// 可能需要調整參數綁定
public ActionResult QPayReturnUrl(
    [FromQuery] string ShopNo, 
    [FromQuery] string PayToken)
```

#### 問題：查詢訂單失敗
**可能原因**：
- ShopNo 不在 ConvertShopNoToHashCodeAndSite 方法中
- 金流 API 連線問題
- PayToken 已過期或無效

**解決方法**：
1. 確認 ShopNo 是否在配置中
2. 檢查網路連線和金流 API 狀態
3. 驗證 PayToken 的有效性

### 3. 日誌分析範例

#### 正常流程日誌
```
[QPayCardController] QPayReturnUrl called at 2024-01-15 10:30:45
  - HTTP Method: GET
  - ShopNo: DA4272_001
  - PayToken: 7e4f3c2b1a
  - QueryString: ?ShopNo=DA4272_001&PayToken=7e4f3c2b1a
[QPayCardWebhook] QPayReturnUrl started
  - ShopNo: DA4272_001
  - PayToken: 7e4f3c2b1a
[QPayCardWebhook] OrderPayQuery completed
[QPayCardWebhook] Processing payment type: 收費單
```

#### 參數缺失日誌
```
[QPayCardController] QPayReturnUrl called at 2024-01-15 10:30:45
  - HTTP Method: GET
  - ShopNo: (null)
  - PayToken: (null)
  - QueryString: 
[QPayCardController] Error: 參數不完整: ShopNo=(null), PayToken=(null)
```

#### 查詢失敗日誌
```
[QPayCardController] QPayReturnUrl called at 2024-01-15 10:30:45
  - HTTP Method: GET
  - ShopNo: DA4272_001
  - PayToken: invalid_token
[QPayCardWebhook] QPayReturnUrl started
  - ShopNo: DA4272_001
  - PayToken: invalid_token
[QPayCardWebhook] Error: 查詢訂單失敗: Invalid PayToken
  - StackTrace: ...
```

### 4. 測試工具

#### 使用瀏覽器測試 GET 請求
```
https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl?ShopNo=DA4272_001&PayToken=test123
```

#### 使用 Postman 測試
**GET 請求**:
- URL: `https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl`
- Params: 
  - `ShopNo`: DA4272_001
  - `PayToken`: test123

**POST 請求**:
- URL: `https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl`
- Body (x-www-form-urlencoded):
  - `ShopNo`: DA4272_001
  - `PayToken`: test123

#### 使用 curl 測試
```bash
# GET 請求
curl -v "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl?ShopNo=DA4272_001&PayToken=test123"

# POST 請求
curl -v -X POST "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "ShopNo=DA4272_001&PayToken=test123"
```

### 5. 金流系統回傳格式檢查

#### 永豐金流 (QPay)
- 通常使用 GET 方法
- 參數：ShopNo, PayToken
- 回傳 URL: `{RETURN_URL}?ShopNo={value}&PayToken={value}`

#### 高鉅金流 (MyPay)
- 通常使用 POST 方法
- 參數：order_id, trade_no, amount, status
- 可能需要調整參數綁定

#### 台新金流 (TSPG)
- 通常使用 POST 方法
- 參數：result, orderNo, mid, tid
- 可能需要建立專用端點

### 6. 緊急修復步驟

如果問題仍然存在，可以啟用更詳細的日誌：

```csharp
// 在 QPayCardController.cs 中加入
System.Diagnostics.Trace.WriteLine($"Request.Path: {Request.Path}");
System.Diagnostics.Trace.WriteLine($"Request.ContentType: {Request.ContentType}");
System.Diagnostics.Trace.WriteLine($"Request.Headers: {string.Join(", ", Request.Headers.Select(h => $"{h.Key}={h.Value}"))}");
```

### 7. 聯絡資訊

遇到無法解決的問題時：
- 檢查 LINE 通知訊息（會發送給管理員）
- 查看完整的 Exception.ToString() 輸出
- 確認金流系統文件中的回傳規格

## 效能監控

建議定期檢查：
- 平均回應時間
- 錯誤率
- 金流 API 回應時間

可以使用 Application Insights 或其他 APM 工具進行監控。
