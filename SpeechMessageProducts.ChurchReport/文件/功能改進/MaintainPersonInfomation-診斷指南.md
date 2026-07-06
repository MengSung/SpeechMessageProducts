# 組員資訊上傳失敗 - 診斷指南

## 問題現象
按下「上傳」按鈕後，前端顯示成功訊息，但資料並未實際更新到 CRM 資料庫。

## 關鍵修復
? **已修復**：在進入背景任務前先取得 `ToolUtility` 實例，避免在背景執行緒中訪問 Controller 實例成員。

## 診斷步驟

### 步驟 1：檢查前端日誌（瀏覽器 Console）

按 F12 開啟開發者工具，切換到 Console 分頁，觀察以下日誌：

```javascript
[Upload] 步驟 1: 關閉編輯儲存格
[Upload] 步驟 2: 儲存待處理的編輯
[Upload] 步驟 3: 檢查是否有修改的資料
[Upload] 步驟 4: 開始上傳 2 筆資料
[Upload] 準備上傳 2 筆已修改的資料
[GetResult] 總可見行數: 50
[GetResult] 已修改的 ContactId 數量: 2
[GetResult] 已修改的 ContactId 清單: [xxx-xxx-xxx, yyy-yyy-yyy]
[GetResult] 加入已修改的資料: 王小明 xxx-xxx-xxx
[GetResult] 加入已修改的資料: 李小華 yyy-yyy-yyy
[GetResult] 最終返回 2 筆已修改的資料
```

**檢查重點：**
- ? `已修改的 ContactId 數量` 是否正確？
- ? `最終返回 X 筆已修改的資料` 是否符合預期？
- ? 如果數量為 0，表示 `modifiedRecords` 沒有正確記錄

### 步驟 2：檢查後端日誌（Visual Studio Output）

在 Visual Studio 中，切換到「輸出」視窗，選擇「偵錯」，觀察以下日誌：

```
[SaveMaintainPersonInfomation] 開始處理
[SaveMaintainPersonInfomation] 資料長度: 1234
[SaveMaintainPersonInfomation] 成功解析到 2 筆資料
[SaveMaintainPersonInfomation] 開始背景上傳 2 筆資料...
[SaveMaintainPersonInfomation] 王小明: 更新電話 [0912345678] -> [0987654321]
[SaveMaintainPersonInfomation] 準備更新 王小明 的資料到 CRM...
[SaveMaintainPersonInfomation] ? 成功更新: 王小明
[SaveMaintainPersonInfomation] 背景處理完成！成功更新: 2 筆
```

**檢查重點：**
- ? 是否有「開始背景上傳」訊息？
- ? 是否有「準備更新 XXX 的資料到 CRM...」訊息？
- ? 是否有「? 成功更新: XXX」訊息？
- ? 如果有「?? 無變更，跳過」，表示資料比對判定為無變更
- ? 如果有「? 更新失敗」，查看錯誤訊息

### 步驟 3：檢查資料比對邏輯

如果看到「無變更，跳過」，檢查以下情況：

#### 3.1 空白字元問題
```csharp
// 舊值："0912345678 " (後面有空白)
// 新值："0912345678"
// 結果：判定為有變更 ?
```

#### 3.2 空值問題
```csharp
// 舊值：null 或 ""
// 新值：""
// 結果：判定為無變更（新值為空） ?
```

#### 3.3 大小寫問題
```csharp
// 舊值："台北市"
// 新值："台北市"
// 結果：判定為無變更（使用 OrdinalIgnoreCase） ?
```

### 步驟 4：檢查 CRM 連線

如果有「找不到聯絡人」或「更新失敗」錯誤：

```csharp
[SaveMaintainPersonInfomation] 找不到聯絡人: xxx-xxx-xxx
[SaveMaintainPersonInfomation] ? 更新失敗: 王小明, 錯誤: 連線逾時
```

**可能原因：**
- ? CRM 連線問題
- ? ContactId 不存在或已刪除
- ? 權限不足
- ? CRM 服務暫時無回應

## 常見問題排除

### 問題 1：前端顯示「沒有需要上傳的資料」

**原因：** `modifiedRecords.size === 0`

**解決方法：**
1. 編輯欄位後，按 Enter 或點擊其他儲存格
2. 確認儲存格背景變成淡黃色（表示已記錄修改）
3. 檢查 Console 是否有「資料已暫存（X 筆待上傳）」訊息

### 問題 2：後端顯示「無變更，跳過」

**原因：** 欄位值比對後判定為無變更

**解決方法：**
1. 檢查是否只是加了空白字元（會被忽略）
2. 檢查是否將欄位改為空值（會被跳過）
3. 確實修改欄位內容（例如電話號碼）

### 問題 3：後端顯示「ToolUtility 為 null」

**原因：** ToolUtility 未正確初始化

**解決方法：**
1. 檢查 `BaseChurchController` 的 DI 設定
2. 檢查 `Startup.cs` 中是否正確註冊服務
3. 重新啟動應用程式

### 問題 4：背景任務沒有日誌輸出

**原因：** 背景任務可能拋出未捕獲的例外

**解決方法：**
1. 在 Visual Studio 中啟用「Common Language Runtime Exceptions」
2. 檢查「輸出」視窗的完整日誌
3. 檢查是否有權限問題

