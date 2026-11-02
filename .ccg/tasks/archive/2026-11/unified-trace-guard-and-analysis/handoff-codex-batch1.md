# Codex 交接提示詞 — 第一批（B0 + B1）

> 使用方式：把下面分隔線以下的全部內容貼給 Codex。
> 完成後把它產出的報告貼回 Claude 審核。

---

## 這批任務的目標（先讀這段）

**這批任務既不是改善效能，也不是寫測試 —— 是「修好量測工具」。**

最終目標是讓 `/Home/ProcessLogin` 從 3,774ms 變快。但現在還不到動手優化那一步，因為目前的數字不可信。

### 為什麼不可信

現有 trace 給出這個結論：

```
/Home/ProcessLogin                        3,774ms
  Login.ValidateUserCredentials           1,946ms   ← 看起來這裡最慢
```

但同一份 trace 裡還有這個事實：`Trace.log` 11,732 行中 89.5% 是除錯訊息，而 `AutoFlush = true` 讓每一行都成為**請求執行緒上的同步磁碟寫入** —— 94 秒內 10,495 次。

所以那 1,946ms 裡面，有多少是它真的在做事，有多少是它在寫「我正在做事」的 log？**現在分不出來。**

這直接決定後續要做什麼：

| 若實際是 | 該做的事 |
|---|---|
| 1,946ms 裡有 1,500ms 是寫 log | 優化 `ValidateUserCredentials` 是白費力氣，關掉 log 就好 |
| 1,946ms 裡只有 100ms 是寫 log | 真的有 1,800ms 的問題要查 |

兩個結論的行動完全相反，所以量測必須先修好。

### 並且：量測工具本身可能就是病因

`AutoFlush = true` 代表每個 `Debug.WriteLine` 都會阻塞請求執行緒等磁碟。10,495 次同步 I/O 發生在請求路徑上。

因此 B1 不只是「修儀器」—— **它有相當機會直接就是效能修復**。這是待驗證的假設，而 B1 正好是驗證它的實驗。

### 兩項任務各自的定位

| 任務 | 定位 | 與效能的關係 |
|---|---|---|
| **B0** 分析器收回版控 | 純風險排除，零功能改動 | 無關。但第 ③ 步要用它，必須先是對的版本 |
| **B1** 降噪 + 關 AutoFlush | 讓數字可信 | 可能直接就是修復 |

### 完整迴圈（你在第 ① 步）

```
① Codex 改程式            ← 你在這裡
        ↓
② 人工跑一次登入           產生新 trace（可能需要專案負責人執行）
        ↓
③ 分析器讀新 trace         產出新報告
        ↓
④ 報告交回審核             比對 1,946ms → 變成多少
        ↓
⑤ 差額 = 量測本身的成本
        ↓
   差額大 → 效能問題主要是 trace 造成，後續計畫要改
   差額小 → 真有 1,800ms 要查，進入下一批的細部歸因
```

**這批任務的最終產出不是「改好了」，而是一個數字** —— 那個數字決定下一批要做什麼。所以驗收時的實測對照（第 3.6 節）是整份報告最重要的部分；拿不到就如實標明「待人工重跑」，**絕對不要估算或偽造**。

---

## 環境

- 工作目錄：`D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree`
- 分支：`feat/dataverse-scoped-connection`（這是 git worktree，不是主 repo）
- Shell：Windows PowerShell 5.1 為主；PowerShell 7 亦可用
- 編碼規範（`.editorconfig` 已定義，必須遵守）：**UTF-8 without BOM、CRLF、檔尾留空行**
- 專案為 .NET 10，`dotnet` 可用

## 先讀這份規劃

**開始動手前，先完整讀過：**

```
.ccg\tasks\unified-trace-guard-and-analysis\next-steps.md
```

那份文件說明了整體脈絡、為什麼是這個順序、以及每一項的驗收標準。你這次只做其中的 **B0** 與 **B1** 兩項，其他項目（B2–B7）**不要碰**。

