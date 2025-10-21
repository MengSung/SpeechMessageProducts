# TSPG 後台通知 (PaymentNotify) 完整實作說明

## 概述

根據 **TSPG REST API v2.14 規格書 4.9 節「信用卡授權交易回應後台通知」**，完整實作了 `PaymentNotify()` 方法。

## 端點資訊

- **路徑**: `/api/TSPG/payment-notify`
- **HTTP 方法**: POST / GET
- **內容類型**: `application/json; charset=utf-8`

## 實作的參數完整清單

### 1. 外層基本欄位

| 參數名稱 | 資料型態 | 說明 | 必要 |
|---------|---------|------|-----|
| ver | string | 格式版本號 (與交易請求電文相同) | Y |
| mid | string(15) | 特店代號 | Y |
| s_mid | string(15) | 子特店代號 | C |
| tid | string(8) | 端末代號 | Y |
| pay_type | int(1) | 付款類別 (1:信用卡) | Y |
| tx_type | int(1) | 交易類別 (1:授權 3:請款 4:取消請款 5:退貨 6:取消退貨 7:查詢 8:取消授權) | Y |

### 2. params 參數清單

#### 2.1 必要參數

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| ret_code | string(6) | 交易結果回應碼 | RetCode |
| ret_msg | string | 回傳訊息 | RetMsg |
| order_no | string(23) | 訂單號碼 | OrderNo |
| auth_id_resp | string(6) | 授權碼 | AuthIdResp |
| rrn | string(12) | 調單號碼 | TransactionId |

#### 2.2 條件必要參數 (當交易成功時)

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| carrierId2 | string(50) | 信用卡載具資訊 | CarrierId2 |
| order_status | string(2) | 訂單狀態碼 | State |
| auth_type | string(3) | 授權方式 (SSL/3D) | - |
| cur | string(3) | 幣別 (NTD:新台幣) | Currency |
| purchase_date | string(19) | 採購日期 (yyyy-MM-dd HH:mm:ss) | PayTime |

#### 2.3 金額相關欄位

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| tx_amt | string(12) | 交易金額 (包含兩位小數) | Cost |
| settle_amt | string(12) | 請款金額 | - |
| settle_seq | string(12) | 請款批號 | - |
| settle_date | string(10) | 請款日期 (yyyy-MM-dd) | - |

#### 2.4 退貨相關欄位

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| refund_trans_amt | string(12) | 退貨金額 | - |
| refund_rrn | string(12) | 退貨調單編號 | - |
| refund_auth_id_resp | string(6) | 退貨授權碼 | - |
| refund_date | string(10) | 退貨日期 (yyyy-MM-dd) | - |

#### 2.5 紅利相關欄位

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| redeem_order_no | string(12) | 紅利訂單編號 | - |
| redeem_pt | string(7) | 折抵點數 | - |
| redeem_amt | string(12) | 折抵金額 | - |
| post_redeem_amt | string(12) | 實付金額 | - |
| post_redeem_pt | string(7) | 剩餘點數 | - |

#### 2.6 分期相關欄位

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| install_order_no | string(12) | 分期訂單號碼 | - |
| install_period | string(2) | 分期期數 | - |
| install_down_pay | string(12) | 首期金額 | - |
| install_pay | string(12) | 每期金額 | - |
| install_down_pay_fee | string(12) | 首期手續費 | - |
| install_pay_fee | string(12) | 每期手續費 | - |

#### 2.7 卡號資訊

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| first_6_digit_of_pan | string(6) | 信用卡卡號前6碼 | First6DigitOfPan |
| last_4_digit_of_pan | string(4) | 信用卡卡號後4碼 | Last4DigitOfPan |

#### 2.8 DCC 交易專用參數

| 參數名稱 | 資料型態 | 說明 | 實作欄位 |
|---------|---------|------|---------|
| ch_amt | string(12) | 交易金額(以持卡人母國幣別計算) | ChAmt |
| ch_currency | string(3) | 持卡人母國幣別(ISO 4217) | ChCurrency |
| ex_rate | string(10) | 轉換匯率 (已計入貼水費率) | ExRate |
| markup_rate | string(5) | 貼水費率(%) | MarkupRate |

## 處理流程

