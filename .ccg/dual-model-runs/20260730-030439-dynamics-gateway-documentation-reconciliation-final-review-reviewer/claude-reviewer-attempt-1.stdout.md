PASS

## 審查結果分組

### Critical
無。

### Warning

**1. Development `WorkloadBindings` 索引合併未清除繼承 binding**
- 檔案：`SpeechMessage.Dynamics.Gateway/appsettings.Development.json`（`WorkloadBindings` 使用 index `"1"`）對照 `SpeechMessage.Dynamics.Gateway/appsettings.json`（index `"0"`）。
- 具體時序：.NET `IConfiguration` 對陣列以數字 index 合併，Development 新增的 index `1` 不會取代 base index `0`，因此 Development host 實際同時持有兩筆 binding。
- 判定：這是**文件已正確保留的 open gate**，非本輪新引入缺陷。`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 第 400–405、675、692、697、705、714 行與 `.ccg/tasks/dynamics-connection-compatibility/review.md` 第 401、579、634 行、`phase4-local-central-boundary-verification.md` 第 401–405 行皆一致記錄為 Warning，並明確要求「不能只把新 entry 從 1 改成 0 就假設 base 子索引已消失」，未誇大為已修正。維持 Warning，繼續追蹤。

### Info

**1. review.md「18 個被指名檔案」措辭略為不精確**
- 檔案：`.ccg/tasks/dynamics-connection-compatibility/review.md` 第 605–607 行、`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 第 1208 行。
- 觀察：文字寫「Gemini 指名的 18 個檔案」，但實際 Gemini stdout（`20260730-024616-.../gemini-reviewer-attempt-1.stdout.md`）Critical 區塊只逐一列出 12 個檔案；18 這個數字對應的是該輪 prompt「主要審查範圍」中全部 Production／Test／Config／Script（不含 SPEC／說明文件）項目數，把 `ChurchReport.MemberInfo.Tests/SessionLifecycle/` 展開成 3 個檔案後剛好等於 18。這只是措辭上「Gemini 指名」與「審查範圍全集」的混用，不是誇大 Phase 完成度或竄改編碼結果——我對其中 4 個核心檔案（`SessionScopedResourceDisposalCoordinator.cs`、`InMemoryDataContextSmallGroup.cs`、`DonationPaymentManager.cs`、`Startup.cs`）做了獨立 byte-level 複查，全部為有效 UTF-8 without BOM、無 mojibake、繁體中文註解可正常呈現，證實 Gemini 的 Critical 確實是 reviewer 端解碼誤判。建議下次撰寫時把「Gemini 指名的 18 個檔案」改成「審查範圍中的 18 個 Production／Test／Config／Script 檔案」以避免混淆，但不構成 release blocker。

**2. 本輪新 run artifacts 中仍含審查機器的 Windows 帳號路徑**
- 檔案：`.ccg/dual-model-runs/20260730-030439-.../health-attempt-1.json`、`ccg-health-20260730-030439.json`、`gemini-reviewer-attempt-1.stderr.md`、`*.prompt.md`。
- 觀察：內容含審查機器的本機作業系統帳號路徑（`C:\Users\<本機帳號>\...`），這是 CCG 工具鏈執行環境中固定的 wrapper／log 路徑資訊，過去每一輪 run（含 `20260730-024616`）也都有相同結構，並非本輪新增或與 CRM／AD FS 相關的 Session marker、Client ID、Callback 或私密 endpoint。未在此審查輸出中轉述其實際字串。判定為工具鏈既有背景雜訊，非 release blocker，也未違反第 10 項要求（該要求針對的是先前提到的 provider Session marker 與產品 workload Windows identity，掃描結果為 0）。

---

## 契約驗證結果

**1. Central／Local／Embedded／Data8／PowerPlatform.Dataverse.Client 記錄**：一致。SPEC（`dynamics-gateway-hosting-version-routing.md`）明確定義 `Gateway`/`Embedded` 兩種 enum、Central／Local 為部署拓撲差異；guide 逐題記錄討論脈絡；程式面複查 `DynamicsExecutionMode.cs` 僅有 `Gateway=0`／`Embedded=1`，`PowerPlatform.Dataverse.Client.csproj` 仍存在並被 `ToolUtility.csproj`／`SpeechMessageProducts.ChurchReport.csproj` 參照。**未發現矛盾。**

**2. `Package01FeeReadsEnabled=false`**：複查 `SpeechMessageProducts.ChurchReport/appsettings.json:559` 與 `appsettings.Development.json:6` 均為 `false`；文件全程未把 Local Gateway／Browser fail-closed smoke 誤寫成真實 CE 或 Phase 5 完成，措辭一致使用「尚未完成」「仍是後續 gate」。**未發現矛盾。**

