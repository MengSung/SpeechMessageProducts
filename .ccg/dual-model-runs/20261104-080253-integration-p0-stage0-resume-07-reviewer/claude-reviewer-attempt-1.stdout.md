## 審查結果（階段0 第7批 / Claude 單模型 / 唯讀）

審查範圍：`image-input-contract.md`、`ImageInputValidator`/`ImageInputText`、三個 controller（MemberInfo/Personal/NewPerson）改動、`Directory.Packages.props`、相關 csproj 改動、兩份新測試檔。僅依所附安全差異＋查明確非機密原始碼（`MemberInfoController.cs`、`ImageInputValidator.cs`、`ToolUtility.csproj`、`SpeechMessageProducts.ChurchReport.csproj` 等）佐證，未讀取機密檔、未執行測試、未建立任何連線。

---

### 🟡 Warning #1 — MemberInfo 真實 action 的例外通知路徑完全未被測試覆蓋（守門假綠燈風險）

- **檔案**：`ChurchReport.MemberInfo.Tests/Images/ImageUploadSafetyTests.cs`（`Upload_UnexpectedFailure_ReportsOriginalExceptionWithoutPrivateMessage`，僅 `[InlineData("personal")][InlineData("new-direct")]`）、`ImageUploadCharacterizationTests.cs`（`AttemptUpload` 的 `route == "member"` 分支）、`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:1109-1122`
- **觸發條件**：`AttemptUpload("member", ...)` 只用反射直接呼叫私有 `NormalizeUploadedImage`，完全繞過真正的 `UploadContactImage(contactId, imageFile)` action（不過 `contactId`/`CanViewContact`、不呼叫 `service.Update`）。因此該 action 本次新增/修改的三個 catch 區塊——`catch (ImageInputRejectedException)`、`catch (FaultException)`（含 `ChurchReportLineAdminNotificationService.ReportException`）、`catch (Exception)`（同樣含通知＋固定訊息）——沒有任何自動化測試實際執行過。`Upload_UnexpectedFailure_...` 這個專門驗證「真故障不洩漏原始例外訊息、確實上報診斷 owner」的測試，其 Theory 資料刻意跳過 `"member"`。
- **影響**：未來若有人把 `MemberInfoController.UploadContactImage` 的 catch 區塊改回洩漏 `ex.Message`/`faultEx.Message`，或漏呼叫 `ReportException`，此測試套件不會偵測到——與文件聲稱「涵蓋...實際共用例外診斷 owner」的覆蓋範圍不符，屬於本輪審查重點「守門假綠燈」的具體案例。三條 controller 中唯獨 MemberInfo 這條（也是唯一多一層 `CanViewContact` 跨使用者授權）缺這段驗證，風險面相對更高。
- **最小修法**：在 `AttemptUpload` 的 `"member"` 分支改為透過真正的 `UploadContactImage` action（補上可控的 `contactId`/`CanViewContact` 假資料與可注入失敗的 SDK service），並把 `"member"` 加進 `Upload_UnexpectedFailure_ReportsOriginalExceptionWithoutPrivateMessage` 與 `AssertRejected` 系列 Theory。

### 🟡 Warning #2 — MemberInfoController 新增的兩處錯誤訊息未走 `ImageInputText`，與同批次其他兩個 controller 不一致

- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:1116, 1121`
- **觸發條件**：本次新增的 `catch (FaultException)`／`catch (Exception)` 直接硬寫字面常數 `"CRM 更新失敗，請稍後再試"`、`"上傳失敗，請稍後再試"`；而同一份 diff 中 `PersonalController.ImageUpload.cs` 與 `NewPersonController.cs` 的等價訊息都改成 `ImageInputText.GetText("Image.Upload.UpdateFailed")` / `ImageInputText.GetText("Image.Upload.Failed")` / `ImageInputText.GetText("Image.Upload.PhotoFailed")`。經查 `ImageInputText.g.cs` 已存在完全相同的字串（`Image.Upload.UpdateFailed` = "CRM 更新失敗，請稍後再試"；`Image.Upload.Failed` = "上傳失敗，請稍後再試"），證實 MemberInfoController 是重複硬寫同一段文字，而非共用既有資源鍵。
- **影響**：(1) 未來若調整 `ImageInputText.resx` 這兩個鍵的文案，MemberInfoController 會悄悄與另兩個 controller 的訊息不一致。(2) `.trellis/spec/backend/image-input-contract.md` 明確要求「不得增加 CJK baseline 或忽略 validator 來接受新顯示文字」，而 I18nCheck 依文件描述是比對 `ImageInputText.g.cs`／`SharedText.g.cs` 兩份生成檔；這兩處新增的原始碼內硬寫中文字面常數不在任一生成檔中，視 I18nCheck 實際比對方式，可能觸發新增 baseline 的禁止情境或被判為繞過守門。
- **最小修法**：將 1116、1121 行改為 `ImageInputText.GetText("Image.Upload.UpdateFailed")`、`ImageInputText.GetText("Image.Upload.Failed")`，與另兩個 controller 統一。

---

### 已確認沒有問題的部分（供交叉比對）

- **解碼邏輯與資源釋放**：`ImageInputValidator.Load`/`LoadAsync`／`BufferInput`/`BufferInputAsync` 逐行驗證無誤：非 seekable 輸入正確以 `MaximumEncodedBytes+1` 上限讀取並在成功/失敗都釋放暫存；seekable 輸入只借用、不 Dispose 呼叫端串流；尺寸計算用 `long` 避免溢位；`ValidateDimensions` 邊界（8192、40,000,000）為「超過才拒絕」，等於不拒絕，與測試 `Upload_ExactDimensionLimits_AreNotRejectedAsOversized` 一致。
- **六個解碼位置**：以 `grep` 確認 ChurchReport 專案中已無任何 `Image.Load`/`Image.LoadAsync`/`Image.Identify`/`IdentifyAsync` 直接呼叫存在於 `ImageInputValidator.cs` 之外，三個 controller 的六個既有入口（MemberInfo 正規化＋兩種縮圖、Personal 上傳＋縮圖、NewPerson 共用正規化）均已改走共用守門，未發現漏改或繞回全格式解碼器的入口。
- **機密外洩**：三個 controller 的新 catch 邏輯都不再把 `ex.Message`／`faultEx.Message` 直接塞進 JSON（原本 MemberInfo/Personal 的舊碼確實有洩漏，本次已修正一致）；`ImageInputRejectedException` 的訊息只來自固定 resx 文字，不含使用者輸入或底層解析訊息。
- **跨使用者／跨教會隔離**：`RestrictedConfiguration`／`ImageInputText` 的 `FrozenDictionary` 皆為不變、不含任何請求/使用者資料的 static；快取鍵（`InvalidatePersonalImageCache`/`InvalidateMemberImageCache`）仍只以已驗證的 `contactId` 為界，未見新引入的跨使用者資料混用風險。`MemberInfoController.UploadContactImage` 的 `CanViewContact` 授權檢查未被本次改動觸碰或繞過。
- **net48/net10 行為一致性**：本批次差異僅觸及 `SpeechMessageProducts.ChurchReport`、`PowerPlatform.Dataverse.Client`、`ToolUtility` 三個專案，三者皆為 `net10.0` 單一 TFM，未見任何 net48 Plugin/Workflow 程式碼路徑被此份差異觸碰，此審查重點對本份差異不適用。
- **CPM 新檔**：`Directory.Packages.props` 的排除邏輯（docs、.ccg/tasks 不受中央版本管理）本身邏輯正確；就地檢查全repo目前沒有任何（非註解）PackageReference 仍帶 `Version=`，未發現與 CPM 衝突的殘留版本宣告。

### 無法驗證/超出本份範圍

- `ImageInputTextData`（`ImageInputText.g.cs` 由 I18nCheck 生成）未包含在所附安全差異中，其實際生成內容與 CJK baseline 判定規則本身無法僅憑此差異驗證，上述 Warning #2 的「可能觸發 baseline」部分為依文件描述推論、已標註為風險而非確定結論。
- `MemoryAllocatorOptions.MaximumPoolSizeMegabytes = 0` 是否確實在 ImageSharp 3.2.0 完全停用池化，需實際建置/執行驗證，不在唯讀審查範圍內，不作為發現項目。

**總結**：本份差異的核心安全邏輯（信任邊界、釋放責任、跨請求隔離、訊息不洩漏）經程式碼比對判斷正確；兩項 Warning 均屬「三個 controller 本應對稱卻有一處落差」的一致性/可驗證性缺口，建議在本批次收斂前補齊，不建議視為可忽略的風格問題。

---
SESSION_ID: 4cb79081-4809-4c1d-b740-0804bb29d3bf
