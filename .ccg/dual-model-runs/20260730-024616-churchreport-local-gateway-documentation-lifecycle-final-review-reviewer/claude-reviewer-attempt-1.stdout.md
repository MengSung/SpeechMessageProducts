PASS

## 審查結論總覽
本輪對 Session/Resource 生命週期核心程式碼、Development Gateway 設定、退役 AD FS 探測腳本、DI 掛載順序、測試涵蓋範圍與 SPEC/文件一致性進行了逐檔精讀（非僅信任摘要），並以 `head -c3` / CRLF 逐行檢查驗證編碼契約。未發現可信的跨 request/session/user/tenant 洩漏、use-after-dispose、無界 queue/cache/task、credential 洩漏、silent fallback 或 production target 暴露。結論為 **PASS**，但有 1 項 Warning 需持續追蹤（文件已正確記載，非本切片程式缺陷）與 2 項 Info 觀察。

---

## Critical 🔴
無。

---

## Warning 🟡

### 1. Development `WorkloadBindings` 陣列以 index 合併，base AppPool binding 未被清除
- **檔案**：`SpeechMessage.Dynamics.Gateway/appsettings.Development.json:18-33`（index `"1"`，`[LOCAL_WINDOWS_IDENTITY_REDACTED]` → `crm82`，僅 `runtime.health.whoami`）對照 `SpeechMessage.Dynamics.Gateway/appsettings.json:24-44`（index `"0"`，`IIS APPPOOL\ChurchReport` → `crm82`，含真實資料讀取 operation 全集）。
- **具體時序**：.NET `IConfiguration` 對 JSON 陣列以數字 index 合併；Development 新增的是 index `1` 而非取代 index `0`，因此 Development host 啟動後，`DynamicsGateway:WorkloadBindings` 實際同時持有 base 與 Development 兩筆 binding。
- **驗證結果**：本機不存在 `IIS APPPOOL\ChurchReport` 這個 Windows identity，故現況不可利用；且此議題已在 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md:400-405,675,697` 明確記載為「Development workload binding hardening Warning」，並列為後續 hardening 待辦（不得只把新 entry 從 `1` 改成 `0` 就視為解決）。
- **判定**：此為**文件已正確保留的 open gate**，不是本切片新引入的程式缺陷；仍列為 Warning 以確保未來部署到具備該 AppPool identity 的環境前必須先關閉此 gap。
- **建議修正方向**：Development 設定改用同一 index（`0`）覆蓋 base binding，或提供顯式「移除/中和繼承 index」機制，而非僅新增更高 index。

---

## Info 🔵

### 1. `InMemoryDataContextSmallGroup.cs` 其餘 session-cached manager 未走 coordinator 生命週期
- **檔案**：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`（例如 `ListManager` 566-604、`SmallGroupDataList` 650-689、`FeeList` 1032-1071 等多個屬性）。
- **觀察**：這些屬性仍使用舊有 `if (_memoryCache.Get(key) == null) { create; set; }` 模式：(a) 非原子的 TOCTOU 檢查，同一 Session 併發請求可能重複建立/覆寫實例；(b) `PostEvictionCallbackRegistration` 未傳入 `State`，導致 callback 內 `if (state != null)` 恆為 false，這些物件在 cache 逐出時**不會**被 Dispose；(c) `DrainSessionResourceScope`（logout/re-login）只撤銷 `DonationPaymentManager` 的 generation，不涉及這些其他 manager。
- **範圍判定**：本次 session lifecycle 契約（項目 1-10）僅要求 `DonationPaymentManager` 具唯一 owner、ref-counted lease 與確定性 drain，該部分已正確以 coordinator 實作（1198-1236 行）並通過測試。其餘 manager 屬於既有舊模式、非本次變更範圍，故列為 Info 而非 Warning/Critical；但若這些型別未來持有 `IDisposable` 資源（HTTP client、DB handle 等），將是獨立的資源洩漏風險，建議後續排入 backlog。

