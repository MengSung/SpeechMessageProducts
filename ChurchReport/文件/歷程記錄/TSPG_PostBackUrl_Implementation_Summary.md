# TSPG post_back_url 前台通知完整實現總結

## 概述

根據 TSPG.pdf (台新規格.txt) 的規格文件，完整實現了 `post_back_url` 前台通知端點，包含所有參數名稱和處理邏輯。

## 實現內容

### 1. TSPGPaymentNotification 模型擴充

在 `ChurchReport\Tools\TSPGModels.cs` 中為 `TSPGPaymentNotification` 類別新增了以下屬性：

#### 前台通知特殊參數（需事先向台新申請）

```csharp
/// <summary>
/// 卡號前6碼 (前台通知專用，需事先向台新申請)
/// </summary>
public string First6DigitOfPan { get; set; }

/// <summary>
/// 卡號後4碼 (前台通知專用，需事先向台新申請)
/// </summary>
public string Last4DigitOfPan { get; set; }

/// <summary>
/// 信用卡載具資訊 (前台通知專用，非必有值)
/// </summary>
public string CarrierId2 { get; set; }
```

#### DCC 交易專用參數（僅DCC交易回傳）

```csharp
/// <summary>
/// 交易金額 (以持卡人母國幣別計算)，僅DCC交易回傳此參數
/// </summary>
public decimal? ChAmt { get; set; }

/// <summary>
/// 持卡人母國幣別 (ISO 4217 Numeric Code)，僅DCC交易回傳此參數
/// </summary>
public string ChCurrency { get; set; }

/// <summary>
/// 轉換匯率 (1元台幣對持卡人本國幣別的匯率)，僅DCC交易回傳此參數
/// </summary>
public decimal? ExRate { get; set; }

/// <summary>
/// 貼水費率(%)，僅DCC交易回傳此參數
/// </summary>
public decimal? MarkupRate { get; set; }
```

### 2. TSPGWebhookHandler 參數映射更新

在 `ChurchReport\Tools\TSPGWebhookHandler.cs` 中更新了三個參數映射方法：

#### MapFormToNotification (處理 POST Form 資料)
```csharp
// 前台通知特殊參數
n.First6DigitOfPan = form["first_6_digit_of_pan"];
n.Last4DigitOfPan = form["last_4_digit_of_pan"];
n.CarrierId2 = form["carrierId2"];

// DCC 交易專用參數
if (decimal.TryParse(form["ch_amt"], out var chAmt)) n.ChAmt = chAmt;
n.ChCurrency = form["ch_currency"];
if (decimal.TryParse(form["ex_rate"], out var exRate)) n.ExRate = exRate;
if (decimal.TryParse(form["markup_rate"], out var markupRate)) n.MarkupRate = markupRate;
```

#### MapQueryToNotification (處理 GET QueryString 資料)
支援兩個版本：`NameValueCollection` 和 `IQueryCollection`，映射相同的參數。

#### MapJsonToNotification (處理 JSON 資料)
```csharp
// 前台通知特殊參數
n.First6DigitOfPan = V("first_6_digit_of_pan", "first6DigitOfPan", "cardPrefix");
n.Last4DigitOfPan = V("last_4_digit_of_pan", "last4DigitOfPan", "cardSuffix");
n.CarrierId2 = V("carrierId2", "carrier_id_2", "carrierIdTwo");

// DCC 交易專用參數
n.ChAmt = VDN("ch_amt", "chAmt", "cardholderAmount");
n.ChCurrency = V("ch_currency", "chCurrency", "cardholderCurrency");
n.ExRate = VDN("ex_rate", "exRate", "exchangeRate");
n.MarkupRate = VDN("markup_rate", "markupRate");
```

### 3. TSPGController 前台通知端點實現

在 `ChurchReport\Controllers\TSPGController.cs` 中實現了完整的 `post_back_url` 處理邏輯：

#### 主要端點

```csharp
[HttpGet("payment-return")]
[HttpPost("payment-return")]
public IActionResult PaymentReturn()

[HttpGet("post-back")]
[HttpPost("post-back")]
public IActionResult PostBack()  // 別名，與 payment-return 相同
```

#### 處理的參數（完整列表）

