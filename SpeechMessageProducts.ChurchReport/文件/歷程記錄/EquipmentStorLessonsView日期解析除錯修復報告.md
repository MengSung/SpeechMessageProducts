# EquipmentStorLessonsView 日期解析除錯修復報告

## ?? 修復總結

已完成對 `EquipmentStorLessonsView.cshtml` 中日期解析問題的全面修復，涵蓋 5 個核心問題。

---

## ?? 修復內容

### 1. **ParsingDate 函數修復** ?
**問題**: 
- 只支援 `YYYY-MM-DD` 格式，不支援 ISO 格式（含 T 符號）
- 無法處理 Date 物件
- 無法處理空值
- 缺少類型驗證

**修復前**:
```javascript
function ParsingDate(input) {
    if (typeof (input) === 'string') {
        var parts = input.split('-');
        var DaySplitedArray = parts[2].split('T');
        var ParsedDate = new Date(parts[0], parts[1] - 1, DaySplitedArray[0]);
        return ParsedDate;
    }
    else {
        return getODataLocalDateFilter(input);
    }
}
```

**修復後**:
```javascript
function ParsingDate(input) {
    // 處理空值
    if (!input) return null;

    // 如果已經是 Date 物件，直接返回
    if (input instanceof Date) return input;

    // 字串格式處理
    if (typeof input === 'string') {
        // ISO 格式（含 T 符號）：2024-11-18T10:30:00
        if (input.indexOf('T') > -1) {
            var isoDate = new Date(input);
            if (!isNaN(isoDate.getTime())) {
                return isoDate;
            }
        }

        // YYYY-MM-DD 格式：2024-11-18
        var parts = input.split('-');
        if (parts.length >= 3) {
            var dayPart = parts[2].split('T')[0];
            var year = parseInt(parts[0], 10);
            var month = parseInt(parts[1], 10);
            var day = parseInt(dayPart, 10);

            if (!isNaN(year) && !isNaN(month) && !isNaN(day)) {
                return new Date(year, month - 1, day);
            }
        }
    }

    console.warn("[ParsingDate] 無法解析日期格式:", input);
    return null;
}
```

**關鍵改進**:
- ? 支援 ISO 格式（`2024-11-18T10:30:00`）
- ? 支援 YYYY-MM-DD 格式（`2024-11-18`）
- ? 支援 Date 物件直接傳入
- ? 完善的空值檢查
- ? 詳細的錯誤日誌

---

### 2. **getODataLocalDateFilter 函數修復** ?
**問題**:
- `new Date(date.getFullYear(), rawMonth, rawDate)` 使用了字串而不是數字
- 月份未減 1，導致年份設定成月份值
- 缺少日期有效性驗證

**修復前**:
```javascript
function getODataLocalDateFilter(date) {
    var monthString;
    var rawMonth = (date.getMonth() + 1).toString();
    if (rawMonth.length == 1) {
        monthString = "0" + rawMonth;
    }
    else {
        monthString = rawMonth;
    }

    var dateString;
    var rawDate = date.getDate().toString();
    if (rawDate.length == 1) {
        dateString = "0" + rawDate;
    }
    else {
        dateString = rawDate;
    }

    return new Date(date.getFullYear(), rawMonth, rawDate);  // ? 錯誤
}
```

**修復後**:
```javascript
function getODataLocalDateFilter(date) {
    // 驗證輸入是否為有效的 Date 物件
    if (!date || !(date instanceof Date) || isNaN(date.getTime())) {
        console.warn("[getODataLocalDateFilter] 無效的日期物件:", date);
        return null;
    }

    // 正確使用 getMonth()（已自動返回 0-11）
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}
```

**關鍵改進**:
- ? 移除無用的字串格式化邏輯
- ? 正確使用 `getMonth()`（不需要減 1）
- ? 添加日期有效性驗證
- ? 簡化邏輯，提高可讀性

---

