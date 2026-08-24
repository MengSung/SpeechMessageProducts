# Codex 提示詞 2B — 上線後清理（工程品質，非阻斷）

> 使用方式：把「────」以下的全部內容貼給 Codex。
> 工作目錄 `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree`，
> 分支 `feat/dataverse-scoped-connection`。
>
> **前提：`codex-prompt-2a-release-gate.md` 已執行完畢且煙霧測試通過。**
> 若 2A 尚未完成，先做 2A，不要做這一份。

────────────────────────────────────────────────────────────

Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

## 這一輪的定位

F1–F4 的程式修改**品質良好、已接受，不要重做、不要重構**。
本輪只處理第一輪留下的五個缺口。它們**都不影響功能正確性**，全部是工程品質、
可維護性與可診斷性——所以可以慢慢做、可以分次做，但每一項都要做完整。

五項彼此獨立，可依此順序執行：**C1 → C2 → C3 → C4 → C5**。
C1 涉及改寫 git 歷史，一定要第一個做，否則後續提交會被捲進去。

先讀 `AGENTS.md`（156 行）與本任務的 `prd.md` / `design.md` / `implement.md`。
編碼契約（UTF-8 無 BOM、CRLF、final CRLF、無 PUA、無 mojibake）與繁體中文 XML 文件要求照舊。

### 不需要重新確認的既有事實

- `ToolUtility.Dataverse.Tests` 71/71、`ToolUtility.Tests` 63/63。
- `ChurchReport.MemberInfo.Tests` 313 passed / **22 failed**；22 個失敗是既有問題
  （`Payments/*NamingTests.cs` 硬編碼找已改名的 `ChurchReport.sln`），**不准修**，數字不得增加。
- 本 repo **沒有** `ChurchReport.Tests/ChurchReport.Tests.csproj`；ChurchReport 的測試放在
  `ChurchReport.MemberInfo.Tests`。第一輪放在該處是正確判斷。
- D365 伺服器缺 `WeeklyReportPlugIn.dll`、`ListManager.cs:223` 的 `ArgumentNullException`
  都是既有問題，不在本任務範圍。

---

## C1（先做）重整提交 `71b42c31`

### 問題

`71b42c31` 的訊息是「修正 DataverseTrace 缺少 BeginBackgroundOperation 導致測試無法編譯」，實際內容是：

- 187 個檔案、6,698 行新增
- **完全沒有觸碰 `ToolUtility/Dataverse/DataverseTrace.cs`**——該方法早在 `774a0587` 就加好了
- 唯一的原始碼變更是 `ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs`（6 行）
- 其餘 186 個檔案全是 `.ccg/dual-model-runs/` 與 `.trellis/` 的執行產物

而且那 6 行的真實內容是：

```diff
- Assert.Equal(52, CountMatches(contextSource, @"WriteSessionDiagnostic\("));
+ Assert.Equal(51, CountMatches(contextSource, @"WriteSessionDiagnostic\("));
- Assert.Equal(21, ... @"\[GetCurrentSessionId\]"));
+ Assert.Equal(20, ... @"\[TryGetSessionCacheKey\]"));
```

這是 **F2 把 `GetCurrentSessionId` 改名為 `TryGetSessionCacheKey` 所必須的配套修正**，
本來就屬於 `3bf57fce`。

**後果**：在 `3bf57fce`（F2）與 `3a7fdf9d`（F1）這兩個提交當下，
`ToolUtility.Dataverse.Tests` 是失敗的（斷言不符，不是編譯失敗——commit 訊息這點也寫錯了），
要到 `71b42c31` 才恢復綠燈。「四個獨立 commit 各自是乾淨回滾點」這個保證因此不成立。

### 要做的事

1. 確認尚未 push（`git log origin/feat/dataverse-scoped-connection..HEAD` 應包含這五個提交）。
   若已 push，**停下來問**，不要強制改寫遠端歷史。
2. 把 `SessionDiagnosticsSwitchTests.cs` 那 6 行併回 `3bf57fce`（F2），
   讓 F2 這個提交自身測試是綠的。可用互動式 rebase 之外的方式達成——
   **注意本環境不支援 `git rebase -i`**，請改用
   `git reset --soft` + 重新提交，或 `git cherry-pick` 重建的方式。