##### 基本參數
- `s_mid` - 次特店代號
- `ret_code` - 交易結果回應碼
- `tx_type` - 交易類型 (1:授權 3:請款 4:取消請款 5:退貨 6:取消退貨 7:查詢 8:取消授權)
- `order_no` - 訂單號碼
- `order_id` - 訂單編號
- `ret_msg` - 回傳訊息
- `auth_id_resp` - 授權碼回應
- `state` - 付款狀態 (1: 成功, 0: 失敗)
- `transaction_id` - 交易編號

##### 前台通知特殊參數（需事先向台新申請）
- `first_6_digit_of_pan` - 卡號前6碼
- `last_4_digit_of_pan` - 卡號後4碼
- `carrierId2` - 信用卡載具資訊

##### DCC 交易專用參數（僅DCC交易回傳）
- `ch_amt` - 交易金額 (以持卡人母國幣別計算)
- `ch_currency` - 持卡人母國幣別 (ISO 4217 Numeric Code)
- `ex_rate` - 轉換匯率 (1元台幣對持卡人本國幣別的匯率)
- `markup_rate` - 貼水費率(%)

##### 其他參數
- `hash` / `signature` - 檢查碼
- `cost` / `amt` - 付款金額
- `actual_cost` - 實際付款金額
- `pay_type` - 付款方式
- `currency` / `cur` - 幣別

#### 輔助方法

```csharp
// 從Request中取得參數值 (支援GET和POST)
private string GetParam(string key)

// 從Request中取得decimal參數值
private decimal? GetDecimalParam(string key)

// 記錄前台通知資訊
private void LogPostBackNotification(TSPGPaymentNotification notification)

// 處理付款成功的返回
private IActionResult HandleSuccessfulPaymentReturn(TSPGPaymentNotification notification)

// 處理付款失敗的返回
private IActionResult HandleFailedPaymentReturn(TSPGPaymentNotification notification)
```

## 日誌記錄範例

系統會記錄完整的前台通知資訊，包含：

```
[TSPG PostBackUrl] 訂單: ORDER001, 交易號: TXN001, 狀態: 1, 結果碼: 00, 交易類型: 1
[TSPG PostBackUrl] 訂單: ORDER001, 交易號: TXN001, 狀態: 1, 結果碼: 00, 交易類型: 1, 卡號: 123456******7890
[TSPG PostBackUrl] 訂單: ORDER001, 交易號: TXN001, 狀態: 1, 結果碼: 00, 交易類型: 1, 載具: /ABC123
[TSPG PostBackUrl] 訂單: ORDER001, 交易號: TXN001, 狀態: 1, 結果碼: 00, 交易類型: 1, DCC金額: 10.50 USD, 匯率: 31.5
```

## 前台通知流程

```
客戶 -> 付款完成 -> TSPG 重定向 -> post_back_url
                                   |
                                   v
                           讀取所有參數
                                   |
                      +------------+------------+
                      |                         |
                    成功                      失敗
                      |                         |
         重定向至成功頁面              重定向至失敗頁面
         /payment-success            /payment-failed
```

## API 端點配置

在 `appsettings.json` 中配置：

```json
{
  "TSPG": {
    "POST_BACK_URL": "https://yourdomain.com/api/TSPG/post-back"
  }
}
```

或使用完整路徑：
```
https://yourdomain.com/api/TSPG/payment-return
```

## 前台通知範例

### HTTP POST Request 格式

```
POST http://xxxTestShop/postBackURL.php HTTP/1.1
Host: 123.123.123.123
Content-Type: application/json
Content-Length: length

s_mid=string&ret_code=string&tx_type=string&order_no=string
 &ret_msg=string&auth_id_resp=string&first_6_digit_of_pan=st
 ring&last_4_digit_of_pan&carrierId2=string
```

### 參數說明對照表

| 參數名稱 | 說明 | 必有 |
|---------|------|------|
| s_mid | 次特店代號，若交易要求時有傳入，必帶此參數 | 條件 |
| ret_code | 交易結果 | 是 |
| tx_type | 交易類型：1:授權 3:請款 4:取消請款 5:退貨 6:取消退貨 7:查詢 8:取消授權 | 是 |
| order_no | 訂單號碼 | 是 |
| ret_msg | 回傳訊息 | 條件 |
| auth_id_resp | 授權碼 | 條件 |
| carrierId2 | 信用卡載具資訊 | 否 |
| first_6_digit_of_pan | 卡號前6碼 | 否* |
| last_4_digit_of_pan | 卡號後4碼 | 否* |
| ch_amt | 交易金額(以持卡人母國幣別計算)，僅DCC交易回傳此參數 | DCC |
| ch_currency | 持卡人母國幣別(ISO 4217 Numeric Code)，僅DCC交易回傳此參數 | DCC |
| ex_rate | 轉換匯率(1元台幣對持卡人本國幣別的匯率)，僅DCC交易回傳此參數 | DCC |
| markup_rate | 貼水費率(%)，僅DCC交易回傳此參數 | DCC |

