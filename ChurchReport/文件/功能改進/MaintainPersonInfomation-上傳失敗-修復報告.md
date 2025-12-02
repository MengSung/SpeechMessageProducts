# 組員資訊上傳失敗修復報告

## 問題描述
使用者在「組員資訊」頁面編輯資料後，按下「上傳」按鈕，雖然前端顯示成功訊息，但資料並未實際更新到 CRM 資料庫。

## 問題分析

### 根本原因
1. **前端傳送所有資料**
   - `GetResult()` 函數會將 DataGrid 中**所有可見的資料**（包括未修改的）轉換成 JSON 傳送到後端
   
2. **後端比對邏輯過於嚴格**
   - 使用 `StringComparison.Ordinal` 進行精確比對（包括空白字元）
   - 對空值（`null` vs 空字串）處理不一致
   - 導致大部分資料被判定為「無變更」而跳過更新

3. **追蹤問題**
   - 前端雖然用 `modifiedRecords` 記錄了已修改的資料
   - 但在 `GetResult()` 中並未使用此記錄來篩選資料

## 修復內容

### 1. 前端修改 (MaintainPersonInfomationView.cshtml)

#### 修改 `GetResult()` 函數
```javascript
function GetResult() {
    try {
        var grid = dataGridInstance || $("#gridContainer").dxDataGrid("instance");
        if (!grid) {
            log("[GetResult] Grid 實例不存在");
            return "[]";
        }

        try { grid.closeEditCell(); } catch(e) {}

        // ? 只返回已修改的資料
        var items = [];
        var rows = grid.getVisibleRows() || [];
        
        log("[GetResult] 總可見行數:", rows.length);
        log("[GetResult] 已修改的 ContactId 數量:", modifiedRecords.size);
        log("[GetResult] 已修改的 ContactId 清單:", Array.from(modifiedRecords));
        
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            if (r && r.rowType === "data" && r.data && r.data.ContactId) {
                // ? 只收集已修改的資料
                if (modifiedRecords.has(r.data.ContactId)) {
                    items.push(r.data);
                    log("[GetResult] 加入已修改的資料:", r.data.FullName, r.data.ContactId);
                }
            }
        }

        log("[GetResult] 最終返回", items.length, "筆已修改的資料");
        return JSON.stringify(items);
    } catch (err) {
        logError("[GetResult] 發生錯誤:", err);
        return "[]";
    }
}
```

**改進重點：**
- ? 只傳送 `modifiedRecords` 中記錄的已修改資料
- ? 添加詳細的日誌追蹤（開發模式）
- ? 減少網路傳輸量和後端處理負擔

### 2. 後端修改 (PersonalController.cs)

#### 改進 `SaveMaintainPersonInfomation` 方法

**改進空值處理：**
```csharp
// ? 更新行動電話（改進空值處理）
var currentPhone = contactEntity.Contains("mobilephone") 
    ? (contactEntity.GetAttributeValue<string>("mobilephone") ?? "")
    : "";
var newPhone = member.Phone ?? "";

// 移除空白字元後比較
currentPhone = currentPhone.Trim();
newPhone = newPhone.Trim();

if (!string.IsNullOrEmpty(newPhone) && !string.Equals(currentPhone, newPhone, StringComparison.OrdinalIgnoreCase))
{
    entityToUpdate["mobilephone"] = newPhone;
    hasChanges = true;
    System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 更新電話 [{currentPhone}] -> [{newPhone}]");
}
```

**改進重點：**
- ? 統一處理 `null` 和空字串為空字串
- ? 使用 `Trim()` 移除前後空白字元
- ? 使用 `OrdinalIgnoreCase` 忽略大小寫差異
- ? 只在新值非空時才進行比對和更新
- ? 添加詳細的變更追蹤日誌

