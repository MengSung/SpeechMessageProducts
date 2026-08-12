# P7.2 Slice D：付款結果本機決策契約分析報告

本報告針對 P7.2 Slice D 「付款結果本機決策契約」進行程式碼與契約分析。本分析遵循「純本機實作、零 I/O、零 Session/connector/CRM 依賴」之原則，評估其與 legacy 行為的相容性、測試邊界及未來升級至受治理 CE 流程時的必要補強。

---

## 一、 UX Analysis (使用者影響評估)

1. **交易安全性與重複扣款防護 (Fail-Closed)**：
   本機決策契約採用極度保守的 `fail-closed` 策略。對於任何模糊狀態（如 `pending`、`unknown`、`incomplete` 或 `null`）一律判定為 `no-go` 且不允許重播。這能有效防止因金流平台重複發送回呼（Webhook）或網路延遲導致的重複寫入與重複扣款，最大程度保障使用者的資金與收據準確性。
2. **去識別化與隱私保護**：
   決策結果與輸入完全不攜帶 CRM ID、Order ID、Owner、Token 等敏感資訊。這確保了即使本機日誌被記錄或記憶體被傾印，使用者的個人隱私與交易明細也不會外洩，符合現代資安與 UX 隱私設計原則。
3. **流暢度與回應時間**：
   由於決策為純同步、零 I/O 的本機邏輯，其執行速度在微秒級，完全消除了因 Dynamics CRM 連線延遲或金流 API 逾時導致的前端 UI 卡頓，能提供即時且流暢的跨平台（行動端與桌面端）體驗。

---

## 二、 Design Evaluation (設計系統與模式評估)

1. **與既有模式的一致性**：
   擬定的本機契約與 `P72ContinuationLocalOnlyPlanBuilder` 及 `P72ContinuationLocalOnlyCatalog` 的設計模式高度一致。透過 `AllowedInputNames` 限制輸入欄位，並透過 `ContainsForbiddenInputAuthority` 拒絕敏感的 routing/CRM 資訊，確保了架構的整潔與安全。
2. **元件重用性**：
   將付款結果的判定（`DonationPaymentResultHelper`）與決策執行（`DonationFeePaymentProcessor`）解耦，建立純粹的決策契約，使得此決策邏輯可以在不同的進入點（如 Webhook 回呼、手動對帳工具、批次同步任務）重用，而不需要依賴複雜的 Dynamics 連線。
3. **狀態語意一致性**：
   此決策為純邏輯層，不涉及 UI Token，但其狀態定義（如 `s_successCodes` 與 `s_failureCodes`）與系統整體的交易狀態 Token 保持語意一致。

---

## 三、 Technical Considerations (前端與後端架構影響)

1. **純同步與零 I/O 限制**：
   決策器不允許進行任何資料庫查詢、CRM API 呼叫或網路請求。這使得單元測試極易撰寫，且執行速度極快，完全消除了 I/O 阻塞的風險。
2. **狀態管理與冪等性**：
   決策僅依賴兩個去識別化的布林觀察（`hasProcessedOrder` 與 `awaitingPayment`）。這簡化了狀態機，但後端在呼叫決策器前，必須負責從 CRM 讀取並計算出這兩個布林值，這部分的讀取邏輯仍需注意並行衝突（Race Condition）。
3. **效能與 Bundle Size**：
   由於不引入任何外部 SDK 或 connector，此 Abstractions 專案保持極輕量，對系統效能與 bundle size 毫無負擔。

---

## 四、 Options (替代方案與權衡)

* **方案 A：純本機去識別化決策契約（擬定方案）**
  * *優點*：極度安全、零 I/O、無隱私洩漏風險、易於測試、符合現有 P7.2 本機限制。
  * *缺點*：無法在決策層進行即時的 CRM 雙重驗證，完全依賴呼叫端傳入的布林觀察準確性。
* **方案 B：允許攜帶去識別化 Order Hash 的半本機契約**
  * *優點*：決策結果可攜帶 Order No 的單向雜湊值（Hash），便於後續 dispatch 階段進行日誌關聯與對帳。
  * *缺點*：稍微增加了資料複雜度，且若雜湊演算法不夠安全，仍有被彩虹表破解還原 Order ID 的微小風險。

---

## 五、 Recommendation (推薦方案與理由)

**推薦採用方案 A**。
在 P7.2 Slice D 階段，由於 `CeExecutorEnabled` 與 `ConsumerEnabled` 均為 `false`，且 CE 軌道已關閉，本機實作的首要任務是確保 **絕對的安全與隔離（Fail-Closed）**。方案 A 透過純布林觀察進行決策，完全杜絕了敏感資料外洩與重複 dispatch 的可能性，是最保守且符合 legacy 已知行為的選擇。

---

## 六、 關鍵分析與邊界評估 (Critical / Warning / Info)

### 1. Semantics 保守性與 Legacy 行為相容性評估

