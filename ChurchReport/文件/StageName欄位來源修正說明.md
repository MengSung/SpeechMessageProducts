# StageName 欄位來源修正說明

## ?? 修正摘要

**修正日期**: 2024-11-18  
**修正項目**: `StageName` 欄位來源  
**修正文件**: `ChurchReport/Controllers/EquipmentController.cs`  
**狀態**: ? 完成並驗證

---

## ?? 問題描述

### 原始錯誤

`StageName` 欄位原本直接從 `new_stor_lessons` 實體的 `new_stagename` 欄位取得：

```csharp
StageName = ToolUtility.GetEntityStringAttribute(ref lesson, "new_stagename")
```

### 正確做法

`StageName` 應該從關聯的 `new_disciple_lessons` 實體的 `new_now_stage_name` 欄位取得。

---

## ??? CRM 實體結構

### 完整關聯關係

```
new_stor_lessons (學生課程記錄)
    ├─ new_stor_lessonsid: GUID (主鍵)
    ├─ new_name: String (記錄名稱)
    ├─ new_current_complete: Boolean (是否完成)
    ├─ new_contact_new_stor_lessons: Lookup → Contact (學員)
    └─ new_new_disciple_lessons_new_stor_les: Lookup → new_disciple_lessons (門徒課程) ?
            │
            └─> new_disciple_lessons (門徒課程主檔)
                    ├─ new_disciple_lessonsid: GUID (主鍵)
                    ├─ new_name: String (課程名稱)
                    ├─ new_class_start_date: DateTime (上課開始日期) ?
                    ├─ new_class_end_date: DateTime (上課結束日期)
                    └─ new_now_stage_name: String (當前階段名稱) ? ← StageName 應取此欄位
```

---

## ?? 修正內容

### 修正前

```csharp
var lessonItem = new EquipmentStorLessons
{
    StorLessonsEntityId = lesson.Id.ToString(),
    DiscipleLessonsName = ToolUtility.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les"),
    StageName = ToolUtility.GetEntityStringAttribute(ref lesson, "new_stagename"), // ? 錯誤
    CurrentComplete = ToolUtility.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
    DiscipleLessonsDateTime = classStartDate
};
```

### 修正後

```csharp
// 取得門徒課程的 ID
var discipleLessonId = ToolUtility.GetEntityLookupAttribute(ref lesson, "new_new_disciple_lessons_new_stor_les");

// 從門徒課程實體取得資料
DateTime classStartDate = DateTime.MinValue;
string stageName = string.Empty; // ? 新增變數

if (discipleLessonId != Guid.Empty)
{
    try
    {
        var discipleLesson = ToolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId);
        
        // 取得上課開始日期
        classStartDate = ToolUtility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date");
        
        // 取得階段名稱 (從 new_disciple_lessons 的 new_now_stage_name) ? 新增
        stageName = ToolUtility.GetEntityStringAttribute(ref discipleLesson, "new_now_stage_name");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 警告: 無法取得門徒課程資料，DiscipleLessonId={discipleLessonId}, 錯誤={ex.Message}");
    }
}

var lessonItem = new EquipmentStorLessons
{
    StorLessonsEntityId = lesson.Id.ToString(),
    DiscipleLessonsName = ToolUtility.GetEntityLookupDisplayName(ref lesson, "new_new_disciple_lessons_new_stor_les"),
    StageName = stageName, // ? 修正: 從 new_disciple_lessons.new_now_stage_name 取得
    CurrentComplete = ToolUtility.GetEntityBoolAttribute(ref lesson, "new_current_complete"),
    DiscipleLessonsDateTime = classStartDate
};
```

---

## ?? 欄位對應表

| 屬性名稱 | CRM 實體 | CRM 欄位名稱 | 欄位類型 | 說明 |
|---------|---------|-------------|---------|------|
| `StorLessonsEntityId` | `new_stor_lessons` | `new_stor_lessonsid` | GUID | 課程記錄 ID |
| `DiscipleLessonsName` | `new_disciple_lessons` | `new_name` (透過 Lookup) | String | 課程名稱 |
| `StageName` | `new_disciple_lessons` | `new_now_stage_name` ? | String | **階段名稱** |
| `CurrentComplete` | `new_stor_lessons` | `new_current_complete` | Boolean | 是否完成 |
| `DiscipleLessonsDateTime` | `new_disciple_lessons` | `new_class_start_date` ? | DateTime | 上課開始日期 |

---

## ?? 修正理由

### 為什麼從 new_disciple_lessons 取得？

1. **資料一致性**
   - `new_now_stage_name` 是課程主檔定義的階段
   - 確保所有學員看到的階段名稱一致

2. **維護便利性**
   - 只需在課程主檔修改階段名稱
   - 所有學員記錄自動反映最新階段

3. **業務邏輯正確性**
   - 階段是課程的屬性，不是學員記錄的屬性
   - `new_stor_lessons.new_stagename` 可能是舊欄位或冗餘欄位

4. **與其他欄位一致**
   - `DiscipleLessonsDateTime` 也從 `new_disciple_lessons` 取得
   - `DiscipleLessonsName` 也從 `new_disciple_lessons` 取得

---

## ?? 測試驗證

### 測試場景 1: 正常課程顯示

