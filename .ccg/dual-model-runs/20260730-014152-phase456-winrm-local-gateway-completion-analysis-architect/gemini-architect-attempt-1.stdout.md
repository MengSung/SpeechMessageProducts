```diff
--- /dev/null
+++ b/docs/dynamics-phase4-6-analysis-report.md
@@ -0,0 +1,320 @@
+# Dynamics Phase 4～6、WinRM 與 Local Gateway 完成度分析報告
+
+## 1. 分析 (Analysis) - 當前架構評估
+
+### 1.1 Phase 4～6 保留必要性與新規格映射
+目前工作樹的架構設計清晰地劃分了三個階段，應完整保留並依新規格映射：
+- **Phase 4 (隔離強化與安全基礎)**：應繼續保留。新規格要求收緊 Gateway 的輸入邊界（JSON-only、Content-Type 嚴格驗證、 canonical queue 限制），並在產品端（ChurchReport）實作 Session 級別的資源生命週期管理（Lease/Drain 機制）。目前此階段的程式碼實作已基本完成，但暴露出嚴重的執行期阻斷問題（見下文 Critical 說明）。
+- **Phase 5 (業務遷移與功能啟用)**：應保留。此階段的核心是將 `Package01FeeReadsEnabled` 設為 `true`，並將 ChurchReport 的奉獻查詢（Donation Fee Reads）正式切換至 Web API 路由。目前此階段尚未啟用，必須等待 Phase 4 的所有安全與隔離驗證完全通過。
+- **Phase 6 (舊 SDK 與 Data8 移除)**：應保留但延後。只有在 Phase 4 與 Phase 5 的所有 Gate（包含真實伺服器驗證、瀏覽器 E2E 測試、效能與資源基準測試）全部通過後，才能安全移除 `PowerPlatform.Dataverse.Client` 專案與 Data8 依賴。
+
+### 1.2 資源洩漏與共享狀態評估 (Session/Memory/Resource Leakage)
+- **Session 隔離與洩漏防護**：ChurchReport 已實作 `SessionScopedResourceDisposalCoordinator`，透過 256-bit 隨機不透明的 Scope ID 綁定 Session 與 `DonationPaymentManager`。登出（Logout）與重新登入（Re-login）時，會在 `Session.Clear()` 執行前先呼叫 `DrainSessionResourceScope`，確保舊世代的資源可見性被立即撤銷，且進行中的請求（In-flight leases）完成後會由唯一的 Cleanup Owner 進行確定性釋放。此設計有效防止了跨 Request/Session 的 Session Leakage。
+- **記憶體與 Socket 洩漏**：
+  - **已修復**：`GatewayHttpClientFactory` 靜態快取已被移除，改用 `IHttpClientFactory` 搭配 `SetHandlerLifetime(10min)` 與 `SocketsHttpHandler`，解決了舊 `HttpClient` 永不釋放導致的 Socket 耗盡與 DNS 變更失效問題。
+  - **潛在風險**：`DonationDynamicsAccessProcessHost` 雖然限制了單一設定世代的 `ServiceProvider` 快取，但在本機開發（Local Dev）模式下，若頻繁變更設定，舊的 `ServiceProvider` 必須確保被完全 Dispose。目前實作已在 `DisposeAsync` 中呼叫 `provider.DisposeAsync()`，但需注意在 `GetOrCreate` 拋出例外時的 Rollback 機制是否會遺留未釋放的資源。
+
+### 1.3 WinRM／DC／D365 VM 安全前置條件與自動化評估
+- **安全前置條件**：
+  - 遠端 VM（`D365DC01` 與 `D365APP01`）必須啟用 WinRM HTTPS 監聽器，禁止使用 HTTP 明文傳輸。
+  - 執行自動化指令碼的帳號必須具備目標 VM 的本機系統管理員權限，且必須使用 Windows 整合驗證（Negotiate/Kerberos），不得在指令碼或設定檔中硬編碼任何明文密碼。
+- **可自動化命令**：
+  - `Provision-DynamicsControlPlaneOnD365App.ps1` 透過排程工作（Scheduled Task）以 `SYSTEM` 或 `S4U` 權限執行 `sqlcmd` 來初始化控制台資料庫，此設計避免了互動式憑證殘留。
+  - `Invoke-DynamicsLiveSmoke.ps1` 可用於自動化執行 Web API 煙霧測試，但必須在具備存取權限的互動式 Windows 工作階段中執行。
+- **不可記錄資訊**：
+  - 嚴禁將 `DYNAMICS_JESUS_PROD_PASSWORD` 等環境變數或 ADFS Token 寫入任何 Log、主控台輸出或測試報告中。
+  - 415 與 413 錯誤路徑不得回顯任何呼叫端傳入的惡意 Body 內容或 Content-Type。
+
+---
+
+## 2. 架構決策 (Architecture Decision) - 關鍵設計選擇與理由
+
+### 2.1 Package01FeeReadsEnabled 保持 false
+- **決策**：`DynamicsAccess:Package01FeeReadsEnabled` 必須保持 `false`。
+- **理由**：目前 Local Gateway、ADFS 授權、瀏覽器 E2E 驗證以及真實環境的煙霧測試尚未完全通過。提前啟用會導致 ChurchReport 嘗試透過未驗證的 Web API 管道存取 Dynamics，造成系統崩潰或資料不一致。
+- **解鎖證據**：解鎖此 Flag 的唯一可接受證據為：
+  1. 實作並通過 Durable Cross-Host Coordinator（解決多 Replica 下的容量限制問題）。
+  2. 通過真實 ADFS/OAuth 憑證的 `Invoke-AdfsTokenProbe.ps1` 測試，取得有效 Token 並成功呼叫 `WhoAmI`。
+  3. 執行 Local Gateway + ChurchReport + 瀏覽器 E2E 整合測試，確認奉獻查詢頁面能正常載入且無資源洩漏。
+
+### 2.2 RequireDurableHostCoordinator 設計缺陷與修正
+- **決策**：修正 `Program.cs` 中對 `RequireDurableHostCoordinator` 的硬編碼限制。
+- **理由**：目前 Gateway 在非 "Testing" 環境下（如 Development/Production）會強制要求 `RequireDurableHostCoordinator = true`。然而，系統目前僅註冊了 `InMemoryRuntimeHostSlotCoordinator`（`IsDurable => false`）。這會導致 Gateway 在啟動或執行任何請求時，直接拋出未處理的 `InvalidOperationException`，導致服務回傳 500 錯誤。這是一個嚴重的 Release Blocker。
+- **修正方案**：在 Durable Coordinator 實作完成前，應允許在非生產環境（如 Development）下使用記憶體協調器，或提供優雅的降級機制，並在拋出租約失敗時回傳 503/500 的受控 JSON 回應，而非讓程序崩潰。
+
+### 2.3 Microsoft.AspNetCore.Authentication.Negotiate 安全漏洞
+- **決策**：升級 `Microsoft.AspNetCore.Authentication.Negotiate` 套件版本。
+- **理由**：目前專案使用的 `10.0.7` 版本存在已知的高嚴重性安全性漏洞（GHSA-2p3q-h3hg-jcqq, GHSA-8prm-248r-h957），這會破壞 Gateway Windows 驗證的安全邊界。
+- **修正方案**：將套件升級至安全版本（如 `10.0.10` 或更高），並在 CI 流程中加入 `dotnet list package --vulnerable` 檢查。
+
+---
+
+## 3. 實作計畫 (Implementation Plan) - 步驟與驗收矩陣
+
+### 3.1 下一個最小實作順序
+
+#### 步驟 1：修復 Gateway 執行期阻斷與套件漏洞
+- **精確檔案**：
+  - `SpeechMessage.Dynamics.Gateway/Program.cs`
+  - `SpeechMessage.Dynamics.Gateway/SpeechMessage.Dynamics.Gateway.csproj`
+- **實作內容**：
+  - 將 `RequireDurableHostCoordinator` 的啟用條件限制為僅在生產環境（Production）且已配置 SQL 控制台時啟用；在 Development 環境下允許使用記憶體協調器。
+  - 升級 `Microsoft.AspNetCore.Authentication.Negotiate` 至 `10.0.10`。
+- **測試**：執行 `dotnet test SpeechMessage.Dynamics.Tests` 確保所有邊界測試通過。
+- **Rollback**：使用 `git checkout` 還原專案檔與 `Program.cs`。
+
+#### 步驟 2：實作 Durable Cross-Host Coordinator (P4-B)
+- **精確檔案**：
+  - 新增 `SpeechMessage.Dynamics.WebApi/Capacity/SqlRuntimeHostSlotCoordinator.cs`
+  - 修改 `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
+- **實作內容**：
+  - 實作基於 SQL Server (`SpeechMessageDynamicsControlPlane`) 的租約協調器，使用 `SERIALIZABLE` 隔離級別與 `UPDLOCK, HOLDLOCK` 確保跨進程的原子化租約取得與更新。
+  - 實作租約過期隔離（Quarantine）與 Fencing Token 機制。
+- **測試**：編寫 `SqlRuntimeHostSlotCoordinatorTests` 進行並發租約爭搶測試。
+- **Rollback**：刪除新增的 SQL 協調器檔案，並將 DI 註冊還原為 InMemory 協調器。
+
+#### 步驟 3：執行 ADFS Token 探針與真實環境驗證
+- **精確檔案**：
+  - `docs/scripts/Invoke-AdfsTokenProbe.ps1`
+  - `docs/scripts/Invoke-DynamicsLiveSmoke.ps1`
+- **實作內容**：
+  - 在開發人員工作站上執行 ADFS Token 探針，驗證是否能成功取得 Bearer Token 並呼叫 `WhoAmI`。
+  - 執行 `Invoke-DynamicsLiveSmoke.ps1 -EnableLive` 驗證與真實 D365 伺服器的連線。
+- **測試**：確認 `adfs-token-probe-latest.json` 輸出 `ok = true`。
+- **Rollback**：清除環境變數與產生的 JSON 暫存檔。
+
+### 3.2 可執行驗收矩陣 (Acceptance Matrix)
+
+| 測試情境 | 執行步驟 | 預期結果 | 資源與安全基準 |
+| --- | --- | --- | --- |
+| **1. 服務啟動與健康檢查** | 啟動 Local Gateway，呼叫 `/ready` 端點。 | 回傳 200 OK，輸出包含已啟動的 Profile 列表與 Generation ID。 | 記憶體佔用穩定，無未釋放的 Socket。 |
+| **2. 授權與邊界驗證** | 使用未授權的 Windows 帳號呼叫 Gateway。 | 立即回傳 403 Forbidden，且不得讀取 Request Body。 | 證明授權檢查優先於 Body I/O。 |
+| **3. 媒體型別防護** | 傳送 `Content-Type: text/plain` 的請求。 | 立即回傳 415 Unsupported Media Type，不租用 ArrayPool。 | 證明非 JSON 請求在解析前被拒絕。 |
+| **4. 奉獻查詢 E2E** | 登入 ChurchReport，進入奉獻查詢頁面。 | 頁面正常載入，資料透過 Local Gateway 取得。 | 觀察 `ActiveEntryCount` 在查詢時增加，完成後歸零。 |
+| **5. 身份重設與排空** | 在 ChurchReport 執行登出（Logout）。 | Session 被清除，`SessionScopedResourceDisposalCoordinator` 成功排空資源。 | 驗證舊的 `DonationPaymentManager` 被確定性 Dispose。 |
+| **6. 服務停止** | 停止 ChurchReport 與 Gateway 服務。 | 所有的 Hosted Service 優雅退出，未完成的請求被取消。 | 確保無殘留的背景 Task 或未釋放的連線。 |
+
+---
+
+## 4. 考量事項 (Considerations)
+
+- **效能與記憶體治理**：Gateway 的 Body Reader 採用了 `ArrayPool<byte>.Shared` 進行緩衝區租用，並在 `finally` 區塊中執行 `CryptographicOperations.ZeroMemory` 清空緩衝區。此設計有效降低了 GC 壓力並防止敏感資訊殘留在記憶體中，必須在後續的實作中嚴格遵守。
+- **安全性邊界**：`DynamicsWebApiClient` 已移除了成功回應中的 `approvedWebApiRoot` 欄位，防止內部 CRM 拓撲外洩。後續實作上游 OData 回傳的 `@odata.context` 與 `@odata.nextLink` 時，必須在伺服器端進行解析或投影，絕對不得將含有內部主機名稱的絕對 URL 直接曝露給前端或產品端。
+- **可維護性與編碼規範**：所有新增與修改的程式碼必須撰寫詳盡的繁體中文註解，說明擁有權（Ownership）、生命週期（Lifecycle）與異常處理（Fail-closed）邏輯，並嚴格遵守 UTF-8 without BOM 與 CRLF 換行格式。
+
+---
+
+## 5. 實作檢查清單 (Implementation Checklist)
+
+- [ ] **修復 Release Blocker**：將 `Program.cs` 中的 `RequireDurableHostCoordinator` 調整為僅在生產環境強制啟用，避免開發環境崩潰。
+- [ ] **升級安全套件**：將 `Microsoft.AspNetCore.Authentication.Negotiate` 升級至 `10.0.10` 以上版本，修復已知漏洞。
+- [ ] **實作 SQL 租約協調器**：完成 `SqlRuntimeHostSlotCoordinator`，支援跨進程的 Fencing 與 Quarantine 機制。
+- [ ] **執行 ADFS 煙霧測試**：執行 `Invoke-AdfsTokenProbe.ps1` 與 `Invoke-DynamicsLiveSmoke.ps1`，取得真實連線綠燈。
+- [ ] **驗證 Session 生命週期**：確保 ChurchReport 登出與關機時，Donation 資源能被 100% 確定性排空與釋放。
+- [ ] **解鎖 Feature Flag**：在上述步驟皆通過且取得 E2E 綠燈後，將 `Package01FeeReadsEnabled` 設為 `true`。
+- [ ] **執行 Phase 6 移除**：在 Phase 5 穩定運行後，移除舊的 SDK 參考與 Data8 專案。
--- a/SpeechMessage.Dynamics.Gateway/Program.cs
+++ b/SpeechMessage.Dynamics.Gateway/Program.cs
@@ -115,2 +115,3 @@
-options.Admission.RequireDurableHostCoordinator =
-    !builder.Environment.IsEnvironment("Testing");
+// 修正架構缺陷：在非生產環境下允許使用記憶體協調器，避免開發與測試環境因缺少 SQL 控制台而直接崩潰 (500 錯誤)
+options.Admission.RequireDurableHostCoordinator = 
+    builder.Environment.IsProduction();
--- a/SpeechMessage.Dynamics.Gateway/SpeechMessage.Dynamics.Gateway.csproj
+++ b/SpeechMessage.Dynamics.Gateway/SpeechMessage.Dynamics.Gateway.csproj
@@ -12,3 +12,3 @@
   <ItemGroup>
-    <PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="10.0.7" />
+    <PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="10.0.10" />
   </ItemGroup>
```