* **Critical (關鍵)**:
  * **去識別化輸入限制**：決策輸入必須嚴格限制在去識別化的布林觀察（`hasProcessedOrder` 與 `awaitingPayment`），絕不能攜帶任何 CRM ID、Order ID 或 Owner 等敏感資訊。這符合 `P72ContinuationLocalOnlyCatalog` 中 `ContainsForbiddenInputAuthority` 的安全檢查，能有效防止敏感資訊洩漏至本機決策日誌或記憶體中。
  - **決策結果純同步**：決策結果必須是純同步、零 I/O 的。任何與 CRM、LINE 或金流 API 的互動都必須被隔離在決策之外。
* **Warning (警告)**:
  * **寫入欄位一致性**：Legacy `DonationFeePaymentProcessor` 在處理成功回呼時，會寫入多個欄位（如 `new_pay_date`、`new_fee_really_paid`、`new_big_chinese_number`、`new_pay_way`、`new_pay_status`、`new_description`、`new_payment_records`）。本機決策僅決定「是否準備一次未來受治理 dispatch」，而實際的寫入邏輯（即 `payments.fee.update.after.payment`）在未來真正執行時，必須確保這些欄位的寫入行為與 legacy 完全一致，否則會導致 CRM 資料不一致。
  * **模糊比對邏輯繼承**：Legacy `DonationPaymentResultHelper` 的成功與失敗判定邏輯相當複雜，包含字串模糊比對（如 `ContainsSuccessText` 檢查 "交易成功" 等，`ContainsFailureText` 檢查 "失敗"、"取消" 等）。在將 outcome 輸入本機決策前，必須確保正規化器（normalizer）完整繼承了這些模糊比對邏輯，否則可能導致原本被 legacy 視為成功的交易被判定為 pending/unknown，進而 fail closed。
* **Info (提示)**:
  * **狀態碼對應**：`new_pay_status == 100000000`（信用卡新建立）代表 awaiting payment，而 `100000001`（信用卡已繳費）代表已付款。本機契約將其抽象化為 `awaiting payment` 布林值，有助於解耦 Dynamics 實體狀態碼與決策邏輯。

### 2. 必測邊界 (Boundary Test Cases)

* **A/B Isolation (A/B 隔離性)**:
  * 測試在多執行緒併發下，同時傳入 A 訂單（已處理）與 B 訂單（未處理）的觀察指標，驗證決策器能正確且獨立地為 A 輸出 `NoDispatch`，為 B 輸出 `PrepareDispatch`，且兩者的記憶體狀態與 inputs 絕無交叉污染（如 `P72ContinuationLocalOnlyPlanBuilderTests.Build_keeps_concurrent_a_and_b_fixture_markers_operation_local` 所驗證的隔離性）。
* **Input Mutation (輸入變更防護)**:
  * 傳入決策器的 inputs 字典在決策過程中或決策後若被外部修改，決策器內部的 snapshot 必須不受影響（防禦性拷貝）。
* **Timeout/Ambiguous (逾時與模糊狀態)**:
  * 當金流回傳狀態為 `pending`、`unknown`、`null` 或逾時未回應時，決策器必須判定為 `fail closed`，不建立任何 plan，且不允許重播。
* **Partial Completion/No Replay (部分完成與禁止重播)**:
  * 若訂單已部分寫入（例如 `hasProcessedOrder == true` 但 `currentPayStatus == 100000000`，或反之），決策器必須保守地判定為已處理，不建立 `payments.fee.update.after.payment` plan，以防止重複 dispatch 造成重複扣款或資料混亂。

### 3. 未來 CE Executor 仍須補上之機制 (避免誤宣稱為 CE Evidence)

由於此 Slice D 契約為 **local-only**（`CeExecutorEnabled=false`、`ConsumerEnabled=false`），它不能產生真正的 CE evidence。未來若要將其升級為受治理的 CE 流程，必須補上以下機制：

* **Evidence Ledger (憑證帳本)**:
  * 必須在安全儲存區（如 Data8 或專用 ledger 服務）寫入不可篡改的交易憑證，記錄金流原始 response hash、時間戳記與決策路徑，作為審計追蹤（audit trail）。
* **Read-Back Verification (回讀驗證)**:
  * 在執行 dispatch 前，必須透過 connector 重新讀取 CRM 中的 `new_payment_records` 與 `new_pay_status`，進行雙重檢查（double-check），確保在決策與執行之間的極短時間差內沒有其他節點寫入相同訂單。
* **Exact Cleanup (精確清理)**:
  * 若 dispatch 失敗或超時，必須有明確的補償機制（compensation transaction）或清理機制，將暫存的狀態或鎖定（lease）釋放，並記錄失敗憑證，避免資源永久鎖定。
* **Idempotency Key (冪等鍵治理)**:
  * 必須引入全域唯一的冪等鍵（如 `OrderNo` 的雜湊值），由 CE 平台統一治理，而非僅依賴本機的布林觀察。
