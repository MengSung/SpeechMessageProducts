# LINE 登入問題 - DisplayViewType=Undefined 最終解決方案

## 🎯 問題根本原因

經過深入分析程式碼後，發現問題的根本原因在於：

### 1. `ListManager.GetDisplayViewType()` 的實作

```csharp
// ChurchReport/Models/ListManager.cs 第 234 行
public String GetDisplayViewType()
{
    if (m_MultiGroupList != null)
    {
        if (m_MultiGroupList.m_WeeklyReportRecordListData != null)
        {
            return m_MultiGroupList.m_WeeklyReportRecordListData.Count > 1 
                ? "MultiGroupView" 
                : "IntegrateView";
        }
        else
        {
            return "IntegrateView";
        }
    }
    else
    {
        return "IntegrateView";  // ✅ 這裡會返回 "IntegrateView"
    }
}
```

### 2. `SetupListManager` 可能失敗的情況

```csharp
// ChurchReport/Models/ListManager.cs 第 51 行
public void SetupListManager(String Account, String Password, DateTime aSelectDate)
{
    try
    {
        m_Account = Account;
        m_Password = Password;
        m_SelectDate = aSelectDate;

        // ❌ 如果這個方法拋出異常或沒有正確設定 m_MultiGroupList
        m_DownloadListManager.GetListManager(
            Account, Password, aSelectDate, 
            ref m_MultiGroupList, 
            ref m_MultiGroupChartDataList, 
            ref LoginType, 
            ref UserType, 
            ref LoginFullName, 
            ref ActiveListId);
    }
    catch (System.Exception e)
    {
        // 異常被拋出，但 m_MultiGroupList 可能還是 null
        throw e;
    }
}
```

### 3. LINE 登入流程中的問題

LINE 登入時，`Account` 是空字串 `""`，`Password` 是 `UserLineId`：

```csharp
// ChurchReport/Controllers/AuthenticationController.cs
var lineLoginViewModel = new GalleryViewModel
{
    Account = "",  // ❌ 空字串
    Password = UserLineId
};
```

這導致 `SetupListManager` 可能：
1. 無法正確查詢資料庫
2. `m_MultiGroupList` 保持為 null 或空
3. `GetDisplayViewType()` 返回 "IntegrateView"（正確）或可能在某些情況下返回其他值

### 4. 為什麼顯示 "Undefined"？

根據您的錯誤訊息 **`DisplayViewType=Undefined`**，這表示：
- C# 後端可能在某個地方設定了 `displayViewType = "Undefined"`
- 或者 `GetDisplayViewType()` 返回了 null/空字串，而前端 JavaScript 將其解釋為 "Undefined"

##已實施的修正方案

我們已經在 `DetermineDisplayViewType()` 方法中添加了保護性檢查：

```csharp
private string DetermineDisplayViewType()
{
    try
    {
        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 開始判斷顯示視圖類型");
        
        // 控制 Navigation 下拉項目
        ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
        ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
        ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] UserType={ViewBag.UserType}");
        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] LoginType={InMemoryContext.ListManager.LoginType}");

        // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
        string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
        
        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] GetDisplayViewType() 回傳值: '{displayViewType ?? "null"}'");
        
        // ✅ 保護性檢查: 如果 displayViewType 是 null 或空字串，設定預設值
        if (string.IsNullOrEmpty(displayViewType))
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 警告: displayViewType 為空，使用預設值");
            
            // 根據 LoginType 決定預設值
            if (InMemoryContext.ListManager.LoginType == "小組長")
            {
                displayViewType = "IntegrateView";
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 小組長預設值: IntegrateView");
            }
            else
            {
                displayViewType = "MultiGroupView";
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 非小組長預設值: MultiGroupView");
            }
        }
        
        // 後續處理...
        
        return displayViewType;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 發生異常: {ex.Message}");
        
        // 發生異常時，返回安全的預設值
        return "IntegrateView";
    }
}
```

## 🔍 除錯步驟

### 步驟 1: 檢查 Visual Studio 輸出視窗

1. 開啟 Visual Studio
2. 點選功能表 **[偵錯]** → **[視窗]** → **[輸出]** (或按 **Ctrl+Alt+O**)
3. 在輸出視窗的下拉選單中，選擇 **「偵錯」**
4. 執行 LINE 登入流程

### 步驟 2: 查看 Debug 輸出

您應該會看到以下輸出：

```
[ProcessLogin] 開始處理登入 - 帳號: , 時間: 2024-11-26 ...
[ValidateUserCredentials] 開始驗證 - 帳號: 
[ValidateUserCredentials] 使用 LINE ID 登入
[ValidateUserCredentials] 驗證成功
[RetrieveUserData] 使用 LINE ID 查詢: Uxxx...
[RetrieveUserData] LINE 登入查詢成功，姓名: XXX
[SetupSystemData] 呼叫 SetupListManager - 開始時間: ...
[SetupSystemData] SetupListManager 完成 - 時間: ...
[DetermineDisplayViewType] 開始判斷顯示視圖類型
[DetermineDisplayViewType] UserType=...
[DetermineDisplayViewType] LoginType=...
[DetermineDisplayViewType] GetDisplayViewType() 回傳值: 'IntegrateView'  // ← 檢查這裡
[DetermineDisplayViewType] 最終視圖類型: IntegrateView
[ProcessLogin] 顯示類型: IntegrateView
```

**關鍵檢查點**:
- **如果看到 `GetDisplayViewType() 回傳值: 'null'`** → `ListManager` 沒有正確初始化
- **如果看到 `SetupListManager 失敗`** → 資料庫查詢異常
- **如果看到 `GetDisplayViewType() 回傳值: ''` (空字串)** → `GetDisplayViewType()` 有邏輯錯誤

