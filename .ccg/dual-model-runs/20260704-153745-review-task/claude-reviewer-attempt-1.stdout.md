# LINE RichMenu 共用 Orchestrator 修復後複查報告

## 一、Critical 🔴

**無 Critical 發現。**

逐項對照 checklist 並實際讀取原始碼後確認：
- `LineRichMenuProvisioningWorkflow.SyncDefinitionAsync`（`LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:81-121`）只開啟一次 PNG stream、讀成 byte[] 後同時餵給 `LineRichMenuFingerprint.Create` 與後續上傳，沒有重複讀取或 sync-over-async。
- `LineRichMenuFingerprint.BuildName` 已拆成兩個 overload（吃 `byte[]` 或吃已算好的 `fingerprint` 字串），未再對已讀完的 bytes 重新計算兩次。
- `RichMenuOrchestrator` 與 `LineRichMenuTextTriggerResolver` 都只剩一個 public 建構子。
- `LineRichMenuTextTriggerPolicy : IRichMenuPolicy` 已取代舊的具象 `HandleTextAsync` 路徑；全域掃描 `HandleTextAsync` / `RichMenuTextContext` / `RichMenuTextDecision` / `LineRichMenuOptions` / `RichMenuResponse` / `RichMenuAliasResponse` 均為 0 命中。
- `LineMessagingProcessor.AspNetCore.Tests` 的 `FakeRichMenuProcessor` 完整實作 `ILineRichMenuProcessor` 全部 15 個成員，型別對齊。
- Boundary 掃描（含專案內建的 `RichMenuProjectBoundaryTests.cs`）確認 `LineMessagingProcessor.RichMenus` 內無 `ChurchReport` / CRM / Controller / DbContext / IActionResult 字樣；`LineMessagingProcessor.Workflows` 內無 RichMenu 殘留檔案。
- `ChurchReport/Tools/LineUtilityClass.cs:689,698` 與 `PushUtility.cs` 的 RichMenu 成功回傳字串已是正常的「成功」，非亂碼。

## 二、Warning 🟡

- **[ChurchReport/Tools/PushUtility.cs:71-79](), [ChurchReport/Tools/LineUtilityClass.cs:271-279]() 與 [ChurchReport/Startup.cs:488]() — 兩套互不相通的 `IRichMenuStateStore`**
  `Startup.cs` 用 `services.AddLineMessagingProcessor(...)` 走 DI，`AddLineRichMenus()` 內部把 `IRichMenuStateStore` 註冊成 **singleton**（`TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>()`）。但 `PushUtility` 與 `LineUtilityClass` 各自的 `CreateDefaultRichMenuAssignmentWorkflow(...)` 在沒有外部注入 workflow 時，會 `new InMemoryRichMenuStateStore()` 建立**自己專屬、與 DI singleton 完全無關**的一份狀態儲存。
  而目前 repo 內至少 10 處以上是直接 `new PushUtility(...)`（例如 `DonationPaymentManager.cs:164`、`DonationFeePaymentProcessor.cs:101,143`、`PersonalQrCodeUtility.cs:69`、`QrCodeUtility.cs:81`、`RecurringDonationPaymentProcessor.cs:71`、`SmallGroupQrCodeUtility.cs:79`、`SundayQrCodeUtility.cs:69`、`LineNotifyUtility.cs:54`、`DonationPaymentProcessor.Core.cs:118`），每次呼叫都是全新實例、全新空的狀態儲存。
  - Why：`LineRichMenuAssignmentWorkflow.AssignAsync` 的「已在此選單就不重複 link」去重判斷，以及 `RichMenuExpirationSweepWorkflow` 的到期還原機制，都依賴同一份 `IRichMenuStateStore`。這些舊入口寫入的狀態，DI 端的到期掃描永遠讀不到；反過來，DI 端寫入的狀態，這些舊入口也看不到。結果是：透過 `PushUtility.AddRichMenuMessage` / `LineUtilityClass.AddRichMenuMessage` 指派的使用者，其「上一個選單」與「到期還原」對這些呼叫路徑而言形同虛設，且每次呼叫都會被判定為「有變化」而重打一次 LINE link API。
  - Fix 建議：讓 `PushUtility` / `LineUtilityClass` 透過 DI 取得同一個 `IRichMenuStateStore`（例如建構子注入或走 `IServiceProvider`），而不是各自 new 一份；或至少在文件註明這兩條路徑目前無法參與到期還原。

