# MyPayController 重構後測試檢查清單

## ? 功能測試

### 1. 金流回傳處理 (PaymentNotify)

#### 1.1 成功交易 - 奉獻類型
- [ ] 測試資料：PRC=250（付款成功），奉獻類別=100000000（十一奉獻）
- [ ] 預期結果：
  - [ ] CRM 收費單狀態更新為「已繳費」
  - [ ] new_fee_really_paid 設為應付金額
  - [ ] new_difference_fee_paid 設為 0
  - [ ] new_pay_date 記錄付款時間
  - [ ] new_pay_way 設為「信用卡」
  - [ ] new_description 包含完整交易資訊
  - [ ] LINE 發送奉獻成功訊息
  - [ ] API 回傳 "8888"

#### 1.2 成功交易 - 課程類型
- [ ] 測試資料：PRC=250，有 new_course_id 關聯
- [ ] 預期結果：
  - [ ] CRM 更新同上
  - [ ] LINE 發送課程繳費成功訊息（包含課程名稱、時間、地點）
  - [ ] API 回傳 "8888"

#### 1.3 成功交易 - 一般類型
- [ ] 測試資料：PRC=250，非奉獻非課程
- [ ] 預期結果：
  - [ ] CRM 更新同上
  - [ ] LINE 發送一般繳費成功訊息
  - [ ] API 回傳 "8888"

#### 1.4 失敗交易 - 各類型
- [ ] 測試資料：PRC=300（交易失敗）
- [ ] 預期結果：
  - [ ] CRM 只更新 new_description（不更新付款狀態）
  - [ ] LINE 發送失敗訊息（包含失敗原因和處理建議）
  - [ ] API 回傳 "8888"

#### 1.5 邊界條件測試
- [ ] 回傳資料為 null
  - [ ] 預期：記錄警告，回傳 BadRequest
- [ ] 缺少必要欄位（uid, key, prc, order_id）
  - [ ] 預期：驗證失敗，記錄警告，回傳 "8888"
- [ ] 找不到對應收費單
  - [ ] 預期：記錄警告，回傳 "8888"
- [ ] 連絡人無 LINE ID
  - [ ] 預期：CRM 正常更新，記錄警告（無法發送 LINE），回傳 "8888"
- [ ] LINE 發送失敗
  - [ ] 預期：CRM 正常更新，記錄錯誤，回傳 "8888"

### 2. 成功頁面 (PaymentSuccess)
- [ ] GET /api/MyPay/success?order_id=TEST123
- [ ] 預期結果：
  - [ ] ViewBag.OrderId = "TEST123"
  - [ ] ViewBag.IsSuccess = true
  - [ ] 顯示成功訊息
  - [ ] 返回 PaymentResult View

### 3. 失敗頁面 (PaymentFailure)
- [ ] GET /api/MyPay/failure?order_id=TEST123&msg=測試錯誤
- [ ] 預期結果：
  - [ ] ViewBag.OrderId = "TEST123"
  - [ ] ViewBag.IsSuccess = false
  - [ ] 顯示失敗訊息：「付款失敗：測試錯誤」
  - [ ] 返回 PaymentResult View

## ?? 服務單元測試建議

### MyPayMessageBuilder
```csharp
[Test]
public void BuildDedicationSuccessMessage_ShouldContainAllRequiredInfo()
{
    // Arrange
    var builder = new MyPayMessageBuilder();
    var fullName = "測試會友";
    var orderId = "ORDER123";
    var transactionId = "TX123";
    var amount = 1000m;
    var category = "十一奉獻";
    var paymentTime = new DateTime(2024, 1, 1, 12, 0, 0);

    // Act
    var message = builder.BuildDedicationSuccessMessage(
        fullName, orderId, transactionId, amount, category, paymentTime);

    // Assert
    Assert.That(message, Does.Contain("測試會友"));
    Assert.That(message, Does.Contain("ORDER123"));
    Assert.That(message, Does.Contain("TX123"));
    Assert.That(message, Does.Contain("NT$ 1,000"));
    Assert.That(message, Does.Contain("十一奉獻"));
    Assert.That(message, Does.Contain("2024/01/01 12:00:00"));
}
```

