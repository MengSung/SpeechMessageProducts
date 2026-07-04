## 審查結果：LINE RichMenu 共用架構後修復審查

### Critical 🔴（發現 1 項，嚴重度高於既有 checklist 所有項目，**必須修復才能合併**）

**檔案編碼損毀（mojibake）已回歸進多個既存檔案，且不只是註解損毀，還造成真實的功能性錯誤與使用者可見文字損壞。**

比對 `git diff HEAD` 與 `Read` 工具實際解碼結果，確認以下檔案的中文字串在這次修改中被「雙重編碼」損毀（很可能是某個工具以錯誤的編碼（如系統 CP950/Big5）讀寫了本來是 UTF-8 的既存檔案，全新建立的 `LineMessagingProcessor.RichMenus` 檔案完全沒有此問題，可見損毀只發生在「本次被修改的既存檔案」上）：

1. **`LineMessagingProcessor/LineMessagingProcessorClass.cs:192`** — 真正的邏輯錯誤（不只是註解）：
   ```csharp
   -   if (MessageType == "模板" || MessageType == "確認")
   +   if (MessageType == "璅⊥" || MessageType == "蝣箄?")
   ```
   字面比對值被改成亂碼，此分支之後永遠不會成立，「您選擇了...正在處理中」的回覆邏輯會變成死碼。

2. **同檔案 `SetupBindingMessage`（約 596 行附近）** — 實際發送給使用者的 LINE 綁定引導訊息被摧毀：
   ```csharp
   -   "請點擊以下網址進行牧養系統與Line的註冊:"
   +   "隢??誑銝雯??脰??折?蝟餌絞?ine?酉??"
   ```

3. **`ChurchReport/Tools/PushUtility.cs` 的 `ChurchCarouselMessage()`** — 真實會發送給教會會友的 LINE Carousel 訊息內容全部損毀：講員真實姓名「講員：魏外楊老師」→「雓嚗?憭??葦」、「簡如牧師邀請您」→「蝪∪??批葦?隢」、「說明網頁」→「隤芣?蝬脤?」、聚會時間「時間：每週二至週五...」→亂碼、「晨禱」→「?函曲」。

4. **`LineUtilityClass.cs` 與 `PushUtility.cs` 中的 `imagePath`**：
   ```csharp
   -   var imagePath = @"D:\暫存區\richmenu.PNG";
   +   var imagePath = @"D:\?怠??\richmenu.PNG";
   ```
   若磁碟上真實資料夾仍叫「暫存區」，此路徑將不存在，legacy `AddRichMenuMessage` 流程在正式環境會直接 `FileNotFoundException`。

5. **`ChurchReport/ChurchReport.csproj`** — 連 MSBuild 的檔案路徑字面值都被損毀：
   ```xml
   -   <Compile Remove="文件\佈署規劃\**" />
   +   <Compile Remove="?辣\雿蔡閬?\**" />
   -   <None Remove="wwwroot\assets\images\永和堂牧養系統web_banner-01.jpg" />
   +   <None Remove="wwwroot\assets\images\瘞詨??擗頂蝯患eb_banner-01.jpg" />
   -   <HintPath>..\..\..\..\DevExpressDevExtreme-23.1.5版本\響應式\主要版本\...\Microsoft.Crm.Sdk.Proxy.dll</HintPath>
   +   <HintPath>..\..\..\..\DevExpressDevExtreme-23.1.5?\?踵?撘銝餉??\...\Microsoft.Crm.Sdk.Proxy.dll</HintPath>
   ```
   若磁碟實際路徑未改名，這些 Remove/HintPath 都會失效（靜默失敗，不一定會報build error，`ChurchReport.MemberInfo.Tests.csproj` 的 `NoWarn` 註解也同樣中標）。

6. **`LineMessagingProcessorClass.cs` 內幾乎所有原本寫得很仔細的架構說明 XML doc 註解**（說明 SDK 邊界、token 處理、共用 workflow 職責等）全部變成不可讀亂碼，文件價值歸零。

