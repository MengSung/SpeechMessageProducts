# IntegrateView 日期更新功能除錯指南

## 修復概要

### 問題描述
當使用者在 IntegrateView 頁面更改小組日期時,系統返回 404 Not Found 錯誤。

### 根本原因
JavaScript 調用的 `UpdateIntegrateDate` action 方法在 `SmallGroupController` 中不存在。

### 修復內容

#### 1. 新增 Controller Action (SmallGroupController.cs)

```csharp
/// <summary>
/// 更新綜合報表日期
/// 當使用者在 IntegrateView 中更改小組日期時調用
/// </summary>
/// <param name="SelectedDate">選擇的日期 (格式: yyyy/M/d)</param>
[HttpGet]
public IActionResult UpdateIntegrateDate(string SelectedDate)
{
    try
    {
        // 解析日期 - 支援多種格式
        if (!DateTime.TryParseExact(SelectedDate, 
            new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
            CultureInfo.InvariantCulture, 
            DateTimeStyles.None, 
            out DateTime selectedDateTime))
        {
            return Json(new { success = false, message = "日期格式錯誤" });
        }

        // 更新選擇的日期
        InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

        // 重新設置 ListManager 以載入新日期的資料
        InMemoryContext.ListManager.SetupListManager(
            InMemoryContext.ListManager.m_Account,
            InMemoryContext.ListManager.m_Password,
            selectedDateTime);

        // 重新載入綜合報表資料
        string activeListId = InMemoryContext.ListManager.ActiveListId;
        InMemoryContext.ListManager.SetupIntegrateData(activeListId);

        // 返回新的 ActiveListId
        return Json(new { 
            success = true, 
            ActiveListId = activeListId,
            message = "日期更新成功" 
        });
    }
    catch (Exception e)
    {
        return Json(new { 
            success = false, 
            message = $"日期更新失敗: {e.Message}" 
        });
    }
}
```

#### 2. 添加必要的 using 語句

```csharp
using System.Globalization;  // 用於 CultureInfo 和 DateTimeStyles
```

#### 3. 更新 View JavaScript (IntegrateView.cshtml)

```javascript
@(Html.DevExtreme().DateBoxFor(m => m.SundayPrayers)
    .Type(DateBoxType.Date)
    .Value(Model.SundayPrayers)
    .Width(200)
    .DisplayFormat("yyyy/M/d")
    .OnChange("OnDateboxChange")
    .CalendarOptions(k => k.FirstDayOfWeek(0))
    .OnValueChanged(@<text>
        function(arg) {
            $("#loadPanel").dxLoadPanel("instance").show();
            $.ajax({
                url: '@Url.Action("UpdateIntegrateDate", "SmallGroup")',
                data: { SelectedDate: getODataLocalDateFilter(arg.value) },
                type: 'GET',
                success: function(response) {
                    $("#loadPanel").dxLoadPanel("instance").hide();
                    if (response.success) {
                        window.location.href = "/SmallGroup/IntegrateView/" + response.ActiveListId;
                    } else {
                        ShowToast(response.message || "日期更新失敗", "error", 5000);
                    }
                },
                error: function(xhr, status, error) {
                    $("#loadPanel").dxLoadPanel("instance").hide();
                    ShowToast("日期更新失敗: " + error, "error", 5000);
                }
            });
        }
    </text>)
)
```

## 除錯步驟

### 1. 檢查 Controller Action 是否存在

使用以下命令檢查:
```powershell
Select-String -Path "ChurchReport\Controllers\SmallGroupController.cs" -Pattern "UpdateIntegrateDate"
```

預期輸出應包含 action 方法定義。

### 2. 檢查路由配置

確認路由正確映射:
- URL: `/SmallGroup/UpdateIntegrateDate`
- HTTP Method: `GET`
- Controller: `SmallGroupController`
- Action: `UpdateIntegrateDate`

### 3. 測試 API 端點

使用瀏覽器開發者工具或 Postman 測試:

**請求:**
```
GET /SmallGroup/UpdateIntegrateDate?SelectedDate=2024/1/15
```

**成功響應:**
```json
{
    "success": true,
    "ActiveListId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "message": "日期更新成功"
}
```

**失敗響應 (日期格式錯誤):**
```json
{
    "success": false,
    "message": "日期格式錯誤"
}
```

### 4. 瀏覽器除錯

#### 開啟瀏覽器開發者工具 (F12)

**檢查 Network 標籤:**
1. 選擇日期
2. 查看 Network 請求
3. 確認請求 URL 正確
4. 檢查響應狀態碼 (應該是 200 OK)
5. 檢查響應內容

**檢查 Console 標籤:**
1. 查看是否有 JavaScript 錯誤
2. 可以添加 console.log 來追蹤:

```javascript
function(arg) {
    console.log("選擇的日期:", arg.value);
    console.log("格式化後:", getODataLocalDateFilter(arg.value));
    
    $("#loadPanel").dxLoadPanel("instance").show();
    $.ajax({
        url: '@Url.Action("UpdateIntegrateDate", "SmallGroup")',
        data: { SelectedDate: getODataLocalDateFilter(arg.value) },
        type: 'GET',
        success: function(response) {
            console.log("API 響應:", response);
            $("#loadPanel").dxLoadPanel("instance").hide();
            if (response.success) {
                console.log("跳轉至:", "/SmallGroup/IntegrateView/" + response.ActiveListId);
                window.location.href = "/SmallGroup/IntegrateView/" + response.ActiveListId;
            } else {
                ShowToast(response.message || "日期更新失敗", "error", 5000);
            }
        },
        error: function(xhr, status, error) {
            console.error("API 錯誤:", xhr, status, error);
            $("#loadPanel").dxLoadPanel("instance").hide();
            ShowToast("日期更新失敗: " + error, "error", 5000);
        }
    });
}
```