### MyPayStatusHelper
```csharp
[Test]
public void IsSuccessfulPaymentStatus_WithCode250_ShouldReturnTrue()
{
    // Arrange
    var helper = new MyPayStatusHelper(mockLogger);

    // Act
    var result = helper.IsSuccessfulPaymentStatus("250");

    // Assert
    Assert.IsTrue(result);
}

[Test]
public void IsSuccessfulPaymentStatus_WithCode300_ShouldReturnFalse()
{
    // Arrange
    var helper = new MyPayStatusHelper(mockLogger);

    // Act
    var result = helper.IsSuccessfulPaymentStatus("300");

    // Assert
    Assert.IsFalse(result);
}

[Test]
public void ParseFinishTime_WithValidFormat_ShouldReturnCorrectDateTime()
{
    // Arrange
    var helper = new MyPayStatusHelper(mockLogger);
    var finishtime = "20240101120000"; // 2024-01-01 12:00:00

    // Act
    var result = helper.ParseFinishTime(finishtime);

    // Assert
    Assert.AreEqual(2024, result.Year);
    Assert.AreEqual(1, result.Month);
    Assert.AreEqual(1, result.Day);
    Assert.AreEqual(12, result.Hour);
    Assert.AreEqual(0, result.Minute);
    Assert.AreEqual(0, result.Second);
}
```

### MyPayFeeTypeHelper
```csharp
[Test]
public void DetermineFeeType_WithCourseId_ShouldReturnCourse()
{
    // Arrange
    var helper = new MyPayFeeTypeHelper(mockLogger);
    var mockFeeEntity = CreateMockFeeEntityWithCourseId();

    // Act
    var result = helper.DetermineFeeType(mockUtility, mockFeeEntity);

    // Assert
    Assert.AreEqual(FeeType.Course, result);
}

[Test]
public void GetDedicationCategoryName_WithCode100000000_ShouldReturn十一奉獻()
{
    // Arrange
    var helper = new MyPayFeeTypeHelper(mockLogger);

    // Act
    var result = helper.GetDedicationCategoryName(100000000);

    // Assert
    Assert.AreEqual("十一奉獻", result);
}
```

## ?? 整合測試場景

### 場景 1：完整的奉獻成功流程
```
1. 金流回傳 PRC=250, order_id=ORDER001, cost=1000
2. 查詢 CRM 找到收費單（奉獻類別=十一奉獻）
3. 更新 CRM 收費單狀態為已繳費
4. 建立奉獻成功 LINE 訊息
5. 發送 LINE 訊息到會友
6. 回傳 "8888" 給金流平台
```

### 場景 2：課程繳費失敗流程
```
1. 金流回傳 PRC=300, order_id=ORDER002
2. 查詢 CRM 找到收費單（關聯課程 ID）
3. 只更新 CRM 的 description（不更新付款狀態）
4. 建立課程繳費失敗 LINE 訊息（包含失敗原因）
5. 發送 LINE 訊息到會友
6. 回傳 "8888" 給金流平台
```

### 場景 3：找不到收費單
```
1. 金流回傳 order_id=NOTFOUND
2. 查詢 CRM 找不到對應收費單
3. 記錄警告日誌
4. 直接回傳 "8888"（不發送 LINE，不更新 CRM）
```

## ?? 效能測試

### 1. 回應時間測試
- [ ] 正常情況下，PaymentNotify 端點回應時間 < 3 秒
- [ ] 高負載情況下（100 並發請求），平均回應時間 < 5 秒

### 2. 資料庫連線測試
- [ ] 確認每次請求後 ToolUtilityClass 正確 Dispose
- [ ] 無資料庫連線洩漏

### 3. 記憶體測試
- [ ] 處理 1000 筆交易後，記憶體使用量穩定
- [ ] 無記憶體洩漏

## ??? 安全性測試

### 1. 輸入驗證
- [ ] SQL Injection 測試（order_id, uid, key 等參數）
- [ ] XSS 測試（msg 參數）
- [ ] 超長字串測試

### 2. 授權測試
- [ ] 確認 PaymentNotify 端點可被金流平台呼叫
- [ ] 確認 success/failure 端點可被一般使用者訪問

## ?? 日誌檢查