### 1. 接收通知

```csharp
// 讀取 JSON 請求內容
string requestBody = await reader.ReadToEndAsync();

// 解析 JSON 結構
dynamic jsonData = JsonConvert.DeserializeObject(requestBody);
```

### 2. 解析所有參數

- 外層欄位：ver, mid, s_mid, tid, pay_type, tx_type
- params 清單：所有業務參數

### 3. 判斷交易結果

```csharp
bool isSuccess = notification.RetCode == "00";
```

### 4. 更新收費單 (付款成功時)

- 設定付款狀態為「信用卡已繳費」(100000001)
- 更新實收金額
- 計算差額
- 設定付款日期
- 記錄交易資訊
- 發送 LINE 通知

### 5. 回應 TSPG

```json
// 成功
{
    "status": "success",
    "message": "通知已接收並處理"
}

// 失敗 (但已收到通知)
{
    "status": "received",
    "message": "付款失敗通知已接收"
}
```

## 日誌記錄

系統會記錄以下資訊：

1. **基本交易資訊**
   - 訂單號碼
   - 調單號
   - 授權碼
   - 結果碼
   - 訊息

2. **金額資訊**
   - 交易金額
   - DCC 金額 (如適用)

3. **卡號資訊**
   - 卡號前6碼 + 後4碼

4. **載具資訊**
   - 信用卡載具

5. **原始 JSON**
   - 完整的請求內容

## 後台通知範例

### 請求範例

```
POST https://yourserver.com/api/TSPG/payment-notify HTTP/1.1
Host: yourserver.com
Content-Type: application/json; charset=utf-8
Content-Length: 179

{
  "ver":"1.0.0",
  "mid":"999000123456789",
  "tid":"T0000000",
  "pay_type":1,
  "tx_type":1,
  "params": {
    "ret_code":"00",
    "order_no":"NO01234567",
    "auth_id_resp":"001241",
    "rrn":"128417503172",
    "carrierId2":"432198hV10AmV1/SHjbun1BtQeDAeeXppEF85HdVfIdY4ANJ0=",
    "tx_amt":"120000",
    "purchase_date":"2024-01-15 14:30:25",
    "first_6_digit_of_pan":"424242",
    "last_4_digit_of_pan":"4242"
  }
}
```

### 回應範例

```json
{
    "status": "success",
    "message": "通知已接收並處理"
}
```

## DCC 交易特殊處理

當交易為 DCC 交易時，會額外回傳以下參數：

- **ch_amt**: 持卡人母國幣別金額
- **ch_currency**: 持卡人母國幣別代碼
- **ex_rate**: 轉換匯率 (已計入貼水費率)
- **markup_rate**: 貼水費率百分比

系統會記錄這些資訊到日誌中。

## 錯誤處理

1. **JSON 解析失敗**: 回傳 500 錯誤
2. **資料庫更新失敗**: 記錄錯誤但回傳成功 (避免 TSPG 重複通知)
3. **找不到收費單**: 記錄警告但回傳成功

## 安全性考量

1. **HTTPS 要求**: result_url 必須使用 HTTPS
2. **SSL/TLS 版本**: 必須支援 TLS v1.2 以上
3. **簽章驗證**: (如需要可另行實作)

## 測試建議

1. 使用 TSPG 測試環境進行完整測試
2. 驗證所有參數都能正確解析
3. 測試各種交易類型 (授權、請款、退貨等)
4. 測試 DCC 交易
5. 測試分期付款
6. 測試紅利交易

## 注意事項

1. **重複通知**: TSPG 可能會重複發送通知，需確保處理具有冪等性
2. **超時重試**: 如果回應超時，TSPG 會重試
3. **順序問題**: 前台通知和後台通知的到達順序不固定
4. **金額格式**: 金額包含兩位小數 (如 100 代表 1.00 元)

## 相關文件

- TSPG REST API v2.14 使用手冊
- 4.9 信用卡授權交易回應後台通知
- 5.1 回應碼 (ret_code) 說明

## 更新記錄

- 2024-01-XX: 完整實作 4.9 規格的所有參數
- 支援所有交易類型的參數解析
- 新增完整的日誌記錄
- 新增 DCC 交易支援
