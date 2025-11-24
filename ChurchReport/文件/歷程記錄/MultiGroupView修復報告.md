# MultiGroupView 修復報告

## 問題描述
有多個小組的名單，但是 `MultiGroupView.cshtml` 頁面沒有顯示資料（圓餅圖和資料網格都是空的）。

## 根本原因分析

### 1. 缺失的 API 端點
`MultiGroupView.cshtml` 視圖檔案中引用了三個 API 端點，但在 `SmallGroupController.cs` 中都不存在：

#### a) `GetMultiGroupChartDataList` (圓餅圖資料)
```javascript
// MultiGroupView.cshtml 中的圓餅圖配置
.DataSource(d => d
    .WebApi()
    .Controller("SmallGroup")
    .LoadAction("GetMultiGroupChartDataList")  // ? 不存在
    .Key("ID")
    .LoadParams(new { WeeklyReportId = @ViewBag.ListId })
)
```

#### b) `AssignSmallGroupGet` (資料網格資料)
```javascript
// MultiGroupView.cshtml 中的 DataGrid 配置
.DataSource(d => d
    .WebApi()
    .Controller("SmallGroup")
    .LoadAction("AssignSmallGroupGet")  // ? 不存在
    .Key("ListEntityId")
    .LoadParams(new { id = @ViewBag.ListId })
)
```

#### c) `UpdateDate` (日期更新)
```javascript
// MultiGroupView.cshtml 中的日期選擇器
$.ajax({
    url: '@Url.Action("UpdateDate", "SmallGroup")',  // ? 不存在
    data: { SelectedDate: getODataLocalDateFilter(arg.value) },
    type: 'GET',
    // ...
});
```

### 2. 缺失的 JavaScript 函數
`getODataLocalDateFilter` 函數在視圖中被調用但未定義。

## 修復方案

### 1. 新增 API 端點到 SmallGroupController.cs

#### a) GetMultiGroupChartDataList - 圓餅圖資料
```csharp
/// <summary>
/// 載入多小組圓餅圖資料
/// 用於 MultiGroupView 的 PieChart 資料來源
/// </summary>
/// <param name="WeeklyReportId">週報ID</param>
/// <param name="loadOptions">載入選項</param>
[HttpGet]
public object GetMultiGroupChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
{
    try
    {
        // 確保多組資料已載入
        if (InMemoryContext.ListManager.m_MultiGroupChartDataList == null ||
            InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList == null)
        {
            return DataSourceLoader.Load(new List<MultiGroupChartData>(), loadOptions);
        }

        var chartData = InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList;

        return DataSourceLoader.Load(chartData, loadOptions);
    }
    catch (Exception e)
    {
        return HandleError(e, "GetMultiGroupChartDataList");
    }
}
```

**資料來源**：
- `InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList`
- 資料結構：`List<MultiGroupChartData>`
  - `ID`: 識別碼
  - `Name`: 名稱（總人數、主日人數、小組人數）
  - `Number`: 數量

#### b) AssignSmallGroupGet - 資料網格資料
```csharp
/// <summary>
/// 載入多小組列表資料
/// 用於 MultiGroupView 的 DataGrid 資料來源
/// </summary>
/// <param name="id">清單ID</param>
/// <param name="loadOptions">載入選項</param>
[HttpGet]
public object AssignSmallGroupGet(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        // 確保多組資料已載入
        if (InMemoryContext.ListManager.m_MultiGroupList == null ||
            InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData == null)
        {
            return DataSourceLoader.Load(new List<WeeklyReportRecord>(), loadOptions);
        }

        var weeklyReportRecords = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

        return DataSourceLoader.Load(weeklyReportRecords, loadOptions);
    }
    catch (Exception e)
    {
        return HandleError(e, "AssignSmallGroupGet");
    }
}
```

