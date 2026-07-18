# F07 LINE RichMenu Engine 診斷產出物審查報告(Round 1)

## 審查方法
逐一核對 `issue.md`、`review-log.md`、`evidence/*.md` 所引用的每一個 `file:line` 證據與實際原始碼(`RichMenuDecision.cs`、`RichMenuOrchestrator.cs`、`ILineRichMenuAssignmentWorkflow.cs`、`LineRichMenuAssignmentWorkflow.cs`、`ILineRichMenuProcessor.cs`、`LineRichMenuProvisioningWorkflow.cs`、`InMemoryRichMenuStateStore.cs`、`RichMenuExpirationSweepWorkflow.cs`、`InMemoryLineRichMenuIdCache.cs`、`LineRichMenuWorkflow.cs`、`ILineRichMenuWorkflow.cs`、`LineMessagingProcessorServiceCollectionExtensions.cs`、`LineMessagingProcessorClass.cs`、對應測試檔),並檢查是否有目前程式碼中實際會呼叫到的路徑支持每個 Impact 敘述。

---

## Critical 🔴
無。六項 retained 結論的 `file:line` 證據全數與現況原始碼相符,未發現偽陽性或行數錯置。

---

## Warning 🟡

### W1. `F07-001` 的 Impact 敘述誇大目前實際曝險程度
- 產出物:`docs/project-modular-diagnostics/F07-line-richmenu-engine/issue.md:38`、`evidence/security-analysis.md:25`
- 來源證據:確認 `RichMenuDecision.Ttl`(`RichMenuDecision.cs:56`)、`RichMenuOrchestrator.cs:102` 不傳遞 `best.Ttl`、`LineRichMenuAssignmentWorkflow.cs:148` 恆寫 `expiresAt: null` 均正確。但我進一步查證,目前 repo 中**沒有任何** `IRichMenuPolicy` 實作(唯一內建的 `LineRichMenuTextTriggerPolicy.cs:46`)會呼叫 `RichMenuDecision.Assign(..., ttl: ...)`;`SpeechMessageProducts.ChurchReport/**` 亦未發現任何 `RichMenuDecision` 用法;測試專案中也沒有任何 `Ttl` 相關斷言。
- 應變更內容:`issue.md` 的 Impact 段落應補充一句澄清,例如「目前 F07 內建與已知消費端均未有 policy 傳入 `ttl`,此為契約層級的潛伏缺陷(latent contract defect),尚無現行呼叫路徑觸發」。這不影響「Confirmed」判定(這是一個真實、會在第一個使用 TTL 的 policy 出現時爆炸的設計缺口),但目前 Severity 定為 **High** 略嫌高估即時風險;建議改列 **Medium(伴隨『一旦被使用即為 High』的備註)**,或保留 High 但需明確標註「無現行 caller,風險為前瞻性」以免誤導後續 CCG round 或修復優先序排程。
- 判定:**Confirmed,建議 downgrade 敘述精確度(可維持 High 分類但需加註前提)**。