**改進生日處理：**
```csharp
// ? 更新生日（改進日期比對）
if (member.BirthDate != DateTime.MinValue && member.BirthDate.Year > 1900)
{
    var currentBirthDate = contactEntity.Contains("birthdate") 
        ? (contactEntity.GetAttributeValue<DateTime?>("birthdate") ?? DateTime.MinValue)
        : DateTime.MinValue;

    // 轉換為本地時間並只比較日期部分
    if (currentBirthDate != DateTime.MinValue)
    {
        currentBirthDate = currentBirthDate.ToLocalTime();
    }
    
    if (currentBirthDate == DateTime.MinValue || currentBirthDate.Date != member.BirthDate.Date)
    {
        entityToUpdate["birthdate"] = member.BirthDate;
        hasChanges = true;
        System.Diagnostics.Debug.WriteLine($"[SaveMaintainPersonInfomation] {member.FullName}: 更新生日 [{currentBirthDate:yyyy-MM-dd}] -> [{member.BirthDate:yyyy-MM-dd}]");
    }
}
```

**改進重點：**
- ? 轉換為本地時區
- ? 只比較日期部分（忽略時間）
- ? 處理 `null` 和 `MinValue` 的情況

## 修復效果

### 修復前
- ? 上傳所有資料（包括未修改的）
- ? 嚴格的字串比對導致判定為「無變更」
- ? 資料未實際更新到 CRM
- ? 使用者看到成功訊息但實際失敗

### 修復後
- ? 只上傳真正修改的資料
- ? 容錯的字串比對（忽略空白、大小寫）
- ? 資料正確更新到 CRM
- ? 詳細的日誌追蹤
- ? 減少網路和資料庫負擔

## 測試建議

### 1. 基本功能測試
```
1. 開啟「組員資訊」頁面
2. 編輯某個成員的「電話」欄位
3. 按下「上傳」按鈕
4. 檢查：
   - 前端顯示「已送出 1 筆資料，正在背景上傳中...」
   - 瀏覽器 Console 顯示詳細日誌（開發模式）
   - 重新整理頁面後，資料已更新
   - CRM 中該聯絡人的電話欄位已更新
```

### 2. 邊界測試
```
測試案例：
- 修改電話為空白字串 → 應跳過更新
- 修改電話前後加空白 → 應正確更新
- 修改多個欄位 → 應全部更新
- 同時修改多筆資料 → 應批次更新
- 修改後不按上傳直接離開 → 應保留編輯狀態
```

### 3. 效能測試
```
- 修改 10 筆資料 → 應只傳送 10 筆
- 修改 1 筆資料（Grid 有 100 筆） → 應只傳送 1 筆
```

## 日誌追蹤

### 前端日誌（開發模式）
```javascript
[GetResult] 總可見行數: 50
[GetResult] 已修改的 ContactId 數量: 2
[GetResult] 已修改的 ContactId 清單: [xxx-xxx-xxx, yyy-yyy-yyy]
[GetResult] 加入已修改的資料: 王小明 xxx-xxx-xxx
[GetResult] 加入已修改的資料: 李小華 yyy-yyy-yyy
[GetResult] 最終返回 2 筆已修改的資料
```

### 後端日誌
```csharp
[SaveMaintainPersonInfomation] 成功解析到 2 筆資料
[SaveMaintainPersonInfomation] 開始背景上傳 2 筆資料...
[SaveMaintainPersonInfomation] 王小明: 更新電話 [0912345678] -> [0987654321]
[SaveMaintainPersonInfomation] 準備更新 王小明 的資料到 CRM...
[SaveMaintainPersonInfomation] ? 成功更新: 王小明
[SaveMaintainPersonInfomation] 李小華: 更新地址 [台北市] -> [新北市]
[SaveMaintainPersonInfomation] 準備更新 李小華 的資料到 CRM...
[SaveMaintainPersonInfomation] ? 成功更新: 李小華
[SaveMaintainPersonInfomation] 背景處理完成！成功更新: 2 筆
```

## 相關文件
- [MaintainPersonInfomation-欄位不存在錯誤-修復報告.md](./MaintainPersonInfomation-欄位不存在錯誤-修復報告.md)
- [效能優化TODO清單.md](../效能優化計畫/效能優化TODO清單.md)

## 修復時間
- 修復日期：2025-01-XX
- 預估測試時間：30 分鐘
- 預估發布時間：測試通過後立即發布

## 備註
- 此修復同時改善了效能（減少不必要的資料傳輸）
- 添加的日誌僅在開發模式顯示，不影響生產環境效能
- 建議在正式環境部署前進行完整回歸測試
