# P7.4 授權費用聯絡人讀取分析報告 — ORG-CALL-00005 (`fee.dedication.retrieve.by.contact`)

## 範圍確認
本分析僅涵蓋 `DedicationAuditController.GetFeesByContactId` 之架構與安全性評估。未執行、亦不建議任何程式碼變更、CE 請求/寫入、flag 啟用、流量切換或 P7.5/P8 行為。以下結論基於現有原始碼（`DedicationAuditController.cs:346-370`、`DonationPaymentManager.cs:687-716`、`DonationDedicationFeeFormService.cs:108-134`、`DonationFeeQueryService.cs:76-195`、`IPackage01FeeReadClient.cs`、`BaseChurchController.cs:294-328`、`DonationNavigationAccessResolver.cs`）與 `.trellis/tasks/08-12-churchreport-productclient-cutover/` 的既有決策紀錄。

---

## 1. 本提案中的關鍵安全/正確性風險

**風險 A（Critical）— 現有唯一的「會計角色」判斷邏輯是 fail-open 的 UI 顯示邏輯，不可直接挪用為授權邊界。**
`BaseChurchController.ResolveDonationManagementAccessFlag`（`BaseChurchController.cs:294-328`）在 `m_LoginContact` 為 null 或任何步驟拋例外時，會靜默 catch 並 fallback 到 `m_DonationPaymentFormModel.IsAOfficeWorker == true`。這個 fallback 是為了「不要讓導覽列渲染失敗」而設計，本質上是 fail-open（用一個可能過期、與當前 request 無關的 session 快取布林值頂替角色判斷）。若提案的「helper」直接重用此方法或其呼叫鏈，會違反任務要求的 fail-closed 語意。**必須新增一支專用的授權判斷方法**，僅重用 `DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)` 與 `new_church_jobtitle` 解析邏輯本身，但去除 catch-and-fallback 行為：任何解析失敗（`PersonalInfomationModel` 為 null、`m_LoginContact` 為 null、屬性讀取例外）都必須回傳「拒絕」，不得回退到 `IsAOfficeWorker` 或任何 session 快取旗標。

**風險 B（Critical）— 拒絕回應必須與「格式錯誤 id」等其他失敗路徑不可區分，避免成為角色/存在性 oracle。**
提案步驟順序（1. 授權 → 2/3. 依 flag 查詢）若嚴格遵守，即可避免此問題；但實作時容易誤把「id 為空」的既有 early-return（`GetFeesByContactId` 目前第一行 `if (string.IsNullOrEmpty(id)) return ... "missing id"`）保留在授權檢查之前。**授權檢查必須先於任何 id 格式判斷執行**，否則未授權使用者可用「有無 id」、「id 是否為合法 GUID」的錯誤訊息差異，反向探測系統行為或帳號是否登入。

**風險 C（Warning）— `_feeRefreshLock` 語意與新 typed 分支的關係未定義。**
`DonationPaymentManager.GetDedicationFeesByContactIdAsync`（`DonationPaymentManager.cs:687-716`）目前用 `_feeRefreshLock`（instance-wide `SemaphoreSlim(1,1)`）序列化對 `m_DonationPaymentFormModel` 的存取，並在 `finally` 正確釋放。若新 typed 分支繞過此方法直接在別處呼叫 `IPackage01FeeReadClient`，就會脫離既有的序列化保護，但因為 typed 分支依提案不再寫入 `m_DonationPaymentFormModel`，理論上不需要此鎖。**建議明確決策**：typed 分支應仍經過 `GetDedicationFeesByContactIdAsync`（或同層新方法）以維持既有的「單一入口、統一 cancellation/lock 慣例」，而非在 Controller 內另行分叉呼叫鏈——這與 `DonationFeeQueryService.FillFeeListAsync` 用 `_package01Enabled` 內部分支（而非呼叫端分支）的既有慣例一致（`DonationFeeQueryService.cs:76-99`）。分叉邏輯放在 Controller 會造成 A/B 邏輯分散在兩層，增加日後稽核與回滾難度。