- **[ChurchReport/Tools/LineUtilityClass.cs:318-339]() — `RebuildDefaultWorkflowsForCurrentClient` 換 token 時重建全新狀態儲存**
  `SetupChannelAccessToken` 觸發時，若使用預設 assignment workflow，會呼叫 `CreateDefaultRichMenuAssignmentWorkflow` 產生一個**全新** `InMemoryRichMenuStateStore()`，讓該實例先前累積的使用者狀態全部消失。目前掃描全 repo，`SetupChannelAccessToken` 沒有任何呼叫端（可能是死碼），暫時風險不高，但一旦未來被接上（例如多機構切換 channel token 的流程），會讓同一實例生命週期內的 RichMenu 狀態被靜默清空。建議之後若要啟用該方法，一併檢討是否該保留舊的 state store 而只換 processor。

## 三、Info 🟢

- **[LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:5-9]()** 的中文註解已清楚說明「僅供開發/測試，重啟或多機部署不保留/不同步」，做得很好；但 DI 註冊（`AddLineRichMenus`）預設就是這個非持久實作，且沒有啟動期 log/warning 提示。建議之後若偵測到 `IRichMenuStateStore` 仍是預設的 `InMemoryRichMenuStateStore` 且環境非 Development，記一筆警告 log，避免未來產品沿用預設值卻誤以為狀態會跨重啟保留。
- **[LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs:13-28]()** 用逐行字串比對做邊界檢查，只能抓到字面命中，無法抓到別名/字串組合等規避手法；作為煙霧測試已足夠，但不是嚴謹保證，未來若要更嚴格可以考慮加上 Roslyn 語意分析。
- **[LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs:14,30]()** 兩個 `BuildName` overload 分別吃 `byte[] pngBytes` 與 `string fingerprint`，簽章相近容易在呼叫端混淆（目前呼叫端都用對了）；可考慮改名如 `BuildNameFromBytes` / `BuildNameFromFingerprint` 提升可讀性，非必要。

## 四、驗證結果覆核

已提供的驗證紀錄（4 個測試專案共 78 個測試通過、`dotnet build` 0 warning/0 error、boundary/legacy 掃描皆乾淨）與本次重新讀碼結果一致，未發現與報告不符之處。額外新增的 `RichMenuExpirationSweepWorkflowTests.cs`（3 個案例：還原上一個選單／無上一個選單則 unassign／未到期不處理）邏輯正確，且與 `RichMenuExpirationSweepWorkflow.SweepAsync` 的實作吻合——已到期項目經由 `AssignAsync`/`UnassignAsync` 處理後，會分別因為狀態被覆寫（清空 ExpiresAt）或被 `RemoveAsync` 移除，不會在下一輪掃描重複觸發。

## 五、合併建議

**可以合併（Approve）。** 共用核心（`LineMessagingProcessor.RichMenus`）本身的正確性、邊界純淨度、建構子唯一性、文字觸發重構等既定修復項目全數驗證通過，沒有 Critical 問題。上述兩項 Warning 屬於 **ChurchReport 產品層**（非共用核心）在「舊入口未接回 DI 單例狀態儲存」的架構債，不影響本次共用核心重構的正確性與可合併性，但建議另開後續任務追蹤，避免到期還原機制在正式環境對舊入口使用者形同虛設。

---
SESSION_ID: e9329a2b-2d2c-4461-8d60-7fd77660d7f5
