# TSPG 台新金流實現總結

## 實現內容概述

本次實現依據 **TSPG REST API v2.14 規格文件**（台新規格.txt），在 `TSPGController.cs` 中完整實現了 `post_back_url` 和 `result_url` 端點，並添加了完整的雜湊值驗證機制。

## 主要變更

### 1. TSPGController.cs 更新

#### 新增建構函數注入
```csharp
private readonly IConfiguration _configuration;
private readonly string _storeKey;
private readonly string _storeIV;

public TSPGController(TSPGWebhookHandler webhookHandler, IConfiguration configuration)
{
    _webhookHandler = webhookHandler;
    _configuration = configuration;
    _storeKey = _configuration["TSPG:StoreKey"] ?? "";
    _storeIV = _configuration["TSPG:StoreIV"] ?? "";
}
```

#### 實現 post_back_url 端點
- **路徑**: `/api/TSPG/post-back`
- **功能**: 
  - 接收用戶付款完成後的返回請求
  - 驗證雜湊值
  - 判斷交易狀態
  - 重導向至成功或失敗頁面
- **支援**: GET 和 POST 方法

#### 實現 result_url 端點
- **路徑**: `/api/TSPG/result-notify`
- **功能**:
  - 接收 TSPG 系統的後端通知
  - 驗證雜湊值
  - 處理業務邏輯
  - 返回 OK 確認給 TSPG
- **支援**: GET 和 POST 方法

#### 實現雜湊值驗證方法
```csharp
private bool VerifyPostBackHash(string orderId, string transactionId, string state, string receivedHash)
{
    // 依據 TSPG 規格: Hash = SHA256(StoreKey + TransactionId + OrderId + State + StoreIV)
    string hashString = $"{_storeKey}{transactionId}{orderId}{state}{_storeIV}";
    string calculatedHash = CalculateSHA256Hash(hashString);
    return string.Equals(calculatedHash, receivedHash, StringComparison.OrdinalIgnoreCase);
}
```

#### 新增輔助方法
- `CalculateSHA256Hash()`: 計算 SHA256 雜湊值
- `IsTransactionSuccess()`: 判斷交易是否成功
- `GetRequestParameters()`: 統一處理 GET/POST 參數
- `LogRequest()`: 詳細記錄請求資訊

#### 新增測試端點
- `/api/TSPG/verify-hash`: 驗證雜湊值
- `/api/TSPG/test-hash-calculation`: 測試雜湊計算
- `/api/TSPG/test-webhook`: 測試 Webhook 邏輯

#### 增強健康檢查端點
- 顯示所有端點資訊
- 顯示配置狀態
- 顯示版本資訊

### 2. 向後相容性

保留了舊版端點以確保相容性：
- `/api/TSPG/payment-notify` → 轉發至 `result-notify`
- `/api/TSPG/payment-return` → 轉發至 `post-back`

### 3. 錯誤處理增強

- 完整的例外捕獲和記錄
- 詳細的日誌輸出
- 友善的錯誤訊息
- 適當的 HTTP 狀態碼

## 雜湊值驗證規格

依據 TSPG REST API v2.14 規格：

### 計算公式
```
Hash = SHA256(StoreKey + TransactionId + OrderId + State + StoreIV)
```

### 參數說明
1. **StoreKey**: 台新提供的特店金鑰（從配置檔讀取）
2. **TransactionId**: TSPG 產生的交易編號
3. **OrderId**: 商戶的訂單編號
4. **State**: 交易狀態（"1" = 成功, "0" = 失敗）
5. **StoreIV**: 台新提供的特店 IV 值（從配置檔讀取）

### 驗證特性
- 使用 SHA256 演算法
- UTF-8 編碼
- 結果轉換為大寫十六進制
- 不區分大小寫比對

## 端點對應關係

| TSPG 參數 | 實現端點 | 說明 |
|-----------|---------|------|
| post_back_url | `/api/TSPG/post-back` | 用戶瀏覽器返回 |
| result_url | `/api/TSPG/result-notify` | 後端通知 |
| - | `/api/TSPG/refund-notify` | 退款通知 |

## 配置要求

需在 `appsettings.json` 中配置以下參數：

```json
{
  "TSPG": {
    "StoreId": "特店代號",
    "StoreKey": "特店金鑰（用於雜湊計算）",
    "StoreIV": "特店IV值（用於雜湊計算）",
    "ApiBaseUrl": "API根網址",
    "TerminalId": "端末機代號",
    "POST_BACK_URL": "https://yourdomain.com/api/TSPG/post-back",
    "RESULT_URL": "https://yourdomain.com/api/TSPG/result-notify"
  }
}
```

**重要**: POST_BACK_URL 和 RESULT_URL 必須使用 HTTPS 且可從外部訪問。

