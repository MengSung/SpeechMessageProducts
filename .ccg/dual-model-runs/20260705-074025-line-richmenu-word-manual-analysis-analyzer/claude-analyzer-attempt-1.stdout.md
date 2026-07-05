# LINE RichMenu 模組交付分析報告（Systems Analyst / 交叉驗證版）

> 本報告基於直接讀取原始碼驗證（非僅推測），並與同批次 Gemini 分析師輸出做交叉核對，標出其中未經驗證或有出入之處。所有結論皆附檔案路徑佐證。

---

## 0. 關鍵結論先講：production 實際佈線比表面描述窄很多

這是本次分析中**最重要、也是同批 Gemini 報告未發現**的一點：

`LineMessagingProcessor.RichMenus` 共用層提供了 Catalog / Provisioning / Assignment / **Orchestrator / Policy / TextTrigger / ExpirationSweep**，且在 `LineMessagingProcessor.AspNetCore` 全部完成 DI 註冊、也都有單元測試覆蓋。**但實測 ChurchReport 專案原始碼後發現：**

- `ILineRichMenuProvisioningWorkflow.SyncAsync()` — **ChurchReport 中零呼叫**（只在 `Startup.cs` 註冊 DI，沒有任何 Controller / 排程 / 啟動流程實際執行同步）。
- `IRichMenuOrchestrator.ApplyAsync()` — **ChurchReport 中零呼叫**。
- `ILineRichMenuTextTriggerResolver` — **ChurchReport 中零呼叫**。
- `IRichMenuExpirationSweepWorkflow.SweepAsync()` — **ChurchReport 中零呼叫**（沒有任何 `BackgroundService`/`IHostedService`/排程觸發它）。

ChurchReport 目前實際執行的路徑只有：`PushUtility`/`LineUtilityClass.AddRichMenuMessage` → `ILineRichMenuAssignmentWorkflow.AssignOrThrowAsync(userId, "legacy-auth")`，以及對應的 `DeleteRichMenuMessage` → `UnassignOrThrowAsync`。這兩個方法只做「把使用者連結/解除連結到單一顆既有選單」，選單本身（`legacy-auth`）必須早已存在於 LINE 平台上（可能是先前手動在 LINE 官方帳號後台建立，或某次未留痕跡的手動同步）——**新的 Provisioning 邏輯目前不負責建立/更新它**。

**對 Word 文件的直接影響**：章節架構與矩陣表必須明確切出「共用層具備能力（已寫完、已測試）」vs「ChurchReport 產品層目前實際有接線執行」兩欄，不能合併成一欄「已完成」，否則讀者會誤以為限時選單自動到期、文字觸發變換選單、VIP policy 動態切換等功能現在就能在 ChurchReport 上跑起來。

---

## 一、文件應包含的章節架構

建議五章，每章明確標註「共用層能力」與「ChurchReport 目前接線狀態」：

**第一章：概述與商業價值**
- LINE RichMenu 概念、在 LINE 官方帳號經營的角色
- 為何重構：三階段演進（見第二章 2.1），而非一次性改動
- 名詞對照表：menuKey（產品邏輯代號）vs richMenuId（LINE 平台實際 ID）vs aliasId（跨選單切換用別名）

**第二章：架構與機制剖析**
- 2.1 三階段演進歷史（`git log`可查）：
  1. `PushUtility` 直接呼叫 `LineMessagingClient` SDK（Create/Upload/Link 全部寫在產品層）
  2. 抽成 `ILineRichMenuWorkflow`（一次性建立+上傳+連結／刪除，仍是單人單選單模式，`LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs`）
  3. 抽成 catalog + provisioning + assignment + orchestrator（多人共用同一份選單、fingerprint 比對、policy 決策）
- 2.2 各元件實際職責（含檔名、公開方法）——見附表
- 2.3 **明確標示：ChurchReport 目前只用到 assignment 這一層**，orchestrator/policy/sweep/trigger 是「已寫好但尚未接上任何呼叫端」的能力