### 2. 部分 Debug/Trace 輸出包含帳號等識別資訊（非 Credential/Token/SessionID）
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs:39`（`[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}`）。
- **觀察**：僅記錄帳號字串（非密碼、Token、Session ID、Credential），不違反本次審查明確列出的敏感資料紅線，但建議日後統一收斂到結構化 logger 並評估帳號是否需要遮罩，屬觀察而非阻擋項。

---

## 契約驗證結果

**Session/Resource 契約（1-10）**：`SessionScopedResourceDisposalCoordinator.cs` 逐一比對程式碼與 `ChurchReport.MemberInfo.Tests/SessionLifecycle/*` 測試名稱（如 `Missing_slot_drain_does_not_remove_generation_published_after_linearization_point`、`Stale_cache_entry_retries_on_registered_slot_instead_of_publishing_orphan_generation`、`Cleanup_failure_remains_owned_until_later_host_drain_retry_succeeds`、`Host_stop_during_factory_retains_failed_prepublication_cleanup_for_retry`、`Identity_reset_waits_for_scope_bound_acquire_publication_before_draining`），確認鎖序（stripe lock → slot lock → entry lock）一致、無反向鎖序死鎖路徑，且各狀態機轉換（Live→Draining→CleanupInProgress→Disposed/CleanupFailed）均如契約所述。`DonationPaymentManager.Dispose()`/`DonationFeePaymentProcessor.Dispose()` 均僅釋放自建 LINE client 與 semaphore，未越權 Dispose Factory/DI 資源，並以 `Interlocked.Exchange` 保證併發冪等。Logout/re-login 均在 `Session.Clear()` 前呼叫 `DrainSessionResourceScope`，失敗時例外向上傳遞、不清 Session（fail closed）。**判定：全數符合。**

**Development Gateway 契約**：`DynamicsGatewayReadinessService.cs` 確認 `VerifySchemaAsync` 僅驗證 schema、不隱性建表；Development `appsettings.Development.json` 之 `OrganizationBaseUri`/`OrganizationWebApiBaseUri` 為 `.invalid` 不可路由位址；`DonationDynamicsAccessBootstrap`/`DynamicsGatewayPreflightHostedService` 在 `Package01FeeReadsEnabled=false` 時嚴格 no-op（不解析 executor、不建 HTTP pool）；`Invoke-AdfsTokenProbe.ps1` 已確認為固定 `throw`、無參數、無 I/O、無背景資源的退役入口。**判定：全數符合，唯 Warning #1 所述 workload-binding 繼承議題待後續 hardening。**

**文件與編碼契約**：所有列於審查清單的 16 個檔案均為 UTF-8 without BOM、每行 CRLF、末行 CRLF（以 `od`/`wc -l` 逐檔核對，無例外）。核心變更檔案（coordinator、bootstrap、preflight、controller、appsettings、SPEC）均具備深入繁體中文說明信任邊界/race/fail-closed/drain 的註解，非僅語法翻譯。

**Package01 與保留元件確認**：`SpeechMessageProducts.ChurchReport/appsettings.json:559` 與 `appsettings.Development.json:6` 均為 `Package01FeeReadsEnabled: false`；`PowerPlatform.Dataverse.Client.csproj` 仍存在並被 `SpeechMessageProducts.ChurchReport.csproj`/`ToolUtility.csproj` 參照，Data8 相依仍在同一專案內，Embedded 執行模式程式路徑（`CreateEmbeddedExecutor`）仍保留但預設走 `Gateway`。**判定：全數符合。**

**文件與證據一致性**：`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md:1146` 明確區分「Local Gateway／ChurchReport Development fail-closed 邊界與 deterministic lifecycle 已通過」與「仍待完成：CE 8.2/9.1 真實 WhoAmI、Authentication/Operation Matrix、rollback、OData annotation 安全投影、跨 Process 容量、Fault/Soak/Performance、Phase 5、Phase 6」，與 SPEC 及本次程式現況一致，未見誇大或矛盾宣稱。

---

## 仍阻擋的 Gate（文件已正確保留，非本切片缺陷）
- 真實 CE 8.2/9.1 WhoAmI、Authentication/Operation Matrix 驗證。
- OData `@odata.context`/`@odata.nextLink` 絕對 URL 的伺服器端安全投影。
- 跨 process aggregate capacity、durable coordinator outage、Fault/Soak/Performance。
- Phase 5：僅能先遷移單一可回滾 ChurchReport workflow。
- Phase 6：Data8、Embedded、`PowerPlatform.Dataverse.Client`、舊 SDK 移除 Gate（目前均正確保留、未被移除）。
- Development `WorkloadBindings` index 繼承 hardening（Warning #1）。

---