## 測試腳本

### 測試案例 1：修改電話號碼
```
1. 開啟「組員資訊」頁面
2. 找到任一成員，點擊「行動電話」欄位
3. 修改電話號碼（例如：0912345678 → 0987654321）
4. 按 Enter 確認（背景應變淡黃色）
5. 按下「上傳」按鈕
6. 預期結果：
   - 前端顯示「已送出 1 筆資料，正在背景上傳中...」
   - Console 顯示「[GetResult] 最終返回 1 筆已修改的資料」
   - Output 顯示「? 成功更新: XXX」
   - 重新整理頁面，電話號碼已更新
```

### 測試案例 2：修改多筆資料
```
1. 開啟「組員資訊」頁面
2. 修改 3 個不同成員的資料（電話、地址、生日）
3. 確認 3 筆都標記為淡黃色
4. 按下「上傳」按鈕
5. 預期結果：
   - 前端顯示「已送出 3 筆資料，正在背景上傳中...」
   - Output 顯示「背景處理完成！成功更新: 3 筆」
```

### 測試案例 3：無變更上傳
```
1. 開啟「組員資訊」頁面
2. 不修改任何資料
3. 直接按下「上傳」按鈕
4. 預期結果：
   - 前端顯示「沒有需要上傳的資料」（警告訊息）
```

## Debug 模式追蹤

### 設定中斷點
在以下位置設定中斷點以深入追蹤：

1. **前端 GetResult() 函數**
   ```javascript
   function GetResult() {
       var grid = dataGridInstance || $("#gridContainer").dxDataGrid("instance");
       // ?? 設定中斷點
       ...
   }
   ```

2. **後端 SaveMaintainPersonInfomation 方法**
   ```csharp
   [HttpPost]
   public IActionResult SaveMaintainPersonInfomation(string aResult)
   {
       try
       {
           // ?? 設定中斷點
           System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] 開始處理");
           ...
       }
   }
   ```

3. **背景任務中的更新邏輯**
   ```csharp
   _ = Task.Run(() =>
   {
       try
       {
           // ?? 設定中斷點
           foreach (var member in members)
           {
               // ?? 設定中斷點
               if (hasChanges)
               {
                   // ?? 設定中斷點
                   toolUtility.UpdateEntity(entityToUpdate);
               }
           }
       }
   });
   ```

### 監看變數
在中斷點停止時，監看以下變數：

- `modifiedRecords.size` - 已修改的資料數量
- `members.Count` - 傳送到後端的資料數量
- `hasChanges` - 是否有變更需要更新
- `entityToUpdate` - 要更新的實體內容

## 效能監控

### 檢查上傳時間
```
[SaveMaintainPersonInfomation] 開始背景上傳 50 筆資料... (T0)
[SaveMaintainPersonInfomation] 準備更新 王小明 的資料到 CRM... (T1)
[SaveMaintainPersonInfomation] ? 成功更新: 王小明 (T2)
[SaveMaintainPersonInfomation] 背景處理完成！成功更新: 50 筆 (T3)
```

**效能指標：**
- 每筆資料更新時間：(T2 - T1) ? 100-500ms
- 總處理時間：(T3 - T0) ? 5-25秒（50 筆）

## 已知限制

### 1. 背景上傳
- ? 優點：使用者不需等待，立即回應
- ?? 限制：無法即時回報錯誤給使用者
- ?? 建議：定期檢查 Output 視窗的日誌

### 2. 欄位限制
- ? 可編輯：電話、地址、生日
- ? 不可編輯：會員身分、信仰狀態、裝備狀態
- ?? 原因：這些欄位由系統管理，不開放手動更新

### 3. 空值處理
- ? 有值 → 有值：更新
- ?? 有值 → 空值：跳過（避免誤刪）
- ?? 空值 → 空值：跳過
- ?? 空值 → 有值：理論上會更新（但可能被前端阻擋）

## 相關文件
- [MaintainPersonInfomation-上傳失敗-修復報告.md](./MaintainPersonInfomation-上傳失敗-修復報告.md)
- [MaintainPersonInfomation-欄位不存在錯誤-修復報告.md](./MaintainPersonInfomation-欄位不存在錯誤-修復報告.md)

## 緊急處理

如果問題依然存在，請執行以下步驟：

### 1. 收集完整日誌
```powershell
# 瀏覽器 Console
1. F12 → Console → 右鍵 → Save as... → console.log

# Visual Studio Output
1. 輸出視窗 → 偵錯 → 全選 → 複製 → 貼到文字檔
```

### 2. 檢查 CRM 連線
```csharp
// 在 SaveMaintainPersonInfomation 開頭加入測試
var testEntity = toolUtility.RetrieveEntity("contact", contactGuid);
System.Diagnostics.Debug.WriteLine($"測試查詢: {testEntity != null}");
```

### 3. 簡化測試
```
1. 只修改 1 筆資料
2. 只修改電話欄位
3. 使用簡單的值（例如：123456789）
4. 觀察完整流程
```

### 4. 聯繫支援
提供以下資訊：
- 前端 Console 日誌
- 後端 Output 日誌
- 修改的欄位和值
- 錯誤訊息截圖