*需事先向台新申請

## 重要注意事項

### 1. 前台通知特殊參數
- `first_6_digit_of_pan` 和 `last_4_digit_of_pan` 需事先向台新銀行申請才會回傳
- 這些參數非必有值，請確認已申請後再使用

### 2. DCC 交易參數
- DCC (Dynamic Currency Conversion) 相關參數僅在 DCC 交易時才會回傳
- 持卡人母國幣別小數位數請參考台新提供的 DCC Currency List

### 3. 參數支援
- 支援 GET 和 POST 兩種方式
- 支援 Form、QueryString 和 JSON 三種格式
- 參數名稱支援多種命名方式（snake_case、camelCase）

### 4. 付款成功判斷
```csharp
IsPaymentSuccess => State == "1" || RetCode == "0000" || RetCode == "00"
```

## 測試建議

### 1. 測試前台通知基本參數
```bash
curl "https://yourdomain.com/api/TSPG/post-back?s_mid=SUB001&ret_code=00&tx_type=1&order_no=ORDER001&state=1&transaction_id=TXN001&auth_id_resp=123456"
```

### 2. 測試特殊參數（卡號資訊）
```bash
curl "https://yourdomain.com/api/TSPG/post-back?order_no=ORDER001&ret_code=00&state=1&first_6_digit_of_pan=123456&last_4_digit_of_pan=7890&carrierId2=/ABC123"
```

### 3. 測試DCC交易參數
```bash
curl "https://yourdomain.com/api/TSPG/post-back?order_no=ORDER001&ret_code=00&state=1&ch_amt=10.50&ch_currency=840&ex_rate=31.5&markup_rate=1.5"
```

### 4. 測試POST方式
```bash
curl -X POST https://yourdomain.com/api/TSPG/post-back \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "order_no=ORDER001&ret_code=00&state=1&tx_type=1&auth_id_resp=123456"
```

## 與 result_url 的區別

| 特性 | post_back_url (前台通知) | result_url (後台通知) |
|------|-------------------------|---------------------|
| 觸發方式 | 瀏覽器重定向 | TSPG 伺服器 POST |
| HTTP 方法 | GET 或 POST | POST (JSON) |
| 時機 | 同步，付款完成立即 | 非同步，稍後發送 |
| 用途 | 顯示結果頁面給用戶 | 更新訂單狀態 |
| 可見性 | 用戶可見 | 後端處理 |
| 必須回應 | 重定向頁面 | 回應 "OK" |
| 特殊參數 | 包含卡號前後碼、DCC | 完整交易資訊 |

## 檔案修改清單

1. **ChurchReport\Tools\TSPGModels.cs**
   - 新增 `First6DigitOfPan`、`Last4DigitOfPan`、`CarrierId2` 屬性
   - 新增 `ChAmt`、`ChCurrency`、`ExRate`、`MarkupRate` 屬性
   - 更新 `IsPaymentSuccess` 判斷邏輯

2. **ChurchReport\Tools\TSPGWebhookHandler.cs**
   - 更新 `MapFormToNotification` 方法
   - 更新 `MapQueryToNotification` 方法（兩個版本）
   - 更新 `MapJsonToNotification` 方法
   - 新增 `VDN` 輔助方法（nullable decimal）

3. **ChurchReport\Controllers\TSPGController.cs**
   - 新增 `PaymentReturn` 端點（完整實現）
   - 新增 `PostBack` 別名端點
   - 新增 `GetParam` 輔助方法
   - 新增 `GetDecimalParam` 輔助方法
   - 新增 `LogPostBackNotification` 日誌方法
   - 新增 `HandleSuccessfulPaymentReturn` 處理方法
   - 新增 `HandleFailedPaymentReturn` 處理方法
   - 新增 `using System.Collections.Generic` 和 `using System.Linq`

## 版本資訊

- **實現版本**: TSPG REST API v2.14
- **實現日期**: 2024
- **參考文件**: 台新規格.txt (TSPG.pdf)
- **實現狀態**: ? 完成

---

**最後更新**: 2024年
**維護團隊**: ChurchReport 開發團隊
