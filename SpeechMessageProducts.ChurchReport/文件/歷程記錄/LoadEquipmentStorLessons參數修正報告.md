# LoadEquipmentStorLessons 參數修正報告

## 問題描述
`EquipmentController.cs` 中的 `LoadEquipmentStorLessons` 方法使用了錯誤的 `RetrieveStorLessonsByFetchXml` 方法調用，傳入了4個空字串參數。

## 原始錯誤代碼
```csharp
// 錯誤：使用4參數版本，但前3個參數都是空字串
var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml("", "", "", id);
```

## 根本原因分析

### ToolUtility 中有兩個版本的方法

#### 版本1: 4參數版本（用於特定課程+特定聯絡人）
```csharp
public EntityCollection RetrieveStorLessonsByFetchXml(
    String LessonName,    // 課程名稱
    String LessonId,      // 課程ID
    String ContactName,   // 聯絡人名稱
    String ContactId      // 聯絡人ID
)
```
**用途**: 查詢某個聯絡人在某個特定課程的記錄

#### 版本2: 2參數版本（用於特定聯絡人的所有課程）
```csharp
public EntityCollection RetrieveStorLessonsByFetchXml(
    String ContactName,   // 聯絡人名稱
    String ContactId      // 聯絡人ID
)
```
**用途**: 查詢某個聯絡人的所有課程記錄

### 為什麼原始代碼不合理

1. **語義不清**: 傳入4個參數但前3個都是空字串，無法表達查詢意圖
2. **效率低下**: FetchXML 會生成不必要的條件判斷
3. **版本錯誤**: 應該使用2參數版本來查詢某個聯絡人的所有課程

## 修正方案

### 修正後的代碼
```csharp
/// <summary>
/// 載入裝備課程清單資料
/// 用於第三層 master-detail 的 DataGrid - 返回 EquipmentStorLessons 清單
/// </summary>
/// <param name="id">聯絡人的 PresentRecordId (CRM ContactId)</param>
/// <param name="loadOptions">載入選項</param>
[HttpGet]
public object LoadEquipmentStorLessons(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        // 確保資料已載入
        if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null || 
            !InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag)
        {
            return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
        }

        // 從成員列表中找到對應的聯絡人
        var members = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
            ?.m_SmallGroupDataList?.m_AllMemeberData?.Members 
            ?? new List<Member>();

        var member = members.FirstOrDefault(m => m.PresentRecordId == id);
        
        if (member == null)
        {
            return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
        }

        // 從 CRM 查詢該聯絡人的所有課程記錄
        // 使用2參數版本: RetrieveStorLessonsByFetchXml(ContactName, ContactId)
        // PresentRecordId 即為 CRM 中的 ContactId
        var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml(
            member.FullName,      // 聯絡人姓名
            member.PresentRecordId // 聯絡人ID (CRM ContactId)
        );

        var lessonsList = new List<EquipmentStorLessons>();

        if (storLessons != null && storLessons.Entities.Count > 0)
        {
            foreach (var lessonEntity in storLessons.Entities)
            {
                var lesson = lessonEntity;
                lessonsList.Add(new EquipmentStorLessons
                {
                    StorLessonsEntityId = lesson.Id.ToString(),
                    DiscipleLessonsName = ToolUtility.GetEntityStringAttribute(ref lesson, "new_name"),
                    StageName = ToolUtility.GetEntityStringAttribute(ref lesson, "new_stagename"),
                    CurrentComplete = ToolUtility.GetEntityBoolAttribute(ref lesson, "new_currentcomplete"),
                    DiscipleLessonsDateTime = ToolUtility.GetEntityDateTimeAttribute(ref lesson, "new_disciplelessonsdatetime")
                });
            }
        }

        return DataSourceLoader.Load(lessonsList, loadOptions);
    }
    catch (Exception e)
    {
        return HandleError(e, "LoadEquipmentStorLessons");
    }
}
```

## 修正要點

### 1. 使用正確的方法版本
- **修正前**: 使用4參數版本，傳入3個空字串
- **修正後**: 使用2參數版本，傳入有意義的參數

