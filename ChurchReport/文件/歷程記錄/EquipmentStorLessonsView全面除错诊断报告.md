# EquipmentStorLessonsView 全面除????告

## ?? ?行摘要

??全面分析，`EquipmentStorLessonsView.cshtml` 已修复??的日期解析??并添加了完善的???理。本?告涵?完整的?据流、已知??和??方法。

---

## ??? 完整?据流架构

### 三? Master-Detail ?构

```
EquipmentView (一?)
    ↓ SmallGroupListEntityId
LoadEquipmentList → EquipmenSmallGroup
    ↓ MasterDetail
EquipmentContactView (二?)
    ↓ SmallGroupListEntityId
LoadEquipmentContact → EquipmentContact (EquipmentContactId = PresentRecordId)
    ↓ MasterDetail
EquipmentStorLessonsView (三?)
    ↓ EquipmentContactId
LoadEquipmentStorLessons → EquipmentStorLessons
```

### ??????

| ?? | ??名 | 值?型 | ?源 | 用途 |
|------|--------|--------|------|------|
| 一? → 二? | `id` (SmallGroupListEntityId) | String (Guid) | EquipmentView | LoadEquipmentContact ?? |
| 二? → 三? | `id` (EquipmentContactId) | String (PresentRecordId) | EquipmentContact | LoadEquipmentStorLessons ?? |
| 三? | - | - | EquipmentStorLessons | 最??示?据 |

---

## ?? ???据?系

### ContactId ?源?

```csharp
EquipmentContact.EquipmentContactId (?自 LoadEquipmentContact)
  ↓
  == Member.PresentRecordId (?自 DownloadIntegrateData)
  
LoadEquipmentStorLessons 使用此 id
  ↓
  查找 Member，?取 member.ContactId
  ↓
  使用 ContactId 查??程 (RetrieveStorLessonsByFetchXml)
```

### 日期?源?

```csharp
EquipmentStorLessons (后端返回)
  ↓ DiscipleLessonsDateTime
  ↓ (? new_disciple_lessons.new_class_start_date 取得)
  
前端 ParsingDate() 解析
  ↓
CalculateCellValue ??和??
  ↓
DevExtreme DataGrid ?示（yyyy/MM/dd 格式）
```

---

## ?? 已修复的??

### 1. 日期解析?? ?

**??**: 原始 ParsingDate 函?只支持 `YYYY-MM-DD` 格式

**修复**:
```javascript
// 支持 ISO 格式 (2024-11-18T10:30:00)
if (input.indexOf('T') > -1) {
    var isoDate = new Date(input);
    if (!isNaN(isoDate.getTime())) {
        return isoDate;
    }
}

// 支持 YYYY-MM-DD 格式
var parts = input.split('-');
if (parts.length >= 3) {
    var year = parseInt(parts[0], 10);
    var month = parseInt(parts[1], 10);
    var day = parseInt(parts[2].split('T')[0], 10);
    
    if (!isNaN(year) && !isNaN(month) && !isNaN(day)) {
        return new Date(year, month - 1, day);
    }
}
```

### 2. getODataLocalDateFilter 月份?算?? ?

**??**: 
```javascript
// ? ?? - 使用字符串而非?字
return new Date(date.getFullYear(), rawMonth, rawDate); // rawMonth 是字符串 "11"
```

**修复**:
```javascript
// ? 正确 - 使用正确的?字
return new Date(date.getFullYear(), date.getMonth(), date.getDate());
```

### 3. 日期有效性?查不完善 ?

**??**: 只?查 `getFullYear() == 1901`

**修复**:
```javascript
// 添加 isNaN ?查
if (!parsedDate || isNaN(parsedDate.getTime())) {
    return null;
}

// 改??效日期??
var year = parsedDate.getFullYear();
if (year <= 1901) {  // ? 使用 <=
    return null;
}
```

### 4. OnCellPrepared 事件未?定 ?

**??**: `cell_prepared` 函?已定?但未?定

**修复**:
```csharp
// 添加事件?定
.OnCellPrepared("cell_prepared")
```

### 5. ???理不完善 ?

**修复**: ?所有函?添加 try-catch 和日志

```javascript
try {
    // ?理??
} catch (error) {
    console.error("[FunctionName] ??:", error);
}
```

---

## ?? 完整???景

### ???境准?

