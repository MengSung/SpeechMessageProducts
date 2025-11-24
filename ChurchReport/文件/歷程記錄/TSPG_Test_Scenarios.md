# TSPG 測試場景與驗證

## 測試環境配置

### appsettings.json (測試環境)
```json
{
  "TSPG": {
    "StoreId": "999812777000198",
    "StoreKey": "TEST_KEY_32_CHARACTERS_XXXX",
    "StoreIV": "TEST_IV_16_CHAR",
    "ApiBaseUrl": "https://tspg-t.taishinbank.com.tw/tspgapi/restapi",
    "TerminalId": "T0000000",
    "TestMerchant3D": "999812777000198",
    "TestMerchantNo3D": "999812777000199",
    "POST_BACK_URL": "https://your-test-domain.com/api/TSPG/post-back",
    "RESULT_URL": "https://your-test-domain.com/api/TSPG/result-notify",
    "TestMode": "true"
  }
}
```

## 場景 1: 正常付款流程 (成功)

### 1.1 建立訂單
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
    "order_no": "TEST_ORDER_001",
    "amt": "100000",
    "cur": "NTD",
    "order_desc": "測試訂單-正常付款",
    "capt_flag": "0",
    "result_flag": "1"
  }
}
```

**預期回應**:
```json
{
  "success": true,
  "order_id": "TEST_ORDER_001",
  "payment_url": "https://tspg-t.taishinbank.com.tw/...",
  "message": "訂單建立成功"
}
```

### 1.2 模擬 post_back_url 回調 (成功)
```bash
# 計算雜湊值
curl "https://your-domain.com/api/TSPG/test-hash-calculation?orderId=TEST_ORDER_001&transactionId=TXN_20240101_001&state=1"

# 取得計算的 hash 後，模擬回調
curl "https://your-domain.com/api/TSPG/post-back?order_id=TEST_ORDER_001&transaction_id=TXN_20240101_001&state=1&ret_code=00&ret_msg=success&cost=100000&hash=CALCULATED_HASH"
```

**預期行為**:
- 驗證雜湊值通過
- 重導向至 `/Home/PaymentSuccess?order_id=TEST_ORDER_001&transaction_id=TXN_20240101_001&amount=100000`

### 1.3 模擬 result_url 通知
```bash
curl -X POST https://your-domain.com/api/TSPG/result-notify \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "order_id=TEST_ORDER_001" \
  -d "transaction_id=TXN_20240101_001" \
  -d "state=1" \
  -d "ret_code=00" \
  -d "cost=100000" \
  -d "actual_cost=100000" \
  -d "hash=CALCULATED_HASH"
```

**預期回應**:
```json
{
  "status": "OK",
  "message": "處理成功",
  "order_id": "TEST_ORDER_001"
}
```

## 場景 2: 付款失敗流程

### 2.1 模擬 post_back_url 回調 (失敗)
```bash
curl "https://your-domain.com/api/TSPG/post-back?order_id=TEST_ORDER_002&transaction_id=TXN_20240101_002&state=0&ret_code=9999&ret_msg=Card%20declined&cost=50000&hash=CALCULATED_HASH"
```

**預期行為**:
- 驗證雜湊值
- 重導向至 `/Home/PaymentFailure?order_id=TEST_ORDER_002&error=Card%20declined&ret_code=9999`

## 場景 3: 雜湊值驗證失敗

### 3.1 使用錯誤的雜湊值
```bash
curl "https://your-domain.com/api/TSPG/post-back?order_id=TEST_ORDER_003&transaction_id=TXN_003&state=1&ret_code=00&hash=WRONG_HASH_VALUE"
```

**預期行為**:
- 雜湊值驗證失敗（記錄警告）
- 仍然處理交易（根據您的安全政策決定）
- 記錄詳細的驗證失敗資訊

### 3.2 驗證正確的雜湊值
```bash
# 步驟 1: 取得正確的雜湊值
curl "https://your-domain.com/api/TSPG/test-hash-calculation?orderId=TEST_ORDER_003&transactionId=TXN_003&state=1"

