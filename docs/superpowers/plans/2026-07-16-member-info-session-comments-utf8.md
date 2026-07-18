# Member Info Session Comments and UTF-8 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為本次會友資訊 session 的程式變更加上深入且可維護的繁體中文註解，並證明所有目標文字檔維持有效 UTF-8。

**Architecture:** 依檔案所有權拆成 C# 業務邏輯、Razor 前端、測試契約三個互不重疊工作包；主代理負責 `.csproj`、Spec/Plan、UTF-8 與整合驗證。任務只新增或校正註解，不允許功能性程式變更。

**Tech Stack:** C# 10、ASP.NET Core Razor、HTML/CSS/JavaScript、DevExtreme 21.2、xUnit、FluentAssertions、PowerShell UTF-8 strict decoder

---

### Task 1: 建立基準與編碼清單

**Files:**
- Verify: all files listed in Tasks 2–5

- [ ] 執行 `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo`，預期 103 項全數通過。
- [ ] 以 `new UTF8Encoding(false, true)` 解碼每個目標檔案並記錄 BOM/U+FFFD，預期全部有效 UTF-8 且 U+FFFD 為 0。
- [ ] 記錄 `git status --short`，保留既有 `.ccg/tasks/fix-member-info-tree-loading/task.json` 與手機防放大未提交變更。

### Task 2: C# 業務邏輯與 ViewModel 註解

**Files:**
- Modify: `ChurchReport/Controllers/MemberInfoController.cs`
- Modify: `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- Modify: `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- Modify: `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- Modify: `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- Modify: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`

- [ ] 完整閱讀六個檔案及其相對主分支 diff。
- [ ] 在搜尋授權資料流、生日正規化、CRM 欄位集合、未填區長排序、特殊權限字串、搜尋列去重與 DTO 契約旁加入繁體中文原因註解。
- [ ] 確認 diff 只包含註解／空白，不更動可執行 token。

### Task 3: Razor/HTML/CSS/JavaScript 註解

**Files:**
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- Modify: `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`

- [ ] 完整閱讀兩個 Razor 檔與其相對主分支 diff。
- [ ] 為工具列、階層節點、搜尋狀態機、取消與逾時、結果替換、loading、grid dispose、單一捲軸、觸控水平滑動與 iOS 16px 防放大加入深入註解。
- [ ] 為性別 OptionSet、生日 nullable/格式化與未設定狀態加入契約註解。
- [ ] 確認 Razor/CSS/JavaScript 註解語法合法，且沒有改動 selector、函式、條件或 DOM。

### Task 4: 防回歸測試註解

**Files:**
- Modify: `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

- [ ] 完整閱讀五個測試檔與其相對主分支 diff。
- [ ] 在非直覺 Arrange 資料與 contract/string assertion 前加入「防止什麼回歸」的繁體中文註解。
- [ ] 不修改測試名稱、輸入、斷言或 helper 行為。

### Task 5: 專案設定與說明文件

**Files:**
- Modify: `ChurchReport/ChurchReport.csproj`
- Maintain: `docs/superpowers/specs/2026-07-16-member-info-session-comments-utf8-design.md`
- Maintain: `docs/superpowers/plans/2026-07-16-member-info-session-comments-utf8.md`

- [ ] 在 `Tests/**` 三個排除項目前加入 XML 註解，說明避免主 Web 專案重複編譯／發佈測試檔。
- [ ] 自我審查 Spec/Plan，確認沒有 placeholder、矛盾、範圍外修改或功能變更指示。

### Task 6: 整合驗證與審查

**Files:**
- Create: `.ccg/tasks/document-member-info-session/review.md`
- Update: `.ccg/tasks/document-member-info-session/task.json`

- [ ] 重新執行完整 MemberInfo tests，預期 103 項全數通過。
- [ ] 重新執行 strict UTF-8、U+FFFD、BOM inventory，預期全部通過。
- [ ] 執行 `git diff --check` 與逐檔差異審查，確認無功能性 token 改變。
- [ ] 並行呼叫 Gemini 與 Claude 審查；若外部 CLI 失敗，把錯誤與主代理人工審查結果寫入 `review.md`。
- [ ] 保持 task 為 `in_progress`，等待使用者測試；依要求不 Commit、不歸檔。
