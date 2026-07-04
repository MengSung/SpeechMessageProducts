# LINE RichMenu 共用架構修復後複審報告

## 範圍確認
已比對 commit `f4ec65ff`（重構RichMenu）與相關檔案，涵蓋 `LineMessagingProcessor.RichMenus`、`.Tests`、`LineMessagingProcessor.AspNetCore(.Tests)`、`ChurchReport` 的實際 `.cs`/`.csproj` 變更（該 commit 內大量檔案為 `.ccg/dual-model-runs` 產出的審查紀錄，非程式碼變更，已排除）。

## Critical 🔴
無 Critical 發現。8 項關鍵修復逐一核實如下：

1. ✅ `LineRichMenuProvisioningWorkflow.SyncDefinitionAsync`（`LineRichMenuProvisioningWorkflow.cs:81-89`）只呼叫一次 `PngImageStreamFactory`，讀出 `imageBytes` 後供 fingerprint 計算與 `new MemoryStream(imageBytes, writable:false)` 重複使用，無重新開啟串流、無 `.GetAwaiter().GetResult()`。
2. ✅ `LineRichMenuFingerprint.BuildName` 現有兩個多載：`(definition, byte[] pngBytes)` 與 `(definition, string fingerprint)`，呼叫端傳入已讀出的 bytes 或現成 fingerprint（`LineRichMenuFingerprint.cs:14-48`）。
3. ✅ `RichMenuOrchestrator` 僅有一個公開建構子（`RichMenuOrchestrator.cs:12`）。
4. ✅ 文字觸發改走 `LineRichMenuTextTriggerPolicy : IRichMenuPolicy`（`LineRichMenuTextTriggerPolicy.cs`），並透過 `TryAddEnumerable(ServiceDescriptor.Transient<IRichMenuPolicy, LineRichMenuTextTriggerPolicy>())` 正確掛進 DI。
5. ✅ 全域搜尋確認 `HandleTextAsync`、`RichMenuTextContext`、`RichMenuTextDecision` 在 RichMenu 相關專案已不存在。
6. ✅ `LineRichMenuTextTriggerResolver` 僅一個公開建構子，接受 `LineRichMenuTextTriggerOptions`（`LineRichMenuTextTriggerResolver.cs:7`）。
7. ✅ `LineMessagingProcessorServiceCollectionExtensionsTests.cs` 中的 `FakeRichMenuProcessor` 已完整實作 `ILineRichMenuProcessor` 全部 15 個成員。
8. ✅ `ChurchReport/Tools/LineUtilityClass.cs:662,676` 與 `PushUtility.cs:425,447` 的成功字串已是正確 UTF-8「成功」，非亂碼。

邊界掃描複驗：`LineMessagingProcessor.RichMenus` 目錄下無 `ChurchReport`/`Microsoft.Xrm`/`IOrganizationService`/`DbContext`/`Controller`/`IActionResult` 字樣；`LineMessagingProcessor.Workflows` 無 RichMenu 殘留檔案；舊型別（`LineRichMenuOptions`/`RichMenuResponse`/`RichMenuAliasResponse`）與 `GetAwaiter().GetResult()`/`PngImageStreamFactory(CancellationToken.None)` 在 RichMenu 相關程式碼中皆未重現（其餘專案中出現的 `GetAwaiter().GetResult()` 是既有、與本次重構無關的 Session/Payment 程式碼，非本次改動引入）。

## Warning 🟡

- **`InMemoryRichMenuStateStore.cs:1-57`**：完全沒有 XML 文件註解說明「僅記憶體、非持久化」。對照 `InMemoryLineRichMenuIdCache.cs:3-5` 有明確寫「正式產品可用資料庫、Redis 或其他持久化儲存替換」，`InMemoryRichMenuStateStore` 卻沒有對等說明。此型別保存的 `RichMenuUserState`（含 `ExpiresAt`、`PreviousMenuKey`）攸關到期還原邏輯，未來產品若忽略此點，在多執行個體或重啟後會直接遺失使用者的選單還原狀態。建議補上與 `InMemoryLineRichMenuIdCache` 一致的文件註解。
- **`RichMenuExpirationSweepWorkflow.cs` 無對應測試**：`LineMessagingProcessor.RichMenus.Tests` 目錄下找不到任何 `*Sweep*`/`*Expir*` 測試檔。此 workflow 負責到期還原（unassign 或還原至 `PreviousMenuKey`），目前完全零覆蓋，屬於 checklist 中「provisioning/assignment/text trigger/DI/boundary 測試缺口」同類問題，建議至少補上「有過期記錄→呼叫 AssignAsync 還原」與「無 PreviousMenuKey→呼叫 UnassignAsync」兩個案例。

## Info 🟢

- **`LineRichMenuAssignmentWorkflow.cs:15-32`** 保留兩個公開建構子（4 參數主建構子 + 2 參數便利建構子，內部 `new InMemoryRichMenuStateStore()`）。因參數數量不同（4 vs 2），DI 解析不會產生歧義（已由現有 `ValidateOnBuild=true` 測試證實可正常解析），非缺陷；但與本次「收斂為單一公開建構子」的整體修復方向不完全一致，未來如想追求風格一致，可考慮把 2 參數版本改為 named 工廠方法（如 `CreateWithInMemoryState`），而非保留兩個 `public` 建構子。
- **`LineRichMenuFingerprint.BuildName` 兩個多載同名不同參數型別**（`byte[]` vs `string`）容易在呼叫端造成型別混淆（例如誤把已算好的 hex fingerprint 字串當成 bytes 陣列傳入不會編譯錯誤，但語意不同）。可考慮兩個多載使用不同方法名稱（如 `BuildNameFromBytes` / `BuildNameFromFingerprint`）以提高可讀性，非必要阻擋合併項目。

## 合併建議

**建議合併（Approve）。**

無 Critical 發現；8 項既定修復皆已核實落地，build（0 warning/0 error）與各測試套件（13+4+33+28 全過）狀態與報告一致，邊界掃描與舊型別殘留掃描皆乾淨。兩項 Warning（`InMemoryRichMenuStateStore` 缺文件註解、`RichMenuExpirationSweepWorkflow` 缺測試）不影響現有功能正確性，屬於文件與測試覆蓋率補強項目，可在合併後以後續 PR 處理，不須阻擋本次合併。

---
SESSION_ID: 7104485e-2330-442f-944f-710bcc65e434
