# 架構審查報告：Dynamics CE 地端服務保護與 SQL 協調器評估

本報告針對 Dynamics 365 Customer Engagement (CE) 8.2/9.1 地端（on-premises）環境與 Gateway 的服務保護（service-protection）及 SQL 協調器（SQL coordinator）進行唯讀架構審查。

---

## 1. 總體評估摘要 (Summary)

經過對儲存庫程式碼（包括 `Program.cs`、`appsettings.json`、`SqlRuntimeHostSlotCoordinator.cs` 及 `InMemoryRuntimeHostSlotCoordinator.cs`）與微軟官方文件的交叉比對，我們得出以下結論：
* **Claude 的部分論點在理論上是正確的**（例如：Dataverse 雲端託管服務的 6,000/1,200/52 限制不直接適用於地端環境；應將容量預算更名為中性術語）。
* **然而，Claude 建議「禁用耐久 SQL 協調器並改用記憶體協調器」以及「斷言地端不會回傳 429」是錯誤且高風險的**。這違反了已接受的系統架構設計（ADR），並會破壞生產環境多副本（multi-replica）部署與滾動更新時的並發保護機制。
* **本審查強烈建議：目前不應進行任何代碼或配置變更，必須保留 `RequireDurableHostCoordinator=true` 與 `SqlRuntimeHostSlotCoordinator` 的啟用狀態。**

---

## 2. 逐項問題解答與證據引用

### Q1: 是否合理得出結論：Dataverse 託管服務的 6,000/1,200/52 預設限制不應被視為權威的 CE 8.2/9.1 地端配額？
* **判定**：**正確 (Correct)**
* **信心度**：極高 (High Confidence)
* **理由與證據**：
  * 微軟官方文件《Service protection API limits (Microsoft Dataverse)》明確指出，這些限制是為了保護 Dataverse 雲端託管平台上的共享資源，且 Web 伺服器數量是由 Dataverse 託管服務動態決定的。
  * 地端部署（如 CE 8.2 `jesus` 與 CE 9.1 `sunnyvalechback`）的硬體資源、IIS 站台設定、SQL Server 規格完全由企業自行管理與配置，並不受微軟雲端基礎設施的硬性配額限制。
  * 搜尋微軟地端文件（如 CE on-premises Web API 說明）並無任何提及 6,000/1,200/52 限制的內容。
* **注意事項 (Caveats)**：
  * 雖然地端沒有 Dataverse 雲端預設的硬性 API 限制，但地端環境仍有其物理極限（如 IIS 連線集區、ASP.NET 執行緒、SQL Server 鎖定與 CPU 瓶頸）。因此，限制並發請求以保護地端 Dynamics 伺服器免於崩潰的需求依然存在。

### Q2: 「地端環境絕對不會回傳 429」這個較強烈的說法是否合理？
* **判定**：**不正確 (Incorrect)**
* **理由與證據**：
  * 雖然 Dynamics CE 地端開箱即用的預設行為可能不會主動拋出 Dataverse 特有的 429 錯誤，但「地端環境絕對不會回傳 429」這個斷言過於武斷。
  * 在實際的企業地端部署中，Gateway 與 Dynamics 之間通常存在反向代理（如 IIS ARR、NGINX、F5 BIG-IP）或 Web 應用程式防火牆（WAF），這些基礎設施在偵測到異常高流量或並發請求時，會主動回傳 `429 Too Many Requests`。
  * 此外，Dynamics CE 的自訂外掛（Plugins）或自訂 API 整合層（Middleware）也可能實作了自訂的限流邏輯，並在超出閾值時回傳 429。
  * 當 IIS 佇列滿載或應用程式集區崩潰時，也可能回傳 503 或 429。因此，Gateway 必須具備處理 429/503 的彈性與容錯能力。

### Q3: 缺少提議的兩個 `x-ms-ratelimit` 標頭是否能證明服務保護或限流不存在？若否，什麼才是有效的真實負載/冒煙測試證據？
* **判定**：**不能證明 (Incorrect)**
* **理由與證據**：
  * 根據微軟官方文件，Dataverse 服務保護主要透過 `Retry-After` 標頭與 429 狀態碼來指示限流，官方文件並未將 `x-ms-ratelimit-burst-remaining-xrm-requests` 和 `x-ms-ratelimit-time-remaining-xrm-requests` 宣告為標準或保證存在的回應標頭。
  * 缺少這兩個標頭僅代表沒有特定的 XRM 速率限制標頭輸出，並不代表後端沒有進行任何形式的流量限制或排隊保護。
  * **有效的真實負載/冒煙測試證據**：
    * 應透過真實的壓力測試（Load Testing）與浸泡測試（Soak Testing），在高並發下觀察 Dynamics 伺服器的 CPU、記憶體、IIS 請求佇列長度、SQL Server 鎖定等待時間（Lock Wait Time）以及回應時間的退化情況。
    * 觀察是否出現 HTTP 429、503 (Service Unavailable) 或 504 (Gateway Timeout) 錯誤。

