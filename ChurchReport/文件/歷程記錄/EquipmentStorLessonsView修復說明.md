# EquipmentStorLessonsView 顯示問題修復說明

## 問題描述
`EquipmentStorLessonsView.cshtml` 中的 `Html.DevExtreme().DataGrid<EquipmentStorLessons>()` 沒有顯示出來。

## 修復內容

### 1. 主要修改
- **檔案**: `ChurchReport/Views/Equipment/EquipmentStorLessonsView.cshtml`
- **修改**: 將 DataSource 從 `.WebApi()` 改為 `.Mvc()`

#### 修改前:
```csharp
.DataSource(d => d.WebApi()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })
)
```

#### 修改後:
```csharp
.DataSource(d => d.Mvc()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })
)
```

### 2. 其他改進
- 修正 `CalculateCellValue` 中的 JavaScript 語法錯誤（添加 `return` 關鍵字）
- 改善 `onInitNewRow` 函數，使用 `console.log` 替代 `alert` 避免干擾

## 原因分析

### 為什麼改用 .Mvc()？
1. **Master-Detail 架構相容性**: DevExtreme 在 master-detail 場景中，`.Mvc()` 對於參數傳遞和上下文綁定支援更好
2. **數據上下文**: `.Mvc()` 能更好地處理來自父級 DataGrid 的 `data` 物件
3. **路由簡化**: `.Mvc()` 使用 ASP.NET Core MVC 的標準路由，不需要額外的 WebAPI 配置

### DataGrid 層次結構
```
EquipmentView.cshtml (小組列表)
└── EquipmentContactView.cshtml (聯絡人列表)
    └── EquipmentStorLessonsView.cshtml (課程列表) ← 這個檔案
```

## 測試步驟

### 1. 檢查頁面訪問
訪問裝備管理頁面:
```
https://your-domain/Equipment/EquipmentView
```

### 2. 檢查瀏覽器控制台
按 F12 開啟開發者工具，查看：
- **Console** 頁籤: 檢查是否有 JavaScript 錯誤
- **Network** 頁籤: 檢查 AJAX 請求

### 3. 預期的 Network 請求
展開 EquipmentContact 行後，應該會看到類似的請求:
```
GET /Equipment/LoadEquipmentStorLessons?id={contactId}
```

### 4. 常見錯誤排查

#### 錯誤 1: 404 Not Found
**症狀**: Network 顯示 `/Equipment/LoadEquipmentStorLessons` 返回 404
**原因**: Controller 路由配置問題
**解決**: 確認 `EquipmentController.cs` 中 `LoadEquipmentStorLessons` 方法存在且為 public

#### 錯誤 2: 500 Internal Server Error
**症狀**: Network 顯示 500 錯誤
**原因**: 後端處理異常
**解決**: 
1. 檢查 `id` 參數是否正確傳遞
2. 查看 `ToolUtility.RetrieveStorLessonsByFetchXml` 方法是否正常運作
3. 檢查 CRM 連線

#### 錯誤 3: DataGrid 空白但無錯誤
**症狀**: 頁面載入成功但看不到 DataGrid
**原因**: CSS 或容器問題
**解決**:
1. 檢查 `MasterDetail.css` 是否正確載入
2. 確認 `.internal-grid-container` 樣式定義
3. 使用瀏覽器開發者工具檢查 DOM 結構

#### 錯誤 4: data.EquipmentContactId is undefined
**症狀**: Console 顯示 `data.EquipmentContactId` 未定義
**原因**: 父級 DataGrid 未正確傳遞數據上下文
**解決**: 確認 `EquipmentContactView.cshtml` 中 DataGrid 的 Key 設置為 `"EquipmentContactId"`

## 驗證清單

- [ ] 建置成功，無編譯錯誤
- [ ] 訪問 `/Equipment/EquipmentView` 頁面可以載入
- [ ] 可以看到小組列表（第一層 DataGrid）
- [ ] 展開小組後可以看到聯絡人列表（第二層 DataGrid）
- [ ] 展開聯絡人後可以看到課程列表（第三層 DataGrid） ← **這是修復目標**
- [ ] 課程列表顯示：課程名稱、階段名稱、是否結業、日期
- [ ] 瀏覽器 Console 無錯誤
- [ ] Network 請求全部成功（200 OK）

## 進階除錯

### 啟用詳細日誌
在 `LoadEquipmentStorLessons` 方法開頭添加日誌:
```csharp
[HttpGet]
public object LoadEquipmentStorLessons(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        System.Diagnostics.Debug.WriteLine($"LoadEquipmentStorLessons called with id: {id}");
        
        var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml("", "", "", id);
        
        System.Diagnostics.Debug.WriteLine($"Retrieved {storLessons?.Entities.Count ?? 0} lessons");
        
        // ... 其餘代碼
    }
    catch (Exception e)
    {
        System.Diagnostics.Debug.WriteLine($"Error in LoadEquipmentStorLessons: {e.Message}");
        return HandleError(e, "LoadEquipmentStorLessons");
    }
}
```

### 手動測試 API
使用 Postman 或瀏覽器直接訪問:
```
GET https://your-domain/Equipment/LoadEquipmentStorLessons?id={valid-contact-id}
```

應該返回類似:
```json
{
    "data": [
        {
            "StorLessonsEntityId": "guid",
            "DiscipleLessonsName": "課程名稱",
            "StageName": "階段名稱",
            "CurrentComplete": false,
            "DiscipleLessonsDateTime": "2024-01-01T00:00:00"
        }
    ],
    "totalCount": 1
}
```

## 如果問題持續

### 備用方案 1: 使用 WebApi 並添加路由
如果必須使用 `.WebApi()`，在 `EquipmentController` 添加:
```csharp
[HttpGet]
[Route("api/Equipment/LoadEquipmentStorLessons")]
public object LoadEquipmentStorLessons(string id, DataSourceLoadOptions loadOptions)
{
    // 現有代碼
}
```

並修改 View:
```csharp
.DataSource(d => d.WebApi()
    .Controller("api/Equipment")  // 注意添加 api/ 前綴
    .LoadAction("LoadEquipmentStorLessons")
    // ...
)
```

### 備用方案 2: 簡化測試
暫時移除 LoadParams，使用硬編碼 ID 測試:
```csharp
.LoadParams(new { id = "test-id-12345" })
```

### 備用方案 3: 檢查模型綁定
確認 `EquipmentContact` 模型包含 `EquipmentContactId` 屬性:
```csharp
public class EquipmentContact
{
    public string EquipmentContactId { get; set; }  // 必須存在
    // ... 其他屬性
}
```

## 聯絡支援
如果以上所有方法都無效，請提供：
1. 瀏覽器 Console 截圖
2. Network 請求/回應詳情
3. Visual Studio Output 視窗的錯誤訊息
4. `EquipmentController.LoadEquipmentStorLessons` 方法的完整代碼

## 參考資料
- DevExtreme DataGrid: https://js.devexpress.com/Documentation/Guide/UI_Components/DataGrid/Getting_Started_with_DataGrid/
- Master-Detail 文件: https://js.devexpress.com/Documentation/Guide/UI_Components/DataGrid/Master-Detail_Interface/
