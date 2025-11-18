# EquipmentStorLessonsView 除?快速?考卡

## ?? 快速?考

### 三??据?构

```
EquipmentView (小?)
    ↓ id = SmallGroupListEntityId
EquipmentContactView (成?)  
    ↓ id = EquipmentContactId (= PresentRecordId)
EquipmentStorLessonsView (?程)
```

### ??字段映射

| ?? | 主? | ?? | ??字段 |
|------|------|------|---------|
| 1 | SmallGroupListEntityId | - | - |
| 2 | EquipmentContactId | SmallGroupListEntityId | - |
| 3 | StorLessonsEntityId | EquipmentContactId | DiscipleLessonsDateTime |

---

## ?? 7 ?核心修复

| # | ?? | 修复 | ?? |
|---|------|------|------|
| 1 | ParsingDate 日期格式有限 | 支持 ISO + YYYY-MM-DD + Date | ? |
| 2 | getODataLocalDateFilter 月份? | 正确使用 getMonth() | ? |
| 3 | CalculateCellValue ?查不完善 | 添加 isNaN + try-catch | ? |
| 4 | OnCellPrepared 未?定 | 添加事件?定 | ? |
| 5 | cell_prepared 缺?? | 添加 null ?查 | ? |
| 6 | OnRowPrepared 不一致 | 改? jQuery 事件 | ? |
| 7 | onInitNewRow ??? | 添加???? | ? |

---

## ?? 快速??

### ?? 1: ?面加?

```
??: https://localhost:5001/Equipment/EquipmentView
?期: ?示小?列表，???
```

### ?? 2: ?据展?

```
操作: 展?小? → 展?成? → 展??程
?期: ?示?程列表，日期正确?示
```

### ?? 3: 日期格式

```
操作: 查看?程行日期列
?期: 格式? yyyy/MM/dd，不?示 1901 年
```

### ?? 4: ??功能

```
操作: ?停?程行
?期: ?示??和?除??
```

---

## ?? 快速??

### ??: ?示 "沒有資料"

**快速?查**:
1. F12 → Network → 展?成? → 查找 LoadEquipmentStorLessons
2. ?查??是否?空?? `[]`
3. ?查 Console 日志是否有??
4. 确? ContactId 不?空

**可能原因**:
- [ ] ContactId ?空
- [ ] CRM ??程??
- [ ] new_disciple_lessons 不存在
- [ ] 日期全被?? (1901 年)

---

### ??: 日期?示?空

**快速?查**:
1. F12 → Network → 查看??中的 DiscipleLessonsDateTime
2. Console ?行: `testDateParsing("2024-11-18T10:30:00")`
3. ?查返回值是否?有效日期

**可能原因**:
- [ ] 日期值是 1901 年
- [ ] 日期解析失?
- [ ] new_disciple_lessons 不存在

---

### ??: ????未?示

**快速?查**:
1. F12 → Console → ?行: `console.log(typeof cell_prepared)`
2. ??出: `function`
3. Console 中?有日志: `[cell_prepared] ?用 ...`

**可能原因**:
- [ ] OnCellPrepared 未?定
- [ ] cell_prepared 有??
- [ ] CSS ?藏了??

---

## ?? 快速??命令

```javascript
// 复制到 F12 Console 中?行

// 刷新?据
refreshEquipmentGrid()

// 展?所有行看?据
expandAllRows()

// ?示所有?据（表格）
showAllData()

// ??日期解析
testDateParsing("2024-11-18")

// ?取?中行
getSelectedRows()
```

---

## ?? ?据流?查

### 后端?查清?

- [ ] EquipmentController.LoadEquipmentStorLessons 有返回?据
- [ ] Console 中有 `[LoadEquipmentStorLessons]` 日志
- [ ] ContactId 不?空
- [ ] RetrieveStorLessonsByFetchXml 返回有效?据
- [ ] 日期不是 1901 年

### 前端?查清?

- [ ] ParsingDate 函?返回正确?型
- [ ] CalculateCellValue ???效日期
- [ ] DevExtreme 正确格式化日期
- [ ] ????正确?示
- [ ] 排序功能正常工作

---

## ?? 修复??

### ??看到的

? 小?列表正常?示  
? 可展?小?行查看成?  
? 可展?成?行查看?程  
? ?程日期格式? yyyy/MM/dd  
? ?停?示??/?除??  
? 排序功能正常工作  
? Console ???  

### 不??看到的

? "沒有資料" ??警告  
? 日期?空或?示? 1901/1/1  
? ????未?示  
? Console 中的??信息  
? Network 404 或 500 ??  

---

## ?? 文??航

| 需求 | 文? |
|------|------|
| ??修复?明 | EquipmentStorLessonsView日期解析除錯修復報告.md |
| ???? | EquipmentStorLessonsView全面除錯診斷報告.md |
| 逐步?? | EquipmentStorLessonsView除錯完整指南.md |
| ??工具 | EquipmentStorLessonsView快速調試工具.js |
| 修复?? | EquipmentStorLessonsView除錯完成總結.md |

---

## ?? 快速?始

### Step 1: ???用
```bash
dotnet run
```

### Step 2: 打??面
```
https://localhost:5001/Equipment/EquipmentView
```

### Step 3: 打???工具
```
F12 → Console ??
```

### Step 4: 加???命令
```javascript
// 复制 EquipmentStorLessonsView快速調試工具.js ?容粘?
```

### Step 5: ??
```javascript
// ?行
refreshEquipmentGrid()
expandAllRows()
showAllData()
```

---

## ?? 成功?志

完成修复后，??能看到：

```
? ?面加?成功
? 小?列表?示
? 展?小?看到成?
? 展?成?看到?程
? ?程?示正确日期 (yyyy/MM/dd 格式)
? ??功能可用
? 排序功能可用
? Console ???
? Network 全部 200 OK
```

---

## ?? ?取支持

1. **查看日志**: VS → ?出窗口 → ??
2. **网?流量**: F12 → Network
3. **控制台??**: F12 → Console
4. **查?文?**: 上面的文??航表

---

## 版本信息

- **文件**: EquipmentStorLessonsView.cshtml
- **修复版本**: 1.0
- **完成日期**: 2024-11-18
- **????**: ? 成功
- **文???**: ? 完整

---

**遇到??？** 按照上面的快速??步??行排查，或查?相?的??文?。