**風險 D（Warning）— 現有 catch 區塊會把 `e.Message` 直接回傳給前端。**
`GetFeesByContactId` 現有 `catch (Exception e) { ... message = e.Message ... }` 會洩漏內部例外文字（可能包含 CRM SDK 或 typed client 內部錯誤細節）。這是既有缺陷，非本提案引入，但由於此端點即將處理財務資料且新增 typed fault 路徑，**若不修正，typed client 的例外訊息也會經同一 catch 洩漏**。是否修正屬於本任務範圍外的決策，但必須在報告中提出以待後續處理（見第 4 節）。

**風險 E（Info）— 「同一角色可查詢任意聯絡人資料」是既有政策，非本次新增的授權缺口，但需與產品擁有者再次確認。**
依任務描述之「既有產品政策」，會計角色可稽核任意聯絡人的奉獻費用（這正是稽核工具的設計目的）。本提案的授權邊界只是把「誰能呼叫」鎖死，並未限制「可查詢對象只能是自己」。這符合稽核情境，但**應在報告中明確標註為假設**，而非架構決策——若產品擁有者本意是限縮查詢範圍，需求會不同。

---

## 2. 會計角色範圍是否為足夠的伺服器端授權邊界？

**結論：作為此既有稽核端點的授權邊界，`CanAccessDonationManagement`（`new_church_jobtitle` 含「會計」）在語意上是足夠的**，因為：
- 這是全庫唯一、已被產品明確定義的「奉獻管理/稽核」角色判斷依據（`DonationNavigationAccessResolver.cs:32-46`、`DonationPaymentModelAssembler.cs:155-159` 兩處獨立實作同一語意，互為佐證）。
- 任務背景已明確聲明「瀏覽器提供的聯絡人 ID 不得成為身份/角色來源」，而此角色來源改為伺服器端解析的登入聯絡人（`m_LoginContact`），滿足此要求。

**但目前程式庫中沒有任何先例把此角色檢查當作「資料存取硬性閘門」使用**——現有唯二用法（`ResolveDonationManagementAccessFlag`、`MemberInfoAccessResolver` 相關三處）全部是導覽列可見性/快取用途，允許 fail-open。因此**不能直接搬用既有方法**，必須新寫一個以拒絕為預設值的獨立判斷函式（見風險 A）。

**伺服器端登入聯絡人快照不可用時的必要 fail-closed 行為：**
1. 若 `InMemoryContext.PersonalInfomationModel` 為 null → 立即拒絕。
2. 若 `m_LoginContact` 為 null：允許嘗試一次既有的 rehydrate（如 `SetPersonalInfomationViewModel()`），但該次嘗試的例外必須被視為拒絕條件，而非被吞掉後放行。
3. Rehydrate 後 `m_LoginContact` 仍為 null，或 `new_church_jobtitle` 讀取為 null/空字串/拋例外 → 拒絕。
4. 拒絕回應必須是固定格式、固定內容（例如統一的 `{ status = "0", message = "Unauthorized" }`），不得依「未登入」「已登入但角色不符」「session 過期」而輸出不同訊息或狀態碼，避免成為角色/登入狀態 oracle。
5. 拒絕路徑不得執行任何後續查詢（無論 flag 為 true/false）、不得呼叫 `ToolUtility.RetrieveEntity` 或 typed client。

---

## 3. 具體不變量與測試建議

**False-gate 相容性**
- 測試：flag=false 時，未授權呼叫仍被拒絕（先前無此檢查，因此這是行為變更，須有明確測試覆蓋新舊差異）；已授權呼叫的回傳資料（`DedicationFeeList`、`TotalAmount`）與變更前逐位元組相同（回歸測試，鎖定既有 legacy 行為不變）。
- 測試：`m_DonationPaymentFormModel` 在 false-gate 路徑下的既有「讀寫共享 session 模型」行為必須維持不變（本提案未要求改動 false-gate 的資料流，只加授權前提）。