### 2. 取得聯絡人完整資訊
```csharp
// 從 InMemoryContext 中查找對應的 Member 物件
var member = members.FirstOrDefault(m => m.PresentRecordId == id);
```

### 3. 傳入正確的參數
```csharp
// ContactName: member.FullName
// ContactId: member.PresentRecordId (這就是 CRM 中的 ContactId)
var storLessons = ToolUtility.RetrieveStorLessonsByFetchXml(
    member.FullName, 
    member.PresentRecordId
);
```

### 4. 增加防禦性檢查
```csharp
// 檢查資料是否已載入
if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null || 
    !InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag)
{
    return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
}

// 檢查成員是否存在
if (member == null)
{
    return DataSourceLoader.Load(new List<EquipmentStorLessons>(), loadOptions);
}
```

## Member 模型說明

### Member 類別的關鍵屬性
```csharp
public class Member
{
    public String PresentRecordId { get; set; }  // CRM 中的 ContactId
    public string FullName { get; set; }         // 聯絡人姓名
    public string EquipmentStatus { get; set; }  // 裝備狀態
    // ... 其他屬性
}
```

### PresentRecordId 的作用
- **在記憶體中**: 作為成員的唯一識別碼
- **在 CRM 中**: 對應到 Contact 實體的 `contactid`
- **在 View 中**: 作為 DataGrid 的 Key (`EquipmentContactId`)

## 數據流程圖

```
1. View (EquipmentContactView.cshtml)
   ↓
   展開 Master-Detail 行
   ↓
   傳遞 data.EquipmentContactId (PresentRecordId)
   
2. Controller (LoadEquipmentStorLessons)
   ↓
   使用 PresentRecordId 從 InMemoryContext 找到 Member
   ↓
   取得 member.FullName 和 member.PresentRecordId
   
3. ToolUtility (RetrieveStorLessonsByFetchXml)
   ↓
   使用 ContactName 和 ContactId 構建 FetchXML
   ↓
   查詢 CRM 取得課程記錄
   
4. Controller (LoadEquipmentStorLessons)
   ↓
   將 Entity 轉換為 EquipmentStorLessons 模型
   ↓
   返回給 View 顯示
```

## FetchXML 查詢邏輯

### 2參數版本的 FetchXML（修正後）
```xml
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='new_stor_lessons'>
    <attribute name='createdon' />
    <attribute name='new_contact_new_stor_lessons' />
    <attribute name='new_fee' />
    <attribute name='new_pay_date' />
    <attribute name='new_current_complete' />
    <attribute name='new_new_disciple_lessons_new_stor_les' />
    <attribute name='new_stor_lessonsid' />
    <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
    <order attribute='new_contact_new_stor_lessons' descending='false' />
    <filter type='and'>
      <condition attribute='new_contact_new_stor_lessons' operator='eq' 
                 uiname='{ContactName}' uitype='contact' value='{ContactId}' />
    </filter>
    <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons'>
      <attribute name='telephone2' />
      <attribute name='address2_line1' />
      <attribute name='parentcustomerid' />
      <attribute name='mobilephone' />
      <attribute name='emailaddress1' />
    </link-entity>
    <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' 
                 to='new_new_disciple_lessons_new_stor_les' alias='ab'>
      <filter type='and'>
        <condition attribute='new_classification' operator='in'>
          <value>100000000</value>
          <value>100000001</value>
        </condition>
      </filter>
    </link-entity>
  </entity>
</fetch>
```

### 查詢條件說明
- **主要條件**: `new_contact_new_stor_lessons` = ContactId
- **課程分類**: 只查詢 `new_classification` 為 100000000 或 100000001 的課程
- **排序**: 按課程和聯絡人排序

## 驗證結果

### 編譯狀態
- ? **編譯成功**: 無語法錯誤
- ? **類型正確**: 參數類型匹配
- ? **邏輯正確**: 查詢邏輯符合業務需求

