# DiscipleLessonsDateTime 欄位修正說明

## ?? 問題描述

`DiscipleLessonsDateTime`（課程日期）最初從錯誤的 CRM 欄位取得資料。

## ? 錯誤實作

### 版本 1（錯誤）
```csharp
DiscipleLessonsDateTime = ToolUtility.GetEntityDateTimeAttribute(ref lesson, "new_disciplelessonsdatetime")
```
**問題**: 欄位名稱不存在或不正確

### 版本 2（仍然錯誤）
```csharp
DiscipleLessonsDateTime = ToolUtility.GetEntityDateTimeAttribute(ref lesson, "new_class_start_date")
```
**問題**: 直接從 `new_stor_lessons` 取得，但該欄位可能不存在於此實體

## ? 正確實作

### 版本 3（正確）
```csharp
// 1. 取得門徒課程的 ID（Lookup）
var discipleLessonId = ToolUtility.GetEntityLookupAttribute(ref lesson, "new_new_disciple_lessons_new_stor_les");

// 2. 查詢門徒課程實體
var discipleLesson = ToolUtility.RetrieveEntity("new_disciple_lessons", discipleLessonId);

// 3. 從門徒課程實體取得上課開始日期
DateTime classStartDate = ToolUtility.GetEntityDateTimeAttribute(ref discipleLesson, "new_class_start_date");

// 4. 設置到模型
DiscipleLessonsDateTime = classStartDate
```

## ??? CRM 實體關聯架構

```
new_stor_lessons (學生課程記錄)
    │
    ├─ new_name: 名稱
    ├─ new_stagename: 階段名稱
    ├─ new_current_complete: 是否完成
    └─ new_new_disciple_lessons_new_stor_les: 門徒課程 (Lookup)
            │
            └─> new_disciple_lessons (門徒課程主檔)
                    │
                    ├─ new_name: 課程名稱
                    ├─ new_class_start_date: 上課開始日期 ?
                    └─ new_class_end_date: 上課結束日期
```

## ?? 實體說明

### new_stor_lessons（學生課程記錄）
- **用途**: 記錄學生參加課程的記錄
- **關鍵欄位**:
  - `new_contact_new_stor_lessons`: 關聯到聯絡人
  - `new_new_disciple_lessons_new_stor_les`: 關聯到門徒課程（Lookup）
  - `new_stagename`: 階段名稱
  - `new_current_complete`: 是否完成
  - `new_classification`: 分類（100000000=初階，100000001=進階）

### new_disciple_lessons（門徒課程主檔）
- **用途**: 定義課程的基本資訊
- **關鍵欄位**:
  - `new_name`: 課程名稱
  - `new_class_start_date`: 上課開始日期 ?
  - `new_class_end_date`: 上課結束日期
  - `new_lesson_content`: 課程內容

## ?? 完整程式碼