# 步驟 2: 使用正確的雜湊值驗證
curl "https://your-domain.com/api/TSPG/verify-hash?orderId=TEST_ORDER_003&transactionId=TXN_003&state=1&hash=CORRECT_HASH"
```

**預期回應**:
```json
{
  "success": true,
  "message": "雜湊值驗證通過",
  "order_id": "TEST_ORDER_003",
  "transaction_id": "TXN_003",
  "state": "1"
}
```

## 場景 4: 退款流程

### 4.1 申請退款
```http
POST /api/TSPG/refund
Content-Type: application/json

{
  "orderId": "TEST_ORDER_001",
  "refundAmount": 50000,
  "reason": "客戶要求退款"
}
```

**預期回應**:
```json
{
  "success": true,
  "order_id": "TEST_ORDER_001",
  "message": "退款申請成功"
}
```

### 4.2 退款通知回調
```bash
curl -X POST https://your-domain.com/api/TSPG/refund-notify \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "order_id=TEST_ORDER_001" \
  -d "transaction_id=REFUND_TXN_001" \
  -d "state=1" \
  -d "ret_code=00" \
  -d "cost=50000" \
  -d "hash=CALCULATED_HASH"
```

## 場景 5: 訂單查詢

### 5.1 查詢存在的訂單
```bash
curl https://your-domain.com/api/TSPG/query-order/TEST_ORDER_001
```

**預期回應**:
```json
{
  "success": true,
  "order_id": "TEST_ORDER_001",
  "status_code": "0000",
  "message": "查詢成功",
  "data": {
    "code": "0000",
    "msg": "查詢成功",
    "uid": "TEST_ORDER_001"
  }
}
```

### 5.2 查詢不存在的訂單
```bash
curl https://your-domain.com/api/TSPG/query-order/NON_EXISTENT_ORDER
```

**預期回應**:
```json
{
  "success": false,
  "status_code": "9999",
  "message": "訂單不存在"
}
```

## 場景 6: 健康檢查與監控

### 6.1 健康狀態檢查
```bash
curl https://your-domain.com/api/TSPG/health
```

**預期回應**:
```json
{
  "status": "healthy",
  "timestamp": "2024-01-01T10:00:00",
  "version": "2.14",
  "service": "TSPG API Controller",
  "endpoints": {
    "post_back_url": "/api/TSPG/post-back",
    "result_url": "/api/TSPG/result-notify",
    "payment_notify": "/api/TSPG/payment-notify",
    "refund_notify": "/api/TSPG/refund-notify"
  },
  "configuration": {
    "store_key_configured": true,
    "store_iv_configured": true,
    "post_back_url": "https://your-domain.com/api/TSPG/post-back",
    "result_url": "https://your-domain.com/api/TSPG/result-notify"
  }
}
```

## 場景 7: 邊界條件測試

### 7.1 缺少必要參數
```bash
curl "https://your-domain.com/api/TSPG/post-back?state=1&hash=ABC"
```

**預期回應**: HTTP 400 Bad Request
```json
{
  "success": false,
  "message": "缺少必要參數 order_id"
}
```

### 7.2 空的訂單編號
```bash
curl "https://your-domain.com/api/TSPG/post-back?order_id=&transaction_id=TXN001&state=1&hash=ABC"
```

**預期回應**: HTTP 400 Bad Request

### 7.3 異常金額
```bash
# 金額為 0
curl "https://your-domain.com/api/TSPG/post-back?order_id=TEST&transaction_id=TXN&state=1&cost=0&hash=HASH"

# 負數金額
curl "https://your-domain.com/api/TSPG/post-back?order_id=TEST&transaction_id=TXN&state=1&cost=-1000&hash=HASH"
```

## 場景 8: 併發測試

### 8.1 重複通知處理
模擬 TSPG 重複發送通知：

```bash
# 第一次通知
curl -X POST https://your-domain.com/api/TSPG/result-notify -d "order_id=ORDER_CONCURRENT&transaction_id=TXN_C1&state=1&hash=HASH1"

