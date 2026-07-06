## 審查報告：reduce-line-wait-500ms

**範圍**：`DonationPaymentProcessor.FeeManagement.cs`、`DonationPaymentProcessor.PaymentProcessing.cs`、對應測試檔。已用 `git diff` 取得實際變更內容（任務提供的 diff 內容是壞的 `System.Object[]` 佔位字串，我改為直接讀取 repo 現況審查），並獨立重跑 `dotnet test --filter FullyQualifiedName~DonationPaymentProcessorKeyInNotificationTests`，結果 8/8 通過，與回報一致。

### Critical 🔴
無。核心邏輯（`Task.WhenAny` + 500ms `Task.Delay`、逾時後用 `ContinueWith(... ExecuteSynchronously)` 觀察背景例外避免 unobserved exception、逾時分支與正常分支都各自正確回傳訊息）在 ATM 與手動輸入奉獻兩條路徑上實作一致且正確。ATM 多組候選 LINE id 的 fallback 只在**同步例外**時才換下一組，逾時時直接回傳失敗不會疊加等待時間，所以不會有「候選數 × 500ms」的疊加風險。

### Warning 🟡
- **`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs:189`** 新增的 `SendDedicationNotificationAsync_returns_timeout_result_when_line_api_is_slow` 只保護了逾時情境，缺少「500ms 內成功發送」與「LINE API 直接丟例外」兩個分支的回歸測試。ATM 路徑對這三種情境都各有測試，手動輸入奉獻路徑目前不對稱，未來若不小心改動 `catch` 區塊或成功分支的回傳字串，不會有測試立刻抓到。
- **`ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:292`** 與 `PaymentProcessing.cs:38` 的 500ms 顯示逾時常數在兩個 partial class 檔案各自宣告一份（`DedicationLineNotificationDisplayTimeout` / `AtmLineNotificationDisplayTimeout`），沒有共用單一來源。之後若只改其中一處，會讓 ATM 與手動輸入奉獻的等待上限出現不易察覺的不一致。

### Info 🟢
- **`DonationPaymentProcessorKeyInNotificationTests.cs:386`** 檔案結尾在 `ThrowingDonationPaymentCreateGatewayAdapter` 類別後多了一個空白行才收斂類別大括號，屬於無意義的格式差異，不影響行為。

### Summary
變更本身（把顯示逾時從 2s/8s 降到 500ms、逾時後用 fire-and-forget 讓真正的 LINE 發送在背景繼續、並用 `ContinueWith` 觀察背景例外）行為正確、與既有 ATM 逾時模式一致，本地測試與獨立重跑均為 8/8 通過。建議在合併前補上手動輸入奉獻「成功」與「例外」兩個分支的測試，並考慮把兩處 500ms 常數收斂成同一來源；這兩點都不阻塞合併，屬於後續強化項目。

**Recovery 備註**：本次審查是直接以本對話所處的 reviewer 角色完成，任務描述要求的「透過 self-healing CCG entrypoint 執行、保留 `.ccg/dual-model-runs` 下的 stdout/stderr/health report」屬於外部 CCG 編排腳本的職責，此環境沒有提供可呼叫的 CCG/Gemini/Codex 編排工具，因此無法產生對應的健康報告與備援分類（quota-block classification 等）；這屬於工具可用性限制，而非審查內容本身的阻塞。

---
SESSION_ID: b7f52d67-0566-4099-bba4-460dbcb37e05
