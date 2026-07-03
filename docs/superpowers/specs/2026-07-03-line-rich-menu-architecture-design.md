# LINE Rich Menu 共用架構設計規格

日期：2026-07-03
分支：Jesus_5.1.6.WorktreeRefactorLine
狀態：brainstorming 已完成，等待 implementation plan
關聯：`2026-07-03-line-shared-extraction-design.md`（Phase 2 四層架構；本案為其 rich menu 延伸）

## 1. 目標

建立可跨產品共用的 rich menu 能力，支援未來產品（建設公司維修系統、協會會員系統、發票收款系統）的兩種核心需求：

1. **依身分顯示不同選單**：系統在綁定、登入、角色變更等時機，依產品身分規則指派對應選單。
2. **依輸入文字切換選單**：使用者輸入關鍵字（例如「維修」「客服」），webhook 判斷後切換選單。

外加**選單內分頁**：同一身分的多個分頁選單用 LINE 原生 alias 切換（`RichMenuSwitchTemplateAction`），點擊瞬間換頁、不經伺服器、不占 API 額度。

各產品的「身分規則」（誰是會員／幹部／廠商）不進共用層。產品在知道身分的地方直接呼叫指派工作流；共用層只提供「宣告選單、佈建選單、指派選單、解析觸發詞」四種與產品無關的機制。

## 2. 需求決策紀錄

| 問題 | 決策 |
|------|------|
| 身分來源 | 兩者並存：後端依產品 DB 主動指派 + 使用者輸入文字自助切換 |
| 觸發詞管理 | 設定檔定義（appsettings 綁入 options），改表需重新部署 |
| 選單佈建 | 宣告式目錄 + 手動觸發同步（管理頁按鈕或 CLI），不做啟動時自動同步 |
| 切換機制 | 混合：同身分內分頁用 alias 原生切換；跨身分指派與文字觸發走 webhook + API 重綁 |
| 交付範圍 | 共用層完整實作 + ChurchReport 現有單鈕認證選單改接新機制作最小驗證，行為不變 |
| 架構方案 | 獨立 `LineMessagingProcessor.RichMenus` 專案；LINE 平台本身當唯一真相來源，不引入資料庫 |

平台限制備忘：LINE 官方後台（OA Manager）手動建立的選單無法用 Messaging API 逐人綁定；身分選單一律透過 API 建立。

## 3. 專案結構與依賴

### 3.1 新增專案

- **`LineMessagingProcessor.RichMenus`**：所有 rich menu 共用邏輯。依賴 `LineMessagingProcessor`（processor 的 RichMenu API 包裝）與 `Line.Messaging`（`RichMenu` 版面模型、`RichMenuSwitchTemplateAction`）。禁止依賴 ChurchReport、CRM（`Microsoft.Xrm` / `IOrganizationService` / `Entity`）、DbContext、ASP.NET Controller——與 Phase 2 相同的邊界規則。
- **`LineMessagingProcessor.RichMenus.Tests`**：對應測試專案，沿用 `CapturingHttpMessageHandler` + 注入 `LineMessagingClient(new HttpClient(handler), "test-token", ...)` 的既有測試模式。

### 3.2 型別搬移

`ILineRichMenuWorkflow`、`LineRichMenuWorkflow`、`LineRichMenuCreateUploadAndLinkRequest`、`LineRichMenuDeleteLinkedRequest`、`LineRichMenuResult`、`LineRichMenuException` 及相關測試，從 `LineMessagingProcessor.Workflows` 搬到 `LineMessagingProcessor.RichMenus`（namespace 改為 `LineMessagingProcessor.RichMenus`）。ChurchReport 的 `PushUtility` / `LineUtilityClass` 更新 using。搬移一次原子完成，並與其他並行 batch 工作錯開時段執行（單一寫者原則）。搬完後 `LineMessagingProcessor.Workflows` 回歸純通知職責。

