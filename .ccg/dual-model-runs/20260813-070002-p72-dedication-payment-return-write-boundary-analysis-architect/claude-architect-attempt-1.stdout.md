已完成程式碼檢視，以下為 P7.2 dedication payment-return write-boundary 分析結果（僅分析、未撰寫程式碼）。

## 1. 呼叫鏈與必須分別治理的 mutation families

`RecurringDonationPaymentProcessor.HandlePaymentReturn`（`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs:167-425`）目前的完整鏈路：

| 步驟 | 程式位置 | 性質 | 對應／應對應 catalog operation |
|---|---|---|---|
| A. 讀取 `new_dedication_booking` | L177 | 讀取 | 觀察來源，非 mutation |
| B. period-001 重複防呆（`RetrieveFeeByFetchXml` + `new_paid_period=="001"`） | L218-253 | 讀取／防呆快取 | 對應現有 `P72DonationPaymentLocalObservation.HasMatchingProcessedOrder` 語意 |
| C. `contact.new_visa_info` 更新（信用卡資訊） | L258-282 | **mutation** | `OperationIds.PaymentsContactUpdateCardProfile`（ORG-CALL-00049，catalog 已建、**尚無 decision/plan builder**） |
| D. `CreateFee`→`new_fee` 建立 + `SetFeeParameter` | L301, 428-556 | **mutation** | `OperationIds.PaymentsFeeUpdateAfterPayment`（ORG-CALL-00036，**已有 Slice D baseline**） |
| E. `AssignOwner("new_fee", ...)`（在 `CreateFee` 內） | L443-453 | **mutation**（獨立於 D） | catalog 未見對應獨立 ID；owner 權威來源需另行定義（見 Critical #3） |
| F. `new_dedication_booking` 完成流程更新（`new_paid_period`／`new_dedication_booking_status`／`new_explain`） | L303-318, 341-345, 377-384 | **mutation** | `OperationIds.PaymentsDedicationCompleteRecurring`（ORG-CALL-00037，catalog 已建、**尚無 decision/plan builder**） |
| G. LINE 成功／失敗通知（payer） | L353, 387 | 外部副作用（非 CRM） | 不進 CRM catalog，但與 D/F 的 commit 順序耦合 |
| H. LINE 例外通知（管理者，含 raw stack trace） | L408 | 外部副作用 | 不進 CRM catalog，僅供對照風險 |

**關鍵觀察**：C、D、E、F 是四個各自獨立、必須分開治理的 mutation family（各自的 fixture／ledger／read-back／cleanup 都不可合併），這與現有 catalog 把它們拆成不同 `OperationId` 的設計一致。目前只有 D（`PaymentsFeeUpdateAfterPayment`）有完整的 local-only decision + plan builder + tests；C、E、F 仍只是 catalog metadata，沒有對應的本機決策層。

## 2. 最小安全的下一個本機實作增量

建議：**比照既有 Slice D 模式，為 `OperationIds.PaymentsDedicationCompleteRecurring`（步驟 F）新增一組本機 decision + plan builder**，這是風險最低、範圍最小的下一步，理由：
- catalog 條目（ORG-CALL-00037，`AllowedInputNames = ["fixtureKey","transition"]`）已存在，不需改 catalog。
- 可完全複製 `P72DonationPaymentLocalDecision` / `P72DonationPaymentLocalPlanBuilder` 的 reducer 模式，不引入新抽象。
- 不觸碰 C（信用卡遮罩）與 E（owner 指派）這兩個仍缺乏明確權威來源設計的家族。

具體檔案（推斷命名，沿用現有慣例）：
- `SpeechMessage.Dynamics.Abstractions/Operations/P72DedicationCompleteRecurringLocalDecision.cs`：純 reducer，輸入為最小去識別化觀察（`IsComplete`、`Outcome`、`HasMatchingProcessedOrder`、以及**一個由邊界預先計算好的 `IsFinalStage: bool`**——不得傳入 `new_total_stages`／`new_paid_period` 原始字串，避免把 legacy 字串解析邏輯搬進權威層）。
- `SpeechMessage.Dynamics.Abstractions/Operations/P72DedicationCompleteRecurringLocalPlanBuilder.cs`：委派 `P72ContinuationLocalOnlyPlanBuilder.Build`，`OperationId = OperationIds.PaymentsDedicationCompleteRecurring`，`Inputs = { fixtureKey, transition }`。
- `SpeechMessage.Dynamics.Tests/P72DedicationCompleteRecurringLocalDecisionTests.cs`：比照 `P72DonationPaymentLocalDecisionTests` 全部案例（含 Barrier A/B 交錯測試）。

C（`PaymentsContactUpdateCardProfile`）與 E（owner 指派權威）應留待**下一個**子任務，因為兩者都還沒有一個乾淨、已審過的「caller 不可攜帶權威」邊界設計可以複製。

## 3. 阻止 consumer cutover 或 CE evidence 的 No-Go 條件

