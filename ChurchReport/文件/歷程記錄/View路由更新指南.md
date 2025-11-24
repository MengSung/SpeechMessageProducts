# View 路由更新指南

## 概述
本文件說明如何更新所有 View 檔案中的路由連結，以配合控制器重構後的新路由結構。

## 更新範圍

### 需要更新的檔案類型
- ? `*.cshtml` - Razor View 檔案
- ? `_Layout.cshtml` - 主版面配置
- ? `*.js` - JavaScript 檔案中的 AJAX 路由
- ? `*.css` - CSS 檔案(如有硬編碼路由)

### 更新方式

#### 1. href 連結
```razor
<!-- 舊的路由 -->
<a href="/Home/MultiGroupView/MultiGroup">回報統計</a>

<!-- 新的路由 -->
<a href="/SmallGroup/MultiGroupView/MultiGroup">回報統計</a>
```

#### 2. Url.Action 呼叫
```razor
<!-- 舊的路由 -->
@Url.Action("MultiGroupView", "Home", new { LoginParameter = "test" })

<!-- 新的路由 -->
@Url.Action("MultiGroupView", "SmallGroup", new { LoginParameter = "test" })
```

#### 3. Html.ActionLink
```razor
<!-- 舊的路由 -->
@Html.ActionLink("回報統計", "MultiGroupView", "Home")

<!-- 新的路由 -->
@Html.ActionLink("回報統計", "MultiGroupView", "SmallGroup")
```

#### 4. RedirectToAction (在控制器中)
```csharp
// 舊的路由
return RedirectToAction("MultiGroupView", "Home");

// 新的路由
return RedirectToAction("MultiGroupView", "SmallGroup");
```

#### 5. AJAX 呼叫
```javascript
// 舊的路由
$.ajax({
    url: '/Home/MultiGroupView',
    // ...
});

// 新的路由
$.ajax({
    url: '/SmallGroup/MultiGroupView',
    // ...
});
```

## 完整路由對照表

### 1. 小組管理路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/MultiGroupView/{param}` | `/SmallGroup/MultiGroupView/{param}` | 多小組回報 |
| `/Home/IntegrateView/{param}` | `/SmallGroup/IntegrateView/{param}` | 整合式小組長點名 |
| `/Home/SmallGroupReportView/{param}` | `/SmallGroup/SmallGroupReportView/{param}` | 小組長點名及個人回報 |

**受影響的 View 檔案:**
- `MultiGroupView.cshtml`
- `IntegrateView.cshtml`
- `SmallGroupReportView.cshtml`
- `_Layout.cshtml`

### 2. 新人管理路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/NewPersonFollowUpView` | `/NewPerson/FollowUpView` | 新人跟進關懷 |
| `/Home/NewPerson` | `/NewPerson/NewPerson` | 新增新人 |

**受影響的 View 檔案:**
- `NewPersonFollowUpView.cshtml`
- `NewPerson.cshtml`
- `_Layout.cshtml`

### 3. 個人資訊路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/PersonalReport` | `/Personal/Report` | 個人回報 |
| `/Home/PersonalInfomationView` | `/Personal/InfomationView` | 個人相關資料 |
| `/Home/MaintainPersonInfomationView` | `/Personal/MaintainInfomationView` | 基本資料維護 |

**受影響的 View 檔案:**
- `PersonalReport.cshtml`
- `PersonalInfomationView.cshtml`
- `MaintainPersonInfomationView.cshtml`
- `_Layout.cshtml`

### 4. 行事曆路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/Scheduler/{type}` | `/Scheduler/{type}` | 行事曆 |
| `/Home/SchedulerView/{param}` | `/Scheduler/SchedulerView/{param}` | LINE LIFF 行事曆 |

**受影響的 View 檔案:**
- `Scheduler.cshtml`
- `SchedulerView.cshtml`
- `_Layout.cshtml`

### 5. 奉獻管理路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/QPayView/{id}` | `/Dedication/QPayView/{id}` | 永豐金流奉獻 |
| `/Home/DedicationFeeView` | `/Dedication/DedicationFeeView` | 奉獻收費清單(LINE) |
| `/Home/DedicationFeeViewWeb` | `/Dedication/DedicationFeeViewWeb` | 奉獻收費清單(網頁) |
| `/Home/KeyInDedicationFeeView` | `/Dedication/KeyInDedicationFeeView` | 手動輸入奉獻(LINE) |
| `/Home/KeyInDedicationFeeViewWeb` | `/Dedication/KeyInDedicationFeeViewWeb` | 手動輸入奉獻(網頁) |
| `/Home/DediationLineLoginView/{param}` | `/Dedication/DediationLineLoginView/{param}` | 奉獻LINE登入 |