**第三章：開發者調用指南**
- DI 註冊：`AddLineRichMenus()`（僅核心） vs `AddLineRichMenuProvisioning<TCatalog>()`（核心+catalog+provisioning，內部仍會呼叫 `AddLineRichMenus()`）
- 如何實作新 Catalog：以 `ChurchReportLegacyRichMenuCatalog` 為例，但需註明其目前是**單選單、圖片來自本機絕對路徑**的過渡期寫法，不是多選單範本
- 如何指派/解除指派：`AssignAsync`/`AssignOrThrowAsync`/`UnassignAsync`/`UnassignOrThrowAsync` 差異（Result 模式 vs 拋例外模式）
- 如何啟用尚未接線的能力（給未來維護者的「怎麼把它真正打開」指南）：
  - 要用 Provisioning：需自行呼叫 `ILineRichMenuProvisioningWorkflow.SyncAsync()`（建議放在啟動流程或後台管理按鈕）
  - 要用 Orchestrator/Policy：需在 webhook 訊息處理流程中建構 `RichMenuContext` 並呼叫 `ApplyAsync`
  - 要用到期回收：需自建排程（`BackgroundService`/Hangfire/Windows 排程皆可）定期呼叫 `SweepAsync(DateTimeOffset.UtcNow)`

**第四章：維運與診斷**
- `IRichMenuStateStore` 預設為 `InMemoryRichMenuStateStore`，**重啟即遺失**，多機/Auto-scaling 下狀態不同步 → 若要用 Sweep/Orchestrator 的「回復前一個選單」能力，必須先換成持久化實作
- 錯誤分類：`LineRichMenuStatus`（ValidationFailed/ProviderRejected/ProviderUnavailable/UnexpectedError）與對應 Exception 型別
- Provisioning 的容錯設計：單一 catalog 項目失敗不中斷整批同步（`LineRichMenuProvisioningWorkflowTests.cs` 有測試覆蓋）

**第五章：創意應用與未來擴充**（見第四節）

---

## 二、容易誤導使用者的風險點

### 🚨 Critical

1. **「已測試」不等於「已在 ChurchReport 上線運作」**（本報告 0 節）。若文件把 Orchestrator/Policy/TextTrigger/Sweep 寫成「本模組現有功能」而不註明「ChurchReport 尚未接線呼叫」，會讓非技術讀者（甚至技術主管）誤以為換個 policy 設定檔就能立即生效——實際上要先寫 webhook 整合程式碼才能觸發。
2. **`ChurchReportLegacyRichMenuCatalog` 圖片路徑是寫死的本機絕對路徑** `D:\暫存區\richmenu.PNG`（`ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs:21`），透過 `File.OpenRead` 讀取，非內嵌資源、非設定檔可調整。若部署機器上此路徑不存在，Provisioning（一旦真的被呼叫）或首次建立選單時會直接 `FileNotFoundException`。文件若展示這個 catalog 當作「標準寫法範例」，務必加註「僅供舊流程過渡使用，正式範本應改用嵌入資源或設定路徑」。
3. **官方數字類規格（圖片尺寸/檔案大小上限/選單與別名數量上限/LINE App 最低版本）目前無法從本任務已抓取的來源檔案驗證。** 經檢查 `.ccg/tasks/line-richmenu-word-manual/sources/` 下 4 個 HTML：
   - `developers_line_biz_en_reference_messaging_api_rich_menu.html` 與 `developers_line_biz_en_reference_messaging_api_richmenu_switch_action.html` **兩檔 MD5 完全相同**，實際內容都只是「Messaging API reference」的頂層目錄頁（連結列表），並非個別詳細規格頁面。
   - 因此諸如「圖片寬高 800–2500px」「檔案大小 1MB」「單一 Provider 選單/別名上限 1000 個」「LINE App 8.11.0 以上才支援 switch action」等**具體數字，在本次已抓取的原始碼佐證之外，屬未經來源驗證的一般性認知**，不應在文件中標示為「已依官方文件核實」。
   - **建議行動**：重新抓取 `Requirements for rich menu image`、`Rich menu alias`、`Rich menu switch action`、`Use per-user rich menus` 這幾個實際細節頁面後再引用數字；若時間不允許，文件中該類數字前應加註「請於發布前至 LINE Developers 官網覆核最新數值」。

### ⚠️ Warning

