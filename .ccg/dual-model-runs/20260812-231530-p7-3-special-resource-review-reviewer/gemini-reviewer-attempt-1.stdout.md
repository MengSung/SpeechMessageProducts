報告類型：CCG 程式碼審查與安全分析報告
審查範疇：P7.3 特殊資源本機實作（`memberinfo.contact.retrieve.image`、`memberinfo.contact.update.image`、`newperson.contact.update.image`、`metadata.optionset.retrieve.by.attribute`、`stats.meeting.retrieve.by.sunday`）

以下為本次審查發現的具體問題分類與說明：

---

### 1. Critical Findings (嚴重缺陷)

#### 缺陷一：未捕獲異常導致連接器租約（Lease）未標記為 Faulted，造成損壞連線重用
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
* **關鍵程式碼段**（約第 197-208 行）：
  ```csharp
  await using var lease = await pool.AcquireAsync(operation, cancellationToken).ConfigureAwait(false);
  var connectorResult = await lease.ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
  if (!connectorResult.Succeeded)
  {
      lease.MarkFaulted();
      ...
  ```
* **具體成因與危害**：
  在 `ExecuteOperationAsync` 中，`lease.ExecuteAsync` 內部會呼叫 `OnPremiseData8ConnectorClient.ExecuteAsync`，進而執行 CRM SDK 呼叫（如 `service.Retrieve`、`service.Update`、`service.Execute` 等）。
  若 CRM 伺服器斷線、逾時、或 SDK 內部拋出未捕獲的異常（例如 `SoapException`、`TimeoutException` 或 `InvalidOperationException`），該異常將直接中斷執行流並向外拋出。這將導致後續的 `if (!connectorResult.Succeeded)` 檢查被跳過，`lease.MarkFaulted()` 永遠不會被執行。
  雖然 `lease` 會透過 `await using` 進行 Dispose，但由於未被標記為 Faulted，連線池（Pool）會誤認為該連線通道依然健康，並將其放回可用佇列中重用。這違反了「將失敗的連接器結果視為不安全的工作階段狀態，必須進行故障驅逐（Fault Eviction）」的安全性要求。
* **修復建議**：
  應使用 `try...catch` 區塊包裹 `lease.ExecuteAsync` 與回應投影邏輯，在捕獲任何異常時明確呼叫 `lease.MarkFaulted()`，再重新拋出異常或返回失敗結果。

---

### 2. Warning Findings (警告事項)

#### 缺陷二：`MetadataOptionSetCache` 缺乏主動過期清理機制，可能導致記憶體佔用過高
* **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/MetadataOptionSetCache.cs`
* **關鍵程式碼段**（約第 400-418 行）：
  ```csharp
  private void RemoveExpiredEntriesLocked(DateTimeOffset now)
  {
      List<MetadataOptionSetCacheKey>? expired = null;
      ...
  }
  ```
* **具體成因與危害**：
  快取過期項目的清理（`RemoveExpiredEntriesLocked`）完全依賴於外部呼叫 `TryGet` 或 `Store` 時觸發的被動清理。若系統在一段時間內沒有新的快取讀寫操作，已過期的快取項目（包含其內部的 `OptionSetOptionRecord` 唯讀集合與字串）將持續滯留在記憶體中，直到下一次操作或整個 Runtime 被 Dispose。在長時間運行的 Gateway 服務中，這可能導致不必要的記憶體佔用。
* **修復建議**：
  雖然本機快取限制了最大項目數（`maximumEntryCount`），但建議在 Runtime 層級或快取初始化時，評估是否引入輕量級的定時清理機制，或確保 Runtime 生命週期能被精確管理以釋放資源。

---

### 3. Info Findings (一般資訊)

#### 說明一：`GatewayOperationParameterNormalizer` 中 `ZeroMemory` 的安全防禦有效性
* **檔案路徑**：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationParameterNormalizer.cs`
* **關鍵程式碼段**（約第 176-182 行）：
  ```csharp
  finally
  {
      if (decodedBytes is not null)
      {
          CryptographicOperations.ZeroMemory(decodedBytes);
      }
  }
  ```
* **說明**：
  此處在 `finally` 區塊中對 Base64 解碼後的暫存 byte 陣列 `decodedBytes` 進行了 `ZeroMemory` 清除，以防止敏感圖片數據殘留在記憶體中。然而，由於 `ContactImageResponseData` 的建構子內部使用了 `imageBytes.ToArray()` 進行防禦性複製（Defensive Copy），因此最終傳遞給 Executor 的複本仍會保留在託管堆積（Managed Heap）中，直至被垃圾回收（GC）。此處的 `ZeroMemory` 僅清除了解碼時產生的第一代暫存緩衝區，防禦效果屬局部性，符合現行架構設計。