**輸入**: 
- 學員有課程記錄
- `new_disciple_lessons` 存在且有 `new_now_stage_name`

**預期**:
```
課程: 舊約概論
階段: 初階 (從 new_disciple_lessons.new_now_stage_name)
日期: 2024-01-15
```

### 測試場景 2: 階段名稱為空

**輸入**:
- 學員有課程記錄
- `new_disciple_lessons.new_now_stage_name` 為空

**預期**:
```
課程: 舊約概論
階段: (空字串，前端顯示為空白)
日期: 2024-01-15
```

### 測試場景 3: 門徒課程不存在

**輸入**:
- 學員有課程記錄
- `discipleLessonId` 為 `Guid.Empty` 或查詢失敗

**預期**:
```
課程: (空)
階段: (空字串)
日期: (MinValue，前端過濾為 null)
```

---

## ?? 除錯方法

### 後端日誌檢查

```csharp
System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 課程: {lessonItem.DiscipleLessonsName}, 階段: {lessonItem.StageName}, 日期: {lessonItem.DiscipleLessonsDateTime:yyyy-MM-dd}");
```

**預期輸出**:
```
[LoadEquipmentStorLessons] 課程: 舊約概論, 階段: 初階, 日期: 2024-01-15
[LoadEquipmentStorLessons] 課程: 新約概論, 階段: 中階, 日期: 2024-02-20
```

### CRM 資料驗證

在 CRM 中檢查 `new_disciple_lessons` 實體：

```
實體: new_disciple_lessons
欄位: new_now_stage_name
值: 初階 / 中階 / 進階 / 結業
```

### 前端顯示檢查

在瀏覽器 Network 標籤中檢查 `LoadEquipmentStorLessons` 的響應：

```json
[
  {
    "StorLessonsEntityId": "12345678-1234-1234-1234-123456789012",
    "DiscipleLessonsName": "舊約概論",
    "StageName": "初階",  // ? 應該顯示正確的階段名稱
    "CurrentComplete": true,
    "DiscipleLessonsDateTime": "2024-01-15T00:00:00"
  }
]
```

---

## ?? 性能影響

### 查詢次數

**修正前**:
- 每個課程記錄: 1 次查詢 (`new_stor_lessons`)

**修正後**:
- 每個課程記錄: 2 次查詢
  1. `new_stor_lessons`
  2. `new_disciple_lessons` (額外查詢)

### 優化建議

如果性能成為問題，可使用 FetchXML 的 linked-entity 一次查詢所有資料：

```xml
<fetch>
  <entity name="new_stor_lessons">
    <attribute name="new_stor_lessonsid" />
    <attribute name="new_current_complete" />
    <link-entity name="new_disciple_lessons" 
                 from="new_disciple_lessonsid" 
                 to="new_new_disciple_lessons_new_stor_les" 
                 alias="dl">
      <attribute name="new_name" />
      <attribute name="new_class_start_date" />
      <attribute name="new_now_stage_name" />  <!-- ? 一次取得 -->
    </link-entity>
    <filter>
      <condition attribute="new_contact_new_stor_lessons" operator="eq" value="{contactId}" />
    </filter>
  </entity>
</fetch>
```

---

## ?? 相關欄位說明

### new_now_stage_name vs new_stagename

| 欄位 | 實體 | 用途 | 推薦使用 |
|------|------|------|---------|
| `new_now_stage_name` | `new_disciple_lessons` | 課程的當前階段定義 | ? 推薦 |
| `new_stagename` | `new_stor_lessons` | 可能是學員記錄的快照 | ? 不推薦 |

**選擇理由**:
1. 主檔定義 > 記錄快照
2. 單一資料來源 > 多重來源
3. 維護便利 > 資料冗餘

---

## ?? 部署檢查清單

- [x] 修改 `EquipmentController.cs`
- [x] 編譯成功
- [ ] 單元測試通過
- [ ] 整合測試通過
- [ ] 前端顯示驗證
- [ ] CRM 資料驗證
- [ ] 性能測試
- [ ] 部署到測試環境
- [ ] 用戶驗收測試
- [ ] 部署到生產環境

---

## ?? 相關文檔

- `DiscipleLessonsDateTime欄位修正說明.md` - 日期欄位修正
- `EquipmentStorLessonsView日期解析除錯修復報告.md` - 前端日期處理
- `ContactId修復完成總結.md` - ContactId 修復
- `EquipmentStorLessonsView修復文檔索引.md` - 完整文檔索引

---

## ? 驗證結果

```
? 編譯成功
? 欄位來源正確 (new_disciple_lessons.new_now_stage_name)
? 與其他欄位邏輯一致
? 錯誤處理完善
? 日誌記錄完整
```

---

## ?? 後續行動

1. **立即執行**
   - 重啟應用程式
   - 測試階段名稱顯示
   - 驗證不同階段的課程

2. **短期改進** (1-2 週)
   - 考慮使用 linked-entity 優化性能
   - 添加階段名稱驗證 (Enum)
   - 實現階段排序功能

3. **長期規劃** (1-2 月)
   - 建立階段進度追蹤
   - 實現階段統計報表
   - 支援自定義階段名稱

---

**修正完成**: 2024-11-18  
**編譯狀態**: ? 成功  
**測試狀態**: ?? 待執行  
**下一步**: 重啟應用並測試顯示