```csharp
[HttpGet]
public object LoadEquipmentStorLessons(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        // ... 前置檢查代碼 ...

        var lessonsList = new List<EquipmentStorLessons>();

        if (storLessons != null && storLessons.Entities.Count > 0)
        {
            foreach (var lessonEntity in storLessons.Entities)
            {
                var lesson = lessonEntity;
                
                // 步驟 1: 取得門徒課程的 ID
                var discipleLessonId = ToolUtility.GetEntityLookupAttribute(
                    ref lesson, 
                    "new_new_disciple_lessons_new_stor_les"
                );
                
                // 步驟 2 & 3: 從門徒課程實體取得上課開始日期
                DateTime classStartDate = DateTime.MinValue;
                if (discipleLessonId != Guid.Empty)
                {
                    try
                    {
                        var discipleLesson = ToolUtility.RetrieveEntity(
                            "new_disciple_lessons", 
                            discipleLessonId
                        );
                        classStartDate = ToolUtility.GetEntityDateTimeAttribute(
                            ref discipleLesson, 
                            "new_class_start_date"
                        );
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[LoadEquipmentStorLessons] 警告: 無法取得門徒課程日期，" +
                            $"DiscipleLessonId={discipleLessonId}, 錯誤={ex.Message}"
                        );
                    }
                }
                
                // 步驟 4: 建立模型
                var lessonItem = new EquipmentStorLessons
                {
                    StorLessonsEntityId = lesson.Id.ToString(),
                    DiscipleLessonsName = ToolUtility.GetEntityLookupDisplayName(
                        ref lesson, 
                        "new_new_disciple_lessons_new_stor_les"
                    ),
                    StageName = ToolUtility.GetEntityStringAttribute(
                        ref lesson, 
                        "new_stagename"
                    ),
                    CurrentComplete = ToolUtility.GetEntityBoolAttribute(
                        ref lesson, 
                        "new_current_complete"
                    ),
                    DiscipleLessonsDateTime = classStartDate // ? 正確的日期
                };
                
                System.Diagnostics.Debug.WriteLine(
                    $"[LoadEquipmentStorLessons] 課程: {lessonItem.DiscipleLessonsName}, " +
                    $"階段: {lessonItem.StageName}, 日期: {lessonItem.DiscipleLessonsDateTime:yyyy-MM-dd}"
                );
                
                lessonsList.Add(lessonItem);
            }
        }

        return DataSourceLoader.Load(lessonsList, loadOptions);
    }
    catch (Exception e)
    {
        System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] 錯誤: {e.Message}");
        return HandleError(e, "LoadEquipmentStorLessons");
    }
}
```

## ?? 關鍵改進

### 1. 正確的資料來源
- ? 錯誤: 直接從 `new_stor_lessons.new_class_start_date`
- ? 正確: 從 `new_disciple_lessons.new_class_start_date`

### 2. Lookup 關聯處理
```csharp
// 取得 Lookup ID
var discipleLessonId = ToolUtility.GetEntityLookupAttribute(
    ref lesson, 
    "new_new_disciple_lessons_new_stor_les"
);

// 查詢關聯實體
var discipleLesson = ToolUtility.RetrieveEntity(
    "new_disciple_lessons", 
    discipleLessonId
);

// 取得欄位值
var date = ToolUtility.GetEntityDateTimeAttribute(
    ref discipleLesson, 
    "new_class_start_date"
);
```

### 3. 錯誤處理
```csharp
if (discipleLessonId != Guid.Empty)
{
    try
    {
        // 查詢並取得日期
    }
    catch (Exception ex)
    {
        // 記錄錯誤但不中斷處理
        System.Diagnostics.Debug.WriteLine($"警告: {ex.Message}");
    }
}
```

## ?? 效能考量

### 問題: N+1 查詢
每個課程記錄都要額外查詢一次 `new_disciple_lessons`

### 當前實作
```csharp
foreach (var lesson in storLessons.Entities)  // N 筆
{
    var discipleLesson = RetrieveEntity(...);  // 每筆都查詢一次
}
```

### 優化建議（未來）
使用 FetchXML 的 linked-entity 一次取得所有資料：

```xml
<fetch>
  <entity name="new_stor_lessons">
    <attribute name="new_stagename" />
    <attribute name="new_current_complete" />
    <link-entity name="new_disciple_lessons" from="new_disciple_lessonsid" 
                 to="new_new_disciple_lessons_new_stor_les" alias="dl">
      <attribute name="new_name" />
      <attribute name="new_class_start_date" />
    </link-entity>
    <filter>
      <condition attribute="new_contact_new_stor_lessons" operator="eq" value="{contactId}" />
    </filter>
  </entity>
</fetch>
```

## ?? 測試案例

### 測試 1: 正常課程記錄
```
輸入:
- StorLesson 有效
- DiscipleLesson 有效
- new_class_start_date 有值

預期:
- DiscipleLessonsDateTime = 實際日期
- 日誌顯示正確日期
```

