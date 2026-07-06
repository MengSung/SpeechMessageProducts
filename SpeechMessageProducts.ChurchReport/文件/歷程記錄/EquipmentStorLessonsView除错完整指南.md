# EquipmentStorLessonsView 除?完整指南

## ?? 快速?始

### 步? 1: ???用

```bash
# 清除之前的构建
dotnet clean

# 重新构建
dotnet build

# ?行?用
dotnet run
```

### 步? 2: 打??面

??: `https://localhost:5001/Equipment/EquipmentView`

### 步? 3: 打???者工具

```
按 F12 打???者工具
?? Console ??
```

### 步? 4: 复制??工具

复制 `EquipmentStorLessonsView快速??工具.js` 的?容到 Console 中?行

---

## ?? 三??据?构?解

### 第一?: EquipmentView (小?列表)

```html
<!-- 文件: EquipmentView.cshtml -->
<!-- ?示: 小?列表 -->
<!-- 控制器: LoadEquipmentList -->
<!-- ??: id (ListId) -->
<!-- 返回: EquipmenSmallGroup[] -->
```

**??配置**:
```csharp
.DataSource(d => d
    .WebApi()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentList")
    .Key("SmallGroupListEntityId")  // ← ??主?
)
```

**????**: 
```javascript
data.SmallGroupListEntityId  // ??第二?作? id ??
```

---

### 第二?: EquipmentContactView (?系人列表)

```html
<!-- 文件: EquipmentContactView.cshtml -->
<!-- ?示: 小?成?列表 -->
<!-- 控制器: LoadEquipmentContact -->
<!-- ??: id (SmallGroupListEntityId) -->
<!-- 返回: EquipmentContact[] -->
```

**??配置**:
```csharp
.DataSource(d => d
    .Mvc()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentContact")
    .Key("EquipmentContactId")  // ← ??主?
    .LoadParams(new { id = new JS("data.SmallGroupListEntityId") })  // ← ??
)
```

**????**:
```javascript
// EquipmentContactId ??上是 Member.PresentRecordId
data.EquipmentContactId  // ??第三?作? id ??
```

---

### 第三?: EquipmentStorLessonsView (?程列表)

```html
<!-- 文件: EquipmentStorLessonsView.cshtml -->
<!-- ?示: ?程?? -->
<!-- 控制器: LoadEquipmentStorLessons -->
<!-- ??: id (EquipmentContactId = PresentRecordId) -->
<!-- 返回: EquipmentStorLessons[] -->
```

**??配置**:
```csharp
.DataSource(d => d.Mvc()
    .Controller("Equipment")
    .LoadAction("LoadEquipmentStorLessons")
    .Key("StorLessonsEntityId")
    .LoadParams(new { id = new JS("data.EquipmentContactId") })  // ← ??
)
```

---

## ?? 完整?据流

### 流程?

```
用??? EquipmentView
        ↓
    加?第一? (小?)
        ↓
用?展?小?行
        ↓
    加?第二? (?系人)
    LoadEquipmentContact(SmallGroupListEntityId)
        ↓
用?展??系人行
        ↓
    加?第三? (?程)
    LoadEquipmentStorLessons(EquipmentContactId)
        ↓
后端查??程:
    1. 根据 PresentRecordId 找到 Member
    2. ?取 Member.ContactId
    3. ?用 RetrieveStorLessonsByFetchXml(ContactId)
    4. 查???的 new_disciple_lessons ?取日期
        ↓
前端?理日期:
    1. ParsingDate() 解析日期字符串
    2. CalculateCellValue() ???效日期
    3. DevExtreme ?示格式化的日期
        ↓
    ?示?程列表
```

---

## ?? 逐步??指南

### ??步? 1: ?? HTML ?构

```javascript
// Console 中?行
var $gridContainer = $("#data-grid");
console.log("DataGrid 容器存在:", $gridContainer.length > 0);
console.log("HTML:", $gridContainer.html().substring(0, 200));
```

**?期**: DataGrid 容器??存在

---

### ??步? 2: ?? JavaScript 函?

```javascript
// Console 中?行
console.log("ParsingDate:", typeof ParsingDate);
console.log("cell_prepared:", typeof cell_prepared);
console.log("OnRowPrepared:", typeof OnRowPrepared);
```

**?期**: 所有函?都???示 "function"

---

