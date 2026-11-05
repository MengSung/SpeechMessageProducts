## 審查結果：P1 局部測試修補（合成測試 / 產品邊界摘錄）

本次審查範圍僅限提供的合成測試差異與 `HttpResponseMessageExtensions.cs`／`ChannelHttpTransport.cs` 摘錄，未讀取其餘 repo、未執行工具、未修改任何檔案。

### 總體結論
四案 ProviderRejected／反射修補，**均屬測試契約修正，非不當降低驗收標準**：

- `PushUtilityTests` / `LineRichMenuWorkflowTests` / `LineNotificationWorkflowTests` 三案原先斷言直接期待回顯 provider 原始 message（如 `"invalid user id"`），這本身就是違反「遠端 body 永久隔離」規則的舊契約。修補後改為斷言安全訊息 `"LINE API request failed. HTTP 400."` 精確相等，並新增 `exception.Which.ToString().Should().NotContain(<合成私密標記>)` 作為反洩漏防線——這是**新增**而非刪減驗證面，且驗證內容與 `HttpResponseMessageExtensions.cs` 的 `safeError` 行為（固定訊息＋保留 status code、不解析/保存遠端 body）完全一致。
- `LineUtilityClassWorkflowTests` 的反射改由 `_transport` 欄位取出 transport 後再讀 `client` 欄位，對應 `ChannelHttpTransport.cs` 實際欄位名稱（`client`），且新增 `oldClient.GetBotInfoAsync()` 需拋 `ObjectDisposedException`（對應 `ChannelHttpTransport.SendAsync` 在 `authorization` 被清空後的防護檢查)與 `clientField.GetValue(utility).Should().NotBeSameAs(oldClient)`，是在原有「自有 HttpClient 已 Disposed」斷言之上**加嚴**，同時覆蓋 SDK 層與替換實例兩個新維度，無刪減。

沒有發現任何「把原本會抓到的回歸改成不會抓到」的案例。

---

### Warning 🟡

1. **`ToolUtility.Tests/TestHelpers/GoldenQueryAssertions.cs`** — 內嵌資源名稱硬編為 `"ToolUtility.Tests.Golden.ToolUtility." + fileName`，前提是專案 `RootNamespace` 恰為 `ToolUtility.Tests`。若未來 csproj 調整 RootNamespace 或資料夾結構，`GetManifestResourceStream` 會回傳 `null`，`Assert.NotNull(stream)` 立即讓所有依賴黃金檔的測試（`GoldenQueryAssertionsTests` 全部案例＋`FeeCharacterizationTests` 兩個黃金比對案例）失敗。目前無法由摘錄確認 RootNamespace 設定是否與此假設一致。
2. 背景說明已自陳「最新完整回歸仍待收斂」，而 `GoldenQueryAssertionsTests`／`FeeCharacterizationTests` 屬本輪新增的 `??` 未追蹤檔案，不確定是否已被包含在「初次全方案 1399/1403、4 失敗」或後續三案通過的驗證範圍內。建議在最終 P1 驗收前，明確把這兩個新測試檔納入一次完整回歸，而不是只靠本次單檔審查判斷綠燈。

### Info 🟢

1. **`GoldenQueryAssertions.CreateSettings()`**：`IgnoreWhitespace = true` 只忽略元素間的插入性空白，對元素文字節點內容不受影響；目前所有黃金檔（`comparison-contract.xml`、`fee-dedication*.xml`）皆為純屬性、無文字節點，因此現況安全。但若未來黃金檔新增含有意義空白的文字節點比較，需重新確認 `IgnoreWhitespace` 是否仍符合「保留欄位值精確比較」的要求。
2. **`PushUtilityTests`** 新增的斷言未檢查 `ErrorCode`（RichMenu 版本有檢查 `ErrorCode == "line-richmenu-provider-rejected"`，Push 版本沒有對應 ErrorCode 斷言）。三個 ProviderRejected 案例的斷言覆蓋深度不完全一致，非缺陷，但後續若要收斂測試風格，可考慮統一加上 ErrorCode 檢查以提高跨檔一致性。
3. `LineUtilityClassWorkflowTests` 新增的 `oldClient.GetBotInfoAsync()` 斷言依賴 `ChannelHttpTransport.SendAsync` 對 `authorization == null` 的提前檢查先於任何真實網路呼叫觸發 `ObjectDisposedException`，符合摘錄程式碼行為且不會發出真實 LINE 請求；此設計正確地滿足「這個測試不發送真實 LINE 請求」的要求。
4. `FeeCharacterizationTests.RetrieveDedicationFeeByDateRange_...` 的文化還原用 `try/finally` 還原 `CultureInfo.CurrentCulture`（非 `DefaultThreadCurrentCulture`），不會污染並行案例，符合註解中強調的要求，實作正確。

### Critical 🔴
無。

---

**備註**：此審查僅針對提供的合成測試片段與兩個產品檔案摘錄進行邏輯比對，不等同完整 SDK 審查或 P1 正式驗收，且不保證未列出的其餘產品程式碼與這些測試假設一致（例如 `LineMessagingClient._transport` 欄位名稱、`LineRichMenuException`/`LineNotificationException` 的 `Result`/`Message` 轉發邏輯），需以實際完整回歸結果為準。

---
SESSION_ID: 626088fa-af83-4826-801e-b8c2b3bdaa1d
