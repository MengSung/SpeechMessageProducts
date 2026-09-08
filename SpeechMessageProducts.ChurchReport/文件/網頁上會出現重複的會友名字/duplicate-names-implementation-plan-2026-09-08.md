# 修正小組回報重複姓名 Implementation Plan

> 給執行者：使用 Trellis inline 工作流及 `superpowers:executing-plans` 逐項實作。此文件不授權直接修改正式 CRM；本輪交付是規劃。

**Goal:** 消除重複 row、半完成報告與錯小組/日期資料，保留合法同名會友、未上傳草稿及既有功能。

**Architecture:** 沿用 MVC → ListManager → connector → Dataverse；新增 Services/SmallGroup 的 scope/key、協調器與 detached snapshot。資料載入、Grid 編輯、上傳與日期切換共用狀態版本契約，CRM 使用平台業務鍵防重。

**Tech Stack:** .NET 10、ASP.NET Core MVC、Dataverse SDK、DevExtreme、xUnit/FluentAssertions、PowerShell。

**Spec:** Trellis 同任務 `prd.md`、`design.md`；使用者文件目錄的 `duplicate-names-implementation-handbook-2026-09-08.md`。

**基準:** `6cb4b489dc84571609bb7bd80c00cf5f26fc34b2`，分支 `Jesus.5.2.3.FixDuplicateName.Worktree`。

## 全域约束

- 同名不同 Contact 不合併；row uniqueness 只在同一 grid 的回應內檢查，AllMember 與子分類可合法同時含該人。
- 不復用錯 key 的舊 snapshot；不發布 LoadFlag=true 的半成品；不捕獲 request-scoped 物件做背景工作。
- 新 holder、Task、CTS、semaphore、CRM lease、cache 均有 owner、容量與 drain/dispose 契約。
- 先讀完整被改檔案再改；保留 UTF-8 無 BOM/CRLF 慣例，不把編碼差異當成程式損毀。
- 每個 PR 可獨立審閱，但 P0 協調核心與所有呼叫/寫入入口必須完成整合後才開啟。
- 不新增無界背景 queue；不全站改 Redis；不把 DevExtreme 最新大版本升級當成本次必要條件。

## 建議批次與交付順序

| 批次 | 可交付物 | 依賴 | 可否單獨宣稱已修復 |
|---|---|---|---|
| P0-A | 診斷與重現 fixture、目前架構證據 | 無 | 否 |
| P0-B | scope/key、single-flight、candidate、holder lease | P0-A | 否，尚需入口接線 |
| P0-C | HTML/API/CRUD/Save/date/登入全部接線 | P0-B | 只能宣稱軟體共享狀態修正，CRM/UI 根因仍需檢驗 |
| P1-D | Dataverse 主檔/明細唯一性及資料處理變更單 | P0-A 的只讀查核與業務規則 | 獨立遷移，需完整整合驗收 |
| P1-E | DevExtreme 資產相容組合、hash、瀏覽器回歸 | P0-A 版本清冊 | 獨立資產 PR |
| R-F | 全範圍驗收與上線記錄 | 全部 | 所有必要 gate PASS 才可 |

估計 8–15 個開發工作日，另計正式資料清理、授權資產、手機實機與等待 CRM owner 的時間；這是規劃估算，取證後調整。

## Task 0：可重現證據與安全診斷