**True-gate 無目標 Entity／無 legacy fallback**
- 測試：flag=true 且授權通過時，`ToolUtility.RetrieveEntity("contact", ...)` 全程零呼叫（可用 mock/spy 驗證呼叫次數為 0）。
- 測試：typed client 拋出例外（含 fault、timeout）或 `OperationCanceledException` 時，**不得**接著呼叫 legacy 路徑；應直接向上拋出或轉為既定錯誤回應。可用「typed client 永遠 throw」的 fake 驗證 legacy 服務完全未被觸及。
- 測試：typed client 回傳空集合、正常集合、以及總額超過 `int` 範圍（比照 `DonationFeeQueryService.FillFeeListViaPackage01Async` 既有的 `OverflowException` 慣例）三種情境的正確性。

**A/B 隔離**
- 測試：flag=false 與 flag=true 兩條路徑各自的程式路徑互不交叉呼叫（true 不呼叫 legacy 服務方法；false 不呼叫 `IPackage01FeeReadClient`）。
- 測試：flag 值僅由伺服器端設定讀取，任何 request 參數/header 都無法覆蓋 flag 判斷結果。

**取消（Cancellation）**
- 測試：傳入已取消的 `CancellationToken` 給 true-gate 與 false-gate 兩路徑，驗證：(a) 授權檢查本身若涉及 I/O（rehydrate）能正確觀察取消；(b) 若既有 `_feeRefreshLock`（或新等效鎖）被此路徑使用，`WaitAsync(cancellationToken)` 取消時鎖未被持有、無需釋放；若鎖已取得則必經 `finally`/`await using` 釋放（比照 `DonationPaymentManager.cs:687-716` 既有 try/finally 慣例與 `DonationFeeQueryService.cs` 的 `await using lease` 慣例）。
- 測試：typed client 呼叫鏈中途取消時，不遺留任何對 `m_DonationPaymentFormModel` 的部分寫入。

**原子結果／無 model 污染**
- 測試：true-gate 執行前後，快照比對 `m_DonationPaymentFormModel` 的所有欄位（尤其 `TotalAmount`、`DedicationFeeList`）完全不變。
- 測試：typed 分支的回傳值是每次呼叫獨立配置的物件（非共享/靜態/快取實例），可用參照相等性驗證兩次連續呼叫回傳不同物件實例。
- 測試：授權失敗時同樣不得產生任何 model 寫入或呼叫任一資料來源（legacy 或 typed）。

---

## 4. 明確排除於本任務外的事項

- Profile／endpoint／credential 的選擇或變更邏輯。
- 任何 CE request 或 mutation。
- `Package01FeeReadsEnabled`（或任何新 flag）之啟用、切換、流量分配。
- 對 `ToolUtility` 的大範圍重構；`RetrieveEntity` 缺乏 legacy drain lease 覆蓋的既有落差（僅 `FillFeeListAsync` 的日期區間查詢有 lease，本次 `GetFeesByContactIdAsync` 的 `RetrieveEntity` 呼叫沒有）——此為既有缺口，不在本次授權邊界修正範圍內，但建議另案追蹤。
- 現有 `catch (Exception e) { message = e.Message }` 造成的內部例外訊息洩漏（風險 D）——建議另案處理，不應與本次授權邊界變更混合提交。
- 「會計角色可查詢任意聯絡人資料」是否應限縮為僅本人／需審計日誌記錄——這是產品政策層級的決策，非架構任務可單方面決定（風險 E）。
- P7.5、P8 或任何後續遷移批次（00064、00066 等）——依 `.trellis/tasks/08-12-churchreport-productclient-cutover/batch-c-caller-shape-inventory.md:13-41` 之既有規劃，三者為獨立 sub-batch，不得混合。

---

## 附註：與既有規劃文件的一致性
`.trellis/tasks/08-12-churchreport-productclient-cutover/batch-c-caller-shape-inventory.md:28-35` 已明確記載 00005 因「瀏覽器可控 CRM ID 不得被當成金融資料存取權限」而被封存，且要求「建立 server-side selected-contact authorization」後才可解封。本提案的第 1 步（伺服器端角色重解析、fail-closed）正是該文件要求的解封條件，方向正確；但依風險 A/B 的分析，**若直接沿用現有 `ResolveDonationManagementAccessFlag` 的 fail-open 實作，則不滿足該文件要求的 fail-closed 條件**，須新增專用的拒絕優先判斷函式才能視為完成解封前提。

---
SESSION_ID: 462f0e3f-3ec9-4a28-836c-0e5a709a6062