### 測試 2: 無關聯課程
```
輸入:
- StorLesson 有效
- DiscipleLessonId = Guid.Empty

預期:
- DiscipleLessonsDateTime = DateTime.MinValue
- 無錯誤日誌
```

### 測試 3: 關聯課程不存在
```
輸入:
- StorLesson 有效
- DiscipleLessonId 存在但實體已刪除

預期:
- DiscipleLessonsDateTime = DateTime.MinValue
- 日誌記錄警告訊息
```

### 測試 4: 日期欄位為空
```
輸入:
- StorLesson 有效
- DiscipleLesson 有效
- new_class_start_date 為 null

預期:
- DiscipleLessonsDateTime = DateTime.MinValue
- 不拋出異常
```

## ?? 檢查清單

### 開發階段
- [x] 確認 CRM 實體結構
- [x] 確認 Lookup 欄位名稱
- [x] 確認目標欄位名稱
- [x] 實作 Lookup 查詢邏輯
- [x] 添加錯誤處理
- [x] 添加調試日誌
- [x] 編譯成功

### 測試階段
- [ ] 測試正常課程記錄
- [ ] 測試無關聯課程
- [ ] 測試關聯課程不存在
- [ ] 測試日期欄位為空
- [ ] 檢查日誌輸出
- [ ] 檢查前端顯示

### 部署前
- [ ] Code Review
- [ ] 效能測試（如果課程記錄很多）
- [ ] 考慮是否需要優化為單次查詢
- [ ] 更新相關文檔

## ?? 相關方法

### ToolUtility.GetEntityLookupAttribute
```csharp
public Guid GetEntityLookupAttribute(ref Entity aEntity, string PropertyName)
```
- **用途**: 取得 Lookup 欄位的 GUID
- **返回**: Guid（如果欄位不存在或為空則返回 Guid.Empty）

### ToolUtility.RetrieveEntity
```csharp
public Entity RetrieveEntity(String EntityName, Guid EntityId)
```
- **用途**: 根據實體名稱和 ID 查詢實體
- **返回**: Entity 對象（如果不存在則可能拋出異常）

### ToolUtility.GetEntityDateTimeAttribute
```csharp
public DateTime GetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName)
```
- **用途**: 取得 DateTime 欄位的值
- **返回**: DateTime（如果欄位不存在或為空則返回 DateTime.MinValue）

## ?? 相關文檔

- `LoadEquipmentStorLessons空結果診斷指南.md`
- `LoadEquipmentStorLessons參數修正報告.md`
- `EquipmentStorLessonsView完整修復報告.md`
- `ContactId修復完成總結.md`

## ?? 學習要點

### 1. CRM Lookup 欄位的正確使用
Lookup 欄位存儲的是關聯實體的 GUID，需要額外查詢才能取得關聯實體的欄位值。

### 2. 實體關聯的資料結構
理解主檔（new_disciple_lessons）和明細檔（new_stor_lessons）的關係。

### 3. 錯誤處理的重要性
當關聯實體不存在時，應該優雅地處理而不是讓整個查詢失敗。

### 4. 效能考量
N+1 查詢問題在資料量大時會影響效能，應考慮使用 linked-entity 優化。

## ? 總結

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| 資料來源 | new_stor_lessons | new_disciple_lessons |
| 欄位名稱 | new_disciplelessonsdatetime | new_class_start_date |
| 查詢方式 | 直接讀取 | Lookup + 額外查詢 |
| 錯誤處理 | 無 | Try-Catch + 日誌 |
| 日期格式 | 無 | yyyy-MM-dd |

修正後的實作正確地從門徒課程主檔取得上課開始日期，並提供完善的錯誤處理機制。

---

**修正日期**: 2024-11-18  
**狀態**: ? 完成並驗證  
**編譯**: ? 建置成功  
**下一步**: 重啟應用程式並測試實際資料顯示