**受影響的 View 檔案:**
- `QPayView.cshtml`
- `DedicationFeeView.cshtml`
- `DedicationFeeViewWeb.cshtml`
- `KeyInDedicationFeeView.cshtml`
- `KeyInDedicationFeeViewWeb.cshtml`
- `DediationLineLoginView.cshtml`
- `_Layout.cshtml`

### 6. 奉獻稽核路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/DedicationFeeAuditViewLine` | `/DedicationAudit/AuditViewLine` | 奉獻稽核(LINE) |
| `/Home/DedicationFeeAuditViewWeb` | `/DedicationAudit/AuditViewWeb` | 奉獻稽核(網頁) |

**受影響的 View 檔案:**
- `DedicationFeeAuditViewLine.cshtml`
- `DedicationFeeAuditViewWeb.cshtml`
- `_Layout.cshtml`

### 7. QR Code 路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/QrCodeView/{param}` | `/QrCode/CourseView/{param}` | 教會課程 QR Code |
| `/Home/PollQrCodeView/{param}` | `/QrCode/PollView/{param}` | 問卷調查 QR Code |
| `/Home/SmallGroupQrCodeView/{param}` | `/QrCode/SmallGroupView/{param}` | 小組聚會 QR Code |
| `/Home/SundayQrCodeView/{param}` | `/QrCode/SundayView/{param}` | 主日 QR Code |
| `/Home/PersonalQrCodeView/{param}` | `/QrCode/PersonalView/{param}` | 個人 QR Code |

**受影響的 View 檔案:**
- `QrCodeView.cshtml`
- `PollQrCodeView.cshtml`
- `SmallGroupQrCodeView.cshtml`
- `SundayQrCodeView.cshtml`
- `PersonalQrCodeView.cshtml`

### 8. 名單管理路由

| 舊路由 | 新路由 | 說明 |
|--------|--------|------|
| `/Home/ChurchRoot` | `/ListManagement/ChurchRoot` | 教會組織架構 |

**受影響的 View 檔案:**
- `ChurchRoot.cshtml`
- `_Layout.cshtml`

## 使用自動化腳本更新

### 1. 測試模式 (推薦先執行)
```powershell
# 進入專案目錄
cd ChurchReport

# 執行腳本
.\Scripts\Update-ViewRoutes.ps1

# 選擇 'Y' 進入測試模式
# 這會顯示所有需要更新的地方，但不實際修改檔案
```

### 2. 正式更新
```powershell
# 執行腳本
.\Scripts\Update-ViewRoutes.ps1

# 選擇 'N' 進入正式模式
# 輸入 'Y' 確認執行
# 這會實際修改檔案
```

### 3. 建議的執行流程
1. ? 先執行測試模式，檢查會更新哪些檔案
2. ? 備份專案 (或確保有 Git 版本控制)
3. ? 執行正式模式更新
4. ? 編譯專案確認無錯誤
5. ? 執行功能測試

## 手動更新步驟

如果不使用自動化腳本，可以按照以下步驟手動更新：

### 步驟 1: 更新 _Layout.cshtml

這是最重要的檔案，包含主要導航選單。

**需要更新的連結:**
```razor
<!-- 小組管理 -->
<li><a href="/SmallGroup/MultiGroupView/MultiGroup">回報統計</a></li>
<li><a href="/SmallGroup/IntegrateView/IntegrateView">小組回報</a></li>

<!-- 新人管理 -->
<li><a href="/NewPerson/NewPerson">新增新人</a></li>

<!-- 個人資訊 -->
<li><a href="/Personal/InfomationView">個人相關資料</a></li>
<li><a href="/Personal/MaintainInfomationView">組員資訊</a></li>

<!-- 奉獻管理 -->
<li><a href="/Dedication/QPayView/網頁登入">奉獻</a></li>
<li><a href="/Dedication/DedicationFeeViewWeb">奉獻收費清單</a></li>
<li><a href="/Dedication/KeyInDedicationFeeView">奉獻管理</a></li>
<li><a href="/DedicationAudit/AuditViewWeb">奉獻稽核</a></li>

<!-- 名單管理 -->
<li><a href="/ListManagement/ChurchRoot">名單管理</a></li>
```