4. **Provisioning 的 fingerprint 比對邏輯只查詢、不建立**：`LineRichMenuAssignmentWorkflow.ResolveRichMenuIdAsync`（`LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:155-208`）在 cache miss 時只會去 LINE 平台「查」有沒有名稱吻合的既有選單，查不到就回傳 `null`（導致 `AssignAsync` 回傳 `line-richmenu-menu-key-not-found`），**它不會自動呼叫 Provisioning 去建立**。要讓 `AssignAsync` 真正成功，必須先有人跑過 `LineRichMenuProvisioningWorkflow.SyncAsync()`。這一步容易被誤讀成「指派時會自動建置」。
5. **`RichMenuExpirationSweepWorkflow.SweepAsync` 的 `Restored` 計數不代表真正成功**（`LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs:36-68`）：對每筆到期狀態呼叫 `AssignAsync`/`UnassignAsync` 後，不論回傳的 `LineRichMenuAssignmentResult.Succeeded` 是否為 true 都一律 `restored++`。若 LINE 端當下拒絕或逾時，報表仍會顯示「已還原」，維運人員可能誤判成功率。文件在介紹 Sweep 報表時應加註此限制，不要把它當成可靠的成功率指標。
6. **舊版 `ILineRichMenuWorkflow`（`LineRichMenuWorkflow.cs`）與新版 `ILineRichMenuAssignmentWorkflow` 並存**，兩者職責容易混淆：前者是「一次性建立+上傳+連結、刪除即整份選單砍掉」的舊流程（適合一人一份選單的場景），後者是「多人共用一份選單」的新架構。文件若只介紹其中一個，讀者可能誤用來源碼裡另一個既有 API。
7. **`RichMenuSwitchTemplateAction`／alias 切換的用戶端版本相容性**（Gemini 草稿標示 LINE App 8.11.0 以上）——如上第 3 點，此版本號未經本次來源驗證，發文件前需覆核。

### ℹ️ Info

8. Cache miss 時的 fingerprint 反查會多一次 LINE `GetRichMenuListAsync` 呼叫，屬設計內延遲，不是效能異常。
9. `LineMessagingProcessor.RichMenus` 有一個架構邊界測試（`RichMenuProjectBoundaryTests.cs`）會掃描整個共用專案原始碼，禁止出現 `ChurchReport`/`Microsoft.Xrm`/`IOrganizationService`/`DbContext`/`Controller`/`IActionResult` 字樣，用來強制共用層不得依賴任何產品層或 Web 層型別——這是文件中可以放心引用、且真正落地執行的架構保證，可作為「已完成」章節的具體證據。
10. 「本分支已通過完整邊界驗證與單元測試，文件撰寫可安心引用上述 API 命名」——這是 Gemini 草稿的結語，**屬未經本次工作階段驗證的斷言**（我沒有實際執行 `dotnet test`）。測試檔案確實存在且覆蓋範圍完整（Assignment 19 案例、Provisioning、Sweep、Orchestrator、TextTrigger、ActionFactory、Boundary 各有測試），但「目前是否全數通過」建議發文前實際跑一次 `dotnet test` 再下結論。

---

## 三、「已完成」與「未來可擴充」標示建議

建議用三欄（不是兩欄）矩陣，把「SDK 支援」「共用層封裝」「ChurchReport 實際接線」分開標示，避免任何一層的「有」被誤讀成全部都通了：

| 能力 | LINE SDK 支援 | 共用層封裝 | ChurchReport 實際接線 | 說明 |
|---|---|---|---|---|
| 建立/上傳/刪除選單 | ✅ | ✅ `LineRichMenuProvisioningWorkflow` | ❌ 未見呼叫 `SyncAsync()` | 選單需另行手動建立或補上呼叫點 |
| 別名管理（Alias） | ✅ `CreateRichMenuAliasAsync` 等 | ✅ | ❌（隨 Provisioning 一併未接線） | |
| 預設選單（Default） | ✅ | ✅ | ❌ | |
| 單人指派/解除（Assign/Unassign） | ✅ `LinkRichMenuToUserAsync`/`UnLinkRichMenuFromUserAsync` | ✅ `LineRichMenuAssignmentWorkflow` | ✅ `PushUtility.AddRichMenuMessage`/`DeleteRichMenuMessage` | **目前唯一真正在 production 路徑上跑的能力** |
| 多 Policy 協調（Orchestrator） | N/A | ✅ | ❌ 未見呼叫 `ApplyAsync` | 需在 webhook 訊息處理流程中接線 |
| 文字觸發切換選單 | N/A | ✅ `LineRichMenuTextTriggerResolver` | ❌ | 同上，需接線到訊息接收流程 |
| 到期自動回收（Sweep） | N/A | ✅ | ❌ 無任何排程呼叫 `SweepAsync` | 需自建 `BackgroundService`/排程 |
| 狀態持久化 | N/A | ✅ 介面化，預設 `InMemory` | ⚠️ 生產環境用 InMemory 會重啟遺失 | 未來擴充：接 Redis/DB 實作 `IRichMenuStateStore` |
| 批次連結/解除（Bulk, ≤500 人） | ✅ `LinkRichMenuToUsersAsync`/`UnLinkRichMenuFromUsersAsync` | ❌ 完全未封裝 | ❌ | 明確標「未來可擴充」 |
| 批次控制（Batch + 進度查詢） | ✅ `RichMenuBatchOperationAsync`/`GetRichMenuBatchProgressAsync` | ❌ | ❌ | 明確標「未來可擴充」 |
| 選單驗證（Validate） | ✅ `ValidateRichMenuAsync`/`ValidateRichMenuBatchRequestAsync` | ❌ | ❌ | 明確標「未來可擴充」 |
| 下載選單圖 / JPEG 上傳 | ✅ `DownloadRichMenuImageAsync`/`UploadRichMenuJpegImageAsync` | ❌ | ❌ | SDK 有但共用層/產品層都用不到，屬冷門延伸方向 |