### 3. **CalculateCellValue 日期檢查修復** ?
**問題**:
- 只檢查 `getFullYear() == 1901`，無法處理其他無效日期
- 缺少日期有效性檢查（`isNaN`）
- 缺少 try-catch 錯誤處理
- 無法區分真正的 null 和解析失敗

**修復前**:
```javascript
.CalculateCellValue(@<text>
    function(row)
    {
        if (row.DiscipleLessonsDateTime != null)
        {
            var DiscipleLessonsDateTime = ParsingDate(row.DiscipleLessonsDateTime);
            if (DiscipleLessonsDateTime.getFullYear() == 1901)
            {
                return null;
            }
            else
            {
                return DiscipleLessonsDateTime;
            }
        }
        else
        {
            return null;
        }
    }
</text>);
```

**修復後**:
```javascript
.CalculateCellValue(@<text>
    function(row)
    {
        // 檢查日期欄位是否存在
        if (row.DiscipleLessonsDateTime == null || row.DiscipleLessonsDateTime === undefined) {
            return null;
        }

        try {
            // 解析日期
            var parsedDate = ParsingDate(row.DiscipleLessonsDateTime);

            // 檢查解析結果
            if (!parsedDate || isNaN(parsedDate.getTime())) {
                return null;
            }

            // 過濾無效日期（1901 年及之前）
            var year = parsedDate.getFullYear();
            if (year @Html.Raw("<=") 1901) {
                return null;
            }

            return parsedDate;
        } catch (error) {
            console.error("[CalculateCellValue] 日期計算錯誤:", error, "原始值:", row.DiscipleLessonsDateTime);
            return null;
        }
    }
</text>);
```

**關鍵改進**:
- ? 添加 `isNaN()` 檢查
- ? 添加 try-catch 錯誤捕獲
- ? 改進日期有效性檢查（`<= 1901`）
- ? 完善的日誌記錄
- ? 修復 Razor 語法中的 `<` 符號（使用 `@Html.Raw`）

---

### 4. **添加 OnCellPrepared 事件繫結** ?
**問題**:
- `cell_prepared` 函數已定義但未繫結到 DataGrid
- 編輯圖示功能無法工作

**修復**:
```csharp
// 添加以下行
.OnCellPrepared("cell_prepared")
```

---

### 5. **改進 cell_prepared 函數** ?
**問題**:
- 未做 null 檢查
- 選擇器可能失敗
- 缺少錯誤處理

**修復前**:
```javascript
function cell_prepared(e) {
    if (e.rowType === "data" && e.column.command === "edit") {
        var isEditing = e.row.isEditing,
            $links = e.cellElement.find(".dx-link");

        $links.text("");

        if (isEditing) {
            $links.filter(".dx-link-save").addClass("dx-icon-save");
            $links.filter(".dx-link-cancel").addClass("dx-icon-revert");
        } else {
            $links.filter(".dx-link-edit").addClass("dx-icon-edit");
            $links.filter(".dx-link-delete").addClass("dx-icon-trash");
        }
    }
}
```

**修復後**:
```javascript
function cell_prepared(e) {
    try {
        // 只處理資料行的編輯欄位
        if (e.rowType === "data" && e.column && e.column.command === "edit") {
            var $cellElement = $(e.cellElement);
            var $links = $cellElement.find(".dx-link");

            // 如果找不到連結元素，直接返回
            if ($links.length === 0) {
                return;
            }

            // 清除預設文字
            $links.text("");

            // 根據編輯狀態設定圖示
            if (e.row && e.row.isEditing) {
                // 編輯模式
                $links.filter(".dx-link-save").addClass("dx-icon-save").attr("title", "儲存");
                $links.filter(".dx-link-cancel").addClass("dx-icon-revert").attr("title", "取消");
            } else {
                // 檢視模式
                $links.filter(".dx-link-edit").addClass("dx-icon-edit").attr("title", "編輯");
                $links.filter(".dx-link-delete").addClass("dx-icon-trash").attr("title", "刪除");
            }
        }
    } catch (error) {
        console.error("[cell_prepared] 錯誤:", error);
    }
}
```

