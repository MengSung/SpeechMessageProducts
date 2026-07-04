## Review 結論

## Review: LINE RichMenu Shared Orchestrator（Jesus_5.1.7.WorktreeRefactorRichMenu）

### Critical 🔴

- **`ChurchReport/Tools/PushUtility.cs:425,447`** 與 **`ChurchReport/Tools/LineUtilityClass.cs:662,676`**：這次 diff 把既有正確編碼的繁體中文字串「靜默轉寫」成亂碼問號（例如 `"成功"` → `"??"`），並殃及大量既有中文註解與 `"報名"`、`"說明網頁"`、`"講員：魏外楊老師"` 等文案。
  - Why：`git show HEAD` 證實這些檔案改動前是乾淨 UTF-8 中文；`file` 指令確認工作區檔案仍是合法 UTF-8/CRLF——不是 BOM 或換行問題，而是內容曾經過一次錯誤來源編碼（如 Big5/GBK）讀取後又存回 UTF-8，造成不可逆的字元轉寫。只有這 3 個「原地修改」的既有檔案中鏢，新建/重新命名的 `LineMessagingProcessor.RichMenus/*` 檔案中文都是乾淨的，可見問題來自編輯這 3 個檔案時使用的工具/流程。
  - Fix：這些回傳字串會被 LINE 訊息或記錄使用，必須用 HEAD 版本或正確編碼源重新校對整份文字內容，而不只是格式檢查。任務描述中「Encoding check：UTF-8 without BOM + CRLF」只驗證了位元組格式，並未攔到這個內容層級的毀損，之後應加上「內容跟 HEAD diff 語意比對」而非只查編碼標頭。

### Warning 🟡

- **`LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:43-51`**：`SyncAsync` 已讀出 `imageBytes` 並算出本地 `fingerprint`（完全未使用），卻又呼叫 `LineRichMenuFingerprint.BuildName(definition)`，該方法內部（`LineRichMenuFingerprint.cs:21-26`）用 `.GetAwaiter().GetResult()` 同步阻塞地**再次**呼叫 `PngImageStreamFactory` 算出第二份 fingerprint。若呼叫端的 factory 不是完全確定性/可重放（網路下載、一次性暫存流等），會導致「實際上傳的圖片」與「寫進選單名稱的版本指紋」不一致，破壞該檔頭部註解特別強調的「避免每次同步都誤判新版本」不變式，同時是雙倍 I/O + sync-over-async。第 105-118 行的私有方法 `BuildVersionedName` 因此變成完全無人呼叫的死碼。
- **`LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs` + `RichMenuOrchestrator.cs`**：文字觸發選單切換（`HandleTextAsync`）與到期還原選單（`RichMenuExpirationSweepWorkflow.SweepAsync`）兩個功能雖然測試齊全，但在目前 DI 接線下**完全沒有生產路徑會呼叫到**——`IRichMenuOrchestrator` 介面只宣告 `ApplyAsync`，DI 只組裝 `(policies, assignmentWorkflow)` 這個建構子，`_textTriggerResolver` 永遠是 null；`LineMessagingProcessorClass.ProcessMessage` 收到文字訊息只是存進 `m_Message`，未呼叫 orchestrator。`IRichMenuExpirationSweepWorkflow` 也只註冊、無任何排程器/`IHostedService` 呼叫。等於交付了兩個「測試綠燈但實際斷線」的功能，未來接手者容易誤以為已生效。建議在文件或 README 中明確標註「尚待產品端接線」，或乾脆先不放行 DI 註冊避免誤導。
- **`RichMenuOrchestrator.cs:13,21`**：兩個建構子參數個數都是 2，目前僅靠手動 factory lambda 避開 DI 容器的建構子選擇問題。若未來有人簡化成 `services.AddTransient<RichMenuOrchestrator>()` 讓容器自動解析，會因兩個建構子參數數量相同而丟出 ambiguous constructor 例外。建議合併成單一建構子（同時接受 policies + 可選 textTriggerResolver）。

### Info 🟢

- Clean boundary、DI TryAdd 冪等註冊、`LineMessagingProcessorRichMenuAdapter` 職責切分、Provisioning workflow 的 alias upsert fallback 邏輯本身寫得清楚，`LineMessagingProcessor.Workflows` 目錄也確認無 RichMenu 殘留，符合任務描述的驗證結果。
- `InMemoryLineRichMenuIdCache` / `InMemoryRichMenuStateStore` 目前僅為記憶體實作（多執行個體/重啟即遺失），但這是有意設計、留給未來產品接自己的持久化 store，非缺陷。

### Summary
**Request changes**：亂碼字串是可立即影響 LINE 使用者可見文案的內容毀損，必須在合併前修正；另有兩個「已完成但未接線」的功能（文字觸發、到期掃描）與一個 provisioning 內的重複 I/O/dead-code 問題，建議一併處理或至少標註清楚範圍，其餘共用架構切分本身設計合理。

---
SESSION_ID: 4e0847d7-83f8-4d30-a8b1-da1cd9bb550d
