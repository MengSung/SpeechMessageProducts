FAIL

## 審查結果分組

### Critical
* **受影響檔案**：
  * `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`
  * `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
  * `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
  * `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  * `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
  * `SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs`
  * `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`
  * `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
  * `SpeechMessageProducts.ChurchReport/Startup.cs`
  * `ChurchReport.MemberInfo.Tests/SessionLifecycle/AuthenticationSessionResourceDrainTests.cs`
  * `ChurchReport.MemberInfo.Tests/SessionLifecycle/DonationOwnedResourceLifecycleTests.cs`
  * `ChurchReport.MemberInfo.Tests/SessionLifecycle/SessionScopedResourceDisposalCoordinatorTests.cs`
* **具體失敗時序與問題**：
  上述所有檔案中的繁體中文註解均出現嚴重的亂碼（例如 `// AI-蝜?銝剜?瑼?閮餉圾`）。這違反了 `.trellis/spec/backend/quality-guidelines.md` 中「所有 scoped source／test／config／script／SPEC／Markdown 必須為 UTF-8 without BOM」的編碼契約。亂碼導致開發與審查人員無法閱讀關於信任邊界、競爭條件、fail-closed、取消／逾時、rollback／drain／dispose／cleanup 的詳細說明，此為 Release Blocker。
* **最小修正方向**：
  將上述受影響的檔案重新轉換為標準的 UTF-8 without BOM 編碼，並修復或還原損壞的繁體中文註解，確保在所有編輯器與編譯環境中均能正確顯示。

### Warning
* **受影響檔案**：`SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
* **具體失敗時序與問題**：
  .NET Configuration 陣列依 index 合併時，繼承的 workload-binding 可能會產生非預期的合併行為。雖然目前已配置 Development entry，但應記錄此 inherited workload-binding Warning，避免誤宣稱 Development entry 已嚴格取代 base binding。
* **最小修正方向**：
  在說明文件或設定檔註解中明確記錄此合併警告，並在部署腳本中加入驗證。

### Info
* **受影響檔案**：`docs/scripts/Invoke-AdfsTokenProbe.ps1`
* **具體失敗時序與問題**：
  該腳本已正確標記為 RETIRED，並在執行時直接拋出異常，符合安全契約，無須進一步修改。

---

## 關鍵契約驗證判斷

### 1. Session／Memory／Socket／Timer／Task／Handler／Semaphore／Cache／Connection／Cancellation Registration 洩漏與 Owner 失去路徑判斷
* **判斷結果**：**無洩漏路徑**。
* **理據**：
  * `SessionScopedResourceDisposalCoordinator` 採用了嚴格的 ref-count 租約與 stripe lock 機制，並在 `Logout` 與 `re-login` 時於 `Session.Clear()` 前進行確定性的 drain。
  * `DonationPaymentManager` 與 `DonationFeePaymentProcessor` 的 `Dispose` 僅釋放其自建的 `LineMessagingClient` 與 `SemaphoreSlim`，未越權釋放 DI 容器擁有的生命週期物件。
  * 在 `Package01FeeReadsEnabled=false` 的情況下，不會建立任何背景 timer、HttpClient pool 或 preflight 流量，因此在目前 Development 配置下不存在上述資源洩漏或失去 owner 的路徑。

### 2. 說明書／SPEC 與目前程式、設定及驗證證據一致性判斷
* **判斷結果**：**一致**。
* **理據**：
  * `.trellis/spec/backend/quality-guidelines.md` 與 `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 正確描述了 Local Gateway 與 Central Gateway 的邊界，且程式碼中的 `Package01FeeReadsEnabled=false` 設定與說明書完全吻合。

### 3. 阻擋真實 CE 8.2／9.1、Phase 5 與 Phase 6 的 Gate 清單
* **Phase 5 Gate**：跨 process capacity、真實 CE 8.2/9.1 OData annotation 投影、以及高負載 soak/performance 測試尚未執行。
* **Phase 6 Gate**：Data8 專案的完全移除與 `PowerPlatform.Dataverse.Client` 專案的 checked-in 驗證尚未完成。

### 4. 關鍵配置與專案保留確認
* **Package01FeeReadsEnabled**：確認已設為 `false`。
* **Embedded、Data8、PowerPlatform.Dataverse.Client**：確認均保留在工作樹中，未被移除。