> 註：Gemini 草稿中矩陣使用的型別名「`RichMenuBulkRequest`」與實際 SDK 型別不符（實際批次相關型別為 `RichMenuBatchOperation`/`RichMenuBatchProgress`；bulk 連結方法為 `LinkRichMenuToUsersAsync`/`UnLinkRichMenuFromUsersAsync`），撰寫文件時請以本報告列出的方法/型別名為準。

---

## 四、可寫進 Word 文件的 RichMenu 創意點子（含可行性標註）

1. **文字觸發分頁式選單切換**（可行性：現有能力足夠，只差接線）——用 `LineRichMenuTextTriggerResolver` + `RichMenuActionFactory.Switch` 做 alias 式頁籤切換，使用者輸入關鍵字或點選單瞬間切換分頁。
2. **角色/等級動態選單（VIP／一般會員）**（可行性：需新增一個 `IRichMenuPolicy` 實作 + 接線 Orchestrator）——CRM 等級查詢結果驅動 Orchestrator 選出對應選單。
3. **限時活動選單＋自動到期回收**（可行性：Sweep 邏輯已寫好，但**必須先補一個排程呼叫 `SweepAsync`**，目前是空接線）——活動開始時 `AssignAsync` 切換，設定 `ExpiresAt`，排程掃描到期後自動用 `PreviousMenuKey` 復原。
4. **購物車/流程進度提示選單**（可行性：需自訂 policy，依訂單狀態決定選單內容，例如未結帳/已結帳/已出貨三態切換）。
5. **問卷/表單進行中鎖定選單**（可行性：文字觸發或狀態旗標驅動的暫時性選單，搭配 Sweep 到期解鎖）。
6. **夜間/離峰自動客服選單**（可行性：只需一個以時間為條件的 `IRichMenuPolicy`，不需新增共用層程式碼）。
7. **多語系選單**（可行性：Policy 依使用者語系偏好挑選對應 menuKey，屬低成本擴充）。
8. **大量行銷推播＋批次選單切換**（可行性：**目前不可行**，需先把 SDK 的 `LinkRichMenuToUsersAsync`/`RichMenuBatchOperationAsync` 封裝進共用層，屬於「未來可擴充」的代表案例，可在文件中明確標「規劃中」）。
9. **選單發布前自動驗證**（可行性：**目前不可行**，需封裝 `ValidateRichMenuAsync`/`ValidateRichMenuBatchRequestAsync` 到 Provisioning 流程，作為未來降低誤上架風險的建議項）。
10. **現場活動/展會導覽選單（搭配 LIFF 定位）**（可行性：需額外整合 LIFF 取得位置或掃碼結果，再驅動一個新 Policy；比前面幾項成本高，適合列為長期路線圖項目）。

---

## 行動項目（Next Steps）

1. [ ] 重新抓取 LINE Developers 的 4 個實際細節頁面（image requirements / rich menu alias / richmenu switch action / use per-user rich menus），取代目前重複的 TOC 頁面，再撰寫含數字規格的段落。
2. [ ] 文件初稿完成前，實際執行一次 `dotnet test`，確認 `LineMessagingProcessor.RichMenus.Tests` 全數通過，再引用「已通過測試」字樣。
3. [ ] 與程式負責人確認：Provisioning／Orchestrator／Sweep／TextTrigger 是否有計畫在近期接線到 ChurchReport，若有，文件可用「即將上線」字樣；若無明確排程，一律標「共用層已具備，尚待接線」。
4. [ ] 決定 `ChurchReportLegacyRichMenuCatalog` 的硬編路徑是否要在文件發布前一併修正（不修正的話文件需明確警語）。

---
SESSION_ID: cc7522b3-acc7-4126-ad97-738088b0f14a