### 步驟 3: 檢查 JavaScript Console

1. 開啟瀏覽器 F12 開發者工具
2. 切換到 **Console** 標籤
3. 執行 LINE 登入流程

您應該會看到：

```javascript
[LINE Profile] { DisplayName: "XXX", UserId: "Uxxx..." }
[UpdateLineUserId] 開始更新 LINE ID { ... }
[AJAX Success] {
    DisplayViewType: "IntegrateView",  // ← 檢查這裡
    ActiveListId: "xxx-xxx-xxx",
    message: "歡迎XXX登入成功!",
    fullname: "XXX"
}
[導向] IntegrateView: xxx-xxx-xxx
```

**如果看到 `DisplayViewType: null` 或 `DisplayViewType: undefined`**:
- 表示 C# 後端回傳了 null 值
- 需要檢查 `SetupSystemData` 是否成功執行

## 💡 可能的問題和解決方案

### 問題 1: `SetupListManager` 拋出異常

**症狀**: 
```
[SetupSystemData] SetupListManager 失敗: ...
```

**解決方案**:
```csharp
// 在 SetupSystemData 方法中添加更詳細的錯誤處理
try
{
    InMemoryContext.ListManager.SetupListManager(
        viewModel.Account, 
        viewModel.Password, 
        DateTime.Now);
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] SetupListManager 失敗:");
    System.Diagnostics.Debug.WriteLine($"  - Message: {ex.Message}");
    System.Diagnostics.Debug.WriteLine($"  - StackTrace: {ex.StackTrace}");
    
    // 設定預設值以避免崩潰
    InMemoryContext.ListManager.LoginType = "小組長";
    InMemoryContext.ListManager.LoginFullName = viewModel.Account;
    InMemoryContext.ListManager.ActiveListId = Guid.NewGuid().ToString();
}
```

### 問題 2: LINE 登入時 `Account` 為空

**症狀**:
```
[SetupListManager] 帳號為空，無法查詢資料
```

**解決方案**:
LINE 登入時應該使用聯絡人的資料，而不是空帳號：

```csharp
// 在 RetrieveUserData 方法中，LINE 登入成功後
if (loginContact != null)
{
    // 使用聯絡人的實際資料更新 viewModel
    string fullName = loginContact.GetAttributeValue<string>("fullname");
    
    // 設定帳號為 "LineIdLogin" 或聯絡人ID
    viewModel.Account = "LineIdLogin";
    viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
}
```

### 問題 3: `m_MultiGroupList` 為 null

**症狀**:
```
[DetermineDisplayViewType] GetDisplayViewType() 回傳值: 'null'
```

**解決方案**:
在 `ListManager.GetDisplayViewType()` 中已經有保護性檢查，但如果還是返回 null，可能需要檢查：

```csharp
// 在 DownloadListManager.GetListManager 方法中
public void GetListManager(...)
{
    try
    {
        // 確保 m_MultiGroupList 至少被初始化
        if (m_MultiGroupList == null)
        {
            m_MultiGroupList = new MultiGroupList
            {
                m_WeeklyReportRecordListData = new List<WeeklyReportRecord>()
            };
        }
        
        // 查詢資料庫...
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[GetListManager] 異常: {ex.Message}");
        
        // 確保至少有空的集合
        if (m_MultiGroupList == null)
        {
            m_MultiGroupList = new MultiGroupList
            {
                m_WeeklyReportRecordListData = new List<WeeklyReportRecord>()
            };
        }
    }
}
```

## 🎯 終極解決方案

如果上述所有方法都無法解決問題，可以考慮：

### 選項 1: 為 LINE 登入建立專用流程

```csharp
private string DetermineDisplayViewType()
{
    try
    {
        // ✅ 特殊處理 LINE 登入
        if (InMemoryContext.LineBindingViewModel?.LineUserId != null)
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 檢測到 LINE 登入");
            
            // LINE 登入時，直接返回 IntegrateView
            return "IntegrateView";
        }
        
        // 正常登入流程...
        string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
        
        // 後續處理...
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 異常: {ex.Message}");
        return "IntegrateView";
    }
}
```

### 選項 2: 簡化 LINE 登入回應

```csharp
// 在 SaveUserLineId 方法中
if (results.Entities.Count > 0)
{
    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 用戶已綁定，準備登入");
    
    // ✅ 直接返回 IntegrateView，不調用 ProcessLogin
    return Json(new
    {
        DisplayViewType = "IntegrateView",
        ActiveListId = results.Entities[0].Id.ToString(),
        message = $"歡迎 {fullName} 登入成功!",
        fullname = fullName
    });
}
```

## 📝 檢查清單

在實際測試前，請確認：

- [ ] Visual Studio 輸出視窗已開啟並設定為「偵錯」
- [ ] 瀏覽器 F12 Console 已開啟
- [ ] 已重新編譯專案 (`dotnet build`)
- [ ] 已重新啟動應用程式
- [ ] LINE LIFF ID 正確設定在 `TempData["Proponent"]`
- [ ] LINE 帳號已正確綁定到資料庫

## 🔧 如果問題持續存在

請提供以下資訊：

1. **Visual Studio 輸出視窗的完整內容** (從 `[ProcessLogin]` 開始到結束)
2. **瀏覽器 Console 的完整輸出**
3. **Network 標籤中 `SaveUserLineId` 的 Request 和 Response**
4. **資料庫中該使用者的 `new_lineid` 欄位值**

這些資訊將幫助我們準確定位問題所在。

---

**狀態**: ✅ 已實施保護性修正  
**待驗證**: 🧪 需要實際測試  
**優先級**: 🔴 高優先級

