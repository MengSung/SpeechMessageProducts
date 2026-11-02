# 執行計畫

執行順序：**F3 → F4 → F2 → F1**。理由見 `design.md`「相依與順序」。
每一階段結束都是一個獨立 commit 與回滾點。

## 前置

- [ ] 完整讀過 `AGENTS.md`（156 行）——文件、UTF-8／CRLF、隔離與效能要求皆為 release-blocking。
- [ ] 讀過 `.trellis/spec/backend/index.md`、`quality-guidelines.md`、`logging-guidelines.md`。
- [ ] 讀過 `docs/architecture/dataverse-gateway-v1.md` 與 `dataverse-architecture-code-conformance-v1.md`。
- [ ] 建立基準：記錄修改前的測試通過數，作為「不得退步」的比較基準。

```bash
dotnet build SpeechMessageProducts.sln -c Debug
```

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

```bash
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

> 已知基準：`ToolUtility.Dataverse.Tests` 37/37、`ToolUtility.Tests` 63/63。

---

## 階段 1 — F3 架構文件修正（純文件，無程式風險）

- [ ] 1.1 修改 `docs/architecture/dataverse-gateway-v1.md`「核心不變量」第 4 條，改為區分傳輸層故障與商業層 fault。完整替換文字見 `design.md` F3 節。
- [ ] 1.2 更新同檔「架構圖元件對照表」第 ⑦ 列「已驗證的保護」欄位，加入 `IsConnectionFault` 的分類語意。
- [ ] 1.3 在文末新增「2026-08-21 實測佐證」段落（7 次 business fault / 0 次 faulted 歸還 / 後續操作沿用同一連線且成功）。
- [ ] 1.4 交叉檢查 `docs/architecture/dataverse-architecture-code-conformance-v1.md` 是否有相同的過時敘述，若有一併修正。

**驗證**：閱讀 `ToolUtility/Dataverse/DataverseGateway.cs` 的 `IsConnectionFault()`（第 201 行起）與 XML 註解，確認文件敘述與程式行為逐條一致。無需建置。

**Commit**：`docs(dataverse): 修正 Gateway 架構文件的 Faulted 不變量以符合實作`

---

## 階段 2 — F4 背景工作觀測邊界

- [ ] 2.1 在 `ToolUtility/Dataverse/DataverseTrace.cs` 的 `EventKind` 列舉新增 `BackgroundBegin`、`BackgroundEnd`。
- [ ] 2.2 在 `TraceEntry` 補上承載 `parentTraceId` 與 `op` 所需欄位（可沿用既有的 `Reason` / `Text` 字串欄位，勿為此新增大量欄位）。
- [ ] 2.3 新增 `private static long _bgSeq;` 與公開方法：

```csharp
public IDisposable BeginBackgroundOperation(string operationName)
```

  - `Enabled == false` 或 `_requestContext.Value == null` 時回傳 `NoopScope.Instance`。
  - 建立子 traceId `{parentTraceId}#bg{seq}`、**全新** `RequestStats`、沿用父 `User`。
  - 寫入 `_requestContext.Value`（`AsyncLocal` copy-on-write，不影響父流程）。
  - 回傳的 scope 於 `Dispose()` 寫出 `bg.end` 並還原前一個 context。
- [ ] 2.4 新增 `BackgroundScope : IDisposable`，結構比照既有的 `RequestScope`（第 339 行起），欄位輸出與 `request.end` 完全一致，另加 `parentTraceId`、`op`。
- [ ] 2.5 在 JSONL 序列化路徑加入兩個新事件的輸出分支，事件名固定為 `bg.begin` / `bg.end`。
- [ ] 2.6 為新 API 撰寫繁體中文 XML 文件註解，明確記載：最長資料生命週期、確定性釋放路徑、為何 copy-on-write 不會污染父 request、以及 `operationName` 不得含使用者資料的信任邊界。
- [ ] 2.7 在 `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs` 的 `Task.Run` lambda 最外層（`_scopeFactory.CreateScope()` **之前**）套用：

```csharp
using var traceScope = DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload");
```

  注意 `DataverseTrace.Current` 可能為 null（Trace 停用），需 null-safe。
- [ ] 2.8 新增測試 `ToolUtility.Dataverse.Tests`：
  - 背景 scope 內的 `CrmOperation` 只累計到 `bg.end`，父 `request.end` 的 `crmCount` 不受影響。
  - 巢狀／平行背景 scope 各自獨立，`parentTraceId` 正確。
  - Trace 停用時 `BeginBackgroundOperation` 為零配置無操作。

**驗證**：

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

**Commit**：`feat(trace): 新增背景工作獨立觀測範圍，消除 request.end 歸因盲區`

---

## 階段 3 — F2 NOSESSION 快取鍵