### 5. 伺服器端除錯

#### 在 Visual Studio 中設定中斷點

1. 在 `SmallGroupController.cs` 的 `UpdateIntegrateDate` 方法開頭設定中斷點
2. 啟動除錯模式 (F5)
3. 在頁面上更改日期
4. 檢查變數值:
   - `SelectedDate` 參數
   - `selectedDateTime` 解析結果
   - `InMemoryContext.ListManager.ActiveListId`

#### 檢查日誌

查看應用程式日誌檔案:
```
ChurchReport\Logs\Trace.log
```

### 6. 常見問題排查

#### 問題 1: 404 Not Found
**原因:** Action 方法不存在或路由錯誤
**解決方案:**
- 確認 `UpdateIntegrateDate` 方法存在
- 檢查方法有 `[HttpGet]` 屬性
- 重新編譯專案

#### 問題 2: 日期格式錯誤
**原因:** 日期字串格式不符合預期
**解決方案:**
- 檢查 `getODataLocalDateFilter` 函數輸出
- 確認支援的日期格式: `yyyy/M/d`, `yyyy/MM/dd`, `yyyy-MM-dd`
- 在 Controller 添加日誌記錄接收到的日期字串

#### 問題 3: InMemoryContext 為 null
**原因:** Session 過期或資料未初始化
**解決方案:**
- 檢查使用者是否已登入
- 確認 `InMemoryContext.ListManager` 已初始化
- 添加 null 檢查

#### 問題 4: 頁面跳轉後資料未更新
**原因:** 資料載入邏輯問題
**解決方案:**
- 確認 `SetupIntegrateData` 正確執行
- 檢查 `LoadFlag` 狀態
- 驗證 `ActiveListId` 是否正確

### 7. 效能檢測

使用瀏覽器開發者工具的 Performance 標籤:
1. 開始錄製
2. 更改日期
3. 停止錄製
4. 分析時間線:
   - AJAX 請求時間
   - 頁面跳轉時間
   - 資料載入時間

### 8. 集成測試

#### 測試案例 1: 正常日期更新
1. 登入系統
2. 進入 IntegrateView
3. 選擇新日期
4. 確認載入面板顯示
5. 確認頁面重新載入
6. 驗證新日期的資料正確顯示

#### 測試案例 2: 相同日期選擇
1. 選擇當前日期
2. 確認系統正常處理

#### 測試案例 3: 無效日期
1. 嘗試輸入無效日期
2. 確認顯示錯誤訊息

## 驗證清單

- [ ] Controller action 存在且編譯通過
- [ ] Using 語句包含 System.Globalization
- [ ] View JavaScript 正確處理響應
- [ ] API 測試成功返回 200 OK
- [ ] 瀏覽器 Console 無錯誤
- [ ] 日期格式正確解析
- [ ] 頁面跳轉正常
- [ ] 新日期資料正確載入
- [ ] 錯誤處理正常工作
- [ ] Toast 通知正確顯示

## 監控與維護

### 添加日誌記錄

在 `UpdateIntegrateDate` 方法中添加:

```csharp
[HttpGet]
public IActionResult UpdateIntegrateDate(string SelectedDate)
{
    try
    {
        // 記錄請求
        Trace.WriteLine($"[UpdateIntegrateDate] 接收日期: {SelectedDate}");
        
        // 解析日期
        if (!DateTime.TryParseExact(SelectedDate, 
            new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
            CultureInfo.InvariantCulture, 
            DateTimeStyles.None, 
            out DateTime selectedDateTime))
        {
            Trace.WriteLine($"[UpdateIntegrateDate] 日期格式錯誤: {SelectedDate}");
            return Json(new { success = false, message = "日期格式錯誤" });
        }

        Trace.WriteLine($"[UpdateIntegrateDate] 解析日期成功: {selectedDateTime}");
        
        // ... 其餘代碼 ...
        
        Trace.WriteLine($"[UpdateIntegrateDate] 更新成功, ActiveListId: {activeListId}");
        
        return Json(new { 
            success = true, 
            ActiveListId = activeListId,
            message = "日期更新成功" 
        });
    }
    catch (Exception e)
    {
        Trace.WriteLine($"[UpdateIntegrateDate] 錯誤: {e.Message}");
        Trace.WriteLine($"[UpdateIntegrateDate] StackTrace: {e.StackTrace}");
        
        return Json(new { 
            success = false, 
            message = $"日期更新失敗: {e.Message}" 
        });
    }
}
```

### 定期檢查

1. 每週檢查錯誤日誌
2. 監控 API 響應時間
3. 收集使用者反饋
4. 定期更新測試案例

## 相關檔案

- Controller: `ChurchReport\Controllers\SmallGroupController.cs`
- View: `ChurchReport\Views\Home\IntegrateView.cshtml`
- 日誌: `ChurchReport\Logs\Trace.log`

## 技術支援

如遇到問題,請提供:
1. 瀏覽器 Console 截圖
2. Network 請求/響應截圖
3. 伺服器端錯誤日誌
4. 重現步驟

---
最後更新: 2024/01/15
版本: 1.0
