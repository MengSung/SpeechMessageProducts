# VALIDATION REPORT
=================
Developer Experience (API Usability): 19/20 - 變更提供了清晰的 `IOrganizationAdmissionManager` 與 `IRuntimeHostSlotCoordinator` 介面，並有完整的單元測試佐證，開發者體驗良好。唯一微小的缺口是 `InMemoryRuntimeHostSlotCoordinator` 的單機限制需要更顯著的警示。
Code Consistency: 19/20 - 程式碼結構與現有的無 SDK 閘道設計模式高度一致，命名規範與異常處理符合專案標準。但 `appsettings.json` 中仍有其他第三方金鑰未套用相同的 Secrets 參照標準。
Security & Isolation (Backend a11y): 20/20 - 嚴格落實了安全邊界。ADFS 與 Dynamics HTTP 處理程序停用了 cookies、重新導向與代理；ADFS token 限制在 32 KiB 且在釋放時清除 buffer，失敗時不 echo 回應主體，有效防止 token 洩漏。
Performance: 20/20 - 准入管理器（Admission Manager）在超時、取消與異常時能原子化地釋放所有預約，防止了 semaphore 與記憶體洩漏。單元測試中也驗證了併發 burst 下的效能與容量限制。
Protocol & Transport Compatibility: 20/20 - 傳輸層配置正確對齊了 Dynamics 365 CE 9.1 IFD 的 Web API 要求，且 `Package01FeeReadsEnabled` 保持為 `false`，確保舊有 SOAP 路徑的相容性不受影響。

TOTAL SCORE: 98/100

RECOMMENDATION: PASS

---

# Dynamics Phase 4 Final Isolation Hardening 審查報告

## 1. 總體評估 (Summary)
本審查針對 Dynamics Phase 4 最終隔離強化（final isolation hardening）的未提交變更進行唯讀事實審查。變更集嚴格遵循了安全邊界約束，核心的准入控制、租約協調器、ADFS Token 提供者以及 HTTP 傳輸層皆進行了嚴格的資源與安全強化。單元測試與 DI 整合測試非常完整，能有效預防 race conditions、記憶體洩漏與 token 洩漏。

**判定結果：PASS**

---

## 2. 關鍵審查發現 (Findings)

### Critical (嚴重缺陷)
* **無**：所有變更皆符合安全邊界與 Phase 4 隔離強化的要求，未發現 session/profile/token 洩漏或 race conditions。

### Warning (警告)
1. **`InMemoryRuntimeHostSlotCoordinator` 的單機限制**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs` (第 25, 59-66 行)
   * **原因說明**：此協調器的 `IsDurable` 屬性恆為 `false`，且其租約管理完全基於記憶體內的 `ConcurrentDictionary`。在多機/高可用性（HA）部署環境下，無法跨主機同步併發限制，可能導致 Dynamics 端觸發服務保護限制。雖然程式碼註解中已有明確警告，但在進入生產環境部署前，必須確保已實作並切換至持久化（Durable）的協調器（如 Redis 實作）。
2. **`appsettings.json` 中的其他明文金鑰一致性缺口**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 540-542 行等其他區段)
   * **原因說明**：雖然 Dynamics 365 的連線密碼已正確移出並改用 User Secrets 參照，但同一檔案中仍有其他第三方金鑰（如 LINE Channel Access Token、LinePay/Sinopac/MyPay/TSPG 金鑰）以明文形式存在。這與專案自訂的「零容忍明文金鑰」標準存在一致性缺口，建議後續比照辦理。

### Info (提示)
1. **`Package01FeeReadsEnabled` 旗標安全隔離**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 559 行)
   * **原因說明**：功能旗標 `Package01FeeReadsEnabled` 確實保持為預設值 `false`。這確保了在所有 live 驗證閘門通過前，不會有實際的產品流量切換至新路由，符合安全邊界約束。
2. **ADFS Token 取得的安全性與記憶體防護**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 37, 139-144, 392-425 行)
   * **原因說明**：`AdfsOAuthTokenProvider` 實作了嚴格的安全防護：
     - 限制 token 回應大小最大為 32 KiB (`MaxTokenResponseBytes = 32 * 1024`)。
     - 使用 `ArrayPool<byte>` 租用 buffer，並在 `finally` 區塊中呼叫 `Return(buffer, clearArray: true)` 確保記憶體被清除，防止 token 殘留。
     - 在 HTTP 請求失敗時，拋出的異常中不包含回應主體（do not echo failure bodies），防止敏感資訊外洩。
     - 每個短壽命的 HttpClient 實例皆在 `finally` 區塊中被明確 dispose。
3. **HTTP Handlers 的安全配置**
   - **檔案路徑**：
     - `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs` (第 80-89 行)
     - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs` (第 92-103 行)
   - **原因說明**：所有 HTTP 處理程序皆已停用 cookies (`UseCookies = false`)、自動重新導向 (`AllowAutoRedirect = false`)、代理伺服器 (`UseProxy = false`)、自動解壓縮 (`AutomaticDecompression = DecompressionMethods.None`) 與預先驗證 (`PreAuthenticate = false`)，有效防止了 session 劫持與不必要的網路暴露。
4. **准入管理器的原子化釋放與防洩漏設計**
   * **檔案路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 185-241, 365-398 行)
   * **原因說明**：在 `AcquireAsync` 中，管理器使用 `_totalAdmission` (SemaphoreSlim) 原子化地限制了 in-flight + queued 的總工作量。在發生超時（timeout）、取消（cancellation）或異常（exception）時，皆會進入 `ReleaseReservation` 釋放所有已預約的資源，確保不會發生 semaphore 或 slot 洩漏。
5. **測試有效性驗證**
   * **檔案路徑**：
     - `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`
     - `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`
     - `SpeechMessage.Dynamics.Tests/WebApiServiceCollectionExtensionsTests.cs`
   * **原因說明**：新增與修改的單元測試非常完整，涵蓋了併發 burst、超時釋放、fencing token 驗證、32 KiB 限制、HTTP handler 安全配置等關鍵安全與效能場景，且皆已通過，驗證效果顯著。

---

## 3. 建議與後續行動 (Suggestions)
1. **實作 Durable 協調器**：針對多機部署環境，應優先設計並實作基於 Redis 或資料庫的 `IRuntimeHostSlotCoordinator`，以取代目前的 `InMemoryRuntimeHostSlotCoordinator`。
2. **清理 appsettings.json 中的明文金鑰**：建議將 `appsettings.json` 中殘留的 LINE 與金流明文金鑰移至 User Secrets 或環境變數中，以達到全專案金鑰管理的一致性。
3. **更新追蹤文件**：修正 `.ccg/tasks/dynamics-connection-compatibility/` 下的 `task.json` 與 `review.md`，使其反映目前分支與 Phase 3/4 的真實進度，避免狀態漂移。
