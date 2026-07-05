## Analysis: RichMenu C# 檔案加註解（annotate-richmenu-cs-files）

### Current State

已實地讀取代表性檔案（Orchestrator、AssignmentWorkflow、ExpirationSweepWorkflow、Adapter、Exception、LINE DTO、Legacy Catalog、測試與邊界測試），確認幾個關鍵事實：

1. **專案已有明確的「保母級說明」風格**，非從零開始。`RichMenuOrchestrator.cs`、`LineRichMenuAssignmentWorkflow.cs`、`RichMenuExpirationSweepWorkflow.cs`、`ILineRichMenuProcessor.cs`、`LineRichMenuException.cs`、`ChurchReportLegacyRichMenuCatalog.cs` 已有 `<summary>` + `<remarks>` 區塊，說明「為什麼這樣切」「邊界在哪」，並在關鍵分支（cache miss、provider 失敗 vs state store 失敗、優先權比較）用行內註解解釋「為什麼不能這樣做」。
2. **簡單型別/POCO 完全沒有註解**：`RichMenuDecision.cs`、`RichMenuDecisionPriority.cs` 目前零註解，推測 `RichMenuUserState.cs`、`LineRichMenuStatus.cs`、`LineRichMenuResult.cs`、`LineRichMenuSyncItem/Outcome/Report.cs`、`LineRichMenuDefinition.cs` 等 record/enum 類似。這是本次任務要補的主要缺口，而不是重寫既有的完整文件。
3. **`Line.Messaging/Messages/RichMenu/*.cs` 是 LINE 官方 API 的資料模型包裝**，已有對照 `developers.line.me` 文件連結的英文 XML 摘要，內容基本正確，但缺少「非顯而易見行為」的說明（見風險 1）。
4. **測試檔命名本身已經是文件**（`ApplyAsync_assigns_menu_when_text_matches_trigger_policy` 這類 Given-When-Then 命名），過度加註解會與方法名重複。
5. **`RichMenuProjectBoundaryTests.cs` 是一個架構強制測試**：它會掃描 `LineMessagingProcessor.RichMenus/` 目錄下所有 `.cs` 檔案的**每一行文字**（含註解），若出現 `"ChurchReport"`、`"Controller"`、`"IActionResult"`、`"DbContext"`、`"Microsoft.Xrm"`、`"IOrganizationService"` 任一子字串就會判定失敗。這對本次「加詳細註解」任務是**最大的風險點**（詳見下方風險 1，Critical）。

---

### 1. Production RichMenu workflow 檔案的註解策略

適用範圍：`RichMenuOrchestrator.cs`、`LineRichMenuAssignmentWorkflow.cs`、`RichMenuExpirationSweepWorkflow.cs`、`LineRichMenuProvisioningWorkflow.cs`、`LineRichMenuWorkflow.cs`、`RichMenuActionFactory.cs`、`LineMessagingProcessorRichMenuAdapter.cs`、各 `I*.cs` 介面、以及目前零註解的 POCO/enum（`RichMenuDecision`、`RichMenuDecisionPriority`、`RichMenuUserState`、`RichMenuContext`、`LineRichMenuStatus`、`LineRichMenuResult`、`LineRichMenuSyncItem/Outcome/Report`、`LineRichMenuDefinition`、`LineRichMenuFingerprint`、`LineRichMenuAssignmentResult`、各 Request/Exception 類別）。

- **沿用既有風格，不要另創新格式**：`<summary>` 一句話說明「這是什麼」，`<remarks>` 用「保母級說明」段落回答「為什麼這樣設計」「邊界在哪」「未來擴充點在哪」。已加註解的檔案作為模板，逐字複製其語氣與段落結構。
- **對零註解的 POCO/enum，至少加型別層級 `<summary>`**，並在有隱含規則時加 `<remarks>`：
  - `RichMenuDecisionPriority`：數值大小代表優先權比較（`Orchestrator` 用 `decision.Priority > best.Priority` 挑選），這個「數字越大越優先、同分先註冊者贏」的規則目前只寫在 `RichMenuOrchestrator.cs` 裡，enum 本身應該也點出這個關聯，避免未來有人在別處新增 policy 時搞錯數值語意。
  - `RichMenuDecision`：`None`/`Assign`/`Remove` 三個靜態工廠是唯一建構路徑（建構子是 private），應說明這是刻意的「合法狀態即型別」（valid-state-only）設計，避免出現 `MenuKey` 與 `Unlink` 同時有值的不合法組合。
  - `RichMenuUserState`：需要說明 `PreviousMenuKey` 與 `ExpiresAt` 的語意，因為 `RichMenuExpirationSweepWorkflow` 依賴這兩個欄位做「回復上一個選單 vs 直接解除」的分支，若欄位語意不清，未來修改 state store 實作容易寫錯。
