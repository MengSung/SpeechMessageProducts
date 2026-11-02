# Codex 提示詞 2A — 上線前把關（唯一必做）

> 使用方式：把「────」以下的全部內容貼給 Codex。
> 工作目錄 `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree`，
> 分支 `feat/dataverse-scoped-connection`。
>
> 這一份的目的**只有一個**：確認 `SaveIntegrate` 這個功能在大改之後還會動。
> 其他所有工程品質項目在 `codex-prompt-2b-cleanup.md`，不要在這一份裡做。

────────────────────────────────────────────────────────────

Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

## 為什麼有這一份

F1–F4 四項修復已完成並提交（`d3a17f54`、`774a0587`、`3bf57fce`、`3a7fdf9d`），程式碼品質已審查通過。
但 `SaveIntegrate` 這條路徑被大幅改寫：

| 環節 | 改動前 | 改動後 |
|---|---|---|
| 傳給上傳器的物件 | Session 快取實物 | 深拷貝副本（`CreateBackgroundUploadCopy`） |
| DI scope 解析 | 繼承的 request scope | 背景 scope override（`BeginBackgroundScope`） |
| Trace 關聯 | 沿用父 request | 獨立 `bg` scope |
| 清理目標 | 共用集合 | 副本 |

四個環節同時換掉，而且：

- 全 repo **沒有任何測試檔案提到 `SaveIntegrate`**（已掃描確認）。新增的測試測的是零件
  （`CreateIsolatedSnapshot`、`Member` 複製建構式、快取行為），不是整條路徑。
- `D:\除錯追蹤\dataverse-trace.jsonl` 目前最新一次執行是 2026-08-22 15:58–15:59，
  該次共 239 筆事件、22 個 request、30 次 CRM 操作，**完全沒有 `bg.begin` / `bg.end`**——
  代表那次執行根本沒碰到 `SaveIntegrate`。

**結論：這個功能自從被改寫之後一次都沒有被真正執行過。** 本份提示詞就是要補上這一次。

---

## 任務 1（先做，1 分鐘）修正一處自相矛盾的註解

`SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs`
的 `CreateIsolatedSnapshot()` 內：

```csharp
// 幸福小組集合不在 SaveIntegrate 的上傳／清理資料流；保留建構式建立的
// 空白背景物件，避免把非必要的會員資料延長到背景工作生命週期。
m_AllMemeberData = CloneSmallGroupData(m_AllMemeberData)
```

註解說「保留空白物件」，程式碼實際上做的是完整深拷貝。而且 `m_AllMemeberData` **確實**
會被傳給上傳器：

```csharp
await backgroundCopy.UploadIntegrateDataAsync(
    ..., backgroundCopy.m_SmallGroupDataList.m_AllMemeberData, ...);
```

所以**程式碼是對的，註解是錯的**。`AGENTS.md` 明文把
「materially misleading/out-of-date comments」列為 review／release blocker。

把註解改成描述實際行為：`m_AllMemeberData` 是上傳器的主要輸入，必須深拷貝，
且副本的生命週期只到背景 lambda 結束。

**只改註解，不要改程式碼邏輯。**

---

## 任務 2 建置與既有測試（確認沒退步）

```bash
dotnet build SpeechMessageProducts.sln -c Debug --no-incremental
```

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