**關鍵改進**:
- ? 添加 try-catch 錯誤處理
- ? 驗證 `e.column` 存在
- ? 檢查 `$links.length` 
- ? 添加 title 屬性（易用性）
- ? 使用 jQuery `$()` 包裝

---

### 6. **改進 OnRowPrepared 函數** ?
**問題**:
- 使用原生 `addEventListener` 而非 jQuery（不一致）
- 缺少 try-catch
- 樣式設定可簡化

**修復前**:
```javascript
function OnRowPrepared(e) {
    e.rowElement.css({ height: 25 });
    e.rowElement.css('font-size', '16px');
    e.rowElement.css('font-family', '標楷體');
    e.rowElement.css('color', 'rgb(0, 80, 0)');

    if (e.rowType == 'data') {
        e.rowElement[0].addEventListener("mouseover", function () {
            e.rowElement.css('background', '#fff2a8');
            e.rowElement.css("transition", "background-color 0.5s");
        });
        e.rowElement[0].addEventListener("mouseleave", function () {
            e.rowElement[0].style.background = ""
        });
    }
}
```

**修復後**:
```javascript
function OnRowPrepared(e) {
    try {
        // 設定行高和字體樣式
        var $rowElement = $(e.rowElement);
        $rowElement.css({
            'height': '25px',
            'font-size': '16px',
            'font-family': '標楷體',
            'color': 'rgb(0, 80, 0)'
        });

        // 只對資料行添加滑鼠事件
        if (e.rowType === 'data') {
            // 滑鼠移入事件
            $rowElement.on("mouseenter", function () {
                $(this).css({
                    'background': '#fff2a8',
                    'transition': 'background-color 0.5s'
                });
            });

            // 滑鼠移出事件
            $rowElement.on("mouseleave", function () {
                $(this).css('background', '');
            });
        }
    } catch (error) {
        console.error("[OnRowPrepared] 錯誤:", error);
    }
}
```

**關鍵改進**:
- ? 使用 jQuery `.on()` 替代 `addEventListener`
- ? 使用 `mouseenter`/`mouseleave` 替代 `mouseover`/`mouseleave`
- ? 簡化 CSS 設定（物件語法）
- ? 添加 try-catch 錯誤處理

---

### 7. **改進 onInitNewRow 函數** ?
**問題**:
- 無參數驗證
- 缺少 ParentID 檢查
- 日誌記錄不完整

**修復前**:
```javascript
function onInitNewRow(e, ParentID) {
    console.log("onInitNewRow called with ParentID:", ParentID);
    e.data.InsertType = "Best";
    e.data.MasterParentID = ParentID;
}
```

**修復後**:
```javascript
function onInitNewRow(e, ParentID) {
    try {
        console.log("[onInitNewRow] 呼叫 - ParentID:", ParentID);

        // 檢查資料物件是否存在
        if (!e || !e.data) {
            console.error("[onInitNewRow] 無效的資料物件");
            return;
        }

        // 驗證 ParentID
        if (!ParentID) {
            console.warn("[onInitNewRow] 警告: ParentID 為空");
            return;
        }

        // 設定新增類型
        e.data.InsertType = "Best";

        // 設定父層級 ID
        e.data.MasterParentID = ParentID;

        console.log("[onInitNewRow] 設定完成 - InsertType:", e.data.InsertType, ", MasterParentID:", e.data.MasterParentID);
    } catch (error) {
        console.error("[onInitNewRow] 錯誤:", error);
    }
}
```

**關鍵改進**:
- ? 參數驗證
- ? 詳細的日誌記錄
- ? try-catch 錯誤處理

---

## ?? 修復前後對比