### 3.3 Processor 補齊包裝

`LineMessagingProcessorClass` 目前只包了 6 個 RichMenu 操作（Create、UploadPng、LinkToUser、GetIdOfUser、Unlink、Delete）。本案需補：

- `GetRichMenuListAsync()`（同步比對用）
- `SetDefaultRichMenuAsync(richMenuId)`（IsDefault 佈建用）
- `CreateRichMenuAliasAsync` / `UpdateRichMenuAliasAsync` / `DeleteRichMenuAliasAsync` / `GetRichMenuAliasAsync`（alias 分頁用；SDK 已有實作，只補 processor 層包裝與參數驗證）

### 3.4 DI 整合

`AddLineRichMenus(...)` 擴充方法放在 `LineMessagingProcessor.AspNetCore`。該專案目前尚未建立（屬 Phase 2 Batch 1 範圍）；若實作本案時仍不存在，由本案建立最小版（僅含 rich menu 註冊與 options 綁定），Batch 1 再擴充通知部分。

## 4. 核心元件

### 4.1 選單目錄 `ILineRichMenuCatalog`

產品宣告式定義選單清單：

```csharp
public sealed class LineRichMenuDefinition
{
    public required string Key { get; init; }               // 產品內唯一，例 "member-main"
    public required RichMenu Layout { get; init; }           // SDK 版面模型（名稱由系統覆寫）
    public required Func<Stream> PngImageStreamFactory { get; init; }
    public string? Alias { get; init; }                      // 要被分頁切換引用才需要
    public bool IsDefault { get; init; }                     // 設為 channel 預設選單（至多一個）
}

public interface ILineRichMenuCatalog
{
    IReadOnlyList<LineRichMenuDefinition> Definitions { get; }
}
```

圖片是產品資源（wwwroot、內嵌資源或產品自選來源），共用層只收 `Func<Stream>`，不管圖片存哪。

### 4.2 同步工作流 `ILineRichMenuProvisioningWorkflow`

手動觸發（管理頁按鈕或 CLI 端點呼叫），把目錄佈建到 LINE channel：

```csharp
public interface ILineRichMenuProvisioningWorkflow
{
    Task<LineRichMenuSyncReport> SyncAsync();
}
```

**命名即狀態**：選單上傳到 LINE 時，名稱編為 `{key}:{內容雜湊}`（版面 JSON 序列化 + 圖檔 bytes 計算 SHA-256 取前 8 碼）。同步演算法：

1. `GetRichMenuList` 取得線上選單。
2. 逐一比對目錄定義：線上存在同名（key+hash 相符）選單 → `UpToDate`，記下 richMenuId；不存在 → 建立 + 上傳圖片 → `Created`。
3. 有 `Alias` 的定義：alias 不存在則 `CreateRichMenuAlias`，存在但指向舊版則 `UpdateRichMenuAlias` 指到新 ID。
4. `IsDefault` 的定義：`SetDefaultRichMenu` 指到目前 ID。
5. 線上出現目錄不認得的選單（含改版後的舊版本、legacy 逐人建立的孤兒選單）→ 只列入報告 `Unknown` 清單，**不自動刪除**（避免誤刪線上使用中的選單；清理由管理者人工決定）。

LINE 平台本身是唯一真相來源，不需要資料庫；同步天生冪等（第二次執行全部 `UpToDate`、零寫入呼叫）。

`LineRichMenuSyncReport` 內容：逐定義的結果（Key、RichMenuId、Outcome：Created / UpToDate / Failed、錯誤訊息）+ Unknown 選單清單。任一定義失敗不中斷其餘定義的同步，整體報告呈現。

### 4.3 指派工作流 `ILineRichMenuAssignmentWorkflow`

身分選單與文字切換共用的執行入口：