- **行內註解只留給「非顯而易見」的分支**，例如既有的：
  - `LineRichMenuAssignmentWorkflow.AssignAsync` 中 state store 失敗 vs LINE provider 失敗「不能包在同一個 try/catch」的理由。
  - `TryMapProviderException` 的 `default` 分支「刻意不吃掉未知例外」的理由。
  - 這種每一行都要重新推導「為什麼」的邏輯，才值得寫在程式碼旁邊；單純的 null 檢查、trim、ArgumentNullException 不需要加註解。
- **`LineMessagingProcessorRichMenuAdapter.cs`**：目前大多是一行委派，只有 `GetRichMenuAliasAsync` 把 LINE 404 轉成 `LineRichMenuAliasNotFoundException`。應該只在這個方法上加註解說明「為什麼只有這個方法不是純委派」，其餘一行委派方法不需要逐一加註解，否則會製造 20 行重複噪音。

### 2. LINE Messaging DTO / action 檔案的註解策略

適用範圍：`Line.Messaging/Messages/RichMenu/*.cs`（`RichMenu.cs`、`ActionArea.cs`、`RichMenuAlias.cs`、`ResponseRichMenu.cs`、`RichMenuBulkRequest.cs`、`RichMenuBatchOperation.cs`）、`Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs`。

- **保留現有指向官方文件的 `<summary>`**，不要覆寫或刪除既有連結型註解，這些是可驗證的權威來源。
- **只補「執行期非顯而易見行為」，用 `<remarks>` 而非改寫 `<summary>`**：
  - `RichMenu.Name` / `ChatBarText` 的 setter 會用 `Substring` **靜默截斷**超長字串（無例外、無記錄），呼叫端傳入 301 字的名稱不會報錯，只會悄悄變成 300 字。這是一個容易被忽略的「隱性資料遺失」行為，值得在 setter 旁加一句提醒，避免未來維護者以為長度驗證會拋例外。
  - `ActionArea.ParseTemplateAction` 用 `Enum.Parse` 解析 LINE 傳回的 `type` 字串，若 LINE 未來新增本專案未支援的 action type，會直接丟 `ArgumentException` 而非優雅降級（`default` 分支只在型別合法但未列舉時回傳 `null`，型別字串本身不合法時是在 `Enum.Parse` 就先炸開）。這對前向相容性是個風險點，建議加註解說明。
- **這批檔案屬於「包裝 LINE 官方 API」的資料模型**，不屬於 `LineMessagingProcessor.RichMenus` 目錄，因此不受 boundary test 限制，可以自由提及 ChurchReport 或任何產品名稱作為範例（但仍建議保持中立，方便未來其他產品重用時不必修改註解）。

### 3. 測試 / 支援檔案的註解策略

適用範圍：`LineMessagingProcessor.RichMenus.Tests/**/*.cs`。

- **不要在每個 `[Fact]` 方法上重複方法名稱已經表達的意思**。既有命名（`ApplyAsync_assigns_menu_when_text_matches_trigger_policy`）已經是 Given-When-Then 形式，逐行加註解只會製造「註解與程式碼各說各話」之後的維護負擔——測試改了但註解沒改，就會誤導人。
- **值得加註解的是測試「意圖」而非「步驟」**，尤其是：
  - `RichMenuProjectBoundaryTests.cs`：**這是本次最需要加說明的檔案**。目前沒有任何註解解釋「為什麼這個測試存在」「為什麼列了這幾個關鍵字」。強烈建議加 `<summary>`/`<remarks>` 明確寫出：這個測試強制 `LineMessagingProcessor.RichMenus` 保持與具體產品（ChurchReport）、CRM（Dataverse/Xrm）、EF（DbContext）、MVC（Controller/IActionResult）解耦；並且**明確提醒維護者：若此測試因為新增註解而失敗，正確做法是修改註解措辭，而不是刪除或放寬這個測試**。這條說明本身就能防止未來有人因為測試「莫名其妙」失敗而直接刪掉架構防護。
  - `RichMenuTestFactory.cs` / `CapturingRichMenuProcessor.cs`：說明這是「測試替身（test double）」，`CapturingRichMenuProcessor` 模擬 LINE API 但只記錄呼叫、不做真實網路請求，讓其他測試可以斷言「是否呼叫了 Link/Unlink」而不需要真的打 LINE API。
- **測試方法內部的 Arrange/Act/Assert 三段式不需要標籤註解**（`// Arrange` 這類），這是 FluentAssertions + xUnit 常見寫法，加了反而是噪音。

### 4. 註解可能誤導維護者的風險

