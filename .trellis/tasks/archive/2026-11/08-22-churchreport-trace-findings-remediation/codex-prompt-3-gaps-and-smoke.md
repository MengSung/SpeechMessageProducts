# Codex 提示詞 3 — 補三個缺口 + 執行煙霧測試

> 使用方式：把「────」以下的全部內容貼給 Codex。
> 工作目錄 `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree`，
> 分支 `feat/dataverse-scoped-connection`。

────────────────────────────────────────────────────────────

Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

## 這一輪的定位

C3／C4 的**程式碼修改已完成、已審查通過、已提交並推送**（`43b33f2b`，24 檔、+1437/−509）。
**不要重做、不要重構、不要改動已提交的程式邏輯。**

本輪只有兩件事：

- **G1～G3**：補上三個文件／紀錄缺口（純文件，不動程式邏輯）
- **G4**：執行煙霧測試（本輪的重點，也是唯一的上線阻斷項）

先讀 `AGENTS.md`（156 行）與本任務的 `prd.md` / `design.md` / `implement.md`。

### 已驗證、不需重新確認的事實

| 項目 | 狀態 |
|---|---|
| `HEAD` | `43b33f2b`，已推送，工作樹乾淨 |
| `ToolUtility.Dataverse.Tests` | 78 / 78 通過（已獨立複驗） |
| `ToolUtility.Tests` | 63 / 63 |
| `ChurchReport.MemberInfo.Tests` | 316 passed / **22 failed**——22 個是既有失敗（`Payments/*NamingTests.cs` 硬編碼找已改名的 `ChurchReport.sln`），**不准修**，數字不得增加 |
| 鎖內是否有 I/O | 已逐一檢查全部 `ExecuteSynchronized` 委派，**無任何 I/O**，設計正確 |
| `CreateIsolatedSnapshot` 是否在鎖內深拷貝 Member | 是。撕裂快照的競態已消除 |
| `.trellis/scripts/verify_trace_invariants.py` | 已更新為可讀 `bg.accepted` / `bg.outcome`，已自我測試通過 |

---

## G1 補上 `CreateIsolatedSnapshot()` 的同步採用現況

### 問題

`SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs` 的
`CreateIsolatedSnapshot()` XML 註解仍寫著：

> 此鎖只界定 SaveIntegrate 的快照建立邊界；既有其他寫入路徑尚未全面採用它，
> 不能把本方法視為整個 legacy 物件圖的全域併發控制機制。

這句話是在**鎖只有自己一個使用者**的時候寫的。C3 完成後已經有十多個寫入端採用同一個同步根，
這段註解現在**嚴重低估自己的成果**，而且沒有告訴讀者「哪些已納入、哪些還沒」。
下一個維護者讀到會誤以為這把鎖沒有作用。

### 要做的事

改寫該註解（以及 `SmallGroupDataList` 型別層級的說明），明確記載三件事。
以下清單是稽核結果，可直接採用，但**請自行 grep 複核後再寫入**，不要照抄未驗證的內容。

**（一）已採用同一同步根的寫入端**

`SmallGroupData`（透過 `AttachSynchronizationRoot` 共用資料圖的 `_syncRoot`）：

- `InsertMember`
- `AddMember`
- `UpdateMember`
- `PopulateObjectAndUpdateEntity`
- `DeleteMember`

`SmallGroupDataList`（透過 `ExecuteSynchronized`）：

- `UpdateSmallGroupAndAllMember`
- `UpdateNewPersonAndAllMember`
- `DeleteMemberFromAllGroups`
- `AddMemberToAllMemberData`
- `RebuildSmallGroupAndNewPersonDataFromAllMembers`
- `RebuildHappyGroupDataFromAllMembers`
- `AddNewPersonToMember`
- `CreateIsolatedSnapshot`

已改為呼叫上述同步 API 的呼叫端：

- `SmallGroupController.Crud.cs`（`UpdateSmallGroupPresentRecord`、`DeletePresentRecord`）
- `NewPersonController.cs`
- `PersonalController.cs`
- `DownloadIntegrateData.Members.cs`
- `DownloadIntegrateData.Setup.cs`（排序與過濾屬結構性寫入，已納入）

**（二）仍未納入的路徑與殘留風險**