3. `.ccg/dual-model-runs/` 的 186 個檔案：判斷是否應進版控。
   - 多數 AI 執行產物不應進版控 → 加入 `.gitignore`，單獨提交
     `chore: 忽略 CCG 雙模型執行產物`。
   - 若專案有意保留這些稽核紀錄 → 用誠實訊息單獨提交
     `chore: 保存 2026-08-22 CCG 雙模型執行產物`。

   **選哪一個要先看專案既有慣例**（檢查 `.gitignore` 現況與 `.ccg/` 的歷史提交模式），
   不要自己決定就動手；若慣例不明確，停下來問。
4. 重建後每個提交都必須：訊息描述實際內容，且該提交當下 `dotnet test ToolUtility.Dataverse.Tests`
   為綠。逐一 checkout 驗證並貼出結果。
5. **不得改動** `d3a17f54`（F3）與 `774a0587`（F4）的內容。

---

## C2 `requiresRefresh` 目前沒有任何消費者

### 問題

第一輪在 `SaveIntegrate` 的回應加了 `requiresRefresh = true`，
但掃過所有 `.js` / `.cshtml` / `.ts`，**零個讀取端**。

這造成一個使用者看得到的行為變化：

- **改動前**：背景清理就地改寫 Session 快取的 `Members`，上傳完成後的後續請求
  看得到「已轉出成員被移除」。
- **改動後**：清理只作用於隨 lambda 丟棄的副本，Session 快取的清單不再被清理。

而且 `SetupListManager`（真正從 CRM 載入的方法）**只在登入時呼叫一次然後快取**
（見 `AuthenticationController.Private.cs:285` 的註解「避免後續 AJAX 請求重複呼叫 SetupListManager」），
後續請求都吃快取、不回 CRM 重抓。

被移除的是 `ShouldRemoveMember` 判定的兩種人：`AssignedGroup` 有值（已指派到別組）、
`FollowUpNextStep == "轉介"`。所以組長會看到自己已經轉出的成員仍留在名單上，
直到 30 分鐘快取絕對過期、重新登入、或切換日期（`SmallGroupController.Date.cs` 會重新
呼叫 `SetupListManager`）。

需要說明的是：**改動前的行為也不乾淨**——那是背景執行緒無鎖改寫共用集合，
同時有數十個請求在讀。所以這是「即時但有競態」換成「安全但過時」，方向對，只是停在一半。

### 要做的事

三選一，選定後把決策與理由寫進 `implement.md`：

- **(a) 讓快取失效（建議）**
  背景上傳成功後，移除該 Session 的 `ListManager` 等快取項，讓下一個請求重新從 CRM 載入。
  **關鍵限制**：快取鍵必須在 request 執行緒上、進入 `Task.Run` 之前就先取好並捕獲成區域變數，
  **絕對不可以在背景執行緒讀 Session 或 HttpContext**——那正是 F1 要消除的東西。
  另外要確認：移除快取項時是否有並行請求正在讀該項，會不會取到半初始化狀態。
  這個選項同時拿到隔離性與即時性，是三者中最完整的。

- **(b) 接上前端**
  在 `Views/Home/IntegrateView.cshtml` 的 `success` 分支讀 `response.requiresRefresh`，
  以配合實測上傳時間（約 14 秒）的延遲觸發重新載入。
  注意現行 JS 已有 `setTimeout(function(){ grid.refresh(); }, 1000)`，
  1 秒遠早於上傳完成，直接沿用沒有意義，必須另外設計。

- **(c) 移除欄位**
  確認此行為變化可接受，刪掉 `requiresRefresh`，並在 `SaveIntegrate` 的 XML 註解
  明確記載「背景清理不影響目前 Session 的顯示清單，需重新登入或切換日期才會更新」。

**三選一，不要三個都做，也不要留一個沒人讀的欄位。**

---

## C3 `SmallGroupDataList.SyncRoot` 是一把沒有第二個使用者的鎖

### 問題

全 repo 掃描結果：