### Q4: 僅因為 Dynamics 是地端部署或預期進程數固定，就禁用耐久 SQL 協調器是否合理？
* **判定**：**完全不合理 (Incorrect)**
* **理由與證據**：
  * **生產環境要求**：已接受的架構設計（ADR）明確要求中央網關（Central Gateway）在生產環境中必須部署至少兩個副本（two replicas）以實現高可用性（HA）。如果禁用耐久協調器（Durable Coordinator），改用 `InMemoryRuntimeHostSlotCoordinator`，則這兩個獨立的網關進程將無法共享准入狀態，導致物理組織的總體併發限制（Aggregate Max In-Flight）被成倍突破，失去保護 Dynamics 的作用。
  * **部署與生命週期管理**：在重啟、滾動更新（Rolling Update）、藍綠部署（Blue-Green Deployment）或主機排空（Draining）過程中，多個網關主機實例會短暫重疊。只有基於資料庫的耐久協調器（`SqlRuntimeHostSlotCoordinator`）能透過 Fencing Token、Epoch 和 Quarantine 機制，確保在主機切換時不會發生並發超載或過期主機（stale host）繼續發送請求的問題。
  * **開發與隔離**：雖然單一開發人員的 Local Gateway 可以是單進程的，但多個開發人員的 Local Gateway 若指向同一個物理測試組織，仍需要耐久協調器來防止並發衝突。
  * **儲存庫現狀**：`Program.cs` 在非 Testing 環境下強制註冊 `SqlRuntimeHostSlotCoordinator` 並執行 `DynamicsGatewayReadinessService`。若將 `RequireDurableHostCoordinator` 設為 `false` 並改用 `InMemoryRuntimeHostSlotCoordinator`，將直接違反已接受的架構設計與安全邊界。

### Q5: 是否應將文檔中的「CRM service-protection budget」更名為中性術語（如「validated organization capacity budget」），同時保留所有准入控制與背壓機制？
* **判定**：**正確 (Correct / Recommended)**
* **理由與證據**：
  * 將其重新命名為「已驗證的組織容量預算」（validated organization capacity budget）或類似的中性術語更為精確，因為地端部署的限制並非來自 Dataverse 雲端服務保護的硬性配額，而是來自地端硬體與 IIS/SQL 的實際承載能力。
  * 儘管名稱改變，但 Gateway 的核心保護機制——包括有界准入控制（bounded admission）、背壓（backpressure）、429/503 錯誤處理以及基於真實負載測試的容量評估——必須完整保留，以防止地端 Dynamics 伺服器因過載而崩潰。

---

## 3. 詳細審查發現 (Detailed Findings)

### 【Critical】生產環境禁用 Durable Coordinator 將導致並發保護失效
* **檔案路徑**：
  * `SpeechMessage.Dynamics.Gateway/Program.cs` (第 125-140 行)
  * `SpeechMessage.Dynamics.Gateway/appsettings.json` (第 94 行)
  * `SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs` (第 27 行)
* **判定依據**：
  * `InMemoryRuntimeHostSlotCoordinator.IsDurable` 為 `false`。其檔頭註解明確指出：「IsDurable=false，代表其無法跨進程、重啟或多主機維持租約的持久性與隔離... 多 Gateway 部署時不可使用此元件，必須使用 durable store」。
  * 生產環境要求至少兩個 Gateway 副本。若將 `RequireDurableHostCoordinator` 設為 `false` 並使用記憶體協調器，兩個副本將各自擁有獨立的計數器，導致實際並發量翻倍（達到 48 而非設定的 24），這將直接威脅地端 Dynamics 伺服器的穩定性。

### 【Warning】「地端不會回傳 429」的假設存在安全隱患
* **檔案路徑**：架構設計與錯誤處理邏輯
* **判定依據**：
  * 企業地端環境通常部署有反向代理（Reverse Proxy）或 WAF。這些組件在遭遇高流量時會主動回傳 429。
  * 若 Gateway 移除對 429 的處理與重試邏輯（如 `Retry-After` 解析），一旦遭遇網路設備或自訂外掛的限流，系統將無法正確進行退避（Backoff）與重試，導致請求直接失敗。

### 【Info】文檔術語更名建議
* **檔案路徑**：架構設計文檔與配置說明
* **判定依據**：
  * 地端環境的限制本質上是「物理伺服器容量限制」，而非 Dataverse 的「服務保護配額」。更名為 `validated organization capacity budget` 能更準確地向維運團隊傳達其配置意義。

---

## 4. 最終建議 (Recommendations)

1. **立即行動**：**不進行任何代碼或配置變更**。
   * 保持 `RequireDurableHostCoordinator = true`。
   * 保持 `SpeechMessage.Dynamics.Gateway/appsettings.json` 中的 `AggregateMaxInFlight = 24` 與 `MaximumRuntimeHosts = 6`。
   * 保持 `Package01FeeReadsEnabled = false`。
2. **後續驗證工作（Phase 4/5 准入條件）**：
   * 在非生產環境（如 `sunnyvalechback` VM）中進行真實的壓力與浸泡測試，收集 Dynamics 伺服器在不同並發數下的 CPU、記憶體與 SQL 鎖定指標。
   * 根據測試數據，調整並驗證 `AggregateMaxInFlight` 的最佳數值。
   * 驗證 `SqlRuntimeHostSlotCoordinator` 在多主機時鐘偏移（$\pm 1$ 秒）下的 Fencing 與 Quarantine 隔離機制是否 100% 精確。
