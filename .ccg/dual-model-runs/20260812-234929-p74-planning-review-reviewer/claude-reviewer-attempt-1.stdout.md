# P7.4 規劃審查報告：ChurchReport ProductClient 逐能力切換

審查範圍：`prd.md` / `design.md` / `implement.md`（`.trellis/tasks/08-12-churchreport-productclient-cutover/`），並對照 70-row `authoritative-gap-matrix.json` 與現行 `SpeechMessageProducts.ChurchReport` / `SpeechMessage.Dynamics.ProductClient` 原始碼。

---

## Critical（嚴重問題）

### 1. `StorLessonProjection` 本身仍以 `RetrieveEntity` 回補欄位，違反「不得以 RetrieveEntity 冒充完成」的硬性要求
- **檔案**：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs:238-254`（`MapDtos`）
- **佐證**：`SpeechMessage.Dynamics.ProductClient\Models\StorLessonRecordDto.cs:16-28` 沒有 `classStartDate` / `stageName` 欄位。
- **問題**：`GetByContactViaPackage01` / `GetByDiscipleLessonViaPackage01`（即 `design.md` Batch B 認定「只需要 lesson view 的已遷移 consumer」）回傳的是型別化 `StorLessonProjection`，**不是** `EntityCollection`。但 `MapDtos` 在組裝這個「已遷移」投影時，仍對每一列呼叫 `_utility.RetrieveEntity("new_disciple_lessons", dId)` 取得 `classStartDate`／`stageName`。
  這正是 `prd.md:41-42`（需求 3）明文禁止的行為：「若 consumer 合約仍要求 Entity 或 EntityCollection，必須先改為 typed view-model/projection，不能以 RetrieveEntity 回補冒充完成」——這裡的差異是，連「已改為 typed projection」的路徑本身都還在用 `RetrieveEntity` 回補。
- **與現況的矛盾**：矩陣中 `ORG-CALL-00061` / `00062`（`fee.dedication...` 家族 `lessons.stor.retrieve.by.contact` / `...by.disciplelesson`）狀態為 `consumer: migrated-disabled`，且 `EquipmentController.cs:373`、`MemberInfoController.cs:549` 已直接呼叫 `GetByContact`（非 EntityCollection 相容層）。這代表**兩個已標記「migrated-disabled」的 consumer，在 flag=true 時仍會觸發 legacy SDK 呼叫**，`prd.md` 已確認事實 #3 只揭露了 `EntityCollection` 相容方法（`ToEntityCollection`）是 legacy bridge，**未揭露 `MapDtos` 本身也是**。
- **對 implement.md 的影響**：Phase 3（`implement.md:35-44`）規劃「將已盤點的 projection-only caller 改為 typed projection；不得呼叫 RetrieveEntity」，但沒有意識到「改用 `GetByContact`」這個動作本身**不會**移除 `RetrieveEntity`，因為問題出在共用的 `MapDtos`，而不是呼叫端選擇 `EntityCollection` 還是 `StorLessonProjection`。若不修正，Batch B 完成後仍會被誤判為「已消除 SDK bridge」，牴觸 hard constraint「不得把 SDK Entity/EntityCollection bridge 當作已完成型別化遷移」。
- **建議**：`implement.md` Phase 3 應新增明確項目——擴充 `StorLessonRecordDto`（或對應 Gateway/Embedded operation）攜帶 `classStartDate`／`stageName`，並移除 `MapDtos` 中的 `RetrieveEntity` 呼叫；在此之前，`00061`／`00062` 都不得被文件或 check 記錄稱為「無 SDK 依賴」。

### 2. `check.jsonl` 對本次雙模型審查狀態的紀錄已經過期，與實際產出不一致
- **檔案**：`.trellis/tasks/08-12-churchreport-productclient-cutover/check.jsonl:7`
- **問題**：該行記錄「dual-model review... exceeded the user-approved 45-second wait limit before a usable backend result... review state is dual-model-not-completed」，時間戳 `23:52:00`。但 `.ccg/dual-model-runs/20260812-234929-p74-planning-review-reviewer/` 顯示該次 run（啟動於 `23:49:29`）**後續確實完成**，Gemini 已產出含 3 項 Critical 的完整審查（`gemini-reviewer-attempt-1.stdout.md`），本次 Claude 審查也已完成。
- **影響**：這是「evidence claims」類別問題——task 記錄目前對外聲稱雙模型審查未完成，但實際上已有一份完整、含有效發現的 Gemini 報告被產出卻未被納入規劃文件或 `check.jsonl`。若不更新，會讓後續執行者誤以為沒有外部審查意見可用，遺漏 Gemini 已指出的 `ToEntityCollection` N+1 與 `FillFeeListAsync` 邊界問題。
- **建議**：在 `check.jsonl` 補一筆記錄，引用本次兩份審查（Claude + Gemini）的具體檔案路徑與結論，並依 Critical #1 更新 Phase 3 計畫後才可視為「本機 review 完成」。

---

## Warning（警告事項）

### 1. `Package01FeeReadsEnabled` 是單一旗標同時控制 fee 與 stor-lesson 兩種能力，與逐能力 rollback 的敘述有落差
- **檔案**：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs:60`、`DonationFeeQueryService` 建構式（經 `DonationDynamicsAccessBootstrap.TryCreatePackage01Client` / `CreateFeeFormService`）皆讀取同一個 `DynamicsAccess:Package01FeeReadsEnabled`。
- **問題**：`design.md:47-52` 先陳述「每個 capability 使用獨立 deployment-owned gate」，但緊接著說明既有 `Package01FeeReadsEnabled` 涵蓋「已明確盤點的 Package01 read capability」（複數），實際上就是 fee date-range（00006）與兩個 stor-lesson 能力（00061/00062）共用同一把 gate。而矩陣中三列的 `rollback.owner` 都各自標為 `p7.4-capability-owner`，暗示可獨立回滾。若 stor-lesson 因 Critical #1 的問題需要單獨關閉，目前的旗標粒度會連 fee date-range 一起關閉。
- **建議**：`design.md` 應明確二選一並在 `implement.md` 落實：（a）承認現階段是「共用一個 bounded gate」，矩陣的逐列 rollback owner 只代表程式碼回退責任而非獨立旗標；或（b）在 Batch B 前拆出獨立的 stor-lesson gate。目前文件的兩段敘述彼此矛盾，容易造成回滾操作誤判。