- `SmallGroupData.Members` 仍是 `public List<Member> Members { get; set; }`。
  **這把鎖是約定式的，不是強制的**：任何 `x.Members.Add(...)` 或 `x.Members = ...` 都能繞過。
  目前已知的呼叫端都已改掉，但沒有任何機制阻止下一個。
- `UploadIntegrateData.Contact.cs` 的
  `SetAllMemberDataByPersonalReport(..., ref SmallGroupData aSmallGroupData)` 直接 `Members.Add`。
  該路徑作用於**背景快照**（背景工作獨佔），因此目前安全；但若未來有人把共用圖傳進去就會失效。
- 純讀取的列舉路徑（View render、圖表資料組裝等）未取鎖。目前的結構性寫入端都已納入，
  因此列舉時的 `InvalidOperationException` 風險已大幅收斂，但未經證明為零。

**（三）為什麼是「一張資料圖一把鎖」**

`SmallGroupDataList` 建構時把四個 `SmallGroupData`（小組／新人跟進／幸福／全體）
全部 attach 到同一個 `_syncRoot`。要說明這是刻意的：四個集合各自持鎖會在
「同時更新兩個集合」時產生鎖序不一致而死鎖；共用一把鎖直接消除鎖序問題。
同時鎖的粒度是 per-session 資料圖而非全域，不會造成跨使用者序列化。

### 限制

**只改註解與 XML 文件，不准改任何程式邏輯。** 繁體中文，符合 `AGENTS.md` 的文件要求。

---

## G2 補記變更清單與端點簽章變更

### 問題

`43b33f2b` 實際變更 24 個檔案，但任務紀錄沒有完整反映。其中一項是
**公開 HTTP 端點的簽章變更**，未被記錄：

```csharp
- public async Task<IActionResult> UpdateSmallGroupPresentRecord(string key, string values, CancellationToken cancellationToken = default)
+ public IActionResult UpdateSmallGroupPresentRecord(string key, string values)
```

同時刪除了 `catch (OperationCanceledException) → StatusCode(499)`。

移除平行 `Task.Run` 之後這個改動是合理的（已無非同步工作），但它是**線上端點的行為變更**：
前端若依賴 HTTP 499 判斷取消，行為會改變。必須留下紀錄。

### 要做的事

在 `.trellis/tasks/08-24-saveintegrate-background-upload-safety-phase-1/implement.md` 補上：

1. **完整變更檔案清單**（`git show --stat 43b33f2b`），逐檔一行說明改了什麼。
   特別要點名以下在先前回報中被遺漏的項目：
   - 新建 `SaveIntegrateBackgroundUploadRunner.cs`（111 行，`SaveIntegrate` 的背景主體被抽出）
   - `NewPersonController.cs` 移除兩個私有方法，改呼叫同步 API
   - `DownloadIntegrateData.Members.cs` / `DownloadIntegrateData.Setup.cs` 的寫入端納入鎖
   - `SmallGroupData.cs` 加入 `AttachSynchronizationRoot` 與五個方法的鎖
2. **端點簽章變更專節**：記錄舊簽章、新簽章、移除 499 的影響，以及為何這是安全的。
   若前端確實沒有依賴 499，附上 grep 證據；若有，明確標為待處理風險。
3. 檢查前端是否有依賴 499：

```bash
grep -rn "499" --include=*.js --include=*.cshtml SpeechMessageProducts.ChurchReport
```

---

## G3 釐清兩個 Trellis 任務的關係

### 問題

目前有兩個任務並存：

| 任務 | 狀態 |
|---|---|
| `08-22-churchreport-trace-findings-remediation` | `in_progress`，`implement.md` 核取方塊全空，但 F1–F4 其實都做完了 |
| `08-24-saveintegrate-background-upload-safety-phase-1` | 新建，承載 C3／C4 |

兩者是同一條工作線，但沒有任何連結，`08-22` 的 `implement.md` 也沒反映實際進度。

### 要做的事

1. 用 `task.py add-subtask` 把 `08-24` 連結為 `08-22` 的子任務：

```bash
python ./.trellis/scripts/task.py add-subtask .trellis/tasks/08-22-churchreport-trace-findings-remediation .trellis/tasks/08-24-saveintegrate-background-upload-safety-phase-1
```

