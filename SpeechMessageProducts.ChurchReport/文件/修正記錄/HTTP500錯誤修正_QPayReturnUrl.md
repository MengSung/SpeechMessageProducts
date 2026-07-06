# HTTP 500 錯誤修正 - QPayReturnUrl 端點

## 問題描述
在訪問 `https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl` 時發生 HTTP 500 錯誤。

## 原因分析
1. **HTTP 方法限制**: 原本控制器只接受 `[HttpPost]`，但金流系統可能使用 GET 或 POST 方式回傳
2. **錯誤處理不足**: 當發生例外時直接拋出，導致 500 錯誤而不是友善的錯誤頁面
3. **缺少日誌記錄**: 無法追蹤實際發生的錯誤原因
4. **參數驗證不足**: 沒有檢查必要參數是否存在

## 修正內容

### 1. QPayCardController.cs 修正
- ? **新增 [HttpGet] 支援**: 現在同時支援 GET 和 POST 請求
- ? **完整的請求日誌**: 記錄所有請求參數（QueryString 和 Form）
- ? **參數驗證**: 檢查 ShopNo 和 PayToken 是否存在
- ? **友善錯誤處理**: 使用 ViewBag 返回錯誤視圖，而非拋出例外
- ? **詳細錯誤日誌**: 記錄完整的錯誤堆疊和內部例外

```csharp
// 修正重點：
[HttpPost]
[HttpGet]  // 新增 GET 支援
[Route("QPayReturnUrl")]
public ActionResult QPayReturnUrl(string ShopNo, string PayToken)
{
    try
    {
        // 1. 記錄所有請求資訊
        System.Diagnostics.Trace.WriteLine($"[QPayCardController] QPayReturnUrl called");
        
        // 2. 參數驗證
        if (string.IsNullOrWhiteSpace(ShopNo) || string.IsNullOrWhiteSpace(PayToken))
        {
            // 返回友善錯誤頁面
            ViewBag.ErrorMessage = "缺少必要的付款資訊，請重新嘗試或聯繫客服";
            return View("~/Views/Home/DisplayErrorView.cshtml");
        }
        
        // 3. 處理付款
        using (QPayCardWebhook webhook = new QPayCardWebhook())
        {
            return webhook.QPayReturnUrl(ShopNo, PayToken);
        }
    }
    catch (Exception ex)
    {
        // 4. 記錄並返回錯誤視圖（不拋出例外）
        System.Diagnostics.Trace.WriteLine($"ERROR: {ex.Message}");
        ViewBag.ErrorMessage = "處理付款結果時發生錯誤，請稍後再試或聯繫客服";
        return View("~/Views/Home/DisplayErrorView.cshtml");
    }
}
```

### 2. QPayWebhook.cs 修正
- ? **多層錯誤處理**: 查詢和處理階段分別處理例外
- ? **詳細日誌記錄**: 記錄每個處理步驟
- ? **友善錯誤頁面**: 返回 HTML 內容而非拋出例外
- ? **保留錯誤通知**: 仍會發送 LINE 通知給管理員，但不中斷用戶流程

```csharp
// 修正重點：
public ActionResult QPayReturnUrl(string ShopNo, string PayToken)
{
    try
    {
        // 1. 記錄處理開始
        System.Diagnostics.Trace.WriteLine($"[QPayCardWebhook] Processing...");
        
        QryOrderPay aQryOrderPay = null;
        try
        {
            // 2. 查詢訂單（獨立錯誤處理）
            aQryOrderPay = m_QPayProcessor.OrderPayQuery(ShopNo, PayToken);
        }
        catch (Exception queryEx)
        {
            // 查詢失敗時返回友善頁面
            return new ContentResult
            {
                Content = "<html><body><h1>付款查詢失敗</h1>...</body></html>",
                ContentType = "text/html"
            };
        }
        
        // 3. 處理查詢結果
        if (aQryOrderPay != null && aQryOrderPay.TSResultContent != null)
        {
            // 根據類型處理...
        }
        else
        {
            // 返回友善錯誤頁面
        }
    }
    catch (Exception e)
    {
        // 4. 最外層錯誤處理（不拋出例外）
        System.Diagnostics.Trace.WriteLine($"ERROR: {e.Message}");
        return new ContentResult
        {
            Content = "<html><body><h1>處理付款時發生錯誤</h1>...</body></html>",
            ContentType = "text/html"
        };
    }
}
```

## 日誌輸出範例

修正後，所有請求都會產生詳細日誌：

```
[QPayCardController] QPayReturnUrl called at 2024-01-15 10:30:45
  - HTTP Method: GET
  - ShopNo: DA4272_001
  - PayToken: abc123xyz
  - QueryString: ?ShopNo=DA4272_001&PayToken=abc123xyz
[QPayCardWebhook] QPayReturnUrl started
  - ShopNo: DA4272_001
  - PayToken: abc123xyz
[QPayCardWebhook] OrderPayQuery completed
[QPayCardWebhook] Processing payment type: 收費單
```

## 測試建議

### 1. 測試各種請求方式
```bash
# GET 請求測試
curl "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl?ShopNo=DA4272_001&PayToken=test123"

# POST 請求測試
curl -X POST "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl" \
  -d "ShopNo=DA4272_001&PayToken=test123"
```

### 2. 測試錯誤情況
```bash
# 缺少參數
curl "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl"

# 無效的 PayToken
curl "https://jesusback.speechmessage.com.tw:8888/api/QPayCard/QPayReturnUrl?ShopNo=DA4272_001&PayToken=invalid"
```

### 3. 檢查日誌
在 Visual Studio 的 Output 視窗中檢查 Debug 輸出，確認所有日誌正常記錄。

## 預期效果

- ? **不再出現 HTTP 500**: 所有錯誤都會返回友善的錯誤頁面
- ? **支援 GET 和 POST**: 金流系統可以使用任一方式回傳
- ? **完整日誌追蹤**: 可以在日誌中追蹤所有請求細節
- ? **用戶友善**: 錯誤時顯示清楚的中文錯誤訊息
- ? **管理員通知**: 仍會透過 LINE 通知管理員有錯誤發生

## 相關檔案

- `ChurchReport/Controllers/QPayCardController.cs` - 控制器修正
- `ChurchReport/Tools/QPayWebhook.cs` - Webhook 處理修正
- `ChurchReport/Views/Home/DisplayErrorView.cshtml` - 錯誤視圖
- `ChurchReport/appsettings.json` - 配置檔案（包含 RETURN_URL）

## 後續建議

1. **監控日誌**: 上線後密切觀察日誌輸出，確認金流系統實際使用的請求格式
2. **測試環境驗證**: 在測試環境充分測試各種付款情境
3. **錯誤頁面優化**: 可以考慮將 HTML 錯誤頁面改為使用專用的 View
4. **參數擴充**: 如果金流系統還傳送其他參數，可以加入日誌記錄

## 修正日期
2024-01-15

## 修正人員
GitHub Copilot Assistant