### W2. `performance-analysis.md` 中已標記為 Confirmed 的 `F07-PERF-002` 未被提升為 `issue.md` retained issue,造成產出物間不一致
- 產出物:`evidence/performance-analysis.md:28-43`(標題明確為「## Confirmed Performance Findings」,`F07-PERF-002` 與 `F07-PERF-001`、`F07-PERF-003` 並列同一層級) vs. `issue.md`(Summary 聲稱僅 6 項 retained issues,且 Non-Retained Observations 段落完全沒有提到 `F07-PERF-002`)
- 額外佐證:`extraction-analysis.md:36-51` 為 `F07-PERF-002` 專門設計了一個 extraction seam(建議 #2「Provider menu resolver / provisioning index」),代表撰寫者本身認定此問題足夠重要到需要架構調整,卻沒有同步在 `issue.md` 中登記為 retained issue 或至少在 Non-Retained Observations 中說明降級理由。
- 應變更內容:應在 `issue.md` 新增第 7 項 retained issue(例如 `F07-007`,對應 `F07-PERF-002`),並附上 `LineRichMenuAssignmentWorkflow.cs:240/248/254/255/262/269/270` 與測試 `LineRichMenuAssignmentWorkflowTests.cs:104` 的證據(此部分證據我已核對存在且正確);或者,若刻意不 retain,必須在 Non-Retained Observations 中明確寫出理由(例如「僅在 cache miss 時觸發,且有 fallback catalog,故不列入 retained issue」)。目前完全缺漏任何說明,屬於產出物內部矛盾。
- 判定:**遺漏 retained issue,需修正(非偽陽性,是漏列)**。

### W3. `F07-PERF-004`(copy-on-write cache 全量複製)同樣為 Confirmed 但未在 `issue.md` 說明去留
- 產出物:`evidence/performance-analysis.md:60-73`
- 該項目本身已在 Impact 段落自陳「風險較低,因選單鍵數量通常受 catalog 大小限制」,因此不 retain 為 top-level issue 屬合理判斷,但同樣缺乏在 `issue.md` Non-Retained Observations 中的交代。
- 應變更內容:建議在 `issue.md` Non-Retained Observations 補一行,如「`F07-PERF-004`(InMemoryLineRichMenuIdCache 全量複製)已評估為低風險,catalog 規模通常有界,不列入 retained issue」,以維持產出物之間的可追溯性(traceability)。
- 判定:**Info 等級的可追溯性缺口,建議補充但不影響本輪 Confirmed 判定**。

---

## Info 🟢

### I1. `issue.md` 六碼 ID(F07-001~006)與 `security-analysis.md`/`performance-analysis.md` 的 ID(F07-SEC-00X / F07-PERF-00X)之間沒有交叉引用欄位
逐條比對後對應關係如下,均正確:
- `F07-001` ↔ `F07-SEC-001`
- `F07-002` ↔ `F07-SEC-002`
- `F07-003` ↔ `F07-SEC-003`
- `F07-004` ↔ `F07-PERF-001`
- `F07-005` ↔ `F07-PERF-003`
- `F07-006` ↔ `F07-SEC-004`

雖然證據文字本身重複貼了完整 file:line,不影響正確性,但若在 `issue.md` 每項標題旁加註對應的 SEC/PERF ID,可加快日後 CCG round 交叉核對速度。屬於建議性優化,非缺陷。

### I2. 逐項覆核結果(供 CCG round 記錄用)

| Issue | 判定 | 證據充分性 | 歸屬 |
|---|---|---|---|
| F07-001 | Confirmed(見 W1 備註) | 充分,行號全數核對正確 | F07 owned(TTL 契約完全在 F07 邊界內:`RichMenuDecision`→`RichMenuOrchestrator`→`ILineRichMenuAssignmentWorkflow`→`LineRichMenuAssignmentWorkflow`) |
| F07-002 | Confirmed | 充分 | F07 owned(state store 與 assignment workflow 皆為 F07 邊界內元件) |
| F07-003 | Confirmed | 充分 | F07 owned(provisioning workflow 完全在 `LineMessagingProcessor.RichMenus/**`) |
| F07-004 | Confirmed | 充分,含 read-only adapter 佐證(`LineMessagingProcessorClass.cs:361/375/393/424/452` 核對無誤) | **主責 F07**(`ILineRichMenuProcessor` 契約缺 token),但根本修復需 F04/F05A 配合(底層 `LineMessagingClient`/`ILineMessagingClient` 亦無 token 參數)。`extraction-analysis.md` 建議 #4 已正確承認此跨模組相依,無需修正。 |
| F07-005 | Confirmed | 充分 | F07 owned(`InMemoryRichMenuStateStore` 為 F07 元件);DI 預設值註冊雖在 `LineMessagingProcessor.AspNetCore`(唯讀範圍),但屬 `TryAddSingleton` 可覆寫預設值,非該模組專屬缺陷,無需delegate。 |
| F07-006 | Confirmed | 充分,含測試斷言核對(`LineRichMenuWorkflowTests.cs:83-86`) | F07 owned |

未發現應該 downgrade、upgrade、merge 或 reject 的項目 — 六項全數 Confirmed 且證據精確。

### I3. 產出物結構性檢查結果
- **Scope manifest 邊界**:`scope-manifest.md` 所列 owned/read-only 路徑與任務指定範圍完全一致,`Explicit Exclusions Honored` 段落亦誠實揭露排除 ChurchReport 業務邏輯、LINE SDK transport、ASP.NET DI 內部細節,符合 diagnosis-only 分工原則。✅
- **issue.md 狀態**:頂端為 `Status: PENDING_CCG_REVIEW`,非 draft/initialized,符合要求。✅
- **CCG round history 佔位符**:六項 retained issue 皆有「CCG round history: Round 1: PENDING.」欄位。✅
- **Extraction 建議依模組界線而非檔案大小**:`extraction-analysis.md` 明確聲明依「module contracts and behavior boundaries」,並在「Not Recommended as Extraction」段落明確拒絕以檔案大小作為切分理由(如 DTO/result classes)。✅
- **Runtime validation plan 未違反 diagnosis-only 限制**:所有 `dotnet test`/restore/build 指令僅列於「Deferred Validation Commands」,明確標註「Run only after the diagnosis-only restriction is lifted」,且開頭聲明本輪未執行任何禁止指令。✅ 額外附帶價值:正確記錄了 `RichMenuProjectBoundaryTests.cs:61` 因方案重新命名(`ChurchReport.sln`→`SpeechMessageProducts.sln`)而可能失效的已知限制,與 `issue.md` Non-Retained Observations 一致。

---

## 結論

六項 retained 結論(`F07-001`~`F07-006`)的技術判定、嚴重度分類與 `file:line` 證據經逐條核對後**全數屬實,無偽陽性**,亦無需要 delegate 到 F04/F05A/F05B/B07 的錯誤歸屬。診斷產出物在流程性要求(scope manifest、狀態欄位、CCG round 佔位符、extraction 依模組界線、runtime plan 免違禁指令)上皆合格。

但存在兩處需要修正才能視為完整輪次:
1. **W1**:`F07-001` 的 Impact 敘述應加註「目前無任何 policy 實際傳入 ttl」的前提說明,避免誤判為現行可被利用的即時風險。
2. **W2**:`F07-PERF-002`(cache-miss 冷路徑重複讀圖 + 全量 provider list 查詢)在 `performance-analysis.md` 中被標記為 Confirmed、且 `extraction-analysis.md` 已為其設計專屬 extraction seam,卻未被提升為 `issue.md` 的 retained issue,亦未在 Non-Retained Observations 中說明降級理由,屬產出物間的內部不一致,需在下一輪修正。

`F07-PERF-004` 的類似缺口(W3)為建議性補充,不阻擋核准。

**`APPROVED_WITH_WARNINGS`**

---
SESSION_ID: c3d0988c-01f0-4880-8c36-034531ba4a33
