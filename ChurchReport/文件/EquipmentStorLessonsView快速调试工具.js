// ============================================================================
// EquipmentStorLessonsView 快速??工具
// ============================================================================
// 在??器 Console (F12) 中复制粘?以下代??行??

// ============================================================================
// 1. 基本信息?查
// ============================================================================

console.log("=== 基本信息?查 ===");

// ?查 jQuery 是否加?
console.log("jQuery 版本:", typeof jQuery !== 'undefined' ? jQuery.fn.jquery : '未加?');

// ?查 DevExtreme 是否加?
console.log("DevExtreme:", typeof DevExpress !== 'undefined' ? '已加?' : '未加?');

// ?取 DataGrid 容器
var $gridContainer = $("#data-grid");
console.log("DataGrid 容器是否存在:", $gridContainer.length > 0);

// ?取 DataGrid ?例
var gridInstance = null;
try {
    gridInstance = $gridContainer.find(".dx-datagrid").dxDataGrid("instance");
    console.log("DataGrid ?例:", gridInstance ? "? 已?取" : "? 未找到");
} catch (e) {
    console.error("?取 DataGrid ?例失?:", e.message);
}

// ============================================================================
// 2. 函?存在性?查
// ============================================================================

console.log("\n=== 函?存在性?查 ===");

var functions = [
    'ParsingDate',
    'getODataLocalDateFilter',
    'cell_prepared',
    'onInitNewRow',
    'OnRowPrepared',
    'moveEditColumnToLeft'
];

functions.forEach(func => {
    var exists = typeof window[func] === 'function';
    console.log(`${func}: ${exists ? '? 存在' : '? 缺失'}`);
});

// ============================================================================
// 3. 日期解析??
// ============================================================================

console.log("\n=== 日期解析?? ===");

if (typeof ParsingDate === 'function') {
    var testCases = [
        { input: "2024-11-18T10:30:00", name: "ISO 格式" },
        { input: "2024-11-18", name: "YYYY-MM-DD 格式" },
        { input: new Date("2024-11-18"), name: "Date ?象" },
        { input: null, name: "空值" },
        { input: "2024/11/18", name: "?效格式" },
        { input: "1901-01-01", name: "1901 年日期" },
        { input: "", name: "空字符串" }
    ];

    testCases.forEach(test => {
        try {
            var result = ParsingDate(test.input);
            var display = result ? result.toLocaleDateString('zh-TW') : 'null';
            console.log(`${test.name}: ${display}`);
        } catch (e) {
            console.error(`${test.name}: ?? -`, e.message);
        }
    });
} else {
    console.warn("ParsingDate 函?不存在");
}

// ============================================================================
// 4. getODataLocalDateFilter ??
// ============================================================================

console.log("\n=== getODataLocalDateFilter ?? ===");

if (typeof getODataLocalDateFilter === 'function') {
    var testDate = new Date(2024, 10, 18); // 2024-11-18 (月份 0-11)
    var result = getODataLocalDateFilter(testDate);
    console.log("?入:", testDate.toLocaleDateString('zh-TW'));
    console.log("?出:", result ? result.toLocaleDateString('zh-TW') : 'null');
} else {
    console.warn("getODataLocalDateFilter 函?不存在");
}

// ============================================================================
// 5. DataGrid ?据?查
// ============================================================================

console.log("\n=== DataGrid ?据?查 ===");

if (gridInstance) {
    try {
        var dataSource = gridInstance.getDataSource();
        if (dataSource) {
            dataSource.load().done(function(data) {
                console.log("?据行?:", data.length);
                if (data.length > 0) {
                    console.log("第一行?据:", data[0]);
                    
                    // ?查日期字段
                    var firstDate = data[0].DiscipleLessonsDateTime;
                    console.log("日期字段值:", firstDate);
                    console.log("日期字段?型:", typeof firstDate);
                    
                    // ??解析
                    if (typeof ParsingDate === 'function' && firstDate) {
                        var parsed = ParsingDate(firstDate);
                        console.log("解析?果:", parsed ? parsed.toLocaleDateString('zh-TW') : 'null');
                    }
                }
            });
        }
    } catch (e) {
        console.error("?查?据失?:", e.message);
    }
} else {
    console.warn("?法?取 DataGrid ?例");
}