### 2. `FillFeeListAsync` 仍以 `Entity contact` 作為方法簽章參數，呼叫端必須先執行 legacy `RetrieveEntity`／`RetrieveContactByLineId`
- **檔案**：`SpeechMessageProducts.ChurchReport\Services\DonationFeeQueryService.cs:57-60`；呼叫端 `DonationDedicationFeeFormService.cs:59-61`、`:111`（`_utility.RetrieveEntity("contact", id)`）。
- **判斷**：`design.md:38`（Batch A 表格）已明確排除「contact identity read」不在本批範圍，因此這不算文件遺漏，但 `FillFeeListAsync` 對外簽章仍暴露 `Microsoft.Xrm.Sdk.Entity`，只在方法內部取出 `contact.Id`/`fullName` 兩個純值。雖未違反 prd.md 需求 2（Entity 沒有跨越 ProductClient 邊界），但屬於「capability boundary」審查應留意的技術債：未來若要把 fee read 這個 typed path 完全獨立成可重用元件，目前的簽章會強迫呼叫端保留 SDK 依賴。
- **建議**：`implement.md` Phase 2 應明文記錄「`FillFeeListAsync` 的 `Entity contact` 參數屬於已知、刻意排除在本批的邊界洩漏，等待 contact identity read 能力批次處理」，避免日後審查誤以為是遺漏。

