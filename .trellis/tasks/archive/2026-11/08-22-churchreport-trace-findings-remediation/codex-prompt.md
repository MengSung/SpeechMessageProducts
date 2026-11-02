# Codex 實作提示詞 — ChurchReport Trace 實測缺陷修復

> 使用方式：把「────」以下的全部內容貼給 Codex。
> 工作目錄必須是
> `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree`
> 分支 `feat/dataverse-scoped-connection`。

────────────────────────────────────────────────────────────

Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

## 你的角色

你是本 repo 的資深 ASP.NET Core 工程師。這個任務**不是探索性開發**：缺陷已經定位完成、
設計已經決定、驗收標準已經寫死。你的工作是把 `implement.md` 的核對清單逐項做完，並用可執行的
指令證明每一項都成立。

## 開工前必須完整讀完（不可跳過、不可只讀摘要）

依序讀：

1. `AGENTS.md`（156 行）— 文件、UTF-8／CRLF、跨使用者隔離與效能要求，全部是 release-blocking 約束。
2. `.trellis/tasks/08-22-churchreport-trace-findings-remediation/prd.md` — 四項缺陷的完整證據。
3. `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md` — 已決定的技術方案。
4. `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md` — 你要執行的核對清單。
5. `.trellis/spec/backend/index.md`、`quality-guidelines.md`、`logging-guidelines.md`。
6. `docs/architecture/dataverse-gateway-v1.md` 與 `docs/architecture/dataverse-architecture-code-conformance-v1.md`。

**環境事實（先告訴你，省得你去找）**：`AGENTS.md` 引用的
`.trellis/spec/backend/cross-user-isolation-and-performance.md` 與
`.trellis/spec/guides/cross-user-isolation-and-performance-review.md`
**這兩個檔案並不存在**。不要為了找它們而中斷或改變計畫，依 `AGENTS.md` 正文的規則執行即可。

## 背景：這些缺陷怎麼來的

2026-08-21 13:48–13:53 對 ChurchReport 做了一次真實操作重現，產出三份 Trace，逐事件重算後：

**連線池核心是好的，不要動它。** 625 次 lease acquire 對應 625 次 return，leaseId 無重複，
每條實體連線最大同時租借數為 1，`callerIdAtReturn` 625/625 為空，`gateway.scope.end` 的
`leaseStillHeld` 恆為 false，連線數 5 分鐘內恆為 2。三個閒置區間的 Managed 記憶體變化為
−6/+0/+0 MB、Handles 為 −32/−14/−26。**沒有 Session Leak，沒有 Memory Leak。**

缺陷全部在產品層、觀測層與文件層。以下四項就是你要修的全部內容。

## 四項缺陷

### F1（P1・正確性）SaveIntegrate 背景任務與前景請求共用可變狀態

`SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
的 `SaveIntegrate` 採 fire-and-forget，在 `Task.Run` 前捕獲的**不是值，是指向 Session 快取
物件圖的參考**：

```csharp
var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
var allMemberData   = weeklyReportRef?.m_SmallGroupDataList?.m_AllMemeberData;
```

背景任務隨後對這個集合就地執行 `RemoveTransferredMembers`。程式自身註解已承認缺少同步機制。

**實測**：`traceId=0HNNV8V1JEM69:00000035` 的背景視窗長 14.2 秒，期間有 **42 個同一使用者的
並行請求**在飛行中，且它們都從同一個 Session 快取鍵取出同一個 `ListManager`。競爭視窗是真的。

方案已定：**短鎖複製 → 長工無鎖 → 短鎖發布**。完整設計見 `design.md` F1 節。
不要改成長時間持鎖（會阻塞 42 個前景請求 14 秒）。

### F2（P1・記憶體）`NOSESSION_` 快取鍵每次呼叫都產生新鍵

`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` 第 229 行：

```csharp
var tempKey = $"NOSESSION_{Environment.MachineName}_{Thread.CurrentThread.ManagedThreadId}_{DateTime.UtcNow.Ticks}";
```

`Ticks` 讓每次呼叫都得到**永遠無法再命中**的新鍵，而該鍵會寫入 `IMemoryCache` 存活 30 分鐘。
六個屬性（`ListManager`、`SmallGroupDataList`、`WeeklyReportData`、`NewPersonModel`、
`PersonalInfomationModel`、`HappyGroupDataManager`）的 getter **每次存取**都會呼叫它。
`Startup.cs:210` 明確不設 `SizeLimit`，所以沒有筆數上限。

方案已定：新增 `TryGetSessionCacheKey(out string key)`，無 Session 時**完全不碰 `IMemoryCache`**，
改回傳實例層級後備物件。完整設計見 `design.md` F2 節。
**不要改 `Startup.cs` 去設 `SizeLimit`**——那會要求每個 `Set` 都提供 `Size`，影響面遠超本任務。

### F3（P2・文件）Gateway 架構文件的 Faulted 不變量已過時

`docs/architecture/dataverse-gateway-v1.md` 核心不變量第 4 條說「任一執行例外都會將 lease 標記
Faulted」。實測 7 次 `crm.op ok=false` 對應的 lease **全部以 healthy 歸還**。

**程式是對的、文件是舊的。** `DataverseGateway.IsConnectionFault()`（第 201 行起）刻意把
`FaultException` 判為不淘汰，只有傳輸層例外才淘汰，XML 註解已完整說明理由與比對順序限制。

**只改文件，不准改 `IsConnectionFault()` 的分類邏輯。** 替換文字見 `design.md` F3 節。

### F4（P2・觀測性）背景工作的 CRM 耗時完全不在 `request.end` 統計內

`request.end` 在 HTTP 管線結束時寫出，fire-and-forget 的工作在那之後才開始：

| traceId | `request.end` 記錄 | 實際背景工作 |
|---|---|---|
| `0HNNV8V1JEM66:00000025` | `durationMs=5, crmCount=0, leaseCount=0` | 62 次 CRM／3,958 ms |
| `0HNNV8V1JEM69:00000035` | `durationMs=0, crmCount=0, leaseCount=0` | 172 次 CRM／14,138 ms |

全域：**625 次 CRM 有 234 次（37.4%）、39,305 ms 有 18,096 ms（46%）不被任何 request 歸因。**

方案已定：在 `ToolUtility/Dataverse/DataverseTrace.cs` 新增
`public IDisposable BeginBackgroundOperation(string operationName)`，寫出 `bg.begin` / `bg.end`
兩個新事件，背景統計獨立累計、不污染父 request。完整設計見 `design.md` F4 節。

**不准把 `request.end` 延後到背景完成**——那會讓「使用者感知延遲」被 14 秒的背景工作污染，
慢請求排名就失去意義了。request 與背景必須是兩個獨立的觀測單位。

## 執行順序（不可調換）

**F3 → F4 → F2 → F1**

- F3 純文件、零風險，先做。
- F4 必須先於 F1：兩者都改 `SaveIntegrate` 的 `Task.Run` 主體，先做 F4 可避免衝突。
- F2 獨立。
- F1 影響面最大，最後做。

每一階段結束建立一個獨立 commit（訊息已在 `implement.md` 各階段末尾給定），作為回滾點。

## 硬性約束

### 編碼（本 repo 已有 4 個檔案存在亂碼前科，這條特別重要）

每一個新增或修改的 `.cs` / `.cshtml` 必須：UTF-8 **無 BOM**、**CRLF** 行尾、檔尾有 CRLF、
**無 mojibake**、**無 Unicode 私用區碼點（U+E000–U+F8FF）**。
在回報完成前必須**逐位元組驗證**，指令見 `implement.md` 第 5.3 項。

### 文件註解

依 `AGENTS.md`：每一個新增或實質修改的區域都要有完整、深入、可維護的**繁體中文**文件。
公開與 internal 型別、建構式、方法、重要屬性都要 C# XML 文件註解。翻譯符號名稱、一行覆述、
單獨 `<inheritdoc />` **不算數**。

涉及 Session、快取、連線池、背景工作、鎖的程式碼，必須明確記載：
**最長資料／資源生命週期、確定性釋放路徑、單一資源擁有者、競態行為、以及如何防止跨 request
與跨使用者洩漏。**

測試的文件要寫清楚：**保護的是哪一條契約、注入了什麼故障、決定性斷言是什麼。**

### 範圍（以下任何一項都不准順手改）

- `BoundedClientPool` / `DataverseGateway` / `ClientLease` / `PooledClient` 的核心生命週期。
- `IsConnectionFault()` 的分類邏輯。
- `appsettings.json` 的明文密碼、`ToolUtilityClass` 的 legacy credential fallback。
- `ICrmConnectionPool` 相容介面的移除。
- `IMemoryCache` 的 `SizeLimit`。
- `BaseChurchController.cs`、`Tools/LineUtilityClass.cs`、`Tools/PushUtility.cs`、
  `ChurchReport.MemberInfo.Tests/Payments/PaymentMessageBuilderEncodingTests.cs`
  的既有編碼損毀（已知問題，另案處理）。
- D365 伺服器端 `WeeklyReportPlugIn.dll` 缺檔（不是程式碼問題）。

`ToolUtility` 是 Host-neutral 共用工具層，**不得參考 ASP.NET Core、`HttpContext`、Session
或任何 Web Hosting 型別**。F4 新增的 API 只接受字串與基本型別。

### 不准做的事

- 不准為了讓測試通過而放寬斷言或標記 skip。
- 不准在沒跑過指令的情況下宣稱「測試通過」或「已驗證」。
- 不准 `git commit --no-verify` 或跳過任何 hook。
- 不准 push，不准建立 PR。commit 之後停下來等待人工確認。

## 驗證（每一項都必須實際執行並貼出輸出）

基準（開工前先跑一次記錄下來）：

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
> 完成後不得低於此數，且必須新增 `implement.md` 要求的測試。

收尾：

```bash
dotnet build SpeechMessageProducts.sln -c Debug --no-incremental
```

```bash
dotnet test SpeechMessageProducts.sln
```

`dotnet --version` = 10.0.400。本 repo 沒有自訂建置腳本。

## 遇到阻礙時

- **F1 的 4.1 步驟**要求先 grep `m_SmallGroupData.Members`、`m_NewPersonFollowUpData.Members`、
  `m_AllMemeberData.Members` 三個字串並**把總數記錄在 `implement.md`**。若超過 30 處，
  改採 `design.md` 中已寫好的「唯讀退路」（背景完全不回寫共用圖），並在 `implement.md` 註明
  採用退路的依據與實際數量。這是預先授權的決策，不需要再問。
- 其他任何需要偏離 `design.md` 的情況：**停下來說明理由並等待指示**，不要自行改變設計。
- 若某一項無法完成，把其餘各項**完整做完**，然後明確說出哪一項沒做、卡在哪裡。
  不要因為一項卡住就縮小整體交付範圍。

## 完成後的回報格式

```
## 完成狀態
F1: 完成 / 未完成（原因）
F2: 完成 / 未完成（原因）
F3: 完成 / 未完成（原因）
F4: 完成 / 未完成（原因）

## 變更檔案
（逐檔列出，每檔一行說明改了什麼）

## F1 定位結果
三個字串的 grep 總數 = N，採用主方案 / 唯讀退路（理由）

## 驗證輸出
（貼上 build 與 test 的實際輸出，含通過數）

## 編碼驗證
（貼上逐位元組檢查的實際輸出）

## 新增測試
（逐項說明：保護哪條契約、注入什麼故障、決定性斷言）

## 未處理事項
（明確列出，含理由）
```

不要宣稱任何沒有實際跑過指令驗證的結論。

────────────────────────────────────────────────────────────