// ============================================================================
// 6. ??功能?查
// ============================================================================

console.log("\n=== ??功能?查 ===");

if (gridInstance) {
    try {
        var option = gridInstance.columnOption("command:edit");
        console.log("??列配置:", option);
    } catch (e) {
        console.warn("?法?取??列配置:", e.message);
    }
}

// ============================================================================
// 7. 事件?定?查
// ============================================================================

console.log("\n=== 事件?定?查 ===");

// ?查 MasterDetail 是否?用
if (gridInstance) {
    try {
        var detailsOptions = gridInstance.option("masterDetail");
        console.log("MasterDetail 配置:", detailsOptions);
    } catch (e) {
        console.warn("?法?取 MasterDetail 配置:", e.message);
    }
}

// ============================================================================
// 8. 性能?控
// ============================================================================

console.log("\n=== 性能?控 ===");

if (gridInstance) {
    // ?控行准?事件
    var originalRowPreparedHandler = window.OnRowPrepared;
    if (originalRowPreparedHandler) {
        var callCount = 0;
        window.OnRowPrepared = function(e) {
            callCount++;
            return originalRowPreparedHandler.call(this, e);
        };
        
        console.log("行准?事件已?始?控");
        
        // 1秒后?出??
        setTimeout(() => {
            console.log(`行准?事件被?用 ${callCount} 次`);
        }, 1000);
    }
}

// ============================================================================
// 9. 网??求?控
// ============================================================================

console.log("\n=== 网??求?控 ===");
console.log("打? Network ??查看以下?求:");
console.log("- LoadEquipmentStorLessons (??被?用多次，每??系人一次)");
console.log("- ?查??体中的日期格式");

// ============================================================================
// 10. ????函?
// ============================================================================

console.log("\n=== ????函? ===");

// 刷新 DataGrid ?据
window.refreshEquipmentGrid = function() {
    if (gridInstance) {
        gridInstance.refresh();
        console.log("已刷新 DataGrid");
    } else {
        console.warn("DataGrid ?例不存在");
    }
};
console.log("刷新: refreshEquipmentGrid()");

// 展?所有行
window.expandAllRows = function() {
    if (gridInstance) {
        gridInstance.expandAll(-1);
        console.log("已展?所有行");
    } else {
        console.warn("DataGrid ?例不存在");
    }
};
console.log("展?所有: expandAllRows()");

// 折?所有行
window.collapseAllRows = function() {
    if (gridInstance) {
        gridInstance.collapseAll(-1);
        console.log("已折?所有行");
    } else {
        console.warn("DataGrid ?例不存在");
    }
};
console.log("折?所有: collapseAllRows()");

// ?取?中行
window.getSelectedRows = function() {
    if (gridInstance) {
        var selected = gridInstance.getSelectedRowKeys();
        console.log("?中的行:", selected);
        return selected;
    } else {
        console.warn("DataGrid ?例不存在");
    }
};
console.log("?取?中: getSelectedRows()");

// ?示所有行?据
window.showAllData = function() {
    if (gridInstance) {
        var dataSource = gridInstance.getDataSource();
        if (dataSource) {
            dataSource.load().done(function(data) {
                console.table(data);
            });
        }
    } else {
        console.warn("DataGrid ?例不存在");
    }
};
console.log("?示所有?据: showAllData()");

// ??日期解析
window.testDateParsing = function(dateString) {
    if (typeof ParsingDate === 'function') {
        var result = ParsingDate(dateString);
        console.log(`ParsingDate("${dateString}") =`, result ? result.toLocaleDateString('zh-TW') : 'null');
        return result;
    } else {
        console.warn("ParsingDate 函?不存在");
    }
};
console.log("??日期: testDateParsing(dateString)");

// ============================================================================
// 11. ??
// ============================================================================

console.log("\n=== ??工具就? ===");
console.log("可用的??函?:");
console.log("- refreshEquipmentGrid()");
console.log("- expandAllRows()");
console.log("- collapseAllRows()");
console.log("- getSelectedRows()");
console.log("- showAllData()");
console.log("- testDateParsing(dateString)");
console.log("\n查看上面的?出?果?行??。");