### 3. `implement.md` Phase 1 仍有 4 項未完成，但未寫出「dual-model 45 秒逾時」後的具體降級決策依據
- **檔案**：`implement.md:12-13`
- **問題**：計畫寫「執行一次 CCG self-healing dual-model planning review，最多等候 45 秒；若沒有可用 output，記錄『雙模型未完成』並以本機 review 繼續」，但沒有規定「45 秒後若背景程序仍在跑且稍後產出結果」時應如何回補（即 Critical #2 描述的情境）。
- **建議**：補一句規則——背景 run 完成後若仍在同一 planning cycle 內，必須回頭把結果併入 `check.jsonl`，不能因為超過等待窗口就永久忽略後續產出。

---

## Info（參考資訊）

### 1. Batch A（fee date-range）typed 邊界乾淨，介面未攜帶 Entity/EntityCollection
- `SpeechMessage.Dynamics.ProductClient\FeeReads\IPackage01FeeReadClient.cs:19-88` 全部方法皆以 `Guid`/`string`/`DateTime` 傳遞，`DonationFeeQueryService.MapFeeDto`（`:134-154`）未呼叫任何 `ToolUtility`/SDK API。這與 prd.md 需求 2 一致，Batch A 的核心轉換邏輯本身沒有發現邊界洩漏。

### 2. 旗標預設值與生命週期骨架符合「flag=false 零資源」要求
- `appsettings.json:595-597`、`appsettings.Development.json:10-12` 三個旗標皆為 `false`。`DonationDynamicsAccessBootstrap.cs` 的 `TryCreatePackage01Client` / `TryCreatePackage02...` 系列方法均在檢查 flag 之後才呼叫 `GetStartedProcessHost()`／建立 executor，`DonationDynamicsAccessProcessHost.DisposeAsync`（`:594-615`）以單一 lock 保證冪等、確定性釋放，未發現 static session/credential cache。這部分與 prd.md 需求 3、design.md 的 lifecycle 承諾相符。

### 3. 矩陣交叉核對結果：prd.md 對 CE/host evidence 的敘述準確
- 對照 `authoritative-gap-matrix.json` 中 `ORG-CALL-00005/00006/00061/00062/00064/00066`：`ceEvidence.ce91`、`hostEvidence.embedded` 均為 `succeeded`，`hostEvidence.dedicated` 均為 `evidence-pending`，與 `prd.md:20`「Dedicated evidence 仍為 evidence-pending」一致；`consumer.status`（00006/00061/00062 = `migrated-disabled`，00005/00064/00066 = `not-migrated`）也與 `prd.md`/`design.md` 的批次分類（A/B vs C）相符。**70 rows** 計數本身也核對無誤，未發現把 row 數誤當 operation 數的情形。

### 4. 未發現 P7.5/P8 範圍外洩
- `prd.md:57-63`、`design.md:72-76` 的排除條款明確，`implement.md` 全篇未出現啟用 flag、CE mutation、送 CE request 或建立 P7.5/P8 任務的步驟；Phase 4 的 no-go 分支（`implement.md:55-56`）正確地把「無 durable admission / drain-first evidence」導向「no-go」而非嘗試繞過。

---

## 結論

本次規劃文件在**整體治理骨架**（旗標預設 false、process host 生命週期、matrix 交叉核對、P7.5/P8 邊界排除）上是穩健的，但存在一個**必須先修正才能安全推進 Batch B** 的 Critical 缺口：`StorLessonQueryService.MapDtos` 讓「已標記 migrated-disabled」的 typed projection 路徑仍隱含 `RetrieveEntity` SDK 呼叫，這與 hard constraint「不得把 SDK Entity/EntityCollection 橋接視為已完成型別化遷移」直接衝突，且未被 `prd.md`/`design.md` 揭露。建議在啟動 `task.py start` 前，先把此發現與修正計畫寫入 `implement.md` Phase 3 與 `check.jsonl`，並更新雙模型審查完成狀態的紀錄。

---
SESSION_ID: d7eb073a-53ac-4e53-8faf-cbee10d83ceb