```csharp
public interface ILineRichMenuAssignmentWorkflow
{
    Task<LineRichMenuResult> AssignAsync(string userId, string menuKey);   // 失敗回 result
    Task AssignOrThrowAsync(string userId, string menuKey);                // 失敗拋 LineRichMenuException
    Task<LineRichMenuResult> UnassignAsync(string userId);                 // 解除綁定 → 回到預設選單
    Task UnassignOrThrowAsync(string userId);
}
```

內部把 `menuKey` 解析成 richMenuId：從目錄取出該 key 的定義並計算 `{key}:{內容雜湊}` 期望名稱，再到 `GetRichMenuList` 結果（執行緒安全的 channel 層級快取，cache miss 時重查一次）找**名稱完全相符**的選單。key 不在目錄、或線上找不到相符名稱（代表尚未同步）→ `ValidationFailed`，不發 link 呼叫。**只快取選單 ID 這類頻道層級資料，絕不快取使用者相關資料**（沿用既有安全原則）。

產品呼叫時機（皆在產品層）：

- 身分指派：綁定完成、登入、角色變更時，產品依自身規則決定 menuKey 後呼叫 `AssignAsync`。
- 大量重指派（例如整批角色調整）第一版不提供，未來需要時以 SDK 既有 bulk link API 擴充。

### 4.4 文字觸發解析器 `ILineRichMenuTextTriggerResolver`

```csharp
public sealed class LineRichMenuTextTriggerOptions
{
    public Dictionary<string, string> Triggers { get; init; } = new(); // 觸發文字 → menuKey
}

public interface ILineRichMenuTextTriggerResolver
{
    bool TryResolve(string messageText, out string menuKey);
}
```

比對規則第一版：**Trim 後 Ordinal 完全比對**（避免劫持一般聊天文字；中文無大小寫問題，英文指令由產品自行定義大小寫）。觸發表由 appsettings 經 options 綁入。產品 webhook 收到文字訊息時先呼叫 `TryResolve`，命中 → 呼叫 4.3 指派；未命中 → 走產品原本的訊息處理。

### 4.5 alias 分頁

同一身分的多個分頁選單各自宣告 `Alias`，版面按鈕用 `RichMenuSwitchTemplateAction(richMenuAliasId, data)` 指向目標 alias。使用者點擊時 LINE App 本地瞬間切換並送出 postback（`data` 內容產品可記錄或忽略），伺服器無需任何動作。因為 richMenuId 每次改版都會變、alias 名稱不變，同步流程負責把 alias 重新指到新版 ID，分頁按鈕永遠不斷。

## 5. 資料流

### 5.1 佈建（管理者）

管理者觸發 → 同步工作流讀目錄 → 比對 LINE 線上清單 → 建立缺的、更新 alias、設定預設 → 回報告。

### 5.2 身分指派（系統）

產品事件（綁定／登入／角色變更）→ 產品身分規則得出 menuKey → `AssignAsync(userId, menuKey)` → 解析 richMenuId（快取）→ `LinkRichMenuToUser`。

### 5.3 文字切換（使用者）

使用者輸入文字 → webhook `MessageEvent` → `TryResolve(text)` 命中 → `AssignAsync` → 選單更換（約 1 秒內生效）；未命中 → 原訊息流程。

### 5.4 分頁切換（使用者）

使用者點分頁鈕 → LINE App 依 alias 本地換頁（瞬間）→ postback 送達 webhook（可忽略）。

## 6. 錯誤處理

沿用既有 `LineRichMenuWorkflow` 的分類（與 Phase 2 §5.3 一致）：

- 空 userId／空 menuKey／menuKey 不在目錄：`ValidationFailed`，不發 HTTP。
- LINE API 非 2xx（`LineResponseException`）：`ProviderRejected`，保留 StatusCode。
- 網路失敗／逾時（`HttpRequestException` / `TaskCanceledException`）：`ProviderUnavailable`。
- 其他：`UnexpectedError`。