1. Period-001 防呆讀取（L221-223）與任何未來 dispatch 之間存在 TOCTOU：RETURN_URL 與 BACKEND_URL 兩個回呼可能在防呆讀取之後、寫入之前同時通過，導致重複建立 `new_fee`。未來治理流程必須在 dispatch 前做**單次、即時、exact** 的 read-back，不能沿用這個快取式防呆判斷。
2. `CreateFee`（D）先於 `new_dedication_booking` 完成更新（F）執行，兩者之間若拋例外，會留下「fee 已建立、booking 未轉態」的部分寫入，且唯一的重試防呆（period-001 fee 是否存在）會讓後續重試直接短路返回、永遠無法補完 F。CE evidence 前必須先定義這個部分狀態的 fail-closed 對帳規則。
3. `AssignOwner` 目前以 `GetOwnerId(aContact)` 決定 owner，但 catalog 的 `ContainsForbiddenInputAuthority` 明確禁止任何名為 `owner` 的 input——這代表 owner 權威只能來自**連線層固定規則**（例如「fee owner = contact owner」），不得以任何形式成為 caller 可控輸入。此規則必須在建立對應本機層前明確落地，否則後續實作者可能誤加 `ownerId` 欄位違反既有約束。
4. 任何本機 plan 若直接攜帶 `CCToken`／`LeftCCNo`／`RightCCNo`／`CCExpDate` 等原始付款資料，而非透過去識別化 `fixtureKey`，一律 No-Go。
5. C、D、E、F 四個 mutation family 若被合併進單一 CE dispatch／單一 ledger，視為 No-Go；各自必須維持獨立 preflight／dispatch／read-back／cleanup。

## 4. 測試需求

- **Idempotency**：`HasMatchingProcessedOrder=true` → `AlreadyProcessed`、無 plan（沿用 D 現有測試，F 需新增同型測試）。
- **Timeout-after-dispatch**：`IsComplete=false` → `NoGo`／`Unavailable`、`ProhibitsReplay=true`，且無論 `Outcome` 為何都不得產生 plan。
- **Read-back**：所有 disposition 下 `RequiresReconciliation` 恆為 true；另需新增 plan-builder 層測試，證明「已證實處理過」的觀察即使重送也不會二次產生 plan。
- **Cleanup**：對 F 的 plan 斷言 `Plan.Definition.CleanupPolicy == P72LocalCleanupPolicy.ReverseKnownKeys`，避免 catalog 漂移只在未來 executor 才被發現。
- **A/B isolation**：比照 `Resolve_keeps_interleaved_a_and_b_payment_observations_operation_local` 的 Barrier 交錯測試，證明兩個並發 booking 決策互不污染。
- **Lifecycle**：每個產出的 plan 都必須斷言 `CeDispatchAllowed == false` 且 `ProductConsumerAllowed == false`，作為 P7.4/P7.5 切流前的常駐防線。

## 5. Findings

**Critical**
- TOCTOU：period-001 防呆讀取與任何寫入之間無鎖，RETURN_URL／BACKEND_URL 雙回呼可競爭出重複 `new_fee`（`RecurringDonationPaymentProcessor.cs:221-253`）。
- 部分寫入風險：`CreateFee`（D）與 booking 完成更新（F）非原子，中途例外會留下 fee 已建、booking 未轉態的不一致狀態，且既有防呆會讓重試短路（`RecurringDonationPaymentProcessor.cs:301-345`）。
- `AssignOwner` 的 owner 權威來源（`GetOwnerId(aContact)`）尚未被納入任何 catalog 定義，且必須維持「非 caller 可控」——若未來新增對應 operation 時誤加 `owner*` 輸入名稱會直接違反既有約束（`RecurringDonationPaymentProcessor.cs:443-453`；`P72ContinuationLocalOnlyCatalog.cs:361-373`）。

**Warning**
- LINE 成功通知在 CRM 寫入呼叫返回後即無條件發送，未驗證寫入是否真正 commit（ToolUtility 呼叫未見 committed／ambiguous 區分），存在「CRM 寫入曖昧但已通知使用者成功」的風險（`RecurringDonationPaymentProcessor.cs:344-353`）。
- 例外處理路徑把 `e.ToString()`（含 stack trace）直接傳給硬編碼的個人 LINE ID，屬於現有生產行為，不應被視為未來治理層可沿用的診斷模式（`RecurringDonationPaymentProcessor.cs:400-408`）。
- `UpdateEntity(ref aDedicationBookingEntity)`（成功路徑）與 `UpdateEntity(aDedicationBookingEntity)`（失敗路徑，無 `ref`）呼叫方式不一致，未來邊界設計應明確釘住單一語意，不應從現有不一致推論行為（`RecurringDonationPaymentProcessor.cs:345, 384`）。

**Info**
- `ProcessStageNumber`／`TransferToDeductTotalNum` 依賴 `OrderNo` 字串切割與 `new_total_stages` 中文字串比對；未來本機決策層應接收邊界預先算好的布林／enum（例如 `IsFinalStage`），不應把這段字串解析邏輯搬進權威判斷（`RecurringDonationPaymentProcessor.cs:307-318, 954-995`）。
- `MoneyToChinese`／`GetDedicationCategoryText` 為純格式化輔助函式，與 CRM mutation 治理無關，不需納入 P7.2 本機層。

---
SESSION_ID: d87753a8-7c92-4ea5-b0d9-860de23945f3