### 步驟 2: 更新各個 View 檔案

使用「尋找和取代」功能:

**Visual Studio:**
1. 按 `Ctrl+Shift+H` 開啟「在檔案中取代」
2. 輸入舊路由模式 (使用正規表示式)
3. 輸入新路由模式
4. 選擇 `*.cshtml` 檔案
5. 點擊「全部取代」

**VS Code:**
1. 按 `Ctrl+Shift+F` 開啟搜尋
2. 使用正規表示式搜尋
3. 逐一確認並取代

### 步驟 3: 更新 JavaScript 檔案

檢查以下檔案中的 AJAX 路由:
- `wwwroot/js/site.js`
- 各個 View 中的 `<script>` 區塊
- 自定義 JavaScript 檔案

範例:
```javascript
// 舊的路由
$.ajax({
    url: '/Home/LoadMultiGroupData',
    type: 'GET',
    // ...
});

// 新的路由
$.ajax({
    url: '/SmallGroup/LoadMultiGroupData',
    type: 'GET',
    // ...
});
```

## 驗證更新

### 1. 編譯檢查
```bash
dotnet build
```
確保沒有編譯錯誤。

### 2. 路由測試
測試所有主要功能的路由:
- [ ] 登入功能
- [ ] 小組回報
- [ ] 新增新人
- [ ] 個人資訊
- [ ] 行事曆
- [ ] 奉獻功能
- [ ] QR Code 掃描

### 3. 導航測試
測試所有導航選單連結:
- [ ] 側邊欄選單
- [ ] 麵包屑導航
- [ ] 返回按鈕
- [ ] 提交表單後的重定向

### 4. AJAX 測試
測試所有 AJAX 功能:
- [ ] DataGrid 載入
- [ ] 表單提交
- [ ] 動態內容更新

## 常見問題

### Q1: 腳本執行失敗怎麼辦?
**A:** 
1. 確認 PowerShell 執行政策允許執行腳本:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```
2. 確認檔案路徑正確
3. 檢查檔案編碼 (應為 UTF-8)

### Q2: 部分路由更新後還是 404?
**A:**
1. 檢查 `Startup.cs` 中的路由配置
2. 確認新控制器已正確建立
3. 檢查 Action 方法名稱是否正確
4. 清除瀏覽器快取

### Q3: 舊連結還能用嗎?
**A:** 
目前新路由還未加入向後相容層。建議:
1. 在 `Startup.cs` 中加入舊路由的重定向
2. 或在 `HomeController` 中保留重定向方法

### Q4: LINE LIFF 路由需要更新嗎?
**A:**
LINE LIFF 路由已在 LINE Developer Console 註冊，**暫時不要更改**。如需更改:
1. 先更新 LINE Developer Console 設定
2. 等待設定生效
3. 再更新程式碼

## 回滾計畫

如果更新後出現問題:

### 方法 1: Git 回滾
```bash
git checkout -- Views/
git checkout -- wwwroot/js/
```

### 方法 2: 從備份還原
還原之前備份的檔案。

### 方法 3: 使用舊路由別名
在 `Startup.cs` 中加入:
```csharp
// 舊路由別名 (向後相容)
routes.MapRoute(
    name: "multigroup_legacy",
    template: "Home/MultiGroupView/{LoginParameter?}",
    defaults: new { controller = "SmallGroup", action = "MultiGroupView" });
```

## 更新檢查清單

### 準備階段
- [ ] 備份專案或確認 Git 狀態
- [ ] 閱讀本文件
- [ ] 準備測試環境

### 執行階段
- [ ] 執行自動化腳本 (測試模式)
- [ ] 檢查測試結果
- [ ] 執行自動化腳本 (正式模式)
- [ ] 或手動更新檔案

### 驗證階段
- [ ] 編譯檢查
- [ ] 路由測試
- [ ] 導航測試
- [ ] AJAX 測試
- [ ] 功能測試

### 完成階段
- [ ] 提交 Git
- [ ] 更新文件
- [ ] 通知團隊
- [ ] 部署到測試環境

## 相關文件

- [路由配置說明](./路由配置說明.md)
- [控制器重構說明](./ControllerRefactoring.md)
- [測試指南](./TestGuide.md)

## 版本歷史

| 版本 | 日期 | 說明 |
|-----|------|------|
| 1.0 | 2024/XX/XX | 初始版本 |

---

**最後更新**: 2024/XX/XX  
**負責人**: 開發團隊  
**狀態**: 待執行 ?