雙入口語意：`...Async` 回 `LineRichMenuResult`；`...OrThrowAsync` 拋 `LineRichMenuException`。同步工作流採報告模式：單一定義失敗不中斷整體，報告呈現逐項結果。共用層不寫產品 log、不更新 CRM；產品拿到 result／exception 後自行決定流程。

## 7. 測試策略

全部沿用 capture-handler 模式（不打真實 LINE API）：

- **請求正確性**：建立／上傳圖片／link／unlink／alias CRUD／set default 的 URL、method、body、Authorization header。
- **同步冪等**：預先在 stub 回應中放入同名（key+hash）選單 → 第二次同步全 `UpToDate`、零寫入呼叫。
- **改版換新**：hash 改變 → 建新版 + alias 改指新 ID + 報告 `Created` + 舊版列入 `Unknown`。
- **部分失敗**：某定義上傳圖片 500 → 該項 `Failed`，其餘定義照常同步。
- **觸發解析**：完全比對命中、Trim 行為、未命中回 false、空字串不觸發。
- **指派防護**：menuKey 不存在 → `ValidationFailed` 且無 HTTP 呼叫；非 2xx → `ProviderRejected`；OrThrow 變體拋例外。
- **邊界掃描**：新專案不得含 `ChurchReport`、`Microsoft.Xrm`、`IOrganizationService`、`Entity`、`Controller`、`IActionResult`、`DbContext`。

## 8. ChurchReport 最小驗證接入

把現有單鈕認證選單改走新機制，使用者可見行為不變：

- `CreateLegacySingleButtonRichMenu()` 的版面宣告進目錄，key 定為 `legacy-auth`。
- `PushUtility.AddRichMenuMessage` / `LineUtilityClass.AddRichMenuMessage` → `AssignOrThrowAsync(userId, "legacy-auth")`。
- `DeleteRichMenuMessage` → `UnassignOrThrowAsync(userId)`。

已知行為差異（有意為之，記錄於此）：

1. 舊實作每次呼叫都「建立新選單再綁定」（逐人一份）；新機制「一份選單、多人綁定」。使用者看到的選單相同。
2. 舊的 Delete 會把選單本體刪掉；新的只解除綁定（使用者看到的效果相同：選單消失、回到預設）。
3. LINE 上既有 legacy 逐人選單成為孤兒，同步報告會列入 `Unknown`，由管理者人工決定清理，本案不自動刪。

## 9. Guardrails

- 承接 Phase 2 §8 全部條款（不進產品語意、不動 `LinePayCSharp`、UTF-8 without BOM + CRLF、不提交 `bin/` `obj/` `artifacts/`）。
- 型別搬移與其他並行 batch 錯開時段、一次原子完成（單一寫者原則）。
- 不動 LINE OA Manager 手動建立的選單。
- 圖片素材屬產品資源，不放進共用專案。
- channel access token 依既有規則由呼叫端注入，不寫死在共用層。
- 同步絕不自動刪除線上選單。

## 10. 驗證

每個 slice 完成後至少執行：

- `LineMessagingProcessor.RichMenus.Tests` 全綠。
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` 0 錯誤。
- 邊界掃描（§7 最後一項）。
- Gemini + Claude 雙模型 review。
- touched text files encoding check（UTF-8 without BOM + CRLF）。

## 11. 下一步

依本 spec 撰寫 implementation plan，建議切片（實作計畫時定案）：

1. **R1**：新專案建立 + `ILineRichMenuWorkflow` 全家搬移 + 全 solution 綠。
2. **R2**：processor 補齊 `GetRichMenuList` / `SetDefaultRichMenu` / alias CRUD 包裝 + 測試。
3. **R3**：目錄 + 同步工作流 + 冪等／改版／部分失敗測試。
4. **R4**：指派工作流 + 文字觸發解析器 + DI 註冊 + 測試。
5. **R5**：ChurchReport 最小驗證接入 + 雙模型 review。