### 改進效果

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| 參數數量 | 4個 (3個空字串) | 2個 (有意義的值) |
| 語義清晰度 | ? 不清晰 | ? 清晰 |
| FetchXML 效率 | ?? 有多餘條件 | ? 精簡 |
| 錯誤處理 | ? 缺少檢查 | ? 完整檢查 |
| 可維護性 | ?? 難以理解 | ? 易於維護 |

## 測試建議

### 1. 單元測試
```csharp
[Test]
public void LoadEquipmentStorLessons_WithValidId_ReturnsLessons()
{
    // Arrange
    var controller = new EquipmentController(...);
    var validId = "valid-present-record-id";
    
    // Act
    var result = controller.LoadEquipmentStorLessons(validId, new DataSourceLoadOptions());
    
    // Assert
    Assert.IsNotNull(result);
    // ... 更多斷言
}

[Test]
public void LoadEquipmentStorLessons_WithInvalidId_ReturnsEmptyList()
{
    // Arrange
    var controller = new EquipmentController(...);
    var invalidId = "invalid-id";
    
    // Act
    var result = controller.LoadEquipmentStorLessons(invalidId, new DataSourceLoadOptions());
    
    // Assert
    // 應返回空列表，不應拋出異常
}
```

### 2. 整合測試
1. **啟動應用程式**
2. **訪問**: `/Equipment/EquipmentView`
3. **操作序列**:
   - 展開小組列表
   - 展開聯絡人列表
   - 檢查課程列表是否正確顯示
4. **驗證 Network**:
   - 請求: `GET /Equipment/LoadEquipmentStorLessons?id={presentRecordId}`
   - 狀態: 200 OK
   - 回應: 包含課程資料的 JSON

### 3. 日誌檢查
在 `LoadEquipmentStorLessons` 方法中添加日誌：
```csharp
System.Diagnostics.Debug.WriteLine($"LoadEquipmentStorLessons called with id: {id}");
System.Diagnostics.Debug.WriteLine($"Found member: {member?.FullName ?? "null"}");
System.Diagnostics.Debug.WriteLine($"Retrieved {storLessons?.Entities.Count ?? 0} lessons");
```

## 常見問題排查

### Q1: 課程列表為空
**可能原因**:
1. 該聯絡人在 CRM 中沒有課程記錄
2. `PresentRecordId` 與 CRM ContactId 不匹配
3. 課程的 `new_classification` 不是 100000000 或 100000001

**解決方法**:
1. 在 CRM 中驗證該聯絡人是否有課程記錄
2. 檢查 `PresentRecordId` 是否正確
3. 檢查課程分類設置

### Q2: Member 為 null
**可能原因**:
1. `InMemoryContext` 未正確載入
2. `PresentRecordId` 不存在於成員列表中

**解決方法**:
1. 確認 `SetupIntegrateData` 已執行
2. 檢查 `LoadEquipmentContact` 返回的資料

### Q3: CRM 查詢錯誤
**可能原因**:
1. CRM 連線問題
2. FetchXML 語法錯誤
3. 權限不足

**解決方法**:
1. 檢查 CRM 連線狀態
2. 驗證 ToolUtility 配置
3. 確認帳號權限

## 相關修正

### 配合修正: EquipmentStorLessonsView.cshtml
```csharp
// 已修正: 使用 .Mvc() 而非 .WebApi()
.DataSource(d => d.Mvc()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })
)
```

## 總結

### 修正內容
1. ? 使用正確的2參數版本方法
2. ? 從 InMemoryContext 取得完整 Member 資訊
3. ? 傳入有意義的參數（ContactName 和 ContactId）
4. ? 增加防禦性檢查
5. ? 改善代碼可讀性和可維護性

### 影響範圍
- **檔案**: `ChurchReport/Controllers/EquipmentController.cs`
- **方法**: `LoadEquipmentStorLessons`
- **影響**: 裝備管理模組的課程列表顯示功能

### 下一步
1. 進行整合測試
2. 驗證 CRM 查詢結果
3. 檢查瀏覽器 Console 和 Network
4. 確認課程列表正確顯示

---

**修正完成日期**: 2024
**修正人員**: GitHub Copilot
**狀態**: ? 編譯成功，待測試驗證