### 1. 正常流程日誌
確認以下日誌項目存在且格式正確：
- [ ] `[MyPay回傳] 收到金流回傳`
- [ ] `[MyPay完整回傳資料]`
- [ ] `[MyPay回傳] 開始驗證欄位`
- [ ] `[MyPay回傳] 欄位驗證通過`
- [ ] `[MyPay回傳] 交易狀態判定`
- [ ] `[MyPay回傳] 找到收費單`
- [ ] `[MyPay回傳] 收費單類型`
- [ ] `[MyPay回傳] 連絡人`
- [ ] `[MyPay回傳] 收費單已更新`
- [ ] `[MyPay回傳] LINE成功通知已發送` 或 `LINE失敗通知已發送`
- [ ] `[MyPay回傳] 處理完成`

### 2. 異常流程日誌
- [ ] 驗證失敗時記錄警告
- [ ] 找不到收費單時記錄警告
- [ ] LINE 發送失敗時記錄錯誤
- [ ] 系統異常時記錄錯誤堆疊

## ?? 向下相容性檢查

- [ ] 舊版 API 呼叫仍可正常運作
- [ ] 舊版方法 `UpdateFeeEntityForSuccessWithMyPay` 可正常使用
- [ ] 舊版方法 `SendPaymentNotificationByType` 可正常使用
- [ ] 資料模型 `MyPayReturnModel` 完全相容

## ?? LINE 訊息內容檢查

### 奉獻成功訊息範例
```
【金流付款成功通知】

親愛的 張三，您好！

您的奉獻已成功完成，感謝您的支持！

付款資訊：
姓名：張三
奉獻類別：十一奉獻
訂單編號：ORDER123
交易編號：TX123456
付款金額：NT$ 1,000
付款時間：2024/01/01 12:00:00
付款方式：信用卡

願上帝賜福與您！
```

### 課程繳費失敗訊息範例
```
【金流付款失敗通知】

親愛的 李四，您好！

很抱歉，您的課程繳費未能完成。

失敗原因：信用卡額度不足，請使用其他卡片或聯繫發卡銀行

課程資訊：
姓名：李四
課程名稱：聖經研讀班
交易編號：TX789012
應繳金額：NT$ 500
嘗試時間：2024/01/01 14:30:00

您可以：
1. 重新嘗試付款
2. 更換其他信用卡
3. 聯繫教會辦公室尋求協助

如有任何問題，請隨時與我們聯繫。
```

## ? 最終檢查清單

### 建置與部署
- [x] 專案建置成功（無編譯錯誤）
- [ ] 所有警告已檢視並處理
- [ ] 相依套件版本正確
- [ ] appsettings.json 設定正確

### 程式碼品質
- [x] 所有服務已註冊到 DI 容器
- [x] 命名空間正確
- [x] 檔案結構清晰
- [x] 註解完整且準確
- [x] 無重複程式碼

### 文件
- [x] 重構說明文件已建立
- [x] 測試檢查清單已建立
- [ ] API 文件已更新
- [ ] README 已更新

### 測試
- [ ] 單元測試完成
- [ ] 整合測試完成
- [ ] 效能測試完成
- [ ] 安全性測試完成

---

## ?? 測試執行記錄

### 測試日期：____/____/____
### 測試人員：________________

| 測試項目 | 結果 | 備註 |
|---------|------|------|
| 奉獻成功流程 | ? 通過 ? 失敗 | |
| 課程成功流程 | ? 通過 ? 失敗 | |
| 一般成功流程 | ? 通過 ? 失敗 | |
| 奉獻失敗流程 | ? 通過 ? 失敗 | |
| 課程失敗流程 | ? 通過 ? 失敗 | |
| 一般失敗流程 | ? 通過 ? 失敗 | |
| 邊界條件測試 | ? 通過 ? 失敗 | |
| 效能測試 | ? 通過 ? 失敗 | |
| 安全性測試 | ? 通過 ? 失敗 | |
| LINE 訊息格式 | ? 通過 ? 失敗 | |

### 整體評估：? 通過 ? 需修正

### 發現問題：
1. _________________________________________________
2. _________________________________________________
3. _________________________________________________

### 修正建議：
1. _________________________________________________
2. _________________________________________________
3. _________________________________________________