**資料來源**：
- `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData`
- 資料結構：`List<WeeklyReportRecord>`
  - `ListEntityId`: 小組 ID
  - `Name`: 小組名稱
  - `TotalNumber`: 總人數
  - `SundayNumber`: 主日人數
  - `SmallGroupNumber`: 小組人數
  - `SundayRate`: 主日出席率
  - `SmallGroupRate`: 小組出席率
  - `ReportStatus`: 週報狀態
  - `ReportContent`: 小組日誌

#### c) UpdateDate - 日期更新
```csharp
/// <summary>
/// 更新多小組檢視的日期
/// 當使用者在 MultiGroupView 中更改日期時調用
/// </summary>
/// <param name="SelectedDate">選擇的日期 (格式: yyyy/M/d)</param>
[HttpGet]
public IActionResult UpdateDate(string SelectedDate)
{
    try
    {
        // 解析日期
        if (!DateTime.TryParseExact(SelectedDate, 
            new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
            CultureInfo.InvariantCulture, 
            DateTimeStyles.None, 
            out DateTime selectedDateTime))
        {
            return Json(new { success = false, message = "日期格式錯誤" });
        }

        // 更新選擇的日期
        InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

        // 重新設置 ListManager 以載入新日期的資料
        InMemoryContext.ListManager.SetupListManager(
            InMemoryContext.ListManager.m_Account,
            InMemoryContext.ListManager.m_Password,
            selectedDateTime);

        return Json(new { 
            success = true, 
            message = "日期更新成功" 
        });
    }
    catch (Exception e)
    {
        return Json(new { 
            success = false, 
            message = $"日期更新失敗: {e.Message}" 
        });
    }
}
```

### 2. 新增 JavaScript 函數到 MultiGroupView.cshtml

```javascript
// 日期格式轉換函數
function getODataLocalDateFilter(date) {
    if (date == null) date = new Date();
    var month = (date.getMonth() + 1).toString();
    var day = date.getDate().toString();
    return date.getFullYear() + "/" + month + "/" + day;
}
```

## 資料流程說明

### MultiGroupView 資料載入流程

1. **使用者登入** → `HomeController.Login`
2. **設定 ListManager** → `ListManager.SetupListManager()`
   - 調用 `DownloadListManager.GetListManager()`
   - 從 CRM 系統載入多小組資料
   - 填充 `m_MultiGroupChartDataList` (圓餅圖資料)
   - 填充 `m_MultiGroupList.m_WeeklyReportRecordListData` (網格資料)
3. **判斷顯示類型** → `ListManager.GetDisplayViewType()`
   - 如果 `m_WeeklyReportRecordListData.Count > 1` → 顯示 `MultiGroupView`
   - 如果 `m_WeeklyReportRecordListData.Count == 1` → 顯示 `IntegrateView`
4. **MultiGroupView 頁面載入**
   - 圓餅圖透過 `GetMultiGroupChartDataList` API 獲取資料
   - 資料網格透過 `AssignSmallGroupGet` API 獲取資料
5. **日期變更**
   - 調用 `UpdateDate` API
   - 重新載入 ListManager 資料
   - 頁面重新導向到 `/SmallGroup/MultiGroupView/MultiGroupView`

### 圓餅圖資料結構範例
```json
[
    { "ID": "001", "Name": "總人數", "Number": 45 },
    { "ID": "002", "Name": "主日人數", "Number": 30 },
    { "ID": "003", "Name": "小組人數", "Number": 25 }
]
```

### 資料網格資料結構範例
```json
[
    {
        "ListEntityId": "001",
        "Name": "夢嵩連碧小組",
        "TotalNumber": "8",
        "SundayNumber": "5",
        "SmallGroupNumber": "4",
        "SundayRate": "0.52",
        "SmallGroupRate": "0.98",
        "ReportStatus": "已回報",
        "ReportContent": "很火熱"
    }
]
```

## 相關檔案修改