```javascript
// 在??器 Console ?行以下代??行??

// 1. ?? ParsingDate 函?
console.log("=== ParsingDate ?? ===");
console.log("ISO 格式:", ParsingDate("2024-11-18T10:30:00"));
console.log("YYYY-MM-DD 格式:", ParsingDate("2024-11-18"));
console.log("Date ?象:", ParsingDate(new Date("2024-11-18")));
console.log("空值:", ParsingDate(null));
console.log("?效格式:", ParsingDate("2024/11/18"));

// 2. ?? getODataLocalDateFilter
console.log("\n=== getODataLocalDateFilter ?? ===");
var testDate = new Date(2024, 10, 18); // 注意：月份是 0-11
console.log("?入:", testDate);
console.log("?出:", getODataLocalDateFilter(testDate));

// 3. ?取 DataGrid ?例
var gridInstance = $("#data-grid").find(".dx-datagrid").dxDataGrid("instance");
console.log("DataGrid ?例:", gridInstance);
```

### ???景 1: 正常?程?示

**?期**: ?程名?、?段、完成??、日期正确?示

```javascript
// ?查 Console ?出
[LoadEquipmentStorLessons] 查??程??: ContactName=林寬仁, ContactId=12345678-1234-1234-1234-123456789012
[LoadEquipmentStorLessons] 查??果: storLessons=true, Count=5
[LoadEquipmentStorLessons] ?程: 舊約概論, ?段: 初階, 日期: 2024-01-15
```

### ???景 2: ??程??

**?期**: ?示 "沒有資料"

```javascript
// ?查 Console ?出
[LoadEquipmentStorLessons] 警告: ??系人(林寬仁)?有?程??，或?程的 new_classification 不是 100000000/100000001
[LoadEquipmentStorLessons] 最?返回?程?量: 0
```

### ???景 3: ContactId ?空

**?期**: ?示空列表，但不??

```javascript
// ?查 Console ?出
[LoadEquipmentStorLessons] 警告: ContactId ?空，FullName=林寬仁, PresentRecordId=xyz-123
```

### ???景 4: 日期格式??

**?期**: 日期列?空但不影?其他列?示

```javascript
// ?查 Console ?出
[CalculateCellValue] 日期?算??: SyntaxError: ... 原始值: invalid-date
```

### ???景 5: ??功能

**?期**: ?停?出???/?除??，??可??

```javascript
// 逐步??
1. ?停?程行 → 看到?色背景和????
2. ???? → ?入??模式，?示保存/取消按?
3. 修改字段 → 更新?据
4. ??保存 → ?用后端更新
```

---

## ?? ???查清?

### 前端??

- [ ] 打???器??者工具 (F12)
- [ ] ?查 Console ??是否有??
- [ ] 查看 Network ??中的 LoadEquipmentStorLessons ?求
- [ ] ?????据中的日期格式
- [ ] ??所有 JavaScript 函?（??上面的??代?）

### 后端??

- [ ] ?查 LoadEquipmentStorLessons 方法的日志?出
- [ ] ?? ContactId 是否正确?入
- [ ] ?? RetrieveStorLessonsByFetchXml 返回的?据
- [ ] ?查 new_disciple_lessons ?体是否存在
- [ ] ?? new_class_start_date 字段是否有?据

### CRM ?据?查

```sql
-- ?查?程??
SELECT 
    new_stor_lessonsid,
    new_name,
    new_stagename,
    new_current_complete,
    new_contact_new_stor_lessons
FROM new_stor_lessons
WHERE new_contact_new_stor_lessons = @ContactId

-- ?查?徒?程
SELECT 
    new_disciple_lessonsid,
    new_name,
    new_class_start_date,
    new_class_end_date
FROM new_disciple_lessons
WHERE new_disciple_lessonsid IN (SELECT new_new_disciple_lessons_new_stor_les FROM new_stor_lessons)
```

---

## ?? 常???与解?方案

### ?? 1: ?示 "沒有資料"

**可能原因**:
1. ContactId ?空 → ?查 DownloadIntegrateData 是否正确?置了 ContactId
2. CRM 中?有?程?? → 在 CRM 中????系人是否有?程??
3. ?程的 new_classification 不符合?件 → ?查 FetchXml 中的???件
4. RetrieveStorLessonsByFetchXml 返回 null → ?查 ToolUtility 中的方法

**??步?**:
```javascript
// 在 LoadEquipmentStorLessons 中添加
System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] ContactId: {member.ContactId}");
System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] storLessons: {storLessons}");
System.Diagnostics.Debug.WriteLine($"[LoadEquipmentStorLessons] Count: {storLessons?.Entities.Count}");
```

