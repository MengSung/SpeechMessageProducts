# TSPG (台新金流) REST API v2.14 實現指南

## 概述

本文檔說明 ChurchReport 專案中 TSPG (台新金流) 的完整實現，包括 `post_back_url` 和 `result_url` 的端點實現，以及完整的雜湊值驗證機制。

## 主要功能

### 1. Webhook 端點實現

依據 TSPG REST API v2.14 規格，系統實現了以下端點：

#### 1.1 post_back_url (用戶返回端點)
- **路徑**: `/api/TSPG/post-back`
- **方法**: GET / POST
- **用途**: 用戶付款完成後，瀏覽器從 TSPG 付款頁面重導向到此端點
- **功能**:
  - 接收付款結果參數
  - 驗證雜湊值
  - 根據交易狀態重導向至成功或失敗頁面

#### 1.2 result_url (後端通知端點)
- **路徑**: `/api/TSPG/result-notify`
- **方法**: GET / POST
- **用途**: TSPG 系統主動向此端點發送交易結果通知
- **功能**:
  - 接收並驗證付款通知
  - 更新訂單狀態
  - 返回確認訊息給 TSPG
  - **重要**: 必須返回 "OK" 或確認訊息，否則 TSPG 會重複通知

#### 1.3 refund-notify (退款通知端點)
- **路徑**: `/api/TSPG/refund-notify`
- **方法**: GET / POST
- **用途**: 接收 TSPG 的退款結果通知

### 2. 雜湊值驗證機制

依據 TSPG REST API v2.14 規格實現完整的雜湊值驗證：

#### 2.1 驗證公式
```
Hash = SHA256(StoreKey + TransactionId + OrderId + State + StoreIV)
```

#### 2.2 驗證參數
- **StoreKey**: 特店金鑰 (從配置檔讀取)
- **TransactionId**: TSPG 交易編號
- **OrderId**: 訂單編號
- **State**: 交易狀態 ("1" = 成功, "0" = 失敗)
- **StoreIV**: 特店 IV 值 (從配置檔讀取)

#### 2.3 驗證流程
1. 從請求中提取必要參數
2. 使用 StoreKey 和 StoreIV 計算雜湊值
3. 與接收到的 Hash 值比對（不區分大小寫）
4. 記錄驗證結果

## 配置設定

在 `appsettings.json` 中配置 TSPG 參數：

```json
{
  "TSPG": {
    "StoreId": "your_merchant_id",
    "StoreKey": "your_store_key",
    "StoreIV": "your_store_iv",
    "ApiBaseUrl": "https://tspg.taishinbank.com.tw/tspgapi/restapi",
    "TerminalId": "T0000000",
    "POST_BACK_URL": "https://yourdomain.com/api/TSPG/post-back",
    "RESULT_URL": "https://yourdomain.com/api/TSPG/result-notify",
    "TestMode": "false"
  }
}
```

### 配置參數說明

| 參數 | 說明 | 必要 |
|------|------|------|
| StoreId | 特店代號（台新提供） | ? |
| StoreKey | 雜湊金鑰（台新提供） | ? |
| StoreIV | 雜湊 IV 值（台新提供） | ? |
| ApiBaseUrl | API 根網址 | ? |
| TerminalId | 端末機代號 | ? |
| POST_BACK_URL | 用戶返回網址（必須是公開可訪問的 HTTPS 網址） | ? |
| RESULT_URL | 後端通知網址（必須是 HTTPS 網址） | ? |
| TestMode | 是否啟用測試模式 | - |

## API 端點清單

### Webhook 端點

| 端點 | 方法 | 說明 |
|------|------|------|
| `/api/TSPG/post-back` | GET/POST | 付款完成返回頁面 |
| `/api/TSPG/result-notify` | GET/POST | 付款結果後端通知 |
| `/api/TSPG/refund-notify` | GET/POST | 退款結果通知 |
| `/api/TSPG/payment-notify` | GET/POST | 付款通知（舊版相容） |
| `/api/TSPG/payment-return` | GET/POST | 付款返回（舊版相容） |

### 操作 API 端點