### 1. ChurchReport/Controllers/SmallGroupController.cs
- ? 新增 `GetMultiGroupChartDataList` API 端點
- ? 新增 `AssignSmallGroupGet` API 端點
- ? 新增 `UpdateDate` API 端點
- ? 已存在 `System.Collections.Generic` using 參考

### 2. ChurchReport/Views/Home/MultiGroupView.cshtml
- ? 新增 `getODataLocalDateFilter` JavaScript 函數

## 測試建議

### 1. 基本功能測試
1. 以管理多個小組的小組長身份登入
2. 確認自動導向到 `MultiGroupView` 頁面
3. 確認圓餅圖正確顯示三個數據：總人數、主日人數、小組人數
4. 確認資料網格顯示所有管理的小組列表

### 2. 日期更新測試
1. 選擇不同的日期
2. 確認載入面板顯示
3. 確認頁面重新載入並顯示該日期的資料

### 3. 小組連結測試
1. 點擊資料網格中的小組名稱連結
2. 確認正確導向到該小組的 `IntegrateView` 頁面

### 4. 異常處理測試
1. 測試無資料的情況（空的圓餅圖和網格）
2. 測試日期格式錯誤的處理
3. 測試 API 錯誤的處理

## 潛在問題與注意事項

### 1. 資料同步問題
- `InMemoryContext.ListManager` 儲存在記憶體中
- 如果多個使用者同時登入，可能會有資料衝突
- 建議：考慮使用 Session 或其他隔離機制

### 2. 效能考量
- 每次日期變更都會重新載入所有資料
- 建議：實作資料快取機制

### 3. 錯誤處理
- 目前使用 `HandleError` 方法統一處理錯誤
- 建議：加強錯誤訊息的詳細度，便於除錯

### 4. 日期格式
- 目前支援三種格式：`yyyy/M/d`、`yyyy/MM/dd`、`yyyy-MM-dd`
- 建議：統一使用單一格式，減少解析錯誤

## 相關模型說明

### ListManager
- **位置**: `ChurchReport/Models/ListManager.cs`
- **功能**: 管理多小組和單一小組的資料
- **重要屬性**:
  - `m_MultiGroupChartDataList`: 圓餅圖資料
  - `m_MultiGroupList`: 多小組列表資料
  - `m_ListSmallGroupWeeklyReport`: 單一小組詳細資料

### MultiGroupChartData
- **位置**: `ChurchReport/Models/MultiGroupChartData.cs`
- **屬性**:
  - `ID`: 識別碼
  - `Name`: 名稱
  - `Number`: 數量

### WeeklyReportRecord
- **位置**: `ChurchReport/Models/WeeklyReportRecord.cs`
- **屬性**:
  - `ListEntityId`: 小組 ID
  - `WeeklyReportEntityId`: 週報 ID
  - `Name`: 小組名稱
  - `TotalNumber`: 總人數
  - `SundayNumber`: 主日人數
  - `SmallGroupNumber`: 小組人數
  - `SundayRate`: 主日出席率
  - `SmallGroupRate`: 小組出席率
  - `ReportStatus`: 週報狀態
  - `ReportContent`: 小組日誌

## 總結

### 修復內容
1. ? 新增 3 個缺失的 API 端點
2. ? 新增 1 個缺失的 JavaScript 函數
3. ? 建置測試通過

### 預期結果
- MultiGroupView 頁面應該能正確顯示圓餅圖和資料網格
- 日期選擇功能應該正常運作
- 小組連結應該能正確導向到 IntegrateView

### 後續建議
1. 實作資料快取機制，提升效能
2. 加強錯誤處理和使用者提示
3. 考慮使用 Session 隔離不同使用者的資料
4. 增加單元測試覆蓋率
5. 優化資料載入流程，減少不必要的 CRM 查詢

---
**修復日期**: 2024
**修復者**: GitHub Copilot
**相關問題**: MultiGroupView 沒有顯示資料
