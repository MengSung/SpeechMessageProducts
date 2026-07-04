# LINE RichMenu 共用架構重構 — Review 結果

## Critical 🔴

- **建置目前失敗，「已執行驗證」記錄已過期，不可信**
  **檔案**：`LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs:111-136`
  我實際執行 `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`，結果是 **建置失敗，8 個編譯錯誤**，與任務描述聲稱的「成功：0 errors」直接矛盾。
  原因：`FakeRichMenuProcessor` 沒有實作目前 `ILineRichMenuProcessor.cs` 介面全部成員（缺 `GetDefaultRichMenuIdAsync`、`CancelDefaultRichMenuAsync`、`DeleteRichMenuAliasAsync`、`GetRichMenuAliasListAsync`），且回傳型別用了不存在的 `Line.Messaging.RichMenuResponse` / `RichMenuAliasResponse`（實際介面是 `Task<IList<ResponseRichMenu>>` / `Task<RichMenuAlias>`）。這代表介面在後續某次修改中新增/改名了成員，但這個測試檔沒有同步更新。
  **結論**：目前分支無法建置，其餘所有「dotnet test 通過 N 項」的驗證結論全部應視為過期、不可信，必須重新跑過。這是合併前必須修的阻斷性問題。

- **`LineMessagingProcessorClass.cs` / `PushUtility.cs` / `LineUtilityClass.cs` 大量繁體中文被寫成亂碼，且造成真實功能回歸**
  用 `git diff` 逐一核對，確認這不是既有問題，而是本次變更把「舊（-）」的正常可讀中文覆寫成「新（+）」的亂碼（含 `\uf55e`、`\uef9b` 等私人使用區碼位，屬 Big5/CP950 誤轉碼的典型特徵，已用 Python 對實際檔案位元組驗證，非終端顯示問題）：
  - **`LineMessagingProcessor/LineMessagingProcessorClass.cs:192`**：`if (MessageType == "模板" || MessageType == "確認")` 被改成 `if (MessageType == "璅⊥" || MessageType == "蝣箄?")`，且不像旁邊「顯示認證」判斷式那樣保留正確字串當 OR 後備。這是**真正的功能回歸**：LINE 使用者點選 postback 的「模板」「確認」選項後，`SendMessage(UserId, "您選擇了...")` 永遠不會被觸發。
  - **`LineMessagingProcessorClass.cs:167,176`**：`follow`/`unfollow` 實際發給使用者的訊息文字被改動（如「歡迎加入好牧人」→「歡迎加入。」），品牌名稱被移除，且此檔不屬於 RichMenu 重構範圍。
  - **`ChurchReport/Tools/PushUtility.cs:588-600`**：既有 Carousel 推播範例的按鈕文案（"報名"、"說明網頁"、"講員：..."）被寫成亂碼——這些是會實際送給使用者的 LINE 訊息內容，且與 RichMenu 重構完全無關。
  - **`ChurchReport/Tools/LineUtilityClass.cs`**：幾乎所有 XML doc 註解、`#region` 名稱、內部路徑常數（如 `D:\Line ?閰制?\`）都變成亂碼。
  這直接推翻任務描述「PushUtility / LineUtilityClass 的 RichMenu 成功回傳字串從亂碼修成清楚的『成功』」與「已檢查 UTF-8 without BOM + CRLF」的驗證結論——事實是反向退化。
  **最小修正方案**：對這三個檔案做逐行比對，只保留刻意要做的修改（改呼叫 `LineMessagingProcessorRichMenuAdapter`、新增 `using LineMessagingProcessor.RichMenus;`），其餘所有註解與字面量還原成變更前的正確中文；`MessageType == "璅⊥" || MessageType == "蝣箄?"` 必須改回 `"模板"` / `"確認"`。

- **`LineRichMenuTextTriggerResolver` 保留雙重建構子，是未來的 DI ambiguous-constructor 地雷**
  **檔案**：`LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs:7,12`
  目前 DI（`AddLineRichMenus`）只註冊 `LineRichMenuTextTriggerOptions` singleton，所以現狀能正確解析，不會拋例外。但 `LineRichMenuOptions`（`LineRichMenuTextTriggerOptions.cs:12`）的文件註解明白寫著「未來若加入角色、流程狀態或預設 menu，也集中在此擴充」——這正是被設計來擴充、之後極可能被註冊進 DI 的型別。一旦有人也把 `LineRichMenuOptions` 註冊為服務，`LineRichMenuTextTriggerResolver` 會因兩個建構子都可被滿足而在 runtime 丟出 `InvalidOperationException`（ambiguous constructor）。
  **最小修正方案**：刪除 `LineRichMenuTextTriggerResolver(LineRichMenuOptions options)` 這個建構子，只保留 `(LineRichMenuTextTriggerOptions)`；需要 `LineRichMenuOptions` 時在註冊處呼叫 `.ToTextTriggerOptions()` 轉換即可（呼叫端已有這個方法可用），不需要類別本身承擔兩種輸入形狀。

## Warning 🟡

- **`LineRichMenuDefinition` 對同一概念暴露兩組屬性名稱**
  **檔案**：`LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs:36-64`
  `MenuKey`/`Key`、`AliasId`/`Alias`、`RichMenu`/`Layout` 三組屬性各自指向同一個 backing field，是同一資料的兩個名字。這類重複 API 會讓呼叫端不確定該用哪一個，且日後兩者其中一個被改名/棄用時容易漏改。建議只保留一組命名（例如保留 `MenuKey`/`AliasId`/`RichMenu`，移除 `Key`/`Alias`/`Layout`），符合任務要求的「一個東西只做一件事、資料流清楚」。

- **`InMemoryRichMenuStateStore` 沒有標示「僅為預設可替換實作」**
  **檔案**：`LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:5`
  對照 `InMemoryLineRichMenuIdCache.cs:5-8` 有明確 XML 文件註解說明「這是預設輕量實作；正式產品可用資料庫、Redis 或其他持久化儲存替換」，`InMemoryRichMenuStateStore` 完全沒有註解。它保存的是使用者目前綁定的 RichMenu 狀態與到期時間，重啟或多執行個體部署時會遺失，風險比 id cache 更高，建議補上等同的說明註解。

## Info 🟢

- Boundary 測試（`RichMenuProjectBoundaryTests.cs`）目前只掃描 `.cs` 檔文字內容比對禁用字串，沒有檢查 `.csproj` 的 `ProjectReference`。以目前狀態沒問題（`LineMessagingProcessor.RichMenus.csproj` 只參照 `Line.Messaging` 與 `LineMessagingProcessor`），但若未來想更嚴謹防止誤加產品專案參照，可以額外對 csproj 內容做同樣的關鍵字掃描。
- `RichMenuOrchestrator`、`RichMenuDecision`、`RichMenuDecisionPriority`、policy pipeline 的設計本身乾淨：單一 public 建構子、決策依優先權挑選、無 concrete-only 分支，符合任務要求的「統一經過 policy pipeline」。這部分沒有發現問題。
- Provisioning workflow（`LineRichMenuProvisioningWorkflow.cs`）已確認：PNG 只讀一次（`ReadAllBytesAsync` 後同時用於 fingerprint 與上傳的 `MemoryStream`），無 `.GetAwaiter().GetResult()`，`LineRichMenuFingerprint.BuildName` 兩個多載職責清楚（byte[] 或已算好的 fingerprint）。這部分修正確實到位。

## Summary

**未發現「RichMenu 共用核心本身」的架構性 Critical 問題**——orchestrator/policy pipeline、provisioning 的 fingerprint 與 stream 處理、產品邊界（無 ChurchReport/CRM/Controller 依賴）都做得乾淨。

但發現三個必須先解決才能合併的 Critical 問題：(1) 目前工作樹**實際上建置失敗**（`LineMessagingProcessor.AspNetCore.Tests` 與現行 `ILineRichMenuProcessor` 介面不同步），(2) **三個既有檔案的繁體中文被本次變更寫成亂碼**，其中至少一處（`MessageType == "模板"/"確認"` 判斷式）是會影響正式環境使用者的功能回歸，(3) `LineRichMenuTextTriggerResolver` 留了一個看似無害、但依設計意圖幾乎必然會在未來觸發的 DI ambiguous-constructor 地雷。

**建議：Request changes**（不能合併），先修上述三項 Critical，再重新完整跑一次 `dotnet build ChurchReport.sln` 與所有測試專案以取得真實可信的驗證結果。

---
SESSION_ID: 3f63ebff-ce99-4fe5-991b-8fa65a46751d