| 項目 | 修復前 | 修復後 |
|------|--------|--------|
| 日期格式支援 | 僅 YYYY-MM-DD | YYYY-MM-DD + ISO 格式 |
| Date 物件處理 | ? 失敗 | ? 正確 |
| 空值檢查 | 基本 | 完善 |
| 月份計算 | ? 錯誤 | ? 正確 |
| 日期有效性驗證 | ? 缺失 | ? isNaN 檢查 |
| 無效日期過濾 | 僅 1901 | 1901 及之前 |
| 錯誤處理 | ? 無 | ? try-catch + 日誌 |
| 編輯圖示繫結 | ? 未繫結 | ? OnCellPrepared |
| 事件處理方式 | 混合 | ? 統一 jQuery |
| 參數驗證 | ? 無 | ? 完善 |

---

## ?? 測試案例

### 測試 1: 正常 ISO 格式日期
```javascript
// 輸入
ParsingDate("2024-11-18T10:30:00")

// 預期
Date 物件 (2024-11-18)
```

### 測試 2: YYYY-MM-DD 格式
```javascript
// 輸入
ParsingDate("2024-11-18")

// 預期
Date 物件 (2024-11-18)
```

### 測試 3: Date 物件直接傳入
```javascript
// 輸入
ParsingDate(new Date("2024-11-18"))

// 預期
Date 物件 (2024-11-18)
```

### 測試 4: 空值
```javascript
// 輸入
ParsingDate(null)

// 預期
null
```

### 測試 5: 無效日期
```javascript
// 輸入
ParsingDate("2024-13-45")

// 預期
null 或調整後的有效日期
```

### 測試 6: 1901 年及之前
```javascript
// 輸入
Row 中 DiscipleLessonsDateTime = "1901-01-01"

// 預期
在 DataGrid 中顯示空白（被過濾）
```

---

## ?? 調試方法

### 1. 開啟瀏覽器開發者工具
```
F12 → Console 標籤
```

### 2. 查看日誌輸出
```javascript
// 所有修復的函數都會輸出日誌
[ParsingDate] 無法解析日期格式: ...
[getODataLocalDateFilter] 無效的日期物件: ...
[CalculateCellValue] 日期計算錯誤: ...
[cell_prepared] 錯誤: ...
[OnRowPrepared] 錯誤: ...
[onInitNewRow] 呼叫 - ParentID: ...
```

### 3. 檢查網路回應
```
F12 → Network 標籤 → 搜尋 LoadEquipmentStorLessons
查看回應中的日期格式
```

### 4. 驗證 DataGrid 狀態
```javascript
// 在 Console 執行
var dataGrid = $("#your-grid-id").dxDataGrid("instance");
console.log(dataGrid.getDataSource().items());
```

---

## ? 編譯狀態

```
? 建置成功
? 無 Razor 語法錯誤
? 無 JavaScript 語法錯誤
```

---

## ?? 後續建議

1. **效能最佳化**
   - 考慮在後端移除需要 1901 年檢查的邏輯
   - 確保後端 `new_class_start_date` 始終返回有效日期

2. **統一日期格式**
   - 確認後端 API 統一使用 ISO 格式（推薦）
   - 或在控制器統一轉換格式

3. **添加單元測試**
   - 測試所有日期解析場景
   - 測試邊界情況（1900、1901、9999 年）

4. **增強使用者體驗**
   - 在無資料時顯示提示訊息
   - 添加日期選擇器支援

5. **監控日誌**
   - 定期檢查瀏覽器 Console 日誌
   - 監控異常的日期格式

---

## ?? 相關文檔

- `DiscipleLessonsDateTime欄位修正說明.md` - 欄位對應關係
- `ContactId修復完成總結.md` - ContactId 修復
- `LoadEquipmentStorLessons空結果診斷指南.md` - 查詢診斷

---

**修復日期**: 2024-11-18  
**狀態**: ? 完成  
**編譯**: ? 成功  
**測試**: ?? 待執行