**修改：**
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs`
- `ChurchReport/Controllers/NewPersonController.cs`
- `ChurchReport/Models/ListManager.cs`

**新增：**
- `ChurchReport/Services/SmallGroup/MemberRowInvariant.cs`
- `ChurchReport.MemberInfo.Tests/SmallGroup/MemberRowInvariantTests.cs`
- `ChurchReport.MemberInfo.Tests/SmallGroup/IntegrateTestData.cs`

**介面：**`MemberRowInvariant.ValidateKeys(IEnumerable<string> keys)`；空/重複 key 拋 `InvalidDataException`，對外轉 409/診斷，不輸出姓名。觀測期只記錄，強制拒絕與前端錯誤處理同批開啟。

- [ ] 先跑既有 LoginFirstPageScope tests，記錄基準；確認舊資料流/首屏延遲不被回復成全同步 HTML。
- [ ] 新增 validator 行為測試，例：

```csharp
[Fact]
public void DuplicateGridKeysAreRejected()
{
    Action act = () => MemberRowInvariant.ValidateKeys(new[] { "r1", "r1" });
    act.Should().Throw<InvalidDataException>();
}
[Fact]
public void DistinctRowsRemainEvenWhenNamesAreEqual()
{
    var rows = new[] { (Id: "r1", Name: "測試同名"), (Id: "r2", Name: "測試同名") };
    Action act = () => MemberRowInvariant.ValidateKeys(rows.Select(x => x.Id));
    act.Should().NotThrow();
}
```

- [ ] 加空字串、GUID 大小寫正規化、合法 draft key、跨分類相同 key 測試；先確認 RED 再寫 validator 至 GREEN。
- [ ] 診斷記錄 RequestTraceId、scope/key 的不可逆別名、instance id、generation、CRM rows/output rows、duplicate counts、duration；不新增原始 Session/帳密/姓名日誌。
- [ ] 依手冊保存正常/異常 Network、DataSource、DOM 證據；可控 fake loader 以 barrier/TaskCompletionSource 重現，不靠 Sleep 猜時序。
- [ ] 正式資料若尚無存取權或無法重現，記為 BLOCKED，勿捏造 PASS；不妨礙先完成有程式證據的缺陷測試。

**Gate A:** 已區分目前分支有鎖與舊報告無鎖；validator 與假資料可重現案例通過。

## Task 1：協調核心與生命週期

**新增（全部屬 Services/SmallGroup）：**
- `IntegrateLoadKey.cs`：完整 scope/key（精確欄位見 design §3）。
- `IntegrateSnapshot.cs`：純值報告/Member/Chart，不含 ToolUtility/Entity/uploader。
- `IntegrateSnapshotCoordinator.cs`：同 key single-flight、generation、失效/錯誤重試。
- `ListManagerStore.cs`：有容量、TTL、active lease/drain 的 holder store。

**修改：**
- `ChurchReport/Models/ListManager.cs`
- `ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- `ChurchReport/Models/IInMemoryDataContext.cs`（必要的 scope 重綁介面）
- `ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs`
- `ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs`
- `ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs`
- `ChurchReport/Models/ListSmallGroupWeeklyReport.cs`
- `ChurchReport/Startup.cs`（DI）、`ChurchReport/Startup.Caching.cs`（專用 cache lifetime）

**測試新增：**
- `ChurchReport.MemberInfo.Tests/SmallGroup/IntegrateSnapshotCoordinatorTests.cs`
- `ChurchReport.MemberInfo.Tests/SmallGroup/ListManagerStoreTests.cs`
- `ChurchReport.MemberInfo.Tests/SmallGroup/IntegrateSnapshotIsolationTests.cs`

**介面：**`ListManager.EnsureIntegrateSnapshotAsync(IntegrateLoadKey key, CancellationToken requestAborted)` 回傳 `Task<IntegrateSnapshot>`。授權、scope 正規化在入口完成；Task coordinator 只接受不含 HttpContext/憑證的輸入。