檔案：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`

- [ ] 3.1 新增 `private bool TryGetSessionCacheKey(out string key)`，把第 218 行 `GetCurrentSessionId()` 的鍵構建邏輯（sessionId + boundUserId + 短指紋 + 短時戳）整段移入；`CurrentSession == null` 時回傳 `false`、`key = null`。
- [ ] 3.2 逐一改寫六個快取屬性，全部改用 `TryGetSessionCacheKey`：
  - `ListManager`（約 620 行）
  - `SmallGroupDataList`（約 705 行）
  - `WeeklyReportData`（約 760 行）
  - `NewPersonModel`（約 815 行）
  - `PersonalInfomationModel`（約 870 行）
  - `HappyGroupDataManager`（約 925 行）

  無 Session 時**一律不碰 `IMemoryCache`**，改回傳實例欄位 `m_XXX ??= new XXX()`。
- [ ] 3.3 移除 `NOSESSION_{...}_{Ticks}` 鍵的產生邏輯。若 `GetCurrentSessionId()` 尚有其他呼叫端，保留為包裝並回傳固定字串 `"NOSESSION"`；以 grep 確認後再決定保留或刪除。
- [ ] 3.4 更新 `WriteSessionDiagnostic` 在無 Session 分支的訊息，明確指出「已改用實例層級後備物件，未寫入行程快取」。
- [ ] 3.5 為改動區域補繁體中文註解，記載：為何 `Ticks` 鍵是無界成長來源、後備物件的最長生命週期（隨 Scoped 服務回收）、以及為何這不會造成跨 request 殘留。
- [ ] 3.6 新增測試（`ChurchReport.Tests`）：在無 `HttpContext` 的情況下重複存取 `ListManager` **1000 次**，斷言 `IMemoryCache` 項目數不隨存取次數成長（使用可計數的 `IMemoryCache` 測試替身）。

**驗證**：

```bash
dotnet test ChurchReport.Tests/ChurchReport.Tests.csproj
```

**Commit**：`fix(cache): 無 Session 時不再產生每次唯一的快取鍵，消除無界成長路徑`

---

## 階段 4 — F1 背景上傳狀態隔離（影響面最大）

- [ ] 4.1 **先做定位**：grep 以下三個字串在全 repo（排除 `bin`/`obj`）的所有出現位置並列成清單：
  - `m_SmallGroupData.Members`
  - `m_NewPersonFollowUpData.Members`
  - `m_AllMemeberData.Members`

  **把總數記錄在本檔案**。若超過 30 處，改採 `design.md`「若圖結構過於糾纏的退路」（唯讀退路，背景不回寫），並在此註明採用退路的依據。

  **F1.1 盤點結果（2026-08-22）**：完整 repository literal scan 為 140 筆；其中產品可執行
  C# 精確字面量 34 筆，加上 `?.Members` 變體後為 44 筆（另有測試命中與文件／歷史產物）。
  盤點報告保存在 `.ccg/tasks/churchreport-trace-findings-remediation/f1-usage-inventory.md`。
  因可執行存取已超過 30 處，採用設計文件的**唯讀退路**：在背景工作開始前建立深層、
  背景專屬快照；背景上傳與清理只修改快照，絕不發布或回寫 Session／IMemoryCache 共用圖，
  回應新增 `requiresRefresh = true` 供前端重新載入。這避免背景長工作用陳舊快照覆蓋同期
  前景 CRUD，也避免把所有 legacy 讀寫端一次性改成長時間持鎖。`CreateIsolatedSnapshot()` 的
  短鎖只界定快照建立邊界，不宣稱尚未採用該鎖的其他 legacy writer 已具全域執行緒安全。

- [ ] 4.2 在 `SpeechMessageProducts.ChurchReport/Models/` 對應的 `SmallGroupDataList` 型別新增：
  - `private readonly object _syncRoot = new();`
  - `internal object SyncRoot => _syncRoot;`
  - `public SmallGroupDataList CreateIsolatedSnapshot()`——於 `lock (_syncRoot)` 內深拷貝三組 `Members`（新 `List<Member>` **且每個 `Member` 為新實例**）。
- [ ] 4.3 若 `Member` 無複製建構式或 `Clone()`，新增之，並確保複製涵蓋所有可變欄位。
- [ ] 4.4 在 `ListSmallGroupWeeklyReport` 新增 `public ListSmallGroupWeeklyReport CreateBackgroundUploadCopy()`：新實例的 `m_SmallGroupDataList` 指向快照，其餘純量欄位以值複製。
- [ ] 4.5 改寫 `SmallGroupController.Save.cs` 的 `SaveIntegrate`：
  - `var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;`
    改為先取得共用參考，再 `var backgroundCopy = weeklyReportRef.CreateBackgroundUploadCopy();`
  - `Task.Run` 內一律使用 `backgroundCopy`，**不得再引用 `weeklyReportRef` 或 `allMemberData`**。
  - `RemoveTransferredMembers` 作用於 `backgroundCopy`。
  - 清理完成後取 `SyncRoot` 鎖，以**整份替換參考**（`list = newList`）而非 `Clear()+AddRange()` 的方式回寫共用圖。
- [ ] 4.6 依 4.1 的清單，把前景直接列舉或改寫這三組 `Members` 的位置改為在 `lock (SyncRoot)` 內取得快照後再操作。
- [ ] 4.7 保留 `password` 捕獲的現行行為，但加上 TODO 註解指向既有機密管理技術債（`appsettings.json` 明文密碼、`ToolUtilityClass` legacy credential fallback），不得靜默忽略。
- [ ] 4.8 為所有改動區域補繁體中文文件註解，明確記載：單一資源擁有者、競態行為、臨界區範圍與為何不長期持鎖、以及背景快照的最長生命週期。
- [ ] 4.9 新增測試（`ChurchReport.Tests`）：模擬背景任務持續改寫快照的同時，前景執行緒重複列舉共用集合 **≥1000 次**，斷言不擲出 `InvalidOperationException` 且每次列舉看到的都是完整清單（非半清空狀態）。

  **F1.2 lifecycle 補正（2026-08-22）**：獨立審查確認 `Task.Run` 流動的
  `IHttpContextAccessor.HttpContext.RequestServices` 仍可讓 legacy `ToolUtilityFactory` 解析已結束的
  request scope。背景工作現於 `using var scope = scopeFactory.CreateScope()` 後立即使用
  `using var ambientScope = ToolUtilityFactory.BeginBackgroundScope(scope.ServiceProvider)`；ambient CRM
  resolver 的流程區域 override 優先於 request accessor，並流入上傳器第二層 Task.Run。scope 仍由
  SaveIntegrate 的 using 唯一釋放，override 只在同一流程保留短期 provider 參考並於離開時還原。

**驗證**：

```bash
dotnet build SpeechMessageProducts.sln -c Debug
```

```bash
dotnet test SpeechMessageProducts.sln
```

**Commit**：`fix(smallgroup): SaveIntegrate 背景上傳改用獨佔快照，消除與前景請求的資料競爭`

---

## 收尾檢查（全部階段完成後）

- [ ] 5.1 全方案建置無新增警告：

```bash
dotnet build SpeechMessageProducts.sln -c Debug --no-incremental
```

- [ ] 5.2 全測試通過，且通過數不低於前置階段記錄的基準：

```bash
dotnet test SpeechMessageProducts.sln
```

- [ ] 5.3 **編碼逐位元組驗證**（AGENTS.md 要求，且本 repo 已有 4 個檔案存在亂碼前科）。對每一個新增或修改的 `.cs`／`.cshtml` 確認：UTF-8 無 BOM、CRLF、檔尾有 CRLF、無 mojibake、無 Unicode 私用區碼點（U+E000–U+F8FF）。

```bash
python .trellis/scripts/check_encoding.py
```

> 任何一欄出現結尾帶 `!` 的標記即為 release blocker，必須修正後重跑。

- [ ] 5.4 **重跑一次真實重現**（同樣操作路徑：登入 → IntegrateView → SaveIntegrate），產生新的三份 Trace，並驗證不變量：

```
Σ request.end.crmCount + Σ bg.end.crmCount == count(crm.op)
```

  以及既有不變量仍成立：acquire 數 == return 數、每 client 最大同時租借數 == 1、
  `callerIdAtReturn` 全空、`leaseStillHeld` 全為 false。

- [ ] 5.5 確認新 Trace 中 `NOSESSION` 出現 0 次。

- [ ] 5.6 更新 `docs/architecture/dataverse-architecture-code-conformance-v1.md` 的「A 上線前必須處理／尚未驗證」清單，把本任務已處理的項目標記為已完成，未處理的保留。

---

## 明確的範圍外項目（不得順手修改）

- `BoundedClientPool` / `DataverseGateway` / `ClientLease` / `PooledClient` 的核心生命週期。
- `IsConnectionFault()` 的分類邏輯（只改文件，不改程式）。
- `appsettings.json` 的明文密碼與 `ToolUtilityClass` legacy credential fallback。
- `ICrmConnectionPool` 相容介面的移除。
- `IMemoryCache` 的 `SizeLimit` 設定。
- `BaseChurchController.cs`、`LineUtilityClass.cs`、`PushUtility.cs`、`PaymentMessageBuilderEncodingTests.cs` 的既有原始碼編碼損毀（另案）。
- D365 伺服器端 `WeeklyReportPlugIn.dll` 缺檔（非程式碼範圍）。

## 已知環境事實

- `.trellis/spec/backend/cross-user-isolation-and-performance.md` 與
  `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` 被 `AGENTS.md` 引用，
  但**檔案並不存在**。不要為了找它們而中斷；依 `AGENTS.md` 正文的規則執行即可。
- `dotnet --version` = 10.0.400。
- 測試以 `dotnet test` 執行；本 repo 無自訂建置腳本。
