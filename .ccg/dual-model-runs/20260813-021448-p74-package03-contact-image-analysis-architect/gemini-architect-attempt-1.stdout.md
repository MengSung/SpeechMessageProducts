# P7.4 Package03 聯絡人圖片讀取候選方案安全性分析報告

本報告針對將 `MemberInfoController.GetContactImage` 切換至 P7.3 `IPackage03SpecialResourceClient.RetrieveContactImageAsync` 的唯讀移轉（Cutover）安全性進行評估。

---

## 審查結論與決策摘要

在滿足以下條件的前提下，此移轉方案是**安全且可行**的：
1. **完全繞過快取**：在啟用 `Package03SpecialResourcesEnabled` 時，必須完全繞過既有的 `IMemoryCache` 讀取與寫入，以避免跨 Profile/Generation 的快取污染與資料洩漏。
2. **正確處理取消訊號**：必須顯式捕獲 `OperationCanceledException` 並重新拋出 (`throw;`)，防止其被寬鬆的 `catch` 吞掉而錯誤回傳預設圖片。
3. **動態 MIME 類型**：應根據 `ContactImageResult.MediaKind` 動態決定回傳的 Content-Type（`image/png` 或 `image/jpeg`）。

---

## 詳細發現與風險評估

### Critical (嚴重問題)

#### 1. 跨 Profile/Generation 快取隔離風險與交互作用
* **檔案位置**: `ChurchReport/Controllers/MemberInfoController.cs` (`GetContactImage` 方法)
* **原理說明**: 
  現有的 `GetContactImage` 在最前端會嘗試從 `IMemoryCache` 讀取快取（快取鍵如 `member-info-contact-image-thumb:{contactGuid:N}:{size}`）。
  提案要求「在新路徑下**不要快取** typed image bytes，因為隔離分割尚未被證實」。然而，若僅在寫入端不寫入，但**讀取端未繞過**，系統仍會讀取到先前 Legacy 模式下寫入的舊快取。
  此外，若多個 Profile/Generation 共用同一個 `IMemoryCache` 實例，且快取鍵未包含 Profile 識別碼，將存在嚴重的跨租戶資料洩漏風險。
* **決策建議**: 
  當 `Package03SpecialResourcesEnabled` 為 `true` 時，必須**完全繞過** `IMemoryCache` 的讀取與寫入邏輯。

#### 2. 連線生命週期與資源洩漏風險
* **檔案位置**: `ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**: 
  Legacy 路徑會在 `try` 區塊中呼叫 `service = GetConnection()` 取得 CRM 連線，並在 `finally` 呼叫 `ReleaseConnection(service)`。
  在新路徑下，圖片讀取完全由 `IPackage03SpecialResourceClient` 負責，**不應**初始化 `IOrganizationService` 連線。
* **決策建議**: 
  必須確保新路徑的執行分支在 `GetConnection()` 被呼叫之前即進行攔截與返回，避免無謂的連線池佔用與潛在的連線洩漏。

---

### Warning (警告)

#### 1. 取消權杖（Cancellation Token）被異常吞噬
* **檔案位置**: `ChurchReport/Controllers/MemberInfoController.cs` (約第 659-662 行)
* **原理說明**: 
  現有程式碼的 `catch` 區塊非常寬鬆：
  ```csharp
  catch
  {
      return GetDefaultImage();
  }
  ```
  當傳入 `RequestAborted` 且客戶端取消請求時，`RetrieveContactImageAsync` 會拋出 `OperationCanceledException`。若直接被此處的 `catch` 捕獲，會錯誤地回傳 `GetDefaultImage()`（200 OK 與 SVG 內容），而非向 ASP.NET Core 容器重新拋出（Rethrow），這會干擾伺服器的連線釋放機制。
* **決策建議**: 
  必須顯式加入 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` 以確保取消訊號正確傳遞。

#### 2. 媒體類型（MIME Type）不匹配
* **檔案位置**: `ChurchReport/Controllers/MemberInfoController.cs` (第 621, 644 行)
* **原理說明**: 
  Legacy 程式碼一律寫死回傳 `image/jpeg`。但 P7.3 的 `ContactImageResult` 可能回傳 `ContactImageMediaKind.Png`。若將 PNG 二進位流以 `image/jpeg` MIME 類型回傳，可能導致部分嚴格的瀏覽器或 CDN 快取解析失敗。
* **決策建議**: 
  應讀取 `ContactImageResult.MediaKind`，並動態對應回傳 `image/png` 或 `image/jpeg`。

#### 3. ProfileAlias 與 WorkloadSubjectId 的解析安全性
* **檔案位置**: `ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**: 
  呼叫 `RetrieveContactImageAsync` 必須提供 `ProfileAlias` 與 `WorkloadSubjectId`。這些參數必須由伺服器端安全解析（例如從 `InMemoryContext` 或設定檔中讀取），絕對不可信任前端傳入的參數，以防越權存取。

---

### Info (提示)

#### 1. 批次端點未覆蓋導致的資料不一致性
* **檔案位置**: `MemberInfoController.GetContactImagesBatch` (第 670 行)
* **原理說明**: 
  依據提案，批次端點 `GetContactImagesBatch` 保持不變（繼續走 Legacy CRM 查詢）。這會導致在同一個畫面上，單張圖片讀取走新路徑，而批次網格載入走舊路徑。若兩端資料同步有時間差，使用者可能會在不同元件中看到不同的頭像。此點應記錄於移轉說明中。

#### 2. 配置預設值與防禦性設計
* **原理說明**: 
  `Package03SpecialResourcesEnabled` 必須預設為 `false`。當為 `false` 時，程式碼路徑必須與原本完全一致，確保零迴歸風險。