| 端點 | 方法 | 說明 |
|------|------|------|
| `/api/TSPG/create-payment` | POST | 建立付款訂單 |
| `/api/TSPG/query-order/{orderId}` | GET | 查詢訂單狀態 |
| `/api/TSPG/cancel-order/{orderId}` | POST | 取消訂單 |
| `/api/TSPG/refund` | POST | 申請退款 |
| `/api/TSPG/capture/{orderId}` | POST | 信用卡請款 |
| `/api/TSPG/transaction-history` | GET | 取得交易記錄 |
| `/api/TSPG/verify-hash` | GET | 驗證雜湊值 |

### 測試與監控端點

| 端點 | 方法 | 說明 |
|------|------|------|
| `/api/TSPG/health` | GET | 健康狀態檢查 |
| `/api/TSPG/test-webhook` | POST | 測試 Webhook 邏輯 |
| `/api/TSPG/test-hash-calculation` | GET | 測試雜湊值計算 |

## 使用範例

### 1. 建立付款訂單

```http
POST /api/TSPG/create-payment
Content-Type: application/json

{
  "sender": "rest",
  "ver": "1.0.0",
  "mid": "999812777000198",
  "tid": "T0000000",
  "pay_type": 1,
  "tx_type": 1,
  "params": {
    "layout": "1",
    "order_no": "ORDER20240101001",
    "amt": "100000",
    "cur": "NTD",
    "order_desc": "測試訂單",
    "capt_flag": "0",
    "result_flag": "1",
    "post_back_url": "https://yourdomain.com/api/TSPG/post-back",
    "result_url": "https://yourdomain.com/api/TSPG/result-notify"
  }
}
```

### 2. 驗證雜湊值

```http
GET /api/TSPG/verify-hash?orderId=ORDER001&transactionId=TXN001&state=1&hash=ABCD1234...
```

### 3. 測試雜湊值計算

```http
GET /api/TSPG/test-hash-calculation?orderId=ORDER001&transactionId=TXN001&state=1
```

回應範例：
```json
{
  "success": true,
  "order_id": "ORDER001",
  "transaction_id": "TXN001",
  "state": "1",
  "hash_string_format": "StoreKey + TransactionId + OrderId + State + StoreIV",
  "calculated_hash": "A1B2C3D4E5F6...",
  "note": "依據 TSPG REST API v2.14 規格計算"
}
```

## TSPG 通知流程

### post_back_url 流程

```
用戶 -> 付款完成 -> TSPG 重導向 -> post_back_url
                                   |
                                   v
                           驗證雜湊值
                                   |
                      +------------+------------+
                      |                         |
                    成功                      失敗
                      |                         |
         重導向至成功頁面          重導向至失敗頁面
```

### result_url 流程

```
TSPG 系統 -> 發送 POST 請求 -> result_url
                              |
                              v
                      驗證雜湊值
                              |
                    處理業務邏輯
                              |
                    更新訂單狀態
                              |
                   返回 OK 給 TSPG
```

## 交易狀態判斷

依據 TSPG 規格，交易成功的判斷條件：

1. **state = "1"** → 交易成功
2. **ret_code = "00" 或 "0000"** → 交易成功
3. 其他情況 → 交易失敗

## 安全性考量

### 1. 雜湊值驗證
- **必須驗證**: 所有來自 TSPG 的通知都應驗證雜湊值
- **防止偽造**: 雜湊值驗證可防止惡意請求偽造交易結果
- **記錄失敗**: 驗證失敗應詳細記錄以供調查

### 2. HTTPS 要求
- POST_BACK_URL 和 RESULT_URL **必須使用 HTTPS**
- 確保傳輸安全性

### 3. 金鑰管理
- StoreKey 和 StoreIV **不應提交到版本控制**
- 建議使用環境變數或 Azure Key Vault 等安全存儲
- 定期更換金鑰

### 4. IP 白名單
- 建議配置防火牆，只允許 TSPG 的 IP 訪問 result_url
- 詢問台新銀行取得官方 IP 清單

## 錯誤處理

### 常見錯誤及處理方式