```
SmallGroupDataList.cs:44  internal object SyncRoot => _syncRoot;   ← 唯一宣告，零個外部呼叫端
SmallGroupDataList.cs:85  lock (_syncRoot)                          ← 唯一取鎖處
```

`CreateIsolatedSnapshot()` 自己取鎖，但**沒有任何前景寫入端取同一把鎖**
（`implement.md` 步驟 4.6 因改採唯讀退路而被跳過）。

結果是競態只解決了一半：

| | 改動前 | 改動後 |
|---|---|---|
| 競態視窗 | **14 秒**（背景整個上傳期間） | **毫秒級**（只有建立快照那一瞬間） |
| 失敗方式 | 靜默的資料錯亂／讀到半清空清單 | 擲 `InvalidOperationException`，fail-closed |
| 觸發條件 | 背景改寫 + 前景讀 | 前景改寫 + 建快照讀 |

**改動後的風險嚴格小於改動前**，而且是 fail-closed（使用者看到錯誤、重按一次即可，
不會寫壞資料）。所以這一項是收斂殘留風險，不是修 bug。

第一輪在註解裡誠實寫了「既有其他寫入路徑尚未全面採用它」——這點是對的，
本項要做的就是把這句話變成不再需要寫。

### 要做的事（範圍嚴格限定，不要擴散到全部 44 個使用點）

1. 定位實測會與背景視窗並行的那三條路徑上、**實際會改寫**
   `m_SmallGroupData.Members` / `m_NewPersonFollowUpData.Members` / `m_AllMemeberData.Members`
   的位置：`UpdateSmallGroupPresentRecord`、`AssignSmallGroupGet`、`GetMultiGroupChartDataList`。
   以 `SmallGroupController` 與 `InMemoryDataContextSmallGroup` 為起點回溯。
2. 只把這些**寫入端**改為在 `lock (SyncRoot)` 內完成集合的新增／移除／整份替換。
   **臨界區內不得有 CRM 呼叫、網路或任何 I/O**，只能有記憶體操作。
3. 若某個寫入端的臨界區無法縮到純記憶體操作（例如它在迴圈中夾雜 CRM 查詢），
   **不要硬套鎖**——改為先在鎖外組出新的 `List<Member>`，再於 `lock` 內以整份替換參考的方式
   發布。這同時讓讀取端永遠看到完整清單。
4. `CreateIsolatedSnapshot()` 加一道保險：即使有遺漏的寫入端，快照建立也不該讓整個背景上傳失敗。
   以「最多重試 3 次、捕捉 `InvalidOperationException`」包住列舉，三次都失敗才向上擲出。
   註解必須寫明它是**縱深防禦，不是正確性來源**，正確性來自步驟 2 的鎖。
5. 更新 `CreateIsolatedSnapshot()` 的 XML 註解：記錄實際採用了哪些寫入端、哪些仍未採用、
   以及未採用者的殘留風險。

### 新增測試

一條前景執行緒持續以**已取鎖的寫入端**改寫集合，另一條持續呼叫 `CreateIsolatedSnapshot()`，
各 ≥1000 次，斷言不擲出例外且每份快照的成員數都是合法值（不是半完成狀態）。

---

## C4 背景例外只記錄型別名稱，診斷資訊不足

### 問題

第一輪把 `SaveIntegrate` 的背景 catch 從

```csharp
$"[SaveIntegrate] 背景上傳失敗: {ex.Message}"   +   ex.StackTrace
```

改成

```csharp
$"[SaveIntegrate] 背景上傳失敗: {ex.GetType().Name}"
```

避免把成員資料寫進診斷檔的用意正確，但這次矯枉過正：本任務要追的實際失敗是

```
FaultException`1[OrganizationServiceFault]: Assembly WeeklyReportPlugIn.dll can not be loaded
```

**所有商業層 fault 的型別名稱都是 `FaultException\`1`**，只記型別等於什麼都沒記。

而且同一個方法最外層的 `catch (Exception e)` 仍記錄 `e.Message`，前後不一致。

（緩解事實：完整錯誤仍會由 ToolUtility 自己的錯誤處理器寫進
`CHURCH_REPORT_TRACE.TXT`，所以資訊沒有真的丟失，只是要多繞一個檔案。）