- [ ] 用 fake builder 寫同 key 100 calls 共用一次 build 測試；TaskCompletionSource 在 build 中途暫停，斷言 `BuildCount==1`、沒有 Ready snapshot 外洩，放行後同 generation/資料一致。
- [ ] 寫 A 日期載入慢、B scope 更新、A 晚完成的測試：A 不得發布到 B；B 失敗不能回傳 A 快照。
- [ ] 寫 request A 取消但 B 仍成功、build failure 後重試、authorization epoch 變更及 cache eviction/drain 測試。
- [ ] 實作短 state lock 與受控 in-flight task；scope mutation、candidate publish、exception cleanup 使用同一狀態機。
- [ ] loader 每次新建，但 CRM 必須從操作 lease 注入；避免 new loader 仍共用 singleton mutable client。保存 Acquire/Release baseline。
- [ ] LoadFlag 僅在 candidate 完整且驗證通過後、swap 前設 true。捕捉空 Contact/找不到小組/缺集合為明確失敗。
- [ ] 將 AllGroupList/WeeklyReportChart 從 metadata cache 分開，key 包含組織/可見範圍，儲存 detached values、有容量/TTL；保留批次 Contact 查詢，勿引入 N+1。
- [ ] store 接入 request IDisposable lease；保留舊 cache key 身分綁定語意、替換非原子的 Get/Set；TTL 到期但仍持有操作時不 dispose 或建立第二個相同 scope writer。
- [ ] 編寫必要的 projection，使 ListSmallGroupWeeklyReport 回應 adapter 不配置 uploader。只 `ToArray()` 仍共用 Member 不能算通過隔離測試。

**Gate B:** 純行為測試 PASS；同 key 同世代最多一個 build；取消與 drain 沒有 retained Task、registration 或重複 holder。

## Task 2：所有入口、草稿與儲存接線

