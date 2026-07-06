我已完整讀過差異涉及的兩個檔案（`DonationPaymentProcessor.PaymentProcessing.cs` 逾時邏輯、測試檔案、以及既有的 `SendDedicationNotificationAsync` 前例）並確認了物件生命週期（`DonationPaymentManager`/`PushUtility`/`LineMessagingClient` 都是手動 `new` 出來、非 DI scoped 服務，且該 Controller 未實作 `IDisposable`）。以下是審查結果。

## Review: speed-up-atm-donation-submit

### Critical 🔴

- **DonationPaymentProcessor.PaymentProcessing.cs:352-395**（`TrySendAtmPaymentInstructionsAsync` 逾時分支）
  - 問題：逾時分支一旦命中就直接 `return`，會整個結束方法、跳出 `for` 迴圈，导致同一次呼叫中原本用來嘗試「下一個候選 LINE ID」（例如 `new_lineid_backup`）的 fallback 機制被跳過。修改前，只有在 `SendAtmPaymentInstructionsAsync` 真的丟出例外時才會 fallback 到下一個候選；現在只要第一個候選在 2 秒內「沒有回應」（不管最終是否會成功），就直接判定整體失敗，永遠不會嘗試 backup id。
  - 為什麼重要：`ResolveAtmNotificationLineIds` 與既有 fallback 測試（`TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails`）的設計初衷明確寫著「若第一個 LINE ID 失效，必須嘗試下一個候選，否則使用者收不到通知」。這次改動讓「單純變慢」的 primary API 跟「provider 明確拒收」被同等處理成「立刻放棄整個流程」，而非「換下一個候選」，等於在慢速網路情境下默默降低了原本刻意設計的通知送達可靠度。
  - PR 描述宣稱「fallback LINE ID behavior remain covered by tests」並不準確：現有 fallback 測試用的是同步例外（`LineIdToReject`），並未涵蓋「primary 逾時 + backup 候選可用」這個新引入的互動情境，因此這個行為變化完全沒有測試保護，也無法確認是否為刻意取捨。
  - 建議：至少要明確決定產品行為——逾時時是否也該嘗試下一個候選 ID（例如把 timeout 也視同一次失敗，繼續跑迴圈，只是**整體**時間仍要有上限，而不是任一次候選逾時就整個放棄）；並補上對應測試斷言目前的選擇。

### Warning 🟡

- **DonationPaymentProcessor.PaymentProcessing.cs:372-375**（訊息語意）
  - 逾時時顯示「LINE 發送結果：發送失敗」＋「LINE API 逾時未回應」，但背景的 `sendTask` 仍在執行、之後仍有機會真正送達成功（見 361-370 行的 `ContinueWith` 只做 log，沒有任何機制回頭更正這個已經顯示給使用者的「失敗」標籤）。把「尚未確認送達」跟「provider 已明確拒收」用同一種紅字「發送失敗」呈現，對使用者具有誤導性，可能造成不必要的人工重送或困惑。
  - 建議：逾時情境使用中性措辭（例如「發送逾時，稍後可能仍會送達」），並在樣式上與「確定失敗」區分，避免使用者誤判。

- **測試覆蓋（DonationPaymentProcessorKeyInNotificationTests.cs）**
  - 新增的逾時測試（`TrySendAtmPaymentInstructionsAsync_returns_timeout_result_when_line_api_is_slow`）只涵蓋單一 lineId、無其他候選 ID 的情境，沒有涵蓋上述 Critical 問題描述的「primary 逾時 + backup 候選存在」互動。建議至少新增一個測試明確斷言：primary 逾時時 `AttemptedLineIds` 是否應該只有 1 筆（目前答案是「是」，但這應該是被測試保護的既定行為，而非意外副作用）。

### Info 🟢

- **背景任務觀察與生命週期**：確認沒有 Critical 等級的「unobserved task exception」或「scoped 服務被提早 Dispose」風險。
  - `SendAtmPaymentInstructionsAsync` 為 `async Task`，例外不會同步拋出，一定會封裝進回傳的 `Task` 內，因此與 `Task.WhenAny` 搭配是安全的。
  - 361-370 行對逾時後仍在跑的 `sendTask` 附上了 `ContinueWith` 來 log `IsFaulted` 例外，避免了 unobserved task exception（優於同檔案既有的 `SendDedicationNotificationAsync` 前例，那裡的逾時分支沒有對背景 task 附加任何觀察者——可視為本次改動的加分項，但不在本次審查範圍內要求回頭補）。
  - `DonationPaymentManager`（承載 `m_LineMessagingClient` / `m_PushUtility` / `DonationPaymentProcessor` 的 Controller）並未實作 `IDisposable`，這些物件也都是建構式內手動 `new`、未經 DI 容器管理，因此不存在「HTTP 請求結束→DI scope 被 dispose→背景任務打到已釋放物件」的風險。
  - closure 捕獲的 `for` 迴圈變數 `index`：因為逾時分支會立即 `return` 結束整個方法（迴圈不會再往下跑），所以委派實際執行時 `index` 值仍然正確，沒有常見的「for 迴圈變數捕獲」陷阱。

- **`Task.Delay(AtmLineNotificationDisplayTimeout)` 未搭配 `CancellationToken` 提前取消**：`sendTask` 先完成時，`timeoutTask` 的計時器仍會跑滿 2 秒才釋放，屬於極輕量的資源占用，不影響正確性，僅供參考。

### Summary
Bounded-wait 的整體方向（保留 ATM 付款資訊同步顯示、只對 LINE 通知的「顯示等待」設上限）沒有問題，也正確地用 `ContinueWith` 避免了 unobserved task exception，物件生命週期上也沒有 scoped-service 被提早釋放的風險。但**逾時分支會讓多候選 LINE ID 的 fallback 機制失效**（Critical），這與既有「主要 ID 失敗要嘗試備援 ID」的可靠度設計互相衝突，且完全沒有測試覆蓋這個新互動情境，PR 描述中「fallback 行為仍受測試保護」的說法並不準確。建議：Request changes——先決定逾時是否也該觸發 fallback 到下一個候選，並補上對應測試，再合併。

---
SESSION_ID: c15bd4f7-b651-444b-802c-38359ff83c8c