2. 更新 `08-22/implement.md`：把 F1–F4 已完成的項目打勾，並在收尾檢查區註明
   哪些由 `08-24` 承接、哪些仍未完成（C2 `requiresRefresh`、C5 收尾）。
3. **不要 archive 任何任務**——煙霧測試還沒過，工作尚未結束。

---

## G4（重點）執行煙霧測試

這是本輪唯一的上線阻斷項。需要人工操作瀏覽器與真實 CRM 帳號，你不能單獨完成，照下列分工。

### G4.1 你先做：備份現有 trace

Development 組態的 trace 輸出目錄是 `D:\除錯追蹤`
（`appsettings.Development.json` 的 `DiagnosticsTrace:Enabled = true`、`Directory`）。

把現有三份檔案**改名備份，不要刪除**：

- `dataverse-trace.jsonl` → `dataverse-trace.前次.jsonl`
- `Trace.log` → `Trace.前次.log`
- `CHURCH_REPORT_TRACE.TXT` → `CHURCH_REPORT_TRACE.前次.TXT`

### G4.2 你再做：建置並啟動

```bash
dotnet build SpeechMessageProducts.sln -c Debug --no-incremental
```

```bash
dotnet run --project SpeechMessageProducts.ChurchReport --configuration Debug
```

監聽 `http://localhost:5000`。確認 `Trace.log` 出現 `Application started` 後，
**停下來把控制權交給使用者**。

### G4.3 交給使用者做（把以下文字原樣給他）

1. 開 `http://localhost:5000`，用真實帳號登入。
2. **先進入 `/SmallGroup/MultiGroupView`**（不要直接跳 IntegrateView，理由見下方既有問題 (b)）。
3. 再進入某個小組的整合檢視頁 `/SmallGroup/IntegrateView/{...}`。
4. 修改至少一筆出席紀錄（勾選／取消勾選任一成員的主日或小組出席）。
5. 按「上傳」按鈕，畫面應立即出現「資料已送出，正在背景上傳中...」。
6. **等待至少 30 秒**再關閉程式或做別的事——背景上傳約需 14 秒，提早關掉這次測試就無效。
7. 若時間允許，請對**一般小組**與**幸福小組**各做一次（兩者的上傳分支不同）。

### G4.4 你再做：自動驗證

使用者回報完成後，先停止應用程式，再執行：

```bash
python .trellis/scripts/verify_trace_invariants.py "D:\除錯追蹤"
```

腳本已更新，會逐條印出實際數字並在任一條失敗時以 1 結束。**判定成敗看這三行**：

```
[PASS] 每個 bg.accepted 都有對應的 bg.outcome
[PASS] 上傳階段有明確結果: stage=upload 的結果事件 N 筆
[PASS] 上傳階段成功: 成功 N / N
```

其餘不變量（租約成對、每條連線最大同時租借數 == 1、`callerIdAtReturn` 全空、
`leaseStillHeld` 全 false、CRM 歸因完整、`NOSESSION` 為 0）也會一併驗證。

> **關鍵語意**：`bg.end` 只代表 scope 已釋放——例外照樣會產生它，**不是成功證據**。
> 真正的成功證據是 `stage=upload` 且 `outcome=succeeded` 的 `bg.outcome` 事件。
> 這正是 C4 新增此事件的目的。

### G4.5 仍需人工確認的一件事

腳本能證明「程式認為上傳成功」，但不能證明「CRM 真的收到資料」。
請使用者到 Dynamics 365 確認他剛才改的那筆出席紀錄確實更新了，並回報結果。

### G4.6 把證據存檔

把下列內容存到
`.trellis/tasks/08-24-saveintegrate-background-upload-safety-phase-1/verification/`：

- `verify_trace_invariants.py` 的完整輸出
- 新的三份 trace 檔案副本
- 使用者對 CRM 資料的確認結果

---

## 已知既有問題：遇到不要當成回歸，也不要去修

### (a) `WeeklyReportPlugIn.dll can not be loaded`

D365 伺服器上 `C:\Program Files\Dynamics 365\server\bin\assembly\WeeklyReportPlugIn.dll`
不存在但 plugin 註冊還在，所有 `Update new_group_present_weekly_report` 都會失敗。
**這是伺服器部署問題，不是程式碼問題。**

