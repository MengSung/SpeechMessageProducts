# TSPG 台新金流實現快速參考

## 核心端點

### 1. post_back_url (用戶返回)
```
URL: /api/TSPG/post-back
方法: GET/POST
用途: 用戶付款後瀏覽器跳轉回商戶網站
```

**接收參數**:
- `order_id`: 訂單編號
- `transaction_id`: 交易編號
- `state`: 狀態 (1=成功, 0=失敗)
- `ret_code`: 返回碼 (00/0000=成功)
- `ret_msg`: 返回訊息
- `cost`: 交易金額
- `hash`: 雜湊值

**處理流程**:
1. 接收參數
2. 驗證雜湊值
3. 判斷交易狀態
4. 重導向至成功/失敗頁面

### 2. result_url (後端通知)
```
URL: /api/TSPG/result-notify
方法: POST/GET
用途: TSPG 主動通知交易結果
```

**重要**: 必須返回 "OK" 或確認訊息，否則 TSPG 會重複通知

**處理流程**:
1. 接收通知
2. 驗證雜湊值
3. 更新訂單狀態
4. 執行業務邏輯
5. 返回 OK 給 TSPG

## 雜湊值驗證公式

```
Hash = SHA256(StoreKey + TransactionId + OrderId + State + StoreIV)
```

### 計算範例

假設:
- StoreKey = "ABC123"
- TransactionId = "TXN001"
- OrderId = "ORDER001"
- State = "1"
- StoreIV = "XYZ789"

計算字串:
```
ABC123 + TXN001 + ORDER001 + 1 + XYZ789
= ABC123TXN001ORDER0011XYZ789
```

然後進行 SHA256 運算，結果轉為大寫十六進制。

## 配置範例

```json
{
  "TSPG": {
    "StoreId": "999812777000198",
    "StoreKey": "your_32_char_key_here_xxxxx",
    "StoreIV": "your_16_char_iv_here_xxxx",
    "ApiBaseUrl": "https://tspg.taishinbank.com.tw/tspgapi/restapi",
    "TerminalId": "T0000000",
    "POST_BACK_URL": "https://yourdomain.com/api/TSPG/post-back",
    "RESULT_URL": "https://yourdomain.com/api/TSPG/result-notify"
  }
}
```

## 測試步驟

### 1. 測試健康狀態
```bash
curl https://yourdomain.com/api/TSPG/health
```

### 2. 測試雜湊計算
```bash
curl "https://yourdomain.com/api/TSPG/test-hash-calculation?orderId=TEST001&transactionId=TXN001&state=1"
```

### 3. 驗證雜湊值
```bash
curl "https://yourdomain.com/api/TSPG/verify-hash?orderId=ORDER001&transactionId=TXN001&state=1&hash=YOUR_HASH_HERE"
```

## 常見問題

### Q1: 雜湊值驗證失敗
**檢查**:
- [ ] StoreKey 和 StoreIV 是否正確
- [ ] 參數順序: StoreKey + TransactionId + OrderId + State + StoreIV
- [ ] 是否使用 UTF-8 編碼
- [ ] Hash 是否為大寫

### Q2: TSPG 重複通知
**原因**: result_url 沒有返回 OK
**解決**: 確保端點返回 HTTP 200 + {"status": "OK"}

### Q3: 用戶無法返回網站
**檢查**:
- [ ] post_back_url 必須是 HTTPS
- [ ] 網址必須可從外部訪問
- [ ] 防火牆設定

## 交易狀態碼

| state | ret_code | 說明 |
|-------|----------|------|
| 1 | 00/0000 | 交易成功 |
| 0 | 其他 | 交易失敗 |

## API 端點速查

| 功能 | 路徑 | 方法 |
|------|------|------|
| 用戶返回 | `/api/TSPG/post-back` | GET/POST |
| 後端通知 | `/api/TSPG/result-notify` | POST |
| 退款通知 | `/api/TSPG/refund-notify` | POST |
| 建立付款 | `/api/TSPG/create-payment` | POST |
| 查詢訂單 | `/api/TSPG/query-order/{orderId}` | GET |
| 取消訂單 | `/api/TSPG/cancel-order/{orderId}` | POST |
| 申請退款 | `/api/TSPG/refund` | POST |
| 健康檢查 | `/api/TSPG/health` | GET |

## 日誌範例

成功案例:
```
[TSPG PostBackUrl] OrderId: ORDER001, TransactionId: TXN001, State: 1
[TSPG Hash驗證] 計算結果: ABCD1234...
[TSPG Hash驗證] 成功 - 雜湊值匹配
[TSPG PostBackUrl] 重導向至成功頁面
```

失敗案例:
```
[TSPG Hash驗證] 失敗 - 雜湊值不匹配
[TSPG Hash驗證] 預期: ABCD1234...
[TSPG Hash驗證] 實際: EFGH5678...
```

## 上線前檢查

- [ ] 已配置正式環境金鑰
- [ ] 網址使用 HTTPS
- [ ] 雜湊驗證測試通過
- [ ] 完整流程測試通過
- [ ] 日誌正常記錄
- [ ] 錯誤處理完善

## 緊急聯絡

- 台新銀行技術支援: [電話]
- 內部技術團隊: [聯絡方式]

---
**建立日期**: 2024
**版本**: 2.14
