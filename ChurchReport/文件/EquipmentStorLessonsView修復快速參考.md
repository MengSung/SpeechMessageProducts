# EquipmentStorLessonsView 修復快速參考

## ?? 修復內容
將 DataSource 從 `.WebApi()` 改為 `.Mvc()`

## ?? 修改檔案
`ChurchReport/Views/Equipment/EquipmentStorLessonsView.cshtml` (第 83 行)

## ? 修改前後對比

### 修改前（錯誤）:
```csharp
.DataSource(d => d.WebApi()  // ? 不適用於 master-detail
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })
)
```

### 修改後（正確）:
```csharp
.DataSource(d => d.Mvc()  // ? 適用於 master-detail
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })
)
```

## ?? 快速測試

### 1. 運行測試腳本
```batch
ChurchReport\文件\測試EquipmentStorLessonsView.bat
```

### 2. 手動測試
1. 啟動應用程式
2. 訪問: `/Equipment/EquipmentView`
3. 展開小組 → 展開聯絡人 → 查看課程列表

### 3. 檢查 Browser Console
按 F12，應該看到:
- ? 無錯誤訊息
- ? Network 請求成功 (200 OK)
- ? 課程列表顯示

## ?? 故障排除

### DataGrid 不顯示
```
原因: JavaScript 錯誤或數據為空
解決: F12 → Console → 查看錯誤訊息
```

### 404 Not Found
```
原因: Controller 路由問題
解決: 確認 LoadEquipmentStorLessons 方法存在
```

### 500 Server Error
```
原因: 後端邏輯錯誤
解決: 查看 Visual Studio Output 視窗
```

## ?? 相關文件
- 完整報告: `文件/EquipmentStorLessonsView完整修復報告.md`
- 除錯指南: `文件/EquipmentStorLessonsView修復說明.md`

## ?? 技術要點

### 為什麼使用 .Mvc()?
1. ? Master-Detail 原生支援
2. ? 自動處理父級數據上下文
3. ? 標準 MVC 路由，無需額外配置
4. ? 與現有代碼一致

### DataGrid 層次結構
```
EquipmentView (小組)
  └─ EquipmentContactView (聯絡人)
      └─ EquipmentStorLessonsView (課程) ← 修復目標
```

## ? 修復狀態
- ? 代碼已修改
- ? 建置成功
- ? 文件完整
- ?? 待用戶測試

---
**修復日期**: 2024
**狀態**: 已完成
**建置**: ? 成功