出席紀錄（`new_present_record`）的上傳不受影響。
**判定煙霧測試成敗時，以出席紀錄是否寫入 CRM 為準，不要以週報是否成功為準。**

若 `bg.outcome` 因此顯示 `stage=upload, outcome=failed`，請在報告中明確區分
「因伺服器缺 DLL 而失敗」與「因本次程式變更而失敗」，不要混為一談。

### (b) `IntegrateView` 的 `ArgumentNullException: Value cannot be null. (Parameter 'source')`

發生在 `Models/ListManager.cs:223`：

```csharp
WeeklyReportRecord aWeeklyReportRecord = m_MultiGroupList.m_WeeklyReportRecordListData.FirstOrDefault(...);
```

`m_WeeklyReportRecordListData` 為 null 時擲出。2026-08-21 就已出現，
`ListManager.cs` **從未被本任務任何提交碰過**。
規避方式已寫進 G4.3 步驟 2（先走 MultiGroupView）。**記錄但不要修。**

### (c) 22 個 payment naming 測試失敗

理由見上方「已驗證事實」。**不准修。**

### (d) 快取過期會得到空白 ListManager

`ListManager` getter 在快取未命中時建立的是 `new ListManager()`（空白），
而非從 CRM 重載；`EnsureCorrectUserData()` 的重載條件要求
`!string.IsNullOrEmpty(listManagerPassword)`，空白物件不滿足。
所以閒置超過 30 分鐘再回來可能看到空白名單。**既有問題，不在本輪範圍。**

---

## 硬性約束

- **不准改動 `43b33f2b` 已提交的程式邏輯。** G1 只改註解，G2／G3 只改文件。
- 不准動 `BoundedClientPool` / `DataverseGateway` / `ClientLease` / `PooledClient`。
- 不准改 `IsConnectionFault()` 的分類邏輯。
- 不准改 `appsettings.json` 明文密碼、`ICrmConnectionPool` 相容介面、`IMemoryCache` 的 `SizeLimit`。
- 不准修上面列出的四種既有問題。
- 不准處理 C2（`requiresRefresh`）——那是下一輪的事。
- 不准 push、不准建立 PR、不准 archive 任務。commit 後停下來等人工確認。
- 不准為了讓測試通過而放寬斷言或標記 skip。
- **不准在沒有實際跑過的情況下宣稱煙霧測試通過。** 若因環境無法啟動、無 CRM 帳號或
  使用者未回應而無法完成 G4，就明確說「G4 未執行」，並把 G1～G3 完整做完。
  不要用單元測試結果替代煙霧測試。

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

基準：`ToolUtility.Dataverse.Tests` 78/78、`ToolUtility.Tests` 63/63、
`ChurchReport.MemberInfo.Tests` 316 passed / 22 failed（22 為既有失敗，不得增加）。

## 回報格式

```
## 完成狀態
G1 快照註解: 完成 / 未完成（原因）
G2 變更清單與端點簽章: 完成 / 未完成（原因）
G3 任務連結與進度: 完成 / 未完成（原因）
G4 煙霧測試: 已執行 / 未執行（原因）

## G1 同步採用現況複核
（貼出你自己 grep 的結果，與提示詞給的清單是否一致；有出入請指出）

## G2 前端是否依賴 HTTP 499
（貼出 grep 輸出）

## G4 煙霧測試結果
是否實際執行：是 / 否
verify_trace_invariants.py 完整輸出：（原樣貼上，不要摘要）
stage=upload 的 outcome：succeeded / failed（failed 請說明是 (a) 伺服器缺 DLL 還是程式問題）
一般小組：測了 / 沒測
幸福小組：測了 / 沒測
使用者確認 CRM 出席紀錄已更新：是 / 否 / 未確認

## 驗證輸出
（build / test / check_encoding 的實際輸出，含通過數）

## 上線建議
可以上線 / 不可以上線（一句話理由）

## 未處理事項
（明確列出，含理由）
```

任何沒有實際跑過指令驗證的結論，一律標記為「未驗證」。

────────────────────────────────────────────────────────────