**修改：**
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.IntegrateView.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs`
- `ChurchReport/Controllers/SmallGroupController/SmallGroupController.MultiGroupView.cs`
- `ChurchReport/Controllers/NewPersonController.cs`
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
- `ChurchReport/Controllers/PersonalController.cs`
- `ChurchReport/Controllers/EquipmentController.cs`
- `ChurchReport/Models/SmallGroupData.cs`
- `ChurchReport/Views/Home/IntegrateView.cshtml`
- `ChurchReport/Views/Home/_GeneralGroupGrids.cshtml`
- `ChurchReport/Extensions/ListManagerCacheExtensions.cs`（如保留舊 wrapper，必須轉接同一核心）

**測試新增：**`ChurchReport.MemberInfo.Tests/SmallGroup/IntegrateEndpointTests.cs`、`IntegrateMutationTests.cs`、`IntegrateUploadLifecycleTests.cs`。

- [ ] 先寫三個 API 同時載入、newperson 首個抵達且 detailed 尚未 ready、chart 舊參數 WeeklyReportId 實際 ListId 的測試。
- [ ] LINE 登入改為 Contact/auth → Setup scope → 決定 ListId/個人/多組 view → ensure 或延遲 placeholder → ViewBag；移除相依 Task.WhenAll，保留既有登入授權。
- [ ] 所有 ensure 入口驗證可見 List；每 action 固定一個 holder/snapshot；合法 empty data 與失敗區分，JSON serialization 不能再走共享 Member reference。
- [ ] Grid update/insert/delete/newperson 改同一 mutation 命令、expectedGeneration、欄位白名單；移除兩份清單平行 PopulateObject。Refresh 不可覆蓋未上傳 draft。
- [ ] SaveIntegrate 移除 fire-and-forget，複製輸入值並等待實際上傳；成功才清理對應 generation 的轉組資料。停用按钮、timeout 結果待查、先對帳後重試。
- [ ] date/multi-group/帳號變更增加 scopeVersion；旧 tab/晚 response 回 409 或 client 忽略，不能拿舊報告成功回應。
- [ ] 沒有週報的 counter key 改穩定 draft key；所有轉 GUID/CRUD/upload 分支一起更新；同名不同 Contact 均保留。
- [ ] 搜尋 direct Setup/LoadFlag/public report writes；每個 production 呼叫移入協調入口。Demo ids `001` 等保留獨立 demo adapter，不繞過真實 scope 驗證。

```powershell
rg -n 'SetupIntegrateData\(|EnsureIntegrateDataLoaded\(|m_ListSmallGroupWeeklyReport\s*=|\.LoadFlag\s*=' ChurchReport/Controllers ChurchReport/Models ChurchReport/Extensions
rg -n 'Task.Run|Task.WhenAll|RemoveTransferredMembers|PopulateObject' ChurchReport/Controllers/SmallGroupController ChurchReport/Controllers/NewPersonController.cs
```

**Gate C:** 所有 production 旁路都有對應處置；同 Session A/B tab、A 登出 B 登入、平行不同使用者、保存/重載競態與 lifecycle PASS。未達 gate 不開啟新核心。

## Task 3：Dataverse 變更單與全寫入者冪等

**新增：**`ChurchReport/Services/PresentRecord/PresentRecordIdentity.cs`、`PresentRecordIdempotencyService.cs`、`ChurchReport.MemberInfo.Tests/SmallGroup/PresentRecordIdempotencyTests.cs`。

**修改清冊：**
- `ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs`
- `ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs`
- `ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs`
- `ChurchReport/Tools/WeeklyReportProcessor.cs`
- `ChurchReport/Models/ListSmallGroupWeeklyReport.cs`
- `ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs`
- `ChurchReport/Tools/PersonalQrCodeUtility.cs`
- `ChurchReport/Tools/SmallGroupQrCodeUtility.cs`
- `ChurchReport/Tools/SundayQrCodeUtility.cs`
- `ChurchReport/WebServiceConnector/NewPerson.cs`、`UploadData.cs`（依 writer 清冊確認實際分支）
- `ToolUtility/QueryOperations/PresentRecordQueryService.cs`、`ComplexQueryService.cs`（查詢/分頁/衝突邊界）

- [ ] 確認同組同週是否一筆、QR 是否可多次、無小組個人回報獨立 scope；确认前不建立全域唯一約束。
- [ ] sandbox 以全部分頁匯出主檔/明細、active/inactive、關聯/欄位與建立來源；不要只查 active 或前 5000 筆。
- [ ] 測試 0/1/>1、同 key 雙 instance、十次相同 command、timeout 後重送、補建不得覆蓋既有出席/關懷欄位。
- [ ] 建立含備份/映射/停写/恢復的正式變更單；暫停 app/plugin/flow 相關 writer，先清理資料再建 key，詳見設計 §7/手冊。
- [ ] 驗證全部被索引 rows 的唯一性；退休列僅 statecode inactive 不足，若使用新文字 key，按映射清空或改 retirement key；所有 active record 的 canonical key 必須非空。
- [ ] 等 Alternate Key 為 Active；Pending/InProgress/Failed 均不啟用新 writer。
- [ ] 將週報與明細改共用 platform key/Upsert/有條件更新。Count>1 回明確衝突，禁止 Count!=1 時再新增；個人 GET 補建拆到受控命令且保留原有使用者功能。
- [ ] 對 owner assignment、關聯轉移、通知等額外副作用作重送驗證；Upsert 不等於整批 exactly-once。
- [ ] 寫入恢復後再次分頁對帳；不直接在本輪執行正式資料清理。

**Gate D:** 業務鍵核准、備份/映射完成、索引 Active、全部 writer 遷移與 CRM sandbox concurrency PASS。

## Task 4：資產對齊與瀏覽器回歸

**修改：** `ChurchReport/ChurchReport.csproj`、`ChurchReport/Startup.cs` 及實際引用 JS/CSS 的下列 Views：

- `ChurchReport/Views/Shared/_Layout.cshtml`
- `ChurchReport/Views/Shared/_LoginResources.cshtml`
- `ChurchReport/Views/Authentication/Login.cshtml`
- `ChurchReport/Views/Authentication/LineIdLoginView.cshtml`
- `ChurchReport/Views/Authentication/LineLiffView.cshtml`
- `ChurchReport/Views/Dedication/DediationLineLoginView.cshtml`
- `ChurchReport/Views/Home/BindingResultView.cshtml`
- `ChurchReport/Views/Home/DonationPaymentLogin.cshtml`
- `ChurchReport/Views/Home/QualificationView.cshtml`
- `ChurchReport/Views/Home/VisitorCard.cshtml`
- `ChurchReport/Views/Home/IntegrateView.cshtml`、`_GeneralGroupGrids.cshtml`（scope/bfcache/error 流程）

**資產：**`ChurchReport/wwwroot/js/devextreme/dx.all.js`、`js/devextreme/aspnet/dx.aspnet.mvc.js`、`wwwroot/lib/devextreme-aspnet-data/js/dx.aspnet.data.js`、Layout 實際引用的 `wwwroot/css/devextreme/` CSS。

- [ ] 全域搜尋、記錄有效/被註解資產與 runtime version；選 23.1.5 相容組合須有合法資產來源；JS data helper/AspNet.Data 查相容性，不要求版本號同號。
- [ ] 本地標籤加 `asp-append-version="true"`，例：

```html
<script src="~/js/devextreme/dx.all.js" asp-append-version="true"></script>
<link rel="stylesheet" href="~/css/devextreme/dx.light.compact.css" asp-append-version="true" />
```

- [ ] 選取日期/小組的請求帶 scopeVersion；忽略較舊完成順序的 response。bfcache pageshow 恢復先驗證 scope，再 reload；有 dirty draft 不得無條件丟棄。
- [ ] Grid A/B（virtual 與 standard）只用來診斷；不把 B 組正常當作 server bug 根治。
- [ ] 執行本專案前台兩層回歸手冊 Tier 1，依 trigger matrix 選 Tier 2；覆蓋桌面/手機/LINE 實機、50 次上下滾動、返回與上傳後 refresh。

**Gate E:** 資產清冊相容、無 404/duplicate key 警告、Network/DataSource/DOM 無異常重複、合法同名及草稿保留。

## Task 5：驗收、量測與交付

所有命令從 repo root 執行；下列是未來實作驗證命令，不是本輪已通過的結果。

```powershell
dotnet --info
dotnet restore ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
dotnet build ChurchReport/ChurchReport.csproj -c Debug --no-restore
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~LoginFirstPageScope' --logger 'trx;LogFileName=login-scope.trx'
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~SmallGroup' --logger 'trx;LogFileName=duplicate-names.trx'
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --no-restore --logger 'trx;LogFileName=memberinfo-full.trx'
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj -c Debug
git diff --check
```

`ChurchReport.Tests/` 目前只有 PerformanceTests 資料夾、沒有 csproj；新增測試放入現有 `ChurchReport.MemberInfo.Tests/SmallGroup/`，不要執行不存在的測試專案。修改某測試/失敗修正後才重跑相應範圍；最終跑完整相關測試。

- [ ] 同 key 100 concurrent × 100 fresh generations；每輪 build=1、keys 唯一、0 incomplete/foreign snapshot。
- [ ] 同使用者/不同 Session 及不同使用者/不同瀏覽器平行，覆蓋 logout/login epoch、未授權 ListId、舊 tab/date、CRM 失敗、取消、cache eviction。
- [ ] fake CRM load test：5 分鐘暖機、15 分鐘壓力、60 分鐘 soak；CRM sandbox 使用低率/保守 quota，不以正式 CRM 壓測。
- [ ] drain 後 active builders/waiters/operation leases/registrations=0；idle cache 等測試 TTL 後 holder=0。重複批次 retained managed bytes 沒有上升趋势；net10 RetainVM 的 reserved/working set 不必精確回初始值，用可達物件/handle/connection baseline 判斷洩漏。
- [ ] 記錄 P50/P95/P99、CRM calls、allocations、ThreadPool queue。相同環境 P95/CRM calls 不超基準 10%；超過先分析正確性成本，未解釋不當作效能通過。
- [ ] 保存本輪 PASS/FAIL/BLOCKED/N/A 紀錄。異常資料只在受控位置保管，Git 只留去識別摘要。
- [ ] 外部 Gemini+Claude review 透過 Start-CcgDualModelRun；quota/auth 失敗如實列出，不能把單一輸出宣稱雙模型 PASS。
- [ ] 依安全版本 canary，再放量；回復只能回安全版本或停用功能，保留 key/診斷，不重開無防重寫入。

**Gate F:** 所有必要隔離/資源/資料完整性測試 PASS，無 release-blocking defect，才可宣稱產品問題解決。規劃交付本身不代表 Gate A–F 已執行。