### ??步? 3: ?? DataGrid ?例

```javascript
// Console 中?行
var gridInstance = $("#data-grid").find(".dx-datagrid").dxDataGrid("instance");
console.log("DataGrid ?例:", gridInstance);
console.log("?据行?:", gridInstance.totalCount());
```

**?期**: ??能?取到?例和行?

---

### ??步? 4: ???据加?

```javascript
// Console 中?行
var dataSource = gridInstance.getDataSource();
dataSource.load().done(function(data) {
    console.log("已加??据行?:", data.length);
    if (data.length > 0) {
        console.log("第一行:", data[0]);
    }
});
```

**?期**: ??能看到??的?程?据

---

### ??步? 5: ??日期?理

```javascript
// Console 中?行
// 假?已加??据
var testDate = data[0].DiscipleLessonsDateTime;
console.log("原始日期:", testDate);
console.log("日期?型:", typeof testDate);

// ?? ParsingDate
var parsed = ParsingDate(testDate);
console.log("解析后:", parsed);
console.log("格式化:", parsed ? parsed.toLocaleDateString('zh-TW') : 'null');
```

**?期**: 日期??被正确解析

---

### ??步? 6: ??后端?据

?查??器 Network ??:

```
1. 打? Network ??
2. 展?一??系人行
3. 查找 LoadEquipmentStorLessons ?求
4. ?查??体中的 DiscipleLessonsDateTime 值
```

**?期**: ????包含日期值

**示例??**:
```json
[
  {
    "StorLessonsEntityId": "12345678-1234-1234-1234-123456789012",
    "DiscipleLessonsName": "舊約概論",
    "StageName": "初階",
    "CurrentComplete": true,
    "DiscipleLessonsDateTime": "2024-01-15T00:00:00"
  }
]
```

---

## ?? 常???排查

### ?? A: ?示 "沒有資料"

**可能原因列表**:
1. ContactId ?空
2. CRM 中?有?程??
3. new_disciple_lessons ?体不存在
4. 日期值全部被??

**排查步?**:

```javascript
// 步? 1: ?查 Network ?求是否?送
// 打? Network ??，展??系人行，查看是否有 LoadEquipmentStorLessons ?求

// 步? 2: ?查???据
// Network ??中右?????求 → 在新???中打???
// 查看返回的??是否?空

// 步? 3: ?查后端日志
// Visual Studio Output 窗口查看???出
System.Diagnostics.Debug.WriteLine(...) 的?出

// 步? 4: ?查 CRM ?据
// 登? CRM，????系人是否真的有?程??
```

---

### ?? B: 日期?示?空

**可能原因列表**:
1. 原始日期值是 1901 年或之前
2. 日期解析失?
3. ??的 new_disciple_lessons 不存在
4. new_class_start_date 字段?空

**排查步?**:

```javascript
// 步? 1: ?查原始值
console.log("原始日期:", data[0].DiscipleLessonsDateTime);

// 步? 2: ?查解析?果
var parsed = ParsingDate(data[0].DiscipleLessonsDateTime);
console.log("解析?果:", parsed);

// 步? 3: ?查年份
if (parsed) {
    console.log("年份:", parsed.getFullYear());
    if (parsed.getFullYear() <= 1901) {
        console.log("→ 日期被??（年份 <= 1901）");
    }
}

// 步? 4: ?查后端日志
// 查看 LoadEquipmentStorLessons 中的日志:
// [LoadEquipmentStorLessons] 警告: ?法取得?徒?程日期
```

---

### ?? C: ????未?示

**可能原因列表**:
1. OnCellPrepared 未?定
2. cell_prepared 函?有??
3. DevExtreme 版本??
4. CSS ?藏了??

**排查步?**:

```javascript
// 步? 1: ?查函?是否存在
console.log("cell_prepared:", typeof cell_prepared);

// 步? 2: 查看 Console ??
// ??在 cell_prepared 日志中看到:
// [cell_prepared] ?用 - rowType: data command: edit

// 步? 3: 手?触?事件
// 在 Console 中?行:
var e = {
    rowType: 'data',
    column: { command: 'edit' },
    cellElement: $(".dx-edit-cell")[0],
    row: { isEditing: false }
};
cell_prepared(e);

// 步? 4: ?查 HTML
// 在 Elements ??中查看??列的 HTML ?构
```