**3. Development LocalDB／Gateway 401／403／400／Browser／AD FS marker／retired probe／host cleanup**：`review.md` 與 `phase4-local-central-boundary-verification.md` 的敘述（`/health` 200、`/ready` 200、anonymous 401、wrong alias 403、unauthorized operation 403、controlled 400 no fallback、瀏覽器 `readyState=complete`＋0 JS error、listener 5080／7244 釋放）與 SPEC 第 656–725 行的 Validation Matrix／Tests Required 章節一致，且與實際 `appsettings.Development.json` 中 LocalDB／控制平面設定結構相符。**未發現矛盾。**

**4. 真實 CE 8.2／9.1、OData 投影、跨程序容量、coordinator fault、soak/performance、Phase 5、Phase 6 open 狀態**：三份文件（`review.md`、`phase4-local-central-boundary-verification.md`、guide 第 18.18.5 節）在每一次增量結尾都重複列出這些項目為「仍開放」，未見任何一處宣稱已完成。**未發現矛盾。**

**5. Development `WorkloadBindings` index merge Warning**：見上方 Warning #1，正確保留、未誤稱已修正。

**6. 雙模型 run `20260730-024616` 整合誠實性**：Claude 逐檔 PASS（無 Critical、1 個既有 Warning、2 個 Info）；Gemini 唯一 Critical 為 mojibake 判定。本次獨立複查 4 個核心來源檔案與全部 7 個文件／SPEC 範圍檔案的 byte-level UTF-8／BOM／CRLF／final-CRLF，結果全部有效，且原始碼實際顯示為正確繁體中文，證實 review.md 對此的記述（「reviewer 解碼誤判，非真實檔案損壞」）是誠實、非誇大的整合。**未發現矛盾。**

**7. Legacy Session cache manager 根因**：guide 18.13／18.17.5、SPEC 第 561–562、576–577 行、`phase4-local-central-boundary-verification.md` 第 430–434 行三處描述完全一致：manager 本身非 `IDisposable`、共用 process-wide `ToolUtilityFactory` singleton、eviction 不得 Dispose shared singleton、真正開放項是該 singleton 缺乏 Production host-shutdown owner（Phase 6 前既有 blocker）。**未發現矛盾，亦未見把 shared singleton 從 eviction Dispose 的錯誤指導。**

**8. SPEC 可執行契約完整性**：`dynamics-gateway-hosting-version-routing.md` 各 Scenario 均保留 Signatures、Contracts、Validation & Error Matrix、Good/Base/Bad、Tests Required、Wrong vs Correct 七段結構，非僅原則敘述。**符合。**

**9. 編碼與格式硬性要求**：本次獨立複查全部 7 個範圍檔案 UTF-8 without BOM、CRLF-only、final CRLF 均為真；`task.json` 可解析；`requirements.md`／`review.md`／SPEC／`phase4-...md`／guide／review-input.md 的 Markdown fence 計數均為偶數（成對）；`git diff --check` 對 6 個受版控檔案回傳 0（passed）。**符合。**

**10. 敏感值殘留**：`20260730-024616` 與 `20260730-030439` run artifacts 掃描未見 CRM／AD FS Client ID、Callback 實值、密碼、Token 或完整私密 endpoint；僅見工具鏈固有的本機帳號路徑（見 Info #2），非本次審查要求消除的產品層 Session marker／Windows identity。**符合，且本報告未重新揭露任何禁止項目的實際數值。**

---

## 明確回答

**文件／SPEC 是否可作為後續 Phase 4～6 的權威解釋說明：是。** `dynamics-gateway-hosting-version-routing.md` 提供可執行的 owner／validation matrix／good-base-bad／tests／wrong-correct 契約，`review.md` 與 `phase4-local-central-boundary-verification.md` 提供時序化證據記錄，`guide.zh-TW.md` 提供決策脈絡，三者互相一致且無誇大完成度，可作為權威依據。

**Consumer flag 與保留元件確認：** `Package01FeeReadsEnabled=false` 持續保持（`appsettings.json`／`appsettings.Development.json` 均已複查）；`Embedded`（enum 值 1、程式路徑保留）、`Data8`／`PowerPlatform.Dataverse.Client`（`.csproj` 與 `ProjectReference`／`PackageReference` 均存在）均確認保留，未被移除。

## 結論

本輪文件與 SPEC 增量誠實、一致，無 Critical。維持 1 個既有 Warning（workload-binding index merge hardening，非本輪新缺陷）與 2 個 Info（措辭精確度、工具鏈路徑雜訊）。判定 **PASS**，可作為後續 Phase 4～6 的權威文件基礎。

---

