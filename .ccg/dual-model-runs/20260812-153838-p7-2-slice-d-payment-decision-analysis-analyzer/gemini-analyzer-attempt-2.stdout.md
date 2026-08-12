# P7.2 Slice D：付款結果本機決策契約分析報告

本報告針對 P7.2 Slice D 「付款結果本機決策契約」進行程式碼與契約分析。本分析遵循**純同步、零 I/O、零 Session/cache/connector/CRM client** 的本機實現原則，不建議或執行任何 CE、feature flag、產品流量、CE 8.2、Official Worker 或 ToolUtility 移除。

---

## 1. UX Analysis (使用者體驗影響評估)

付款結果的判定與後續處理直接影響使用者的信任感與操作流暢度：
* **避免重複入帳與扣款焦慮**：Legacy 系統中若因網路逾時或重複發送 `RETURN_URL` 與 `BACKEND_URL`，可能導致使用者看到多次扣款或重複入帳通知。本機契約透過 `AlreadyProcessed` 狀態與 `ProhibitsReplay = true` 限制，能有效在前端與本機端阻斷重複的 dispatch，提升使用者對交易狀態的信賴。
* **即時且明確的狀態回饋**：將決策邏輯抽離為純同步契約後，系統能以極低延遲判定交易狀態。對於成功交易，可立即引導至成功頁面；對於失敗交易，則明確提示需要進行對帳（`RequireReconciliation`），避免使用者在未知狀態下重複嘗試付款。
* **異常狀態的 fail-closed 保護**：當金流回傳狀態不明（`Pending` 或 `Unknown`）時，系統採取 `NoGo` 策略。雖然這會限制使用者的即時操作，但能防止在狀態未明時寫入錯誤資料，保護使用者的資金與帳務安全。

---

## 2. Design Evaluation (設計系統與模式評估)

* **一致性設計模式**：Slice D 擬定的 `P72DonationPaymentLocalDecision` 採用了與 `P72AttendanceWeeklyReportDecision` 相同的「觀察（Observation）- 決策（Decision）- 處置（Disposition）」模式。這種純同步、無副作用的狀態機設計，易於進行單元測試與形式驗證。
* **嚴格的去識別化與安全邊界**：
  * 決策輸入與輸出完全不攜帶 CRM ID、order ID、Owner、profile、endpoint、credential、token 等敏感資訊。
  * 符合 `P72ContinuationLocalOnlyCatalog` 中 `ContainsForbiddenInputAuthority` 的安全限制，防止 caller 透過偽造路由權限（routing authority）進行越權操作。
* **Token 與主題使用**：本機契約僅使用去識別化的布林值（如 `HasMatchingProcessedOrder`、`IsAwaitingPayment`）與正規化列舉（`P72DonationPaymentOutcome`），不依賴任何 Dynamics 實體狀態碼（如 `100000000`），實現了業務邏輯與底層資料庫設計的解耦。

---

## 3. Technical Considerations (技術考量與契約分析)

### 3.1 Semantics 保守性與 Legacy 已知行為比對 (Critical / Warning / Info)

#### 【Info】語意高度保守且符合 Legacy 行為
* **成功寫入條件一致**：Legacy `DonationFeePaymentProcessor` 僅在 `hasProcessedOrder != true` 且 `currentPayStatus == 100000000`（信用卡新建立）時執行寫入。本機契約將此抽象化為 `HasMatchingProcessedOrder == false` 且 `IsAwaitingPayment == true` 時才允許 `PrepareFutureGovernedDispatch`，兩者邏輯完全等價。
* **重複請求阻斷**：Legacy 系統中若訂單已記錄或狀態非待付款，則視為已處理。本機契約對應返回 `AlreadyProcessed`，且 `ProhibitsReplay = true`，符合 legacy 的冪等性預期。
* **失敗不重播**：Legacy 系統中失敗回呼（`isPaymentSuccess == false`）僅寫入日誌，不執行 CRM 寫入。本機契約返回 `RequireReconciliation`，禁止自動重播，符合 legacy 安全邊界。

#### 【Warning】依賴外部正規化器（Normalizer）的正確性
* 本機決策契約本身是純同步且去識別化的，這意味著**所有原始資料的解析與狀態讀取都必須在進入決策前完成**。
* 外部系統必須確保：
  1. 呼叫 `DonationPaymentResultHelper.IsPaymentSuccess` 的邏輯，將原始金流狀態碼（如 `S`、`0000`、`FAIL` 等）與模糊字串（如 "交易成功"、"失敗"）正確正規化為 `P72DonationPaymentOutcome`。
  2. 從 CRM 正確讀出 `new_payment_records` 與 `new_pay_status`，並準確判定 `HasMatchingProcessedOrder` 與 `IsAwaitingPayment`。
  *若外部正規化器在比對字串或讀取狀態時出現偏差，本機決策將會直接做出錯誤的處置。*

#### 【Critical】無
* 本機契約在設計上完全關閉了非預期的寫入通道（`CeExecutorEnabled = false`、`ConsumerEnabled = false`），在語意上極為保守，無重大安全漏洞。

---

### 3.2 必測邊界設計