### 要做的事

1. 加一個集中的例外摘要輔助方法，輸出：例外型別 + **經過清理的**訊息 +
   最內層 `InnerException` 的型別與訊息。
2. 清理規則要明確且有註解：
   - **遮罩**：GUID、電子郵件、電話號碼、身分證字號等模式。
   - **保留**：CRM ErrorCode（例如 `0x80044191`、`0x80040216`）與伺服器端訊息
     （例如組件檔名、欄位名稱）。`OrganizationServiceFault` 的訊息本身不含成員個資，
     正是本任務最需要的診斷資訊。
3. `SaveIntegrate` 的背景 catch、背景清理 catch、最外層 catch **一律改用同一個輔助方法**，
   消除目前的不一致。
4. 為輔助方法寫測試：注入含 GUID 與電子郵件的例外訊息，斷言輸出已遮罩；
   注入 CRM ErrorCode 與組件名稱，斷言這些**保留**。

---

## C5 Trellis 收尾

1. 依 C1–C4 的實際結果更新 `implement.md`，把已完成項目打勾，未完成的註明原因。
2. 更新 `docs/architecture/dataverse-architecture-code-conformance-v1.md` 的
   「A 上線前必須處理／尚未驗證」清單：本任務已處理的標記完成，未處理的保留。
   特別注意其中「產品層仍有 legacy `Task.Factory.StartNew` fire-and-forget 路徑，
   沒有統一 host queue、取消、drain 與完成等待」這一項**仍未處理**，不可標記完成。
3. 依 `.trellis/workflow.md` 的 Phase 3 執行 spec update 與收尾。
4. **不要 push、不要建立 PR。** 全部完成後停下來等人工確認。

---

## 硬性約束

- 不准動 `BoundedClientPool` / `DataverseGateway` / `ClientLease` / `PooledClient` 核心生命週期。
- 不准改 `IsConnectionFault()` 的分類邏輯。
- 不准改 `appsettings.json` 明文密碼、`ToolUtilityClass` legacy credential fallback、
  `ICrmConnectionPool` 相容介面、`IMemoryCache` 的 `SizeLimit`。
- 不准修 `BaseChurchController.cs` 等 4 個檔案的既有編碼損毀（另案）。
- 不准修那 22 個既有的 payment naming 測試失敗，也不准修 `ListManager.cs:223` 的
  `ArgumentNullException`（既有 bug，另案）。
- 不准為了讓測試通過而放寬斷言或標記 skip。
- `ToolUtility` 不得參考 ASP.NET Core、`HttpContext`、Session 或任何 Web Hosting 型別。
- 不准在沒實際跑過指令的情況下宣稱「已驗證」。
- 遇到需要偏離本文件設計的情況：**停下來說明理由並等待指示**。

---

## 驗證指令

```bash
dotnet build SpeechMessageProducts.sln -c Debug --no-incremental
```

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

```bash
python .trellis/scripts/check_encoding.py
```

C3 完成後建議重跑一次煙霧測試（步驟見 `codex-prompt-2a-release-gate.md` 任務 3），
並以下列指令驗證不變量：

```bash
python .trellis/scripts/verify_trace_invariants.py "D:\除錯追蹤"
```

---

## 回報格式

```
## 完成狀態
C1 提交重整: 完成 / 未完成（原因）
C2 requiresRefresh: 選了 (a)/(b)/(c) + 理由
C3 SyncRoot 採用: 完成 / 未完成（原因）
C4 例外摘要: 完成 / 未完成（原因）
C5 Trellis 收尾: 完成 / 未完成（原因）

## C1 提交結構
（重建後的提交清單；每筆的訊息、實際內容、以及該提交當下的測試結果）

## C3 採用 SyncRoot 的寫入端
（逐一列出 檔案:行號；並列出仍未採用者與其殘留風險）

## 驗證輸出
（build / test / check_encoding 的實際輸出，含通過數）

## 新增測試
（逐項說明：保護哪條契約、注入什麼故障、決定性斷言）

## 未處理事項
（明確列出，含理由）
```

任何沒有實際跑過指令驗證的結論，一律標記為「未驗證」。

────────────────────────────────────────────────────────────
