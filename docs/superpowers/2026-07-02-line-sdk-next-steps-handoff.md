# LINE SDK 下一步交接文件（給 Codex）

> 2026-07-02 下午由 Claude 主 session 整理。本文件是唯一交接來源：先讀完本文件再動手，
> 不需要重新診斷或重新盤點。所有結論都有檔案佐證，直接引用即可。

## 一、現況快照（2026-07-02 15:00 前後）

- 分支/worktree：`Jesus_5.1.6.WorktreeRefactorLine`
- 第一份 plan（API 對照矩陣）**已完成並提交**：`Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`（~145 項，P0=29 / P1=45 / P2=71）
- 第二份 plan（P0 修復）**實作完成但全部未提交**：
  - 已改：`Line.Messaging/ILineMessagingClient.cs`、`Line.Messaging/LineMessagingClient.cs`、`LineMessagingProcessor/LineMessagingProcessorClass.cs`、`ChurchReport.sln`
  - 新增未追蹤：`Line.Messaging.Tests/`（16 tests 全綠）、`docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md`、`.ccg/tasks/line-messaging-sdk-p0-fixes/`
- 雙模型審查已跑兩輪，終審結果：
  - Gemini 終審（`.ccg/tasks/line-messaging-sdk-p0-fixes/review-gemini-final.txt`）：Critical: None，前輪問題全解
  - Claude 終審（`.ccg/tasks/line-messaging-sdk-p0-fixes/review-claude-final.txt`）：**還剩 1 Critical + 2 Warning（見下）**

## 二、第一部分：P0 收尾（必須先做完才能進第三份 plan）

### 2.1 修最後一個 Critical

`Line.Messaging/LineMessagingClient.cs:1013` 的 `VerifyContentPreparationAsync`：
endpoint 已改對（`/content/transcoding`），但回傳判斷仍是 `status == "ready"`。
官方 status 枚舉是 `processing` / `succeeded` / `failed` → 目前永遠回傳 false。

要求：
- 依官方枚舉建立強型別 status 解析（succeeded=true、processing=輪詢中、failed=false 或擲出明確錯誤，簽章設計依現有呼叫端語意決定，不要破壞相容）。
- 現有測試只驗了 request URL 沒驗回傳值 → **補一條測試 mock `{"status":"succeeded"}` 並斷言回傳值**，
  以及 `processing`/`failed` 兩條。這就是終審漏網的原因，測試必須驗行為不是只驗 URL。

### 2.2 處理兩個 Warning

1. `LineMessagingProcessor/LineMessagingProcessorClass.cs:49` `ResolveDefaultChannelAccessToken`：
   - 現況：用 `Directory.GetCurrentDirectory()` 直接讀 `appsettings.json`，繞過 `IConfiguration`，
     env-var/user-secrets 覆寫全部失效；且每次 `new LineMessagingProcessorClass()`（約 8 個呼叫點）都同步讀檔+解析。
   - 要求：改為可注入 `IConfiguration`/options 的路徑，並把解析結果快取一次（`Lazy<T>` 或 static）。
     保留無參數建構子相容（內部 fallback），呼叫點不必全改。
2. 明文 token 仍在被 git 追蹤的 `ChurchReport/appsettings.json`：
   - 至少：確認這是既有部署慣例還是新引入。若是這次搬進去的，改用 user-secrets 或部署時注入；
     若是既有慣例，於 commit message 記錄風險並在 review 記錄標註（不要默默留著）。

### 2.3 記帳與提交

- 把 `docs/superpowers/plans/2026-07-02-line-messaging-sdk-p0-fixes.md` 的 54 個 checkbox 按實際完成狀態勾掉。
- 驗證後提交（拆成合理的 commit：SDK 修復 / 測試專案 / plan 與 CCG 記錄）。

### 2.4 收尾驗證（全部要過）

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

雙模型複審（環境已於 2026-07-02 永久修復，直接用以下已驗證形式，不要改）：

```powershell
# PowerShell 下管線前必須（新 shell 的 profile 會自動做，但手動時別忘）：
$OutputEncoding = [System.Text.Encoding]::UTF8
$task | & "$HOME\.claude\bin\codeagent-wrapper.exe" --progress --lite --backend gemini - "<worktree路徑>"
$task | & "$HOME\.claude\bin\codeagent-wrapper.exe" --progress --lite --backend claude - "<worktree路徑>"
# 若見 "not running in a trusted directory"：$env:GEMINI_CLI_TRUST_WORKSPACE='true'
# 審查 prompt 要給真實 diff，不要寫「請只回答 OK」（reviewer 會拒答）
```

## 三、第二部分：第三份 plan（P1 篩選，不是全補）

### 3.1 原則

- **YAGNI**：矩陣的 P2（71 項）明確不做；P1（45 項）也只做「ChurchReport 現有產品流程會受益」的子集。
- **Linus 代碼原則**：少特殊情況（訊息共用欄位走 common base 而不是每個 message class 複製貼上）、
  資料流清楚（webhook envelope → event → handler 單向）、不藏全域狀態（token 不做 static 可變狀態）、
  一個類別只做一件事（processor 是產品 adapter，LINE protocol 歸 SDK）。

### 3.2 篩選程序（先做這個，再寫 plan）

1. 盤點實際用量：grep `ChurchReport/` 與 `LineMessagingProcessor/` 對 `ILineMessagingClient`/SDK 的所有呼叫點，
   列出「產品實際使用的 API 面」。
2. 拿使用面與矩陣 P1 清單取交集，只有交集 + 可靠性項目進入 plan。

### 3.3 P1 候選（矩陣行號佐證，按預期價值排序）

| 候選 | 矩陣依據 | 對 ChurchReport 的價值 |
|---|---|---|
| push/multicast/broadcast 加 retry key | L79/L80/L82 | 繳費/奉獻 LINE 通知的可靠性（重送不重複）|
| webhook envelope + `webhookEventId`/`deliveryContext`/`mode` | L132–L136 | 通知去重與 redelivery 處理的基礎 |
| 缺漏 webhook events（unsend/membership/video complete）| L143–L145 | 依實際用量取捨 |
| 訊息 common base：`quoteToken`/`sender`/mention | L151–L165 | 客服互動體驗；共用 base 消滅重複欄位 |
| 13 個 `NotImplemented` 方法 | 矩陣各處 | 只實作使用面有的；其餘標 obsolete 或移除宣告（誠實的介面）|
| processor 縮成產品 adapter | L72 | 邊界清理，LINE 呼叫全走 SDK interface |
| narrowcast 強型別 model | L81 | 僅在產品有用到 narrowcast 時做 |

### 3.4 產出物要求

- `docs/superpowers/specs/2026-07-02-line-sdk-p1-<範圍名>-design.md`（設計）
- `docs/superpowers/plans/2026-07-02-line-sdk-p1-<範圍名>.md`（可執行 plan，checkbox 步驟，含每步驗證指令）
- plan 的 guardrails 要像第一份 plan 一樣明確列出「不可修改的範圍」。

## 四、明確不要做的事

- 不要重新產矩陣、不要重跑 API 盤點（矩陣就是地圖）。
- 不要動 `LinePayCSharp/`（Line Pay 是另一個模組，不在本工作範圍）。
- 不要在 SDK（`Line.Messaging/`）引入 ChurchReport、CRM、DbContext 等產品相依（沿用金流抽離的邊界紀律）。
- 不要為了「補齊官方全部功能」把 P2 塞進 plan — 那不是目標，易於管理才是。