若你發現 `next-steps.md` 的描述與實際程式碼不符，**不要自行修正程式碼**，把差異寫進報告的「發現的落差」欄位。

---

## 任務 B0 — 把分析器收回版控

### 背景

更新後的分析器目前只存在於 repo 外，沒有任何版本歷史，這是唯一「什麼都不做就可能歸零」的風險。

| | 路徑 | 行數 | 位元組 |
|---|---|---:|---:|
| 來源（新，實際在用） | `D:\除錯追蹤\PowerShell\Analyze-ChurchReportTraces.ps1` | 1,280 | 74,564 |
| 目標（舊，repo 內） | `SpeechMessageProducts.ChurchReport\Tools\Analyze-ChurchReportTraces.ps1` | 860 | 41,956 |

已事先比對過：**兩支的函式名稱集合完全相同**，是同一支腳本的原地演進，不是分叉。

### 要做的事

1. 先算來源與目標的 SHA-256，記錄下來（報告要附）。
2. 把 repo 內現版另存為暫存備份（放 `%TEMP%`，**不要放進 repo**），供 diff 用。
3. 用來源覆蓋目標。
4. **做 diff 檢查**：確認沒有任何邏輯只存在於舊版而在新版消失。若發現有，**停下來寫進報告**，不要自行合併。
5. 確認覆蓋後檔案符合編碼規範（UTF-8 without BOM、CRLF）。
6. 跑 fixture 驗證（見下方）。
7. commit，**只包含這一個檔案**。

### Fixture 驗證

fixture 位於：

```
.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures\          （valid）
.trellis\tasks\08-19-unified-trace-guard-and-analysis\fixtures-invalid\  （invalid）
```

三種情境的期望 exit code：

| 情境 | 期望 exit code | 期望整體狀態 |
|---|---:|---|
| valid fixture | 0 | WARN |
| invalid fixture | 2 | FAIL |
| 指向不存在的目錄（missing） | 0 | WARN |

**Windows PowerShell 5.1 與 PowerShell 7 各跑一次**，六組結果都要進報告。報告輸出請寫到 `%TEMP%`，不要寫進 repo，也不要寫進 `D:\除錯追蹤`。

---

## 任務 B1 — 降低 Trace.log 的噪音與量測污染

### 背景

`D:\除錯追蹤\Trace.log` 目前 11,732 行，其中 89.5% 是噪音：

```
GetCurrentSessionId                  6,202 行  (52.9%)
GenerateCurrentRequestFingerprint    4,293 行  (36.6%)
[Perf] 系列                             76 行  ( 0.6%)
```

而 `Program.cs` 設了 `AutoFlush = true`，代表**每一行都是請求執行緒上的同步磁碟寫入** —— 94 秒的量測窗內發生了 10,495 次。這讓所有 phase 時間數字都被量測本身污染，後續 B2 的歸因分析在這個狀態下做不出可信結論。

順帶：這些行還把 **session GUID、BoundUserId、以及 X-Forwarded-For 的原始 client IP** 明文寫進 Trace.log。

### 設計決策（已定案，不要改用其他做法）

**決策 1：噪音行用開關關掉，不要刪除。**

理由：這些是當初除錯 session 問題時加的，問題已解決但可能還會需要。刪掉就回不來了。

**決策 2：照抄專案既有的 `ProfilingSwitch` 模式，不要發明新機制。**

參考 `SpeechMessageProducts.ChurchReport\Diagnostics\Profiling\ProfilingSwitch.cs`：

- `#if DEBUG` 包起來
- `public static volatile bool Enabled = false;` —— **預設關閉**
- 由 Startup 從既有的 `DiagnosticTraceOptions.Enabled` 指派
- 欄位不得承載任何 request、使用者或租戶狀態

新開關命名為 `SessionDiagnosticsSwitch`，放在同一個 `Diagnostics` 命名空間下。

**決策 3：`AutoFlush` 改為 `false`，並補明確的 flush 點。**