為確保本機契約的穩固性，單元測試（如 `P72DonationPaymentLocalDecisionTests`）必須覆蓋以下邊界：

| 測試維度 | 邊界條件 | 預期行為 | 測試方法說明 |
| :--- | :--- | :--- | :--- |
| **A/B Isolation** | 並行處理 A 訂單（全新成功）與 B 訂單（已處理成功） | A 與 B 的決策結果互不干擾，無狀態洩漏 | 使用 `Barrier` 同步多個執行緒並行呼叫 `Resolve`，驗證返回的 `Disposition` 各自獨立。 |
| **Input Mutation** | 在 Plan Builder 建立 Plan 後，修改原始輸入字典 | Plan 內部的 Inputs 保持不變 | 在 `Build` 成功後，修改傳入的 `request.Inputs` 字典，驗證 `result.Plan.Inputs` 仍維持原值（防竄改）。 |
| **Timeout/Ambiguous** | 輸入 `IsComplete = false` 或 `Outcome = Pending/Unknown` | 決策判定為 `NoGo`，失敗分類為 `Unavailable` | 驗證在傳輸逾時或金流狀態未明時，系統必須 fail-closed，禁止任何 dispatch。 |
| **Partial Completion** | 輸入 `HasMatchingProcessedOrder = true` 或 `IsAwaitingPayment = false` | 決策判定為 `AlreadyProcessed`，禁止重播 | 驗證此時 `CanPrepareFutureDispatch` 為 `false`，且 Plan Builder 拒絕為其建立任何 partial plan。 |

---

## 4. Options (替代方案評估)

### 方案 A：純本機同步決策 + 延遲 Dispatch（當前擬定契約）
* **優點**：零 I/O、無副作用、極易測試；安全邊界清晰，完全符合 fail-closed 原則。
* **缺點**：無法在決策當下完成 CRM 寫入，必須依賴後續受治理的 executor 執行 dispatch。
* **評估**：最安全、最保守的方案，適合目前 CE 軌道關閉、僅允許本機實現的階段。

### 方案 B：即時本機決策 + 立即本機寫入（繞過 CE 治理）
* **優點**：交易處理延遲低，不需等待後續 dispatch 流程。
* **缺點**：違反「零 I/O、零 CRM client」原則，且在併發高時極易產生 TOCTOU（檢查時與使用時）時間差漏洞，導致重複寫入。
* **評估**：高風險，不符合目前 Slice D 的架構約束。

### 方案 C：完全依賴 CE 雲端決策（不進行本機預檢）
* **優點**：決策與執行完全在受治理的雲端環境完成。
* **缺點**：由於 P7.2 Slice C 的 fresh CE cycle 已關閉，此方案目前無法執行。
* **評估**：不可行。

---

## 5. Recommendation (推薦方案與未來補強建議)

### 推薦採用：方案 A (純本機同步決策 + 延遲 Dispatch)
此方案在不引入 Dynamics 連線與 I/O 的前提下，完整鎖定了付款結果的判定邏輯，且透過專用 Plan Builder 限制了只有 `fresh-success` 才能轉為 `payments.fee.update.after.payment` 本機計畫，最大程度地降低了系統風險。

### 未來 CE Executor 仍須補上的機制 (避免誤宣稱為 CE Evidence)
由於本機決策僅是「模擬與預測」，**絕不能將本機產生的 Plan 或決策結果直接宣稱為已執行的 CE 證據**。未來若要啟用 CE Executor，必須補上以下受治理的機制：

1. **Evidence Ledger (證據帳本寫入)**：
   * 在執行任何 CRM 寫入前，必須先將決策輸入、金流原始 payload、以及預期寫入的欄位快照，寫入去中心化或受治理的唯讀 ledger（如 Slice C 中使用的 `P72FreshSliceCFixtureFileLedger`）。
   * 寫入的 ledger 必須包含唯一的 nonce 或 correlation ID，作為未來審計與對帳的唯一憑證。
2. **Preflight Read-Back (執行前雙重讀回驗證)**：
   * 在 executor 真正執行 `payments.fee.update.after.payment` 寫入前，必須透過 Dynamics Connector 重新讀取 CRM 中該筆 Fee 的最新狀態（`new_payment_records` 與 `new_pay_status`）。
   * 必須再次驗證 `hasProcessedOrder != true && currentPayStatus == 100000000`。這是因為在本機決策與實際執行之間可能存在時間差（Time-of-Check to Time-of-Use, TOCTOU），必須透過強一致性的讀回（Read-Back）來避免併發衝突。
3. **Deterministic Cleanup (確定性清理)**：
   * 如果 dispatch 執行失敗（例如 CRM 連線中斷、寫入逾時），必須有明確的 cleanup 策略（如 `ReverseKnownKeys` 或 `DiscardOperationLocalState`），將已變更的暫存狀態或鎖定資源釋放，避免留下髒資料。
4. **Reconciliation Loop (對帳閉環)**：
   * 對於所有 `RequireReconciliation`（失敗）或 `NoGo`（未知）的交易，必須有獨立的背景對帳任務（Reconciliation Worker），定期讀取金流對帳單與 CRM 狀態進行比對，並在確認無誤後手動或透過受控通道補單，而非由 Webhook 失敗回呼自動重播。