## 建立的文檔

### 1. TSPG_Implementation_Guide.md
完整的實現指南，包含：
- 詳細的端點說明
- 雜湊值驗證機制
- 配置設定指南
- API 端點清單
- 使用範例
- 安全性考量
- 錯誤處理
- 測試指南
- 故障排查
- 上線檢查清單

### 2. TSPG_Quick_Reference.md
快速參考指南，包含：
- 核心端點速查
- 雜湊值計算公式
- 配置範例
- 測試步驟
- 常見問題
- 日誌範例
- 緊急聯絡資訊

### 3. TSPG_Test_Scenarios.md
測試場景文檔，包含：
- 測試環境配置
- 8 個完整測試場景
- 測試用雜湊計算工具
- 測試檢查清單
- 測試報告範本

## 測試驗證

### 快速測試步驟

1. **健康檢查**
```bash
curl https://yourdomain.com/api/TSPG/health
```

2. **測試雜湊計算**
```bash
curl "https://yourdomain.com/api/TSPG/test-hash-calculation?orderId=TEST001&transactionId=TXN001&state=1"
```

3. **驗證雜湊值**
```bash
curl "https://yourdomain.com/api/TSPG/verify-hash?orderId=ORDER001&transactionId=TXN001&state=1&hash=YOUR_HASH"
```

## 日誌記錄

系統會詳細記錄：
- 接收到的請求（方法、路徑、參數）
- 雜湊值計算過程
- 驗證結果
- 處理流程
- 例外情況

範例日誌：
```
[TSPG PostBackUrl] ============ 收到請求 ============
[TSPG PostBackUrl] 時間: 2024-01-01 10:00:00
[TSPG PostBackUrl] 方法: GET
[TSPG PostBackUrl] OrderId: ORDER001, TransactionId: TXN001, State: 1
[TSPG Hash驗證] 計算字串: StoreKey + TXN001 + ORDER001 + 1 + StoreIV
[TSPG Hash驗證] 計算結果: ABCD1234...
[TSPG Hash驗證] 成功 - 雜湊值匹配
[TSPG PostBackUrl] 重導向至成功頁面
```

## 安全性措施

1. **雜湊值驗證**: 所有來自 TSPG 的請求都進行雜湊值驗證
2. **HTTPS 要求**: POST_BACK_URL 和 RESULT_URL 必須使用 HTTPS
3. **敏感資訊保護**: StoreKey 和 StoreIV 不記錄在日誌中
4. **參數驗證**: 驗證所有必要參數存在性
5. **錯誤處理**: 適當的錯誤訊息，不洩露系統資訊

## 上線前檢查

- [x] 實現 post_back_url 端點
- [x] 實現 result_url 端點
- [x] 實現雜湊值驗證
- [x] 建立完整文檔
- [x] 編譯無錯誤
- [ ] 配置正式環境參數
- [ ] 完成完整測試
- [ ] 部署至正式環境
- [ ] 向台新銀行註冊 Webhook URL

## 後續工作建議

### 必須完成
1. 在正式環境配置正確的 StoreId, StoreKey, StoreIV
2. 確保 POST_BACK_URL 和 RESULT_URL 可從外部訪問
3. 完成所有測試場景
4. 實現訂單狀態更新的業務邏輯
5. 實現重複通知檢測機制

### 建議增強
1. 添加 IP 白名單驗證（只接受台新銀行的 IP）
2. 實現完整的訂單管理功能
3. 添加交易記錄查詢頁面
4. 實現自動對帳功能
5. 添加監控和告警機制

### 可選優化
1. 使用 Azure Key Vault 存儲密鑰
2. 實現交易數據的加密存儲
3. 添加效能監控
4. 實現交易統計報表

## 技術支援

### 台新銀行
- 技術文檔: TSPG REST API v2.14
- 聯絡窗口: [請填入]

### 內部團隊
- 文檔位置: `ChurchReport\文件\`
- 主要文件:
  - `TSPG_Implementation_Guide.md`
  - `TSPG_Quick_Reference.md`
  - `TSPG_Test_Scenarios.md`
  - `台新規格.txt`

## 版本資訊

- **實現版本**: 2.14
- **依據規格**: TSPG REST API v2.14
- **實現日期**: 2024
- **最後更新**: 2024

## 總結

本次實現完整符合 TSPG REST API v2.14 規格要求，提供了：

? 完整的 post_back_url 和 result_url 實現
? 嚴格的雜湊值驗證機制
? 詳細的日誌記錄
? 完善的錯誤處理
? 豐富的測試工具
? 完整的技術文檔

系統已準備好進行測試和部署。

---
**文檔編寫**: AI Assistant
**技術審查**: [待填入]
**批准人**: [待填入]
**日期**: 2024