| 風險 | 等級 | 說明 |
|---|---|---|
| 在 `LineMessagingProcessor.RichMenus/**/*.cs` 的註解中寫出 `"ChurchReport"` 等禁用字 | **Critical** | `RichMenuProjectBoundaryTests.cs` 逐行掃描該目錄下所有 `.cs` 檔（含註解），命中 `ChurchReport`/`Controller`/`IActionResult`/`DbContext`/`Microsoft.Xrm`/`IOrganizationService` 任一子字串就會讓測試失敗。既有註解已刻意迴避（用「建設公司維修系統」等假想產品名稱取代 ChurchReport），**加註解時必須沿用這個規則**：舉例時用抽象/假想產品名，不能用真實產品名稱，也不要意外把 `ChurchReport` 當作範例貼進去（例如「未來若要接 ChurchReport 的...」這種寫法會直接讓 CI 紅燈）。 |
| 把「目前的實作細節」寫成「保證的行為」 | Warning | 例如 `RichMenuExpirationSweepWorkflow` 目前 `Report` 只統計嘗試筆數而非成功筆數（`restored++` 在呼叫後就一定累加，不論 assign/unassign 是否真的成功）。若註解寫成「Restored 代表成功還原數」會與程式碼實際語意不符，之後有人依此寫監控告警會誤判。應該如實描述「目前只統計嘗試次數，尚未逐筆記錄成敗」。 |
| 幫 vendored/第三方包裝碼加太多「設計理念」註解 | Info | 依既有 memory 紀錄，`Line.Messaging` 被歸類為 vendored 第三方碼（[[nowarn-audit-vendored-vs-own]]）。目前看不到自動化重新 vendor 的機制（git log 只有初始匯入 2 筆），風險不高，但仍建議標記這批檔案的註解是「本專案維護者所加」，避免未來誤以為是 LINE 官方原始碼的一部分。 |
| 過度精確描述數值/優先權，日後改了沒同步更新 | Warning | 若在 `RichMenuDecisionPriority` 註解裡寫死「TextTrigger=80, Explicit=100」這類具體數字並解釋順序，之後有人改動數值卻沒回頭改註解，註解就會說謊。建議只描述「數字越大優先權越高，實際數值以程式碼為準」，避免複製具體數字造成未來漂移。 |
| 幫測試「Arrange 區塊」逐行加註解，複製方法名的敘述 | Info | 純粹增加閱讀噪音，且容易在重構時忘記同步刪除，不建議。 |

### 5. 修改後的驗證建議

1. **先跑一次 `RichMenuProjectBoundaryTests`**（`dotnet test --filter FullyQualifiedName~RichMenuProjectBoundaryTests`），確認新增註解沒有誤觸禁用字。這是本次任務**唯一會被「文件變更」實際破壞的自動化檢查**，優先順序最高。
2. **整包跑 `LineMessagingProcessor.RichMenus.Tests`**，確認純加註解不影響任何既有測試（理論上加 `///` 或 `//` 不應改變任何行為，但可作為 regression safety net）。
3. **`dotnet build` 全solution**，確認 XML 文件註解語法正確（`<summary>`、`<see cref="...">` 若打錯型別名稱，在啟用 XML doc 產生的專案會變成 build warning，甚至在 `TreatWarningsAsErrors` 設定下擋 build）。
4. **抽查 `<see cref="...">` 是否都指向存在的型別/成員**，尤其是引用其他檔案型別的地方（例如 `RichMenuOrchestrator` 已有的 `<see cref="IRichMenuPolicy"/>`），新增註解若引用了拼錯的型別名稱，`cref` 會在 build 時產生 CS1574 警告。
5. **`git diff --stat`** 確認這次是純新增行/新增註解，沒有意外改到任何可執行邏輯（因為是 documentation-only 變更，diff 裡不應該出現任何非註解、非空白的程式碼行變動）。

---

### Recommendation

按「先有骨架再補血肉」的順序執行：先幫零註解的 POCO/enum（風險最低、價值最高）補上型別 `<summary>`，再處理已有部分註解但可以強化 remarks 的 workflow 類別，最後才處理 `Line.Messaging` DTO 與測試檔案。**全程比對既有已加註解檔案的語氣與結構作為模板**，並在動手前把 `RichMenuProjectBoundaryTests.cs` 的禁用字清單記下來，作為每次加註解後的自我檢查清單。

### Action Items
1. [ ] 抄錄 `RichMenuOrchestrator.cs` / `LineRichMenuAssignmentWorkflow.cs` 的 `<summary>`+`<remarks>` 模板，套用到零註解 POCO/enum。
2. [ ] 為 `RichMenuProjectBoundaryTests.cs` 加上說明其存在目的與「測試失敗時該改註解不該刪測試」的警語。
3. [ ] 為 `RichMenu.cs` 的 `Name`/`ChatBarText` setter 加上「靜默截斷」行為說明。
4. [ ] 為 `ActionArea.ParseTemplateAction` 加上未知 action type 會拋例外的前向相容性提醒。
5. [ ] 全部改完後執行第 5 節列出的驗證步驟，優先確認 boundary test 綠燈。

我這次的角色是 Systems Analyst（分析用，不做程式修改），以上即完整分析與建議；如需我接手實際加註解，請明確授權後再進行。

---
SESSION_ID: 8b026fc6-eb6e-4de7-852e-3374d0324514