位置：`SpeechMessageProducts.ChurchReport\Program.cs` 第 195 行與第 220 行。

flush 點至少要涵蓋：請求結束、程序正常停止、未處理例外。
可參考 `ToolUtility\Diagnostics\FileToolUtilityTracer.cs`（該檔已經是 `AutoFlush = false`）的既有處理方式。

**決策 4：不要為噪音行加時間戳。**

決策 1 讓它們預設不輸出，加時間戳是多餘的工作。

### 要改的範圍

噪音全部集中在**單一檔案**：

```
SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs   （1,308 行）
```

該檔共 51 個 `System.Diagnostics.Debug.WriteLine`，分布在：

| 方法 | 數量 |
|---|---:|
| `GetCurrentSessionId` | 21 |
| `GenerateCurrentRequestFingerprint` | 18 |
| `SetSessionDirtyFlag` | 11 |
| `InMemoryDataContext` | 1 |

**這 51 個全部要納入開關**。不要漏掉 `SetSessionDirtyFlag`（它現在沒出現在 trace 裡只是因為這次沒觸發）。

加上 `Program.cs` 的 AutoFlush，本任務預期改動 **3 個檔案 + 1 個新檔**（開關本身）。

### 測試

新增測試，驗證開關關閉時這些方法不產生 trace 行。放在既有測試專案裡，沿用既有的測試風格。

---

## 硬性禁止事項

以下任一項發生，這次交付即視為失敗：

1. **不得修改、刪除、輪替、truncate `D:\除錯追蹤\` 底下的任何原始 trace 檔**（`dataverse-trace.jsonl`、`Trace.log`）。報告必須附上這兩個檔案在你開工前與收工後的 SHA-256，證明未變動。
2. **不得動 B2–B7 的範圍**。特別是：不要改分析器的 `[Perf-Phase]` 解析、不要改 `PerfThresholds`、不要碰 `DataverseGateway` 的平行競態、不要改 `BoundedClientPool`。
3. **不得改動 `DataverseTrace` 發出的事件格式或欄位**。分析器與發射端的契約這次不動。
4. **不得為了讓測試通過而放寬既有斷言**。測試失敗就報告失敗。
5. **不得 force push、不得 rebase、不得動既有 commit**。只准新增 commit。
6. 產出的暫存檔一律寫 `%TEMP%`，不要留在 repo 或 `D:\除錯追蹤`。

## 遇到這些情況請停下來，寫進報告而不是自行決定

- B0 的 diff 發現有邏輯只存在於舊版
- 51 個 `Debug.WriteLine` 中有任何一個，關掉後會改變程式行為（例如它其實有副作用）
- `AutoFlush = false` 之後發現既有測試依賴同步 flush
- 任何需要改動上面「硬性禁止事項」所列範圍才能完成的情形

---

## 驗收

### B0

- [ ] repo 內分析器行數為 1,280
- [ ] 覆蓋後檔案 SHA-256 == 來源檔 SHA-256
- [ ] 六組 fixture 執行（3 情境 × 2 個 PowerShell 版本）exit code 全部符合預期
- [ ] `git status` 除了預期的變更外是乾淨的

### B1

- [ ] 專案 Debug build：0 warning / 0 error
- [ ] 專案 Release build：0 warning / 0 error
- [ ] `dotnet test ToolUtility.Dataverse.Tests` 通過（基準：57/57）
- [ ] `dotnet test ToolUtility.Tests` 通過（基準：63/63）
- [ ] 新增的開關測試通過
- [ ] 開關預設值為 `false`

### B1 的實測驗收（可能需要人工協助）

程式改完後需要**重跑一次應用程式並操作一次登入**，才能取得對照數字。若你無法在此環境啟動互動式應用程式，**不要偽造數字** —— 在報告中標明「待人工重跑」，其餘部分照常交付。

若能重跑，需要這兩個數字：

| 指標 | 改動前 | 改動後 | 期望 |
|---|---:|---|---|
| Trace.log 總行數 | 11,732 | ? | 降到約 1,300 以內 |
| `/Home/ProcessLogin` 的 `Login.ValidateUserCredentials` | 1,946 ms | ? | **差額即為量測本身的成本** |

第二個數字是這整項任務的重點：它會告訴我們原本那 1,946ms 裡有多少是 trace 自己造成的。

---

## 報告格式（必填，貼回給 Claude 審核用）

請嚴格照這個結構輸出。**所有指令輸出請原樣貼上，不要改寫或摘要** —— 審核者需要看原始輸出。

```markdown
# Codex 執行報告 — 第一批（B0 + B1）

