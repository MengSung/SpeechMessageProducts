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

> **⚠️ 已知狀態：這個分支已經 push 到 origin 了。**
> `git status -sb` 顯示本機與 `origin/feat/dataverse-scoped-connection` 完全同步，
> 五個提交（含 `71b42c31`）都已在遠端。
>
> 因此重寫歷史需要 `git push --force-with-lease`。
> **除非使用者明確授權 force push，否則本項只做步驟 3（`.gitignore`），跳過步驟 1、2、4。**
> 詢問時要講清楚：若有其他人已經 pull 過這個分支，force push 會造成他們的本機歷史分岔。

1. 先確認使用者是否授權 force push。未授權就只做步驟 3。
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

### ⛔ 絕對不可以這樣做：移除快取項

一個看起來很自然的解法是「背景上傳成功後把 `ListManager` 的快取項移除，讓下一個請求重新載入」。
**這會造成比過時資料嚴重得多的後果，禁止採用。**

理由在 `InMemoryDataContextSmallGroup.cs` 的 `ListManager` getter：

```csharp
if (_memoryCache.Get(key) == null)
{
    ...
    m_ListManager = new ListManager();          // ← 快取未命中時建立的是「空白物件」
    _memoryCache.Set<ListManager>(key, m_ListManager, options);
    SetSessionDirtyFlag();
}
return _memoryCache.Get<ListManager>(key);
```

快取未命中時建立的是**空白 `ListManager`，不會從 CRM 重新載入**。
真正從 CRM 載入的是 `SetupListManager()`，而它只在登入
（`AuthenticationController.Private.cs:275`）與切換日期
（`SmallGroupController.Date.cs:87,134`）時被呼叫。

所以移除快取項＝**把組長整個 session 的資料清空**，畫面會變成空白名單。

另外提醒：`SetSessionDirtyFlag()` 寫入的 `session.SetInt32("dirty", 1)`
**全 repo 沒有任何讀取端**（已掃描確認），是唯寫死碼，不可以拿來當重載機制。

### 要做的事

二選一，選定後把決策與理由寫進 `implement.md`：

- **(c) 接受行為變化並誠實記錄（建議）**
  刪掉沒有消費者的 `requiresRefresh` 欄位，或把它改成純提示用途——
  在前端 `success` 分支顯示「資料已上傳，名單將於重新登入或切換日期後更新」之類的文字，
  不做任何自動重載。同時在 `SaveIntegrate` 的 XML 註解明確記載：
  「背景清理只作用於背景副本，不影響目前 Session 的顯示清單。」

  理由：CRM 是真相來源且資料正確；改動前的『即時清理』本身帶著 14 秒的無鎖競態，
  並不是一個值得復原的可靠行為。用一句誠實的提示換掉一個不可靠的副作用是合理取捨。

- **(d) 背景完成後重新從 CRM 載入**
  背景上傳成功後，在背景自己的 DI scope 內重新執行等同 `SetupListManager` 的載入，
  再以整份替換的方式更新快取項。

  **這個選項工程量遠大於 (c)**，且必須解決三個問題：載入所需的參數要在 request 執行緒
  先捕獲（不可在背景讀 Session）、重載期間前景請求讀到的是舊物件還是新物件要有明確語意、
  以及重載本身失敗時不可讓 session 變成空白。
  **除非使用者明確要求即時更新，否則不要選這個。**

**二選一，不要兩個都做，也不要留一個沒人讀的欄位。**

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
| 結構性競態（新增／移除元素） | 有，且會擲 `InvalidOperationException` | 有，但視窗極短 |
| **欄位層競態（靜默）** | 有 | **仍然有** |
| 觸發條件 | 背景改寫 + 前景讀 | 前景改寫 + 建快照讀 |

### ⚠️ 這不是 fail-closed：主要失敗模式是「靜默的半新半舊快照」

`SmallGroupData.UpdateMember(key, values)` **只修改既有 `Member` 的欄位，不改變
`List<Member>` 的結構**：

```csharp
Member aUpdatedMember = Members.DefaultIfEmpty(null).FirstOrDefault(o => o.PresentRecordId == key);
aUpdatedMember.ModifyFlag = true;
// ...接著以 JSON 反序列化逐一寫入欄位
```

（注意第 54 行有一段被註解掉的 `//lock (m_MemberDataLocker)`——曾經有鎖，後來被拿掉了。）

而 `Member` 的複製建構式是**逐一指派 48 個欄位**。所以：

> 當 `CreateIsolatedSnapshot()` 正在複製某個 `Member` 的 48 個欄位時，
> 若 `UpdateMember` 同時改寫同一個 `Member`，會得到**一半新值、一半舊值**的副本，
> **而且不會擲出任何例外**——因為集合結構沒變。

這份半新半舊的快照接著會被上傳到 CRM。**結果是 CRM 裡出現使用者從未輸入過的組合**
（例如主日出席是新值、小組出席是舊值）。

更麻煩的是 `UpdateSmallGroupPresentRecord` 本身用兩個**平行** `Task.Run` 同時改
`m_SmallGroupData` 與 `m_AllMemeberData`（`SmallGroupController.Crud.cs:79`），
所以連前景自己內部都沒有一致性保證。

**因此不可以用「搜尋 log 有沒有 `InvalidOperationException`」來判斷要不要做這一項**——
主要失敗模式根本不產生例外。

第一輪在註解裡誠實寫了「既有其他寫入路徑尚未全面採用它」——這句話比它看起來更重要。

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
C2 requiresRefresh: 選了 (c)/(d) + 理由（絕不可移除快取項）
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