# 第二次通知（相同訂單）
curl -X POST https://your-domain.com/api/TSPG/result-notify -d "order_id=ORDER_CONCURRENT&transaction_id=TXN_C1&state=1&hash=HASH1"
```

**預期行為**:
- 第一次: 正常處理，更新訂單狀態
- 第二次: 檢測到重複，返回 OK 但不重複處理業務邏輯

## 測試用雜湊計算工具

### PowerShell 版本
```powershell
function Calculate-TSPGHash {
    param(
        [string]$StoreKey,
        [string]$TransactionId,
        [string]$OrderId,
        [string]$State,
        [string]$StoreIV
    )
    
    $hashString = "$StoreKey$TransactionId$OrderId$State$StoreIV"
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($hashString)
    $hashBytes = $sha256.ComputeHash($bytes)
    $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToUpper()
    
    Write-Host "Hash String: $hashString"
    Write-Host "Calculated Hash: $hash"
    
    return $hash
}

# 使用範例
Calculate-TSPGHash -StoreKey "YOUR_KEY" -TransactionId "TXN001" -OrderId "ORDER001" -State "1" -StoreIV "YOUR_IV"
```

### Python 版本
```python
import hashlib

def calculate_tspg_hash(store_key, transaction_id, order_id, state, store_iv):
    hash_string = f"{store_key}{transaction_id}{order_id}{state}{store_iv}"
    hash_value = hashlib.sha256(hash_string.encode('utf-8')).hexdigest().upper()
    
    print(f"Hash String: {hash_string}")
    print(f"Calculated Hash: {hash_value}")
    
    return hash_value

# 使用範例
calculate_tspg_hash("YOUR_KEY", "TXN001", "ORDER001", "1", "YOUR_IV")
```

## 測試檢查清單

### 功能測試
- [ ] 正常付款流程（成功）
- [ ] 正常付款流程（失敗）
- [ ] 訂單取消
- [ ] 訂單退款
- [ ] 訂單查詢
- [ ] 交易記錄查詢

### 雜湊驗證測試
- [ ] 正確的雜湊值通過驗證
- [ ] 錯誤的雜湊值被拒絕
- [ ] 缺少雜湊值的處理
- [ ] 雜湊計算測試工具正常運作

### Webhook 測試
- [ ] post_back_url 正常接收和處理
- [ ] result_url 正常接收和返回 OK
- [ ] refund_notify 正常處理
- [ ] 重複通知正確處理

### 錯誤處理測試
- [ ] 缺少必要參數
- [ ] 無效的訂單編號
- [ ] 網路錯誤處理
- [ ] 超時處理

### 安全性測試
- [ ] HTTPS 連接
- [ ] 雜湊值驗證
- [ ] 敏感資訊不記錄在日誌
- [ ] SQL 注入防護

### 效能測試
- [ ] 單一請求響應時間 < 1秒
- [ ] 併發 10 個請求正常處理
- [ ] 重複通知不影響效能

### 監控測試
- [ ] 健康檢查端點正常
- [ ] 日誌正確記錄
- [ ] 錯誤告警機制運作
- [ ] 測試工具端點可用

## 測試報告範本

### 測試執行摘要
```
測試日期: 2024-XX-XX
測試人員: [姓名]
測試環境: [測試/正式]
測試範圍: TSPG 金流整合

總測試案例: XX
通過: XX
失敗: XX
待修復: XX
```

### 測試結果詳情
| 場景 | 測試案例 | 結果 | 備註 |
|------|---------|------|------|
| 場景1 | 正常付款流程 | ? | - |
| 場景2 | 付款失敗流程 | ? | - |
| 場景3 | 雜湊驗證 | ? | - |
| ... | ... | ... | ... |

---
**版本**: 2.14
**最後更新**: 2024