---

### ?? D: 排序??

**可能原因列表**:
1. ?算字段返回?型不一致
2. 某些行的日期? null，某些? Date ?象

**排查步?**:

```javascript
// 步? 1: ?查所有日期值的?型
dataSource.load().done(function(data) {
    data.forEach((item, index) => {
        var type = item.DiscipleLessonsDateTime ? typeof item.DiscipleLessonsDateTime : 'null';
        console.log(`行 ${index}: ${type}`);
    });
});

// 步? 2: 确保 CalculateCellValue 返回一致的?型
// 修改 CalculateCellValue 使所有行都返回相同?型
```

---

## ?? 完整?查清?

### 前端?查

- [ ] ?面成功加?，? 404 ??
- [ ] Console 中? JavaScript ??
- [ ] DataGrid 容器正确?示
- [ ] 第一?小?列表?示
- [ ] 可展?小?行
- [ ] 第二??系人列表?示
- [ ] 可展??系人行
- [ ] 第三??程列表?示
- [ ] ?程日期正确?示（非 1901 年）
- [ ] ????正确?示
- [ ] 排序功能正常工作
- [ ] ? Network ??（全部 200 OK）

### 后端?查

- [ ] EquipmentController.LoadEquipmentList 正确返回?据
- [ ] EquipmentController.LoadEquipmentContact 正确返回?据
- [ ] EquipmentController.LoadEquipmentStorLessons 正确返回?据
- [ ] ContactId 正确?入
- [ ] RetrieveStorLessonsByFetchXml 返回有效?据
- [ ] 日期字段正确?置（non-1901）
- [ ] ??日志?出正确
- [ ] ?异常?出

### CRM ?据?查

- [ ] 小?存在 CRM 中
- [ ] ?系人存在 CRM 中
- [ ] ?系人有有效的 ContactId
- [ ] ?程??存在 CRM 中
- [ ] ?程??了 new_disciple_lessons
- [ ] new_class_start_date 有有效值
- [ ] new_classification 符合???件

---

## ?? 常用??命令

```javascript
// 刷新?据
refreshEquipmentGrid()

// 展?所有行
expandAllRows()

// ?取所有?据
showAllData()

// ??日期解析
testDateParsing("2024-11-18")
testDateParsing("2024-11-18T10:30:00")
testDateParsing("1901-01-01")

// ?取?中行
getSelectedRows()

// 手??用行准?事件
OnRowPrepared({
    rowType: 'data',
    rowElement: $(".dx-data-row")[0]
})
```

---

## ?? 性能?控

```javascript
// ?控日期解析性能
console.time("DateParsing");
for (let i = 0; i < 1000; i++) {
    ParsingDate("2024-11-18T10:30:00");
}
console.timeEnd("DateParsing");

// ?控 DataGrid 加???
console.time("DataGridLoad");
gridInstance.refresh();
console.timeEnd("DataGridLoad");
```

---

## ?? 安全?查

- [ ] ??是否有 SQL 注入可能
- [ ] ??是否有 XSS 漏洞
- [ ] ??用??限?查
- [ ] ??日期?界值（极端日期）

---

## ?? 相?文件

| 文件 | 用途 |
|------|------|
| EquipmentView.cshtml | 第一??? |
| EquipmentContactView.cshtml | 第二??? |
| EquipmentStorLessonsView.cshtml | 第三???（?前文件） |
| EquipmentController.cs | ?据加?控制器 |
| EquipmentStorLessons.cs | 模型? |
| EquipmentContact.cs | 模型? |
| EquipmenSmallGroup.cs | 模型? |

---

## ?? ?取?助

### 查看日志

```
Visual Studio → ?? → ?出 → ??
查看 System.Diagnostics.Debug.WriteLine(...) 的?出
```

### 查看 Network 流量

```
F12 → Network → 刷新?面 → 查看?求和??
```

### 查看 Console ??

```
F12 → Console → 查看所有??和警告
```

---

## ? ??完成

完成上述步?后，?面??能?：

1. ? ?示小?列表
2. ? 展?小??示?系人
3. ? 展??系人?示?程
4. ? 正确?示?程日期
5. ? 支持??功能
6. ? 支持排序功能
7. ? ?任何??或警告

---

**最后更新**: 2024-11-18  
**文?版本**: 1.0  
**??**: ? 完整
