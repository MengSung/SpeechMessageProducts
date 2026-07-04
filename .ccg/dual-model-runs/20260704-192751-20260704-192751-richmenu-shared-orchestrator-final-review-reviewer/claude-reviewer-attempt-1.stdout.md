## RichMenu Shared Orchestrator + CCG Self-Healing — Review 結果

### 1. Critical findings

**No Critical findings.**

驗證項目：
- `LineMessagingProcessor.RichMenus/*.cs` 四個檔案的變更**全部是繁體中文註解新增**，沒有任何邏輯行被修改（逐行比對 diff 確認）。獨立重建 `LineMessagingProcessor.RichMenus.csproj`：0 警告、0 錯誤。
- 對 `ChurchReport|Dataverse|CrmService|IActionResult|DbContext|SpeechMessage|Controller` 及舊特殊路徑 `HandleTextAsync|RichMenuTextContext|RichMenuTextDecision` 做 grep，`LineMessagingProcessor.RichMenus/*.cs` 內皆無命中，共用層邊界維持乾淨。
- 四個修改檔案開頭皆為 `using`/`namespace`，無 BOM，確認 UTF-8 without BOM。
- 註解內容與實際程式碼邏輯逐一核對一致，未發現誤導：
  - `LineRichMenuWorkflow.cs` 註解宣稱「新版 assignment workflow 已改成只轉 provider error，未知程式錯誤直接往外拋」→ 核對 `LineRichMenuAssignmentWorkflow.cs:239,252` 為 `catch (Exception ex) when (TryMapProviderException(...))`，未命中時例外確實會往外拋，註解屬實。
  - `RichMenuOrchestrator.cs` 註解「若兩個 policy 同優先權，先註冊者保留」→ 對應程式碼為嚴格 `>` 比較（非 `>=`），與註解一致。

### 2. Warning findings

1. **CCG 自我修復 exit code 對「雙模型成功」與「單模型降級」的區分依賴呼叫端主動讀 JSON，而非 exit code 本身**
   `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:518-524`：`summary.ok=true`（雙模型皆成功）與 `summary.degradedFallback=true`（僅一個模型成功、另一個 quota 被擋但已明確 `-AllowSingleModelWhenQuotaBlocked`）**都回傳 exit code 0**。`Start-CcgDualModelRun.ps1:116` 又只是 `exit $LASTEXITCODE` 原樣轉發。
   目前唯二區分兩者的方式是解析 `summary.json` 裡的 `ok` / `degradedFallback` 欄位；若未來任何自動化只檢查 `$LASTEXITCODE -eq 0`（例如 CI gate 或更簡化的呼叫端），會把「單模型降級」誤判為「完整雙模型成功」——這正是 AGENTS.md 自己強調「Never report a quota-blocked run as a successful dual-model review」要避免的情境。
   目前 `.trellis/spec/guides/ccg-external-review-thinking-guide.md` 與 `docs/ccg-dual-model-health-permanent-fix.md` 已經用文件明確寫出「exit 0 且 degradedFallback=true」的區分，可視為文件層已知並公告的行為，但腳本本身沒有提供獨立 exit code（例如降級用 1）作為 fail-safe，仍建議未來收斂。

2. **AGENTS.md 與新增的「Standing Fallback Policy」段落之間存在政策描述落差**
   `.trellis/spec/guides/ccg-external-review-thinking-guide.md` 新增「Standing Fallback Policy」，說明 project owner 已經**全面預先核准**單模型降級（只要另一模型成功即可繼續，非逐任務核准）。但 `AGENTS.md` 的 `Required recovery behavior` 第 5 點仍寫「Continue only if the task owner explicitly allowed a single-model fallback」，語意上仍是「逐任務明確允許」。而三份文件（AGENTS.md、`.trellis` guide、`docs/ccg-dual-model-health-permanent-fix.md`）給出的**標準範例指令現在都預設帶了 `-AllowSingleModelWhenQuotaBlocked`**，等於每次照抄範例就會自動視為「已核准」。這讓「逐任務核准」與「已全面核准」兩種語意同時存在、互相矛盾，未來 agent 若照字面讀 AGENTS.md 第 5 點，可能誤以為每次都需要額外向使用者確認，或反過來誤以為降級已是預設常態而不再留意。建議統一措辭，明確此為「專案層級已核准的常態 fallback 政策」還是「仍需每個任務單獨核准」。

3. **RichMenuOrchestrator.cs 新增註解列了三個具體產品情境（建設維修、協會會員、發票收款）**
   `LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs` 的類別註解為了說明「未來如何多產品共用」而舉了三個具體業務範例。這是共用層文件註解裡少見地寫入了假設性的未來產品情境，雖然沒有引入實際程式相依，但屬於「為假設性未來需求做設計說明」的量，若這些產品從未真正落地，日後容易變成過時、誤導的舉例（例如三個範例中有兩個目前並不存在於這個 repo）。建議改成更抽象的「多租戶/多角色情境」說明，或標明這只是說明性範例、非既定路線圖。

### 3. Info findings

1. 「保母級說明」註解風格在四個檔案中頗長，對新手很友善，但作為 XML doc summary 放在 class 最上方會讓 IntelliSense 的 tooltip 變得很長；可考慮把「保母級說明」移到 `<remarks>` 區塊（`LineRichMenuWorkflow.cs` 已經這樣做），`LineRichMenuProvisioningWorkflow.cs` 和 `RichMenuOrchestrator.cs` 目前仍混在 `<summary>` 裡，建議統一風格。
2. `RichMenuExpirationSweepWorkflow.cs` 新增註解正確指出「若未來需要逐筆錯誤報告，可以在這裡擴充」，但目前 `SweepAsync` 迴圈本身沒有 try/catch，單筆 `AssignAsync`/`UnassignAsync` 失敗會讓整個 sweep 中斷（與 `LineRichMenuProvisioningWorkflow` 的逐項 try/catch 設計不同）。這屬於既有邏輯、非本次 diff 引入，僅供未來重構參考，非本輪必須修正項。
3. `Invoke-CcgDualModelWithSelfHealing.ps1` 新增的 `fallbackAccepted` 欄位其實等同於呼叫參數 `[bool]$AllowSingleModelWhenQuotaBlocked` 的鏡射，對除錯有幫助但略為冗餘；可接受，非必須修正。

### 4. 是否建議合併/提交

**建議可以合併。** 本輪 `.cs` 變更是純註解補強（無邏輯異動、無產品相依滲入、無舊特殊路徑復辟、UTF-8 without BOM、獨立重建 0 警告 0 錯誤），符合 Linus 原則審查標準。CCG 文件與腳本變更方向正確（把 exit 0 拆成「完全成功」與「已核准降級」兩種語意並記錄在 summary.json），但存在上述 2 項 Warning（exit code 對外部呼叫端的區分力道不足、AGENTS.md 與新政策文字不一致），建議在下一輪順手澄清，不阻塞本次合併。

---
SESSION_ID: e201dd97-afc6-42af-8851-55b0ef487a30