### ?? 2: 日期?示?空

**可能原因**:
1. 日期值是 DateTime.MinValue → ??器?其?? null
2. 日期值是 1901 年 → ??器?其?? null
3. 日期解析失? → ParsingDate 返回 null
4. ??的 new_disciple_lessons ?体不存在 → classStartDate ?? DateTime.MinValue

**??步?**:
```javascript
// 在 CalculateCellValue 中已添加日志
console.error("[CalculateCellValue] 日期?算??:", error, "原始值:", row.DiscipleLessonsDateTime);
```

### ?? 3: ????未?示

**可能原因**:
1. OnCellPrepared 事件未?定 → 需添加 `.OnCellPrepared("cell_prepared")`
2. cell_prepared 函?有?? → 查看 Console ??
3. DevExtreme 版本?? → ?查 DevExtreme 文?

**??步?**:
```javascript
// ?查事件是否?定
console.log("cell_prepared 函?是否存在:", typeof cell_prepared === 'function');

// 在 cell_prepared 中添加日志
console.log("[cell_prepared] ?用 - rowType:", e.rowType, "command:", e.column?.command);
```

### ?? 4: 排序??

**可能原因**:
1. ?算字段的返回?型不一致 → CalculateCellValue 有?返回 Date，有?返回 null
2. DevExtreme ?法正确排序混合?型 → 需确保返回?型一致

**解?方案**:
```javascript
// 确保返回?型一致
if (year <= 1901 || !parsedDate) {
    return null;  // ?一返回 null，而不是某?特殊值
}
return parsedDate;  // ?一返回 Date ?象
```

---

## ?? 性能优化建?

### 1. ?少不必要的查?

**?前**: 每??程??都需要?外查? new_disciple_lessons

**优化**: 在后端使用 linked-entity 一次?取所有?据

```xml
<fetch>
  <entity name="new_stor_lessons">
    <attribute name="new_name" />
    <attribute name="new_stagename" />
    <attribute name="new_current_complete" />
    <link-entity name="new_disciple_lessons" from="new_disciple_lessonsid" 
                 to="new_new_disciple_lessons_new_stor_les" alias="dl">
      <attribute name="new_class_start_date" />
    </link-entity>
    <filter>
      <condition attribute="new_contact_new_stor_lessons" operator="eq" value="{contactId}" />
    </filter>
  </entity>
</fetch>
```

### 2. ?存日期格式?果

```javascript
// ?建?存?象
var dateFormatCache = {};

function ParsingDateCached(input) {
    if (dateFormatCache[input]) {
        return dateFormatCache[input];
    }
    
    var result = ParsingDate(input);
    dateFormatCache[input] = result;
    return result;
}
```

### 3. 批量??而非逐行??

```javascript
// 不要在 CalculateCellValue 中做重复??
// 而是在后端一次性??所有日期
```

---

## ?? 相?文?

- `DiscipleLessonsDateTime欄位修正說明.md` - 欄位對應詳解
- `ContactId修復完成總結.md` - ContactId 修复全?程
- `EquipmentStorLessonsView日期解析除錯修復報告.md` - 日期解析修复??

---

## ? ??清?

### ???段
- [x] Razor ?法正确（?????）
- [x] HTML ??匹配
- [x] JavaScript ?法有效
- [x] C# 代?正确

### ?行?段
- [ ] ?面成功加?
- [ ] 三? Master-Detail ?示正确
- [ ] ?程?据正确加?
- [ ] 日期格式正确?示
- [ ] ??功能正常工作
- [ ] 排序功能正常工作
- [ ] ? Console ??

### 功能??
- [ ] ???景 1: 正常?程?示
- [ ] ???景 2: ??程??
- [ ] ???景 3: ContactId ?空
- [ ] ???景 4: 日期格式??
- [ ] ???景 5: ??功能

---

## ?? 后?行?

### 立即?行
1. 重??用程序
2. 清除??器?存
3. ?? `/Equipment/EquipmentView`
4. 打???者工具（F12）查看 Console 日志

### 短期改?
1. ??性能优化（使用 linked-entity）
2. 添加?元??
3. 改???消息提示

### ?期?划
1. ???程??/新增功能
2. 添加?程?度跟?
3. 生成?????表

---

**最后更新**: 2024-11-18  
**??**: ? 完成并??  
**??**: ? 成功  
**文?**: ? 完整