| 錯誤情況 | 處理方式 |
|---------|---------|
| 雜湊值驗證失敗 | 記錄詳細資訊，通知管理員 |
| 缺少必要參數 | 返回 400 Bad Request |
| 訂單不存在 | 返回 404 Not Found |
| 系統錯誤 | 返回 500，記錄錯誤詳情 |
| TSPG 重複通知 | 返回 OK，標記為已處理 |

## 日誌記錄

系統會記錄以下資訊：

1. **請求詳情**: 方法、路徑、參數
2. **雜湊驗證**: 計算過程、驗證結果
3. **處理結果**: 成功或失敗原因
4. **例外情況**: 完整堆疊追蹤

查看日誌：
```
[TSPG PostBackUrl] OrderId: xxx, TransactionId: xxx, State: 1
[TSPG Hash驗證] 計算字串: StoreKey + TXN001 + ORDER001 + 1 + StoreIV
[TSPG Hash驗證] 成功 - 雜湊值匹配
[TSPG PostBackUrl] 重導向至成功頁面: /Home/PaymentSuccess?...
```

## 測試指南

### 1. 健康檢查
```bash
curl https://yourdomain.com/api/TSPG/health
```

### 2. 測試雜湊計算
```bash
curl "https://yourdomain.com/api/TSPG/test-hash-calculation?orderId=TEST001&transactionId=TXN001&state=1"
```

### 3. 模擬 post_back_url 通知
```bash
curl "https://yourdomain.com/api/TSPG/post-back?order_id=TEST001&transaction_id=TXN001&state=1&ret_code=00&hash=CALCULATED_HASH"
```

### 4. 模擬 result_url 通知
```bash
curl -X POST https://yourdomain.com/api/TSPG/result-notify \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "order_id=TEST001&transaction_id=TXN001&state=1&cost=100000&hash=CALCULATED_HASH"
```

## 故障排查

### 問題: 雜湊值驗證總是失敗

**檢查項目**:
1. StoreKey 和 StoreIV 是否正確配置
2. 參數順序是否正確：StoreKey + TransactionId + OrderId + State + StoreIV
3. 是否使用 UTF-8 編碼
4. Hash 值是否為大寫十六進制

**測試方法**:
```bash
# 使用測試端點驗證
curl "https://yourdomain.com/api/TSPG/test-hash-calculation?orderId=ORDER001&transactionId=TXN001&state=1"
```

### 問題: TSPG 重複發送通知

**原因**: result_url 端點沒有正確返回確認訊息

**解決方法**:
1. 確保端點返回 HTTP 200 狀態碼
2. 返回 JSON 格式：`{"status": "OK"}`
3. 處理速度不應超過 30 秒

### 問題: 用戶無法返回網站

**檢查項目**:
1. post_back_url 是否為公開可訪問的網址
2. 是否使用 HTTPS
3. 防火牆是否阻擋訪問
4. 路由配置是否正確

## 上線檢查清單

- [ ] 已配置正式環境的 StoreId、StoreKey、StoreIV
- [ ] POST_BACK_URL 和 RESULT_URL 使用 HTTPS
- [ ] 網址可從外部訪問（非 localhost）
- [ ] 雜湊值驗證測試通過
- [ ] 已完成完整的付款流程測試
- [ ] 日誌記錄正常運作
- [ ] 錯誤處理機制完善
- [ ] 已配置監控和告警
- [ ] 資料庫備份機制就緒

## 參考資料

- TSPG REST API v2.14 規格文件
- 台新銀行技術支援: [聯絡資訊]
- 內部文件: `ChurchReport\文件\台新規格.txt`

## 版本歷史

| 版本 | 日期 | 說明 |
|------|------|------|
| 2.14.0 | 2024-01-XX | 依據 TSPG REST API v2.14 完整實現 |
| 2.14.1 | 2024-01-XX | 新增完整雜湊值驗證機制 |
| 2.14.2 | 2024-01-XX | 新增測試端點和監控功能 |

## 支援與聯絡

如有問題或需要協助，請聯絡開發團隊。

---

**最後更新**: 2024年
**維護者**: ChurchReport 開發團隊
