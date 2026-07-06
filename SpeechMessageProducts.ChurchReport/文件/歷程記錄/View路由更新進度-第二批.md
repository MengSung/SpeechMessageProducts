# View 路由更新進度 - 第二批 (高優先級)

## ?? 更新日期
開始日期: 2024/12/XX  
當前時間: 進行中

## ?? 本批目標
更新所有??高優先級的 View 檔案 (約 20 個檔案)

## ? 已完成的檔案 (3/20)

| # | 檔案 | 控制器 | 狀態 | 備註 |
|---|------|--------|------|------|
| 1 | `Views/Shared/_Layout.cshtml` | 多個 | ? 完成 | 主版面配置 |
| 2 | `Views/Home/MultiGroupView.cshtml` | SmallGroup | ? 完成 | 多小組回報頁面 |
| 3 | `Views/Home/_GeneralGroupGrids.cshtml` | SmallGroup + NewPerson | ? 完成 | 小組資料網格 |

## ?? 進行中的檔案 (17/20)

### 小組管理相關 (2個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/IntegrateView.cshtml` | ? 待處理 | 整合式小組長點名 |
| `Views/Home/_HappyGroupGrid.cshtml` | ? 待處理 | 幸福小組資料網格 |
| `Views/Home/_IndividualReportGrid.cshtml` | ? 待處理 | 個人回報資料網格 |

### 新人管理相關 (2個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/NewPerson.cshtml` | ? 待處理 | 新增新人表單 |
| `Views/Home/NewPersonFollowUpView.cshtml` | ? 待處理 | 新人跟進關懷 |

### 個人資訊相關 (3個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/PersonalReport.cshtml` | ? 待處理 | 個人回報頁面 |
| `Views/Home/PersonalInfomationView.cshtml` | ? 待處理 | 個人資訊頁面 |
| `Views/Home/MaintainPersonInfomationView.cshtml` | ? 待處理 | 基本資料維護 |

### 奉獻管理相關 (3個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/QPayView.cshtml` | ? 待處理 | 永豐金流奉獻 |
| `Views/Home/DedicationFeeView.cshtml` | ? 待處理 | 奉獻收費清單(LINE) |
| `Views/Home/DedicationFeeViewWeb.cshtml` | ? 待處理 | 奉獻收費清單(網頁) |

### 奉獻稽核相關 (2個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/DedicationFeeAuditViewLine.cshtml` | ? 待處理 | 奉獻稽核(LINE) |
| `Views/Home/DedicationFeeAuditViewWeb.cshtml` | ? 待處理 | 奉獻稽核(網頁) |

### QR Code 相關 (1個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/QrCodeView.cshtml` | ? 待處理 | 教會課程 QR Code |

### 名單管理相關 (1個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/ChurchRoot.cshtml` | ? 待處理 | 教會組織架構 |

### 其他頁面 (3個待處理)

| 檔案 | 狀態 | 說明 |
|------|------|------|
| `Views/Home/Login.cshtml` | ? 待處理 | 登入頁面 |
| `Views/Home/LineIdLoginView.cshtml` | ? 待處理 | LINE登入頁面 |
| `Views/Home/DisplayErrorView.cshtml` | ? 待處理 | 錯誤頁面 |

## ?? 進度統計

- **總檔案數**: 20
- **已完成**: 3 (15%)
- **進行中**: 0 (0%)
- **待處理**: 17 (85%)

## ?? 更新方法

### 自動化腳本
```powershell
# 模擬執行（預覽變更）
.\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf

# 實際執行
.\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf:$false

# 詳細模式
.\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf:$false -Verbose
```

### 手動更新步驟

1. **打開檔案**
2. **搜尋**替換模式 (Ctrl+H):
   - 小組管理: `Controller("Home")` → `Controller("SmallGroup")`
   - 新人管理: `Controller("Home")` → `Controller("NewPerson")`
   - 個人資訊: `Controller("Home")` → `Controller("Personal")`
   - 奉獻管理: `Controller("Home")` → `Controller("Dedication")`
   - 等等...

3. **檢查**其他路由形式:
   - URL 路徑: `/Home/Action` → `/NewController/Action`
   - Url.Action: `Url.Action("Action", "Home")` → `Url.Action("Action", "NewController")`
   - JavaScript: `"/Home/Action"` → `"/NewController/Action"`

4. **儲存檔案**

5. **測試**:
   - 建置專案
   - 執行應用程式
   - 測試相關功能

## ?? 注意事項

### 不要更新的檔案
- `Views/Home/SchedulerView.cshtml` - 已在 LINE 註冊
- `Views/Home/LineLiffView.cshtml` - 已在 LINE 註冊  
- 包含 QR Code 的部分路由 - 可能已印製實體卡片

### 向後相容性
建議在控制器中保留向後相容路由:
```csharp
[Route("/Home/OldAction")]  // 向後相容
[Route("/NewController/NewAction")]  // 新路由
public IActionResult NewAction()
{
    // ...
}
```

## ?? 變更模式

### WebAPI Controller
```csharp
// 修改前
.Controller("Home")
.LoadAction("LoadIntegrate")

// 修改後
.Controller("SmallGroup")
.LoadAction("LoadIntegrate")
```

### URL 路徑
```csharp
// 修改前
"/Home/MultiGroupView"

// 修改後
"/SmallGroup/MultiGroupView"
```

### Url.Action
```csharp
// 修改前
@Url.Action("SaveIntegrate", "Home")

// 修改後
@Url.Action("SaveIntegrate", "SmallGroup")
```

### JavaScript AJAX
```javascript
// 修改前
url: "/Home/LoadIntegrate"

// 修改後
url: "/SmallGroup/LoadIntegrate"
```

## ? 測試檢查清單

每個檔案更新後需要測試:

- [ ] 頁面可以正常載入
- [ ] 導航連結正確
- [ ] 表單提交正常
- [ ] AJAX 呼叫成功
- [ ] DataGrid 載入成功
- [ ] 增刪改查功能正常
- [ ] 沒有 404 錯誤
- [ ] 沒有 JavaScript 錯誤

## ?? 今日目標

### 上午 (4 小時)
- [x] 建立批次更新腳本
- [x] 更新 `_GeneralGroupGrids.cshtml`
- [ ] 更新 `IntegrateView.cshtml`
- [ ] 更新 `NewPerson.cshtml`
- [ ] 更新 `NewPersonFollowUpView.cshtml`

### 下午 (4 小時)
- [ ] 更新個人資訊相關 (3個檔案)
- [ ] 更新奉獻管理相關 (3個檔案)
- [ ] 測試所有更新的檔案

## ?? 累計進度

### 總體進度 (所有 View)
- 已完成: 3/101 (3%)
- 本批目標: 20/101 (20%)
- 預計總進度: 23/101 (23%)

### 本批進度
- 已完成: 3/20 (15%)
- 預計今日完成: 10/20 (50%)

## ?? 下一批計畫

完成本批 (高優先級) 後,進入第三批 (中優先級):
- 行事曆相關
- 剩餘 QR Code
- 剩餘奉獻管理
- 課程點名繳費

---

**最後更新**: 2024/12/XX  
**更新人**: 開發團隊  
**狀態**: ?? 進行中