任務描述中提到的「item 8：RichMenu 成功回傳字串從亂碼改為清楚字串」只有 `ConfirmMessage()` 這一處是**正確**的（"確認按鈕"→"確認訊息" 等雙邊都是合法中文，屬於刻意改字），但同一批修改把其他大量**原本正確**的字串反向劣化成亂碼。這已超出 RichMenu 重構範疇，是資料完整性與功能正確性的嚴重回歸。

**建議修復方式**：對這 5 個檔案執行 `git checkout HEAD -- <file>` 還原，再手動、以正確 UTF-8 重新套用真正想要的改動（RichMenus 專案參照、`LineMessagingProcessorRichMenuAdapter` 接線、`ConfirmMessage` 改字、歡迎/取消追蹤訊息簡化），避免再用同一個造成損毀的工具/流程處理這些檔案。

---

### Warning 🟡

- **`LineRichMenuAssignmentWorkflow.cs:13-28`** 仍保留兩個 public constructor（3 參數 + 2 參數 convenience overload，預設建立新的 `InMemoryRichMenuStateStore()`），與本次對 `RichMenuOrchestrator`、`LineRichMenuTextTriggerResolver` 做的「單一 public constructor」清理不一致。目前因為 `IRichMenuStateStore` 有註冊，DI 會選 3 參數版本，暫無實際 ambiguity；但若未來有人不小心移除該註冊，DI 會靜默改用 2 參數版本並產生一個獨立、未共享的 in-memory state store，造成難以察覺的狀態遺失。建議統一成單一 constructor。
- **`InMemoryRichMenuStateStore.cs`** 沒有像 `InMemoryLineRichMenuIdCache` 一樣加上「這只是預設輕量實作，正式產品需换成持久化儲存」的說明註解。`IRichMenuStateStore` 目前用 `TryAddSingleton` 註冊為預設值，未來產品若沒注意到，容易誤以為這是可上線的持久化方案，導致重啟或多實例部署時 RichMenu 指派/過期狀態全部消失。
- **`LineRichMenuDefinition.cs`** 對同一份資料暴露兩組平行屬性名稱（`MenuKey`/`Key`、`AliasId`/`Alias`、`RichMenu`/`Layout`）。這是全新型別，沒有舊消費者需要相容，保留兩套命名只會讓未來整合的人不確定該用哪一組，建議只保留一組。

### Info 🟢

- `RichMenuContext.Roles` / `Attributes` 目前沒有任何內建 `IRichMenuPolicy` 使用（僅有文字觸發 policy），屬預期中的「留給產品端擴充」，可在類別註解補一句說明用途。
- `LineRichMenuProvisioningWorkflow.UpsertAliasAsync` 同時 catch `LineRichMenuAliasNotFoundException` 與 404 的 `LineResponseException` 做相同 fallback，邏輯有點重複，非緊急。
- 多個 `.csproj` 檔案結尾少了換行（`\ No newline at end of file`），純風格瑣事。

---

### 合併建議

**不建議合併（Do Not Merge）**。RichMenu 共用架構本身（provisioning / assignment / orchestrator / text-trigger policy / DI 註冊）設計乾淨、測試齊全（13+4+33+28 全過、邊界與 legacy 掃描皆乾淨），checklist 列出的 8 項既有修復也都確認到位，沒有發現 DI 衝突、產品耦合外洩或舊文字觸發路徑重現的問題。

但本次工作附帶引入的**編碼損毀回歸**（見上方 Critical）已造成真實的邏輯錯誤與使用者可見訊息損壞，性質比架構審查範圍更嚴重，必須先還原並修正這幾個檔案，重新驗證後才能合併。Warning 項目建議一併處理但非阻擋合併的必要條件。

---
SESSION_ID: 5a342026-2216-43e8-a877-cfc67e014f95