```bash
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

```bash
python .trellis/scripts/check_encoding.py
```

基準（不得低於）：

| 專案 | 期望 |
|---|---|
| `ToolUtility.Dataverse.Tests` | 71 / 71 |
| `ToolUtility.Tests` | 63 / 63 |
| `ChurchReport.MemberInfo.Tests` | 313 passed / **22 failed** |

那 22 個失敗是**既有問題**：`Payments/*NamingTests.cs` 與 `*DefaultsTests.cs` 硬編碼尋找
`ChurchReport.sln`，而該檔已改名為 `SpeechMessageProducts.sln`。
**不准修它們**，也不准把它們算成本次的退步。數字必須正好是 22，變多才是問題。

---

## 任務 3（核心）實際執行一次煙霧測試

這一步需要人工操作瀏覽器與真實 CRM 帳號登入，你不能單獨完成。照下列分工執行。

### 3.1 你先做：清空並備份現有 trace

Development 組態的 trace 輸出目錄是 `D:\除錯追蹤`
（`appsettings.Development.json` 的 `DiagnosticsTrace:Directory`）。

把現有三份檔案改名備份（**不要刪除**）：

- `dataverse-trace.jsonl` → `dataverse-trace.前次.jsonl`
- `Trace.log` → `Trace.前次.log`
- `CHURCH_REPORT_TRACE.TXT` → `CHURCH_REPORT_TRACE.前次.TXT`

### 3.2 你再做：啟動應用程式

```bash
dotnet run --project SpeechMessageProducts.ChurchReport --configuration Debug
```

環境為 Development，監聽 `http://localhost:5000`。
確認 `Trace.log` 出現 `Application started` 後，**停下來把控制權交給使用者**。

### 3.3 交給使用者做：在瀏覽器操作

明確告訴使用者要做這五步（照抄以下文字給他）：

1. 開 `http://localhost:5000`，用真實帳號登入。
2. 進入某個小組的整合檢視頁（`/SmallGroup/IntegrateView/{...}`）。
3. 修改至少一筆出席紀錄（勾選／取消勾選任一成員的主日或小組出席）。
4. 按「上傳」按鈕。畫面應立即出現「資料已送出，正在背景上傳中...」。
5. **等待至少 30 秒**再關閉程式或做其他事——背景上傳需要約 14 秒，提早關掉會讓這次測試無效。

### 3.4 你再做：分析結果

使用者回報操作完成後，先停止應用程式，然後執行：

```bash
python .trellis/scripts/verify_trace_invariants.py "D:\除錯追蹤"
```

這個腳本會逐條印出實際數字並在任一條失敗時以 1 結束。它檢查：

| 不變量 | 意義 |
|---|---|
| `Σ request.end.crmCount + Σ bg.end.crmCount == count(crm.op)` | F4 歸因完整，沒有孤兒背景工作 |
| `bg.begin` / `bg.end` 成對且 `parentTraceId` 有效 | F4 背景範圍正確 |
| 至少有一筆 `bg.end` | **本次重現有效**——沒有就代表沒碰到 SaveIntegrate，要重做 |
| acquire 數 == return 數、leaseId 無重複 | 租約無洩漏 |
| 每條連線最大同時租借數 == 1 | 沒有兩個請求共用同一條實體連線 |
| 結束時無未歸還租約 | 租約沒有逃出邊界 |
| `callerIdAtReturn` 全為空 | 身分未跨請求殘留 |
| `gateway.scope.end.leaseStillHeld` 全為 false | 租約由正常路徑歸還，不是靠 DI 救回 |
| `Trace.log` 的 `NOSESSION` 次數 == 0 | F2 生效 |

### 3.5 額外必須人工確認的三件事（腳本測不到）

1. **`Trace.log` 出現「背景上傳完成」而不是「背景上傳失敗」**

   ```bash
   grep -n "背景上傳完成\|背景上傳失敗\|背景清理失敗" "D:\除錯追蹤\Trace.log"
   ```

2. **`bg.end` 的 `crmCount` 是合理的量級**（舊 trace 的兩次分別是 62 與 172；
   個位數代表背景工作在很早期就中斷了）。

3. **CRM 裡真的有資料**：請使用者確認他剛才改的那筆出席紀錄，在 Dynamics 365 中確實更新了。
   這是唯一能證明「整條路徑真的通」的檢查，腳本無法代替。

---

## 已知的既有問題：看到這三種錯誤不要當成回歸，也不要去修

煙霧測試過程中一定會遇到，先講清楚免得誤判：

### (a) `WeeklyReportPlugIn.dll can not be loaded`

D365 伺服器上 `C:\Program Files\Dynamics 365\server\bin\assembly\WeeklyReportPlugIn.dll`
不存在，但 plugin 註冊還在。所有 `Update new_group_present_weekly_report` 都會失敗。

**這是伺服器部署問題，不是程式碼問題**，四個修復沒有也不該碰它。
出席紀錄（`new_present_record`）的上傳不受影響，仍應成功。
判斷煙霧測試成敗時，**請以出席紀錄是否寫入 CRM 為準**，不要以週報是否成功為準。

### (b) `IntegrateView` 的 `ArgumentNullException: Value cannot be null. (Parameter 'source')`

發生在 `Models/ListManager.cs:223`：

```csharp
WeeklyReportRecord aWeeklyReportRecord = m_MultiGroupList.m_WeeklyReportRecordListData.FirstOrDefault(e => e.ListEntityId == ListEntityId);
```

`m_WeeklyReportRecordListData` 為 null 時 `FirstOrDefault` 會擲出。這在 2026-08-21 14:12:35
就已經出現過，而 `ListManager.cs` **完全沒有被四個修復提交碰過**（已用 git log 確認）。

**這是既有 bug，不在本任務範圍。** 若煙霧測試踩到它，請使用者先走
`/SmallGroup/MultiGroupView` 再進 `IntegrateView`，讓 `m_MultiGroupList` 先被填好。
把這件事記錄下來，但不要在這一份任務裡修。

### (c) 那 22 個 payment naming 測試失敗

理由見任務 2。不准修。

---

## 硬性約束

- **本份提示詞只做任務 1、2、3。** 不要順手做提交整理、不要動 `SyncRoot`、
  不要改 `requiresRefresh`、不要改例外記錄方式——那些全在 2B。
- 不准修上面列出的三種既有問題。
- 不准為了讓測試通過而放寬斷言或標記 skip。
- 不准 push、不准建立 PR。
- **不准在沒有實際跑過的情況下宣稱煙霧測試通過。**
  如果因為沒有 CRM 帳號、環境無法啟動或使用者未回應而無法完成任務 3，
  就明確說「任務 3 未執行」，不要用單元測試結果替代。

---

## 回報格式

```
## 任務 1 註解修正
完成 / 未完成（原因）

## 任務 2 建置與既有測試
build: 0 warnings / 0 errors？（貼實際輸出）
ToolUtility.Dataverse.Tests: __ / __
ToolUtility.Tests: __ / __
ChurchReport.MemberInfo.Tests: __ passed / __ failed（必須正好 22 failed）
check_encoding.py: （貼實際輸出）

## 任務 3 煙霧測試
是否實際執行：是 / 否（否的話原因是什麼）
verify_trace_invariants.py 輸出：（完整貼上，不要摘要）
Trace.log 背景上傳結果：完成 / 失敗（貼 grep 輸出）
bg.end 的 crmCount：__
使用者確認 CRM 資料已更新：是 / 否 / 未確認

## 遇到的既有問題
（列出踩到 (a)(b)(c) 哪幾項，不要修）

## 上線建議
可以上線 / 不可以上線（一句話理由）
```

────────────────────────────────────────────────────────────