## 0. 總結
- B0 狀態：完成 / 部分完成 / 失敗
- B1 狀態：完成 / 部分完成 / 失敗
- 需要人工介入的項目：

## 1. 原始 trace 檔未變動證明
| 檔案 | 開工前 SHA-256 | 收工後 SHA-256 | 相同 |
|---|---|---|---|
| D:\除錯追蹤\dataverse-trace.jsonl | | | |
| D:\除錯追蹤\Trace.log | | | |

## 2. B0 — 分析器收回版控
### 2.1 檔案雜湊
| 檔案 | SHA-256 | 行數 | 位元組 |
|---|---|---:|---:|
| 來源（覆蓋前） | | | |
| 目標（覆蓋前） | | | |
| 目標（覆蓋後） | | | |

### 2.2 新舊版 diff 檢查
- 只存在於舊版的邏輯：有 / 無
- 若有，逐項列出：

### 2.3 Fixture 執行結果
| 情境 | PowerShell 版本 | exit code | 整體狀態 | 符合預期 |
|---|---|---:|---|---|
| valid | 5.1 | | | |
| invalid | 5.1 | | | |
| missing | 5.1 | | | |
| valid | 7 | | | |
| invalid | 7 | | | |
| missing | 7 | | | |

### 2.4 commit
- commit hash 與訊息：
- `git show --stat` 輸出：

## 3. B1 — 降噪與 AutoFlush
### 3.1 改動的檔案
| 檔案 | 改動內容摘要 | 增/刪行數 |
|---|---|---|

### 3.2 開關實作
- 型別名稱與檔案路徑：
- 預設值：
- 由何處指派：
- 貼上開關檔案的完整內容：

### 3.3 51 個 Debug.WriteLine 的處理
| 方法 | 應處理 | 實際處理 | 有無遺漏 |
|---|---:|---:|---|
| GetCurrentSessionId | 21 | | |
| GenerateCurrentRequestFingerprint | 18 | | |
| SetSessionDirtyFlag | 11 | | |
| InMemoryDataContext | 1 | | |

### 3.4 AutoFlush 改動
- Program.cs 改動前後的程式碼片段：
- 新增的 flush 點（位置與觸發條件）：

### 3.5 建置與測試（原始輸出）
Debug build:
（貼原始輸出）

Release build:
（貼原始輸出）

dotnet test ToolUtility.Dataverse.Tests:
（貼原始輸出）

dotnet test ToolUtility.Tests:
（貼原始輸出）

### 3.6 實測對照
| 指標 | 改動前 | 改動後 | 備註 |
|---|---:|---:|---|
| Trace.log 總行數 | 11,732 | | |
| Login.ValidateUserCredentials | 1,946 ms | | |
（若未重跑，此節寫「待人工重跑」並說明原因）

### 3.7 commit
- commit hash 與訊息：
- `git show --stat` 輸出：

## 4. 發現的落差
（`next-steps.md` 的描述與實際程式碼不符之處；沒有就寫「無」）

## 5. 我停下來沒做的事
（觸發「請停下來」條款的項目，以及為什麼；沒有就寫「無」）

## 6. 我做了但規格沒要求的事
（任何超出 B0/B1 範圍的改動，逐項列出並說明理由；沒有就寫「無」）

## 7. 最終 git 狀態
git status --porcelain 輸出：
git log --oneline -5 輸出：
```
